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
/// Tracks dirty state and delegates persistence to <see cref="ConfigManager"/>.
/// </summary>
public partial class SettingsViewModel : ObservableObject
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

    private readonly ConfigManager _configManager;
    private readonly LocalizationManager _localizer;
    private readonly IDialogService _dialogService;

    // --- General ---

    [ObservableProperty]
    private string _defaultLocale = "en";

    [ObservableProperty]
    private string _defaultTheme = "Dark";

    [ObservableProperty]
    private int _maxEmbeddedSessions = 10;

    [ObservableProperty]
    private bool _preventSleepDuringSession = true;

    [ObservableProperty]
    private string _externalEditorPath = "";

    // --- Terminal ---

    [ObservableProperty]
    private string _terminalFontFamily = "Consolas";

    [ObservableProperty]
    private int _terminalFontSize = 14;

    [ObservableProperty]
    private string _terminalColorScheme = "Dracula";

    // --- SSH & SFTP ---

    [ObservableProperty]
    private string _plinkPath = "";

    [ObservableProperty]
    private string _sshDefaultMode = "Embedded";

    [ObservableProperty]
    private int _antiIdleInterval = 60;

    [ObservableProperty]
    private int _sshTmoutResetInterval = 240;

    [ObservableProperty]
    private bool _sftpAutoOpenOnSsh = true;

    [ObservableProperty]
    private string _x11ServerPath = "";

    [ObservableProperty]
    private bool _x11AutoStart = true;

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

    [ObservableProperty]
    private string _credentialProviderCommand = "";

    [ObservableProperty]
    private string _credentialProviderDatabase = "";

    [ObservableProperty]
    private bool _requireCredentialGuard;

    // --- Advanced / Logging ---

    [ObservableProperty]
    private bool _enableLogging = true;

    [ObservableProperty]
    private bool _sessionLoggingEnabled;

    [ObservableProperty]
    private string _sessionLogDirectory = @"logs\sessions";

    [ObservableProperty]
    private int _tunnelEstablishmentDelayMs = 2500;

    [ObservableProperty]
    private int _embeddedRdpTimeoutMs = 30000;

    // --- Collections ---

    [ObservableProperty]
    private ObservableCollection<ExternalToolItemViewModel> _externalTools = new();

    [ObservableProperty]
    private ExternalToolItemViewModel? _selectedExternalTool;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private ObservableCollection<GatewayItemViewModel> _gateways = new();

    [ObservableProperty]
    private GatewayItemViewModel? _selectedGateway;

    [ObservableProperty]
    private ObservableCollection<ProjectItemViewModel> _projects = new();

    [ObservableProperty]
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
        ConfigManager configManager,
        LocalizationManager localizer,
        IDialogService dialogService)
    {
        _configManager = configManager;
        _localizer = localizer;
        _dialogService = dialogService;
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
        MaxEmbeddedSessions = settings.MaxEmbeddedSessions;
        PreventSleepDuringSession = settings.PreventSleepDuringSession;
        ExternalEditorPath = settings.ExternalEditorPath;

        // Terminal
        TerminalFontFamily = settings.TerminalFontFamily;
        TerminalFontSize = settings.TerminalFontSize;
        TerminalColorScheme = settings.TerminalColorScheme;

        // SSH & SFTP
        PlinkPath = settings.PlinkPath;
        SshDefaultMode = settings.SshDefaultMode;
        AntiIdleInterval = settings.AntiIdleIntervalSeconds;
        SshTmoutResetInterval = settings.SshTmoutResetIntervalSeconds;
        SftpAutoOpenOnSsh = settings.SftpAutoOpenOnSsh;
        X11ServerPath = settings.X11ServerPath ?? "";
        X11AutoStart = settings.X11AutoStart;

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

        ExternalTools = new ObservableCollection<ExternalToolItemViewModel>(
            settings.ExternalTools.Select(t => new ExternalToolItemViewModel
            {
                Name = t.Name,
                ExecutablePath = t.ExecutablePath,
                Arguments = t.Arguments
            }));

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

        IsDirty = false;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var settings = await _configManager.LoadSettingsAsync();

        // General
        settings.DefaultLocale = DefaultLocale;
        settings.DefaultTheme = DefaultTheme;
        settings.MaxEmbeddedSessions = MaxEmbeddedSessions;
        settings.PreventSleepDuringSession = PreventSleepDuringSession;
        settings.ExternalEditorPath = ExternalEditorPath;

        // Terminal
        settings.TerminalFontFamily = TerminalFontFamily;
        settings.TerminalFontSize = TerminalFontSize;
        settings.TerminalColorScheme = TerminalColorScheme;

        // SSH & SFTP
        settings.PlinkPath = PlinkPath;
        settings.SshDefaultMode = SshDefaultMode;
        settings.AntiIdleIntervalSeconds = AntiIdleInterval;
        settings.SshTmoutResetIntervalSeconds = SshTmoutResetInterval;
        settings.SftpAutoOpenOnSsh = SftpAutoOpenOnSsh;
        settings.X11ServerPath = string.IsNullOrWhiteSpace(X11ServerPath) ? null : X11ServerPath;
        settings.X11AutoStart = X11AutoStart;

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

        settings.ExternalTools = ExternalTools.Select(t => new ExternalToolDefinition
        {
            Name = t.Name,
            ExecutablePath = t.ExecutablePath,
            Arguments = t.Arguments
        }).ToList();

        await _configManager.SaveSettingsAsync(settings);

        if (!string.Equals(_localizer.CurrentLocale, DefaultLocale, StringComparison.OrdinalIgnoreCase))
        {
            await _localizer.SwitchLocaleAsync(DefaultLocale);
        }

        IsDirty = false;
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

            var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            List<ServerProfileDto> imported;
            List<string>? importWarnings = null;

            if (ext is ".mxtsessions" or ".ini" or ".mobaconf")
            {
                // MobaXterm files are often Windows-1252 encoded
                var bytes = await File.ReadAllBytesAsync(dialog.FileName, cancellationToken);
                var content = HasUtf8Bom(bytes)
                    ? System.Text.Encoding.UTF8.GetString(bytes)
                    : System.Text.Encoding.GetEncoding(1252).GetString(bytes);
                var mobaResult = MobaXtermImporter.Parse(content);
                imported = mobaResult.Servers;
                importWarnings = mobaResult.Warnings;
            }
            else if (ext is ".rdp")
            {
                var content = await File.ReadAllTextAsync(dialog.FileName, cancellationToken);
                var rdpServer = RdpFileImporter.Parse(content, Path.GetFileName(dialog.FileName));
                imported = rdpServer is not null ? [rdpServer] : [];
            }
            else if (ext is ".rdg")
            {
                var content = await File.ReadAllTextAsync(dialog.FileName, cancellationToken);
                var rdcResult = RdcManImporter.Parse(content);
                imported = rdcResult.Servers;
                importWarnings = rdcResult.Warnings;
            }
            else if (ext is ".xml")
            {
                var content = await File.ReadAllTextAsync(dialog.FileName, cancellationToken);
                // Detect mRemoteNG format by looking for Connections root or Node elements
                if (content.Contains("<Connections", StringComparison.OrdinalIgnoreCase)
                    || content.Contains("Type=\"Connection\"", StringComparison.OrdinalIgnoreCase))
                {
                    var mrnResult = MRemoteNgImporter.Parse(content);
                    imported = mrnResult.Servers;
                    importWarnings = mrnResult.Warnings;
                }
                else
                {
                    // Try RDCMan format
                    var rdcResult = RdcManImporter.Parse(content);
                    imported = rdcResult.Servers;
                    importWarnings = rdcResult.Warnings;
                }
            }
            else
            {
                var json = await File.ReadAllTextAsync(dialog.FileName, cancellationToken);
                imported = JsonSerializer.Deserialize<List<ServerProfileDto>>(json, ImportJsonOptions)
                    ?? [];
            }

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
        // Populate parent gateway options (exclude self)
        var settings = await _configManager.LoadSettingsAsync();
        vm.AvailableParents = new ObservableCollection<GatewayOption>(
            settings.SshGateways.Select(g => new GatewayOption(g.Id, $"{g.Name} ({g.Host})")));

        var result = await _dialogService.ShowGatewayDialogAsync(vm);
        if (result?.Saved == true)
        {
            result.Gateway.Id = Guid.NewGuid().ToString();
            settings.SshGateways.Add(result.Gateway);
            await _configManager.SaveSettingsAsync(settings);

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
        }
    }

    [RelayCommand]
    private async Task EditGatewayAsync(CancellationToken cancellationToken)
    {
        if (SelectedGateway == null) return;

        var settings = await _configManager.LoadSettingsAsync();
        var gwDto = settings.SshGateways.FirstOrDefault(g => g.Id == SelectedGateway.Id);
        if (gwDto == null) return;

        var vm = GatewayDialogViewModel.FromDto(gwDto);
        vm.AvailableParents = new ObservableCollection<GatewayOption>(
            settings.SshGateways
                .Where(g => g.Id != gwDto.Id)
                .Select(g => new GatewayOption(g.Id, $"{g.Name} ({g.Host})")));

        var result = await _dialogService.ShowGatewayDialogAsync(vm);
        if (result?.Saved == true)
        {
            var idx = settings.SshGateways.FindIndex(g => g.Id == gwDto.Id);
            if (idx >= 0)
            {
                result.Gateway.Id = gwDto.Id;
                settings.SshGateways[idx] = result.Gateway;
                await _configManager.SaveSettingsAsync(settings);

                SelectedGateway.Name = result.Gateway.Name;
                SelectedGateway.Host = result.Gateway.Host;
                SelectedGateway.Port = result.Gateway.Port;
                SelectedGateway.User = result.Gateway.User;
                SelectedGateway.HasKey = !string.IsNullOrEmpty(result.Gateway.KeyPath);
                SelectedGateway.HasPassword = !string.IsNullOrEmpty(result.Gateway.SshPasswordEncrypted);
            }
        }
    }

    [RelayCommand]
    private async Task DeleteGatewayAsync(CancellationToken cancellationToken)
    {
        if (SelectedGateway == null) return;

        var confirmed = await _dialogService.ShowConfirmAsync(
            "Delete Gateway",
            $"Delete gateway '{SelectedGateway.Name}'? Servers using this gateway will lose their tunnel configuration.",
            "danger");

        if (!confirmed) return;

        var settings = await _configManager.LoadSettingsAsync();
        settings.SshGateways.RemoveAll(g => g.Id == SelectedGateway.Id);
        await _configManager.SaveSettingsAsync(settings);

        Gateways.Remove(SelectedGateway);
        SelectedGateway = null;
    }

    [RelayCommand]
    private async Task AddProjectAsync(CancellationToken cancellationToken)
    {
        var vm = new ProjectDialogViewModel
        {
            DialogTitle = _localizer["ProjectDialogTitleAdd"]
        };

        var result = await _dialogService.ShowProjectDialogAsync(vm);
        if (result is not { Saved: true })
        {
            return;
        }

        var settings = await _configManager.LoadSettingsAsync();
        result.Project.Id = Guid.NewGuid().ToString();
        settings.Projects.Add(result.Project);
        await _configManager.SaveSettingsAsync(settings);

        Projects.Add(new ProjectItemViewModel
        {
            Id = result.Project.Id,
            Name = result.Project.Name,
            Color = result.Project.Color ?? "#3B82F6",
            Description = result.Project.Description ?? ""
        });

        ConfigurationChanged?.Invoke();
    }

    [RelayCommand]
    private async Task EditProjectAsync(CancellationToken cancellationToken)
    {
        if (SelectedProject is null) return;

        var settings = await _configManager.LoadSettingsAsync();
        var projectDto = settings.Projects.FirstOrDefault(p => p.Id == SelectedProject.Id);
        if (projectDto is null) return;

        var vm = ProjectDialogViewModel.FromDto(projectDto);
        vm.DialogTitle = _localizer["ProjectDialogTitleEdit"];

        var result = await _dialogService.ShowProjectDialogAsync(vm);
        if (result is not { Saved: true }) return;

        var idx = settings.Projects.FindIndex(p => p.Id == projectDto.Id);
        if (idx >= 0)
        {
            result.Project.Id = projectDto.Id;
            settings.Projects[idx] = result.Project;
            await _configManager.SaveSettingsAsync(settings);

            SelectedProject.Name = result.Project.Name;
            SelectedProject.Color = result.Project.Color ?? "#3B82F6";
            SelectedProject.Description = result.Project.Description ?? "";
        }

        ConfigurationChanged?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteProjectAsync(CancellationToken cancellationToken)
    {
        if (SelectedProject is null) return;

        var settings = await _configManager.LoadSettingsAsync();
        var servers = await _configManager.LoadServersAsync();
        var usageCount = servers.Count(s =>
            string.Equals(s.ProjectId, SelectedProject.Id, StringComparison.Ordinal));

        var message = usageCount > 0
            ? _localizer.Format("ConfirmDeleteProjectInUse", usageCount)
                + "\n" + _localizer.Format("ConfirmDeleteProjectMessage", SelectedProject.Name)
            : _localizer.Format("ConfirmDeleteProjectMessage", SelectedProject.Name);

        var confirmed = await _dialogService.ShowConfirmAsync(
            _localizer["ConfirmDeleteProjectTitle"],
            message,
            "danger");

        if (!confirmed) return;

        settings.Projects.RemoveAll(p => p.Id == SelectedProject.Id);

        // Unassign servers from the deleted project
        foreach (var server in servers.Where(s =>
            string.Equals(s.ProjectId, SelectedProject.Id, StringComparison.Ordinal)))
        {
            server.ProjectId = null;
        }

        await _configManager.SaveSettingsAsync(settings);
        await _configManager.SaveServersAsync(servers);

        Projects.Remove(SelectedProject);
        SelectedProject = null;

        ConfigurationChanged?.Invoke();
    }

    [RelayCommand]
    private async Task AddExternalToolAsync(CancellationToken cancellationToken)
    {
        var name = await _dialogService.ShowInputAsync(
            _localizer["ExternalToolDialogTitle"],
            _localizer["ExternalToolPromptName"]);
        if (string.IsNullOrWhiteSpace(name)) return;

        var executablePath = await _dialogService.ShowInputAsync(
            _localizer["ExternalToolDialogTitle"],
            _localizer["ExternalToolPromptPath"]);
        if (string.IsNullOrWhiteSpace(executablePath)) return;

        var arguments = await _dialogService.ShowInputAsync(
            _localizer["ExternalToolDialogTitle"],
            _localizer["ExternalToolPromptArguments"],
            "{Host} {Port} {User}");

        ExternalTools.Add(new ExternalToolItemViewModel
        {
            Name = name,
            ExecutablePath = executablePath,
            Arguments = arguments ?? ""
        });

        IsDirty = true;
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

    partial void OnDefaultThemeChanged(string value)
    {
        ThemeChanged?.Invoke(value);
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Mark dirty when any settings property changes (except IsDirty itself)
        if (e.PropertyName is not (nameof(IsDirty)))
        {
            IsDirty = true;
        }
    }
}
