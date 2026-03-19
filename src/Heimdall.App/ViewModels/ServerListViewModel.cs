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
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Security;
using Heimdall.Core.StateMachine;

namespace Heimdall.App.ViewModels;

/// <summary>
/// ViewModel for the server list with filtering, sorting, and connection actions.
/// </summary>
public partial class ServerListViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;
    private readonly LocalizationManager _localizer;
    private readonly ConnectionStateMachine _connectionSm;
    private readonly ConnectionService _connectionService;

    internal ConnectionService ConnectionService => _connectionService;
    private readonly IDialogService _dialogService;

    private List<ServerItemViewModel> _allServers = [];
    private List<ProjectTarget> _projectTargets = [];
    private AppSettings? _currentSettings;
    private readonly HashSet<string> _expandedNodes = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.Timer? _expandSaveTimer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private ObservableCollection<ServerItemViewModel> _servers = [];

    [ObservableProperty]
    private ObservableCollection<FolderViewModel> _groupedServers = [];

    [ObservableProperty]
    private ObservableCollection<string> _projects = [];

    [ObservableProperty]
    private ObservableCollection<string> _groups = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedProject = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ServerItemViewModel? _selectedServer;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    /// <summary>
    /// True when the server list contains no entries, used to show the empty state overlay.
    /// </summary>
    public bool IsEmpty => Servers.Count == 0;

    /// <summary>
    /// True when a server is selected in the TreeView, used to toggle the detail panel.
    /// </summary>
    public bool HasSelection => SelectedServer is not null;

    /// <summary>
    /// Raised when a connection result is ready and a session tab should be created.
    /// Parameters: sessionId, originalServerId, displayName, connectionType, session result.
    /// </summary>
    public event Action<string, string, string, string, Core.Models.ISessionResult?>? SessionReady;

    /// <summary>
    /// Raised when a non-modal status message should be surfaced in the shell.
    /// </summary>
    public event Action<string>? StatusMessageRequested;

    public ServerListViewModel(
        ConfigManager configManager,
        LocalizationManager localizer,
        ConnectionStateMachine connectionSm,
        ConnectionService connectionService,
        IDialogService dialogService)
    {
        _configManager = configManager;
        _localizer = localizer;
        _connectionSm = connectionSm;
        _connectionService = connectionService;
        _dialogService = dialogService;

        _connectionSm.StateChanged += OnConnectionStateChanged;
    }

    /// <summary>
    /// Populates the server list from loaded DTOs and settings.
    /// </summary>
    public void LoadServers(List<ServerProfileDto> serverDtos, AppSettings settings)
    {
        _currentSettings = settings;
        var selectedServerId = SelectedServer?.Id;
        var projectMap = BuildProjectMap(settings);

        // Restore expand state from settings
        _expandedNodes.Clear();
        if (settings.TreeExpandedNodes.Count > 0)
        {
            foreach (var node in settings.TreeExpandedNodes)
            {
                _expandedNodes.Add(node);
            }
        }

        _allServers = serverDtos
            .Select(dto => ServerItemViewModel.FromDto(
                dto,
                ResolveProject(projectMap, dto.ProjectId),
                _connectionSm.GetState(dto.Id).ToString()))
            .ToList();

        RefreshLookupCollections(settings);
        IsSidebarVisible = !settings.SidebarCollapsed;
        ApplyFilter(selectedServerId);
    }

    public IReadOnlyList<ProjectTarget> GetProjectTargets(bool includeNoProject)
    {
        var targets = new List<ProjectTarget>();

        if (includeNoProject)
        {
            targets.Add(new ProjectTarget(
                string.Empty,
                _localizer["TreeNodeNoProject"],
                string.Empty,
                IsVirtualProject: true));
        }

        targets.AddRange(_projectTargets);
        return targets;
    }

    public IReadOnlyList<GroupTarget> GetGroupTargets(string? projectId, bool includeNoGroup)
    {
        var normalizedProjectId = projectId ?? string.Empty;
        var targets = new List<GroupTarget>();

        if (includeNoGroup)
        {
            targets.Add(new GroupTarget(
                string.Empty,
                _localizer["TreeNodeNoGroup"],
                IsVirtualGroup: true));
        }

        var groupTargets = _allServers
            .Where(s => string.Equals(s.ProjectId, normalizedProjectId, StringComparison.Ordinal))
            .Where(s => !string.IsNullOrWhiteSpace(s.Group))
            .Select(s => s.Group)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new GroupTarget(name, name))
            .ToList();

        targets.AddRange(groupTargets);

        // Also include empty folders from settings
        if (_currentSettings?.EmptyGroups is not null)
        {
            foreach (var path in _currentSettings.EmptyGroups)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    targets.Add(new GroupTarget(path, path));
                }
            }
        }

        return targets;
    }

    [RelayCommand]
    private async Task ConnectAsync(ServerItemViewModel? server, CancellationToken cancellationToken)
    {
        if (server is null)
        {
            return;
        }

        var servers = await _configManager.LoadServersAsync();
        var serverDto = servers.FirstOrDefault(
            s => string.Equals(s.Id, server.Id, StringComparison.Ordinal));

        if (serverDto is null)
        {
            return;
        }

        var settings = await _configManager.LoadSettingsAsync();

        // Apply group-level inherited defaults (gateway, SSH username, key path)
        // before preflight and connection. Server's own values take priority.
        if (settings.GroupDefaults.Count > 0 && !string.IsNullOrEmpty(serverDto.Group))
        {
            var groupDefaults = Core.Configuration.GroupDefaultsDto.Resolve(
                serverDto.Group, settings.GroupDefaults);
            groupDefaults.ApplyTo(serverDto);
        }

        // Resolve credentials from external provider if configured and server
        // has no stored password. The retrieved password is DPAPI-encrypted into
        // the DTO so all downstream code (ConnectionService, EmbeddedRdpView) works
        // without modification.
        await TryResolveExternalCredentialsAsync(serverDto, settings, cancellationToken);

        var preflight = _connectionService.RunPreflight(serverDto, settings);
        if (!preflight.Success)
        {
            server.ConnectionState = "Error";
            _dialogService.ShowError(
                _localizer["ErrorPreflightTitle"],
                preflight.Message ?? _localizer["ErrorPreflightFailed"]);
            return;
        }

        // Generate a unique session ID so duplicate connections to the same server
        // get independent state tracking (tunnel lifecycle, error recovery)
        var sessionId = $"{server.Id}_{Guid.NewGuid().ToString("N")[..8]}";

        Core.Logging.FileLogger.Info($"ConnectAsync: {server.DisplayName} type={serverDto.ConnectionType} gateway={serverDto.SshGatewayId} sessionId={sessionId}");

        // Use sessionId for state machine keying — allows duplicate connections
        // to the same server without sharing state or tunnels
        var originalId = serverDto.Id;
        serverDto.Id = sessionId;
        _connectionSm.TryTransition(sessionId, Core.Models.ConnectionState.Initializing);

        try
        {
            ConnectionResult result;

            switch (serverDto.ConnectionType?.ToUpperInvariant())
            {
                case "RDP":
                    result = await _connectionService.ConnectRdpAsync(
                        serverDto, settings, cancellationToken);
                    break;

                case "SSH":
                    result = await _connectionService.ConnectSshAsync(
                        serverDto, settings, cancellationToken);
                    break;

                case "SFTP":
                    result = await _connectionService.ConnectSftpAsync(
                        serverDto, settings, cancellationToken);
                    break;

                case "FTP":
                    result = await _connectionService.ConnectFtpAsync(
                        serverDto, settings, cancellationToken);
                    break;

                case "LOCAL":
                    result = await _connectionService.ConnectLocalShellAsync(
                        serverDto, settings, cancellationToken);
                    break;

                case "CITRIX":
                    result = await _connectionService.ConnectCitrixAsync(
                        serverDto, settings, cancellationToken);
                    break;

                case "VNC":
                    result = await _connectionService.ConnectVncAsync(
                        serverDto, settings, cancellationToken);
                    break;

                case "TELNET":
                    result = await _connectionService.ConnectTelnetAsync(
                        serverDto, settings, cancellationToken);
                    break;

                default:
                    _connectionSm.SetError(sessionId,
                        _localizer.Format("ErrorUnsupportedConnectionType", serverDto.ConnectionType ?? ""));
                    server.ConnectionState = "Error";
                    serverDto.Id = originalId;
                    return;
            }

            if (result.Success)
            {
                SessionReady?.Invoke(
                    sessionId, originalId, server.DisplayName,
                    serverDto.ConnectionType, result.Session);
            }
            else
            {
                server.ConnectionState = "Error";
                _dialogService.ShowError(
                    _localizer["ErrorConnectionTitle"],
                    result.ErrorMessage ?? _localizer["ErrorConnectionFailed"]);
            }
        }
        catch (OperationCanceledException)
        {
            _connectionSm.Reset(sessionId);
        }
        catch (Exception ex)
        {
            var failure = Ssh.FailureClassifier.Classify(ex);
            _connectionSm.SetError(sessionId, failure.Message);
            server.ConnectionState = "Error";
            _dialogService.ShowError(
                _localizer["ErrorConnectionTitle"], failure.Message);
        }
        finally
        {
            serverDto.Id = originalId;
        }
    }

    /// <summary>
    /// If the external credential provider is enabled and the server has no stored
    /// password for its connection type, executes the configured command to retrieve
    /// the password and injects it (DPAPI-encrypted) into the DTO. This allows all
    /// downstream code to work unchanged.
    /// </summary>
    private async Task TryResolveExternalCredentialsAsync(
        ServerProfileDto serverDto, AppSettings settings, CancellationToken ct)
    {
        if (!settings.UseExternalCredentialProvider)
        {
            return;
        }

        var provider = new Core.Security.CommandCredentialProvider(
            settings.CredentialProviderCommand, settings.CredentialProviderDatabase);

        if (!provider.IsAvailable)
        {
            return;
        }

        bool needsSshPassword = string.IsNullOrEmpty(serverDto.SshPasswordEncrypted)
            && serverDto.ConnectionType?.ToUpperInvariant() is "SSH" or "SFTP";

        bool needsRdpPassword = string.IsNullOrEmpty(serverDto.RdpPasswordEncrypted)
            && serverDto.ConnectionType?.ToUpperInvariant() is "RDP" or "CITRIX";

        bool needsFtpPassword = string.IsNullOrEmpty(serverDto.FtpPasswordEncrypted)
            && serverDto.ConnectionType?.ToUpperInvariant() is "FTP";

        if (!needsSshPassword && !needsRdpPassword && !needsFtpPassword)
        {
            return;
        }

        try
        {
            var host = serverDto.RemoteServer;
            var port = needsRdpPassword ? serverDto.RemotePort
                : needsFtpPassword ? serverDto.FtpPort
                : serverDto.SshPort;
            var username = needsRdpPassword ? serverDto.RdpUsername
                : needsFtpPassword ? serverDto.FtpUsername
                : serverDto.SshUsername;

            var credential = await provider.GetCredentialAsync(
                host, port, username, serverDto.DisplayName, ct);

            if (credential is null)
            {
                return;
            }

            // Inject the retrieved password as a DPAPI-encrypted value so all
            // downstream DecryptPassword/CredentialProtector.Unprotect calls work.
            var encrypted = CredentialProtector.Protect(credential.Password);

            if (needsSshPassword)
            {
                serverDto.SshPasswordEncrypted = encrypted;
            }

            if (needsRdpPassword)
            {
                serverDto.RdpPasswordEncrypted = encrypted;
            }

            if (needsFtpPassword)
            {
                serverDto.FtpPasswordEncrypted = encrypted;
            }

            // If the provider returned a username and the server has none, inject it
            if (!string.IsNullOrEmpty(credential.Username))
            {
                if (needsSshPassword && string.IsNullOrEmpty(serverDto.SshUsername))
                {
                    serverDto.SshUsername = credential.Username;
                }

                if (needsRdpPassword && string.IsNullOrEmpty(serverDto.RdpUsername))
                {
                    serverDto.RdpUsername = credential.Username;
                }

                if (needsFtpPassword && string.IsNullOrEmpty(serverDto.FtpUsername))
                {
                    serverDto.FtpUsername = credential.Username;
                }
            }

            Core.Logging.FileLogger.Info(
                $"External credential provider resolved password for {serverDto.DisplayName}");
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation
            throw;
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"External credential provider failed for {serverDto.DisplayName}: {ex.Message}");
            _dialogService.ShowError(
                _localizer["ErrorConnectionTitle"],
                _localizer.Format("ErrorCredentialProviderFailed", ex.Message));
        }
    }

    [RelayCommand]
    private async Task AddServerAsync(ServerDialogSeed? seed, CancellationToken cancellationToken)
    {
        var dialogVm = new ServerDialogViewModel
        {
            DialogTitle = _localizer["DialogTitleAddServer"],
            Group = seed?.GroupName ?? string.Empty,
            SelectedProjectId = seed?.ProjectId ?? string.Empty
        };

        var settings = await _configManager.LoadSettingsAsync();
        PopulateServerDialogOptions(dialogVm, settings);

        var result = await _dialogService.ShowServerDialogAsync(dialogVm);

        if (result is not { Saved: true })
        {
            return;
        }

        var servers = await _configManager.LoadServersAsync();
        result.Server.Id = Guid.NewGuid().ToString();
        servers.Add(result.Server);
        await _configManager.SaveServersAsync(servers);

        _allServers.Add(ServerItemViewModel.FromDto(
            result.Server,
            ResolveProject(BuildProjectMap(settings), result.Server.ProjectId),
            _connectionSm.GetState(result.Server.Id).ToString()));

        RefreshLookupCollections(settings);
        ApplyFilter(result.Server.Id);
        OnPropertyChanged(nameof(Servers));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task EditServerAsync(ServerItemViewModel? server, CancellationToken cancellationToken)
    {
        if (server is null)
        {
            return;
        }

        var servers = await _configManager.LoadServersAsync();
        var serverDto = servers.FirstOrDefault(
            s => string.Equals(s.Id, server.Id, StringComparison.Ordinal));

        if (serverDto is null)
        {
            return;
        }

        var dialogVm = ServerDialogViewModel.FromDto(serverDto);
        dialogVm.DialogTitle = _localizer["DialogTitleEditServer"];

        var settings = await _configManager.LoadSettingsAsync();
        PopulateServerDialogOptions(dialogVm, settings);

        var result = await _dialogService.ShowServerDialogAsync(dialogVm);

        if (result is not { Saved: true })
        {
            return;
        }

        result.Server.Id = serverDto.Id;

        var index = servers.FindIndex(
            s => string.Equals(s.Id, serverDto.Id, StringComparison.Ordinal));

        if (index < 0)
        {
            return;
        }

        servers[index] = result.Server;
        await _configManager.SaveServersAsync(servers);

        server.UpdateFromDto(
            result.Server,
            ResolveProject(BuildProjectMap(settings), result.Server.ProjectId));

        RefreshLookupCollections(settings);
        ApplyFilter(server.Id);
    }

    [RelayCommand]
    private async Task DeleteServerAsync(ServerItemViewModel? server, CancellationToken cancellationToken)
    {
        if (server is null)
        {
            return;
        }

        var confirmed = await _dialogService.ShowConfirmAsync(
            _localizer["DialogTitleDeleteServer"],
            _localizer.Format("ConfirmDeleteServer", server.DisplayName),
            "warning");

        if (!confirmed)
        {
            return;
        }

        var servers = await _configManager.LoadServersAsync();
        servers.RemoveAll(
            s => string.Equals(s.Id, server.Id, StringComparison.Ordinal));
        await _configManager.SaveServersAsync(servers);

        _allServers.RemoveAll(
            s => string.Equals(s.Id, server.Id, StringComparison.Ordinal));

        _connectionSm.Remove(server.Id);
        RefreshLookupCollections(await _configManager.LoadSettingsAsync());
        ApplyFilter();
        OnPropertyChanged(nameof(Servers));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task DuplicateServerAsync(ServerItemViewModel? server, CancellationToken cancellationToken)
    {
        if (server is null)
        {
            return;
        }

        var servers = await _configManager.LoadServersAsync();
        var sourceDto = servers.FirstOrDefault(
            s => string.Equals(s.Id, server.Id, StringComparison.Ordinal));

        if (sourceDto is null)
        {
            return;
        }

        var settings = await _configManager.LoadSettingsAsync();
        var json = System.Text.Json.JsonSerializer.Serialize(sourceDto);
        var clone = System.Text.Json.JsonSerializer.Deserialize<ServerProfileDto>(json);

        if (clone is null)
        {
            return;
        }

        clone.Id = Guid.NewGuid().ToString();
        clone.DisplayName = sourceDto.DisplayName + _localizer["ServerDuplicateSuffix"];
        clone.RdpPasswordEncrypted = null;

        servers.Add(clone);
        await _configManager.SaveServersAsync(servers);

        _allServers.Add(ServerItemViewModel.FromDto(
            clone,
            ResolveProject(BuildProjectMap(settings), clone.ProjectId),
            _connectionSm.GetState(clone.Id).ToString()));

        RefreshLookupCollections(settings);
        ApplyFilter(clone.Id);
        OnPropertyChanged(nameof(Servers));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task MoveToProjectAsync(ServerMoveToProjectRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return;
        }

        var normalizedProjectId = request.ProjectId ?? string.Empty;
        if (string.Equals(request.Server.ProjectId, normalizedProjectId, StringComparison.Ordinal))
        {
            return;
        }

        var servers = await _configManager.LoadServersAsync();
        var serverDto = servers.FirstOrDefault(
            s => string.Equals(s.Id, request.Server.Id, StringComparison.Ordinal));

        if (serverDto is null)
        {
            return;
        }

        serverDto.ProjectId = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId;
        await _configManager.SaveServersAsync(servers);

        var settings = await _configManager.LoadSettingsAsync();
        request.Server.ProjectId = normalizedProjectId;
        ApplyProjectMetadata(request.Server, ResolveProject(BuildProjectMap(settings), request.ProjectId));

        RefreshLookupCollections(settings);
        ApplyFilter(request.Server.Id);
    }

    [RelayCommand]
    private async Task MoveToGroupAsync(ServerMoveToGroupRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return;
        }

        var normalizedGroupName = request.GroupName ?? string.Empty;
        if (string.Equals(request.Server.Group, normalizedGroupName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var servers = await _configManager.LoadServersAsync();
        var serverDto = servers.FirstOrDefault(
            s => string.Equals(s.Id, request.Server.Id, StringComparison.Ordinal));

        if (serverDto is null)
        {
            return;
        }

        serverDto.Group = string.IsNullOrWhiteSpace(request.GroupName) ? null : request.GroupName;
        await _configManager.SaveServersAsync(servers);

        request.Server.Group = normalizedGroupName;
        RefreshLookupCollections(await _configManager.LoadSettingsAsync());
        ApplyFilter(request.Server.Id);
    }

    [RelayCommand]
    private void CopyHostname(ServerItemViewModel? server)
    {
        if (server is null || string.IsNullOrWhiteSpace(server.RemoteServer))
        {
            return;
        }

        Clipboard.SetText(server.RemoteServer);
        StatusMessageRequested?.Invoke(
            _localizer.Format("StatusCopiedToClipboard", server.RemoteServer));
    }

    [RelayCommand]
    private void CopyUsername(ServerItemViewModel? server)
    {
        if (server is null || string.IsNullOrWhiteSpace(server.Username))
        {
            return;
        }

        Clipboard.SetText(server.Username);
        StatusMessageRequested?.Invoke(
            _localizer.Format("StatusCopiedToClipboard", server.Username));
    }

    [RelayCommand]
    private async Task AddServerToGroupAsync(ServerGroupContext? group, CancellationToken cancellationToken)
    {
        if (group is null)
        {
            return;
        }

        await AddServerAsync(
            new ServerDialogSeed(group.ProjectId, group.IsVirtualGroup ? string.Empty : group.GroupName),
            cancellationToken);
    }


    [RelayCommand]
    private async Task RenameGroupAsync(ServerGroupContext? group, CancellationToken cancellationToken)
    {
        if (group is null || group.IsVirtualGroup)
        {
            return;
        }

        var newName = await _dialogService.ShowInputAsync(
            _localizer["RenameGroupDialogTitle"],
            _localizer["ServerFieldGroup"],
            group.GroupName);

        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        newName = newName.Trim();
        if (string.Equals(group.GroupName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var servers = await _configManager.LoadServersAsync();

        foreach (var serverDto in servers.Where(dto => MatchesGroup(dto, group.ProjectId, group.GroupName)))
        {
            serverDto.Group = newName;
        }

        await _configManager.SaveServersAsync(servers);

        foreach (var server in _allServers.Where(item => MatchesGroup(item, group.ProjectId, group.GroupName)))
        {
            server.Group = newName;
        }

        RefreshLookupCollections(await _configManager.LoadSettingsAsync());
        ApplyFilter(SelectedServer?.Id);
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(ServerGroupContext? group, CancellationToken cancellationToken)
    {
        if (group is null || group.IsVirtualGroup)
        {
            return;
        }

        var confirmed = await _dialogService.ShowConfirmAsync(
            _localizer["TreeCtxDeleteGroup"],
            _localizer.Format("TreeCtxDeleteGroupConfirm", group.GroupName),
            "warning");

        if (!confirmed)
        {
            return;
        }

        var servers = await _configManager.LoadServersAsync();

        foreach (var serverDto in servers.Where(dto => MatchesGroup(dto, group.ProjectId, group.GroupName)))
        {
            serverDto.Group = null;
        }

        await _configManager.SaveServersAsync(servers);

        foreach (var server in _allServers.Where(item => MatchesGroup(item, group.ProjectId, group.GroupName)))
        {
            server.Group = string.Empty;
        }

        RefreshLookupCollections(await _configManager.LoadSettingsAsync());
        ApplyFilter(SelectedServer?.Id);
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedProjectChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter(string? preferredSelectedServerId = null)
    {
        var filtered = _allServers.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText;
            filtered = filtered.Where(s =>
                s.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                s.RemoteServer.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                s.Group.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SelectedProject))
        {
            filtered = filtered.Where(s =>
                string.Equals(s.ProjectName, SelectedProject, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Servers = new ObservableCollection<ServerItemViewModel>(filteredList);
        RebuildGroupedView(filteredList);
        SynchronizeSelection(preferredSelectedServerId);
    }

    /// <summary>
    /// Rebuilds the hierarchical folder tree from server Group paths.
    /// The Group field is the full folder path (e.g., "ADSEC/Gateway/Linux").
    /// Servers without a Group appear at the tree root.
    /// Empty folders from settings are included even without servers.
    /// </summary>
    private void RebuildGroupedView(List<ServerItemViewModel> filteredServers)
    {
        // Build the folder tree from server Group paths
        var root = new FolderViewModel { Name = "__root__", FullPath = "" };

        foreach (var server in filteredServers)
        {
            string folderPath = (server.Group?.Trim() ?? "").Replace('\\', '/');
            if (string.IsNullOrEmpty(folderPath))
            {
                // Server at tree root (no folder)
                root.Servers.Add(server);
            }
            else
            {
                var folder = EnsureFolderPath(root, folderPath);
                folder.Servers.Add(server);
            }
        }

        // Add empty folders from settings
        if (_currentSettings?.EmptyGroups is not null)
        {
            foreach (var path in _currentSettings.EmptyGroups)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    EnsureFolderPath(root, path);
                }
            }
        }

        // Sort all levels: folders first (alphabetical), then servers (by SortOrder, then name)
        SortFolderRecursive(root);

        // The top-level children become GroupedServers
        // Root servers go into a virtual "(root)" folder only if there are also named folders
        var topFolders = root.SubFolders.ToList();
        var rootServers = root.Servers.ToList();

        if (rootServers.Count > 0)
        {
            var rootFolder = new FolderViewModel
            {
                Name = _localizer["TreeNodeNoGroup"],
                FullPath = "",
                IsExpanded = _expandedNodes.Contains("::nogroup"),
                Servers = new ObservableCollection<ServerItemViewModel>(rootServers)
            };
            rootFolder.PropertyChanged += OnFolderExpandedChanged;
            topFolders.Add(rootFolder);
        }

        GroupedServers = new ObservableCollection<FolderViewModel>(topFolders);
    }

    /// <summary>
    /// Ensures a folder path exists in the tree, creating intermediate folders as needed.
    /// "ADSEC/Gateway/Linux" creates ADSEC → Gateway → Linux.
    /// </summary>
    private FolderViewModel EnsureFolderPath(FolderViewModel root, string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        var pathSoFar = "";

        foreach (var segment in segments)
        {
            pathSoFar = string.IsNullOrEmpty(pathSoFar) ? segment : $"{pathSoFar}/{segment}";

            var existing = current.SubFolders.FirstOrDefault(
                f => string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                current = existing;
            }
            else
            {
                var newFolder = new FolderViewModel
                {
                    Name = segment,
                    FullPath = pathSoFar,
                    IsExpanded = _expandedNodes.Contains(pathSoFar)
                };
                newFolder.PropertyChanged += OnFolderExpandedChanged;
                current.SubFolders.Add(newFolder);
                current = newFolder;
            }
        }

        return current;
    }

    private static void SortFolderRecursive(FolderViewModel folder)
    {
        // Sort sub-folders alphabetically
        var sortedFolders = folder.SubFolders
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        folder.SubFolders = new ObservableCollection<FolderViewModel>(sortedFolders);

        // Sort servers by SortOrder then name
        var sortedServers = folder.Servers
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        folder.Servers = new ObservableCollection<ServerItemViewModel>(sortedServers);

        // Recurse
        foreach (var sub in folder.SubFolders)
        {
            SortFolderRecursive(sub);
        }
    }

    /// <summary>
    /// Tracks expand/collapse state changes on folder nodes and schedules
    /// a debounced save of TreeExpandedNodes to settings.
    /// </summary>
    private void OnFolderExpandedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderViewModel.IsExpanded) || sender is not FolderViewModel folder)
        {
            return;
        }

        var key = folder.ExpansionKey;
        if (folder.IsExpanded)
        {
            _expandedNodes.Add(key);
        }
        else
        {
            _expandedNodes.Remove(key);
        }

        ScheduleExpandStateSave();
    }

    /// <summary>
    /// Debounced save of TreeExpandedNodes — waits 500ms after last toggle
    /// before writing to disk, to avoid spamming settings.json on rapid clicks.
    /// </summary>
    private void ScheduleExpandStateSave()
    {
        _expandSaveTimer?.Dispose();
        _expandSaveTimer = new System.Threading.Timer(
            _ => SaveExpandStateAsync(),
            null,
            TimeSpan.FromMilliseconds(500),
            Timeout.InfiniteTimeSpan);
    }

    private async void SaveExpandStateAsync()
    {
        try
        {
            var settings = await _configManager.LoadSettingsAsync();
            settings.TreeExpandedNodes = _expandedNodes.ToList();
            await _configManager.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Failed to save tree expand state: {ex.Message}");
        }
    }

    private void RefreshLookupCollections(AppSettings settings)
    {
        _projectTargets = settings.Projects
            .Where(project => !string.IsNullOrWhiteSpace(project.Id) && !string.IsNullOrWhiteSpace(project.Name))
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .Select(project => new ProjectTarget(project.Id, project.Name, project.Color ?? string.Empty))
            .ToList();

        Projects = new ObservableCollection<string>(_projectTargets.Select(project => project.Name));

        Groups = new ObservableCollection<string>(
            _allServers
                .Where(server => !string.IsNullOrWhiteSpace(server.Group))
                .Select(server => server.Group)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
    }

    private void PopulateServerDialogOptions(ServerDialogViewModel dialogVm, AppSettings settings)
    {
        dialogVm.AvailableGateways = new(BuildGatewayOptions(settings.SshGateways));
    }

    private void SynchronizeSelection(string? preferredSelectedServerId)
    {
        var targetId = preferredSelectedServerId ?? SelectedServer?.Id;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            SelectedServer = null;
            return;
        }

        SelectedServer = Servers.FirstOrDefault(
            server => string.Equals(server.Id, targetId, StringComparison.Ordinal));
    }

    private static Dictionary<string, ProjectDto> BuildProjectMap(AppSettings settings)
    {
        return settings.Projects
            .Where(project => !string.IsNullOrWhiteSpace(project.Id))
            .GroupBy(project => project.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
    }

    private static IEnumerable<GatewayOption> BuildGatewayOptions(IEnumerable<SshGatewayDto> gateways)
    {
        var gatewayList = gateways.ToList();
        var gatewayMap = gatewayList
            .Where(gateway => !string.IsNullOrWhiteSpace(gateway.Id))
            .ToDictionary(gateway => gateway.Id, gateway => gateway, StringComparer.Ordinal);

        return gatewayList.Select(gateway => new GatewayOption(
            gateway.Id,
            FormatGatewayDisplayText(gateway),
            gateway.Name,
            gateway.Host,
            gateway.Port,
            BuildGatewayRouteText(gateway, gatewayMap)));
    }

    private static string BuildGatewayRouteText(
        SshGatewayDto gateway,
        IReadOnlyDictionary<string, SshGatewayDto> gatewayMap)
    {
        var route = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = gateway;

        while (current is not null && !string.IsNullOrWhiteSpace(current.Id) && visited.Add(current.Id))
        {
            route.Insert(0, FormatGatewayDisplayText(current));

            if (string.IsNullOrWhiteSpace(current.ParentGatewayId) ||
                !gatewayMap.TryGetValue(current.ParentGatewayId, out current))
            {
                break;
            }
        }

        return string.Join(" -> ", route);
    }

    private static string FormatGatewayDisplayText(SshGatewayDto gateway)
    {
        return $"{gateway.Name} ({gateway.Host}:{gateway.Port})";
    }

    private static ProjectDto? ResolveProject(IReadOnlyDictionary<string, ProjectDto> projectMap, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        return projectMap.TryGetValue(projectId, out var project) ? project : null;
    }

    private static void ApplyProjectMetadata(ServerItemViewModel server, ProjectDto? project)
    {
        server.ProjectName = project?.Name ?? string.Empty;
        server.ProjectColor = project?.Color ?? string.Empty;
    }

    private static bool MatchesGroup(ServerProfileDto server, string? projectId, string groupName)
    {
        return string.Equals(server.ProjectId ?? string.Empty, projectId ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(server.Group ?? string.Empty, groupName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesGroup(ServerItemViewModel server, string? projectId, string groupName)
    {
        return string.Equals(server.ProjectId, projectId ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(server.Group, groupName, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveProjectNodeName(
        IGrouping<string, ServerItemViewModel> projectGroup,
        string noProjectLabel)
    {
        var projectName = projectGroup.FirstOrDefault()?.ProjectName;
        return string.IsNullOrWhiteSpace(projectName) ? noProjectLabel : projectName;
    }

    private static string ResolveGroupNodeName(
        IGrouping<string, ServerItemViewModel> group,
        string noGroupLabel)
    {
        var groupName = group.FirstOrDefault()?.Group;
        return string.IsNullOrWhiteSpace(groupName) ? noGroupLabel : groupName;
    }

    private void OnConnectionStateChanged(
        string serverId,
        Heimdall.Core.Models.ConnectionState previousState,
        Heimdall.Core.Models.ConnectionState newState,
        string? error)
    {
        // State machine events may fire from background threads; marshal to UI thread
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var server = _allServers.FirstOrDefault(s =>
                string.Equals(s.Id, serverId, StringComparison.Ordinal));

            if (server is not null)
            {
                server.ConnectionState = newState.ToString();
            }
        });
    }
}

public sealed record ServerDialogSeed(string? ProjectId, string? GroupName);

public sealed record ServerMoveToProjectRequest(ServerItemViewModel Server, string? ProjectId);

public sealed record ServerMoveToGroupRequest(ServerItemViewModel Server, string? GroupName);

public sealed record ServerGroupContext(
    string? ProjectId,
    string ProjectName,
    string GroupName,
    bool IsVirtualGroup);

public sealed record ProjectTarget(
    string Id,
    string Name,
    string Color,
    bool IsVirtualProject = false);

public sealed record GroupTarget(
    string GroupName,
    string DisplayName,
    bool IsVirtualGroup = false);
