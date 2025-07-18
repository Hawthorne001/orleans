#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.GrainReferences;
using Orleans.Internal;
using Orleans.Runtime.Messaging;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime
{
    /// <summary>
    /// A client which is hosted within a silo.
    /// </summary>
    internal sealed partial class HostedClient : IGrainContext, IGrainExtensionBinder, IDisposable, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly object lockObj = new object();
        private readonly Channel<Message> incomingMessages;
        private readonly IGrainReferenceRuntime grainReferenceRuntime;
        private readonly InvokableObjectManager invokableObjects;
        private readonly InsideRuntimeClient runtimeClient;
        private readonly ILogger logger;
        private readonly IInternalGrainFactory grainFactory;
        private readonly MessageCenter siloMessageCenter;
        private readonly MessagingTrace messagingTrace;
        private readonly ConcurrentDictionary<Type, (object Implementation, IAddressable Reference)> _extensions = new ConcurrentDictionary<Type, (object, IAddressable)>();
        private readonly ConcurrentDictionary<Type, object> _components = new();
        private readonly IServiceScope _serviceProviderScope;
        private bool disposing;
        private Task? messagePump;

        public HostedClient(
            InsideRuntimeClient runtimeClient,
            ILocalSiloDetails siloDetails,
            ILogger<HostedClient> logger,
            IGrainReferenceRuntime grainReferenceRuntime,
            IInternalGrainFactory grainFactory,
            MessageCenter messageCenter,
            MessagingTrace messagingTrace,
            DeepCopier deepCopier,
            GrainReferenceActivator referenceActivator,
            InterfaceToImplementationMappingCache interfaceToImplementationMappingCache)
        {
            this.incomingMessages = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

            this.runtimeClient = runtimeClient;
            this.grainReferenceRuntime = grainReferenceRuntime;
            this.grainFactory = grainFactory;
            this.invokableObjects = new InvokableObjectManager(
                this,
                runtimeClient,
                deepCopier,
                messagingTrace,
                runtimeClient.ServiceProvider.GetRequiredService<DeepCopier<Response>>(),
                interfaceToImplementationMappingCache,
                logger);
            this.siloMessageCenter = messageCenter;
            this.messagingTrace = messagingTrace;
            this.logger = logger;

            this.ClientId = CreateHostedClientGrainId(siloDetails.SiloAddress);
            this.Address = Gateway.GetClientActivationAddress(this.ClientId.GrainId, siloDetails.SiloAddress);
            this.GrainReference = referenceActivator.CreateReference(this.ClientId.GrainId, default);
            _serviceProviderScope = runtimeClient.ServiceProvider.CreateScope();
        }

        public static ClientGrainId CreateHostedClientGrainId(SiloAddress siloAddress) => ClientGrainId.Create($"hosted-{siloAddress.ToParsableString()}");

        /// <inheritdoc />
        public ClientGrainId ClientId { get; }

        public GrainReference GrainReference { get; }

        public GrainId GrainId => this.ClientId.GrainId;

        public object? GrainInstance => null;

        public ActivationId ActivationId => this.Address.ActivationId;

        public GrainAddress Address { get; }

        public IServiceProvider ActivationServices => _serviceProviderScope.ServiceProvider;

        public IGrainLifecycle ObservableLifecycle => throw new NotImplementedException();

        public IWorkItemScheduler Scheduler => throw new NotImplementedException();

        public bool IsExemptFromCollection => true;

        /// <inheritdoc />
        public override string ToString() => $"{nameof(HostedClient)}_{this.Address}";

        /// <inheritdoc />
        public IAddressable CreateObjectReference(IAddressable obj)
        {
            if (obj is GrainReference) throw new ArgumentException("Argument obj is already a grain reference.");

            var observerId = ObserverGrainId.Create(this.ClientId);
            var grainReference = this.grainFactory.GetGrain(observerId.GrainId);
            if (!this.invokableObjects.TryRegister(obj, observerId))
            {
                throw new ArgumentException(
                    string.Format("Failed to add new observer {0} to localObjects collection.", grainReference),
                    nameof(grainReference));
            }

            return grainReference;
        }

        /// <inheritdoc />
        public void DeleteObjectReference(IAddressable obj)
        {
            if (obj is not GrainReference reference)
            {
                throw new ArgumentException("Argument reference is not a grain reference.");
            }

            if (!ObserverGrainId.TryParse(reference.GrainId, out var observerId))
            {
                throw new ArgumentException($"Reference {reference.GrainId} is not an observer reference");
            }

            if (!invokableObjects.TryDeregister(observerId))
            {
                throw new ArgumentException("Reference is not associated with a local object.", "reference");
            }
        }

        public TComponent? GetComponent<TComponent>() where TComponent : class
        {
            if (this is TComponent component) return component;
            if (_components.TryGetValue(typeof(TComponent), out var result))
            {
                return (TComponent)result;
            }
            else if (typeof(TComponent) == typeof(PlacementStrategy))
            {
                return (TComponent)(object)ClientObserversPlacement.Instance;
            }

            lock (lockObj)
            {
                if (ActivationServices.GetService<TComponent>() is { } activatedComponent)
                {
                    return (TComponent)_components.GetOrAdd(typeof(TComponent), activatedComponent);
                }
            }

            return default;
        }

        public void SetComponent<TComponent>(TComponent? instance) where TComponent : class
        {
            if (this is TComponent)
            {
                throw new ArgumentException("Cannot override a component which is implemented by the client context");
            }

            lock (lockObj)
            {
                if (instance == null)
                {
                    _components.Remove(typeof(TComponent), out _);
                    return;
                }

                _components[typeof(TComponent)] = instance;
            }
        }

        /// <inheritdoc />
        public bool TryDispatchToClient(Message message)
        {
            if (!ClientGrainId.TryParse(message.TargetGrain, out var targetClient) || !this.ClientId.Equals(targetClient))
            {
                return false;
            }

            if (message.IsExpired)
            {
                this.messagingTrace.OnDropExpiredMessage(message, MessagingInstruments.Phase.Receive);
                return true;
            }

            this.ReceiveMessage(message);
            return true;
        }

        public void ReceiveMessage(object message)
        {
            var msg = (Message)message;

            if (msg.Direction == Message.Directions.Response)
            {
                // Requests are made through the runtime client, so deliver responses to the rutnime client so that the request callback can be executed.
                this.runtimeClient.ReceiveResponse(msg);
            }
            else
            {
                // Requests against client objects are scheduled for execution on the client.
                this.incomingMessages.Writer.TryWrite(msg);
            }
        }

        /// <inheritdoc />
        void IDisposable.Dispose()
        {
            if (this.disposing) return;
            this.disposing = true;
            _serviceProviderScope.Dispose();
            Utils.SafeExecute(() => this.siloMessageCenter.SetHostedClient(null));
            Utils.SafeExecute(() => this.incomingMessages.Writer.TryComplete());
            Utils.SafeExecute(() => this.messagePump?.GetAwaiter().GetResult());
        }

        private void Start()
        {
            this.messagePump = Task.Run(this.RunClientMessagePump);
        }

        private async Task RunClientMessagePump()
        {
            var reader = this.incomingMessages.Reader;
            while (true)
            {
                try
                {
                    var more = await reader.WaitToReadAsync();
                    if (!more)
                    {
                        LogDebugShuttingDown(this.logger);
                        break;
                    }

                    while (reader.TryRead(out var message))
                    {
                        if (message == null) continue;
                        switch (message.Direction)
                        {
                            case Message.Directions.OneWay:
                            case Message.Directions.Request:
                                this.invokableObjects.Dispatch(message);
                                break;
                            default:
                                LogErrorUnsupportedMessage(this.logger, message);
                                break;
                        }
                    }
                }
                catch (Exception exception)
                {
                    LogErrorMessagePumpException(this.logger, exception);
                }
            }
        }

        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe("HostedClient", ServiceLifecycleStage.RuntimeGrainServices, OnStart, OnStop);

            Task OnStart(CancellationToken cancellation)
            {
                if (cancellation.IsCancellationRequested) return Task.CompletedTask;

                // Register with the message center so that we can receive messages.
                this.siloMessageCenter.SetHostedClient(this);

                // Start pumping messages.
                this.Start();
                return Task.CompletedTask;
            }

            async Task OnStop(CancellationToken cancellation)
            {
                this.incomingMessages.Writer.TryComplete();

                if (this.messagePump != null)
                {
                    await messagePump.WaitAsync(cancellation).SuppressThrowing();
                }
            }
        }

        public bool Equals(IGrainContext? other) => ReferenceEquals(this, other);

        public (TExtension, TExtensionInterface) GetOrSetExtension<TExtension, TExtensionInterface>(Func<TExtension> newExtensionFunc)
            where TExtension : class, TExtensionInterface
            where TExtensionInterface : class, IGrainExtension
        {
            (TExtension, TExtensionInterface) result;
            if (this.TryGetExtension(out result))
            {
                return result;
            }

            lock (this.lockObj)
            {
                if (this.TryGetExtension(out result))
                {
                    return result;
                }

                var implementation = newExtensionFunc();
                var reference = this.grainFactory.CreateObjectReference<TExtensionInterface>(implementation);
                _extensions[typeof(TExtensionInterface)] = (implementation, reference);
                result = (implementation, reference);
                return result;
            }
        }

        private bool TryGetExtension<TExtension, TExtensionInterface>(out (TExtension, TExtensionInterface) result)
            where TExtension : class, TExtensionInterface
            where TExtensionInterface : class, IGrainExtension
        {
            if (_extensions.TryGetValue(typeof(TExtensionInterface), out var existing))
            {
                if (existing.Implementation is TExtension typedResult)
                {
                    result = (typedResult, existing.Reference.AsReference<TExtensionInterface>());
                    return true;
                }

                throw new InvalidCastException($"Cannot cast existing extension of type {existing.Implementation} to target type {typeof(TExtension)}");
            }

            result = default;
            return false;
        }

        private bool TryGetExtension<TExtensionInterface>([NotNullWhen(true)] out TExtensionInterface? result)
            where TExtensionInterface : IGrainExtension
        {
            if (_extensions.TryGetValue(typeof(TExtensionInterface), out var existing))
            {
                result = (TExtensionInterface)existing.Implementation;
                return true;
            }

            result = default;
            return false;
        }

        public TExtensionInterface GetExtension<TExtensionInterface>()
            where TExtensionInterface : class, IGrainExtension
        {
            if (this.TryGetExtension<TExtensionInterface>(out var result))
            {
                return result;
            }

            lock (this.lockObj)
            {
                if (this.TryGetExtension(out result))
                {
                    return result;
                }

                var implementation = this.ActivationServices.GetKeyedService<IGrainExtension>(typeof(TExtensionInterface));
                if (implementation is null)
                {
                    throw new GrainExtensionNotInstalledException($"No extension of type {typeof(TExtensionInterface)} is installed on this instance and no implementations are registered for automated install");
                }

                var reference = this.GrainReference.Cast<TExtensionInterface>();
                _extensions[typeof(TExtensionInterface)] = (implementation, reference);
                result = (TExtensionInterface)implementation;
                return result;
            }
        }

        public TTarget GetTarget<TTarget>() where TTarget : class => throw new NotImplementedException();
        public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken) { }
        public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken) { }
        public Task Deactivated => Task.CompletedTask;

        public void Rehydrate(IRehydrationContext context)
        {
            // Migration is not supported, but we need to dispose of the context if it's provided
            (context as IDisposable)?.Dispose();
        }

        public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken)
        {
            // Migration is not supported. Do nothing: the contract is that this method attempts migration, but does not guarantee it will occur.
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(Runtime.HostedClient)} completed processing all messages. Shutting down.")]
        private static partial void LogDebugShuttingDown(ILogger logger);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100327,
            Level = LogLevel.Error,
            Message = "Message not supported: {Message}")]
        private static partial void LogErrorUnsupportedMessage(ILogger logger, Message message);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100326,
            Level = LogLevel.Error,
            Message = "RunClientMessagePump has thrown an exception. Continuing.")]
        private static partial void LogErrorMessagePumpException(ILogger logger, Exception exception);
    }
}
