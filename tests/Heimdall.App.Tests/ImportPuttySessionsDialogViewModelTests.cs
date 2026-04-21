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
using Heimdall.Core.Import;
using Heimdall.Core.Ssh;

namespace Heimdall.App.Tests;

public sealed class ImportPuttySessionsDialogViewModelTests
{
    [Fact]
    public async Task InitializeAsync_MarksInvalidDisabled_AndDuplicateUnselected()
    {
        using var fixture = await PuttyDialogFixture.CreateAsync([]);
        await fixture.ConfigManager.SaveServersAsync(
        [
            new ServerProfileDto
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = "Prod SSH",
                ConnectionType = "SSH",
                RemoteServer = "existing.example.com"
            }
        ]);

        await fixture.ViewModel.InitializeAsync(new PuttySessionParseResult(
        [
            new PuttySessionCandidate { DisplayName = "Prod SSH", HostName = "prod.example.com" },
            new PuttySessionCandidate { DisplayName = "Broken", HostName = null },
            new PuttySessionCandidate { DisplayName = "Fresh", HostName = "fresh.example.com" }
        ],
        [
            new PuttySessionDiagnostic(PuttyDiagnosticLevel.Warning, PuttyDiagnosticCode.MissingHostName, "Broken")
        ]));

        Assert.Equal(3, fixture.ViewModel.Items.Count);
        Assert.False(fixture.ViewModel.Items[0].IsSelected);
        Assert.False(fixture.ViewModel.Items[1].IsSelectable);
        Assert.False(fixture.ViewModel.Items[1].IsSelected);
        Assert.True(fixture.ViewModel.Items[2].IsSelected);
        Assert.True(fixture.ViewModel.HasDiagnostics);
    }

    [Fact]
    public async Task ConfirmAsync_ImportsOnlySelectableSelectedItems()
    {
        using var fixture = await PuttyDialogFixture.CreateAsync([]);
        await fixture.ViewModel.InitializeAsync(new PuttySessionParseResult(
        [
            new PuttySessionCandidate { DisplayName = "Broken", HostName = null },
            new PuttySessionCandidate { DisplayName = "Fresh", HostName = "fresh.example.com" }
        ],
        []));

        fixture.ViewModel.Items[0].IsSelected = true;
        await fixture.ViewModel.ConfirmCommand.ExecuteAsync(null);

        var servers = await fixture.ConfigManager.LoadServersAsync();
        var imported = Assert.Single(servers);
        Assert.Equal("Fresh", imported.DisplayName);
        Assert.NotNull(fixture.ViewModel.Result);
        Assert.Equal(1, fixture.ViewModel.Result!.ImportedCount);
        Assert.Equal(0, fixture.ViewModel.Result!.SkippedInvalid);
    }

    private sealed class PuttyDialogFixture : IDisposable
    {
        private PuttyDialogFixture(string rootPath, ConfigManager configManager, ImportPuttySessionsDialogViewModel viewModel)
        {
            RootPath = rootPath;
            ConfigManager = configManager;
            ViewModel = viewModel;
        }

        public string RootPath { get; }

        public ConfigManager ConfigManager { get; }

        public ImportPuttySessionsDialogViewModel ViewModel { get; }

        public static async Task<PuttyDialogFixture> CreateAsync(IReadOnlyList<RawPuttySession> sessions)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "heimdall-b61-dialog-tests", Guid.NewGuid().ToString("N"));
            var configManager = new ConfigManager(rootPath);
            await configManager.InitializeAsync();
            var localizer = await CommandLibraryTestHelpers.CreateAppLocalizerAsync();
            var importer = new PuttySessionImporter(new FakePuttySessionRegistrySource(sessions), configManager);
            var viewModel = new ImportPuttySessionsDialogViewModel(localizer, importer);
            return new PuttyDialogFixture(rootPath, configManager, viewModel);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
