using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Runtime.Scheduler;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;


namespace UnitTestGrains
{
    public class TimerGrain : Grain, ITimerGrain
    {
        private bool deactivating;
        private int counter = 0;
        private Dictionary<string, IDisposable> allTimers;
        private IDisposable defaultTimer;
        private static readonly TimeSpan period = TimeSpan.FromMilliseconds(100);
        private readonly string DefaultTimerName = "DEFAULT TIMER";
        private IGrainContext context;

        private readonly ILogger logger;

        public TimerGrain(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger($"{this.GetType().Name}-{this.IdentityString}");
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            ThrowIfDeactivating();
            context = RuntimeContext.Current;
            defaultTimer = this.RegisterTimer(Tick, DefaultTimerName, period, period);
            allTimers = new Dictionary<string, IDisposable>();
            return Task.CompletedTask;
        }

        public Task StopDefaultTimer()
        {
            ThrowIfDeactivating();
            defaultTimer.Dispose();
            return Task.CompletedTask;
        }
        private Task Tick(object data)
        {
            counter++;
            logger.LogInformation(
                "{Data} Tick # {Counter} RuntimeContext = {RuntimeContext}",
                data,
                counter,
                RuntimeContext.Current);

            // make sure we run in the right activation context.
            if(!Equals(context, RuntimeContext.Current))
                logger.LogError((int)ErrorCode.Runtime_Error_100146, "Grain not running in the right activation context");

            string name = (string)data;
            IDisposable timer;
            if (name == DefaultTimerName)
            {
                timer = defaultTimer;
            }
            else
            {
                timer = allTimers[(string)data];
            }
            if(timer == null)
                logger.LogError((int)ErrorCode.Runtime_Error_100146, "Timer is null");
            if (timer != null && counter > 10000)
            {
                // do not let orphan timers ticking for long periods
                timer.Dispose();
            }

            return Task.CompletedTask;
        }

        public Task<TimeSpan> GetTimerPeriod()
        {
            return Task.FromResult(period);
        }

        public Task<int> GetCounter()
        {
            ThrowIfDeactivating();
            return Task.FromResult(counter);
        }
        public Task SetCounter(int value)
        {
            ThrowIfDeactivating();
            lock (this)
            {
                counter = value;
            }
            return Task.CompletedTask;
        }
        public Task StartTimer(string timerName)
        {
            ThrowIfDeactivating();
            IDisposable timer = this.RegisterTimer(Tick, timerName, TimeSpan.Zero, period);
            allTimers.Add(timerName, timer);
            return Task.CompletedTask;
        }

        public Task StopTimer(string timerName)
        {
            ThrowIfDeactivating();
            IDisposable timer = allTimers[timerName];
            timer.Dispose();
            return Task.CompletedTask;
        }

        public Task LongWait(TimeSpan time)
        {
            ThrowIfDeactivating();
            Thread.Sleep(time);
            return Task.CompletedTask;
        }

        public Task Deactivate()
        {
            deactivating = true;
            DeactivateOnIdle();
            return Task.CompletedTask;
        }

        private void ThrowIfDeactivating()
        {
            if (deactivating) throw new InvalidOperationException("This activation is deactivating");
        }
    }

    public class TimerCallGrain : Grain, ITimerCallGrain
    {
        private int tickCount;
        private Exception tickException;
        private IGrainTimer timer;
        private string timerName;
        private IGrainContext context;
        private TaskScheduler activationTaskScheduler;

        private readonly ILogger logger;

        public TimerCallGrain(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger($"{this.GetType().Name}-{this.IdentityString}");
        }

        public Task<int> GetTickCount() { return Task.FromResult(tickCount); }
        public Task<Exception> GetException() { return Task.FromResult(tickException); }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            context = RuntimeContext.Current;
            activationTaskScheduler = TaskScheduler.Current;
            return Task.CompletedTask;
        }

        public Task StartTimer(string name, TimeSpan delay)
        {
            logger.LogInformation("StartTimer Name={Name} Delay={Delay}", name, delay);
            if (timer is not null) throw new InvalidOperationException("Expected timer to be null");
            this.timer = base.RegisterTimer(TimerTick, name, delay, Constants.INFINITE_TIMESPAN); // One shot timer
            this.timerName = name;

            return Task.CompletedTask;
        }

        public Task StartTimer(string name, TimeSpan delay, string operationType)
        {
            logger.LogInformation("StartTimer Name={Name} Delay={Delay}", name, delay);
            if (timer is not null) throw new InvalidOperationException("Expected timer to be null");
            var state = Tuple.Create<string, object>(operationType, name);
            this.timer = base.RegisterTimer(TimerTickAdvanced, state, delay, Constants.INFINITE_TIMESPAN); // One shot timer
            this.timerName = name;

            return Task.CompletedTask;
        }

        public Task RestartTimer(string name, TimeSpan delay)
        {
            logger.LogInformation("RestartTimer Name={Name} Delay={Delay}", name, delay);
            this.timerName = name;
            timer.Change(delay, Constants.INFINITE_TIMESPAN);

            return Task.CompletedTask;
        }

        public Task RestartTimer(string name, TimeSpan delay, TimeSpan period)
        {
            logger.LogInformation("RestartTimer Name={Name} Delay={Delay} Period={Period}", name, delay, period);
            this.timerName = name;
            timer.Change(delay, period);

            return Task.CompletedTask;
        }

        public Task StopTimer(string name)
        {
            logger.LogInformation("StopTimer Name={Name}", name);
            if (name != this.timerName)
            {
                throw new ArgumentException($"Wrong timer name: Expected={this.timerName} Actual={name}");
            }

            timer.Dispose();
            timer = null;
            timerName = null;
            return Task.CompletedTask;
        }

        private async Task TimerTick(object data)
        {
            try
            {
                await ProcessTimerTick(data);
            }
            catch (Exception exc)
            {
                this.tickException = exc;
                throw;
            }
        }

        private async Task TimerTickAdvanced(object data)
        {
            try
            {
                var state = (Tuple<string, object>)data;
                var operation = state.Item1;
                var name = state.Item2;

                await ProcessTimerTick(name);

                if (operation == "update_period")
                {
                    var newPeriod = TimeSpan.FromSeconds(100);
                    timer.Change(newPeriod, newPeriod);
                }
                else if (operation == "dispose_timer")
                {
                    await StopTimer((string)name);
                }
            }
            catch (Exception exc)
            {
                this.tickException = exc;
                throw;
            }
        }

        private async Task ProcessTimerTick(object data)
        {
            string step = "TimerTick";
            LogStatus(step);
            // make sure we run in the right activation context.
            CheckRuntimeContext(step);

            string name = (string)data;
            if (name != this.timerName)
            {
                throw new ArgumentException(string.Format("Wrong timer name: Expected={0} Actual={1}", this.timerName, name));
            }

            ISimpleGrain grain = GrainFactory.GetGrain<ISimpleGrain>(0, SimpleGrain.SimpleGrainNamePrefix);

            LogStatus("Before grain call #1");
            await grain.SetA(tickCount);
            step = "After grain call #1";
            LogStatus(step);
            CheckRuntimeContext(step);

            LogStatus("Before Delay");
            await Task.Delay(TimeSpan.FromSeconds(1));
            step = "After Delay";
            LogStatus(step);
            CheckRuntimeContext(step);

            LogStatus("Before grain call #2");
            await grain.SetB(tickCount);
            step = "After grain call #2";
            LogStatus(step);
            CheckRuntimeContext(step);

            LogStatus("Before grain call #3");
            int res = await grain.GetAxB();
            step = "After grain call #3 - Result = " + res;
            LogStatus(step);
            CheckRuntimeContext(step);

            tickCount++;
        }

        private void CheckRuntimeContext(string what)
        {
            if (RuntimeContext.Current == null 
                || !RuntimeContext.Current.Equals(context))
            {
                throw new InvalidOperationException(
                    string.Format("{0} in timer callback with unexpected activation context: Expected={1} Actual={2}",
                                  what, context, RuntimeContext.Current));
            }
            if (TaskScheduler.Current.Equals(activationTaskScheduler) && TaskScheduler.Current is ActivationTaskScheduler)
            {
                // Everything is as expected
            }
            else
            {
                throw new InvalidOperationException(
                    string.Format("{0} in timer callback with unexpected TaskScheduler.Current context: Expected={1} Actual={2}",
                                  what, activationTaskScheduler, TaskScheduler.Current));
            }
        }

        private void LogStatus(string what)
        {
            logger.LogInformation(
                "{TimerName} Tick # {TickCount} - {Step} - RuntimeContext.Current={RuntimeContext} TaskScheduler.Current={TaskScheduler} CurrentWorkerThread={Thread}",
                timerName,
                tickCount,
                what,
                RuntimeContext.Current,
                TaskScheduler.Current,
                Thread.CurrentThread.Name);
        }
    }

    public class TimerRequestGrain : Grain, ITimerRequestGrain
    {
        private TaskCompletionSource<int> completionSource;

        public Task<string> GetRuntimeInstanceId()
        {
            return Task.FromResult(this.RuntimeIdentity);
        }

        public async Task StartAndWaitTimerTick(TimeSpan dueTime)
        {
            this.completionSource = new TaskCompletionSource<int>();
            var timer = this.RegisterTimer(TimerTick, null, dueTime, TimeSpan.FromMilliseconds(-1));
            await this.completionSource.Task;
        }

        public Task StartStuckTimer(TimeSpan dueTime)
        {
            this.completionSource = new TaskCompletionSource<int>();
            var timer = this.RegisterTimer(StuckTimerTick, null, dueTime, TimeSpan.FromSeconds(1));
            return Task.CompletedTask;
        }

        private Task TimerTick(object state)
        {
            this.completionSource.SetResult(1);
            return Task.CompletedTask;
        }

        private async Task StuckTimerTick(object state)
        {
            await completionSource.Task;
        }
    }
}
