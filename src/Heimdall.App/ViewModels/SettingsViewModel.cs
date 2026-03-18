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

    [ObservableProperty]
    private string _plinkPath = "";

    [ObservableProperty]
    private string _defaultLocale = "en";

    [ObservableProperty]
    private string _defaultTheme = "Dark";

    [ObservableProperty]
    private bool _enableLogging = true;

    [ObservableProperty]
    private int _antiIdleInterval = 60;

    [ObservableProperty]
    private int _maxEmbeddedSessions = 10;

    [ObservableProperty]
    private string _externalEditorPath = "";

    [ObservableProperty]
    private bool _sftpAutoOpenOnSsh = true;

    [ObservableProperty]
    private string _terminalFontFamily = "Consolas";

    [ObservableProperty]
    private int _terminalFontSize = 14;

    [ObservableProperty]
    private string _terminalColorScheme = "Dracula";

    [ObservableProperty]
    private bool _sessionLoggingEnabled;

    [ObservableProperty]
    private string _sessionLogDirectory = @"logs\sessions";

    [ObservableProperty]
    private bool _useExternalCredentialProvider;

    [ObservableProperty]
    private string _credentialProviderCommand = "";

    [ObservableProperty]
    private string _credentialProviderDatabase = "";

    [ObservableProperty]
    private ObservableCollection<ExternalToolItemViewModel> _externalTools = new();

    [ObservableProperty]
    private ExternalToolItemViewModel? _selectedExternalTool;

    [ObservableProperty]
    private int _defaultResolutionWidth = 1920;

    [ObservableProperty]
    private int _defaultResolutionHeight = 1080;

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
        PlinkPath = settings.PlinkPath;
        DefaultLocale = settings.DefaultLocale;
        DefaultTheme = settings.DefaultTheme;
        EnableLogging = settings.EnableLogging;
        AntiIdleInterval = settings.AntiIdleIntervalSeconds;
        MaxEmbeddedSessions = settings.MaxEmbeddedSessions;
        ExternalEditorPath = settings.ExternalEditorPath;
        SftpAutoOpenOnSsh = settings.SftpAutoOpenOnSsh;
        TerminalFontFamily = settings.TerminalFontFamily;
        TerminalFontSize = settings.TerminalFontSize;
        TerminalColorScheme = settings.TerminalColorScheme;
        SessionLoggingEnabled = settings.SessionLoggingEnabled;
        SessionLogDirectory = settings.SessionLogDirectory;
        UseExternalCredentialProvider = settings.UseExternalCredentialProvider;
        CredentialProviderCommand = settings.CredentialProviderCommand ?? "";
        CredentialProviderDatabase = settings.CredentialProviderDatabase ?? "";
        DefaultResolutionWidth = settings.DefaultResolutionWidth;
        DefaultResolutionHeight = settings.DefaultResolutionHeight;

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

        settings.PlinkPath = PlinkPath;
        settings.DefaultLocale = DefaultLocale;
        settings.DefaultTheme = DefaultTheme;
        settings.EnableLogging = EnableLogging;
        settings.AntiIdleIntervalSeconds = AntiIdleInterval;
        settings.MaxEmbeddedSessions = MaxEmbeddedSessions;
        settings.ExternalEditorPath = ExternalEditorPath;
        settings.SftpAutoOpenOnSsh = SftpAutoOpenOnSsh;
        settings.TerminalFontFamily = TerminalFontFamily;
        settings.TerminalFontSize = TerminalFontSize;
        settings.TerminalColorScheme = TerminalColorScheme;
        settings.SessionLoggingEnabled = SessionLoggingEnabled;
        settings.SessionLogDirectory = SessionLogDirectory;
        settings.UseExternalCredentialProvider = UseExternalCredentialProvider;
        settings.CredentialProviderCommand = CredentialProviderCommand;
        settings.CredentialProviderDatabase = CredentialProviderDatabase;
        settings.DefaultResolutionWidth = DefaultResolutionWidth;
        settings.DefaultResolutionHeight = DefaultResolutionHeight;

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
    private Task ResetToDefaultsAsync(CancellationToken cancellationToken)
    {
        var defaults = new AppSettings();
        LoadFromSettings(defaults);
        IsDirty = true;
        return Task.CompletedTask;
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
                Filter = _localizer["ImportDialogFilter"]
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var json = await File.ReadAllTextAsync(dialog.FileName, cancellationToken);
            var imported = JsonSerializer.Deserialize<List<ServerProfileDto>>(json, ImportJsonOptions);

            if (imported is null || imported.Count == 0)
            {
                _dialogService.ShowInfo(
                    _localizer["ImportDialogTitle"],
                    _localizer.Format("StatusImportSuccess", 0));
                return;
            }

            var confirmed = await _dialogService.ShowConfirmAsync(
                _localizer["ConfirmImportTitle"],
                _localizer.Format("ConfirmImportMessage", imported.Count));

            if (!confirmed)
            {
                return;
            }

            var existing = await _configManager.LoadServersAsync();
            var existingIds = new HashSet<string>(existing.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);

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
            _dialogService.ShowInfo(
                _localizer["ImportDialogTitle"],
                _localizer.Format("StatusImportBreakdown", totalImported, newCount, updatedCount));

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
