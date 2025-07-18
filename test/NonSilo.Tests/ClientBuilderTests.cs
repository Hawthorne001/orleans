using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Messaging;
using Orleans.Runtime;
using Xunit;

namespace NonSilo.Tests
{
    /// <summary>
    /// A no-op implementation of IGatewayListProvider used for testing client configuration
    /// without requiring actual gateway connectivity.
    /// </summary>
    public class NoOpGatewaylistProvider : IGatewayListProvider
    {
        public TimeSpan MaxStaleness => throw new NotImplementedException();

        public bool IsUpdatable => throw new NotImplementedException();

        public Task<IList<Uri>> GetGateways()
        {
            throw new NotImplementedException();
        }

        public Task InitializeGatewayListProvider()
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Tests for the Orleans ClientBuilder, which is responsible for configuring and building Orleans client instances.
    /// These tests verify client configuration validation, service registration, and proper initialization of client components
    /// without requiring a full Orleans cluster.
    /// </summary>
    [TestCategory("BVT")]
    [TestCategory("ClientBuilder")]
    public class ClientBuilderTests
    {
        /// <summary>
        /// Tests that a client cannot be created without specifying a ClusterId and a ServiceId.
        /// </summary>
        [Fact]
        public void ClientBuilder_ClusterOptionsTest()
        {
            Assert.Throws<OrleansConfigurationException>(() =>
            {
                var host = new HostBuilder()
                    .UseOrleansClient((ctx, clientBuilder) =>
                    {
                        clientBuilder.Configure<ClusterOptions>(options =>
                        {
                            options.ClusterId = null;
                            options.ServiceId = null;
                        });

                        clientBuilder.ConfigureServices(services =>
                            services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                    })
                    .Build();

                _ = host.Services.GetRequiredService<IClusterClient>();
            });

            Assert.Throws<OrleansConfigurationException>(() =>
            {
                var host = new HostBuilder()
                    .UseOrleansClient((ctx, clientBuilder) =>
                    {
                        clientBuilder.Configure<ClusterOptions>(options =>
                        {
                            options.ClusterId = "someClusterId";
                            options.ServiceId = null;
                        });

                        clientBuilder.ConfigureServices(services =>
                            services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                    })
                    .Build();

                _ = host.Services.GetRequiredService<IClusterClient>();
            });

            Assert.Throws<OrleansConfigurationException>(() =>
            {
                var host = new HostBuilder()
                    .UseOrleansClient((ctx, clientBuilder) =>
                    {
                        clientBuilder.Configure<ClusterOptions>(options =>
                        {
                            options.ClusterId = null;
                            options.ServiceId = "someServiceId";
                        });

                        clientBuilder.ConfigureServices(services =>
                            services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                    })
                    .Build();

                _ = host.Services.GetRequiredService<IClusterClient>();
            });

            var host = new HostBuilder()
                .UseOrleansClient((ctx, clientBuilder) =>
                {
                    clientBuilder.Configure<ClusterOptions>(options =>
                    {
                        options.ClusterId = "someClusterId";
                        options.ServiceId = "someServiceId";
                    });

                    clientBuilder.ConfigureServices(services => services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                })
                .Build();

            var client = host.Services.GetRequiredService<IClusterClient>();
            Assert.NotNull(client);
        }

        /// <summary>
        /// Tests that a client can be created without specifying configuration.
        /// </summary>
        [Fact]
        public void ClientBuilder_NoSpecifiedConfigurationTest()
        {
            var hostBuilder = new HostBuilder()
                .UseOrleansClient((ctx, clientBuilder) =>
                {
                    clientBuilder.ConfigureServices(services => services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                })
                .ConfigureServices(RemoveConfigValidators);

            var host = hostBuilder.Build();

            var client = host.Services.GetRequiredService<IClusterClient>();
            Assert.NotNull(client);
        }

        /// <summary>
        /// Verifies that the client throws an exception during startup if no grain interfaces are registered.
        /// This ensures that clients have at least one grain interface to communicate with.
        /// </summary>
        [Fact]
        public void ClientBuilder_ThrowsDuringStartupIfNoGrainInterfacesAdded()
        {
            // Add only an assembly with generated serializers but no grain interfaces
            var hostBuilder = new HostBuilder()
                .UseOrleansClient((ctx, clientBuilder) =>
                {
                    clientBuilder
                        .UseLocalhostClustering()
                        .Configure<GrainTypeOptions>(options =>
                        {
                            options.Interfaces.Clear();
                        });
                })
                .ConfigureServices(services => services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());

            var host = hostBuilder.Build();

            Assert.Throws<OrleansConfigurationException>(() => _ = host.Services.GetRequiredService<IClusterClient>());
        }

        /// <summary>
        /// Tests that the <see cref="IClientBuilder.ConfigureServices"/> delegate works as expected.
        /// </summary>
        [Fact]
        public void ClientBuilder_ServiceProviderTest()
        {
            var hostBuilder = new HostBuilder()
                .UseOrleansClient((ctx, clientBuilder) =>
                {
                    clientBuilder.ConfigureServices(services => services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                })
                .ConfigureServices(RemoveConfigValidators);

            Assert.Throws<ArgumentNullException>(() => hostBuilder.ConfigureServices(null));

            var registeredFirst = new int[1];

            var one = new MyService { Id = 1 };
            hostBuilder.ConfigureServices(
                services =>
                {
                    Interlocked.CompareExchange(ref registeredFirst[0], 1, 0);
                    services.AddSingleton(one);
                });

            var two = new MyService { Id = 2 };
            hostBuilder.ConfigureServices(
                services =>
                {
                    Interlocked.CompareExchange(ref registeredFirst[0], 2, 0);
                    services.AddSingleton(two);
                });

            var host = hostBuilder.Build();

            var client = host.Services.GetRequiredService<IClusterClient>();
            var services = client.ServiceProvider.GetServices<MyService>()?.ToList();
            Assert.NotNull(services);

            // Both services should be registered.
            Assert.Equal(2, services.Count);
            Assert.NotNull(services.Find(svc => svc.Id == 1));
            Assert.NotNull(services.Find(svc => svc.Id == 2));

            // Service 1 should have been registered first - the pipeline order should be preserved.
            Assert.Equal(1, registeredFirst[0]);

            // The last registered service should be provided by default.
            Assert.Equal(2, client.ServiceProvider.GetRequiredService<MyService>().Id);
        }

        /// <summary>
        /// Tests that attempting to configure both a silo and a client in the same host throws an exception.
        /// Orleans requires separate hosts for silos and clients.
        /// </summary>
        [Fact]
        public void ClientBuilderThrowsDuringStartupIfSiloBuildersAdded()
        {
            Assert.Throws<OrleansConfigurationException>(() =>
            {
                _ = new HostBuilder()
                    .UseOrleans((ctx, siloBuilder) =>
                    {
                        siloBuilder.UseLocalhostClustering();
                    })
                    .UseOrleansClient((ctx, clientBuilder) =>
                    {
                        clientBuilder.UseLocalhostClustering();
                    });
            });
        }

        /// <summary>
        /// Tests that attempting to configure both a silo and a client using the Host.CreateApplicationBuilder API throws an exception.
        /// This verifies that the same restriction applies to the modern hosting API.
        /// </summary>
        [Fact]
        public void ClientBuilderWithHotApplicationBuilderThrowsDuringStartupIfSiloBuildersAdded()
        {
            Assert.Throws<OrleansConfigurationException>(() =>
            {
                _ = Host.CreateApplicationBuilder()
                    .UseOrleans(siloBuilder =>
                    {
                        siloBuilder.UseLocalhostClustering();
                    })
                    .UseOrleansClient(clientBuilder =>
                    {
                        clientBuilder.UseLocalhostClustering();
                    });
            });
        }

        private static void RemoveConfigValidators(IServiceCollection services)
        {
            var validators = services.Where(descriptor => descriptor.ServiceType == typeof(IConfigurationValidator)).ToList();
            foreach (var validator in validators) services.Remove(validator);
        }

        private class MyService
        {
            public int Id { get; set; }
        }
    }
}
