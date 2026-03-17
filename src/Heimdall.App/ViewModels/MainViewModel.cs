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
using System.Windows;
using Heimdall.App.Services;
using Heimdall.App.Theming;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.StateMachine;
using Heimdall.Sftp;
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

    // Exposed for split pane session creation from code-behind
    internal ConfigManager ConfigManager => _configManager;
    internal IDialogService DialogService => _dialogService;
    internal EmbeddedSessionManager EmbeddedSessionManager => _embeddedSessionManager;

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
    private bool _isBroadcastMode;

    [ObservableProperty]
    private string _selectedTab = "Servers";

    // --- Retractable Tunnel Panel ---

    [ObservableProperty]
    private bool _isTunnelPanelOpen;

    [RelayCommand]
    private void ToggleTunnelPanel() => IsTunnelPanelOpen = !IsTunnelPanelOpen;

    // --- Command Palette (Ctrl+K) ---

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    [ObservableProperty]
    private string _paletteSearchText = "";

    [ObservableProperty]
    private ObservableCollection<ServerItemViewModel> _paletteResults = [];

    [ObservableProperty]
    private ServerItemViewModel? _selectedPaletteItem;

    /// <summary>Active tunnel snapshots for the Tunnels tab.</summary>
    [ObservableProperty]
    private ObservableCollection<TunnelInfo> _tunnelList = [];

    [ObservableProperty]
    private TunnelInfo? _selectedTunnel;

    /// <summary>Scheduled task entries for the Scheduled tab.</summary>
    [ObservableProperty]
    private ObservableCollection<ScheduledTaskDto> _scheduledTasks = [];

    [ObservableProperty]
    private ScheduledTaskDto? _selectedScheduledTask;

    /// <summary>True when the palette has no matching results.</summary>
    public bool HasNoPaletteResults => PaletteResults.Count == 0;

    /// <summary>True when there are no active tunnels.</summary>
    public bool HasNoTunnels => TunnelList.Count == 0;

    /// <summary>True when there are no scheduled tasks.</summary>
    public bool HasNoScheduledTasks => ScheduledTasks.Count == 0;

    /// <summary>True when the Servers tab is selected.</summary>
    public bool IsServersTabSelected => string.Equals(SelectedTab, "Servers", StringComparison.Ordinal);

    /// <summary>True when the Tunnels tab is selected.</summary>
    public bool IsTunnelsTabSelected => string.Equals(SelectedTab, "Tunnels", StringComparison.Ordinal);

    /// <summary>True when the Scheduled tab is selected.</summary>
    public bool IsScheduledTabSelected => string.Equals(SelectedTab, "Scheduled", StringComparison.Ordinal);

    /// <summary>True when the Settings tab is selected.</summary>
    public bool IsSettingsTabSelected => string.Equals(SelectedTab, "Settings", StringComparison.Ordinal);

    /// <summary>True when the About tab is selected.</summary>
    public bool IsAboutTabSelected => string.Equals(SelectedTab, "About", StringComparison.Ordinal);

    partial void OnPaletteResultsChanged(ObservableCollection<ServerItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasNoPaletteResults));
    }

    partial void OnTunnelListChanged(ObservableCollection<TunnelInfo> value)
    {
        OnPropertyChanged(nameof(HasNoTunnels));
    }

    partial void OnScheduledTasksChanged(ObservableCollection<ScheduledTaskDto> value)
    {
        OnPropertyChanged(nameof(HasNoScheduledTasks));
    }

    partial void OnSelectedTabChanged(string value)
    {
        Heimdall.Core.Logging.FileLogger.Info($"MainViewModel SelectedTab changed to {value}");

        OnPropertyChanged(nameof(IsServersTabSelected));
        OnPropertyChanged(nameof(IsTunnelsTabSelected));
        OnPropertyChanged(nameof(IsScheduledTabSelected));
        OnPropertyChanged(nameof(IsSettingsTabSelected));
        OnPropertyChanged(nameof(IsAboutTabSelected));

        // Refresh tunnel list when switching to Tunnels tab
        if (IsTunnelsTabSelected)
        {
            RefreshTunnelList();
        }

        // Reload scheduled tasks when switching to Scheduled tab
        if (IsScheduledTabSelected && _currentSettings is not null)
        {
            LoadScheduledTasks(_currentSettings);
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

        // Reload server list after a config import
        Settings.ConfigurationChanged += async () =>
            await ReloadConfigurationAsync(await _configManager.LoadSettingsAsync());

        // Wire broadcast relay so terminal views can fan out input
        _embeddedSessionManager.BroadcastCallback = BroadcastToAllTerminals;
        _embeddedSessionManager.IsBroadcastActive = () => IsBroadcastMode;

        // Wire SSH reconnect: close the old session tab and re-connect from scratch
        _embeddedSessionManager.ReconnectRequestedCallback = OnReconnectRequested;

        // Swap WPF theme ResourceDictionary when the user changes the theme setting
        Settings.ThemeChanged += OnThemeChanged;

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
            LoadScheduledTasks(settings);

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
    private void OnSessionReady(string serverId, string displayName, string connectionType, Core.Models.ISessionResult? session)
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

        // Auto-open SFTP alongside SSH
        if (string.Equals(connectionType, "SSH", StringComparison.OrdinalIgnoreCase)
            && _currentSettings?.SftpAutoOpenOnSsh == true)
        {
            _ = AutoOpenSftpAsync(tab, serverId);
        }
    }

    /// <summary>
    /// Closes the disconnected session tab and starts a fresh connection to
    /// the same server, reusing the standard connection flow.
    /// </summary>
    private async void OnReconnectRequested(SessionTabViewModel tab, string serverId, string connectionType)
    {
        if (string.IsNullOrEmpty(serverId))
        {
            return;
        }

        // Close the old tab (disposes the dead session)
        Connection.CloseSessionCommand.Execute(tab);

        // Re-connect through the server list's standard connection path
        var servers = await _configManager.LoadServersAsync();
        var serverDto = servers.FirstOrDefault(
            s => string.Equals(s.Id, serverId, StringComparison.Ordinal));

        if (serverDto is null)
        {
            StatusText = _localizer["ErrorServerNotFound"];
            return;
        }

        // Trigger the same flow as double-clicking the server in the tree
        var serverVm = ServerList.Servers.FirstOrDefault(
            s => string.Equals(s.Id, serverId, StringComparison.Ordinal));

        if (serverVm is not null)
        {
            ServerList.ConnectCommand.Execute(serverVm);
        }
    }

    /// <summary>
    /// Automatically connects an SFTP session and attaches it as the secondary
    /// split pane of an existing SSH session tab.
    /// </summary>
    private async Task AutoOpenSftpAsync(SessionTabViewModel tab, string serverId)
    {
        try
        {
            var servers = await _configManager.LoadServersAsync();
            var server = servers.FirstOrDefault(
                s => string.Equals(s.Id, serverId, StringComparison.Ordinal));

            if (server is null || string.IsNullOrEmpty(server.SshUsername))
            {
                Core.Logging.FileLogger.Info(
                    $"SFTP auto-open skipped for {serverId}: server not found or no SSH username.");
                return;
            }

            var sftpResult = await ServerList.ConnectionService
                .ConnectSftpAsync(server, _currentSettings!, CancellationToken.None)
                .ConfigureAwait(false);

            if (!sftpResult.Success || sftpResult.Session is null)
            {
                Core.Logging.FileLogger.Warn(
                    $"SFTP auto-open failed for {serverId}: {sftpResult.ErrorMessage}");
                return;
            }

            // Create the SFTP host control on the UI thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                tab.SecondaryHostControl = _embeddedSessionManager.CreateHostControl(
                    tab,
                    tab.Title,
                    "SFTP",
                    sftpResult.Session,
                    _currentSettings);

                tab.SecondaryServerId = serverId;
                tab.SecondaryConnectionType = "SFTP";
                tab.SplitOrientation = Core.Models.SplitOrientation.Vertical;
                tab.IsSplit = true;
            });

            Core.Logging.FileLogger.Info(
                $"SFTP auto-open succeeded for {serverId}.");
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"SFTP auto-open error for {serverId}: {ex.Message}");
        }
    }

    private void OnApplicationStatusChanged(ApplicationStatus previous, ApplicationStatus current)
    {
        var metadata = ApplicationStatusMachine.GetMetadata(current);
        StatusText = _localizer[metadata.DisplayKey];
        IsBusy = !metadata.AllowsUserAction;
    }

    /// <summary>
    /// Swaps the active theme <see cref="ResourceDictionary"/> and updates the
    /// DWM title bar to match. Called when the user changes the theme combo in Settings.
    /// </summary>
    private static void OnThemeChanged(string themeName)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var themeUri = string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase)
            ? new Uri("Themes/LightTheme.xaml", UriKind.Relative)
            : new Uri("Themes/DarkTheme.xaml", UriKind.Relative);

        var newTheme = new ResourceDictionary { Source = themeUri };

        // Find and replace the current theme dictionary
        var existing = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("Theme") == true);

        if (existing is not null)
        {
            app.Resources.MergedDictionaries.Remove(existing);
        }

        app.Resources.MergedDictionaries.Add(newTheme);

        // Update DWM dark/light title bar on every open window
        foreach (Window window in app.Windows)
        {
            WindowThemeHelper.ApplyCurrentTheme(window);
        }
    }

    private void OnTunnelOpened(TunnelInfo info)
    {
        TunnelCount = _tunnelManager.GetActiveTunnels().Count;
        RefreshTunnelList();
        IsTunnelPanelOpen = true;
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
    private void CloseTunnel(TunnelInfo? tunnel)
    {
        if (tunnel is null)
        {
            return;
        }

        _tunnelManager.CloseTunnel(tunnel.LocalPort);
        TunnelCount = _tunnelManager.GetActiveTunnels().Count;
        RefreshTunnelList();
        StatusText = $"Tunnel on port {tunnel.LocalPort} closed.";
    }

    [RelayCommand]
    private void CloseAllTunnels()
    {
        _tunnelManager.CloseAllTunnels();
        TunnelCount = 0;
        RefreshTunnelList();
        StatusText = "All tunnels closed.";
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
    private async Task AddScheduledTaskAsync(CancellationToken cancellationToken)
    {
        var serverName = await _dialogService.ShowInputAsync(
            _localizer["ScheduledTaskDialogTitleAdd"],
            _localizer["ScheduledTaskFieldServer"]);

        if (string.IsNullOrWhiteSpace(serverName))
        {
            return;
        }

        var schedule = await _dialogService.ShowInputAsync(
            _localizer["ScheduledTaskDialogTitleAdd"],
            _localizer["ScheduledTaskFieldTime"],
            "Daily 08:00");

        if (string.IsNullOrWhiteSpace(schedule))
        {
            return;
        }

        var task = new ScheduledTaskDto
        {
            Id = Guid.NewGuid().ToString(),
            ServerName = serverName,
            Schedule = schedule,
            Enabled = true,
            NextRun = DateTime.Today.AddDays(1).AddHours(8)
        };

        ScheduledTasks.Add(task);
        OnPropertyChanged(nameof(HasNoScheduledTasks));
        await SaveScheduledTasksAsync();
        StatusText = _localizer.Format("StatusScheduledTaskAdded", serverName);
    }

    [RelayCommand]
    private async Task DeleteScheduledTaskAsync(CancellationToken cancellationToken)
    {
        if (SelectedScheduledTask is null)
        {
            return;
        }

        var taskName = SelectedScheduledTask.ServerName;
        var confirmed = await _dialogService.ShowConfirmAsync(
            _localizer["ConfirmDeleteProjectTitle"],
            _localizer.Format("StatusScheduledTaskDeleted", taskName),
            "warning");

        if (!confirmed)
        {
            return;
        }

        ScheduledTasks.Remove(SelectedScheduledTask);
        OnPropertyChanged(nameof(HasNoScheduledTasks));
        await SaveScheduledTasksAsync();
        StatusText = _localizer.Format("StatusScheduledTaskDeleted", taskName);
    }

    private void LoadScheduledTasks(AppSettings settings)
    {
        ScheduledTasks = new ObservableCollection<ScheduledTaskDto>(settings.ScheduledTasks);
    }

    private async Task SaveScheduledTasksAsync()
    {
        var settings = await _configManager.LoadSettingsAsync();
        settings.ScheduledTasks = [.. ScheduledTasks];
        await _configManager.SaveSettingsAsync(settings);
    }

    /// <summary>
    /// Connects to a server and attaches the resulting session as the secondary
    /// pane of an existing session tab. Extracted from code-behind.
    /// </summary>
    public async Task SplitSessionWithServerAsync(
        SessionTabViewModel session,
        string serverId,
        Core.Models.SplitOrientation orientation)
    {
        var servers = await _configManager.LoadServersAsync();
        var settings = await _configManager.LoadSettingsAsync();

        var serverDto = servers.FirstOrDefault(
            s => string.Equals(s.Id, serverId, StringComparison.Ordinal));

        if (serverDto is null) return;

        var connService = ServerList.ConnectionService;
        Services.ConnectionResult result;
        switch (serverDto.ConnectionType?.ToUpperInvariant())
        {
            case "SSH":
                result = await connService.ConnectSshAsync(serverDto, settings);
                break;
            case "SFTP":
                result = await connService.ConnectSftpAsync(serverDto, settings);
                break;
            case "LOCAL":
                result = await connService.ConnectLocalShellAsync(serverDto, settings);
                break;
            default:
                result = await connService.ConnectRdpAsync(serverDto, settings);
                break;
        }

        if (!result.Success || result.Session is null)
        {
            StatusText = result.ErrorMessage ?? _localizer["ErrorSplitSessionFailed"];
            return;
        }

        var hostControl = _embeddedSessionManager.CreateHostControl(
            session, serverDto.DisplayName, serverDto.ConnectionType ?? "SSH",
            result.Session, settings);

        session.SecondaryHostControl = hostControl;
        session.SecondaryServerId = serverDto.Id;
        session.SecondaryConnectionType = serverDto.ConnectionType ?? "";
        session.SplitOrientation = orientation;
        session.IsSplit = true;
    }

    // --- Command Palette logic ---

    partial void OnPaletteSearchTextChanged(string value)
    {
        var query = value?.Trim() ?? "";
        if (string.IsNullOrEmpty(query))
        {
            PaletteResults = new ObservableCollection<ServerItemViewModel>(
                ServerList.Servers.Take(10));
            SelectedPaletteItem = PaletteResults.FirstOrDefault();
            return;
        }

        var matches = ServerList.Servers
            .Select(s => (Server: s, Score: FuzzyScore(s, query)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Server)
            .Take(15)
            .ToList();

        // Ad-hoc SSH URL parsing: "ssh user@host" or "user@host:port"
        var adHoc = TryParseAdHocSsh(query);
        if (adHoc is not null && matches.Count == 0)
        {
            matches.Insert(0, adHoc);
        }

        PaletteResults = new ObservableCollection<ServerItemViewModel>(matches);
        SelectedPaletteItem = PaletteResults.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenCommandPalette()
    {
        PaletteSearchText = "";
        IsCommandPaletteOpen = true;
        OnPaletteSearchTextChanged("");
        SelectedPaletteItem = PaletteResults.FirstOrDefault();
    }

    [RelayCommand]
    private void CloseCommandPalette()
    {
        IsCommandPaletteOpen = false;
    }

    [RelayCommand]
    private async Task ConnectFromPaletteAsync(ServerItemViewModel? server)
    {
        if (server is null) return;
        IsCommandPaletteOpen = false;

        if (server.Id.StartsWith("adhoc-", StringComparison.Ordinal))
        {
            await ConnectAdHocSshAsync(server);
        }
        else
        {
            ServerList.ConnectCommand.Execute(server);
        }
    }

    [RelayCommand]
    private async Task ConnectSplitFromPaletteAsync(ServerItemViewModel? server)
    {
        if (server is null) return;
        IsCommandPaletteOpen = false;

        var activeSession = Connection.ActiveSession;
        if (activeSession is not null && !server.Id.StartsWith("adhoc-", StringComparison.Ordinal))
        {
            await SplitSessionWithServerAsync(
                activeSession, server.Id, Core.Models.SplitOrientation.Vertical);
        }
        else if (server.Id.StartsWith("adhoc-", StringComparison.Ordinal))
        {
            await ConnectAdHocSshAsync(server);
        }
        else
        {
            ServerList.ConnectCommand.Execute(server);
        }
    }

    /// <summary>
    /// Connects an ad-hoc SSH server by building a temporary DTO from the
    /// palette item and calling ConnectionService directly.
    /// </summary>
    private async Task ConnectAdHocSshAsync(ServerItemViewModel server)
    {
        var dto = new ServerProfileDto
        {
            Id = server.Id,
            DisplayName = server.DisplayName,
            RemoteServer = server.RemoteServer ?? "",
            ConnectionType = "SSH",
            SshPort = 22,
            SshUsername = server.DisplayName.Contains('@')
                ? server.DisplayName.Split('@')[0]
                : ""
        };

        // Parse port from display name if present (user@host:port)
        var parts = server.DisplayName.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[1], out var port))
        {
            dto.SshPort = port;
            dto.RemoteServer = parts[0].Contains('@')
                ? parts[0].Split('@')[1]
                : parts[0];
        }

        var settings = await _configManager.LoadSettingsAsync();
        var result = await ServerList.ConnectionService.ConnectSshAsync(dto, settings);
        if (result.Success && result.Session is not null)
        {
            var tab = Connection.AddSession(dto.Id, dto.DisplayName, "SSH");
            tab.HostControl = _embeddedSessionManager.CreateHostControl(
                tab, dto.DisplayName, "SSH", result.Session, settings);
            tab.Status = "Connected";
            StatusText = _localizer.Format("StatusConnected", dto.DisplayName);
        }
        else
        {
            StatusText = result.ErrorMessage ?? _localizer["ErrorConnectionFailed"];
        }
    }

    /// <summary>
    /// Attempts to parse an ad-hoc SSH URL from the palette search text.
    /// Supports: ssh user@host, ssh user@host:port, user@host
    /// Returns a temporary ServerItemViewModel for connection, or null.
    /// </summary>
    /// <summary>
    /// Scores how well a server matches a query using fuzzy matching.
    /// Returns 0 for no match. Higher = better match.
    /// </summary>
    private static int FuzzyScore(ServerItemViewModel server, string query)
    {
        int best = 0;
        best = Math.Max(best, FuzzyScoreString(server.DisplayName, query));
        best = Math.Max(best, FuzzyScoreString(server.RemoteServer ?? "", query));
        best = Math.Max(best, FuzzyScoreString(server.Group ?? "", query) / 2); // Group matches worth less
        return best;
    }

    private static int FuzzyScoreString(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return 0;

        // Exact prefix match = highest score
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 100 + (query.Length * 10);

        // Contains = good score
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 50 + (query.Length * 5);

        // Fuzzy: all query chars appear in order (non-contiguous)
        int qi = 0;
        int consecutive = 0;
        int score = 0;

        for (int ti = 0; ti < text.Length && qi < query.Length; ti++)
        {
            if (char.ToLowerInvariant(text[ti]) == char.ToLowerInvariant(query[qi]))
            {
                qi++;
                consecutive++;
                score += consecutive * 2; // Consecutive chars score more
            }
            else
            {
                consecutive = 0;
            }
        }

        return qi == query.Length ? score : 0;
    }

    private ServerItemViewModel? TryParseAdHocSsh(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var text = input.Trim();
        if (text.StartsWith("ssh ", StringComparison.OrdinalIgnoreCase))
        {
            text = text[4..].Trim();
        }

        // Match user@host or user@host:port
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"^([^@]+)@([^:]+)(?::(\d+))?$");

        if (!match.Success) return null;

        string user = match.Groups[1].Value;
        string host = match.Groups[2].Value;
        int port = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 22;

        var vm = new ServerItemViewModel
        {
            Id = $"adhoc-{Guid.NewGuid():N}",
            DisplayName = $"{user}@{host}:{port}",
            RemoteServer = host,
            ConnectionType = "SSH",
            Group = ""
        };
        return vm;
    }

    public string Localize(string key)
    {
        return _localizer[key];
    }

    // --- Broadcast mode ---

    partial void OnIsBroadcastModeChanged(bool value)
    {
        UpdateBroadcastIndicators(value);
    }

    [RelayCommand]
    private void ToggleBroadcast()
    {
        IsBroadcastMode = !IsBroadcastMode;
        StatusText = IsBroadcastMode
            ? _localizer["BroadcastModeOn"]
            : _localizer["BroadcastModeOff"];
    }

    /// <summary>
    /// Updates the broadcast badge on all active SSH/Local terminal views.
    /// </summary>
    private void UpdateBroadcastIndicators(bool active)
    {
        foreach (var session in Connection.ActiveSessions)
        {
            if (session.HostControl is Views.EmbeddedSshView primarySsh)
            {
                primarySsh.SetBroadcastIndicator(active);
            }

            if (session.IsSplit && session.SecondaryHostControl is Views.EmbeddedSshView secondarySsh)
            {
                secondarySsh.SetBroadcastIndicator(active);
            }
        }
    }

    /// <summary>
    /// Sends raw byte input to all active terminal sessions except the originating view.
    /// Called by <see cref="Views.EmbeddedSshView"/> when broadcast mode is enabled.
    /// </summary>
    public void BroadcastToAllTerminals(byte[] data, object? sender)
    {
        if (!IsBroadcastMode)
        {
            return;
        }

        foreach (var session in Connection.ActiveSessions)
        {
            BroadcastToHostControl(session.HostControl, data, sender);

            if (session.IsSplit)
            {
                BroadcastToHostControl(session.SecondaryHostControl, data, sender);
            }
        }
    }

    private static void BroadcastToHostControl(object? hostControl, byte[] data, object? sender)
    {
        if (hostControl is Views.EmbeddedSshView sshView && sshView != sender)
        {
            try
            {
                sshView.WriteBytes(data);
            }
            catch (ObjectDisposedException)
            {
                // Session already closed; skip.
            }
        }
    }

    private async Task ReloadConfigurationAsync(AppSettings settings, List<ServerProfileDto>? servers = null)
    {
        _currentSettings = settings;
        var currentServers = servers ?? await _configManager.LoadServersAsync();

        ServerCount = currentServers.Count;
        ServerList.LoadServers(currentServers, settings);
        Settings.LoadFromSettings(settings);
        LoadScheduledTasks(settings);
        WindowTitle = _localizer.Format("WindowTitle", ServerCount);
    }
}
