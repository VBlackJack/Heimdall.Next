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
using Heimdall.Core.Discovery;
using System.IO;

namespace Heimdall.App.Tests;

public sealed class NetworkCartographyViewModelTests : IDisposable
{
    public NetworkCartographyViewModelTests()
    {
        CleanupArtifacts();
    }

    [Fact]
    public async Task Scan_EmptySubnet_SetsError()
    {
        var vm = new NetworkCartographyViewModel();
        vm.Initialize(null);
        vm.Subnet = "   ";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowError);
        Assert.False(vm.IsScanning);
        Assert.Contains("ToolNetMapErrorEmptySubnet", vm.StatusText);
    }

    [Fact]
    public async Task Scan_ValidSubnet_DelegatesToScanner()
    {
        var scanner = new FakeScanner
        {
            ScanResult = CreateSnapshot("10.0.0.0/30", "10.0.0.1")
        };
        var vm = new NetworkCartographyViewModel();
        vm.Initialize(null);
        vm.SetScanner(scanner);
        vm.Subnet = "10.0.0.0/30";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(1, scanner.ScanCallCount);
        Assert.Equal("10.0.0.0/30", scanner.LastProfile?.Subnet);
    }

    [Fact]
    public async Task Scan_AutoAppendsSlash24()
    {
        var scanner = new FakeScanner
        {
            ScanResult = CreateSnapshot("10.0.0.1/24")
        };
        var vm = new NetworkCartographyViewModel();
        vm.Initialize(null);
        vm.SetScanner(scanner);
        vm.Subnet = "10.0.0.1";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal("10.0.0.1/24", scanner.LastProfile?.Subnet);
    }

    [Fact]
    public async Task Scan_InvalidCidr_SetsError()
    {
        var scanner = new FakeScanner();
        var vm = new NetworkCartographyViewModel();
        vm.Initialize(null);
        vm.SetScanner(scanner);
        vm.Subnet = "not-a-subnet";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowError);
        Assert.Equal(0, scanner.ScanCallCount);
    }

    [Fact]
    public async Task Scan_CompletesSuccessfully_UpdatesState()
    {
        var snapshot = CreateSnapshot("10.0.0.0/30", "10.0.0.1", "10.0.0.2");
        var scanner = new FakeScanner { ScanResult = snapshot };
        var vm = new NetworkCartographyViewModel();
        vm.Initialize(null);
        vm.SetScanner(scanner);
        vm.Subnet = "10.0.0.0/30";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(vm.IsScanning);
        Assert.True(vm.ShowResults);
        Assert.Equal(2, vm.HostResults.Count);
        Assert.NotNull(vm.LastSnapshot);
        Assert.Contains("ToolNetMapStatusComplete", vm.StatusText);
    }

    [Fact]
    public async Task Cancel_StopsScanning()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner
        {
            OnScan = async (_, _, _, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return CreateSnapshot("10.0.0.0/30");
            }
        };

        var vm = new NetworkCartographyViewModel();
        vm.Initialize(null);
        vm.SetScanner(scanner);
        vm.Subnet = "10.0.0.0/30";

        var task = vm.ScanCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.CancelCommand.Execute(null);
        await task;

        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task BuildCsvExport_DelegatesToEngine()
    {
        var scanner = new FakeScanner
        {
            ScanResult = CreateSnapshot("10.0.0.0/30", "10.0.0.1")
        };
        var vm = new NetworkCartographyViewModel();
        vm.Initialize(null);
        vm.SetScanner(scanner);
        vm.Subnet = "10.0.0.0/30";

        await vm.ScanCommand.ExecuteAsync(null);
        var csv = vm.BuildCsvExport();

        Assert.Contains("10.0.0.1", csv);
    }

    [Fact]
    public void GetDrawIoXml_NoSnapshot_ReturnsNull()
    {
        var vm = new NetworkCartographyViewModel();
        vm.Initialize(null);

        Assert.Null(vm.GetDrawIoXml());
    }

    [Fact]
    public async Task ClearKb_ResetsStats()
    {
        var nonEmptyKb = new NetworkKnowledgeBase(
            1,
            DateTime.UtcNow,
            new KnowledgeBaseTtlConfig(),
            new Dictionary<string, KnownHost>(StringComparer.OrdinalIgnoreCase)
            {
                ["10.0.0.1"] = KnowledgeBaseManager.MergeHost(
                    null,
                    CreateHost("10.0.0.1"),
                    DateTime.UtcNow,
                    "test")
            });
        await KnowledgeBaseManager.SaveAsync(nonEmptyKb);

        var vm = new NetworkCartographyViewModel();
        vm.Initialize(null);
        await vm.LoadKbStatsAsync();
        Assert.True(vm.CanClearKb);

        await vm.ClearKbCommand.ExecuteAsync(null);

        Assert.False(vm.CanClearKb);
        Assert.Contains("ToolNetMapKbStatsNever", vm.KbStatsText);
    }

    public void Dispose()
    {
        CleanupArtifacts();
    }

    private static void CleanupArtifacts()
    {
        var configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
        var kbPath = Path.Combine(configDir, "network-kb.json");
        var scansDir = Path.Combine(configDir, "network-scans");

        try
        {
            if (File.Exists(kbPath))
            {
                File.Delete(kbPath);
            }

            if (Directory.Exists(scansDir))
            {
                Directory.Delete(scansDir, true);
            }
        }
        catch
        {
            // Best effort cleanup for isolated test runs.
        }
    }

    private static NetworkScanSnapshot CreateSnapshot(string subnet, params string[] hostIps)
    {
        return new NetworkScanSnapshot(
            "test",
            DateTime.UtcNow,
            new ScanProfile(subnet, ScanDepth.Quick, null, 50, 2000, false, true),
            null,
            TimeSpan.Zero,
            hostIps.Select(CreateHost).ToList(),
            null);
    }

    private static HostScanResult CreateHost(string ipAddress)
    {
        return new HostScanResult(
            ipAddress,
            $"host-{ipAddress.Replace('.', '-')}.lab",
            true,
            5,
            [new ServiceResult(80, true, "HTTP", null, null, 1)],
            new RoleMatch("Web Server", 80, ["HTTP"]),
            [new RoleMatch("Web Server", 80, ["HTTP"])]);
    }

    private sealed class FakeScanner : ICartographyScanner
    {
        public NetworkScanSnapshot? ScanResult { get; set; }

        public List<string>? RemoteSubnets { get; set; }

        public int ScanCallCount { get; private set; }

        public ScanProfile? LastProfile { get; private set; }

        public Func<ScanProfile, NetworkKnowledgeBase?, Action<CartographyScanProgress>?, CancellationToken, Task<NetworkScanSnapshot>>? OnScan { get; set; }

        public async Task<NetworkScanSnapshot> ScanAsync(
            ScanProfile profile,
            NetworkKnowledgeBase? knowledgeBase,
            Action<CartographyScanProgress>? onProgress,
            CancellationToken ct)
        {
            ScanCallCount++;
            LastProfile = profile;

            if (OnScan is not null)
            {
                return await OnScan(profile, knowledgeBase, onProgress, ct);
            }

            onProgress?.Invoke(new CartographyScanProgress
            {
                Phase = "Discovery",
                StatusKey = "ToolNetMapStatusDiscovery",
                StatusArgs = ["0", "1"],
                IsIndeterminate = true,
                Completed = 0,
                Total = 1,
            });

            var snapshot = ScanResult ?? CreateSnapshot(profile.Subnet);
            var completed = 0;
            foreach (var host in snapshot.Hosts)
            {
                completed++;
                onProgress?.Invoke(new CartographyScanProgress
                {
                    Phase = "TunnelScan",
                    StatusKey = "ToolNetMapTunnelScanningHost",
                    StatusArgs = [host.IpAddress, completed.ToString(), snapshot.Hosts.Count.ToString()],
                    IsIndeterminate = false,
                    Completed = completed,
                    Total = snapshot.Hosts.Count,
                    CompletedHost = host,
                });
            }

            return snapshot;
        }

        public Task<List<string>> DetectRemoteSubnetsAsync(CancellationToken ct)
            => Task.FromResult(RemoteSubnets ?? []);

        public void SetGateway(Heimdall.Core.Configuration.SshGatewayDto? gateway)
        {
        }

        public void Cleanup()
        {
        }
    }
}
