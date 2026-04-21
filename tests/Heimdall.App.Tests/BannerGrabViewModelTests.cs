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

using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Configuration;
using Heimdall.Core.Network;

namespace Heimdall.App.Tests;

public class BannerGrabViewModelTests
{
    [Fact]
    public async Task Grab_EmptyHost_SetsError()
    {
        var service = new FakeGrabService();
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        await vm.GrabAsync("   ", "22");

        Assert.True(vm.ShowError);
        Assert.False(vm.IsGrabbing);
        Assert.Equal(0, service.GrabCallCount);
    }

    [Fact]
    public async Task Grab_InvalidHost_SetsError()
    {
        var service = new FakeGrabService();
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        await vm.GrabAsync("bad;host", "22");

        Assert.True(vm.ShowError);
        Assert.Equal(0, service.GrabCallCount);
    }

    [Fact]
    public async Task Grab_EmptyPorts_SetsError()
    {
        var service = new FakeGrabService();
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        await vm.GrabAsync("10.0.0.1", string.Empty);

        Assert.True(vm.ShowError);
        Assert.Equal(0, service.GrabCallCount);
    }

    [Fact]
    public async Task Grab_InvalidPorts_SetsError()
    {
        var service = new FakeGrabService();
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        await vm.GrabAsync("10.0.0.1", "abc");

        Assert.True(vm.ShowError);
        Assert.Equal(0, service.GrabCallCount);
    }

    [Fact]
    public async Task Grab_ValidInput_DelegatesToService()
    {
        var service = new FakeGrabService();
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        await vm.GrabAsync("10.0.0.1", "22,80");

        Assert.Equal(1, service.GrabCallCount);
        Assert.Equal("10.0.0.1", service.LastHost);
        Assert.Equal([22, 80], service.LastPorts);
    }

    [Fact]
    public async Task Grab_CompletesSuccessfully_PopulatesResults()
    {
        var service = new FakeGrabService
        {
            GrabResult =
            [
                new BannerProbeResult(22, "OpenSSH", "5 ms", "SSH-2.0-OpenSSH_9.0"),
                new BannerProbeResult(80, "HTTP", "10 ms", null),
            ],
        };
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        await vm.GrabAsync("10.0.0.1", "22,80");

        Assert.Equal(2, vm.GetAllResults().Count);
        Assert.False(vm.IsGrabbing);
    }

    [Fact]
    public async Task Grab_FilteredResults_BannerOnly()
    {
        var service = new FakeGrabService
        {
            GrabResult =
            [
                new BannerProbeResult(22, "OpenSSH", "5 ms", "SSH-2.0-OpenSSH_9.0"),
                new BannerProbeResult(80, "HTTP", "10 ms", null),
            ],
        };
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        await vm.GrabAsync("10.0.0.1", "22,80");

        Assert.Single(vm.GetFilteredResults(true));
    }

    [Fact]
    public async Task Grab_FilteredResults_All()
    {
        var service = new FakeGrabService
        {
            GrabResult =
            [
                new BannerProbeResult(22, "OpenSSH", "5 ms", "SSH-2.0-OpenSSH_9.0"),
                new BannerProbeResult(80, "HTTP", "10 ms", null),
            ],
        };
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        await vm.GrabAsync("10.0.0.1", "22,80");

        Assert.Equal(2, vm.GetFilteredResults(false).Count);
    }

    [Fact]
    public async Task SetGateway_DelegatesToService()
    {
        var service = new FakeGrabService();
        var gateway = new SshGatewayDto { Name = "gw", Host = "jump", Port = 22, User = "me" };
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        vm.SetGateway(gateway);
        await vm.GrabAsync("10.0.0.1", "22");

        Assert.Same(gateway, service.LastGateway);
    }

    [Fact]
    public async Task BuildCsvExport_AfterGrab_ReturnsNonEmptyString()
    {
        var service = new FakeGrabService
        {
            GrabResult =
            [
                new BannerProbeResult(22, "OpenSSH", "5 ms", "SSH-2.0-OpenSSH_9.0"),
            ],
        };
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        await vm.GrabAsync("10.0.0.1", "22");

        var csv = vm.BuildCsvExport();
        Assert.False(string.IsNullOrWhiteSpace(csv));
    }

    [Fact]
    public async Task ResultCountText_AfterGrab_ShowsCorrectFormat()
    {
        var service = new FakeGrabService
        {
            GrabResult =
            [
                new BannerProbeResult(22, "OpenSSH", "5 ms", "SSH-2.0-OpenSSH_9.0"),
                new BannerProbeResult(80, "HTTP", "10 ms", null),
            ],
        };
        var vm = new BannerGrabViewModel(service);
        vm.Initialize(null);

        await vm.GrabAsync("10.0.0.1", "22,80");

        Assert.Equal("1 / 2", vm.ResultCountText);
    }

    private sealed class FakeGrabService : IBannerGrabService
    {
        public IReadOnlyList<BannerProbeResult>? GrabResult { get; set; }
        public int GrabCallCount { get; private set; }
        public SshGatewayDto? LastGateway { get; private set; }
        public string LastHost { get; private set; } = string.Empty;
        public IReadOnlyList<int> LastPorts { get; private set; } = Array.Empty<int>();

        public Task<IReadOnlyList<BannerProbeResult>> GrabAsync(
            string host,
            IReadOnlyList<int> ports,
            Action<BannerGrabProgress>? onProgress,
            CancellationToken ct)
        {
            GrabCallCount++;
            LastHost = host;
            LastPorts = [.. ports];
            var results = GrabResult ?? Array.Empty<BannerProbeResult>();

            for (var i = 0; i < results.Count; i++)
            {
                onProgress?.Invoke(new BannerGrabProgress
                {
                    Completed = i + 1,
                    Total = ports.Count,
                    LatestResult = results[i],
                });
            }

            return Task.FromResult(results);
        }

        public void SetGateway(SshGatewayDto? gateway)
        {
            LastGateway = gateway;
        }
    }
}
