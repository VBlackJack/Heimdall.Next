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
/// Maps from <see cref="ServerProfileDto"/> for UI binding.
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

    [ObservableProperty]
    private string _macAddress = "";

    [ObservableProperty]
    private string _tags = "";

    [ObservableProperty]
    private string _endpoint = "";

    public bool IsActiveSession =>
        !string.IsNullOrEmpty(ConnectionState)
        && !string.Equals(ConnectionState, "Disconnected", StringComparison.OrdinalIgnoreCase);

    public string ConnectionTypeBadge => ConnectionType.ToUpperInvariant() switch
    {
        "RDP" => "RDP",
        "SSH" => "SSH",
        "SFTP" => "SFTP",
        "FTP" => "FTP",
        "VNC" => "VNC",
        "TELNET" => "TEL",
        "CITRIX" => "CTX",
        "LOCAL" => "SH",
        _ when ConnectionType.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase) => "TOOL",
        _ => ConnectionType.ToUpperInvariant()
    };

    /// <summary>
    /// Creates a <see cref="ServerItemViewModel"/> from a <see cref="ServerProfileDto"/>.
    /// </summary>
    public static ServerItemViewModel FromDto(
        ServerProfileDto dto,
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
            Tags = dto.Tags ?? "",
            Endpoint = FormatEndpoint(dto),
            IsFavorite = dto.IsFavorite,
            SortOrder = dto.SortOrder,
            MacAddress = dto.MacAddress ?? "",
        };
    }

    /// <summary>
    /// Applies updated values from a <see cref="ServerProfileDto"/> to this ViewModel.
    /// </summary>
    public void UpdateFromDto(ServerProfileDto dto, ProjectDto? project = null)
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
        Tags = dto.Tags ?? "";
        Endpoint = FormatEndpoint(dto);
        IsFavorite = dto.IsFavorite;
        SortOrder = dto.SortOrder;
        MacAddress = dto.MacAddress ?? "";
    }

    partial void OnConnectionTypeChanged(string value)
    {
        OnPropertyChanged(nameof(ConnectionTypeBadge));
    }


    partial void OnRemoteServerChanged(string value)
        => Endpoint = string.IsNullOrEmpty(value) ? "" : (RemotePort > 0 ? $"{value}:{RemotePort}" : value);

    partial void OnConnectionStateChanged(string value)
        => OnPropertyChanged(nameof(IsActiveSession));

    private static string FormatEndpoint(ServerProfileDto dto)
    {
        var type = dto.ConnectionType?.ToUpperInvariant();
        if (type is "LOCAL" or "CITRIX")
        {
            return "";
        }

        var host = dto.RemoteServer;
        if (string.IsNullOrEmpty(host)) return "";

        var port = type switch
        {
            "SSH" or "SFTP" => dto.SshPort,
            "FTP" => dto.FtpPort,
            "VNC" => dto.VncPort,
            "TELNET" => dto.TelnetPort,
            _ => dto.RemotePort
        };

        return port > 0 ? $"{host}:{port}" : host;
    }

    private static string GetUsername(ServerProfileDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.SshUsername)) return dto.SshUsername;
        if (!string.IsNullOrWhiteSpace(dto.RdpUsername)) return dto.RdpUsername;
        if (!string.IsNullOrWhiteSpace(dto.FtpUsername)) return dto.FtpUsername;
        if (!string.IsNullOrWhiteSpace(dto.TelnetUsername)) return dto.TelnetUsername;
        return "";
    }
}
