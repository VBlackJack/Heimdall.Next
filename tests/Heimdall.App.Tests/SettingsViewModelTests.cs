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

    [Fact]
    public void CollapseTunnelsPanelByDefault_LoadFromSettings_DefaultIsTrue()
    {
        var viewModel = CreateViewModel(new FakeConfigManager());

        viewModel.LoadFromSettings(new AppSettings());

        Assert.True(viewModel.CollapseTunnelsPanelByDefault);
    }

    [Fact]
    public void SessionHealthMonitorSettings_LoadFromSettings_MirrorsAllFields()
    {
        var viewModel = CreateViewModel(new FakeConfigManager());

        viewModel.LoadFromSettings(new AppSettings
        {
            SessionHealthMonitorEnabled = false,
            SessionHealthCheckIntervalSeconds = 120,
            SessionHealthProbeTimeoutMs = 4000,
            SessionHealthMaxConcurrent = 20
        });

        Assert.False(viewModel.SessionHealthMonitorEnabled);
        Assert.Equal(120, viewModel.SessionHealthCheckIntervalSeconds);
        Assert.Equal(4000, viewModel.SessionHealthProbeTimeoutMs);
        Assert.Equal(20, viewModel.SessionHealthMaxConcurrent);
    }

    [Fact]
    public async Task SessionHealthMonitorSettings_SaveCommand_PersistsAllFieldsToAppSettings()
    {
        var config = new FakeConfigManager();
        var viewModel = CreateViewModel(config);
        viewModel.SessionHealthMonitorEnabled = false;
        viewModel.SessionHealthCheckIntervalSeconds = 90;
        viewModel.SessionHealthProbeTimeoutMs = 3500;
        viewModel.SessionHealthMaxConcurrent = 25;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var saved = Assert.IsType<AppSettings>(config.SavedSettings);
        Assert.False(saved.SessionHealthMonitorEnabled);
        Assert.Equal(90, saved.SessionHealthCheckIntervalSeconds);
        Assert.Equal(3500, saved.SessionHealthProbeTimeoutMs);
        Assert.Equal(25, saved.SessionHealthMaxConcurrent);
    }

    [Fact]
    public async Task SessionHealthCheckInterval_OutOfRange_BlocksSave()
    {
        var config = new FakeConfigManager();
        var viewModel = CreateViewModel(config);
        viewModel.SessionHealthCheckIntervalSeconds = 5; // below the documented 15 s floor

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasErrors);
        Assert.Null(config.SavedSettings);
    }

    [Fact]
    public void CollapseTunnelsPanelByDefault_LoadFromSettings_PreservesFalse()
    {
        var viewModel = CreateViewModel(new FakeConfigManager());

        viewModel.LoadFromSettings(new AppSettings
        {
            CollapseTunnelsPanelByDefault = false
        });

        Assert.False(viewModel.CollapseTunnelsPanelByDefault);
    }

    [Fact]
    public void CollapseTunnelsPanelByDefault_LoadFromSettings_PreservesTrue()
    {
        var viewModel = CreateViewModel(new FakeConfigManager());

        viewModel.LoadFromSettings(new AppSettings
        {
            CollapseTunnelsPanelByDefault = true
        });

        Assert.True(viewModel.CollapseTunnelsPanelByDefault);
    }

    [Fact]
    public async Task SaveAsync_PersistsCollapseTunnelsPanelByDefault_True()
    {
        var config = new FakeConfigManager
        {
            Settings = new AppSettings
            {
                CollapseTunnelsPanelByDefault = false
            }
        };
        var viewModel = CreateViewModel(config);
        viewModel.CollapseTunnelsPanelByDefault = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var saved = Assert.IsType<AppSettings>(config.SavedSettings);
        Assert.True(saved.CollapseTunnelsPanelByDefault);
    }

    [Fact]
    public async Task SaveAsync_PersistsCollapseTunnelsPanelByDefault_False()
    {
        var config = new FakeConfigManager();
        var viewModel = CreateViewModel(config);
        viewModel.CollapseTunnelsPanelByDefault = false;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var saved = Assert.IsType<AppSettings>(config.SavedSettings);
        Assert.False(saved.CollapseTunnelsPanelByDefault);
    }

    [Fact]
    public void FileShareEnableTftp_LoadFromSettings_PreservesFalse()
    {
        var viewModel = CreateViewModel(new FakeConfigManager());

        viewModel.LoadFromSettings(new AppSettings
        {
            FileShareEnableTftp = false
        });

        Assert.False(viewModel.FileShareEnableTftp);
    }

    [Fact]
    public void FileShareEnableTftp_LoadFromSettings_PreservesTrue()
    {
        var viewModel = CreateViewModel(new FakeConfigManager());

        viewModel.LoadFromSettings(new AppSettings
        {
            FileShareEnableTftp = true
        });

        Assert.True(viewModel.FileShareEnableTftp);
    }

    [Fact]
    public async Task SaveAsync_PersistsFileShareEnableTftp()
    {
        var config = new FakeConfigManager();
        var viewModel = CreateViewModel(config);
        viewModel.FileShareEnableTftp = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var saved = Assert.IsType<AppSettings>(config.SavedSettings);
        Assert.True(saved.FileShareEnableTftp);
    }

    [Fact]
    public void CollapseTunnelsPanelByDefault_RaisesPropertyChanged()
    {
        var viewModel = CreateViewModel(new FakeConfigManager());
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        viewModel.CollapseTunnelsPanelByDefault = false;

        Assert.Contains(nameof(SettingsViewModel.CollapseTunnelsPanelByDefault), changes);
    }

    [Fact]
    public async Task ResetRdpDefaultsCommand_RestoresRdpDefaults()
    {
        var dialog = new FakeDialogService { ConfirmResult = true };
        var viewModel = CreateViewModel(new FakeConfigManager(), dialog);
        SetNonDefaultRdpValues(viewModel);

        await viewModel.ResetRdpDefaultsCommand.ExecuteAsync(null);

        var expected = await LoadExpectedFactoryDefaultsAsync();
        AssertRdpDefaultsMatch(viewModel, expected);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public async Task ResetRdpDefaultsCommand_DoesNotTouchUnrelatedProperties()
    {
        var dialog = new FakeDialogService { ConfirmResult = true };
        var viewModel = CreateViewModel(new FakeConfigManager(), dialog);
        viewModel.DefaultTheme = "Buffy";
        viewModel.PlinkPath = @"C:\Tools\plink.exe";
        viewModel.TerminalFontSize = 22;
        SetNonDefaultRdpValues(viewModel);

        await viewModel.ResetRdpDefaultsCommand.ExecuteAsync(null);

        Assert.Equal("Buffy", viewModel.DefaultTheme);
        Assert.Equal(@"C:\Tools\plink.exe", viewModel.PlinkPath);
        Assert.Equal(22, viewModel.TerminalFontSize);
    }

    [Fact]
    public async Task ResetRdpDefaultsCommand_CancelledConfirmationDoesNotModifyState()
    {
        var dialog = new FakeDialogService { ConfirmResult = false };
        var viewModel = CreateViewModel(new FakeConfigManager(), dialog);
        SetNonDefaultRdpValues(viewModel);

        await viewModel.ResetRdpDefaultsCommand.ExecuteAsync(null);

        Assert.Equal(1280, viewModel.DefaultResolutionWidth);
        Assert.Equal(720, viewModel.DefaultResolutionHeight);
        Assert.Equal("External", viewModel.RdpDefaultMode);
        Assert.False(viewModel.RdpDefaultNla);
        Assert.False(viewModel.RdpDefaultRedirectClipboard);
        Assert.False(viewModel.RdpDefaultAutoReconnect);
    }

    [Fact]
    public async Task ApplyRdpModeToAllCommand_OnlyUpdatesRdpProfiles()
    {
        var config = new FakeConfigManager
        {
            Servers =
            [
                new ServerProfileDto { ConnectionType = "RDP", RdpMode = "Embedded" },
                new ServerProfileDto { ConnectionType = "SSH", RdpMode = "Embedded" },
                new ServerProfileDto { ConnectionType = "RDP", RdpMode = "External" }
            ]
        };
        var dialog = new FakeDialogService { ConfirmResult = true };
        var viewModel = CreateViewModel(config, dialog);
        viewModel.RdpDefaultMode = "External";

        await viewModel.ApplyRdpModeToAllCommand.ExecuteAsync(null);

        Assert.NotNull(config.SavedServers);
        Assert.Equal("External", config.Servers[0].RdpMode);
        Assert.Equal("Embedded", config.Servers[1].RdpMode);
        Assert.Equal("External", config.Servers[2].RdpMode);
        var confirm = Assert.Single(dialog.ConfirmCalls);
        Assert.Equal("danger", confirm.Severity);
    }

    [Fact]
    public async Task ImportConfigCommand_RdpDelegatesToProfileImportService()
    {
        var config = new FakeConfigManager();
        var profileImport = new FakeProfileImportService
        {
            Result = new ProfileImportResult { HasChanges = true }
        };
        var viewModel = CreateViewModel(config, profileImportService: profileImport);
        var importPath = Path.Combine(Path.GetTempPath(), "profile.rdp");
        viewModel.ImportFilePathProvider = () => importPath;
        var configurationChanged = false;
        viewModel.ConfigurationChanged += () => configurationChanged = true;

        await viewModel.ImportConfigCommand.ExecuteAsync(null);

        Assert.Equal(importPath, Assert.Single(profileImport.ImportedPaths));
        Assert.True(configurationChanged);
    }

    [Fact]
    public async Task ImportConfigCommand_NoSelectedPath_DoesNotCallProfileImportService()
    {
        var profileImport = new FakeProfileImportService();
        var viewModel = CreateViewModel(new FakeConfigManager(), profileImportService: profileImport);
        viewModel.ImportFilePathProvider = () => null;

        await viewModel.ImportConfigCommand.ExecuteAsync(null);

        Assert.Empty(profileImport.ImportedPaths);
    }

    [Fact]
    public async Task ImportConfigCommand_ProfileImportFailure_ShowsError()
    {
        var dialog = new FakeDialogService();
        var profileImport = new FakeProfileImportService
        {
            Result = ProfileImportResult.Failure("Unsupported import file type: .txt.")
        };
        var viewModel = CreateViewModel(new FakeConfigManager(), dialog, profileImport);
        viewModel.ImportFilePathProvider = () => Path.Combine(Path.GetTempPath(), "profile.txt");

        await viewModel.ImportConfigCommand.ExecuteAsync(null);

        Assert.Single(dialog.ErrorCalls);
        Assert.Contains(".txt", dialog.ErrorCalls[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SettingsViewModel CreateViewModel(
        FakeConfigManager config,
        FakeDialogService? dialog = null,
        IProfileImportService? profileImportService = null)
    {
        var localizer = new LocalizationManager();
        dialog ??= new FakeDialogService();
        var trustedHostKeys = new TrustedHostKeysSettingsViewModel(
            new HostKeyTrustService(new HostKeyStore()),
            () => new KnownHostsImportReport(0, 0, []),
            () => new KnownHostsExportReport(0, 0, 0),
            localizer,
            dialog,
            new FakeClipboardService(),
            new FakeUiDispatcher());

        return new SettingsViewModel(config, localizer, dialog, trustedHostKeys, profileImportService);
    }

    private static async Task<AppSettings> LoadExpectedFactoryDefaultsAsync()
    {
        var defaultsPath = Path.Combine(AppContext.BaseDirectory, "config", "settings.default.json");
        if (!File.Exists(defaultsPath))
        {
            return new AppSettings();
        }

        var json = await File.ReadAllTextAsync(defaultsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AppSettings();
    }

    private static void SetNonDefaultRdpValues(SettingsViewModel viewModel)
    {
        viewModel.DefaultResolutionWidth = 1280;
        viewModel.DefaultResolutionHeight = 720;
        viewModel.RdpDefaultMode = "External";
        viewModel.RdpDefaultNla = false;
        viewModel.RdpDefaultColorDepth = 16;
        viewModel.RdpDefaultDynamicResolution = false;
        viewModel.RdpDefaultMultiMonitor = true;
        viewModel.RdpDefaultRedirectClipboard = false;
        viewModel.RdpDefaultRedirectDrives = true;
        viewModel.RdpDefaultRedirectPrinters = true;
        viewModel.RdpDefaultRedirectComPorts = true;
        viewModel.RdpDefaultRedirectSmartCards = true;
        viewModel.RdpDefaultRedirectWebcam = true;
        viewModel.RdpDefaultRedirectUsb = true;
        viewModel.RdpDefaultAudioCapture = true;
        viewModel.RdpDefaultAutoReconnect = false;
        viewModel.RdpDefaultBitmapCaching = false;
        viewModel.RdpDefaultCompression = false;
        viewModel.RdpDefaultAudioMode = 2;
    }

    private static void AssertRdpDefaultsMatch(SettingsViewModel viewModel, AppSettings expected)
    {
        Assert.Equal(expected.DefaultResolutionWidth, viewModel.DefaultResolutionWidth);
        Assert.Equal(expected.DefaultResolutionHeight, viewModel.DefaultResolutionHeight);
        Assert.Equal(expected.RdpDefaultMode, viewModel.RdpDefaultMode);
        Assert.Equal(expected.RdpDefaultNla, viewModel.RdpDefaultNla);
        Assert.Equal(expected.RdpDefaultColorDepth, viewModel.RdpDefaultColorDepth);
        Assert.Equal(expected.RdpDefaultDynamicResolution, viewModel.RdpDefaultDynamicResolution);
        Assert.Equal(expected.RdpDefaultMultiMonitor, viewModel.RdpDefaultMultiMonitor);
        Assert.Equal(expected.RdpDefaultRedirectClipboard, viewModel.RdpDefaultRedirectClipboard);
        Assert.Equal(expected.RdpDefaultRedirectDrives, viewModel.RdpDefaultRedirectDrives);
        Assert.Equal(expected.RdpDefaultRedirectPrinters, viewModel.RdpDefaultRedirectPrinters);
        Assert.Equal(expected.RdpDefaultRedirectComPorts, viewModel.RdpDefaultRedirectComPorts);
        Assert.Equal(expected.RdpDefaultRedirectSmartCards, viewModel.RdpDefaultRedirectSmartCards);
        Assert.Equal(expected.RdpDefaultRedirectWebcam, viewModel.RdpDefaultRedirectWebcam);
        Assert.Equal(expected.RdpDefaultRedirectUsb, viewModel.RdpDefaultRedirectUsb);
        Assert.Equal(expected.RdpDefaultAudioCapture, viewModel.RdpDefaultAudioCapture);
        Assert.Equal(expected.RdpDefaultAutoReconnect, viewModel.RdpDefaultAutoReconnect);
        Assert.Equal(expected.RdpDefaultBitmapCaching, viewModel.RdpDefaultBitmapCaching);
        Assert.Equal(expected.RdpDefaultCompression, viewModel.RdpDefaultCompression);
        Assert.Equal(expected.RdpDefaultAudioMode, viewModel.RdpDefaultAudioMode);
    }

    private sealed class FakeConfigManager : IConfigManager
    {
        public AppSettings Settings { get; set; } = new();

        public AppSettings? SavedSettings { get; private set; }

        public List<ServerProfileDto> Servers { get; set; } = [];

        public List<ServerProfileDto>? SavedServers { get; private set; }

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
            => Task.FromResult(Servers);

        public Task SaveServersAsync(List<ServerProfileDto> servers)
        {
            SavedServers = servers;
            Servers = servers;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public void SetText(string text)
        {
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public bool ConfirmResult { get; set; }

        public List<(string Title, string Message, string Severity)> ConfirmCalls { get; } = [];

        public List<(string Title, string Message)> ErrorCalls { get; } = [];

        public Task<bool> ShowConfirmAsync(string title, string message, string severity = "info")
        {
            ConfirmCalls.Add((title, message, severity));
            return Task.FromResult(ConfirmResult);
        }

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

        public Task<string?> ShowBulkEditPasswordAsync(int count, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public void ShowError(string title, string message)
        {
            ErrorCalls.Add((title, message));
        }

        public void ShowInfo(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }
    }

    private sealed class FakeProfileImportService : IProfileImportService
    {
        public ProfileImportResult Result { get; set; } = ProfileImportResult.NoChanges();

        public List<string> ImportedPaths { get; } = [];

        public Task<ProfileImportResult> ImportFromPathAsync(string path, CancellationToken ct = default)
        {
            ImportedPaths.Add(path);
            return Task.FromResult(Result);
        }

        public Task<ProfileImportResult> ImportFromPathsAsync(IEnumerable<string> paths, CancellationToken ct = default)
        {
            ImportedPaths.AddRange(paths);
            return Task.FromResult(Result);
        }
    }
}
