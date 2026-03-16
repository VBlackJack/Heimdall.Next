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
using Heimdall.Core.Configuration;

namespace Heimdall.App.ViewModels;

/// <summary>
/// ViewModel representing a single server item in the server list.
/// Maps from <see cref="RdpServerDto"/> for UI binding.
/// </summary>
public partial class ServerItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _remoteServer = "";

    [ObservableProperty]
    private int _remotePort;

    [ObservableProperty]
    private string _group = "";

    [ObservableProperty]
    private string _connectionType = "RDP";

    [ObservableProperty]
    private string _connectionState = "Disconnected";

    [ObservableProperty]
    private string _environment = "";

    [ObservableProperty]
    private string _projectId = "";

    [ObservableProperty]
    private string _projectName = "";

    [ObservableProperty]
    private string _projectColor = "";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private int _sortOrder;

    public string ConnectionTypeBadge => ConnectionType.ToUpperInvariant() switch
    {
        "SSH" => "S",
        "SFTP" => "F",
        _ => "R"
    };

    /// <summary>
    /// Creates a <see cref="ServerItemViewModel"/> from a <see cref="RdpServerDto"/>.
    /// </summary>
    public static ServerItemViewModel FromDto(
        RdpServerDto dto,
        ProjectDto? project = null,
        string connectionState = "Disconnected")
    {
        return new ServerItemViewModel
        {
            Id = dto.Id,
            DisplayName = dto.DisplayName,
            RemoteServer = dto.RemoteServer,
            RemotePort = dto.RemotePort,
            Group = dto.Group ?? "",
            ConnectionType = dto.ConnectionType,
            ConnectionState = connectionState,
            Environment = dto.Environment ?? "",
            ProjectId = dto.ProjectId ?? "",
            ProjectName = project?.Name ?? "",
            ProjectColor = project?.Color ?? "",
            Username = GetUsername(dto),
            IsFavorite = dto.IsFavorite,
            SortOrder = dto.SortOrder,
        };
    }

    /// <summary>
    /// Applies updated values from a <see cref="RdpServerDto"/> to this ViewModel.
    /// </summary>
    public void UpdateFromDto(RdpServerDto dto, ProjectDto? project = null)
    {
        DisplayName = dto.DisplayName;
        RemoteServer = dto.RemoteServer;
        RemotePort = dto.RemotePort;
        Group = dto.Group ?? "";
        ConnectionType = dto.ConnectionType;
        Environment = dto.Environment ?? "";
        ProjectId = dto.ProjectId ?? "";
        ProjectName = project?.Name ?? "";
        ProjectColor = project?.Color ?? "";
        Username = GetUsername(dto);
        IsFavorite = dto.IsFavorite;
        SortOrder = dto.SortOrder;
    }

    partial void OnConnectionTypeChanged(string value)
    {
        OnPropertyChanged(nameof(ConnectionTypeBadge));
    }

    private static string GetUsername(RdpServerDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.SshUsername))
        {
            return dto.SshUsername;
        }

        return dto.RdpUsername ?? "";
    }
}
