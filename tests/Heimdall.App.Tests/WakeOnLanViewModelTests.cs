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

using System.Globalization;
using System.IO;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;
using Heimdall.Core.Network;

namespace Heimdall.App.Tests;

public sealed class WakeOnLanViewModelTests
{
    [Fact]
    public void DefaultValues_AreInitialized()
    {
        var vm = new WakeOnLanViewModel(new FakeWakeOnLanService());

        Assert.Equal(string.Empty, vm.MacAddress);
        Assert.Equal(WakeOnLanViewModel.DefaultBroadcastAddress, vm.BroadcastAddress);
        Assert.Equal(WakeOnLanViewModel.DefaultPort.ToString(CultureInfo.InvariantCulture), vm.Port);
        Assert.Equal(WakeOnLanStatusKind.None, vm.StatusKind);
        Assert.Equal(string.Empty, vm.History);
    }

    [Fact]
    public async Task SendCommand_InvalidMac_ShowsLocalizedError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new WakeOnLanViewModel(new FakeWakeOnLanService());
        vm.Initialize(localizer);
        vm.MacAddress = "bad";

        vm.SendCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.Equal(WakeOnLanStatusKind.Error, vm.StatusKind);
        Assert.Equal(localizer["ToolWolErrorInvalidMac"], vm.StatusText);
    }

    [Fact]
    public async Task SendCommand_InvalidBroadcast_ShowsLocalizedError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new WakeOnLanViewModel(new FakeWakeOnLanService());
        vm.Initialize(localizer);
        vm.MacAddress = "AA:BB:CC:DD:EE:FF";
        vm.BroadcastAddress = "bad ip";

        vm.SendCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.Equal(WakeOnLanStatusKind.Error, vm.StatusKind);
        Assert.Equal(localizer["ToolWolErrorInvalidBroadcast"], vm.StatusText);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("99999")]
    [InlineData("abc")]
    public async Task SendCommand_InvalidPort_FallsBackToDefault(string port)
    {
        var service = new FakeWakeOnLanService
        {
            ResultFactory = request => WakeOnLanResult.Sent("AA:BB:CC:DD:EE:FF", request.BroadcastAddress, request.Port),
        };
        var vm = new WakeOnLanViewModel(service)
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Port = port,
        };

        vm.SendCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.StatusKind == WakeOnLanStatusKind.Sent);

        Assert.NotNull(service.LastRequest);
        Assert.Equal(WakeOnLanViewModel.DefaultPort, service.LastRequest!.Port);
        Assert.Equal(WakeOnLanViewModel.DefaultPort.ToString(CultureInfo.InvariantCulture), vm.Port);
    }

    [Fact]
    public async Task SendCommand_Success_UpdatesStatusAndHistory()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWakeOnLanService
        {
            ResultFactory = _ => WakeOnLanResult.Sent("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9),
        };
        var vm = new WakeOnLanViewModel(service);
        vm.Initialize(localizer);
        vm.MacAddress = "AA-BB-CC-DD-EE-FF";

        vm.SendCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.StatusKind == WakeOnLanStatusKind.Sent);

        Assert.Contains("AA:BB:CC:DD:EE:FF", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("AA:BB:CC:DD:EE:FF", vm.History, StringComparison.Ordinal);
        Assert.Contains("255.255.255.255:9", vm.History, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendCommand_AppendsHistoryNewestFirst()
    {
        var service = new FakeWakeOnLanService
        {
            ResultFactory = _ => WakeOnLanResult.Sent("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9),
        };
        var vm = new WakeOnLanViewModel(service)
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
        };

        vm.SendCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.StatusKind == WakeOnLanStatusKind.Sent);
        var firstHistory = vm.History;

        vm.MacAddress = "11:22:33:44:55:66";
        service.ResultFactory = _ => WakeOnLanResult.Sent("11:22:33:44:55:66", "255.255.255.255", 9);
        vm.SendCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.History.Contains(Environment.NewLine, StringComparison.Ordinal));

        var lines = vm.History.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("[", lines[0], StringComparison.Ordinal);
        Assert.Contains("11:22:33:44:55:66", lines[0], StringComparison.Ordinal);
        Assert.Equal(firstHistory, lines[1]);
    }

    [Fact]
    public async Task SendCommand_ServiceError_ShowsLocalizedSocketMessage()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new FakeWakeOnLanService
        {
            ResultFactory = _ => WakeOnLanResult.Error("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9, "ToolWolErrorSocket", "boom"),
        };
        var vm = new WakeOnLanViewModel(service);
        vm.Initialize(localizer);
        vm.MacAddress = "AA:BB:CC:DD:EE:FF";

        vm.SendCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.StatusKind == WakeOnLanStatusKind.Error);

        Assert.Contains("boom", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendCommand_SetsSendingStatusWhileBusy()
    {
        var service = new BlockingWakeOnLanService();
        var vm = new WakeOnLanViewModel(service)
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
        };

        vm.SendCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsBusy);

        Assert.Equal(WakeOnLanStatusKind.Sending, vm.StatusKind);
        Assert.False(vm.SendCommand.CanExecute(null));

        service.Release(WakeOnLanResult.Sent("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9));
        await WaitUntilAsync(() => !vm.IsBusy);
    }

    [Fact]
    public async Task UpdateLocalizer_ReprojectsSuccessStatus()
    {
        var en = await CreateLocalizerAsync("en");
        var fr = await CreateLocalizerAsync("fr");
        var service = new FakeWakeOnLanService
        {
            ResultFactory = _ => WakeOnLanResult.Sent("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9),
        };
        var vm = new WakeOnLanViewModel(service);
        vm.Initialize(en);
        vm.MacAddress = "AA:BB:CC:DD:EE:FF";

        vm.SendCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.StatusKind == WakeOnLanStatusKind.Sent);
        var english = vm.StatusText;

        vm.UpdateLocalizer(fr);

        Assert.NotEqual(english, vm.StatusText);
        Assert.Contains("AA:BB:CC:DD:EE:FF", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateLocalizer_ReprojectsErrorStatus()
    {
        var en = await CreateLocalizerAsync("en");
        var fr = await CreateLocalizerAsync("fr");
        var service = new FakeWakeOnLanService
        {
            ResultFactory = _ => WakeOnLanResult.Error("AA:BB:CC:DD:EE:FF", "255.255.255.255", 9, "ToolWolErrorSocket", "boom"),
        };
        var vm = new WakeOnLanViewModel(service);
        vm.Initialize(en);
        vm.MacAddress = "AA:BB:CC:DD:EE:FF";

        vm.SendCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy && vm.StatusKind == WakeOnLanStatusKind.Error);
        var english = vm.StatusText;

        vm.UpdateLocalizer(fr);

        Assert.NotEqual(english, vm.StatusText);
        Assert.Contains("boom", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocaleChanged_Event_ReprojectsHelpText()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new WakeOnLanViewModel(new FakeWakeOnLanService());
        vm.Initialize(localizer);
        var englishHelp = vm.HelpText;

        await localizer.SwitchLocaleAsync("fr");

        Assert.NotEqual(englishHelp, vm.HelpText);
    }

    [Fact]
    public void ToggleHelpCommand_FlipsVisibility()
    {
        var vm = new WakeOnLanViewModel(new FakeWakeOnLanService());

        Assert.False(vm.IsHelpVisible);
        vm.ToggleHelpCommand.Execute(null);
        Assert.True(vm.IsHelpVisible);
        vm.ToggleHelpCommand.Execute(null);
        Assert.False(vm.IsHelpVisible);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var vm = new WakeOnLanViewModel(new FakeWakeOnLanService());

        vm.Dispose();
        vm.Dispose();
    }

    [Fact]
    public void Dispose_ThenSend_HasNoSideEffects()
    {
        var service = new FakeWakeOnLanService();
        var vm = new WakeOnLanViewModel(service)
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
        };

        vm.Dispose();
        vm.SendCommand.Execute(null);

        Assert.Null(service.LastRequest);
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

    private sealed class FakeWakeOnLanService : IWakeOnLanService
    {
        public Func<WakeOnLanRequest, WakeOnLanResult> ResultFactory { get; set; } =
            _ => WakeOnLanResult.Sent("AA:BB:CC:DD:EE:FF", WakeOnLanViewModel.DefaultBroadcastAddress, WakeOnLanViewModel.DefaultPort);

        public WakeOnLanRequest? LastRequest { get; private set; }

        public Task<WakeOnLanResult> SendAsync(WakeOnLanRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(ResultFactory(request));
        }
    }

    private sealed class BlockingWakeOnLanService : IWakeOnLanService
    {
        private readonly TaskCompletionSource<WakeOnLanResult> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WakeOnLanResult> SendAsync(WakeOnLanRequest request, CancellationToken ct)
        {
            return _gate.Task;
        }

        public void Release(WakeOnLanResult result) => _gate.TrySetResult(result);
    }
}
