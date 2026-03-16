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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        ServerListViewModel serverList,
        ConnectionViewModel connection,
        SettingsViewModel settings)
    {
        _configManager = configManager;
        _localizer = localizer;
        _connectionSm = connectionSm;
        _appStatus = appStatus;
        _tunnelManager = tunnelManager;
        ServerList = serverList;
        Connection = connection;
        Settings = settings;

        _appStatus.StatusChanged += OnApplicationStatusChanged;
        _tunnelManager.TunnelOpened += OnTunnelOpened;
        _tunnelManager.TunnelClosed += OnTunnelClosed;
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

            ServerList.LoadServers(servers, settings);
            Settings.LoadFromSettings(settings);

            _appStatus.TryTransition(ApplicationStatus.Ready);
            StatusText = _localizer["StatusReady"];
        }
        finally
        {
            IsBusy = false;
        }
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
    }

    private void OnTunnelClosed(int localPort, string? error)
    {
        TunnelCount = _tunnelManager.GetActiveTunnels().Count;
    }
}
