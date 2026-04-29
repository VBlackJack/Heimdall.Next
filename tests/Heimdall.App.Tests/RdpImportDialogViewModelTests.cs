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
using Heimdall.App.Services.Import;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class RdpImportDialogViewModelTests
{
    [Fact]
    public async Task Confirm_ReturnsSelectedEntriesAndResolutions()
    {
        var vm = await CreateViewModelAsync();
        vm.Rows[0].ConflictResolution = RdpConflictResolution.Replace;
        vm.Rows[1].IsSelected = false;

        vm.ConfirmCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Equal(3, vm.Result!.Entries.Count);
        Assert.Contains(vm.Result.Entries, entry => entry.ConflictResolution == RdpConflictResolution.Replace);
        Assert.Contains(
            vm.Result.Entries,
            entry => entry.SourceFilePath.EndsWith("b.rdp", StringComparison.OrdinalIgnoreCase) && !entry.IsSelected);
    }

    [Fact]
    public async Task Confirm_WhenNothingSelected_CannotExecute()
    {
        var vm = await CreateViewModelAsync();
        vm.SelectNoneCommand.Execute(null);

        Assert.False(vm.ConfirmCommand.CanExecute(null));
        vm.ConfirmCommand.Execute(null);
        Assert.Null(vm.Result);
    }

    [Fact]
    public async Task ApplyAllReplace_ChangesConflictRowsOnly()
    {
        var vm = await CreateViewModelAsync();

        vm.ApplyAllReplaceCommand.Execute(null);

        Assert.Equal(RdpConflictResolution.Replace, vm.Rows[0].ConflictResolution);
        Assert.Equal(RdpConflictResolution.Replace, vm.Rows[1].ConflictResolution);
        Assert.Equal(RdpConflictResolution.Skip, vm.Rows[2].ConflictResolution);
    }

    [Fact]
    public async Task ParseErrorRow_StartsDeselected()
    {
        var vm = await CreateViewModelAsync();

        Assert.False(vm.Rows[2].IsSelected);
    }

    [Fact]
    public async Task FileIssues_AreSurfaced()
    {
        var vm = await CreateViewModelAsync();

        Assert.True(vm.HasFileIssues);
        Assert.Contains("not found", vm.FileIssuesText!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectAll_LeavesParseErrorRowsDeselected()
    {
        var vm = await CreateViewModelAsync();
        vm.SelectAllCommand.Execute(null);

        Assert.True(vm.Rows[0].IsSelected);
        Assert.True(vm.Rows[1].IsSelected);
        Assert.False(vm.Rows[2].IsSelected);
    }

    [Fact]
    public async Task RowAccessibleSummary_IncludesSourceParseErrorAndConflict()
    {
        var localizer = await CreateLocalizerAsync();
        var row = new RdpImportRowViewModel(
            new RdpImportPreviewEntry
            {
                SourceFilePath = "C:\\broken.rdp",
                ProposedName = "Broken",
                Candidate = new ServerProfileDto
                {
                    DisplayName = "Broken",
                    RemoteServer = "",
                    RemotePort = 3389,
                    ConnectionType = "RDP"
                },
                HasParseError = true,
                ParseErrorMessage = "invalid host",
                HasNameConflict = true,
                ConflictingExistingName = "Broken"
            },
            localizer);

        Assert.Contains("broken.rdp", row.RowAccessibleSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid host", row.RowAccessibleSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(row.ConflictText, row.RowAccessibleSummary, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<RdpImportDialogViewModel> CreateViewModelAsync()
    {
        var localizer = await CreateLocalizerAsync();
        return new RdpImportDialogViewModel(localizer, new RdpImportPreview
        {
            Entries =
            [
                new RdpImportPreviewEntry
                {
                    SourceFilePath = "C:\\a.rdp",
                    ProposedName = "Alpha",
                    Candidate = new ServerProfileDto { DisplayName = "Alpha", RemoteServer = "a.example.com", RemotePort = 3389, ConnectionType = "RDP" },
                    HasNameConflict = true,
                    ConflictingExistingName = "Alpha"
                },
                new RdpImportPreviewEntry
                {
                    SourceFilePath = "C:\\b.rdp",
                    ProposedName = "Bravo",
                    Candidate = new ServerProfileDto { DisplayName = "Bravo", RemoteServer = "b.example.com", RemotePort = 3389, ConnectionType = "RDP" },
                    HasNameConflict = true,
                    ConflictingExistingName = "Bravo"
                },
                new RdpImportPreviewEntry
                {
                    SourceFilePath = "C:\\c.rdp",
                    ProposedName = "Charlie",
                    Candidate = new ServerProfileDto { DisplayName = "Charlie", RemoteServer = "", RemotePort = 3389, ConnectionType = "RDP" },
                    HasParseError = true,
                    ParseErrorMessage = "invalid"
                }
            ],
            FilesNotFound = ["missing.rdp"],
            FilesUnreadable = []
        });
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync()
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en");
        return manager;
    }
}
