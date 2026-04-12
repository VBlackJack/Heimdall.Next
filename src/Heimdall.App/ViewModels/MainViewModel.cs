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
using Heimdall.Sftp;
using Heimdall.Ssh;

namespace Heimdall.App.ViewModels;

/// <summary>
/// Root ViewModel orchestrating the application shell: tabs, status bar,
/// and coordination between child ViewModels.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly LocalizationManager _localizer;
    private readonly ConnectionStateMachine _connectionSm;
    private readonly ApplicationStatusMachine _appStatus;
    private readonly TunnelManager _tunnelManager;
    private readonly HostKeyStore _hostKeyStore;
    private readonly IDialogService _dialogService;
    private readonly EmbeddedSessionManager _embeddedSessionManager;
    private readonly ThemeService _themeService;
    private readonly TaskSchedulerService _taskScheduler;

    private bool _disposed;
    private Action? _onConfigurationChanged;
    private Action<string, string, Core.Models.ToolContext>? _onToolSessionRequested;
    private Action<string>? _onStatusMessageRequested;

    private AppSettings? _currentSettings;

    /// <summary>Split/merge service handling all pane operations.</summary>
    public SplitService Split { get; }

    // Exposed for split pane session creation and context menu building from code-behind
    internal ConfigManager ConfigManager => _configManager;
    internal IDialogService DialogService => _dialogService;
    internal EmbeddedSessionManager EmbeddedSessionManager => _embeddedSessionManager;
    internal AppSettings? CurrentSettings => _currentSettings;

    [ObservableProperty]
    private string _windowTitle = "";

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

    /// <summary>
    /// Monotonic counter bumped after every theme swap. Bound as a trigger value
    /// by <c>MultiBinding</c>s that need to re-run their brush-resolving converters
    /// when the active theme changes (see e.g. <c>ConnectionTypeToColorConverter</c>
    /// in the server TreeView).
    /// </summary>
    [ObservableProperty]
    private int _themeRevision;

    [ObservableProperty]
    private string _selectedTab = "Servers";

    private string _previousTab = "Servers";
    private bool _suppressTabChangeGuard;

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
    private string? _splitPalettePaneId;

    /// <summary>
    /// Indicates whether the Command Palette is currently in split mode (selecting
    /// a server will split an existing session rather than opening a new tab).
    /// </summary>
    public bool IsPaletteInSplitMode => _splitPaletteSession is not null;

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

    /// <summary>True when the Tools tab is selected.</summary>
    public bool IsToolsTabSelected => string.Equals(SelectedTab, "Tools", StringComparison.Ordinal);

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

        // Guard: prompt to save unsaved settings when leaving Settings tab.
        // Revert immediately and handle the dialog asynchronously to avoid
        // blocking the WPF dispatcher (the previous .GetAwaiter().GetResult()
        // pattern caused deadlocks when the dialog posted back to the UI thread).
        if (!_suppressTabChangeGuard
            && string.Equals(_previousTab, "Settings", StringComparison.Ordinal)
            && !string.Equals(value, "Settings", StringComparison.Ordinal)
            && Settings.IsDirty)
        {
            _suppressTabChangeGuard = true;
            SelectedTab = "Settings";
            _suppressTabChangeGuard = false;
            _ = SafeFireAndForgetAsync(HandleUnsavedSettingsGuardAsync(value));
            return;
        }

        ApplyTabChange(value);
    }

    /// <summary>
    /// Shows the save/discard/cancel dialog asynchronously when leaving the
    /// Settings tab with unsaved changes, then navigates to the target tab
    /// if the user chose Save or Discard.
    /// </summary>
    private async Task HandleUnsavedSettingsGuardAsync(string targetTab)
    {
        var title = _localizer["SettingsUnsavedWarningTitle"];
        var message = _localizer["SettingsUnsavedWarning"];
        var result = await _dialogService.ShowSaveDiscardCancelAsync(title, message);

        if (result is null)
        {
            // Cancel: stay on Settings (already reverted)
            return;
        }

        if (result == true)
        {
            // Save
            Settings.SaveCommand.Execute(null);
        }
        else
        {
            // Discard
            await Settings.DiscardChangesAsync();
        }

        // Navigate to the originally intended tab
        _suppressTabChangeGuard = true;
        SelectedTab = targetTab;
        _suppressTabChangeGuard = false;
    }

    /// <summary>
    /// Applies the post-navigation side effects for a tab change:
    /// updates <c>_previousTab</c>, raises property-changed notifications,
    /// and refreshes tab-specific data.
    /// </summary>
    private void ApplyTabChange(string value)
    {
        _previousTab = value;

        OnPropertyChanged(nameof(IsServersTabSelected));
        OnPropertyChanged(nameof(IsTunnelsTabSelected));
        OnPropertyChanged(nameof(IsScheduledTabSelected));
        OnPropertyChanged(nameof(IsToolsTabSelected));
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

    /// <summary>Centralized tool registry shared with MainWindow for menus.</summary>
    public ToolRegistry ToolRegistry { get; }

    /// <summary>Recently used tool IDs (most recent first, max 5).</summary>
    private readonly List<string> _recentToolIds = new();
    private const int MaxRecentTools = 5;

    /// <summary>Returns the current list of recently used tool IDs.</summary>
    internal IReadOnlyList<string> RecentToolIds => _recentToolIds;

    /// <summary>Returns the favorite tool IDs from persisted settings.</summary>
    internal List<string> FavoriteToolIds => _currentSettings?.FavoriteToolIds ?? [];

    /// <summary>Toggles a tool's favorite status and persists the change.</summary>
    internal async Task ToggleFavoriteToolAsync(string toolId)
    {
        if (_currentSettings is null) return;
        var id = toolId.ToUpperInvariant();
        if (_currentSettings.FavoriteToolIds.Contains(id))
            _currentSettings.FavoriteToolIds.Remove(id);
        else
            _currentSettings.FavoriteToolIds.Add(id);
        await _configManager.MergeSettingAsync(s => s.FavoriteToolIds = _currentSettings.FavoriteToolIds);
    }

    public MainViewModel(
        ConfigManager configManager,
        LocalizationManager localizer,
        ConnectionStateMachine connectionSm,
        ApplicationStatusMachine appStatus,
        TunnelManager tunnelManager,
        HostKeyStore hostKeyStore,
        IDialogService dialogService,
        EmbeddedSessionManager embeddedSessionManager,
        ThemeService themeService,
        ToolRegistry toolRegistry,
        SplitService splitService,
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
        _themeService = themeService;
        ToolRegistry = toolRegistry;
        Split = splitService;
        ServerList = serverList;
        Connection = connection;
        Settings = settings;

        _taskScheduler = new TaskSchedulerService
        {
            TasksProvider = () => ScheduledTasks.ToList(),
            TaskDueCallback = OnScheduledTaskDueAsync,
            PersistCallback = SaveScheduledTasksAsync
        };

        // Wire SplitService callbacks for access to session tab state
        Split.ActiveSessionsProvider = () => Connection.ActiveSessions;
        Split.ActiveSessionProvider = () => Connection.ActiveSession;
        Split.SetActiveSession = s => Connection.ActiveSession = s;
        Split.SetHasActiveSessions = v => Connection.HasActiveSessions = v;
        Split.SetStatusText = s => StatusText = s;
        ServerList.ConnectionService.SetStatusText = s => StatusText = s;

        _appStatus.StatusChanged += OnApplicationStatusChanged;
        _tunnelManager.TunnelOpened += OnTunnelOpened;
        _tunnelManager.TunnelClosed += OnTunnelClosed;

        // Keep _currentSettings in sync when settings are saved elsewhere
        _configManager.SettingsChanged += OnSettingsChanged;

        // Reload server list after a config import
        _onConfigurationChanged = async () =>
            await ReloadConfigurationAsync(await _configManager.LoadSettingsAsync());
        Settings.ConfigurationChanged += _onConfigurationChanged;

        // Wire broadcast relay so terminal views can fan out input
        _embeddedSessionManager.BroadcastCallback = BroadcastToAllTerminals;
        _embeddedSessionManager.IsBroadcastActive = () => IsBroadcastMode;

        // Wire SSH reconnect: close the old session tab and re-connect from scratch
        _embeddedSessionManager.ReconnectRequestedCallback = OnReconnectRequested;

        // Wire cross-tool navigation so tools can open other tool tabs
        _embeddedSessionManager.OpenToolCallback = (toolId, title, ctx) =>
            OpenToolTabAsync(toolId, title, ctx);

        // Instant theme preview when the user changes the Settings combo.
        // Actual persistence triggers a second ApplyTheme via ConfigManager.SettingsChanged,
        // which is a no-op because ThemeService is idempotent.
        Settings.ThemeChanged += OnSettingsThemePreview;

        // Track the theme revision counter so MultiBinding triggers fire on swap.
        // Initial sync covers the startup apply that happened before this subscription.
        _themeService.ThemeChanged += OnThemeServiceThemeChanged;
        ThemeRevision = _themeService.ThemeRevision;

        // Wire server list session events to the connection tab manager
        ServerList.SessionReady += OnSessionReady;
        _onToolSessionRequested = (toolId, title, ctx) =>
        {
            TrackRecentTool(toolId.ToUpperInvariant());
            _ = SafeFireAndForgetAsync(OpenToolTabAsync(toolId, title, ctx));
        };
        ServerList.ToolSessionRequested += _onToolSessionRequested;
        _onStatusMessageRequested = message => StatusText = message;
        ServerList.StatusMessageRequested += _onStatusMessageRequested;

        StatusText = _localizer["StatusReady"];
    }

    /// <summary>
    /// Loads settings, server inventory, and initializes the UI state.
    /// Called after the window is shown.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        using var _ = _appStatus.BeginOperation("Loading");

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

            // Restore previous workspace if session persistence is enabled
            if (settings.EnableSessionPersistence)
            {
                var workspace = await WorkspaceService.LoadAsync().ConfigureAwait(false);
                if (workspace?.Sessions.Count > 0)
                {
                    StatusText = _localizer["WorkspaceRestoring"];
                    int restored = 0;
                    foreach (var session in workspace.Sessions)
                    {
                        var server = ServerList.Servers.FirstOrDefault(
                            s => string.Equals(s.Id, session.ServerId,
                                 StringComparison.OrdinalIgnoreCase));
                        if (server is null) continue;
                        try
                        {
                            ServerList.ConnectCommand.Execute(server);
                            restored++;
                        }
                        catch (Exception ex)
                        {
                            Core.Logging.FileLogger.Error(
                                $"Workspace restore failed for {session.ServerName}: {ex.Message}");
                        }
                    }
                    Core.Logging.FileLogger.Info(
                        _localizer.Format("LogWorkspaceRestored", restored));
                }
            }

            // OperationScope.Dispose() handles the Ready transition
            StatusText = _localizer["StatusReady"];
            WindowTitle = _localizer.Format("WindowTitle", ServerCount);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Collects the currently open session tabs for workspace persistence.
    /// </summary>
    public IEnumerable<WorkspaceSessionDto> GetOpenSessions()
    {
        return Connection.ActiveSessions
            .Where(t => !string.IsNullOrWhiteSpace(t.OriginalServerId))
            .Select(t => new WorkspaceSessionDto
            {
                ServerId = t.OriginalServerId,
                ServerName = t.Title ?? t.OriginalServerId,
                Protocol = t.ConnectionType ?? ""
            });
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
            _ = SafeFireAndForgetAsync(AutoOpenSftpAsync(tab, originalServerId, Split.GetSessionToken(tab)));
        }
    }

    /// <summary>
    /// Closes the disconnected session tab and starts a fresh connection to
    /// the same server, reusing the standard connection flow.
    /// </summary>
    private void OnReconnectRequested(SessionTabViewModel tab, string serverId, string connectionType)
    {
        _ = SafeFireAndForgetAsync(OnReconnectRequestedAsync(tab, serverId, connectionType));
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
    private async Task AutoOpenSftpAsync(SessionTabViewModel tab, string serverId, CancellationToken ct = default)
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
                .ConnectSftpAsync(server, _currentSettings!, ct)
                .ConfigureAwait(false);

            if (!sftpResult.Success || sftpResult.Session is null)
            {
                Core.Logging.FileLogger.Warn(
                    $"SFTP auto-open failed for {serverId}: {sftpResult.ErrorMessage}");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusText = _localizer.Format("StatusSftpAutoOpenFailed", sftpResult.ErrorMessage ?? ""));
                return;
            }

            // Create the SFTP host control on the UI thread and wrap root in a split container
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var sftpPane = new Core.Models.SessionPaneModel
                {
                    ServerId = serverId,
                    OriginalServerId = tab.OriginalServerId,
                    ConnectionType = "SFTP",
                    Title = tab.Title,
                    Status = "Connected"
                };
                sftpPane.HostControl = _embeddedSessionManager.CreateHostControl(
                    tab, tab.Title, "SFTP", sftpResult.Session, _currentSettings);

                var currentRoot = tab.RootContent;
                tab.RootContent = new Core.Models.SplitContainerModel
                {
                    First = currentRoot,
                    Second = sftpPane,
                    Orientation = Core.Models.SplitOrientation.Vertical,
                    SplitRatio = 0.5
                };
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
    /// Forwards the instant-preview theme change from the Settings combo to
    /// the centralized <see cref="ThemeService"/>. The service performs the
    /// actual dictionary swap, DWM title bar update, and event broadcast.
    /// </summary>
    private void OnSettingsThemePreview(string themeName)
    {
        _themeService.ApplyTheme(themeName);
    }

    /// <summary>
    /// Mirrors <see cref="ThemeService.ThemeRevision"/> into <see cref="ThemeRevision"/>
    /// so XAML <c>MultiBinding</c>s re-run their brush-resolving converters after a swap.
    /// </summary>
    private void OnThemeServiceThemeChanged(string themeName)
    {
        ThemeRevision = _themeService.ThemeRevision;
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
        var vm = new ScheduledTaskDialogViewModel
        {
            DialogTitle = _localizer["ScheduledTaskDialogTitleAdd"]
        };

        await PopulateScheduledTaskServersAsync(vm);

        var result = await _dialogService.ShowScheduledTaskDialogAsync(vm);
        if (result is null)
        {
            return;
        }

        TaskSchedulerService.ComputeNextRun(result.Task, DateTime.Now);
        ScheduledTasks.Add(result.Task);
        OnPropertyChanged(nameof(HasNoScheduledTasks));
        await SaveScheduledTasksAsync();
        StatusText = _localizer.Format("StatusScheduledTaskAdded", result.Task.ServerName);
    }

    [RelayCommand]
    private async Task EditScheduledTaskAsync(CancellationToken cancellationToken)
    {
        if (SelectedScheduledTask is null)
        {
            return;
        }

        var vm = ScheduledTaskDialogViewModel.FromDto(SelectedScheduledTask);
        vm.DialogTitle = _localizer["ScheduledTaskDialogTitleEdit"];
        await PopulateScheduledTaskServersAsync(vm);
        vm.SelectServerById(SelectedScheduledTask.ServerId);

        var result = await _dialogService.ShowScheduledTaskDialogAsync(vm);
        if (result is null)
        {
            return;
        }

        // Replace the existing task in the collection
        var index = ScheduledTasks.IndexOf(SelectedScheduledTask);
        if (index >= 0)
        {
            TaskSchedulerService.ComputeNextRun(result.Task, DateTime.Now);
            ScheduledTasks[index] = result.Task;
            SelectedScheduledTask = result.Task;
        }

        await SaveScheduledTasksAsync();
        StatusText = _localizer.Format("StatusScheduledTaskUpdated", result.Task.ServerName);
    }

    /// <summary>
    /// Populates the server options list in the scheduled task dialog ViewModel
    /// from the current server inventory.
    /// </summary>
    private async Task PopulateScheduledTaskServersAsync(ScheduledTaskDialogViewModel vm)
    {
        var servers = await _configManager.LoadServersAsync();
        var options = servers
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new Dialogs.ServerOption(
                s.Id,
                s.DisplayName,
                $"{s.DisplayName} ({s.ConnectionType})",
                s.ConnectionType ?? "SSH"))
            .ToList();

        vm.AvailableServers = new System.Collections.ObjectModel.ObservableCollection<Dialogs.ServerOption>(options);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _appStatus.StatusChanged -= OnApplicationStatusChanged;
        _tunnelManager.TunnelOpened -= OnTunnelOpened;
        _tunnelManager.TunnelClosed -= OnTunnelClosed;
        _configManager.SettingsChanged -= OnSettingsChanged;
        Settings.ConfigurationChanged -= _onConfigurationChanged;
        Settings.ThemeChanged -= OnSettingsThemePreview;
        _themeService.ThemeChanged -= OnThemeServiceThemeChanged;
        ServerList.SessionReady -= OnSessionReady;
        ServerList.ToolSessionRequested -= _onToolSessionRequested;
        ServerList.StatusMessageRequested -= _onStatusMessageRequested;
    }

    private static async Task SafeFireAndForgetAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Fire-and-forget task failed: {ex.Message}", ex);
        }
    }

    // ── Split operations (delegated to SplitService) ─────────────────

    public async Task SplitSessionWithServerAsync(
        SessionTabViewModel session,
        string serverId,
        Core.Models.SplitOrientation orientation,
        string? paneId = null)
    {
        await Split.SplitSessionWithServerAsync(session, serverId, orientation, paneId);
    }

    private void SplitSessionWithTool(
        SessionTabViewModel session,
        string paletteToolPayload,
        Core.Models.SplitOrientation orientation,
        string? paneId = null)
    {
        Split.SplitSessionWithTool(session, paletteToolPayload, orientation, paneId);
        // Parse tool ID for recent tracking
        var pipeIndex = paletteToolPayload.IndexOf('|');
        var toolId = pipeIndex >= 0 ? paletteToolPayload[..pipeIndex] : paletteToolPayload;
        TrackRecentTool(toolId.ToUpperInvariant());
    }

    // --- Command Palette logic ---

    partial void OnPaletteSearchTextChanged(string value)
    {
        var query = value?.Trim() ?? "";
        if (string.IsNullOrEmpty(query))
        {
            var initialResults = new List<ServerItemViewModel>();

            // In split mode, show active sessions first (merge candidates)
            if (_splitPaletteSession is not null)
            {
                foreach (var s in Connection.ActiveSessions)
                {
                    // Skip the session being split
                    if (s == _splitPaletteSession) continue;
                    if (s.HostControl is null) continue;

                    initialResults.Add(new ServerItemViewModel
                    {
                        Id = $"session-{s.ServerId}",
                        DisplayName = $"\u2194 {s.Title}",
                        RemoteServer = _localizer["SplitMergeActiveSession"],
                        ConnectionType = s.ConnectionType ?? "",
                        Group = _localizer["SplitActiveSessionsHeader"]
                    });
                }
            }

            // In split mode, boost previously paired servers to the top
            var boostedIds = new HashSet<string>(StringComparer.Ordinal);
            if (_splitPaletteSession is not null)
            {
                var sessionServerId = _splitPaletteSession.OriginalServerId;
                var partners = Split.LayoutMemory.FindAllPartners(sessionServerId);
                foreach (var entry in partners)
                {
                    var partnerId = string.Equals(entry.PrimaryServerId, sessionServerId, StringComparison.Ordinal)
                        ? entry.SecondaryServerId
                        : entry.PrimaryServerId;

                    var serverVm = ServerList.Servers.FirstOrDefault(
                        s => string.Equals(s.Id, partnerId, StringComparison.Ordinal));
                    if (serverVm is not null && boostedIds.Add(partnerId))
                    {
                        initialResults.Add(serverVm);
                    }
                }
            }

            // Show servers (skip already-boosted ones).
            // In split mode, show ALL servers so every treeview entry is reachable.
            // In normal palette mode, limit to 10 recent servers.
            var availableServers = ServerList.Servers
                .Where(s => !boostedIds.Contains(s.Id));
            if (_splitPaletteSession is null)
                availableServers = availableServers.Take(10);
            initialResults.AddRange(availableServers);

            // Then recent tools at the bottom (if any)
            foreach (var toolId in _recentToolIds)
            {
                var desc = ToolRegistry.GetById(toolId);
                if (desc is not null)
                {
                    initialResults.Add(new ServerItemViewModel
                    {
                        Id = $"tool-{desc.Id.ToLowerInvariant()}",
                        DisplayName = _localizer[desc.LabelKey],
                        ConnectionType = desc.ToolType,
                        Group = _localizer["PaletteRecentToolsHeader"]
                    });
                }
            }

            PaletteResults = new ObservableCollection<ServerItemViewModel>(initialResults);
            SelectedPaletteItem = PaletteResults.FirstOrDefault();
            return;
        }

        // Check for tool commands first (e.g., "subnet 192.168.1.0/24", "hash", "tools")
        var toolItems = TryParseToolCommand(query);
        if (toolItems.Count > 0)
        {
            PaletteResults = new ObservableCollection<ServerItemViewModel>(toolItems);
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
                Endpoint = query,
                ConnectionType = "SSH",
                Group = ""
            });
            matches.Add(new ServerItemViewModel
            {
                Id = $"adhoc-rdp-{query}",
                DisplayName = _localizer.Format("QuickConnectRdpTo", query),
                RemoteServer = query,
                Endpoint = query,
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
        PalettePlaceholder = _localizer["PaletteSearchPlaceholder"];
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
    public void OpenSplitPalette(SessionTabViewModel session, Core.Models.SplitOrientation orientation, string? paneId = null)
    {
        _splitPaletteSession = session;
        _splitPaletteOrientation = orientation;
        _splitPalettePaneId = paneId;
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

    /// <summary>
    /// Synchronous entry point for palette item selection (used by mouse click handler).
    /// Captures split state immediately to avoid race conditions with Popup deactivation.
    /// </summary>
    public void ExecutePaletteSelection(ServerItemViewModel item)
    {
        var splitSession = _splitPaletteSession;
        var splitOrientation = _splitPaletteOrientation;
        var splitPaneId = _splitPalettePaneId;
        _splitPaletteSession = null;
        _splitPalettePaneId = null;
        IsCommandPaletteOpen = false;

        if (splitSession is not null)
        {
            // Check if this is an active session merge (prefix "session-")
            if (item.Id.StartsWith("session-", StringComparison.Ordinal))
            {
                var sourceSessionId = item.Id["session-".Length..];
                MergeExistingSession(splitSession, sourceSessionId, splitOrientation, splitPaneId);
                return;
            }

            // Built-in tool selected in split mode — dock tool in split pane
            if (item.Id.StartsWith("tool-", StringComparison.Ordinal))
            {
                SplitSessionWithTool(splitSession, item.Id["tool-".Length..], splitOrientation, splitPaneId);
                return;
            }

            if (!item.Id.StartsWith("adhoc-", StringComparison.Ordinal))
            {
                _ = SafeFireAndForgetAsync(SplitSessionWithServerAsync(splitSession, item.Id, splitOrientation, splitPaneId));
                return;
            }
        }

        // Fall through to normal palette behavior
        _ = SafeFireAndForgetAsync(ConnectFromPaletteInternalAsync(item));
    }

    [RelayCommand]
    private async Task ConnectFromPaletteAsync(ServerItemViewModel? server)
    {
        if (server is null) return;

        var splitSession = _splitPaletteSession;
        var splitOrientation = _splitPaletteOrientation;
        var splitPaneId = _splitPalettePaneId;
        _splitPaletteSession = null;
        _splitPalettePaneId = null;
        IsCommandPaletteOpen = false;

        // If the palette was opened in split mode, route to split logic
        if (splitSession is not null && !server.Id.StartsWith("adhoc-", StringComparison.Ordinal))
        {
            // Check if this is an active session merge
            if (server.Id.StartsWith("session-", StringComparison.Ordinal))
            {
                var sourceSessionId = server.Id["session-".Length..];
                MergeExistingSession(splitSession, sourceSessionId, splitOrientation, splitPaneId);
                return;
            }

            // Built-in tool selected in split mode
            if (server.Id.StartsWith("tool-", StringComparison.Ordinal))
            {
                SplitSessionWithTool(splitSession, server.Id["tool-".Length..], splitOrientation, splitPaneId);
                return;
            }

            await SplitSessionWithServerAsync(splitSession, server.Id, splitOrientation, splitPaneId);
            return;
        }

        if (server.Id.StartsWith("tool-", StringComparison.Ordinal))
        {
            await OpenToolFromPaletteAsync(server);
        }
        else if (server.Id.StartsWith("ext-tool-", StringComparison.Ordinal))
        {
            LaunchExternalToolFromPalette(server);
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
    /// Normal (non-split) palette action — extracted so <see cref="ExecutePaletteSelection"/>
    /// can call it after the split check without duplicating the routing logic.
    /// </summary>
    private async Task ConnectFromPaletteInternalAsync(ServerItemViewModel server)
    {
        if (server.Id.StartsWith("tool-", StringComparison.Ordinal))
        {
            await OpenToolFromPaletteAsync(server);
        }
        else if (server.Id.StartsWith("ext-tool-", StringComparison.Ordinal))
        {
            LaunchExternalToolFromPalette(server);
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

    public void MergeExistingSession(
        SessionTabViewModel target,
        string sourceSessionId,
        Core.Models.SplitOrientation orientation,
        string? targetPaneId = null)
    {
        Split.MergeExistingSession(target, sourceSessionId, orientation, targetPaneId);
    }

    public Task SwapSplitPanesAsync(SessionTabViewModel session, string? paneId = null)
    {
        return Split.SwapSplitPanesAsync(session, paneId);
    }

    public void ToggleSplitOrientation(SessionTabViewModel session, string? paneId = null)
    {
        Split.ToggleSplitOrientation(session, paneId);
    }

    [RelayCommand]
    private void CloseSecondaryPane(SessionTabViewModel? session)
    {
        if (session is null || !session.IsSplit) return;

        var secondaryPane = session.RootContent is SplitContainerModel c
            ? Core.Models.SplitTreeHelper.FirstLeaf(c.Second)
            : null;
        if (secondaryPane is not null)
        {
            ClosePane(session, secondaryPane.PaneId);
        }
    }

    public void ClosePane(SessionTabViewModel session, string paneId)
    {
        Split.ClosePane(session, paneId);
    }

    [RelayCommand]
    private async Task ReconnectSecondaryAsync(SessionTabViewModel? session)
    {
        if (session is null || !session.IsSplit) return;

        var secondaryPane = session.RootContent is SplitContainerModel c
            ? Core.Models.SplitTreeHelper.FirstLeaf(c.Second)
            : null;
        if (secondaryPane is not null)
        {
            await ReconnectPaneAsync(session, secondaryPane.PaneId);
        }
    }

    public async Task ReconnectPaneAsync(SessionTabViewModel session, string paneId)
    {
        await Split.ReconnectPaneAsync(session, paneId);
    }

    public void CleanupOrphanedPane(string serverId)
    {
        Split.CleanupOrphanedPane(serverId);
    }

    /// <summary>
    /// Localized button text for the secondary pane reconnect button.
    /// </summary>
    public string ReconnectSecondaryButtonText => _localizer["SplitReconnectSecondary"];

    /// <summary>
    /// Localized button text for the secondary pane close button.
    /// </summary>
    public string CloseSecondaryButtonText => _localizer["SplitCloseSecondary"];

    /// <summary>
    /// Localized text for the drag-to-split drop zone.
    /// </summary>
    public string DropToMergeText => _localizer["SplitDropToMerge"];

    [RelayCommand]
    private async Task ConnectSplitFromPaletteAsync(ServerItemViewModel? server)
    {
        if (server is null) return;
        IsCommandPaletteOpen = false;

        if (server.Id.StartsWith("tool-", StringComparison.Ordinal))
        {
            var activeForTool = Connection.ActiveSession;
            if (activeForTool is not null)
            {
                SplitSessionWithTool(activeForTool, server.Id["tool-".Length..],
                    Core.Models.SplitOrientation.Vertical);
                return;
            }

            await OpenToolFromPaletteAsync(server);
            return;
        }

        if (server.Id.StartsWith("ext-tool-", StringComparison.Ordinal))
        {
            LaunchExternalToolFromPalette(server);
            return;
        }

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
            tab.Status = _localizer["StatusConnected"];
            StatusText = _localizer.Format("StatusConnected", !string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.DisplayName : dto.RemoteServer);
        }
        else if (result.Success)
        {
            // External mode: process launched, no embedded tab needed
            StatusText = _localizer.Format("StatusConnected", !string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.DisplayName : dto.RemoteServer);
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
        best = Math.Max(best, FuzzyScoreString(server.Group ?? "", query) / 2);
        best = Math.Max(best, FuzzyScoreString(server.Username ?? "", query) / 2);
        best = Math.Max(best, FuzzyScoreString(server.ConnectionType ?? "", query) / 2);
        best = Math.Max(best, FuzzyScoreString(server.Environment ?? "", query) / 2);
        best = Math.Max(best, FuzzyScoreString(server.Tags ?? "", query) / 2);
        best = Math.Max(best, FuzzyScoreString(server.ProjectName ?? "", query) / 2);
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
    /// Tracks a tool ID as recently used for the palette's "recent tools" section.
    /// </summary>
    public void TrackRecentTool(string toolId)
    {
        _recentToolIds.Remove(toolId);
        _recentToolIds.Insert(0, toolId);
        while (_recentToolIds.Count > MaxRecentTools)
            _recentToolIds.RemoveAt(_recentToolIds.Count - 1);
    }

    /// <summary>
    /// Checks whether the palette query matches a tool command prefix.
    /// Returns matching tool palette items, or all tools when the query is "tool" / "tools".
    /// Uses the centralized <see cref="ToolRegistry"/> instead of a static array.
    /// </summary>
    private List<ServerItemViewModel> TryParseToolCommand(string query)
    {
        var results = new List<ServerItemViewModel>();
        var lower = query.ToLowerInvariant();

        // Show all tools when user types "tool" or "tools", grouped by category
        if (lower is "tool" or "tools")
        {
            foreach (var descriptor in ToolRegistry.All)
            {
                results.Add(new ServerItemViewModel
                {
                    Id = $"tool-{descriptor.Id.ToLowerInvariant()}",
                    DisplayName = _localizer[descriptor.LabelKey],
                    ConnectionType = descriptor.ToolType,
                    Group = _localizer[descriptor.CategoryLabelKey]
                });
            }
            return results;
        }

        // Check if query starts with a known tool prefix
        foreach (var descriptor in ToolRegistry.All)
        {
            foreach (var prefix in descriptor.CommandPrefixes)
            {
                if (!lower.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var rest = query[prefix.Length..].Trim();
                string displayName;

                if (!string.IsNullOrEmpty(rest) && descriptor.LabelWithArgKey is not null)
                {
                    displayName = _localizer.Format(descriptor.LabelWithArgKey, rest);
                }
                else
                {
                    displayName = _localizer[descriptor.LabelKey];
                }

                results.Add(new ServerItemViewModel
                {
                    Id = $"tool-{descriptor.Id.ToLowerInvariant()}|{rest}",
                    DisplayName = displayName,
                    ConnectionType = descriptor.ToolType,
                    Group = _localizer["PaletteToolsSectionHeader"]
                });
                break;
            }

            if (results.Count > 0) break;
        }

        // Always search external tools (even when a built-in tool matched,
        // so user-defined tools with overlapping names remain discoverable).
        var extTools = _currentSettings?.ExternalTools ?? [];
        foreach (var ext in extTools)
        {
            if (string.IsNullOrWhiteSpace(ext.Name)) continue;

            if (FuzzyScoreString(ext.Name, query) > 0
                || ext.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new ServerItemViewModel
                {
                    Id = $"ext-tool-{ext.Name}",
                    DisplayName = ext.Name,
                    ConnectionType = "EXTERNAL",
                    Group = _localizer["PaletteExternalToolsHeader"]
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Opens a tool tab from a palette item whose Id starts with "tool-".
    /// Extracts the tool ID and optional argument from the encoded Id.
    /// </summary>
    private async Task OpenToolFromPaletteAsync(ServerItemViewModel item)
    {
        // Id format: "tool-<toolid>|<argument>"
        var payload = item.Id["tool-".Length..];
        var pipeIndex = payload.IndexOf('|');
        var toolId = pipeIndex >= 0 ? payload[..pipeIndex] : payload;
        var argument = pipeIndex >= 0 ? payload[(pipeIndex + 1)..] : null;

        var context = !string.IsNullOrEmpty(argument) ? new ToolContext(Argument: argument) : null;
        TrackRecentTool(toolId.ToUpperInvariant());
        await OpenToolTabAsync(toolId, item.DisplayName, context);
    }

    /// <summary>
    /// Launches an external tool from a palette item whose Id starts with "ext-tool-".
    /// Matches the tool name back to the configured ExternalToolDefinition and starts
    /// the process. When a server is selected, placeholders are resolved against it.
    /// </summary>
    private void LaunchExternalToolFromPalette(ServerItemViewModel item)
    {
        var toolName = item.Id["ext-tool-".Length..];
        var extTool = _currentSettings?.ExternalTools
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));

        if (extTool is null) return;

        try
        {
            // Resolve placeholders against the currently selected server when available.
            var selectedServer = ServerList.SelectedServer;
            var arguments = selectedServer is not null
                ? extTool.ResolveArguments(
                    selectedServer.RemoteServer,
                    selectedServer.EffectivePort,
                    selectedServer.Username,
                    serverName: selectedServer.DisplayName,
                    protocol: selectedServer.ConnectionType,
                    keyFile: selectedServer.SshKeyPath,
                    project: selectedServer.ProjectName,
                    gateway: selectedServer.GatewayName)
                : extTool.Arguments;

            if (Core.Security.InputValidator.IsShellTarget(extTool.ExecutablePath))
            {
                Core.Logging.FileLogger.Warn(
                    $"Blocked external tool '{toolName}': executable is a shell target ({extTool.ExecutablePath}).");
                return;
            }

            var needsElevation = extTool.RunAsAdministrator;
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = extTool.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = needsElevation
            };

            if (!string.IsNullOrWhiteSpace(extTool.WorkingDirectory))
            {
                psi.WorkingDirectory = extTool.WorkingDirectory;
            }

            if (needsElevation)
            {
                psi.Verb = "runas";
            }

            if (extTool.RunHidden)
            {
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
            }

            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Failed to launch external tool '{toolName}': {ex.Message}");
        }
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

    public string BroadcastToggleTooltip => IsBroadcastMode
        ? _localizer["BroadcastModeOn"]
        : _localizer["TooltipToggleBroadcast"];

    partial void OnIsBroadcastModeChanged(bool value)
    {
        UpdateBroadcastIndicators(value);
        OnPropertyChanged(nameof(BroadcastToggleTooltip));
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
            foreach (var pane in Core.Models.SplitTreeHelper.EnumerateLeaves(session.RootContent))
            {
                if (pane.HostControl is Views.EmbeddedSshView sshView)
                {
                    sshView.SetBroadcastIndicator(active);
                }
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
            foreach (var pane in Core.Models.SplitTreeHelper.EnumerateLeaves(session.RootContent))
            {
                BroadcastToHostControl(pane.HostControl, data, sender);
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

    // ── Tool tabs ────────────────────────────────────────────────

    /// <summary>
    /// Opens a non-connection tool as a session tab, bypassing
    /// ConnectionService and ConnectionStateMachine entirely.
    /// </summary>
    internal async Task OpenToolTabAsync(string toolId, string title, ToolContext? context)
    {
        await Task.CompletedTask;

        var connectionType = $"TOOL:{toolId.ToUpperInvariant()}";

        // Singleton behavior: reuse existing tab for tools that have no context
        // (e.g., Password Generator, UUID, Chmod). Network tools or tools opened
        // with a specific argument are allowed to have multiple instances.
        var isNetworkTool = ToolRegistry.IsNetworkTool(toolId);
        var hasContext = !string.IsNullOrWhiteSpace(context?.TargetHost) || !string.IsNullOrWhiteSpace(context?.Argument);

        if (!isNetworkTool && !hasContext)
        {
            var existing = Connection.ActiveSessions
                .FirstOrDefault(s => s.ConnectionType == connectionType);
            if (existing is not null)
            {
                Connection.ActiveSession = existing;
                return;
            }
        }

        var sessionId = $"tool-{toolId.ToLowerInvariant()}-{Guid.NewGuid():N}";

        var tab = Connection.AddSession(sessionId, title, connectionType);
        try
        {
            tab.HostControl = _embeddedSessionManager.CreateToolControl(
                tab, toolId, context, _currentSettings);
            tab.Status = _localizer["StatusReady"];
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Failed to create tool control: {toolId}", ex);
            Connection.ActiveSessions.Remove(tab);
            Connection.HasActiveSessions = Connection.ActiveSessions.Count > 0;
            throw;
        }
    }
}
