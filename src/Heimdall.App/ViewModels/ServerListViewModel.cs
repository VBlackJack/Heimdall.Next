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
    private readonly IDialogService _dialogService;

    private List<ServerItemViewModel> _allServers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private ObservableCollection<ServerItemViewModel> _servers = [];

    [ObservableProperty]
    private ObservableCollection<ServerGroupViewModel> _groupedServers = [];

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
    /// Parameters: serverId, displayName, connectionType, session object.
    /// </summary>
    public event Action<string, string, string, object?>? SessionReady;

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
    public void LoadServers(List<RdpServerDto> serverDtos, AppSettings settings)
    {
        _allServers = serverDtos
            .Select(ServerItemViewModel.FromDto)
            .ToList();

        // Extract distinct projects from settings
        var projectNames = settings.Projects
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        Projects = new ObservableCollection<string>(projectNames);

        // Extract distinct groups from servers
        var groupNames = _allServers
            .Where(s => !string.IsNullOrWhiteSpace(s.Group))
            .Select(s => s.Group)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        Groups = new ObservableCollection<string>(groupNames);

        IsSidebarVisible = !settings.SidebarCollapsed;

        ApplyFilter();
    }

    [RelayCommand]
    private async Task ConnectAsync(ServerItemViewModel? server, CancellationToken cancellationToken)
    {
        if (server is null)
        {
            return;
        }

        // Load full server DTO
        var servers = await _configManager.LoadServersAsync();
        var serverDto = servers.FirstOrDefault(
            s => string.Equals(s.Id, server.Id, StringComparison.Ordinal));

        if (serverDto is null)
        {
            return;
        }

        var settings = await _configManager.LoadSettingsAsync();

        // Preflight checks
        var preflight = _connectionService.RunPreflight(serverDto, settings);
        if (!preflight.Success)
        {
            server.ConnectionState = "Error";
            _dialogService.ShowError(
                _localizer["ErrorPreflightTitle"],
                preflight.Message ?? _localizer["ErrorPreflightFailed"]);
            return;
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Initializing);

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

                default:
                    _connectionSm.SetError(server.Id,
                        _localizer.Format("ErrorUnsupportedConnectionType", serverDto.ConnectionType ?? ""));
                    server.ConnectionState = "Error";
                    return;
            }

            if (result.Success)
            {
                SessionReady?.Invoke(
                    server.Id, server.DisplayName,
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
            _connectionSm.Reset(server.Id);
        }
        catch (Exception ex)
        {
            var failure = Ssh.FailureClassifier.Classify(ex);
            _connectionSm.SetError(server.Id, failure.Message);
            server.ConnectionState = "Error";
            _dialogService.ShowError(
                _localizer["ErrorConnectionTitle"], failure.Message);
        }
    }

    [RelayCommand]
    private async Task AddServerAsync(CancellationToken cancellationToken)
    {
        var dialogVm = new ServerDialogViewModel
        {
            DialogTitle = _localizer["DialogTitleAddServer"]
        };

        // Populate available gateways and projects from config
        var settings = await _configManager.LoadSettingsAsync();

        dialogVm.AvailableGateways = new(settings.SshGateways.Select(
            g => new GatewayOption(g.Id, $"{g.Name} ({g.Host})")));

        dialogVm.AvailableProjects = new(settings.Projects.Select(
            p => new ProjectOption(p.Id, p.Name, p.Color ?? "#3B82F6")));

        var result = await _dialogService.ShowServerDialogAsync(dialogVm);

        if (result is not { Saved: true })
        {
            return;
        }

        var servers = await _configManager.LoadServersAsync();
        result.Server.Id = Guid.NewGuid().ToString();
        servers.Add(result.Server);
        await _configManager.SaveServersAsync(servers);

        var newItem = ServerItemViewModel.FromDto(result.Server);
        _allServers.Add(newItem);
        ApplyFilter();
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

        // Load the full DTO for editing
        var servers = await _configManager.LoadServersAsync();
        var serverDto = servers.FirstOrDefault(
            s => string.Equals(s.Id, server.Id, StringComparison.Ordinal));

        if (serverDto is null)
        {
            return;
        }

        var dialogVm = ServerDialogViewModel.FromDto(serverDto);
        dialogVm.DialogTitle = _localizer["DialogTitleEditServer"];

        // Populate available gateways and projects
        var settings = await _configManager.LoadSettingsAsync();

        dialogVm.AvailableGateways = new(settings.SshGateways.Select(
            g => new GatewayOption(g.Id, $"{g.Name} ({g.Host})")));

        dialogVm.AvailableProjects = new(settings.Projects.Select(
            p => new ProjectOption(p.Id, p.Name, p.Color ?? "#3B82F6")));

        var result = await _dialogService.ShowServerDialogAsync(dialogVm);

        if (result is not { Saved: true })
        {
            return;
        }

        // Preserve the original ID
        result.Server.Id = serverDto.Id;

        // Update the DTO in the list
        var index = servers.FindIndex(
            s => string.Equals(s.Id, serverDto.Id, StringComparison.Ordinal));

        if (index >= 0)
        {
            servers[index] = result.Server;
            await _configManager.SaveServersAsync(servers);
        }

        // Update the ViewModel item in place
        server.UpdateFromDto(result.Server);
        ApplyFilter();
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

        // Deep copy by serializing and deserializing
        var json = System.Text.Json.JsonSerializer.Serialize(sourceDto);
        var clone = System.Text.Json.JsonSerializer.Deserialize<RdpServerDto>(json);

        if (clone is null)
        {
            return;
        }

        clone.Id = Guid.NewGuid().ToString();
        clone.DisplayName = $"{sourceDto.DisplayName} ({_localizer["LabelCopy"]})";

        // Clear encrypted credentials from the copy for security
        clone.RdpPasswordEncrypted = null;

        servers.Add(clone);
        await _configManager.SaveServersAsync(servers);

        var newItem = ServerItemViewModel.FromDto(clone);
        _allServers.Add(newItem);
        ApplyFilter();
        OnPropertyChanged(nameof(Servers));
        OnPropertyChanged(nameof(IsEmpty));
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

    private void ApplyFilter()
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
    }

    /// <summary>
    /// Rebuilds the hierarchical grouped view from the filtered server list.
    /// Groups servers by their Group property and sorts alphabetically.
    /// </summary>
    private void RebuildGroupedView(List<ServerItemViewModel> filteredServers)
    {
        var ungroupedLabel = "Ungrouped";

        var groups = filteredServers
            .GroupBy(s => string.IsNullOrWhiteSpace(s.Group) ? ungroupedLabel : s.Group,
                     StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => string.Equals(g.Key, ungroupedLabel, StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ServerGroupViewModel
            {
                GroupName = g.Key,
                Servers = new ObservableCollection<ServerItemViewModel>(
                    g.OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
            })
            .ToList();

        GroupedServers = new ObservableCollection<ServerGroupViewModel>(groups);
    }

    private void OnConnectionStateChanged(
        string serverId,
        Heimdall.Core.Models.ConnectionState previousState,
        Heimdall.Core.Models.ConnectionState newState,
        string? error)
    {
        var server = _allServers.FirstOrDefault(s =>
            string.Equals(s.Id, serverId, StringComparison.Ordinal));

        if (server is not null)
        {
            server.ConnectionState = newState.ToString();
        }
    }
}
