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
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
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

    private List<ServerItemViewModel> _allServers = [];

    [ObservableProperty]
    private ObservableCollection<ServerItemViewModel> _servers = [];

    [ObservableProperty]
    private ObservableCollection<string> _projects = [];

    [ObservableProperty]
    private ObservableCollection<string> _groups = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedProject = "";

    [ObservableProperty]
    private ServerItemViewModel? _selectedServer;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    public ServerListViewModel(
        ConfigManager configManager,
        LocalizationManager localizer,
        ConnectionStateMachine connectionSm)
    {
        _configManager = configManager;
        _localizer = localizer;
        _connectionSm = connectionSm;

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

        // Connection orchestration will be implemented in Phase 4B
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task AddServerAsync(CancellationToken cancellationToken)
    {
        // Server dialog will be implemented in Phase 4B
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task EditServerAsync(ServerItemViewModel? server, CancellationToken cancellationToken)
    {
        if (server is null)
        {
            return;
        }

        // Server edit dialog will be implemented in Phase 4B
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteServerAsync(ServerItemViewModel? server, CancellationToken cancellationToken)
    {
        if (server is null)
        {
            return;
        }

        // Delete confirmation and persistence will be implemented in Phase 4B
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DuplicateServerAsync(ServerItemViewModel? server, CancellationToken cancellationToken)
    {
        if (server is null)
        {
            return;
        }

        // Duplication logic will be implemented in Phase 4B
        await Task.CompletedTask;
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

        Servers = new ObservableCollection<ServerItemViewModel>(
            filtered.OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase));
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
