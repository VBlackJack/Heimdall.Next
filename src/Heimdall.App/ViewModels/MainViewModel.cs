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
    private readonly TaskSchedulerService _taskScheduler;

    private AppSettings? _currentSettings;

    // Exposed for split pane session creation and context menu building from code-behind
    internal ConfigManager ConfigManager => _configManager;
    internal IDialogService DialogService => _dialogService;
    internal EmbeddedSessionManager EmbeddedSessionManager => _embeddedSessionManager;
    internal AppSettings? CurrentSettings => _currentSettings;

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

    /// <summary>
    /// When non-null, the Command Palette is in "split mode": selecting a server
    /// will split the specified session instead of opening a new tab.
    /// </summary>
    private SessionTabViewModel? _splitPaletteSession;
    private Core.Models.SplitOrientation _splitPaletteOrientation;

    [ObservableProperty]
    private string _palettePlaceholder = "";

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

        _taskScheduler = new TaskSchedulerService
        {
            TasksProvider = () => ScheduledTasks.ToList(),
            TaskDueCallback = OnScheduledTaskDueAsync,
            PersistCallback = SaveScheduledTasksAsync
        };

        _appStatus.StatusChanged += OnApplicationStatusChanged;
        _tunnelManager.TunnelOpened += OnTunnelOpened;
        _tunnelManager.TunnelClosed += OnTunnelClosed;

        // Keep _currentSettings in sync when settings are saved elsewhere
        _configManager.SettingsChanged += OnSettingsChanged;

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

            // Compute NextRun for any tasks that lack it (e.g., migrated from old format)
            var now = DateTime.Now;
            foreach (var task in ScheduledTasks)
            {
                if (task.NextRun is null && task.Enabled)
                {
                    TaskSchedulerService.ComputeNextRun(task, now);
                }
            }

            _taskScheduler.Start();

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
    private void OnSessionReady(string sessionId, string originalServerId, string displayName, string connectionType, Core.Models.ISessionResult? session)
    {
        Core.Logging.ConnectionHistory.RecordConnect(originalServerId, displayName, connectionType);

        if (session is null)
        {
            StatusText = _localizer.Format("StatusConnected", displayName);
            return;
        }

        var tab = Connection.AddSession(sessionId, displayName, connectionType);
        tab.OriginalServerId = originalServerId;
        tab.HostControl = _embeddedSessionManager.CreateHostControl(
            tab,
            displayName,
            connectionType,
            session,
            _currentSettings);
        tab.Status = string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase)
            ? _localizer["StatusConnectingProgress"]
            : _localizer["StatusConnected"];

        // Resolve tunnel chain route for visual display in session header
        // (uses sessionId — correct for state machine lookup)
        tab.TunnelRoute = ResolveTunnelRoute(sessionId);

        StatusText = string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase)
            ? _localizer.Format("StatusEmbeddedRdpOpening", displayName)
            : _localizer.Format("StatusConnected", displayName);

        // Auto-open SFTP alongside SSH — use original server ID for inventory lookup
        if (string.Equals(connectionType, "SSH", StringComparison.OrdinalIgnoreCase)
            && _currentSettings?.SftpAutoOpenOnSsh == true)
        {
            _ = AutoOpenSftpAsync(tab, originalServerId);
        }
    }

    /// <summary>
    /// Closes the disconnected session tab and starts a fresh connection to
    /// the same server, reusing the standard connection flow.
    /// </summary>
    private void OnReconnectRequested(SessionTabViewModel tab, string serverId, string connectionType)
    {
        _ = OnReconnectRequestedAsync(tab, serverId, connectionType);
    }

    private async Task OnReconnectRequestedAsync(SessionTabViewModel tab, string serverId, string connectionType)
    {
        if (string.IsNullOrEmpty(serverId))
        {
            return;
        }

        try
        {
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
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Reconnect failed for {serverId}", ex);
            StatusText = _localizer.Format("StatusReconnectFailed", ex.Message);
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
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusText = _localizer.Format("StatusSftpAutoOpenFailed", sftpResult.ErrorMessage ?? ""));
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
                tab.SecondaryOriginalServerId = tab.OriginalServerId;
                tab.SecondaryConnectionType = "SFTP";
                tab.SecondaryTitle = tab.Title;
                tab.SecondaryStatus = "Connected";
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

    private void OnSettingsChanged(AppSettings settings)
    {
        _currentSettings = settings;
        SleepPrevention.Enabled = settings.PreventSleepDuringSession;
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

        if (!string.IsNullOrEmpty(error))
        {
            StatusText = _localizer.Format("StatusTunnelClosed", localPort) + $" ({error})";
        }
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

        _tunnelManager.ForceCloseTunnel(tunnel.LocalPort);
        TunnelCount = _tunnelManager.GetActiveTunnels().Count;
        RefreshTunnelList();
        StatusText = _localizer.Format("StatusTunnelClosed", tunnel.LocalPort);
    }

    [RelayCommand]
    private void CloseAllTunnels()
    {
        _tunnelManager.CloseAllTunnels();
        TunnelCount = 0;
        RefreshTunnelList();
        StatusText = _localizer["StatusAllTunnelsClosed"];
    }

    [RelayCommand]
    private void CopyTunnelPort(TunnelInfo? tunnel)
    {
        if (tunnel is null)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(tunnel.LocalPort.ToString());
            StatusText = _localizer.Format("StatusPortCopied", tunnel.LocalPort);
        }
        catch (Exception ex) { Core.Logging.FileLogger.Warn($"[MainViewModel] clipboard copy: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task AddScheduledTaskAsync(CancellationToken cancellationToken)
    {
        // Step 1: Ask for server name (ideally from existing servers)
        var serverName = await _dialogService.ShowInputAsync(
            _localizer["ScheduledTaskDialogTitleAdd"],
            _localizer["ScheduledTaskFieldServer"]);

        if (string.IsNullOrWhiteSpace(serverName))
        {
            return;
        }

        // Resolve server ID and connection type from the server inventory
        var servers = await _configManager.LoadServersAsync();
        var serverDto = servers.FirstOrDefault(
            s => string.Equals(s.DisplayName, serverName, StringComparison.OrdinalIgnoreCase));

        var serverId = serverDto?.Id ?? string.Empty;
        var connectionType = serverDto?.ConnectionType ?? "SSH";

        // Step 2: Ask for schedule type ("Daily HH:mm" or "Every N min")
        var scheduleInput = await _dialogService.ShowInputAsync(
            _localizer["ScheduledTaskDialogTitleAdd"],
            _localizer["ScheduledTaskFieldTime"],
            "Daily 08:00");

        if (string.IsNullOrWhiteSpace(scheduleInput))
        {
            return;
        }

        var task = new ScheduledTaskDto
        {
            Id = Guid.NewGuid().ToString(),
            ServerId = serverId,
            ServerName = serverDto?.DisplayName ?? serverName,
            ConnectionType = connectionType,
            Enabled = true
        };

        // Parse the schedule input to determine type and parameters
        ParseScheduleInput(scheduleInput, task);
        TaskSchedulerService.ComputeNextRun(task, DateTime.Now);

        ScheduledTasks.Add(task);
        OnPropertyChanged(nameof(HasNoScheduledTasks));
        await SaveScheduledTasksAsync();
        StatusText = _localizer.Format("StatusScheduledTaskAdded", task.ServerName);
    }

    /// <summary>
    /// Parses user input like "Daily 08:00" or "Every 30 min" into structured DTO fields.
    /// </summary>
    private static void ParseScheduleInput(string input, ScheduledTaskDto task)
    {
        var trimmed = input.Trim();

        // Check for interval pattern: "Every N min" or just a number
        if (trimmed.StartsWith("Every ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[1], out var minutes) && minutes > 0)
            {
                task.ScheduleType = nameof(Core.Models.ScheduleType.Interval);
                task.IntervalMinutes = minutes;
                task.Schedule = $"Every {minutes} min";
                return;
            }
        }

        if (int.TryParse(trimmed, out var intervalOnly) && intervalOnly > 0)
        {
            task.ScheduleType = nameof(Core.Models.ScheduleType.Interval);
            task.IntervalMinutes = intervalOnly;
            task.Schedule = $"Every {intervalOnly} min";
            return;
        }

        // Default: Daily schedule — extract time part
        task.ScheduleType = nameof(Core.Models.ScheduleType.Daily);

        // Try "Daily HH:mm" format
        if (trimmed.StartsWith("Daily ", StringComparison.OrdinalIgnoreCase))
        {
            task.TimeOfDay = trimmed[6..].Trim();
        }
        else
        {
            // Assume the input is just HH:mm
            task.TimeOfDay = trimmed;
        }

        // Validate the time format; default to 08:00 if invalid
        if (!TimeSpan.TryParse(task.TimeOfDay, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            task.TimeOfDay = "08:00";
        }

        task.Schedule = $"Daily {task.TimeOfDay}";
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
            _localizer["ConfirmDeleteScheduledTaskTitle"],
            _localizer.Format("ConfirmDeleteScheduledTaskMessage", taskName),
            "danger");

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
    /// Called by <see cref="TaskSchedulerService"/> when a scheduled task is due.
    /// Resolves the target server and triggers the standard connection flow.
    /// </summary>
    private async Task OnScheduledTaskDueAsync(ScheduledTaskDto task)
    {
        Core.Logging.FileLogger.Info(
            $"Executing scheduled task '{task.ServerName}' (serverId={task.ServerId}, type={task.ConnectionType}).");

        StatusText = _localizer.Format("StatusScheduledTaskTriggered", task.ServerName, task.ConnectionType);

        // Find the server in the current server list by ID or name fallback
        var server = ServerList.Servers.FirstOrDefault(
            s => !string.IsNullOrEmpty(task.ServerId)
                 && string.Equals(s.Id, task.ServerId, StringComparison.Ordinal))
            ?? ServerList.Servers.FirstOrDefault(
                s => string.Equals(s.DisplayName, task.ServerName, StringComparison.OrdinalIgnoreCase));

        if (server is null)
        {
            Core.Logging.FileLogger.Warn(
                $"Scheduled task '{task.ServerName}': server not found in inventory. Skipping.");
            StatusText = _localizer.Format("ErrorScheduledTaskFailed",
                $"Server '{task.ServerName}' not found");
            return;
        }

        try
        {
            ServerList.ConnectCommand.Execute(server);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error(
                $"Scheduled task '{task.ServerName}' connection failed: {ex.Message}");
            StatusText = _localizer.Format("ErrorScheduledTaskFailed", ex.Message);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the scheduler. Called during application shutdown.
    /// </summary>
    public void StopScheduler()
    {
        _taskScheduler.Stop();
        _taskScheduler.Dispose();
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
            case "TELNET":
                result = await connService.ConnectTelnetAsync(serverDto, settings);
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
        session.SecondaryOriginalServerId = serverDto.Id;
        session.SecondaryConnectionType = serverDto.ConnectionType ?? "";
        session.SecondaryTitle = serverDto.DisplayName;
        session.SecondaryStatus = "Connected";
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

        // Bare IP / hostname: propose SSH and RDP ad-hoc connections
        if (matches.Count == 0 && LooksLikeHostOrIp(query))
        {
            matches.Add(new ServerItemViewModel
            {
                Id = $"adhoc-ssh-{query}",
                DisplayName = _localizer.Format("QuickConnectSshTo", query),
                RemoteServer = query,
                ConnectionType = "SSH",
                Group = ""
            });
            matches.Add(new ServerItemViewModel
            {
                Id = $"adhoc-rdp-{query}",
                DisplayName = _localizer.Format("QuickConnectRdpTo", query),
                RemoteServer = query,
                ConnectionType = "RDP",
                Group = ""
            });
        }

        PaletteResults = new ObservableCollection<ServerItemViewModel>(matches);
        SelectedPaletteItem = PaletteResults.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenCommandPalette()
    {
        _splitPaletteSession = null;
        PalettePlaceholder = _localizer["QuickConnectShortcut"];
        PaletteSearchText = "";
        IsCommandPaletteOpen = true;
        OnPaletteSearchTextChanged("");
        SelectedPaletteItem = PaletteResults.FirstOrDefault();
    }

    /// <summary>
    /// Opens the Command Palette in "split mode". Selecting a server will split
    /// the given session instead of opening a new tab. This replaces the old
    /// ContextMenu approach that did not scale beyond ~20 servers.
    /// </summary>
    public void OpenSplitPalette(SessionTabViewModel session, Core.Models.SplitOrientation orientation)
    {
        _splitPaletteSession = session;
        _splitPaletteOrientation = orientation;
        PalettePlaceholder = _localizer["SplitPaletteHint"];
        PaletteSearchText = "";
        IsCommandPaletteOpen = true;
        OnPaletteSearchTextChanged("");
        SelectedPaletteItem = PaletteResults.FirstOrDefault();
    }

    [RelayCommand]
    private void CloseCommandPalette()
    {
        _splitPaletteSession = null;
        IsCommandPaletteOpen = false;
    }

    [RelayCommand]
    private async Task ConnectFromPaletteAsync(ServerItemViewModel? server)
    {
        if (server is null) return;

        var splitSession = _splitPaletteSession;
        var splitOrientation = _splitPaletteOrientation;
        _splitPaletteSession = null;
        IsCommandPaletteOpen = false;

        // If the palette was opened in split mode, route to split logic
        if (splitSession is not null && !server.Id.StartsWith("adhoc-", StringComparison.Ordinal))
        {
            await SplitSessionWithServerAsync(splitSession, server.Id, splitOrientation);
            return;
        }

        if (server.Id.StartsWith("adhoc-", StringComparison.Ordinal))
        {
            await ConnectAdHocAsync(server);
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
            await ConnectAdHocAsync(server);
        }
        else
        {
            ServerList.ConnectCommand.Execute(server);
        }
    }

    /// <summary>
    /// Connects an ad-hoc server (SSH or RDP) by building a temporary DTO
    /// from the palette item and calling ConnectionService directly.
    /// </summary>
    private async Task ConnectAdHocAsync(ServerItemViewModel server)
    {
        var connType = server.ConnectionType?.ToUpperInvariant() ?? "SSH";

        var dto = new ServerProfileDto
        {
            Id = server.Id,
            DisplayName = server.DisplayName,
            RemoteServer = server.RemoteServer ?? "",
            ConnectionType = connType,
        };

        if (connType == "SSH")
        {
            dto.SshPort = 22;
            dto.SshUsername = server.DisplayName.Contains('@')
                ? server.DisplayName.Split('@')[0]
                : "";

            // Parse port from display name if present (user@host:port)
            var parts = server.DisplayName.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var port))
            {
                dto.SshPort = port;
                dto.RemoteServer = parts[0].Contains('@')
                    ? parts[0].Split('@')[1]
                    : parts[0];
            }
        }
        else if (connType == "RDP")
        {
            dto.RemotePort = 3389;
        }

        var settings = await _configManager.LoadSettingsAsync();
        ConnectionResult result;

        if (connType == "RDP")
        {
            result = await ServerList.ConnectionService.ConnectRdpAsync(dto, settings);
        }
        else
        {
            result = await ServerList.ConnectionService.ConnectSshAsync(dto, settings);
        }

        if (result.Success && result.Session is not null)
        {
            var tab = Connection.AddSession(dto.Id, dto.DisplayName, connType);
            tab.HostControl = _embeddedSessionManager.CreateHostControl(
                tab, dto.DisplayName, connType, result.Session, settings);
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

    /// <summary>
    /// Returns true when the input looks like a bare IP address or hostname
    /// (no spaces, no protocol prefix, alphanumeric with dots and hyphens).
    /// </summary>
    private static bool LooksLikeHostOrIp(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Contains(' '))
            return false;
        return System.Net.IPAddress.TryParse(input, out _)
            || System.Text.RegularExpressions.Regex.IsMatch(
                input, @"^[a-zA-Z0-9][a-zA-Z0-9.\-]*$");
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

    /// <summary>
    /// Provides access to the localization manager for components that need
    /// to resolve i18n keys outside the view model (e.g., floating windows).
    /// </summary>
    public LocalizationManager GetLocalizer() => _localizer;

    /// <summary>
    /// Resolves the tunnel chain route for a server, returning a display string
    /// like "via GatewayA" or "via GatewayA → GatewayB" for chained tunnels.
    /// Returns empty string for direct connections.
    /// </summary>
    private string ResolveTunnelRoute(string serverId)
    {
        if (_currentSettings is null) return "";

        var stateData = _connectionSm.GetStateData(serverId);
        if (stateData?.TunnelLocalPort is null) return "";

        // Find which gateway hosts this tunnel by matching the tunnel's ServerName
        var tunnels = _tunnelManager.GetActiveTunnels();
        var tunnel = tunnels.FirstOrDefault(t => t.LocalPort == stateData.TunnelLocalPort);
        if (tunnel is null) return "";

        var gatewayId = _currentSettings.SshGateways
            .FirstOrDefault(g => string.Equals(g.Host, tunnel.ServerName, StringComparison.OrdinalIgnoreCase))?.Id;

        if (string.IsNullOrEmpty(gatewayId))
            return $"via {tunnel.ServerName}";

        // Walk the gateway chain to build the full route
        var names = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!string.IsNullOrEmpty(gatewayId) && visited.Add(gatewayId))
        {
            var gw = _currentSettings.SshGateways.FirstOrDefault(
                g => string.Equals(g.Id, gatewayId, StringComparison.OrdinalIgnoreCase));
            if (gw is null) break;
            names.Add(string.IsNullOrWhiteSpace(gw.Name) ? gw.Host : gw.Name);
            gatewayId = gw.ParentGatewayId;
        }

        if (names.Count == 0) return "";
        names.Reverse();
        return "via " + string.Join(" \u2192 ", names);
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

    internal async Task ReloadConfigurationAsync(AppSettings settings, List<ServerProfileDto>? servers = null)
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
