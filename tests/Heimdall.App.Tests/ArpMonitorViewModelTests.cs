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

namespace Heimdall.App.Tests;

public sealed class ArpMonitorViewModelTests
{
    [Fact]
    public async Task RefreshCommand_PopulatesEntriesFromReader()
    {
        var vm = CreateViewModel(Map("192.168.1.10", "AA-BB-CC-DD-EE-FF"));
        vm.Initialize(await CreateLocalizerAsync("en"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Single(vm.Entries);
        var entry = vm.Entries[0];
        Assert.Equal("192.168.1.10", entry.Ip);
        Assert.Equal("AA-BB-CC-DD-EE-FF", entry.Mac);
        Assert.Equal("new", entry.Status);
        Assert.Equal("New", entry.StatusDisplay);
        Assert.True(vm.HasResults);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task RefreshCommand_MarksExistingEntryStable()
    {
        var vm = CreateViewModel(
            Map("192.168.1.10", "AA-BB-CC-DD-EE-FF"),
            Map("192.168.1.10", "AA-BB-CC-DD-EE-FF"));
        vm.Initialize(await CreateLocalizerAsync("en"));

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);

        var entry = Assert.Single(vm.Entries);
        Assert.Equal("stable", entry.Status);
        Assert.Equal("Stable", entry.StatusDisplay);
    }

    [Fact]
    public async Task RefreshCommand_MarksChangedMacAsChangedAndRaisesAlert()
    {
        var vm = CreateViewModel(
            Map("192.168.1.10", "AA-BB-CC-DD-EE-FF"),
            Map("192.168.1.10", "11-22-33-44-55-66"));
        vm.Initialize(await CreateLocalizerAsync("en"));
        ArpMacChangedEventArgs? alert = null;
        vm.MacChangedDetected += (_, args) => alert = args;

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);

        var entry = Assert.Single(vm.Entries);
        Assert.Equal("changed", entry.Status);
        Assert.Equal("Changed", entry.StatusDisplay);
        Assert.Equal("AA-BB-CC-DD-EE-FF", entry.PreviousMac);
        Assert.NotNull(alert);
        Assert.Equal("192.168.1.10", alert!.Ip);
        Assert.Equal("AA-BB-CC-DD-EE-FF", alert.PreviousMac);
        Assert.Equal("11-22-33-44-55-66", alert.NewMac);
    }

    [Fact]
    public async Task RefreshCommand_MarksRemovedEntriesAsGone()
    {
        var vm = CreateViewModel(
            Map("192.168.1.10", "AA-BB-CC-DD-EE-FF"),
            new Dictionary<string, string>());
        vm.Initialize(await CreateLocalizerAsync("en"));

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);

        var entry = Assert.Single(vm.Entries);
        Assert.Equal("gone", entry.Status);
        Assert.Equal("Gone", entry.StatusDisplay);
    }

    [Fact]
    public async Task RefreshCommand_WhenReaderThrows_SetsErrorState()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new ArpMonitorViewModel(new FakeArpTableReader(new InvalidOperationException("boom")));
        vm.Initialize(localizer);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.False(vm.HasResults);
        Assert.Equal(localizer["ToolArpErrorReadFailed"], vm.ErrorMessage);
    }

    [Fact]
    public async Task RefreshCommand_UpdatesSummaryTexts()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = CreateViewModel(
            Map("192.168.1.10", "AA-BB-CC-DD-EE-FF"),
            Map("192.168.1.11", "11-22-33-44-55-66"));
        vm.Initialize(localizer);

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(string.Format(localizer["ToolArpTotal"], 2), vm.TotalText);
        Assert.StartsWith(string.Format(localizer["ToolArpLastRefresh"], string.Empty), vm.LastRefreshText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_RunsImmediateRefreshAndSetsRunning()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = CreateViewModel(Map("192.168.1.10", "AA-BB-CC-DD-EE-FF"));
        vm.Initialize(localizer);

        await vm.StartAsync(60000);

        Assert.True(vm.IsRunning);
        Assert.Single(vm.Entries);
        Assert.Equal(string.Format(localizer["ToolArpTotal"], 1), vm.TotalText);

        vm.Stop();
    }

    [Fact]
    public void Stop_ClearsRunningStateAndRestoresEmptyStateText()
    {
        var vm = CreateViewModel();
        vm.Initialize(null);

        vm.Stop();

        Assert.False(vm.IsRunning);
        Assert.Equal("ToolArpEmptyState", vm.EmptyStateText);
    }

    private static ArpMonitorViewModel CreateViewModel(params IReadOnlyDictionary<string, string>[] snapshots)
        => new(new FakeArpTableReader(snapshots));

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    private static IReadOnlyDictionary<string, string> Map(string ip, string mac)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ip] = mac
        };

    private sealed class FakeArpTableReader : IArpTableReader
    {
        private readonly Queue<IReadOnlyDictionary<string, string>> _snapshots = [];
        private readonly Exception? _exception;

        public FakeArpTableReader(params IReadOnlyDictionary<string, string>[] snapshots)
        {
            foreach (var snapshot in snapshots)
            {
                _snapshots.Enqueue(snapshot);
            }
        }

        public FakeArpTableReader(Exception exception)
        {
            _exception = exception;
        }

        public Task<IReadOnlyDictionary<string, string>> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_exception is not null)
            {
                return Task.FromException<IReadOnlyDictionary<string, string>>(_exception);
            }

            var next = _snapshots.Count > 0 ? _snapshots.Dequeue() : new Dictionary<string, string>();
            return Task.FromResult(next);
        }
    }
}
