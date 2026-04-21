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
using Heimdall.Core.Security;

namespace Heimdall.App.Tests;

public class DefaultCredentialViewModelTests
{
    [Fact]
    public async Task ScanAsync_EmptyHost_ShowsError()
    {
        var vm = new DefaultCredentialViewModel();
        vm.Initialize(null);
        vm.Host = "   ";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowError);
        Assert.False(vm.ShowResults);
    }

    [Fact]
    public async Task ScanAsync_NoAutoDetect_TestsKnownPortsWithoutProbing()
    {
        var probed = new List<int>();
        var tested = new List<(string Service, int Port)>();
        var scanner = new FakeScanner(
            probeFunc: (_, port, _) =>
            {
                probed.Add(port);
                return Task.FromResult(true);
            },
            testFunc: (_, port, service, _, _, _) =>
            {
                tested.Add((service, port));
                return Task.FromResult(new CredTestResultDto
                {
                    Service = service,
                    Port = port,
                    Username = "test",
                    Password = "test",
                    Status = CredTestStatus.Changed,
                });
            });

        var vm = new DefaultCredentialViewModel();
        vm.Initialize(null);
        vm.SetScanner(scanner);
        vm.SetDelayProvider(_ => Task.CompletedTask);
        vm.Host = "10.0.0.1";
        vm.AutoDetect = false;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Empty(probed);
        Assert.NotEmpty(tested);
        Assert.True(vm.ShowResults);
        Assert.NotEmpty(vm.ScanResults);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanAsync_AutoDetect_ProbesAndTests()
    {
        var scanner = new FakeScanner(
            probeFunc: (_, port, _) => Task.FromResult(port == 22),
            testFunc: (_, port, service, _, _, _) =>
                Task.FromResult(new CredTestResultDto
                {
                    Service = service,
                    Port = port,
                    Username = "root",
                    Password = "root",
                    Status = CredTestStatus.Default,
                }));

        var vm = new DefaultCredentialViewModel();
        vm.Initialize(null);
        vm.SetScanner(scanner);
        vm.SetDelayProvider(_ => Task.CompletedTask);
        vm.Host = "10.0.0.1";
        vm.AutoDetect = true;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowResults);
        Assert.NotEmpty(vm.ScanResults);
        Assert.All(vm.ScanResults, result => Assert.Equal("SSH", result.Service));
    }

    [Fact]
    public async Task ScanAsync_AutoDetect_NoPorts_ShowsNoDefaultsSummary()
    {
        var vm = new DefaultCredentialViewModel();
        vm.Initialize(null);
        vm.SetScanner(new FakeScanner(
            probeFunc: (_, _, _) => Task.FromResult(false)));
        vm.SetDelayProvider(_ => Task.CompletedTask);
        vm.Host = "10.0.0.1";
        vm.AutoDetect = true;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowResults);
        Assert.Empty(vm.ScanResults);
        Assert.Contains("ToolDefCredNoDefaults", vm.SummaryText);
    }

    [Fact]
    public async Task ScanAsync_ScannerError_ReturnsErrorStatus()
    {
        var scanner = new FakeScanner(
            testFunc: (_, port, service, _, _, _) =>
                Task.FromResult(new CredTestResultDto
                {
                    Service = service,
                    Port = port,
                    Username = "root",
                    Password = "root",
                    Status = CredTestStatus.Error,
                    ErrorDetail = "Connection refused",
                }));

        var vm = new DefaultCredentialViewModel();
        vm.Initialize(null);
        vm.SetScanner(scanner);
        vm.SetDelayProvider(_ => Task.CompletedTask);
        vm.Host = "10.0.0.1";
        vm.AutoDetect = false;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowResults);
        Assert.Contains(vm.ScanResults, result => result.Status == CredTestStatus.Error);
    }

    [Fact]
    public async Task Cancel_StopsOngoingScan()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner(
            testFunc: async (_, port, service, _, _, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new CredTestResultDto
                {
                    Service = service,
                    Port = port,
                    Username = "root",
                    Password = "root",
                    Status = CredTestStatus.Changed,
                };
            });

        var vm = new DefaultCredentialViewModel();
        vm.Initialize(null);
        vm.SetScanner(scanner);
        vm.SetDelayProvider(_ => Task.CompletedTask);
        vm.Host = "10.0.0.1";
        vm.AutoDetect = false;

        var task = vm.ScanCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.CancelCommand.Execute(null);
        await task;

        Assert.False(vm.IsScanning);
    }

    [Fact]
    public void BuildCsvExport_DelegatesToEngine()
    {
        var vm = new DefaultCredentialViewModel();
        vm.Initialize(null);
        vm.ScanResults =
        [
            new CredTestResultDto
            {
                Service = "SSH",
                Port = 22,
                Username = "root",
                Password = "root",
                Status = CredTestStatus.Default,
            },
        ];

        var csv = vm.BuildCsvExport();

        Assert.Contains("SSH", csv);
        Assert.Contains("22", csv);
    }

    [Fact]
    public void BuildReportText_DelegatesToEngine()
    {
        var vm = new DefaultCredentialViewModel();
        vm.Initialize(null);
        vm.ScanResults =
        [
            new CredTestResultDto
            {
                Service = "FTP",
                Port = 21,
                Username = "admin",
                Password = "admin",
                Status = CredTestStatus.Changed,
            },
        ];

        var report = vm.BuildReportText();

        Assert.Contains("FTP", report);
        Assert.Contains("admin", report);
    }

    private sealed class FakeScanner : ICredentialScanner
    {
        private readonly Func<string, int, CancellationToken, Task<bool>> _probeFunc;
        private readonly Func<string, int, string, string, string, CancellationToken, Task<CredTestResultDto>> _testFunc;

        public FakeScanner(
            Func<string, int, CancellationToken, Task<bool>>? probeFunc = null,
            Func<string, int, string, string, string, CancellationToken, Task<CredTestResultDto>>? testFunc = null)
        {
            _probeFunc = probeFunc ?? ((_, _, _) => Task.FromResult(false));
            _testFunc = testFunc ?? ((_, port, service, user, pass, _) =>
                Task.FromResult(new CredTestResultDto
                {
                    Service = service,
                    Port = port,
                    Username = user,
                    Password = pass,
                    Status = CredTestStatus.Changed,
                }));
        }

        public Task<bool> ProbePortAsync(string host, int port, CancellationToken ct)
            => _probeFunc(host, port, ct);

        public Task<CredTestResultDto> TestCredentialAsync(
            string host,
            int port,
            string service,
            string user,
            string pass,
            CancellationToken ct)
            => _testFunc(host, port, service, user, pass, ct);

        public void Cleanup()
        {
        }
    }
}
