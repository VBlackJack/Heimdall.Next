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
using Heimdall.Core.Discovery;
using Heimdall.Core.Localization;
using System.IO;

namespace Heimdall.App.Tests;

public sealed class SmbEnumeratorViewModelTests
{
    [Fact]
    public void EnumerateCommand_EmptyHost_CannotExecute()
    {
        var vm = new SmbEnumeratorViewModel(new FakeSmbService());

        vm.HostInput = string.Empty;

        Assert.False(vm.EnumerateCommand.CanExecute(null));
    }

    [Fact]
    public async Task EnumerateCommand_InvalidHost_SetsInvalidHostError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new SmbEnumeratorViewModel(new FakeSmbService());
        vm.UpdateLocalizer(localizer);
        vm.HostInput = "bad host name";

        vm.EnumerateCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.True(vm.HasError);
        Assert.Equal(localizer["ErrorInvalidHost"], vm.ErrorMessage);
    }

    [Fact]
    public async Task EnumerateCommand_SuccessfulDirectOutcome_PopulatesResultAndFindings()
    {
        var service = new FakeSmbService
        {
            Outcome = new SmbEnumOutcome(
                SmbEnumerationEngine.BuildResult(
                    new NtlmInfo("HOST01", "DOMAIN", "host01.domain.local", "domain.local", "forest.local", "20348"),
                    new SmbNegotiateInfo("guid-1", 0x0311, true, true, new DateTime(2026, 4, 17, 10, 0, 0), null, 0),
                    null,
                    null,
                    "00-11-22-33-44-55",
                    false),
                null,
                null),
        };
        var localizer = await CreateLocalizerAsync("en");
        var vm = new SmbEnumeratorViewModel(service);
        vm.UpdateLocalizer(localizer);
        vm.HostInput = "server.local";

        vm.EnumerateCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.HasResults);

        Assert.NotNull(vm.Result);
        Assert.True(vm.HasResults);
        Assert.True(vm.HasProtocolSection);
        Assert.Equal("HOST01", vm.Result!.ComputerName);
        Assert.NotEmpty(vm.Findings);
        Assert.Contains("HOST01", vm.LastReport, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnumerateCommand_TunnelOutcome_HidesProtocolSection()
    {
        var service = new FakeSmbService
        {
            Outcome = new SmbEnumOutcome(
                SmbEnumerationEngine.BuildTunnelResult(new SmbTunnelObservations(
                    Domain: "WORKGROUP",
                    OsInfo: "Unix",
                    ServerName: "SAMBA",
                    RpcServerName: null,
                    RpcOsVersion: null,
                    NetBiosName: "NBHOST",
                    NetBiosDomain: null,
                    MacAddress: null,
                    HasAnyData: true)),
                null,
                null),
        };
        var vm = new SmbEnumeratorViewModel(service)
        {
            HostInput = "server.local",
        };

        vm.EnumerateCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.HasResults);

        Assert.False(vm.HasProtocolSection);
        Assert.Equal("SAMBA", vm.Result!.ComputerName);
    }

    [Fact]
    public async Task EnumerateCommand_ServiceOutcomeError_SetsLocalizedError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new SmbEnumeratorViewModel(new FakeSmbService
        {
            Outcome = new SmbEnumOutcome(null, "ToolSmbErrorConnection", "boom"),
        });
        vm.UpdateLocalizer(localizer);
        vm.HostInput = "server.local";

        vm.EnumerateCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.True(vm.HasError);
        Assert.Contains("boom", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnumerateCommand_ServiceThrows_SetsConnectionError()
    {
        var vm = new SmbEnumeratorViewModel(new FakeSmbService
        {
            ThrowOnEnumerate = new InvalidOperationException("boom"),
        })
        {
            HostInput = "server.local",
        };

        vm.EnumerateCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.True(vm.HasError);
        Assert.Contains("boom", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelCommand_WhileBusy_CancelsAndStops()
    {
        var service = new BlockingSmbService();
        var vm = new SmbEnumeratorViewModel(service)
        {
            HostInput = "server.local",
        };

        vm.EnumerateCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsBusy);
        vm.CancelCommand.Execute(null);
        service.Release();
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void SetGateway_ForwardsToService()
    {
        var service = new FakeSmbService();
        var vm = new SmbEnumeratorViewModel(service);
        var gateway = new SshGatewayDto { Name = "gw", Host = "jump", Port = 22 };

        vm.SetGateway(gateway);

        Assert.Same(gateway, service.LastGateway);
    }

    [Fact]
    public async Task LocaleChange_AfterResults_RebuildsReportAndFindings()
    {
        var service = new FakeSmbService
        {
            Outcome = new SmbEnumOutcome(
                SmbEnumerationEngine.BuildResult(
                    null,
                    new SmbNegotiateInfo("guid", 0x0311, true, true, null, null, 0),
                    null,
                    null,
                    null,
                    false),
                null,
                null),
        };
        var en = await CreateLocalizerAsync("en");
        var fr = await CreateLocalizerAsync("fr");
        var vm = new SmbEnumeratorViewModel(service)
        {
            HostInput = "server.local",
        };
        vm.UpdateLocalizer(en);

        vm.EnumerateCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.HasResults);
        var englishFinding = vm.Findings[0].Text;
        var englishReport = vm.LastReport;

        vm.UpdateLocalizer(fr);

        Assert.NotEqual(englishFinding, vm.Findings[0].Text);
        Assert.NotEqual(englishReport, vm.LastReport);
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 2000)
    {
        var timeoutAt = Environment.TickCount64 + timeoutMs;
        while (!predicate())
        {
            if (Environment.TickCount64 > timeoutAt)
            {
                throw new TimeoutException("Condition was not met before timeout.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FakeSmbService : ISmbEnumerationService
    {
        public SmbEnumOutcome Outcome { get; set; } = new(null, "ToolSmbErrorConnection", null);
        public Exception? ThrowOnEnumerate { get; set; }
        public SshGatewayDto? LastGateway { get; private set; }

        public Task<SmbEnumOutcome> EnumerateAsync(SmbEnumInputs inputs, CancellationToken ct)
        {
            if (ThrowOnEnumerate is not null)
            {
                return Task.FromException<SmbEnumOutcome>(ThrowOnEnumerate);
            }

            return Task.FromResult(Outcome);
        }

        public void SetGateway(SshGatewayDto? gateway)
        {
            LastGateway = gateway;
        }
    }

    private sealed class BlockingSmbService : ISmbEnumerationService
    {
        private readonly TaskCompletionSource<object?> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SmbEnumOutcome> EnumerateAsync(SmbEnumInputs inputs, CancellationToken ct)
        {
            using var registration = ct.Register(() => _gate.TrySetCanceled(ct));
            await _gate.Task;
            return new SmbEnumOutcome(
                SmbEnumerationEngine.BuildTunnelResult(new SmbTunnelObservations(
                    Domain: null,
                    OsInfo: null,
                    ServerName: "HOST",
                    RpcServerName: null,
                    RpcOsVersion: null,
                    NetBiosName: null,
                    NetBiosDomain: null,
                    MacAddress: null,
                    HasAnyData: true)),
                null,
                null);
        }

        public void SetGateway(SshGatewayDto? gateway)
        {
        }

        public void Release()
        {
            _gate.TrySetResult(null);
        }
    }
}
