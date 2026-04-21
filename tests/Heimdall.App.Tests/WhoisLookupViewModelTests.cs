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

using System.IO;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Network;

namespace Heimdall.App.Tests;

public sealed class WhoisLookupViewModelTests
{
    [Fact]
    public async Task LookupCommand_EmptyDomain_SetsRequiredError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWhoisLookupService();
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(localizer);
        vm.Domain = string.Empty;

        vm.LookupCommand.Execute(null);

        Assert.True(vm.ShowError);
        Assert.Equal(localizer["ToolWhoisErrorDomainRequired"], vm.ErrorText);
        Assert.Null(service.LastRequest);
    }

    [Fact]
    public async Task LookupCommand_WhitespaceDomain_SetsRequiredError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWhoisLookupService();
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(localizer);
        vm.Domain = "   ";

        vm.LookupCommand.Execute(null);

        Assert.True(vm.ShowError);
        Assert.Equal(localizer["ToolWhoisErrorDomainRequired"], vm.ErrorText);
        Assert.Null(service.LastRequest);
    }

    [Fact]
    public async Task LookupCommand_InvalidInput_SetsInvalidDomainError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWhoisLookupService();
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(localizer);
        vm.Domain = "invalid host name";

        vm.LookupCommand.Execute(null);

        Assert.True(vm.ShowError);
        Assert.Equal(localizer["ToolWhoisErrorInvalidDomain"], vm.ErrorText);
        Assert.Null(service.LastRequest);
    }

    [Fact]
    public async Task LookupCommand_ValidDomain_SuccessPopulatesResults()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWhoisLookupService
        {
            Result = WhoisLookupResult.Ok("Registrar: Example", 42),
        };
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(localizer);
        vm.Domain = "example.com";

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.HasResults);

        Assert.Equal("Registrar: Example", vm.Results);
        Assert.Contains("example.com", vm.ResultHeader, StringComparison.Ordinal);
        Assert.Contains("42", vm.StatusText, StringComparison.Ordinal);
        Assert.False(vm.ShowError);
    }

    [Fact]
    public async Task LookupCommand_ValidIpAddress_IsAccepted()
    {
        var service = new FakeWhoisLookupService
        {
            Result = WhoisLookupResult.Ok("NetRange: 8.8.8.0/24", 10),
        };
        var vm = new WhoisLookupViewModel(service)
        {
            Domain = "8.8.8.8",
        };

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.HasResults);

        Assert.NotNull(service.LastRequest);
        Assert.Equal("8.8.8.8", service.LastRequest!.Domain);
    }

    [Fact]
    public async Task LookupCommand_TrimsDomainBeforeServiceCall()
    {
        var service = new FakeWhoisLookupService
        {
            Result = WhoisLookupResult.Ok("ok", 1),
        };
        var vm = new WhoisLookupViewModel(service)
        {
            Domain = "  example.com  ",
        };

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.HasResults);

        Assert.NotNull(service.LastRequest);
        Assert.Equal("example.com", service.LastRequest!.Domain);
    }

    [Fact]
    public async Task LookupCommand_ServiceErrorWithArg_FormatsError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWhoisLookupService
        {
            Result = WhoisLookupResult.Error("ToolWhoisErrorFailed", 9, "boom"),
        };
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(localizer);
        vm.Domain = "example.com";

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.True(vm.ShowError);
        Assert.Contains("boom", vm.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupCommand_ServiceErrorWithoutArg_UsesBareTemplate()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWhoisLookupService
        {
            Result = WhoisLookupResult.Error("ToolWhoisErrorTimeout", 10000),
        };
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(localizer);
        vm.Domain = "example.com";

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.True(vm.ShowError);
        Assert.Equal(localizer["ToolWhoisErrorTimeout"], vm.ErrorText);
    }

    [Fact]
    public async Task LookupCommand_ServiceThrowsOperationCancelled_ShowsTimeout()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWhoisLookupService
        {
            Exception = new OperationCanceledException(),
        };
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(localizer);
        vm.Domain = "example.com";

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.True(vm.ShowError);
        Assert.Equal(localizer["ToolWhoisErrorTimeout"], vm.ErrorText);
    }

    [Fact]
    public async Task LookupCommand_ServiceThrowsException_ShowsFailedError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWhoisLookupService
        {
            Exception = new InvalidOperationException("oops"),
        };
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(localizer);
        vm.Domain = "example.com";

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.True(vm.ShowError);
        Assert.Contains("oops", vm.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelCommand_UserCancellation_ClearsStatusWithoutError()
    {
        var service = new BlockingWhoisLookupService();
        var vm = new WhoisLookupViewModel(service) { Domain = "example.com" };

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsBusy);
        vm.CancelCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.False(vm.ShowError);
        Assert.Equal(string.Empty, vm.StatusText);
    }

    [Fact]
    public async Task LookupCommand_WhileBusy_CanExecuteIsFalse()
    {
        var service = new BlockingWhoisLookupService();
        var vm = new WhoisLookupViewModel(service) { Domain = "example.com" };

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsBusy);

        Assert.False(vm.LookupCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));

        vm.CancelCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);
    }

    [Fact]
    public void ToggleHelpCommand_FlipsHelpVisibility()
    {
        var vm = new WhoisLookupViewModel(new FakeWhoisLookupService());

        Assert.False(vm.IsHelpVisible);
        vm.ToggleHelpCommand.Execute(null);
        Assert.True(vm.IsHelpVisible);
        vm.ToggleHelpCommand.Execute(null);
        Assert.False(vm.IsHelpVisible);
    }

    [Fact]
    public void CopyResultsCommand_IsDisabledWhenNoResults()
    {
        var vm = new WhoisLookupViewModel(new FakeWhoisLookupService());

        Assert.False(vm.CopyResultsCommand.CanExecute(null));
    }

    [Fact]
    public async Task CopyResultsCommand_BecomesEnabledAfterSuccess()
    {
        var service = new FakeWhoisLookupService
        {
            Result = WhoisLookupResult.Ok("ok", 1),
        };
        var vm = new WhoisLookupViewModel(service)
        {
            Domain = "example.com",
        };

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.HasResults);

        Assert.True(vm.CopyResultsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Initialize_WithLocalizer_PopulatesWatermarkAndEmptyState()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new WhoisLookupViewModel(new FakeWhoisLookupService());

        vm.Initialize(localizer);

        Assert.Equal(localizer["ToolWatermarkExampleDomainOrIp"], vm.DomainWatermark);
        Assert.Equal(localizer["ToolWhoisEmptyState"], vm.EmptyStateText);
        Assert.False(string.IsNullOrWhiteSpace(vm.HelpText));
    }

    [Fact]
    public async Task UpdateLocalizer_ReprojectsSuccessHeaderAndStatus()
    {
        var en = await CreateLocalizerAsync("en");
        var fr = await CreateLocalizerAsync("fr");
        var service = new FakeWhoisLookupService
        {
            Result = WhoisLookupResult.Ok("ok", 123),
        };
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(en);
        vm.Domain = "example.com";

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.HasResults);
        var englishHeader = vm.ResultHeader;
        var englishStatus = vm.StatusText;

        vm.UpdateLocalizer(fr);

        Assert.NotEqual(englishHeader, vm.ResultHeader);
        Assert.NotEqual(englishStatus, vm.StatusText);
        Assert.Contains("example.com", vm.ResultHeader, StringComparison.Ordinal);
        Assert.Contains("123", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateLocalizer_ReprojectsErrorWithArgs()
    {
        var en = await CreateLocalizerAsync("en");
        var fr = await CreateLocalizerAsync("fr");
        var service = new FakeWhoisLookupService
        {
            Result = WhoisLookupResult.Error("ToolWhoisErrorFailed", 1, "boom"),
        };
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(en);
        vm.Domain = "example.com";

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);
        var englishError = vm.ErrorText;

        vm.UpdateLocalizer(fr);

        Assert.NotEqual(englishError, vm.ErrorText);
        Assert.Contains("boom", vm.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocaleChanged_Event_RefreshesHelpAndWatermark()
    {
        var en = await CreateLocalizerAsync("en");
        var vm = new WhoisLookupViewModel(new FakeWhoisLookupService());
        vm.Initialize(en);
        var englishHelp = vm.HelpText;
        var englishWatermark = vm.DomainWatermark;

        await en.SwitchLocaleAsync("fr");

        Assert.NotEqual(englishHelp, vm.HelpText);
        Assert.NotEqual(englishWatermark, vm.DomainWatermark);
    }

    [Fact]
    public void SetGateway_ForwardsToService()
    {
        var service = new FakeWhoisLookupService();
        var gateway = new SshGatewayDto { Name = "gw", Host = "jump", Port = 22 };
        var vm = new WhoisLookupViewModel(service);

        vm.SetGateway(gateway);

        Assert.Same(gateway, service.LastGateway);
    }

    [Fact]
    public void SetGateway_Null_ForwardsToService()
    {
        var service = new FakeWhoisLookupService();
        var gateway = new SshGatewayDto { Name = "gw", Host = "jump", Port = 22 };
        var vm = new WhoisLookupViewModel(service);

        vm.SetGateway(gateway);
        vm.SetGateway(null);

        Assert.Null(service.LastGateway);
    }

    [Fact]
    public async Task UpdateLocalizer_SameInstance_IsNoOp()
    {
        var en = await CreateLocalizerAsync("en");
        var vm = new WhoisLookupViewModel(new FakeWhoisLookupService());
        vm.Initialize(en);
        var help = vm.HelpText;

        vm.UpdateLocalizer(en);

        Assert.Equal(help, vm.HelpText);
    }

    [Fact]
    public async Task ResetTransientState_ClearsPreviousErrorOnNextLookup()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWhoisLookupService
        {
            Result = WhoisLookupResult.Error("ToolWhoisErrorFailed", 1, "first"),
        };
        var vm = new WhoisLookupViewModel(service);
        vm.Initialize(localizer);
        vm.Domain = "example.com";

        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);
        Assert.True(vm.ShowError);

        service.Result = WhoisLookupResult.Ok("ok", 2);
        vm.LookupCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.HasResults);

        Assert.False(vm.ShowError);
        Assert.Equal("ok", vm.Results);
    }

    [Fact]
    public void SearchWith_PrefillsDomain()
    {
        var vm = new WhoisLookupViewModel(new FakeWhoisLookupService());

        vm.SearchWith("example.com");

        Assert.Equal("example.com", vm.Domain);
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new WhoisLookupViewModel(new FakeWhoisLookupService());
        vm.Initialize(localizer);

        vm.Dispose();
        vm.Dispose();
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromLocaleChanged()
    {
        var en = await CreateLocalizerAsync("en");
        var vm = new WhoisLookupViewModel(new FakeWhoisLookupService());
        vm.Initialize(en);
        var helpBeforeDispose = vm.HelpText;

        vm.Dispose();
        await en.SwitchLocaleAsync("fr");

        Assert.Equal(helpBeforeDispose, vm.HelpText);
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

    private sealed class FakeWhoisLookupService : IWhoisLookupService
    {
        public WhoisLookupResult Result { get; set; } = WhoisLookupResult.Ok(string.Empty, 0);
        public Exception? Exception { get; set; }
        public WhoisLookupRequest? LastRequest { get; private set; }
        public SshGatewayDto? LastGateway { get; private set; }

        public void SetGateway(SshGatewayDto? gateway) => LastGateway = gateway;

        public Task<WhoisLookupResult> LookupAsync(WhoisLookupRequest request, CancellationToken ct)
        {
            LastRequest = request;
            if (Exception is not null)
            {
                return Task.FromException<WhoisLookupResult>(Exception);
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class BlockingWhoisLookupService : IWhoisLookupService
    {
        private readonly TaskCompletionSource<WhoisLookupResult> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SetGateway(SshGatewayDto? gateway)
        {
        }

        public async Task<WhoisLookupResult> LookupAsync(WhoisLookupRequest request, CancellationToken ct)
        {
            using var registration = ct.Register(() => _gate.TrySetCanceled(ct));
            return await _gate.Task.ConfigureAwait(false);
        }
    }
}
