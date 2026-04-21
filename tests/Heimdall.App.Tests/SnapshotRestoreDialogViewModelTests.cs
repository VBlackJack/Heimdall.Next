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
using Heimdall.App.Services.SessionSnapshot;
using Heimdall.App.ViewModels;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class SnapshotRestoreDialogViewModelTests
{
    [Fact]
    public async Task RestoreSelected_ReturnsOnlyCheckedEntries()
    {
        var vm = await CreateViewModelAsync();
        vm.Sessions[1].IsSelected = false;

        vm.RestoreSelectedCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal(SnapshotRestoreDialogAction.RestoreSelected, vm.Result!.Action);
        Assert.Equal(2, vm.Result.Sessions.Count);
        Assert.DoesNotContain(vm.Result.Sessions, session => session.Order == 1);
    }

    [Fact]
    public async Task RestoreSelected_WhenNothingChecked_DoesNothing()
    {
        var vm = await CreateViewModelAsync();
        vm.AllSelected = false;

        var canExecute = vm.RestoreSelectedCommand.CanExecute(null);
        vm.RestoreSelectedCommand.Execute(null);

        Assert.False(canExecute);
        Assert.Null(vm.Result);
    }

    [Fact]
    public async Task DontRestore_SetsDontRestoreResult()
    {
        var vm = await CreateViewModelAsync();

        vm.DontRestoreCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal(SnapshotRestoreDialogAction.DontRestore, vm.Result!.Action);
        Assert.Empty(vm.Result.Sessions);
    }

    [Fact]
    public async Task ToggleSelectAll_UpdatesAllRows()
    {
        var vm = await CreateViewModelAsync();

        vm.AllSelected = false;

        Assert.All(vm.Sessions, session => Assert.False(session.IsSelected));

        vm.AllSelected = true;

        Assert.All(vm.Sessions, session => Assert.True(session.IsSelected));
    }

    [Fact]
    public async Task UnknownServer_UsesFallbackLabel()
    {
        var localizer = await CreateLocalizerAsync("en");
        var unknownId = Guid.NewGuid().ToString();
        var snapshot = CreateSnapshot(
            new SessionSnapshotEntry { ServerId = unknownId, ConnectionType = "SSH", Order = 0 });
        var vm = new SnapshotRestoreDialogViewModel(localizer, snapshot, []);

        Assert.Equal(
            localizer.Format("DialogSnapshotRestoreUnknownServer", unknownId),
            vm.Sessions[0].DisplayName);
    }

    private static async Task<SnapshotRestoreDialogViewModel> CreateViewModelAsync()
    {
        var localizer = await CreateLocalizerAsync("en");
        return new SnapshotRestoreDialogViewModel(localizer, CreateSnapshot(), CreateServers());
    }

    private static SessionSnapshotFile CreateSnapshot(params SessionSnapshotEntry[]? entries)
    {
        return new SessionSnapshotFile
        {
            SavedAtUtc = new DateTime(2026, 4, 19, 12, 34, 56, DateTimeKind.Utc),
            Sessions = entries is { Length: > 0 }
                ? [.. entries]
                : [
                    new SessionSnapshotEntry { ServerId = "9bfc78bc-90e1-47c8-8bf5-a4320278950e", ConnectionType = "SSH", Order = 0 },
                    new SessionSnapshotEntry { ServerId = "4d8f7af2-00a1-446c-bc30-6ca9992cbc8e", ConnectionType = "RDP", Order = 1 },
                    new SessionSnapshotEntry { ServerId = "6e51b2f8-e718-432b-8739-e18eb469c00e", ConnectionType = "SFTP", Order = 2 },
                ]
        };
    }

    private static IEnumerable<ServerItemViewModel> CreateServers()
    {
        return
        [
            new ServerItemViewModel { Id = "9bfc78bc-90e1-47c8-8bf5-a4320278950e", DisplayName = "Alpha" },
            new ServerItemViewModel { Id = "4d8f7af2-00a1-446c-bc30-6ca9992cbc8e", DisplayName = "Bravo" },
            new ServerItemViewModel { Id = "6e51b2f8-e718-432b-8739-e18eb469c00e", DisplayName = "Charlie" },
        ];
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }
}
