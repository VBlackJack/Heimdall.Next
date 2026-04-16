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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Logging;

namespace Heimdall.App.ViewModels;

/// <summary>
/// ViewModel for the application settings tab.
/// Tracks dirty state and delegates persistence to <see cref="IConfigManager"/>.
/// </summary>
public partial class SettingsViewModel : ObservableValidator
{
    private static bool HasUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfigManager _configManager;
    private readonly LocalizationManager _localizer;
    private readonly IDialogService _dialogService;

    private string _originalTheme = "";

    // Working buffers (mutated by CRUD, flushed to disk on Save)
    private List<SshGatewayDto> _pendingGateways = new();
    private List<ProjectDto> _pendingProjects = new();

    // Projects removed before Save — servers are unassigned on flush
    private readonly List<string> _deletedProjectIds = new();

    // --- General ---

    [ObservableProperty]
    private string _defaultLocale = "en";

    [ObservableProperty]
    private string _defaultTheme = "DraculaPro";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 20, ErrorMessage = "Max embedded sessions must be between 1 and 20.")]
    private int _maxEmbeddedSessions = 10;

    [ObservableProperty]
    private bool _preventSleepDuringSession = true;

    [ObservableProperty]
    private bool _enableSessionPersistence;

    [ObservableProperty]
    private string _externalEditorPath = "";

    // --- Terminal ---

    [ObservableProperty]
    private string _terminalFontFamily = "Consolas";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(8, 72, ErrorMessage = "Terminal font size must be between 8 and 72.")]
    private int _terminalFontSize = 14;

    [ObservableProperty]
    private string _terminalColorScheme = "Dracula";

    [ObservableProperty]
    private string _powerShellExecutionPolicy = "Default";

    // --- SSH & SFTP ---

    [ObservableProperty]
    private string _plinkPath = "";

    [ObservableProperty]
    private string _puttyPath = "";

    [ObservableProperty]
    private string _sshDefaultMode = "Embedded";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 3600, ErrorMessage = "Anti-idle interval must be between 0 and 3600 seconds.")]
    private int _antiIdleInterval = 60;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 3600, ErrorMessage = "SSH TMOUT reset interval must be between 0 and 3600 seconds.")]
    private int _sshTmoutResetInterval = 240;

    [ObservableProperty]
    private bool _sftpAutoOpenOnSsh = true;

    [ObservableProperty]
    private string _x11ServerPath = "";

    [ObservableProperty]
    private bool _x11AutoStart = true;

    // --- External tool provider paths ---

    [ObservableProperty]
    private string _sysinternalsPath = "";

    [ObservableProperty]
    private string _nirSoftPath = "";

    [ObservableProperty]
    private string _nanaRunPath = "";

    // --- Command Library Git Sync ---

    [ObservableProperty]
    private bool _cmdLibGitSyncEnabled;

    [ObservableProperty]
    private string _cmdLibGitSyncUrl = "";

    [ObservableProperty]
    private string _cmdLibGitSyncBranch = "main";

    [ObservableProperty]
    private string _cmdLibGitSyncAuthorName = "Heimdall User";

    [ObservableProperty]
    private string _cmdLibGitSyncAuthorEmail = "heimdall@local";

    [ObservableProperty]
    private bool _cmdLibGitSyncOnStartup;

    [ObservableProperty]
    private bool _cmdLibGitSyncAutoPush = true;

    // --- RDP defaults ---

    [ObservableProperty]
    private int _defaultResolutionWidth = 1920;

    [ObservableProperty]
    private int _defaultResolutionHeight = 1080;

    [ObservableProperty]
    private string _rdpDefaultMode = "Embedded";

    [ObservableProperty]
    private bool _rdpDefaultNla = true;

    [ObservableProperty]
    private int _rdpDefaultColorDepth = 32;

    [ObservableProperty]
    private bool _rdpDefaultDynamicResolution = true;

    [ObservableProperty]
    private bool _rdpDefaultMultiMonitor;

    [ObservableProperty]
    private bool _rdpDefaultRedirectClipboard = true;

    [ObservableProperty]
    private bool _rdpDefaultRedirectDrives;

    [ObservableProperty]
    private bool _rdpDefaultRedirectPrinters;

    [ObservableProperty]
    private bool _rdpDefaultAutoReconnect = true;

    [ObservableProperty]
    private bool _rdpDefaultBitmapCaching = true;

    [ObservableProperty]
    private bool _rdpDefaultCompression = true;

    [ObservableProperty]
    private int _rdpDefaultAudioMode;

    // --- Security ---

    [ObservableProperty]
    private bool _useExternalCredentialProvider;

    public string CredProvHelpText => UseExternalCredentialProvider
        ? ""
        : _localizer["SettingsCredProvDisabledHint"];

    partial void OnUseExternalCredentialProviderChanged(bool value)
    {
        OnPropertyChanged(nameof(CredProvHelpText));
    }

    [ObservableProperty]
    private string _credentialProviderCommand = "";

    [ObservableProperty]
    private string _credentialProviderDatabase = "";

    [ObservableProperty]
    private bool _requireCredentialGuard;

    // --- UI state (persisted but not exposed in Settings tab) ---

    [ObservableProperty]
    private bool _showToolsPanel;

    // --- Advanced / Logging ---

    [ObservableProperty]
    private bool _enableLogging = true;

    [ObservableProperty]
    private bool _sessionLoggingEnabled;

    [ObservableProperty]
    private string _sessionLogDirectory = @"logs\sessions";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 30000, ErrorMessage = "Tunnel establishment delay must be between 0 and 30000 ms.")]
    private int _tunnelEstablishmentDelayMs = 2500;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1000, 120000, ErrorMessage = "Embedded RDP timeout must be between 1000 and 120000 ms.")]
    private int _embeddedRdpTimeoutMs = 30000;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(5000, 600000, ErrorMessage = "External tool timeout must be between 5000 and 600000 ms.")]
    private int _externalToolTimeoutMs = 60000;

    // --- Collections ---

    [ObservableProperty]
    private ObservableCollection<ExternalToolItemViewModel> _externalTools = new();

    [ObservableProperty]
    private ExternalToolItemViewModel? _selectedExternalTool;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private ObservableCollection<GatewayItemViewModel> _gateways = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditGatewayCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteGatewayCommand))]
    private GatewayItemViewModel? _selectedGateway;

    [ObservableProperty]
    private ObservableCollection<ProjectItemViewModel> _projects = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProjectCommand))]
    private ProjectItemViewModel? _selectedProject;

    /// <summary>
    /// Raised after a server import completes so the main shell can reload
    /// the server list and related UI state.
    /// </summary>
    public event Action? ConfigurationChanged;

    /// <summary>
    /// Raised when the user changes the theme selection so the shell can
    /// swap the active <see cref="System.Windows.ResourceDictionary"/> at runtime.
    /// </summary>
    public event Action<string>? ThemeChanged;

    public SettingsViewModel(
        IConfigManager configManager,
        LocalizationManager localizer,
        IDialogService dialogService)
    {
        _configManager = configManager;
        _localizer = localizer;
        _dialogService = dialogService;
    }

    /// <summary>
    /// Applies the current <see cref="SshDefaultMode"/> to every server in the inventory.
    /// </summary>
    [RelayCommand]
    private async Task ApplySshModeToAllAsync()
    {
        var servers = await _configManager.LoadServersAsync();
        var mode = SshDefaultMode;
        var changeCount = servers.Count(s => !string.Equals(s.SshMode, mode, StringComparison.Ordinal));

        if (changeCount == 0)
        {
            FileLogger.Info("ApplySshModeToAll: no changes needed.");
            return;
        }

        var confirmed = await _dialogService.ShowConfirmAsync(
            _localizer["ConfirmApplyAllTitle"],
            _localizer.Format("ConfirmApplySshModeMessage", mode, changeCount, servers.Count),
            "danger");

        if (!confirmed) return;

        foreach (var server in servers)
        {
            server.SshMode = mode;
        }

        await _configManager.SaveServersAsync(servers);
        ConfigurationChanged?.Invoke();
        FileLogger.Info($"Applied SSH mode '{mode}' to {changeCount}/{servers.Count} servers.");
    }

    /// <summary>
    /// Applies the current <see cref="RdpDefaultMode"/> to every server in the inventory.
    /// </summary>
    [RelayCommand]
    private async Task ApplyRdpModeToAllAsync()
    {
        var servers = await _configManager.LoadServersAsync();
        var mode = RdpDefaultMode;
        var changeCount = servers.Count(s => !string.Equals(s.RdpMode, mode, StringComparison.Ordinal));

        if (changeCount == 0)
        {
            FileLogger.Info("ApplyRdpModeToAll: no changes needed.");
            return;
        }

        var confirmed = await _dialogService.ShowConfirmAsync(
            _localizer["ConfirmApplyAllTitle"],
            _localizer.Format("ConfirmApplyRdpModeMessage", mode, changeCount, servers.Count),
            "danger");

        if (!confirmed) return;

        foreach (var server in servers)
        {
            server.RdpMode = mode;
        }

        await _configManager.SaveServersAsync(servers);
        ConfigurationChanged?.Invoke();
        FileLogger.Info($"Applied RDP mode '{mode}' to {changeCount}/{servers.Count} servers.");
    }

    /// <summary>
    /// Populates ViewModel properties from the loaded <see cref="AppSettings"/>.
    /// Does not mark the ViewModel as dirty.
    /// </summary>
    public void LoadFromSettings(AppSettings settings)
    {
        // General
        DefaultLocale = settings.DefaultLocale;
        DefaultTheme = settings.DefaultTheme;
        _originalTheme = settings.DefaultTheme;
        MaxEmbeddedSessions = settings.MaxEmbeddedSessions;
        PreventSleepDuringSession = settings.PreventSleepDuringSession;
        EnableSessionPersistence = settings.EnableSessionPersistence;
        ExternalEditorPath = settings.ExternalEditorPath;

        // UI state
        ShowToolsPanel = settings.ShowToolsPanel;

        // Terminal
        TerminalFontFamily = settings.TerminalFontFamily;
        TerminalFontSize = settings.TerminalFontSize;
        TerminalColorScheme = settings.TerminalColorScheme;
        PowerShellExecutionPolicy = settings.PowerShellExecutionPolicy;

        // SSH & SFTP
        PlinkPath = settings.PlinkPath;
        PuttyPath = settings.PuttyPath ?? "";
        SshDefaultMode = settings.SshDefaultMode;
        AntiIdleInterval = settings.AntiIdleIntervalSeconds;
        SshTmoutResetInterval = settings.SshTmoutResetIntervalSeconds;
        SftpAutoOpenOnSsh = settings.SftpAutoOpenOnSsh;
        X11ServerPath = settings.X11ServerPath ?? "";
        X11AutoStart = settings.X11AutoStart;
        SysinternalsPath = settings.SysinternalsPath ?? "";
        NirSoftPath = settings.NirSoftPath ?? "";
        NanaRunPath = settings.NanaRunPath ?? "";

        // Command Library Git Sync
        CmdLibGitSyncEnabled = settings.CmdLibGitSyncEnabled;
        CmdLibGitSyncUrl = settings.CmdLibGitSyncUrl ?? "";
        CmdLibGitSyncBranch = settings.CmdLibGitSyncBranch;
        CmdLibGitSyncAuthorName = settings.CmdLibGitSyncAuthorName;
        CmdLibGitSyncAuthorEmail = settings.CmdLibGitSyncAuthorEmail;
        CmdLibGitSyncOnStartup = settings.CmdLibGitSyncOnStartup;
        CmdLibGitSyncAutoPush = settings.CmdLibGitSyncAutoPush;

        // RDP defaults
        DefaultResolutionWidth = settings.DefaultResolutionWidth;
        DefaultResolutionHeight = settings.DefaultResolutionHeight;
        RdpDefaultMode = settings.RdpDefaultMode;
        RdpDefaultNla = settings.RdpDefaultNla;
        RdpDefaultColorDepth = settings.RdpDefaultColorDepth;
        RdpDefaultDynamicResolution = settings.RdpDefaultDynamicResolution;
        RdpDefaultMultiMonitor = settings.RdpDefaultMultiMonitor;
        RdpDefaultRedirectClipboard = settings.RdpDefaultRedirectClipboard;
        RdpDefaultRedirectDrives = settings.RdpDefaultRedirectDrives;
        RdpDefaultRedirectPrinters = settings.RdpDefaultRedirectPrinters;
        RdpDefaultAutoReconnect = settings.RdpDefaultAutoReconnect;
        RdpDefaultBitmapCaching = settings.RdpDefaultBitmapCaching;
        RdpDefaultCompression = settings.RdpDefaultCompression;
        RdpDefaultAudioMode = settings.RdpDefaultAudioMode;

        // Security
        UseExternalCredentialProvider = settings.UseExternalCredentialProvider;
        CredentialProviderCommand = settings.CredentialProviderCommand ?? "";
        CredentialProviderDatabase = settings.CredentialProviderDatabase ?? "";
        RequireCredentialGuard = settings.RequireCredentialGuard;

        // Advanced / Logging
        EnableLogging = settings.EnableLogging;
        SessionLoggingEnabled = settings.SessionLoggingEnabled;
        SessionLogDirectory = settings.SessionLogDirectory;
        TunnelEstablishmentDelayMs = settings.TunnelEstablishmentDelayMs;
        EmbeddedRdpTimeoutMs = settings.EmbeddedRdpTimeoutMs;
        ExternalToolTimeoutMs = settings.ExternalToolTimeoutMs;

        UnsubscribeExternalToolTracking();

        ExternalTools = new ObservableCollection<ExternalToolItemViewModel>(
            settings.ExternalTools.Select(t => new ExternalToolItemViewModel
            {
                Name = t.Name,
                ExecutablePath = t.ExecutablePath,
                Arguments = t.Arguments,
                WorkingDirectory = t.WorkingDirectory,
                RunAsAdministrator = t.RunAsAdministrator,
                RunHidden = t.RunHidden
            }));

        SubscribeExternalToolTracking();

        Gateways = new ObservableCollection<GatewayItemViewModel>(
            settings.SshGateways.Select(g => new GatewayItemViewModel
            {
                Id = g.Id,
                Name = g.Name,
                Host = g.Host,
                Port = g.Port,
                User = g.User,
                HasKey = !string.IsNullOrEmpty(g.KeyPath),
                HasPassword = !string.IsNullOrEmpty(g.SshPasswordEncrypted),
                ParentGatewayId = g.ParentGatewayId
            }));

        Projects = new ObservableCollection<ProjectItemViewModel>(
            settings.Projects.Select(p => new ProjectItemViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Color = p.Color ?? "#3B82F6",
                Description = p.Description ?? ""
            }));

        // Seed working buffers from loaded settings
        _pendingGateways = settings.SshGateways.Select(CloneGateway).ToList();
        _pendingProjects = settings.Projects.Select(CloneProject).ToList();
        _deletedProjectIds.Clear();

        IsDirty = false;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        ValidateAllProperties();
        RefreshValidationSummary();

        if (HasErrors)
        {
            return;
        }

        var settings = await _configManager.LoadSettingsAsync();

        // General
        settings.DefaultLocale = DefaultLocale;
        settings.DefaultTheme = DefaultTheme;
        settings.MaxEmbeddedSessions = MaxEmbeddedSessions;
        settings.PreventSleepDuringSession = PreventSleepDuringSession;
        settings.EnableSessionPersistence = EnableSessionPersistence;
        settings.ExternalEditorPath = ExternalEditorPath;

        // Terminal
        settings.TerminalFontFamily = TerminalFontFamily;
        settings.TerminalFontSize = TerminalFontSize;
        settings.TerminalColorScheme = TerminalColorScheme;
        settings.PowerShellExecutionPolicy = PowerShellExecutionPolicy;

        // SSH & SFTP
        settings.PlinkPath = PlinkPath;
        settings.PuttyPath = string.IsNullOrWhiteSpace(PuttyPath) ? null : PuttyPath;
        settings.SshDefaultMode = SshDefaultMode;
        settings.AntiIdleIntervalSeconds = AntiIdleInterval;
        settings.SshTmoutResetIntervalSeconds = SshTmoutResetInterval;
        settings.SftpAutoOpenOnSsh = SftpAutoOpenOnSsh;
        settings.X11ServerPath = string.IsNullOrWhiteSpace(X11ServerPath) ? null : X11ServerPath;
        settings.X11AutoStart = X11AutoStart;
        settings.SysinternalsPath = string.IsNullOrWhiteSpace(SysinternalsPath) ? null : SysinternalsPath;
        settings.NirSoftPath = string.IsNullOrWhiteSpace(NirSoftPath) ? null : NirSoftPath;
        settings.NanaRunPath = string.IsNullOrWhiteSpace(NanaRunPath) ? null : NanaRunPath;

        // Command Library Git Sync
        settings.CmdLibGitSyncEnabled = CmdLibGitSyncEnabled;
        settings.CmdLibGitSyncUrl = string.IsNullOrWhiteSpace(CmdLibGitSyncUrl) ? null : CmdLibGitSyncUrl;
        settings.CmdLibGitSyncBranch = CmdLibGitSyncBranch;
        settings.CmdLibGitSyncAuthorName = CmdLibGitSyncAuthorName;
        settings.CmdLibGitSyncAuthorEmail = CmdLibGitSyncAuthorEmail;
        settings.CmdLibGitSyncOnStartup = CmdLibGitSyncOnStartup;
        settings.CmdLibGitSyncAutoPush = CmdLibGitSyncAutoPush;

        // RDP defaults
        settings.DefaultResolutionWidth = DefaultResolutionWidth;
        settings.DefaultResolutionHeight = DefaultResolutionHeight;
        settings.RdpDefaultMode = RdpDefaultMode;
        settings.RdpDefaultNla = RdpDefaultNla;
        settings.RdpDefaultColorDepth = RdpDefaultColorDepth;
        settings.RdpDefaultDynamicResolution = RdpDefaultDynamicResolution;
        settings.RdpDefaultMultiMonitor = RdpDefaultMultiMonitor;
        settings.RdpDefaultRedirectClipboard = RdpDefaultRedirectClipboard;
        settings.RdpDefaultRedirectDrives = RdpDefaultRedirectDrives;
        settings.RdpDefaultRedirectPrinters = RdpDefaultRedirectPrinters;
        settings.RdpDefaultAutoReconnect = RdpDefaultAutoReconnect;
        settings.RdpDefaultBitmapCaching = RdpDefaultBitmapCaching;
        settings.RdpDefaultCompression = RdpDefaultCompression;
        settings.RdpDefaultAudioMode = RdpDefaultAudioMode;

        // Security
        settings.UseExternalCredentialProvider = UseExternalCredentialProvider;
        settings.CredentialProviderCommand = CredentialProviderCommand;
        settings.CredentialProviderDatabase = CredentialProviderDatabase;
        settings.RequireCredentialGuard = RequireCredentialGuard;

        // Advanced / Logging
        settings.EnableLogging = EnableLogging;
        settings.SessionLoggingEnabled = SessionLoggingEnabled;
        settings.SessionLogDirectory = SessionLogDirectory;
        settings.TunnelEstablishmentDelayMs = TunnelEstablishmentDelayMs;
        settings.EmbeddedRdpTimeoutMs = EmbeddedRdpTimeoutMs;
        settings.ExternalToolTimeoutMs = ExternalToolTimeoutMs;

        // UI state
        settings.ShowToolsPanel = ShowToolsPanel;

        // Validate external tools before persisting
        var extToolError = ValidateExternalTools();
        if (extToolError is not null)
        {
            ValidationSummary = extToolError;
            HasValidationErrors = true;
            return;
        }

        settings.ExternalTools = ExternalTools.Select(t => new ExternalToolDefinition
        {
            Name = t.Name.Trim(),
            ExecutablePath = t.ExecutablePath.Trim(),
            Arguments = t.Arguments,
            WorkingDirectory = t.WorkingDirectory,
            RunAsAdministrator = t.RunAsAdministrator,
            RunHidden = t.RunHidden
        }).ToList();

        // Flush buffered gateways and projects
        settings.SshGateways = _pendingGateways.Select(CloneGateway).ToList();
        settings.Projects = _pendingProjects.Select(CloneProject).ToList();

        await _configManager.SaveSettingsAsync(settings);

        // Unassign servers from deleted projects
        if (_deletedProjectIds.Count > 0)
        {
            var servers = await _configManager.LoadServersAsync();
            var changed = false;
            foreach (var server in servers.Where(s =>
                s.ProjectId is not null && _deletedProjectIds.Contains(s.ProjectId)))
            {
                server.ProjectId = null;
                changed = true;
            }

            if (changed)
            {
                await _configManager.SaveServersAsync(servers);
            }

            _deletedProjectIds.Clear();
        }

        _originalTheme = DefaultTheme;

        if (!string.Equals(_localizer.CurrentLocale, DefaultLocale, StringComparison.OrdinalIgnoreCase))
        {
            await _localizer.SwitchLocaleAsync(DefaultLocale);
        }

        IsDirty = false;
        ConfigurationChanged?.Invoke();
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync(CancellationToken cancellationToken)
    {
        // Load factory defaults from settings.default.json (preserves bundled external tools)
        // rather than new AppSettings() which has empty defaults for collections.
        var defaultsPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "config", "settings.default.json");

        AppSettings defaults;
        if (System.IO.File.Exists(defaultsPath))
        {
            var json = await System.IO.File.ReadAllTextAsync(defaultsPath, cancellationToken);
            defaults = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, ImportJsonOptions)
                       ?? new AppSettings();
        }
        else
        {
            defaults = new AppSettings();
        }

        LoadFromSettings(defaults);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task ExportConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = _localizer["ExportDialogTitle"],
                Filter = _localizer["ExportDialogFilter"],
                DefaultExt = ".json",
                FileName = "servers.json"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var servers = await _configManager.LoadServersAsync();
            var json = JsonSerializer.Serialize(servers, ExportJsonOptions);
            await File.WriteAllTextAsync(dialog.FileName, json, new System.Text.UTF8Encoding(false), cancellationToken);

            var count = servers.Count;
            FileLogger.Info($"Exported {count} server(s) to {dialog.FileName}");
            _dialogService.ShowInfo(
                _localizer["ExportDialogTitle"],
                _localizer.Format("StatusExportSuccess", count));
        }
        catch (Exception ex)
        {
            FileLogger.Error("Export failed", ex);
            _dialogService.ShowError(
                _localizer["ExportDialogTitle"],
                _localizer.Format("StatusExportFailed", ex.Message));
        }
    }

    [RelayCommand]
    private async Task ImportConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = _localizer["ImportDialogTitle"],
                Filter = _localizer["ImportDialogFilterAll"]
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            IsBusy = true;
            var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();

            var (imported, importWarnings) = ext switch
            {
                ".mxtsessions" or ".ini" or ".mobaconf" => await ImportMobaXtermAsync(dialog.FileName, cancellationToken),
                ".rdp" => await ImportRdpFileAsync(dialog.FileName, cancellationToken),
                ".rdg" => await ImportRdcManAsync(dialog.FileName, cancellationToken),
                ".xml" => await ImportXmlAsync(dialog.FileName, cancellationToken),
                _ => await ImportJsonAsync(dialog.FileName, cancellationToken),
            };

            if (imported.Count == 0)
            {
                _dialogService.ShowInfo(
                    _localizer["ImportDialogTitle"],
                    _localizer["ImportNoSessionsFound"]);
                return;
            }

            var confirmMessage = ext is ".mxtsessions" or ".ini" or ".mobaconf"
                ? _localizer.Format("ConfirmImportMobaXtermMessage", imported.Count)
                : _localizer.Format("ConfirmImportMessage", imported.Count);

            var confirmed = await _dialogService.ShowConfirmAsync(
                _localizer["ConfirmImportTitle"],
                confirmMessage);

            if (!confirmed)
            {
                return;
            }

            var existing = await _configManager.LoadServersAsync();

            var newCount = 0;
            var updatedCount = 0;

            foreach (var server in imported)
            {
                if (string.IsNullOrEmpty(server.Id))
                {
                    server.Id = Guid.NewGuid().ToString();
                }

                var existingIndex = existing.FindIndex(
                    s => string.Equals(s.Id, server.Id, StringComparison.OrdinalIgnoreCase));

                if (existingIndex >= 0)
                {
                    existing[existingIndex] = server;
                    updatedCount++;
                }
                else
                {
                    existing.Add(server);
                    newCount++;
                }
            }

            await _configManager.SaveServersAsync(existing);

            var totalImported = newCount + updatedCount;
            FileLogger.Info($"Imported {totalImported} server(s) from {dialog.FileName} ({newCount} new, {updatedCount} updated)");

            var statusMessage = _localizer.Format("StatusImportBreakdown", totalImported, newCount, updatedCount);

            if (importWarnings is { Count: > 0 })
            {
                var warningText = string.Join("\n", importWarnings.Take(10));
                statusMessage += "\n\n" + _localizer.Format("ImportMobaXtermWarnings", importWarnings.Count)
                    + "\n" + warningText;
            }

            if (ext is ".mxtsessions" or ".ini" or ".mobaconf")
            {
                statusMessage += "\n\n" + _localizer["ImportMobaXtermPasswordNotice"];
                _dialogService.ShowWarning(_localizer["ImportDialogTitle"], statusMessage);
            }
            else
            {
                _dialogService.ShowInfo(_localizer["ImportDialogTitle"], statusMessage);
            }

            ConfigurationChanged?.Invoke();
        }
        catch (JsonException ex)
        {
            FileLogger.Error("Import failed: invalid JSON", ex);
            _dialogService.ShowError(
                _localizer["ImportDialogTitle"],
                _localizer.Format("StatusImportFailed", ex.Message));
        }
        catch (Exception ex)
        {
            FileLogger.Error("Import failed", ex);
            _dialogService.ShowError(
                _localizer["ImportDialogTitle"],
                _localizer.Format("StatusImportFailed", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<(List<ServerProfileDto> Servers, List<string>? Warnings)> ImportMobaXtermAsync(
        string filePath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var content = HasUtf8Bom(bytes)
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : System.Text.Encoding.GetEncoding(1252).GetString(bytes);
        var mobaResult = MobaXtermImporter.Parse(content);
        return (mobaResult.Servers, mobaResult.Warnings);
    }

    private async Task<(List<ServerProfileDto> Servers, List<string>? Warnings)> ImportRdpFileAsync(
        string filePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var rdpServer = RdpFileImporter.Parse(content, Path.GetFileName(filePath));
        return (rdpServer is not null ? [rdpServer] : [], null);
    }

    private async Task<(List<ServerProfileDto> Servers, List<string>? Warnings)> ImportRdcManAsync(
        string filePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var rdcResult = RdcManImporter.Parse(content);
        return (rdcResult.Servers, rdcResult.Warnings);
    }

    private async Task<(List<ServerProfileDto> Servers, List<string>? Warnings)> ImportXmlAsync(
        string filePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (content.Contains("<Connections", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Type=\"Connection\"", StringComparison.OrdinalIgnoreCase))
        {
            var mrnResult = MRemoteNgImporter.Parse(content);
            return (mrnResult.Servers, mrnResult.Warnings);
        }

        var rdcResult = RdcManImporter.Parse(content);
        return (rdcResult.Servers, rdcResult.Warnings);
    }

    private async Task<(List<ServerProfileDto> Servers, List<string>? Warnings)> ImportJsonAsync(
        string filePath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var servers = JsonSerializer.Deserialize<List<ServerProfileDto>>(json, ImportJsonOptions) ?? [];
        return (servers, null);
    }

    [RelayCommand]
    private async Task ImportCitrixAppsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var scanResult = CitrixCacheScanner.Scan();

            if (scanResult.Resources.Count == 0)
            {
                var warningMsg = scanResult.Warnings.Count > 0
                    ? string.Join("\n", scanResult.Warnings)
                    : _localizer["CitrixNoAppsFound"];
                _dialogService.ShowInfo(_localizer["CitrixScanTitle"], warningMsg);
                return;
            }

            var confirmed = await _dialogService.ShowConfirmAsync(
                _localizer["CitrixScanTitle"],
                _localizer.Format("CitrixScanConfirm", scanResult.Resources.Count));

            if (!confirmed) return;

            var imported = CitrixCacheScanner.ToServerProfiles(scanResult.Resources);
            var existing = await _configManager.LoadServersAsync();

            var newCount = 0;
            foreach (var server in imported)
            {
                existing.Add(server);
                newCount++;
            }

            await _configManager.SaveServersAsync(existing);

            var statusMsg = _localizer.Format("CitrixScanSuccess", newCount);
            if (scanResult.Warnings.Count > 0)
            {
                statusMsg += "\n\n" + string.Join("\n", scanResult.Warnings.Take(5));
            }

            _dialogService.ShowInfo(_localizer["CitrixScanTitle"], statusMsg);
            FileLogger.Info($"Imported {newCount} Citrix app(s) from local cache");
            ConfigurationChanged?.Invoke();
        }
        catch (Exception ex)
        {
            FileLogger.Error("Citrix scan failed", ex);
            _dialogService.ShowError(
                _localizer["CitrixScanTitle"],
                _localizer.Format("StatusImportFailed", ex.Message));
        }
    }

    [RelayCommand]
    private async Task AddGatewayAsync(CancellationToken cancellationToken)
    {
        var vm = new GatewayDialogViewModel();
        vm.AvailableParents = new ObservableCollection<GatewayOption>(
            _pendingGateways.Select(g => new GatewayOption(g.Id, $"{g.Name} ({g.Host})")));

        var result = await _dialogService.ShowGatewayDialogAsync(vm);
        if (result?.Saved == true)
        {
            result.Gateway.Id = Guid.NewGuid().ToString();
            _pendingGateways.Add(result.Gateway);

            Gateways.Add(new GatewayItemViewModel
            {
                Id = result.Gateway.Id,
                Name = result.Gateway.Name,
                Host = result.Gateway.Host,
                Port = result.Gateway.Port,
                User = result.Gateway.User,
                HasKey = !string.IsNullOrEmpty(result.Gateway.KeyPath),
                HasPassword = !string.IsNullOrEmpty(result.Gateway.SshPasswordEncrypted)
            });

            IsDirty = true;
        }
    }

    private bool CanEditGateway() => SelectedGateway is not null;

    [RelayCommand(CanExecute = nameof(CanEditGateway))]
    private async Task EditGatewayAsync(CancellationToken cancellationToken)
    {
        var gateway = SelectedGateway!;
        var gwDto = _pendingGateways.FirstOrDefault(g => g.Id == gateway.Id);
        if (gwDto == null) return;

        var vm = GatewayDialogViewModel.FromDto(gwDto);
        vm.AvailableParents = new ObservableCollection<GatewayOption>(
            _pendingGateways
                .Where(g => g.Id != gwDto.Id)
                .Select(g => new GatewayOption(g.Id, $"{g.Name} ({g.Host})")));

        var result = await _dialogService.ShowGatewayDialogAsync(vm);
        if (result?.Saved == true)
        {
            var idx = _pendingGateways.FindIndex(g => g.Id == gwDto.Id);
            if (idx >= 0)
            {
                result.Gateway.Id = gwDto.Id;
                _pendingGateways[idx] = result.Gateway;

                gateway.Name = result.Gateway.Name;
                gateway.Host = result.Gateway.Host;
                gateway.Port = result.Gateway.Port;
                gateway.User = result.Gateway.User;
                gateway.HasKey = !string.IsNullOrEmpty(result.Gateway.KeyPath);
                gateway.HasPassword = !string.IsNullOrEmpty(result.Gateway.SshPasswordEncrypted);
            }

            IsDirty = true;
        }
    }

    private bool CanDeleteGateway() => SelectedGateway is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteGateway))]
    private async Task DeleteGatewayAsync(CancellationToken cancellationToken)
    {
        var gateway = SelectedGateway!;
        var confirmed = await _dialogService.ShowConfirmAsync(
            _localizer["ConfirmDeleteGatewayTitle"],
            _localizer.Format("ConfirmDeleteGatewayDetailMessage", gateway.Name),
            "danger");

        if (!confirmed) return;

        _pendingGateways.RemoveAll(g => g.Id == gateway.Id);
        Gateways.Remove(gateway);
        SelectedGateway = null;
        IsDirty = true;
    }

    [RelayCommand]
    private async Task AddProjectAsync(CancellationToken cancellationToken)
    {
        var vm = new ProjectDialogViewModel
        {
            DialogTitle = _localizer["ProjectDialogTitleAdd"]
        };

        var result = await _dialogService.ShowProjectDialogAsync(vm);
        if (result is not { Saved: true }) return;

        result.Project.Id = Guid.NewGuid().ToString();
        _pendingProjects.Add(result.Project);

        Projects.Add(new ProjectItemViewModel
        {
            Id = result.Project.Id,
            Name = result.Project.Name,
            Color = result.Project.Color ?? "#3B82F6",
            Description = result.Project.Description ?? ""
        });

        IsDirty = true;
    }

    private bool CanEditProject() => SelectedProject is not null;

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private async Task EditProjectAsync(CancellationToken cancellationToken)
    {
        var project = SelectedProject!;
        var projectDto = _pendingProjects.FirstOrDefault(p => p.Id == project.Id);
        if (projectDto is null) return;

        var vm = ProjectDialogViewModel.FromDto(projectDto);
        vm.DialogTitle = _localizer["ProjectDialogTitleEdit"];

        var result = await _dialogService.ShowProjectDialogAsync(vm);
        if (result is not { Saved: true }) return;

        var idx = _pendingProjects.FindIndex(p => p.Id == projectDto.Id);
        if (idx >= 0)
        {
            result.Project.Id = projectDto.Id;
            _pendingProjects[idx] = result.Project;

            project.Name = result.Project.Name;
            project.Color = result.Project.Color ?? "#3B82F6";
            project.Description = result.Project.Description ?? "";
        }

        IsDirty = true;
    }

    private bool CanDeleteProject() => SelectedProject is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteProject))]
    private async Task DeleteProjectAsync(CancellationToken cancellationToken)
    {
        var project = SelectedProject!;

        // Check server usage for the confirmation message
        var servers = await _configManager.LoadServersAsync();
        var usageCount = servers.Count(s =>
            string.Equals(s.ProjectId, project.Id, StringComparison.Ordinal));

        var message = usageCount > 0
            ? _localizer.Format("ConfirmDeleteProjectInUse", usageCount)
                + "\n" + _localizer.Format("ConfirmDeleteProjectMessage", project.Name)
            : _localizer.Format("ConfirmDeleteProjectMessage", project.Name);

        var confirmed = await _dialogService.ShowConfirmAsync(
            _localizer["ConfirmDeleteProjectTitle"],
            message,
            "danger");

        if (!confirmed) return;

        _pendingProjects.RemoveAll(p => p.Id == project.Id);
        _deletedProjectIds.Add(project.Id);

        Projects.Remove(project);
        SelectedProject = null;
        IsDirty = true;
    }

    [RelayCommand]
    private Task AddExternalToolAsync(CancellationToken cancellationToken)
    {
        var newTool = new ExternalToolItemViewModel
        {
            Name = _localizer["ExternalToolDefaultName"],
            Arguments = "{Host}"
        };

        ExternalTools.Add(newTool);
        SelectedExternalTool = newTool;
        IsDirty = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task RemoveExternalToolAsync(CancellationToken cancellationToken)
    {
        if (SelectedExternalTool is null) return Task.CompletedTask;

        ExternalTools.Remove(SelectedExternalTool);
        SelectedExternalTool = null;
        IsDirty = true;
        return Task.CompletedTask;
    }

    [ObservableProperty]
    private string? _credentialProviderTestResult;

    [RelayCommand]
    private async Task TestCredentialProviderAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CredentialProviderCommand))
        {
            CredentialProviderTestResult = _localizer["CredProvTestNoCommand"];
            return;
        }

        CredentialProviderTestResult = _localizer["CredProvTestRunning"];

        try
        {
            var provider = new Core.Security.CommandCredentialProvider(
                CredentialProviderCommand, CredentialProviderDatabase);

            var result = await provider.GetCredentialAsync(
                "test.example.com", 22, "testuser", "TestEntry", cancellationToken);

            CredentialProviderTestResult = result is not null
                ? _localizer["CredProvTestSuccess"]
                : _localizer["CredProvTestNoResult"];
        }
        catch (OperationCanceledException)
        {
            CredentialProviderTestResult = _localizer["CredProvTestTimeout"];
        }
        catch (Exception ex)
        {
            CredentialProviderTestResult = _localizer.Format("CredProvTestError", ex.Message);
        }
    }

    partial void OnDefaultThemeChanged(string value)
    {
        ThemeChanged?.Invoke(value);
    }

    public async Task DiscardChangesAsync()
    {
        DefaultTheme = _originalTheme;
        var settings = await _configManager.LoadSettingsAsync();
        LoadFromSettings(settings);
    }

    private void SubscribeExternalToolTracking()
    {
        foreach (var tool in ExternalTools)
            tool.PropertyChanged += OnExternalToolItemPropertyChanged;
        ExternalTools.CollectionChanged += OnExternalToolsCollectionChanged;
    }

    private void UnsubscribeExternalToolTracking()
    {
        if (ExternalTools is null) return;
        foreach (var tool in ExternalTools)
            tool.PropertyChanged -= OnExternalToolItemPropertyChanged;
        ExternalTools.CollectionChanged -= OnExternalToolsCollectionChanged;
    }

    private void OnExternalToolItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        IsDirty = true;
    }

    private void OnExternalToolsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ExternalToolItemViewModel tool in e.OldItems)
                tool.PropertyChanged -= OnExternalToolItemPropertyChanged;
        if (e.NewItems is not null)
            foreach (ExternalToolItemViewModel tool in e.NewItems)
                tool.PropertyChanged += OnExternalToolItemPropertyChanged;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Mark dirty when any settings property changes, excluding non-settings properties
        if (e.PropertyName is not (nameof(IsDirty) or nameof(IsBusy)
            or nameof(SelectedGateway) or nameof(SelectedProject)
            or nameof(SelectedExternalTool) or nameof(HasValidationErrors)
            or nameof(ValidationSummary)))
        {
            IsDirty = true;
        }
    }

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private string? _validationSummary;

    private static readonly Dictionary<string, string> SettingsValidationKeyMap = new(StringComparer.Ordinal)
    {
        ["Max embedded sessions must be between 1 and 20."] = "ValidationSettingsMaxSessions",
        ["Terminal font size must be between 8 and 72."] = "ValidationSettingsFontSize",
        ["Anti-idle interval must be between 0 and 3600 seconds."] = "ValidationSettingsAntiIdle",
        ["SSH TMOUT reset interval must be between 0 and 3600 seconds."] = "ValidationSettingsTmoutReset",
        ["Tunnel establishment delay must be between 0 and 30000 ms."] = "ValidationSettingsTunnelDelay",
        ["Embedded RDP timeout must be between 1000 and 120000 ms."] = "ValidationSettingsRdpTimeout",
        ["External tool timeout must be between 5000 and 600000 ms."] = "ValidationSettingsExtToolTimeout",
    };

    private string? GetLocalizedFieldError(string propertyName)
    {
        var error = GetErrors(propertyName)
            .OfType<System.ComponentModel.DataAnnotations.ValidationResult>()
            .FirstOrDefault();

        var message = error?.ErrorMessage;
        if (message is not null
            && SettingsValidationKeyMap.TryGetValue(message, out var key))
        {
            return _localizer[key];
        }

        return message;
    }

    private void RefreshValidationSummary()
    {
        var firstError = GetLocalizedFieldError(nameof(MaxEmbeddedSessions))
            ?? GetLocalizedFieldError(nameof(TerminalFontSize))
            ?? GetLocalizedFieldError(nameof(AntiIdleInterval))
            ?? GetLocalizedFieldError(nameof(SshTmoutResetInterval))
            ?? GetLocalizedFieldError(nameof(TunnelEstablishmentDelayMs))
            ?? GetLocalizedFieldError(nameof(EmbeddedRdpTimeoutMs))
            ?? GetLocalizedFieldError(nameof(ExternalToolTimeoutMs));

        ValidationSummary = firstError;
        HasValidationErrors = firstError is not null;
    }

    /// <summary>
    /// Returns a localized error message if any external tool has an empty name,
    /// empty executable path, duplicate name, or references a non-existent binary.
    /// Returns null when valid.
    /// </summary>
    private string? ValidateExternalTools()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in ExternalTools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.ExecutablePath))
            {
                return _localizer["ValidationExtToolIncomplete"];
            }

            if (!seen.Add(tool.Name.Trim()))
            {
                return _localizer.Format("ValidationExtToolDuplicate", tool.Name.Trim());
            }

            var exePath = tool.ExecutablePath.Trim();
            if (!ExeExistsOnDiskOrPath(exePath))
            {
                return _localizer.Format("ValidationExtToolNotFound", tool.Name.Trim(), exePath);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true if the executable exists at the given absolute path
    /// or can be found on the system PATH.
    /// </summary>
    private static bool ExeExistsOnDiskOrPath(string exePath)
    {
        if (System.IO.File.Exists(exePath)) return true;

        // Bare filename like "ping.exe" — search PATH
        if (!System.IO.Path.IsPathRooted(exePath))
        {
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? [];
            foreach (var dir in pathDirs)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var fullPath = System.IO.Path.Combine(dir.Trim(), exePath);
                if (System.IO.File.Exists(fullPath)) return true;
            }
        }

        return false;
    }

    private static SshGatewayDto CloneGateway(SshGatewayDto g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        Host = g.Host,
        Port = g.Port,
        User = g.User,
        KeyPath = g.KeyPath,
        SshPasswordEncrypted = g.SshPasswordEncrypted,
        IsDefault = g.IsDefault,
        ParentGatewayId = g.ParentGatewayId,
        HostKeyFingerprint = g.HostKeyFingerprint
    };

    private static ProjectDto CloneProject(ProjectDto p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Color = p.Color,
        DefaultSshUsername = p.DefaultSshUsername,
        DefaultSshKeyPath = p.DefaultSshKeyPath,
        DefaultGatewayId = p.DefaultGatewayId
    };
}
