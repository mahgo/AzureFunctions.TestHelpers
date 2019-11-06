using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AzureFunctions.TestHelpers.Starters;
using FluentAssertions;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace AzureFunctions.TestHelpers.Tests
{
    public class DurableFunctionsHelper : IClassFixture<DurableFunctionsHelper.HostFixture>
    {
        private readonly HostFixture _host;

        public DurableFunctionsHelper(HostFixture host)
        {
            _host = host;
            _host.Mock.ClearSubstitute();
        }
        
        [Fact]
        public async Task Ready()
        {
            // Arrange
            _host.Mock
                .When(x => x.Execute())
                .Do(x => Thread.Sleep(15000)); // waiting long enough to let the failure orchestration kick in (when enabled).

            var jobs = _host.Jobs;

            // Act
            await jobs.CallAsync(nameof(Starter), new Dictionary<string, object>
            {
                ["timerInfo"] = new TimerInfo(new WeeklySchedule(), new ScheduleStatus())
            });

            await jobs
                .Ready()
                .ThrowIfFailed()
                .Purge();

            // Assert
            _host.Mock
                .Received()
                .Execute();
        }

        [Fact]
        public async Task WaitWithTimeout()
        {
            // Arrange
            _host.Mock
                .When(x => x.Execute())
                .Do(x => Thread.Sleep(60000));

            var jobs = _host.Jobs;

            // Act
            await jobs.CallAsync(nameof(Starter), new Dictionary<string, object>
            {
                ["timerInfo"] = new TimerInfo(new WeeklySchedule(), new ScheduleStatus())
            });

            // Act & Assert
            jobs.Invoking(async x => await x.Ready(TimeSpan.FromSeconds(20)))
                .Should()
                .Throw<TaskCanceledException>();

            await jobs
                .Terminate()
                .Purge();
        }

        [Fact]
        public async Task WaitDoesNotThrow()
        {
            // Arrange
            _host.Mock
                .When(x => x.Execute())
                .Do(x => throw new InvalidOperationException());

            var jobs = _host.Jobs;

            // Act
            await jobs.CallAsync(nameof(Starter), new Dictionary<string, object>
            {
                ["timerInfo"] = new TimerInfo(new WeeklySchedule(), new ScheduleStatus())
            });

            await jobs
                .Ready()
                .Purge();

            // Assert
            _host.Mock.Received()
                .Execute();
        }

        [Fact]
        public async Task ThrowIfFailed()
        {
            // Arrange
            _host.Mock
                .When(x => x.Execute())
                .Do(x => throw new InvalidOperationException());

            var jobs = _host.Jobs;

            // Act
            await jobs.CallAsync(nameof(Starter), new Dictionary<string, object>
            {
                ["timerInfo"] = new TimerInfo(new WeeklySchedule(), new ScheduleStatus())
            });
            
            // Assert
            jobs.Invoking(x => x
                    .Ready(TimeSpan.FromSeconds(20))
                    .ThrowIfFailed())
                .Should()
                .Throw<Exception>();

            await jobs
                .Ready()
                .Purge();
        }
        
        [Fact]
        public async Task Terminate()
        {
            // Arrange
            _host.Mock
                .When(x => x.Execute())
                .Do(x => Thread.Sleep(60000));

            var jobs = _host.Jobs;
            await jobs.CallAsync(nameof(Starter), new Dictionary<string, object>
            {
                ["timerInfo"] = new TimerInfo(new WeeklySchedule(), new ScheduleStatus())
            });

            // Act
            await jobs
                .Terminate()
                .Purge();

            // Assert
            await jobs.Ready();
        }
        
        public class HostFixture : IDisposable, IAsyncLifetime
        {
            public IInjectable Mock { get; }
            private readonly IHost _host;
            public IJobHost Jobs => _host.Services.GetService<IJobHost>();

            public HostFixture()
            {
                Mock = Substitute.For<IInjectable>();
                _host = new HostBuilder()
                    .ConfigureWebJobs(builder => builder
                        .AddDurableTask(options =>
                        {
                            options.HubName = nameof(DurableFunctionsHelper);
                            options.MaxQueuePollingInterval = TimeSpan.FromSeconds(2);
                        })
                        .AddAzureStorageCoreServices()
                        .ConfigureServices(services => services.AddSingleton(Mock)))
                    .Build();
            }
        
            public void Dispose() => _host.Dispose();

            public Task InitializeAsync() => _host.StartAsync();

            public Task DisposeAsync() => Task.CompletedTask;
        }
    }
}