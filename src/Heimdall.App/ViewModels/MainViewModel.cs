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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;

namespace Heimdall.App.ViewModels;

/// <summary>
/// Root ViewModel orchestrating the application shell: tabs, status bar,
/// and coordination between child ViewModels.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;
    private readonly LocalizationManager _localizer;
    private readonly ConnectionStateMachine _connectionSm;
    private readonly ApplicationStatusMachine _appStatus;
    private readonly TunnelManager _tunnelManager;
    private readonly HostKeyStore _hostKeyStore;
    private readonly IDialogService _dialogService;
    private readonly EmbeddedSessionManager _embeddedSessionManager;

    private AppSettings? _currentSettings;

    [ObservableProperty]
    private string _windowTitle = "Heimdall";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private int _serverCount;

    [ObservableProperty]
    private int _tunnelCount;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _selectedTab = "Servers";

    /// <summary>Active tunnel snapshots for the Tunnels tab.</summary>
    [ObservableProperty]
    private ObservableCollection<TunnelInfo> _tunnelList = [];

    /// <summary>True when the Servers tab is selected.</summary>
    public bool IsServersTabSelected => string.Equals(SelectedTab, "Servers", StringComparison.Ordinal);

    /// <summary>True when the Tunnels tab is selected.</summary>
    public bool IsTunnelsTabSelected => string.Equals(SelectedTab, "Tunnels", StringComparison.Ordinal);

    /// <summary>True when the Scheduled tab is selected.</summary>
    public bool IsScheduledTabSelected => string.Equals(SelectedTab, "Scheduled", StringComparison.Ordinal);

    /// <summary>True when the Settings tab is selected.</summary>
    public bool IsSettingsTabSelected => string.Equals(SelectedTab, "Settings", StringComparison.Ordinal);

    partial void OnSelectedTabChanged(string value)
    {
        Heimdall.Core.Logging.FileLogger.Info($"MainViewModel SelectedTab changed to {value}");

        OnPropertyChanged(nameof(IsServersTabSelected));
        OnPropertyChanged(nameof(IsTunnelsTabSelected));
        OnPropertyChanged(nameof(IsScheduledTabSelected));
        OnPropertyChanged(nameof(IsSettingsTabSelected));

        // Refresh tunnel list when switching to Tunnels tab
        if (IsTunnelsTabSelected)
        {
            RefreshTunnelList();
        }
    }

    /// <summary>
    /// Child ViewModel for the server list and filtering.
    /// </summary>
    public ServerListViewModel ServerList { get; }

    /// <summary>
    /// Child ViewModel for active embedded sessions (tabs).
    /// </summary>
    public ConnectionViewModel Connection { get; }

    /// <summary>
    /// Child ViewModel for application settings.
    /// </summary>
    public SettingsViewModel Settings { get; }

    public MainViewModel(
        ConfigManager configManager,
        LocalizationManager localizer,
        ConnectionStateMachine connectionSm,
        ApplicationStatusMachine appStatus,
        TunnelManager tunnelManager,
        HostKeyStore hostKeyStore,
        IDialogService dialogService,
        EmbeddedSessionManager embeddedSessionManager,
        ServerListViewModel serverList,
        ConnectionViewModel connection,
        SettingsViewModel settings)
    {
        _configManager = configManager;
        _localizer = localizer;
        _connectionSm = connectionSm;
        _appStatus = appStatus;
        _tunnelManager = tunnelManager;
        _hostKeyStore = hostKeyStore;
        _dialogService = dialogService;
        _embeddedSessionManager = embeddedSessionManager;
        ServerList = serverList;
        Connection = connection;
        Settings = settings;

        _appStatus.StatusChanged += OnApplicationStatusChanged;
        _tunnelManager.TunnelOpened += OnTunnelOpened;
        _tunnelManager.TunnelClosed += OnTunnelClosed;

        // Wire server list session events to the connection tab manager
        ServerList.SessionReady += OnSessionReady;
        ServerList.StatusMessageRequested += message => StatusText = message;
    }

    /// <summary>
    /// Loads settings, server inventory, and initializes the UI state.
    /// Called after the window is shown.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var settings = await _configManager.LoadSettingsAsync();
            var servers = await _configManager.LoadServersAsync();

            ServerCount = servers.Count;
            TunnelCount = _tunnelManager.GetActiveTunnels().Count;

            // Load host keys from gateway configurations into the TOFU store
            var hostKeyEntries = settings.SshGateways
                .Where(g => !string.IsNullOrEmpty(g.HostKeyFingerprint))
                .Select(g => (g.Host, g.Port, (string?)g.HostKeyFingerprint));

            _hostKeyStore.LoadFromConfig(hostKeyEntries);

            _currentSettings = settings;
            ServerList.LoadServers(servers, settings);
            Settings.LoadFromSettings(settings);

            _appStatus.TryTransition(ApplicationStatus.Ready);
            StatusText = _localizer["StatusReady"];
            WindowTitle = _localizer.Format("WindowTitle", ServerCount);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Handles the session-ready event from ServerListViewModel by creating
    /// a session tab in the ConnectionViewModel.
    /// </summary>
    private void OnSessionReady(string serverId, string displayName, string connectionType, object? session)
    {
        if (session is null)
        {
            StatusText = _localizer.Format("StatusConnected", displayName);
            return;
        }

        var tab = Connection.AddSession(serverId, displayName, connectionType);
        tab.HostControl = _embeddedSessionManager.CreateHostControl(
            tab,
            displayName,
            connectionType,
            session,
            _currentSettings);
        tab.Status = string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase)
            ? "Connecting"
            : "Connected";

        StatusText = string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase)
            ? string.Format("Opening embedded RDP session for {0}.", displayName)
            : _localizer.Format("StatusConnected", displayName);
    }

    private void OnApplicationStatusChanged(ApplicationStatus previous, ApplicationStatus current)
    {
        var metadata = ApplicationStatusMachine.GetMetadata(current);
        StatusText = _localizer[metadata.DisplayKey];
        IsBusy = !metadata.AllowsUserAction;
    }

    private void OnTunnelOpened(TunnelInfo info)
    {
        TunnelCount = _tunnelManager.GetActiveTunnels().Count;
        RefreshTunnelList();
    }

    private void OnTunnelClosed(int localPort, string? error)
    {
        TunnelCount = _tunnelManager.GetActiveTunnels().Count;
        RefreshTunnelList();
    }

    private void RefreshTunnelList()
    {
        var tunnels = _tunnelManager.GetActiveTunnels();
        TunnelList = new ObservableCollection<TunnelInfo>(tunnels);
    }

    [RelayCommand]
    private async Task AddProjectAsync(CancellationToken cancellationToken)
    {
        var dialogVm = new ProjectDialogViewModel
        {
            DialogTitle = _localizer["ProjectDialogTitleAdd"]
        };

        var result = await _dialogService.ShowProjectDialogAsync(dialogVm);
        if (result is not { Saved: true })
        {
            return;
        }

        var settings = await _configManager.LoadSettingsAsync();
        result.Project.Id = Guid.NewGuid().ToString();
        settings.Projects.Add(result.Project);

        await _configManager.SaveSettingsAsync(settings);
        await ReloadConfigurationAsync(settings);

        StatusText = _localizer.Format("StatusProjectAdded", result.Project.Name);
    }

    [RelayCommand]
    private async Task EditProjectAsync(ServerProjectViewModel? project, CancellationToken cancellationToken)
    {
        if (project is null || project.IsVirtualProject)
        {
            return;
        }

        var settings = await _configManager.LoadSettingsAsync();
        var projectDto = settings.Projects.FirstOrDefault(
            candidate => string.Equals(candidate.Id, project.ProjectId, StringComparison.Ordinal));

        if (projectDto is null)
        {
            return;
        }

        var dialogVm = ProjectDialogViewModel.FromDto(projectDto);
        dialogVm.DialogTitle = _localizer["ProjectDialogTitleEdit"];

        var result = await _dialogService.ShowProjectDialogAsync(dialogVm);
        if (result is not { Saved: true })
        {
            return;
        }

        result.Project.Id = projectDto.Id;

        var index = settings.Projects.FindIndex(
            candidate => string.Equals(candidate.Id, projectDto.Id, StringComparison.Ordinal));

        if (index < 0)
        {
            return;
        }

        settings.Projects[index] = result.Project;
        await _configManager.SaveSettingsAsync(settings);
        await ReloadConfigurationAsync(settings);

        StatusText = _localizer.Format("StatusProjectUpdated", result.Project.Name);
    }

    [RelayCommand]
    private async Task DeleteProjectAsync(ServerProjectViewModel? project, CancellationToken cancellationToken)
    {
        if (project is null || project.IsVirtualProject)
        {
            return;
        }

        var settings = await _configManager.LoadSettingsAsync();
        var servers = await _configManager.LoadServersAsync();
        var projectDto = settings.Projects.FirstOrDefault(
            candidate => string.Equals(candidate.Id, project.ProjectId, StringComparison.Ordinal));

        if (projectDto is null)
        {
            return;
        }

        var usageCount = servers.Count(
            server => string.Equals(server.ProjectId, project.ProjectId, StringComparison.Ordinal));

        var confirmationMessage = _localizer.Format("ConfirmDeleteProjectMessage", project.ProjectName);
        if (usageCount > 0)
        {
            confirmationMessage += Environment.NewLine + Environment.NewLine
                + _localizer.Format("ConfirmDeleteProjectInUse", usageCount);
        }

        var confirmed = await _dialogService.ShowConfirmAsync(
            _localizer["ConfirmDeleteProjectTitle"],
            confirmationMessage,
            "warning");

        if (!confirmed)
        {
            return;
        }

        settings.Projects.RemoveAll(
            candidate => string.Equals(candidate.Id, project.ProjectId, StringComparison.Ordinal));

        foreach (var server in servers.Where(
                     candidate => string.Equals(candidate.ProjectId, project.ProjectId, StringComparison.Ordinal)))
        {
            server.ProjectId = null;
        }

        await _configManager.SaveSettingsAsync(settings);
        await _configManager.SaveServersAsync(servers);
        await ReloadConfigurationAsync(settings, servers);

        StatusText = _localizer.Format("StatusProjectDeleted", project.ProjectName);
    }

    public string Localize(string key)
    {
        return _localizer[key];
    }

    private async Task ReloadConfigurationAsync(AppSettings settings, List<RdpServerDto>? servers = null)
    {
        _currentSettings = settings;
        var currentServers = servers ?? await _configManager.LoadServersAsync();

        ServerCount = currentServers.Count;
        ServerList.LoadServers(currentServers, settings);
        Settings.LoadFromSettings(settings);
        WindowTitle = _localizer.Format("WindowTitle", ServerCount);
    }
}
