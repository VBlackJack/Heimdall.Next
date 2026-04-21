/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.ComponentModel;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Configuration;
using Heimdall.Core.Network;

namespace Heimdall.App.Tests;

public class PingToolViewModelTests
{
    [Fact]
    public void ValidateInputs_Delegates_ToEngine()
    {
        var vm = new PingToolViewModel();
        var (inputs, errorKey) = vm.ValidateInputs("", "1000", "");

        Assert.Null(inputs);
        Assert.Equal("ToolValidationHostRequired", errorKey);
    }

    [Fact]
    public async Task StartAsync_FillsHistoryAndStats_AfterPings()
    {
        var service = new FakePingService();
        service.Queue.Enqueue(new PingProbeResult(1, "10:00:00", 10, PingStatus.Success, 64, "OK", "1.1.1.1"));
        service.Queue.Enqueue(new PingProbeResult(2, "10:00:01", 12, PingStatus.Success, 64, "OK", "1.1.1.1"));
        service.Queue.Enqueue(new PingProbeResult(3, "10:00:02", 14, PingStatus.Success, 64, "OK", "1.1.1.1"));

        var vm = new PingToolViewModel(service);

        await vm.StartAsync(new PingInputs("example.com", 1000, 3), 10);

        Assert.Equal(3, vm.GetHistory().Count);
        Assert.Equal(3, vm.Stats.Received);
        Assert.Equal(3, vm.SentCount);
    }

    [Fact]
    public async Task StartAsync_SessionCompleted_WhenCountReached()
    {
        var service = new FakePingService();
        service.Queue.Enqueue(new PingProbeResult(1, "10:00:00", 10, PingStatus.Success, 64, "OK", "1.1.1.1"));
        service.Queue.Enqueue(new PingProbeResult(2, "10:00:01", 12, PingStatus.Success, 64, "OK", "1.1.1.1"));

        var vm = new PingToolViewModel(service);

        await vm.StartAsync(new PingInputs("example.com", 1000, 2), 10);

        Assert.True(vm.SessionCompleted);
    }

    [Fact]
    public async Task StartAsync_UnlimitedCount_StopsWhenCancelled()
    {
        var service = new FakePingService
        {
            DelayPerPing = TimeSpan.FromMilliseconds(20),
        };
        service.Queue.Enqueue(new PingProbeResult(1, "10:00:00", 10, PingStatus.Success, 64, "OK", "1.1.1.1"));
        service.Queue.Enqueue(new PingProbeResult(2, "10:00:01", 12, PingStatus.Success, 64, "OK", "1.1.1.1"));
        service.Queue.Enqueue(new PingProbeResult(3, "10:00:02", 14, PingStatus.Success, 64, "OK", "1.1.1.1"));

        var vm = new PingToolViewModel(service);
        var task = vm.StartAsync(new PingInputs("example.com", 1000, 0), 1);

        await Task.Delay(30);
        vm.Stop();
        await task;

        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task StartAsync_GatewayConnectThrows_SetsError()
    {
        var service = new FakePingService
        {
            ThrowOnStart = new Exception("down"),
        };
        var vm = new PingToolViewModel(service);
        vm.Initialize(null);

        await vm.StartAsync(new PingInputs("example.com", 1000, 1), 10);

        Assert.True(vm.ShowError);
        Assert.Contains("down", vm.ErrorText, StringComparison.Ordinal);
        Assert.Equal(0, service.PingCallCount);
    }

    [Fact]
    public async Task StartAsync_PingThrows_RecordsAsError()
    {
        var service = new FakePingService
        {
            ThrowOnPing = new Exception("net"),
        };
        var vm = new PingToolViewModel(service);

        await vm.StartAsync(new PingInputs("example.com", 1000, 1), 10);

        Assert.NotNull(vm.LatestResult);
        Assert.Equal(PingStatus.Error, vm.LatestResult!.Status);
        Assert.Single(vm.GetHistory());
    }

    [Fact]
    public void SetGateway_Delegates()
    {
        var service = new FakePingService();
        var gateway = new SshGatewayDto { Name = "gw", Host = "jump", Port = 22, User = "me" };
        var vm = new PingToolViewModel(service);

        vm.SetGateway(gateway);

        Assert.Same(gateway, service.LastGateway);
    }

    [Fact]
    public async Task Stop_EndsSession()
    {
        var service = new FakePingService
        {
            DelayPerPing = TimeSpan.FromMilliseconds(20),
        };
        service.Queue.Enqueue(new PingProbeResult(1, "10:00:00", 10, PingStatus.Success, 64, "OK", "1.1.1.1"));
        service.Queue.Enqueue(new PingProbeResult(2, "10:00:01", 12, PingStatus.Success, 64, "OK", "1.1.1.1"));

        var vm = new PingToolViewModel(service);
        var task = vm.StartAsync(new PingInputs("example.com", 1000, 5), 1);

        await Task.Delay(25);
        vm.Stop();
        await task;

        Assert.True(service.SessionEnded);
    }

    [Fact]
    public async Task DataPoints_RingBuffer_StopsGrowingPast60()
    {
        var service = new FakePingService();
        for (var index = 1; index <= 100; index++)
        {
            service.Queue.Enqueue(new PingProbeResult(index, "10:00:00", index, PingStatus.Success, 64, "OK", "1.1.1.1"));
        }

        var vm = new PingToolViewModel(service);
        await vm.StartAsync(new PingInputs("example.com", 1000, 100), 1);

        Assert.Equal(PingStatsEngine.MaxDataPoints, vm.GetDataPoints().Count);
    }

    [Fact]
    public async Task LatestResult_FiresPropertyChanged_PerPing()
    {
        var service = new FakePingService();
        service.Queue.Enqueue(new PingProbeResult(1, "10:00:00", 10, PingStatus.Success, 64, "OK", "1.1.1.1"));
        service.Queue.Enqueue(new PingProbeResult(2, "10:00:01", 12, PingStatus.Success, 64, "OK", "1.1.1.1"));
        service.Queue.Enqueue(new PingProbeResult(3, "10:00:02", 14, PingStatus.Success, 64, "OK", "1.1.1.1"));

        var vm = new PingToolViewModel(service);
        var count = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PingToolViewModel.LatestResult))
            {
                count++;
            }
        };

        await vm.StartAsync(new PingInputs("example.com", 1000, 3), 1);

        Assert.Equal(3, count);
    }

    private sealed class FakePingService : IPingService
    {
        public Queue<PingProbeResult> Queue { get; } = new();
        public Exception? ThrowOnStart { get; set; }
        public Exception? ThrowOnPing { get; set; }
        public TimeSpan DelayPerPing { get; set; }
        public SshGatewayDto? LastGateway { get; private set; }
        public bool SessionEnded { get; private set; }
        public int PingCallCount { get; private set; }

        public void SetGateway(SshGatewayDto? gateway)
        {
            LastGateway = gateway;
        }

        public Task StartSessionAsync(CancellationToken ct)
        {
            if (ThrowOnStart is not null)
            {
                throw ThrowOnStart;
            }

            SessionEnded = false;
            return Task.CompletedTask;
        }

        public async Task<PingProbeResult> PingAsync(string host, int seq, int timeoutMs, CancellationToken ct)
        {
            PingCallCount++;

            if (DelayPerPing > TimeSpan.Zero)
            {
                await Task.Delay(DelayPerPing, ct);
            }

            if (ThrowOnPing is not null)
            {
                throw ThrowOnPing;
            }

            if (Queue.Count > 0)
            {
                return Queue.Dequeue();
            }

            return new PingProbeResult(seq, "10:00:00", 10, PingStatus.Success, 64, "OK", host);
        }

        public void EndSession()
        {
            SessionEnded = true;
        }

        public void Dispose()
        {
            EndSession();
        }
    }
}
