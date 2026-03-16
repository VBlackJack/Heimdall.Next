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

namespace Heimdall.Core.Models;

/// <summary>
/// Server connection entry supporting RDP, SSH, and SFTP connection types.
/// </summary>
public partial class RdpServer : ObservableObject
{
    #region Identity

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TunnelDescription))]
    private string _remoteServer = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TunnelDescription))]
    private int _remotePort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TunnelDescription))]
    private int _localPort;

    [ObservableProperty]
    private string _group = string.Empty;

    [ObservableProperty]
    private string _tags = string.Empty;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private int _sortOrder;

    #endregion

    #region Connection

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRdpConnection))]
    [NotifyPropertyChangedFor(nameof(IsSshConnection))]
    [NotifyPropertyChangedFor(nameof(IsSftpConnection))]
    [NotifyPropertyChangedFor(nameof(ConnectionTypeIcon))]
    private ConnectionType _connectionType = ConnectionType.Rdp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TunnelDescription))]
    private string? _sshGatewayId;

    [ObservableProperty]
    private string? _sshUsername;

    [ObservableProperty]
    private int _sshPort = 22;

    [ObservableProperty]
    private SshMode _sshMode = SshMode.External;

    [ObservableProperty]
    private string? _sshKeyPath;

    [ObservableProperty]
    private string? _sshPasswordEncrypted;

    [ObservableProperty]
    private bool _sshCompression;

    [ObservableProperty]
    private bool _sshX11Forwarding;

    [ObservableProperty]
    private bool _sshAgentForwarding;

    [ObservableProperty]
    private string? _sshHostKeyFingerprint;

    [ObservableProperty]
    private bool _useDirectConnection;

    #endregion

    #region RDP Settings

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCredentials))]
    private string? _rdpUsername;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCredentials))]
    private string? _rdpPasswordEncrypted;

    [ObservableProperty]
    private RdpMode _rdpMode = RdpMode.External;

    [ObservableProperty]
    private bool _rdpUseGlobalDefaults = true;

    [ObservableProperty]
    private bool _rdpRedirectClipboard = true;

    [ObservableProperty]
    private bool _rdpRedirectDrives;

    [ObservableProperty]
    private bool _rdpRedirectPrinters;

    [ObservableProperty]
    private bool _rdpRedirectComPorts;

    [ObservableProperty]
    private bool _rdpRedirectSmartCards;

    [ObservableProperty]
    private bool _rdpRedirectWebcam;

    [ObservableProperty]
    private bool _rdpRedirectUsb;

    [ObservableProperty]
    private RdpAudioMode _rdpAudioMode = RdpAudioMode.Disabled;

    [ObservableProperty]
    private bool _rdpAudioCapture;

    [ObservableProperty]
    private bool _rdpMultiMonitor;

    [ObservableProperty]
    private bool _rdpDynamicResolution = true;

    [ObservableProperty]
    private bool _rdpNla = true;

    [ObservableProperty]
    private int _rdpColorDepth = 32;

    [ObservableProperty]
    private bool _rdpBitmapCaching = true;

    [ObservableProperty]
    private bool _rdpCompression = true;

    [ObservableProperty]
    private bool _rdpAutoReconnect = true;

    [ObservableProperty]
    private string? _rdpGateway;

    [ObservableProperty]
    private bool _rdpAntiIdle;

    [ObservableProperty]
    private AspectRatio _rdpAspectRatio = AspectRatio.Stretch;

    #endregion

    #region Metadata

    [ObservableProperty]
    private string? _macAddress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProduction))]
    private EnvironmentType _environment = EnvironmentType.None;

    [ObservableProperty]
    private string? _projectId;

    [ObservableProperty]
    private DateTime _createdAt = DateTime.UtcNow;

    [ObservableProperty]
    private DateTime? _lastConnectedAt;

    #endregion

    #region Validation

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private string? _validationError;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Whether stored RDP credentials are present.
    /// </summary>
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(RdpUsername) && !string.IsNullOrWhiteSpace(RdpPasswordEncrypted);

    public bool IsRdpConnection => ConnectionType == ConnectionType.Rdp;

    public bool IsSshConnection => ConnectionType == ConnectionType.Ssh;

    public bool IsSftpConnection => ConnectionType == ConnectionType.Sftp;

    /// <summary>
    /// Segoe MDL2 icon glyph for the connection type.
    /// </summary>
    public string ConnectionTypeIcon => ConnectionType switch
    {
        ConnectionType.Ssh => "\uE756",
        ConnectionType.Sftp => "\uE8B7",
        _ => "\uE7F4"
    };

    /// <summary>
    /// Whether the server is in a production environment.
    /// </summary>
    public bool IsProduction => Environment == EnvironmentType.Production;

    /// <summary>
    /// Tunnel port mapping description (e.g., ":3389 -> host:3389").
    /// </summary>
    public string TunnelDescription => $":{LocalPort} -> {RemoteServer}:{RemotePort}";

    /// <summary>
    /// Parsed tag list from the comma-separated <see cref="Tags"/> string.
    /// </summary>
    public string[] TagList
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Tags))
            {
                return [];
            }

            return Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
    }

    #endregion

    #region Search

    private string? _searchableText;

    /// <summary>
    /// Returns true if this server matches the given search query (case-insensitive).
    /// </summary>
    public bool Matches(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return GetSearchableText().Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private string GetSearchableText()
    {
        return _searchableText ??= string.Join('\n',
            DisplayName, RemoteServer, Group,
            RdpUsername ?? string.Empty, ConnectionType.ToString(),
            SshUsername ?? string.Empty, Tags, Environment.ToString())
            .ToLowerInvariant();
    }

    /// <summary>
    /// Invalidates the cached searchable text. Called automatically when relevant properties change.
    /// </summary>
    partial void OnDisplayNameChanged(string value) => InvalidateSearchableText();
    partial void OnRemoteServerChanged(string value) => InvalidateSearchableText();
    partial void OnGroupChanged(string value) => InvalidateSearchableText();
    partial void OnRdpUsernameChanged(string? value) => InvalidateSearchableText();
    partial void OnConnectionTypeChanged(ConnectionType value) => InvalidateSearchableText();
    partial void OnSshUsernameChanged(string? value) => InvalidateSearchableText();
    partial void OnTagsChanged(string value) => InvalidateSearchableText();
    partial void OnEnvironmentChanged(EnvironmentType value) => InvalidateSearchableText();

    private void InvalidateSearchableText() => _searchableText = null;

    #endregion
}
