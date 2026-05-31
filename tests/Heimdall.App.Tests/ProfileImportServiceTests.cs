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
using System.Text.Json;
using Heimdall.App.Services;
using Heimdall.App.Services.Import;
using Heimdall.App.Services.PostConnect;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Configuration;
using Heimdall.Core.Import;
using Heimdall.Core.Localization;
using Heimdall.Core.Ssh;

namespace Heimdall.App.Tests;

public sealed class ProfileImportServiceTests
{
    [Fact]
    public async Task ImportFromPathAsync_Rdp_UsesRichPreviewAndImportsSelection()
    {
        using var fixture = new ProfileImportFixture();
        var path = await fixture.WriteTextAsync("Workstation.rdp", "full address:s:rdp.example.com");

        var result = await fixture.Service.ImportFromPathAsync(path, CancellationToken.None);

        var servers = await fixture.ConfigManager.LoadServersAsync();
        Assert.True(result.HasChanges);
        Assert.Equal(1, result.ImportedCount);
        Assert.Single(servers);
        Assert.Equal("Workstation", servers[0].DisplayName);
        Assert.NotNull(fixture.Dialog.LastRdpImportViewModel);
        Assert.Equal("Import .rdp files", fixture.Dialog.LastRdpImportViewModel.DialogTitle);
    }

    [Fact]
    public async Task ImportFromPathAsync_Json_UsesPreviewAndImportsSelection()
    {
        using var fixture = new ProfileImportFixture();
        var path = await fixture.WriteJsonAsync("servers.json",
        [
            new ServerProfileDto
            {
                Id = "json-import",
                DisplayName = "Json SSH",
                ConnectionType = "SSH",
                RemoteServer = "ssh.example.com",
                SshPort = 22
            }
        ]);

        var result = await fixture.Service.ImportFromPathAsync(path, CancellationToken.None);

        var servers = await fixture.ConfigManager.LoadServersAsync();
        Assert.True(result.HasChanges);
        Assert.Equal(1, result.ImportedCount);
        Assert.Single(servers);
        Assert.Equal("Json SSH", servers[0].DisplayName);
        Assert.NotNull(fixture.Dialog.LastRdpImportViewModel);
        Assert.Equal("Import profiles", fixture.Dialog.LastRdpImportViewModel.DialogTitle);
    }

    [Fact]
    public async Task ImportFromPathAsync_JsonOversized_ReturnsFailureBeforePreviewOrPersistence()
    {
        using ProfileImportFixture fixture = new(maxImportFileSizeBytes: 10);
        string path = await fixture.WriteTextAsync(
            "servers.json",
            "[{\"displayName\":\"Oversized\",\"remoteServer\":\"host.example\"}]");

        ProfileImportResult result = await fixture.Service.ImportFromPathAsync(path, CancellationToken.None);

        List<ServerProfileDto> servers = await fixture.ConfigManager.LoadServersAsync();
        Assert.True(result.IsFailure);
        Assert.Contains("Import file is too large", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("10 B", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(servers);
        Assert.Null(fixture.Dialog.LastRdpImportViewModel);
    }

    [Fact]
    public async Task ImportFromPathAsync_UnknownExtension_ReturnsFailure()
    {
        using var fixture = new ProfileImportFixture();
        var path = await fixture.WriteTextAsync("servers.txt", "ignored");

        var result = await fixture.Service.ImportFromPathAsync(path, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(".txt", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fixture.Dialog.LastRdpImportViewModel);
    }

    private sealed class ProfileImportFixture : IDisposable
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "heimdall-profile-import-tests", Guid.NewGuid().ToString("N"));

        public ConfigManager ConfigManager { get; }

        public TestDialogService Dialog { get; } = new();

        public ProfileImportService Service { get; }

        public ProfileImportFixture(long maxImportFileSizeBytes = AppConstants.MaxImportFileSizeBytes)
        {
            Directory.CreateDirectory(RootPath);
            ConfigManager = new ConfigManager(RootPath);
            LocalizationManager localizer = CreateLocalizerAsync().GetAwaiter().GetResult();
            RdpImportService rdpImport = new(ConfigManager, localizer);
            Service = new ProfileImportService(ConfigManager, localizer, Dialog, rdpImport, maxImportFileSizeBytes);
        }

        public async Task<string> WriteTextAsync(string fileName, string content)
        {
            var path = Path.Combine(RootPath, fileName);
            await File.WriteAllTextAsync(path, content);
            return path;
        }

        public async Task<string> WriteJsonAsync(string fileName, IReadOnlyList<ServerProfileDto> servers)
        {
            var path = Path.Combine(RootPath, fileName);
            var json = JsonSerializer.Serialize(servers, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
            }
        }

        private static async Task<LocalizationManager> CreateLocalizerAsync()
        {
            var manager = new LocalizationManager();
            await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en");
            return manager;
        }
    }

    private sealed class TestDialogService : IDialogService
    {
        public RdpImportDialogViewModel? LastRdpImportViewModel { get; private set; }

        public Task<bool> ShowConfirmAsync(string title, string message, string severity = "info")
            => Task.FromResult(true);

        public Task<bool?> ShowSaveDiscardCancelAsync(string title, string message)
            => Task.FromResult<bool?>(null);

        public Task<string?> ShowInputAsync(string title, string prompt, string? defaultValue = null)
            => Task.FromResult<string?>(null);

        public Task<string?> ShowPasswordInputAsync(string title, string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<ServerDialogResult?> ShowServerDialogAsync(ServerDialogViewModel? editVm = null)
            => Task.FromResult<ServerDialogResult?>(null);

        public Task<GatewayDialogResult?> ShowGatewayDialogAsync(GatewayDialogViewModel? editVm = null)
            => Task.FromResult<GatewayDialogResult?>(null);

        public Task<ProjectDialogResult?> ShowProjectDialogAsync(ProjectDialogViewModel? editVm = null)
            => Task.FromResult<ProjectDialogResult?>(null);

        public Task<ScheduledTaskDialogResult?> ShowScheduledTaskDialogAsync(ScheduledTaskDialogViewModel? editVm = null)
            => Task.FromResult<ScheduledTaskDialogResult?>(null);

        public Task ShowPinDialogAsync(PinDialogViewModel viewModel)
            => Task.CompletedTask;

        public Task<PinSetupResult?> ShowPinSetupDialogAsync(PinSetupDialogViewModel viewModel)
            => Task.FromResult<PinSetupResult?>(null);

        public Task<SnapshotRestoreDialogResult?> ShowSnapshotRestoreDialogAsync(SnapshotRestoreDialogViewModel viewModel)
            => Task.FromResult<SnapshotRestoreDialogResult?>(null);

        public Task<RdpImportSelection?> ShowRdpImportDialogAsync(RdpImportDialogViewModel viewModel)
        {
            LastRdpImportViewModel = viewModel;
            return Task.FromResult<RdpImportSelection?>(new RdpImportSelection
            {
                Entries =
                [
                    .. viewModel.Rows.Select(row => new RdpImportSelectionEntry
                    {
                        SourceFilePath = row.SourceFilePath,
                        IsSelected = !row.HasParseError,
                        ConflictResolution = row.HasNameConflict
                            ? RdpConflictResolution.AutoRename
                            : RdpConflictResolution.Skip
                    })
                ]
            });
        }

        public Task<ImportOutcome?> ShowImportOpenSshConfigAsync(OpenSshParseResult parseResult)
            => Task.FromResult<ImportOutcome?>(null);

        public Task<ImportOutcome?> ShowImportPuttySessionsAsync(PuttySessionParseResult parseResult)
            => Task.FromResult<ImportOutcome?>(null);

        public Task<KnownHostsImportOutcome?> ShowImportKnownHostsAsync(KnownHostsImportPreview preview)
            => Task.FromResult<KnownHostsImportOutcome?>(null);

        public Task ShowTrustedHostKeyDetailsAsync(TrustedHostKeyDetailsDialogViewModel viewModel)
            => Task.CompletedTask;

        public Task<ImportKnownHostsConflictResolution?> ShowImportKnownHostsConflictAsync(
            ImportKnownHostsConflictDialogViewModel viewModel)
            => Task.FromResult<ImportKnownHostsConflictResolution?>(null);

        public Task<CommandLibraryPickerResult?> ShowCommandLibraryPickerAsync(
            CommandLibraryPickerDialogViewModel viewModel,
            AutoPrefillContext? prefillContext = null,
            string? existingActionId = null,
            IReadOnlyDictionary<string, string>? existingValues = null)
            => Task.FromResult<CommandLibraryPickerResult?>(null);

        public Task<int?> ShowBulkEditPortAsync(int count, int? initialPort, CancellationToken cancellationToken)
            => Task.FromResult<int?>(null);

        public Task<string?> ShowBulkEditUsernameAsync(int count, string? initialUsername, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<string?> ShowBulkEditPasswordAsync(int count, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public void ShowError(string title, string message)
        {
        }

        public void ShowInfo(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }
    }
}
