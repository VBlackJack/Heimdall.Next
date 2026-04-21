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
using Heimdall.Core.Localization;
using Heimdall.Core.SystemInfo;

namespace Heimdall.App.Tests;

public sealed class ServiceStatusViewModelTests
{
    [Fact]
    public async Task Initialize_PopulatesHelpAndWatermark()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new ServiceStatusViewModel(new FakeServiceStatusService());

        vm.Initialize(localizer);

        Assert.False(string.IsNullOrWhiteSpace(vm.HelpText));
        Assert.Equal(localizer["ToolWatermarkServiceFilter"], vm.FilterWatermark);
    }

    [Fact]
    public async Task RefreshCommand_LoadsServicesAndCounts()
    {
        var vm = new ServiceStatusViewModel(new FakeServiceStatusService());

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.DisplayedServices.Count);
        Assert.True(vm.HasRefreshSnapshot);
        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(1, vm.RunningCount);
        Assert.Equal(1, vm.StoppedCount);
    }

    [Fact]
    public async Task FilterText_FiltersByNameAndDisplayName()
    {
        var vm = new ServiceStatusViewModel(new FakeServiceStatusService());
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.FilterText = "Windows";
        Assert.Equal(2, vm.DisplayedServices.Count);

        vm.FilterText = "bits";
        Assert.Single(vm.DisplayedServices);
        Assert.Equal("bits", vm.DisplayedServices[0].Name);
    }

    [Fact]
    public async Task RunningOnly_FiltersDisplayedButNotCounts()
    {
        var vm = new ServiceStatusViewModel(new FakeServiceStatusService());
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.RunningOnly = true;

        Assert.Single(vm.DisplayedServices);
        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(1, vm.RunningCount);
    }

    [Fact]
    public async Task RefreshCommand_ReplacesPreviousResults()
    {
        var service = new FakeServiceStatusService();
        var vm = new ServiceStatusViewModel(service);
        await vm.RefreshCommand.ExecuteAsync(null);

        service.Entries =
        [
            new ServiceEntry("new", "New Service", "Running", "Automatic"),
        ];

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Single(vm.DisplayedServices);
        Assert.Equal("new", vm.DisplayedServices[0].Name);
    }

    [Fact]
    public async Task RefreshCommand_Timeout_ShowsLocalizedError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new ServiceStatusViewModel(new ThrowingServiceStatusService(new OperationCanceledException()));
        vm.Initialize(localizer);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Equal(localizer["ToolServicesErrorTimeout"], vm.ErrorText);
    }

    [Fact]
    public async Task RefreshCommand_GenericError_ShowsFormattedMessage()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new ServiceStatusViewModel(new ThrowingServiceStatusService(new InvalidOperationException("boom")));
        vm.Initialize(localizer);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Contains("boom", vm.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshCommand_IsBusyWhileLoading()
    {
        var service = new BlockingServiceStatusService();
        var vm = new ServiceStatusViewModel(service);

        var refreshTask = vm.RefreshCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => vm.IsBusy);

        Assert.False(vm.RefreshCommand.CanExecute(null));

        service.Release([]);
        await refreshTask;
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task CopyResultsCommand_RaisesSanitizedPayloadWithoutHeader()
    {
        var vm = new ServiceStatusViewModel(new FakeServiceStatusService());
        await vm.RefreshCommand.ExecuteAsync(null);

        string? payload = null;
        vm.CopyResultsRequested += (_, text) => payload = text;

        vm.CopyResultsCommand.Execute(null);

        Assert.NotNull(payload);
        Assert.DoesNotContain("Service Name", payload, StringComparison.Ordinal);
        Assert.Contains("bits\tBackground Intelligent Transfer Service\tStopped\tManual", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyResultsCommand_UsesFilteredDisplayedServicesOnly()
    {
        var vm = new ServiceStatusViewModel(new FakeServiceStatusService());
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.FilterText = "bits";

        string? payload = null;
        vm.CopyResultsRequested += (_, text) => payload = text;
        vm.CopyResultsCommand.Execute(null);

        Assert.NotNull(payload);
        Assert.DoesNotContain("w32time", payload, StringComparison.Ordinal);
        Assert.Contains("bits", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartServiceCommand_LaunchesAndReloads()
    {
        var service = new FakeServiceStatusService();
        var vm = new ServiceStatusViewModel(service);
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.StartServiceCommand.ExecuteAsync(vm.DisplayedServices[0]);

        Assert.Single(service.StartCalls);
        Assert.True(service.LoadCalls >= 2);
    }

    [Fact]
    public async Task StopServiceCommand_NullParameter_DoesNothing()
    {
        var service = new FakeServiceStatusService();
        var vm = new ServiceStatusViewModel(service);

        await vm.StopServiceCommand.ExecuteAsync(null);

        Assert.Empty(service.StopCalls);
    }

    [Fact]
    public async Task UpdateLocalizer_ReprojectsHelpWatermarkAndError()
    {
        var en = await CreateLocalizerAsync("en");
        var fr = await CreateLocalizerAsync("fr");
        var vm = new ServiceStatusViewModel(new ThrowingServiceStatusService(new InvalidOperationException("boom")));
        vm.Initialize(en);
        await vm.RefreshCommand.ExecuteAsync(null);
        var englishHelp = vm.HelpText;
        var englishWatermark = vm.FilterWatermark;
        var englishError = vm.ErrorText;

        vm.UpdateLocalizer(fr);

        Assert.NotEqual(englishHelp, vm.HelpText);
        Assert.NotEqual(englishWatermark, vm.FilterWatermark);
        Assert.NotEqual(englishError, vm.ErrorText);
        Assert.Contains("boom", vm.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocaleChanged_ReprojectsHelpAndWatermark()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new ServiceStatusViewModel(new FakeServiceStatusService());
        vm.Initialize(localizer);
        var englishHelp = vm.HelpText;
        var englishWatermark = vm.FilterWatermark;

        await localizer.SwitchLocaleAsync("fr");

        Assert.NotEqual(englishHelp, vm.HelpText);
        Assert.NotEqual(englishWatermark, vm.FilterWatermark);
    }

    [Fact]
    public void ToggleHelpCommand_FlipsVisibility()
    {
        var vm = new ServiceStatusViewModel(new FakeServiceStatusService());

        Assert.False(vm.IsHelpVisible);
        vm.ToggleHelpCommand.Execute(null);
        Assert.True(vm.IsHelpVisible);
        vm.ToggleHelpCommand.Execute(null);
        Assert.False(vm.IsHelpVisible);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var vm = new ServiceStatusViewModel(new FakeServiceStatusService());

        vm.Dispose();
        vm.Dispose();
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromLocaleChanged()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new ServiceStatusViewModel(new FakeServiceStatusService());
        vm.Initialize(localizer);
        var helpBefore = vm.HelpText;

        vm.Dispose();
        await localizer.SwitchLocaleAsync("fr");

        Assert.Equal(helpBefore, vm.HelpText);
    }

    [Fact]
    public async Task Dispose_ThenRefresh_HasNoSideEffects()
    {
        var service = new FakeServiceStatusService();
        var vm = new ServiceStatusViewModel(service);

        vm.Dispose();
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(vm.DisplayedServices);
        Assert.Equal(0, service.LoadCalls);
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

    private sealed class FakeServiceStatusService : IServiceStatusService
    {
        public int LoadCalls { get; private set; }
        public List<string> StartCalls { get; } = [];
        public List<string> StopCalls { get; } = [];
        public List<string> RestartCalls { get; } = [];

        public IReadOnlyList<ServiceEntry> Entries { get; set; } =
        [
            new ServiceEntry("w32time", "Windows Time", "Running", "Automatic"),
            new ServiceEntry("bits", "Background Intelligent Transfer Service", "Stopped", "Manual"),
            new ServiceEntry("eventlog", "Windows Event Log", "Paused", "Automatic"),
        ];

        public Task<IReadOnlyList<ServiceEntry>> LoadAsync(CancellationToken ct)
        {
            LoadCalls++;
            return Task.FromResult(Entries);
        }

        public void StartService(string serviceName) => StartCalls.Add(serviceName);
        public void StopService(string serviceName) => StopCalls.Add(serviceName);
        public void RestartService(string serviceName) => RestartCalls.Add(serviceName);
    }

    private sealed class ThrowingServiceStatusService : IServiceStatusService
    {
        private readonly Exception _exception;

        public ThrowingServiceStatusService(Exception exception)
        {
            _exception = exception;
        }

        public Task<IReadOnlyList<ServiceEntry>> LoadAsync(CancellationToken ct) =>
            Task.FromException<IReadOnlyList<ServiceEntry>>(_exception);

        public void StartService(string serviceName)
        {
        }

        public void StopService(string serviceName)
        {
        }

        public void RestartService(string serviceName)
        {
        }
    }

    private sealed class BlockingServiceStatusService : IServiceStatusService
    {
        private readonly TaskCompletionSource<IReadOnlyList<ServiceEntry>> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<ServiceEntry>> LoadAsync(CancellationToken ct) => _gate.Task;

        public void StartService(string serviceName)
        {
        }

        public void StopService(string serviceName)
        {
        }

        public void RestartService(string serviceName)
        {
        }

        public void Release(IReadOnlyList<ServiceEntry> entries) => _gate.TrySetResult(entries);
    }
}
