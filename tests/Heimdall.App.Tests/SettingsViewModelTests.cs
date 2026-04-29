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
using Heimdall.App.Services.Import;
using Heimdall.App.Services.PostConnect;
using Heimdall.App.ViewModels;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.App.ViewModels.Settings;
using Heimdall.Core.Configuration;
using Heimdall.Core.Import;
using Heimdall.Core.Localization;
using Heimdall.Core.Ssh;
using Heimdall.Ssh;

namespace Heimdall.App.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task SaveThenLoad_PreservesNewRdpRedirectionDefaults()
    {
        var config = new FakeConfigManager();
        var viewModel = CreateViewModel(config);
        viewModel.RdpDefaultRedirectComPorts = true;
        viewModel.RdpDefaultRedirectSmartCards = true;
        viewModel.RdpDefaultRedirectWebcam = true;
        viewModel.RdpDefaultRedirectUsb = true;
        viewModel.RdpDefaultAudioCapture = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var saved = Assert.IsType<AppSettings>(config.SavedSettings);
        Assert.True(saved.RdpDefaultRedirectComPorts);
        Assert.True(saved.RdpDefaultRedirectSmartCards);
        Assert.True(saved.RdpDefaultRedirectWebcam);
        Assert.True(saved.RdpDefaultRedirectUsb);
        Assert.True(saved.RdpDefaultAudioCapture);

        var reloaded = CreateViewModel(new FakeConfigManager());
        reloaded.LoadFromSettings(saved);

        Assert.True(reloaded.RdpDefaultRedirectComPorts);
        Assert.True(reloaded.RdpDefaultRedirectSmartCards);
        Assert.True(reloaded.RdpDefaultRedirectWebcam);
        Assert.True(reloaded.RdpDefaultRedirectUsb);
        Assert.True(reloaded.RdpDefaultAudioCapture);
    }

    [Fact]
    public void NewRdpRedirectionDefaults_AreFalseByDefault()
    {
        var settings = new AppSettings();

        Assert.False(settings.RdpDefaultRedirectComPorts);
        Assert.False(settings.RdpDefaultRedirectSmartCards);
        Assert.False(settings.RdpDefaultRedirectWebcam);
        Assert.False(settings.RdpDefaultRedirectUsb);
        Assert.False(settings.RdpDefaultAudioCapture);
    }

    [Fact]
    public void RdpResolutionPresets_DefaultMatchesBuiltInSet()
    {
        var settings = new AppSettings();

        Assert.Equal(10, settings.RdpResolutionPresets.Length);
        Assert.Equal("1920x1080", settings.RdpResolutionPresets[0]);
        Assert.Contains("3840x2160", settings.RdpResolutionPresets);
    }

    [Fact]
    public async Task SaveThenLoad_PreservesResolutionPresetsAndAdvancedTimeouts()
    {
        var config = new FakeConfigManager();
        var viewModel = CreateViewModel(config);
        viewModel.RdpResolutionPresets = ["2560x1080", "3440x1440"];
        viewModel.RdpResizeEnableDelayMs = 5000;
        viewModel.RdpArtifactCleanupDelayMs = 7000;
        viewModel.RdpCredentialAutofillTimeoutMs = 60000;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var saved = Assert.IsType<AppSettings>(config.SavedSettings);
        Assert.Equal(new[] { "2560x1080", "3440x1440" }, saved.RdpResolutionPresets);
        Assert.Equal(5000, saved.RdpResizeEnableDelayMs);
        Assert.Equal(7000, saved.RdpArtifactCleanupDelayMs);
        Assert.Equal(60000, saved.RdpCredentialAutofillTimeoutMs);

        var reloaded = CreateViewModel(new FakeConfigManager());
        reloaded.LoadFromSettings(saved);

        Assert.Equal(new[] { "2560x1080", "3440x1440" }, reloaded.RdpResolutionPresets);
        Assert.Equal(5000, reloaded.RdpResizeEnableDelayMs);
        Assert.Equal(7000, reloaded.RdpArtifactCleanupDelayMs);
        Assert.Equal(60000, reloaded.RdpCredentialAutofillTimeoutMs);
    }

    private static SettingsViewModel CreateViewModel(FakeConfigManager config)
    {
        var localizer = new LocalizationManager();
        var dialog = new FakeDialogService();
        var trustedHostKeys = new TrustedHostKeysSettingsViewModel(
            new HostKeyTrustService(new HostKeyStore()),
            () => new KnownHostsImportReport(0, 0, []),
            () => new KnownHostsExportReport(0, 0, 0),
            localizer,
            dialog,
            new FakeClipboardService(),
            new FakeUiDispatcher());

        return new SettingsViewModel(config, localizer, dialog, trustedHostKeys);
    }

    private sealed class FakeConfigManager : IConfigManager
    {
        public AppSettings Settings { get; set; } = new();

        public AppSettings? SavedSettings { get; private set; }

        public string ConfigPath => "config";

        public string SettingsPath => "settings.json";

        public string ServersPath => "servers.json";

        public event Action<AppSettings>? SettingsChanged;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<AppSettings> LoadSettingsAsync() => Task.FromResult(Settings);

        public Task SaveSettingsAsync(AppSettings settings)
        {
            SavedSettings = settings;
            Settings = settings;
            SettingsChanged?.Invoke(settings);
            return Task.CompletedTask;
        }

        public Task<bool> MergeHostKeyAsync(string hostPortKey, string fingerprint)
            => Task.FromResult(false);

        public Task<int> MergeTrustedHostKeysAsync(IEnumerable<KeyValuePair<string, string>> entries)
            => Task.FromResult(0);

        public Task MergeSettingAsync(Action<AppSettings> mutate)
        {
            mutate(Settings);
            SavedSettings = Settings;
            SettingsChanged?.Invoke(Settings);
            return Task.CompletedTask;
        }

        public Task<List<ServerProfileDto>> LoadServersAsync()
            => Task.FromResult(new List<ServerProfileDto>());

        public Task SaveServersAsync(List<ServerProfileDto> servers)
            => Task.CompletedTask;
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public void SetText(string text)
        {
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task<bool> ShowConfirmAsync(string title, string message, string severity = "info")
            => Task.FromResult(false);

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

        public Task<SnapshotRestoreDialogResult?> ShowSnapshotRestoreDialogAsync(SnapshotRestoreDialogViewModel viewModel)
            => Task.FromResult<SnapshotRestoreDialogResult?>(null);

        public Task<RdpImportSelection?> ShowRdpImportDialogAsync(RdpImportDialogViewModel viewModel)
            => Task.FromResult<RdpImportSelection?>(null);

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
