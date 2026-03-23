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
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the redesigned server add/edit dialog.
/// Keeps the persisted DTO model intact while exposing UX-friendly
/// derived state for tunnel routing, authentication, and option grouping.
/// </summary>
public partial class ServerDialogViewModel : ObservableValidator
{
    private const int DefaultRdpPort = 3389;
    private const int DefaultSshPort = 22;
    private const int DefaultTelnetPort = 23;
    private const int DefaultRdpTunnelPort = 33890;
    private const int DefaultSshTunnelPort = 2222;

    /// <summary>
    /// Localizer for translating validation error messages. Set by the dialog service.
    /// </summary>
    public LocalizationManager? Localizer { get; set; }

    // --- Dialog state ---

    [ObservableProperty]
    private string _dialogTitle = "";

    [ObservableProperty]
    private bool _isEditMode;

    // --- Identity ---

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Display name is required.")]
    [MinLength(1, ErrorMessage = "Display name cannot be empty.")]
    private string _displayName = "";

    [ObservableProperty]
    private string _remoteServer = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    private int _remotePort = DefaultRdpPort;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "Local tunnel port must be between 1 and 65535.")]
    private int _localPort = DefaultRdpTunnelPort;

    [ObservableProperty]
    private bool _useAutomaticTunnelPort = true;

    [ObservableProperty]
    private string _group = "";

    [ObservableProperty]
    private string _connectionType = "RDP";

    // --- SSH settings ---

    [ObservableProperty]
    private string _sshUsername = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "SSH port must be between 1 and 65535.")]
    private int _sshPort = DefaultSshPort;

    [ObservableProperty]
    private string _sshKeyPath = "";

    [ObservableProperty]
    private string _sshPassword = "";

    // Existing encrypted passwords (preserved on edit if user doesn't change them)
    public string? ExistingSshPasswordEncrypted { get; set; }
    public string? ExistingRdpPasswordEncrypted { get; set; }

    [ObservableProperty]
    private bool _sshCompression;

    [ObservableProperty]
    private bool _sshX11Forwarding;

    [ObservableProperty]
    private bool _sshAgentForwarding;

    [ObservableProperty]
    private string _sshMode = "Embedded";

    // --- Local Shell settings ---

    [ObservableProperty]
    private string _localShellExecutable = "powershell.exe";

    [ObservableProperty]
    private string _localShellArguments = "";

    [ObservableProperty]
    private string _localShellWorkingDirectory = "";

    [ObservableProperty]
    private bool _localShellElevated;

    [ObservableProperty]
    private Core.Models.ElevationMode _elevationMode = Core.Models.ElevationMode.None;

    // --- Citrix settings ---

    [ObservableProperty]
    private string _citrixStoreFrontUrl = "";

    [ObservableProperty]
    private string _citrixAppName = "";

    [ObservableProperty]
    private string _citrixIcaFilePath = "";

    [ObservableProperty]
    private bool _citrixSeamlessMode = true;

    [ObservableProperty]
    private bool _citrixUseSso = true;

    // --- FTP settings ---

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "FTP port must be between 1 and 65535.")]
    private int _ftpPort = 21;

    [ObservableProperty]
    private string _ftpUsername = "";

    [ObservableProperty]
    private string _ftpPassword = "";

    // Existing encrypted FTP password (preserved on edit if user doesn't change it)
    public string? ExistingFtpPasswordEncrypted { get; set; }

    // --- Telnet settings ---

    [ObservableProperty]
    private string _telnetUsername = "";

    [ObservableProperty]
    private string _telnetPassword = "";

    public string? ExistingTelnetPasswordEncrypted { get; set; }

    // --- FTP options ---

    [ObservableProperty]
    private bool _ftpPassiveMode = true;

    [ObservableProperty]
    private bool _ftpUseSsl;

    // --- VNC settings ---

    [ObservableProperty]
    private int _vncPort = 5900;

    [ObservableProperty]
    private string _vncPassword = "";

    [ObservableProperty]
    private bool _vncViewOnly;

    // Existing encrypted VNC password (preserved on edit if user doesn't change it)
    public string? ExistingVncPasswordEncrypted { get; set; }

    // --- RDP settings ---

    [ObservableProperty]
    private string _rdpUsername = "";

    [ObservableProperty]
    private string _rdpPassword = "";

    [ObservableProperty]
    private string _rdpMode = "Embedded";

    [ObservableProperty]
    private bool _rdpUseGlobalDefaults = true;

    [ObservableProperty]
    private bool _rdpAntiIdle;

    [ObservableProperty]
    private bool _redirectClipboard = true;

    [ObservableProperty]
    private bool _redirectDrives;

    [ObservableProperty]
    private bool _redirectPrinters;

    [ObservableProperty]
    private bool _rdpRedirectComPorts;

    [ObservableProperty]
    private bool _rdpRedirectSmartCards;

    [ObservableProperty]
    private bool _rdpRedirectWebcam;

    [ObservableProperty]
    private bool _rdpRedirectUsb;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 2, ErrorMessage = "Audio mode must be 0 (disabled), 1 (local), or 2 (remote).")]
    private int _rdpAudioMode;

    [ObservableProperty]
    private bool _rdpAudioCapture;

    [ObservableProperty]
    private bool _rdpMultiMonitor;

    [ObservableProperty]
    private bool _rdpDynamicResolution = true;

    [ObservableProperty]
    private bool _rdpNla = true;

    [ObservableProperty]
    private string _rdpAspectRatio = "Stretch";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(8, 32, ErrorMessage = "Color depth must be between 8 and 32.")]
    private int _rdpColorDepth = 32;

    [ObservableProperty]
    private bool _rdpBitmapCaching = true;

    [ObservableProperty]
    private bool _rdpCompression = true;

    [ObservableProperty]
    private bool _rdpAutoReconnect = true;

    [ObservableProperty]
    private string _rdpGateway = "";

    // --- Gateway ---

    [ObservableProperty]
    private string _selectedGatewayId = "";

    [ObservableProperty]
    private bool _directConnection;

    [ObservableProperty]
    private ObservableCollection<GatewayOption> _availableGateways = [];

    [ObservableProperty]
    private string? _gatewayTestMessage;

    // --- Project ---

    [ObservableProperty]
    private string _selectedProjectId = "";

    [ObservableProperty]
    private ObservableCollection<ProjectOption> _availableProjects = [];

    // --- Metadata ---

    [ObservableProperty]
    private string _tags = "";

    [ObservableProperty]
    private string _macAddress = "";

    [ObservableProperty]
    private string _environment = "None";

    [ObservableProperty]
    private bool _isFavorite;

    // --- Validation ---

    [ObservableProperty]
    private string? _validationError;

    public bool IsRdpConnection => string.Equals(ConnectionType, "RDP", StringComparison.OrdinalIgnoreCase);

    public bool IsSshConnection => string.Equals(ConnectionType, "SSH", StringComparison.OrdinalIgnoreCase);

    public bool IsSftpConnection => string.Equals(ConnectionType, "SFTP", StringComparison.OrdinalIgnoreCase);

    public bool IsCitrixConnection => string.Equals(ConnectionType, "Citrix", StringComparison.OrdinalIgnoreCase);

    public bool IsFtpConnection => string.Equals(ConnectionType, "FTP", StringComparison.OrdinalIgnoreCase);

    public bool IsVncConnection => string.Equals(ConnectionType, "VNC", StringComparison.OrdinalIgnoreCase);

    public bool IsTelnetConnection => string.Equals(ConnectionType, "Telnet", StringComparison.OrdinalIgnoreCase);

    public bool IsSshFamilyConnection => IsSshConnection || IsSftpConnection;

    public bool UsesGateway => !DirectConnection && !string.IsNullOrWhiteSpace(SelectedGatewayId);

    public bool CanSelectGateway => !DirectConnection;

    public bool CanEditTunnelPort => UsesGateway && !UseAutomaticTunnelPort;

    public int EndpointPort
    {
        get => IsRdpConnection ? RemotePort
            : IsVncConnection ? VncPort
            : IsFtpConnection ? FtpPort
            : IsTelnetConnection ? RemotePort
            : SshPort;
        set
        {
            if (IsRdpConnection || IsTelnetConnection)
            {
                RemotePort = value;
            }
            else if (IsVncConnection)
            {
                VncPort = value;
            }
            else if (IsFtpConnection)
            {
                FtpPort = value;
            }
            else
            {
                SshPort = value;
            }
        }
    }

    public string EndpointPortLabel => IsRdpConnection ? "Remote RDP port"
        : IsVncConnection ? "VNC port"
        : IsFtpConnection ? "FTP port"
        : IsTelnetConnection ? "Telnet port"
        : "Remote SSH port";

    public string EndpointPortHelpText => IsRdpConnection
        ? "Remote desktop port on the destination server."
        : "SSH service port on the destination server.";

    public string LocalTunnelPortDisplay => UseAutomaticTunnelPort
        ? string.Format(CultureInfo.InvariantCulture, "Auto ({0})", LocalPort)
        : LocalPort.ToString(CultureInfo.InvariantCulture);

    public string ConnectionPathHeadline => UsesGateway
        ? "Heimdall will create an SSH tunnel before opening the session."
        : "Heimdall will connect directly to the destination host.";

    public string GatewayExplanation => UsesGateway
        ? "Traffic will be routed through this SSH gateway."
        : "Select a gateway if the server is only reachable through an SSH jump host.";

    public string GatewayRouteText => SelectedGateway?.EffectiveRouteText ?? "No gateway selected";

    public string SelectedGatewayTitle => SelectedGateway?.EffectiveName ?? "No gateway selected";

    public string SelectedGatewayEndpoint => SelectedGateway?.EndpointText ?? "No SSH gateway selected";

    public string SessionKindLabel => IsRdpConnection
        ? "RDP session"
        : IsFtpConnection
            ? "FTP session"
            : IsSftpConnection
                ? "SFTP session"
                : "SSH session";

    public string SessionModeSummary => IsRdpConnection
        ? "Remote Desktop opens after tunnel setup completes."
        : IsFtpConnection
            ? "The FTP browser connects directly using the credentials below."
            : IsSftpConnection
                ? "The SFTP browser reuses the SSH authentication settings below."
                : "The SSH shell connects directly or through the tunnel shown above.";

    public string TunnelSummary => UsesGateway
        ? string.Format(
            CultureInfo.InvariantCulture,
            "Local port {0} forwards to {1}:{2} through {3}.",
            LocalTunnelPortDisplay,
            GetDestinationHost(),
            EndpointPort,
            SelectedGateway?.EffectiveName ?? "the selected gateway")
        : "No SSH tunnel is required for this connection.";

    public string ClientNodeCaption => UsesGateway
        ? string.Format(CultureInfo.InvariantCulture, "Local tunnel on localhost:{0}", LocalTunnelPortDisplay)
        : "Direct outbound connection";

    public string GatewayNodeCaption => UsesGateway
        ? string.Format(CultureInfo.InvariantCulture, "{0}", SelectedGateway?.EffectiveName ?? "Gateway")
        : "Gateway not used";

    public string DestinationNodeCaption => string.IsNullOrWhiteSpace(RemoteServer)
        ? "Destination server"
        : string.Format(CultureInfo.InvariantCulture, "{0}:{1}", RemoteServer, EndpointPort);

    public string ClientToGatewayLabel => UsesGateway ? "SSH tunnel" : "Direct transport";

    public string GatewayToServerLabel => SessionKindLabel;

    /// <summary>
    /// Triggers full validation of all annotated properties.
    /// Sets <see cref="ValidationError"/> to the first error found, or null if valid.
    /// </summary>
    [RelayCommand]
    private void Validate()
    {
        ValidateAllProperties();

        if (!HasErrors && UsesGateway && !UseAutomaticTunnelPort && LocalPort <= 0)
        {
            ValidationError = Localizer?["ValidationTunnelPortRequired"]
                ?? "Enter a local tunnel port or switch back to Auto.";
            return;
        }

        ValidationError = HasErrors ? GetFirstError() : null;
    }

    /// <summary>
    /// Opens a file picker for SSH key selection.
    /// The View layer handles the actual file dialog; this command signals intent.
    /// </summary>
    [RelayCommand]
    private void BrowseSshKey()
    {
        // Intentionally empty: the View handles the actual file picker interaction.
    }

    [RelayCommand]
    private void TestGateway()
    {
        GatewayTestMessage = UsesGateway
            ? Localizer?["GatewayDiagnosticsNotAvailable"] ?? "Gateway diagnostics are not available yet."
            : Localizer?["GatewayDiagnosticsSelectGateway"] ?? "Select a gateway and disable direct connection to test tunneling.";
    }

    /// <summary>
    /// Maps the current ViewModel state to a flat DTO for persistence.
    /// </summary>
    public ServerProfileDto ToDto()
    {
        return new ServerProfileDto
        {
            DisplayName = DisplayName,
            RemoteServer = RemoteServer,
            RemotePort = RemotePort,
            LocalPort = LocalPort,
            Group = string.IsNullOrWhiteSpace(Group) ? null : Group,
            ConnectionType = ConnectionType,
            SshUsername = string.IsNullOrWhiteSpace(SshUsername) ? null : SshUsername,
            SshPort = SshPort,
            SshKeyPath = string.IsNullOrWhiteSpace(SshKeyPath) ? null : SshKeyPath,
            SshPasswordEncrypted = string.IsNullOrEmpty(SshPassword)
                ? ExistingSshPasswordEncrypted
                : Heimdall.Core.Security.CredentialProtector.Protect(SshPassword),
            SshCompression = SshCompression,
            SshX11Forwarding = SshX11Forwarding,
            SshAgentForwarding = SshAgentForwarding,
            SshMode = SshMode,
            LocalShellExecutable = string.IsNullOrWhiteSpace(LocalShellExecutable) ? null : LocalShellExecutable,
            LocalShellArguments = string.IsNullOrWhiteSpace(LocalShellArguments) ? null : LocalShellArguments,
            LocalShellWorkingDirectory = string.IsNullOrWhiteSpace(LocalShellWorkingDirectory) ? null : LocalShellWorkingDirectory,
            LocalShellElevated = ElevationMode != Core.Models.ElevationMode.None,
            ElevationMode = ElevationMode,
            CitrixStoreFrontUrl = string.IsNullOrWhiteSpace(CitrixStoreFrontUrl) ? null : CitrixStoreFrontUrl,
            CitrixAppName = string.IsNullOrWhiteSpace(CitrixAppName) ? null : CitrixAppName,
            CitrixIcaFilePath = string.IsNullOrWhiteSpace(CitrixIcaFilePath) ? null : CitrixIcaFilePath,
            CitrixSeamlessMode = CitrixSeamlessMode,
            CitrixUseSso = CitrixUseSso,
            FtpPort = FtpPort,
            FtpUsername = string.IsNullOrWhiteSpace(FtpUsername) ? null : FtpUsername,
            FtpPasswordEncrypted = string.IsNullOrEmpty(FtpPassword)
                ? ExistingFtpPasswordEncrypted
                : Heimdall.Core.Security.CredentialProtector.Protect(FtpPassword),
            FtpPassiveMode = FtpPassiveMode,
            FtpUseSsl = FtpUseSsl,
            VncPort = VncPort,
            VncPassword = string.IsNullOrEmpty(VncPassword)
                ? ExistingVncPasswordEncrypted
                : Heimdall.Core.Security.CredentialProtector.Protect(VncPassword),
            VncViewOnly = VncViewOnly,
            TelnetPort = IsTelnetConnection ? RemotePort : 23,
            TelnetUsername = string.IsNullOrWhiteSpace(TelnetUsername) ? null : TelnetUsername,
            TelnetPasswordEncrypted = string.IsNullOrEmpty(TelnetPassword)
                ? ExistingTelnetPasswordEncrypted
                : Heimdall.Core.Security.CredentialProtector.Protect(TelnetPassword),
            RdpUsername = string.IsNullOrWhiteSpace(RdpUsername) ? null : RdpUsername,
            RdpPasswordEncrypted = string.IsNullOrEmpty(RdpPassword)
                ? ExistingRdpPasswordEncrypted
                : Heimdall.Core.Security.CredentialProtector.Protect(RdpPassword),
            RdpMode = RdpMode,
            RdpUseGlobalDefaults = RdpUseGlobalDefaults,
            RdpAntiIdle = RdpAntiIdle,
            RdpRedirectClipboard = RedirectClipboard,
            RdpRedirectDrives = RedirectDrives,
            RdpRedirectPrinters = RedirectPrinters,
            RdpRedirectComPorts = RdpRedirectComPorts,
            RdpRedirectSmartCards = RdpRedirectSmartCards,
            RdpRedirectWebcam = RdpRedirectWebcam,
            RdpRedirectUsb = RdpRedirectUsb,
            RdpAudioMode = RdpAudioMode,
            RdpAudioCapture = RdpAudioCapture,
            RdpMultiMonitor = RdpMultiMonitor,
            RdpDynamicResolution = RdpDynamicResolution,
            RdpNla = RdpNla,
            RdpAspectRatio = RdpAspectRatio,
            RdpColorDepth = RdpColorDepth,
            RdpBitmapCaching = RdpBitmapCaching,
            RdpCompression = RdpCompression,
            RdpAutoReconnect = RdpAutoReconnect,
            RdpGateway = string.IsNullOrWhiteSpace(RdpGateway) ? null : RdpGateway,
            SshGatewayId = string.IsNullOrWhiteSpace(SelectedGatewayId) ? null : SelectedGatewayId,
            UseDirectConnection = DirectConnection,
            ProjectId = string.IsNullOrWhiteSpace(SelectedProjectId) ? null : SelectedProjectId,
            Tags = string.IsNullOrWhiteSpace(Tags) ? null : Tags,
            Environment = Environment == "None" ? null : Environment,
            MacAddress = string.IsNullOrWhiteSpace(MacAddress) ? null : MacAddress,
            IsFavorite = IsFavorite
        };
    }

    /// <summary>
    /// Creates a ViewModel pre-populated from an existing DTO (for edit mode).
    /// </summary>
    public static ServerDialogViewModel FromDto(ServerProfileDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var connectionType = string.IsNullOrWhiteSpace(dto.ConnectionType) ? "RDP" : dto.ConnectionType;
        var suggestedTunnelPort = GetSuggestedTunnelPort(connectionType);
        var storedLocalPort = dto.LocalPort <= 0 ? suggestedTunnelPort : dto.LocalPort;

        return new ServerDialogViewModel
        {
            IsEditMode = true,
            DisplayName = dto.DisplayName,
            RemoteServer = dto.RemoteServer,
            RemotePort = dto.RemotePort,
            LocalPort = storedLocalPort,
            UseAutomaticTunnelPort = dto.LocalPort <= 0 || dto.LocalPort == suggestedTunnelPort,
            Group = dto.Group ?? "",
            ConnectionType = connectionType,
            SshUsername = dto.SshUsername ?? "",
            SshPort = dto.SshPort,
            SshKeyPath = dto.SshKeyPath ?? "",
            SshCompression = dto.SshCompression,
            SshX11Forwarding = dto.SshX11Forwarding,
            SshAgentForwarding = dto.SshAgentForwarding,
            SshMode = dto.SshMode,
            LocalShellExecutable = dto.LocalShellExecutable ?? "powershell.exe",
            LocalShellArguments = dto.LocalShellArguments ?? "",
            LocalShellWorkingDirectory = dto.LocalShellWorkingDirectory ?? "",
            LocalShellElevated = dto.LocalShellElevated,
            ElevationMode = dto.ElevationMode,
            CitrixStoreFrontUrl = dto.CitrixStoreFrontUrl ?? "",
            CitrixAppName = dto.CitrixAppName ?? "",
            CitrixIcaFilePath = dto.CitrixIcaFilePath ?? "",
            CitrixSeamlessMode = dto.CitrixSeamlessMode,
            CitrixUseSso = dto.CitrixUseSso,
            FtpPort = dto.FtpPort > 0 ? dto.FtpPort : 21,
            FtpUsername = dto.FtpUsername ?? "",
            ExistingFtpPasswordEncrypted = dto.FtpPasswordEncrypted,
            FtpPassiveMode = dto.FtpPassiveMode,
            FtpUseSsl = dto.FtpUseSsl,
            VncPort = dto.VncPort > 0 ? dto.VncPort : 5900,
            VncViewOnly = dto.VncViewOnly,
            ExistingVncPasswordEncrypted = dto.VncPassword,
            TelnetUsername = dto.TelnetUsername ?? "",
            ExistingTelnetPasswordEncrypted = dto.TelnetPasswordEncrypted,
            RdpUsername = dto.RdpUsername ?? "",
            ExistingRdpPasswordEncrypted = dto.RdpPasswordEncrypted,
            ExistingSshPasswordEncrypted = dto.SshPasswordEncrypted,
            RdpMode = dto.RdpMode,
            RdpUseGlobalDefaults = dto.RdpUseGlobalDefaults,
            RdpAntiIdle = dto.RdpAntiIdle,
            RedirectClipboard = dto.RdpRedirectClipboard,
            RedirectDrives = dto.RdpRedirectDrives,
            RedirectPrinters = dto.RdpRedirectPrinters,
            RdpRedirectComPorts = dto.RdpRedirectComPorts,
            RdpRedirectSmartCards = dto.RdpRedirectSmartCards,
            RdpRedirectWebcam = dto.RdpRedirectWebcam,
            RdpRedirectUsb = dto.RdpRedirectUsb,
            RdpAudioMode = dto.RdpAudioMode,
            RdpAudioCapture = dto.RdpAudioCapture,
            RdpMultiMonitor = dto.RdpMultiMonitor,
            RdpDynamicResolution = dto.RdpDynamicResolution,
            RdpNla = dto.RdpNla,
            RdpAspectRatio = dto.RdpAspectRatio,
            RdpColorDepth = dto.RdpColorDepth,
            RdpBitmapCaching = dto.RdpBitmapCaching,
            RdpCompression = dto.RdpCompression,
            RdpAutoReconnect = dto.RdpAutoReconnect,
            RdpGateway = dto.RdpGateway ?? "",
            SelectedGatewayId = dto.SshGatewayId ?? "",
            DirectConnection = dto.UseDirectConnection,
            SelectedProjectId = dto.ProjectId ?? "",
            Tags = dto.Tags ?? "",
            MacAddress = dto.MacAddress ?? "",
            Environment = dto.Environment ?? "None",
            IsFavorite = dto.IsFavorite
        };
    }

    partial void OnConnectionTypeChanged(string value)
    {
        EndpointPort = GetDefaultEndpointPort(value);

        if (UseAutomaticTunnelPort)
        {
            LocalPort = GetSuggestedTunnelPort(value);
        }

        GatewayTestMessage = null;
        RaiseDerivedStateChanged();
    }

    partial void OnRemotePortChanged(int value)
    {
        RaisePortDerivedStateChanged();
    }

    partial void OnRemoteServerChanged(string value)
    {
        RaiseDerivedStateChanged();
    }

    partial void OnSshPortChanged(int value)
    {
        RaisePortDerivedStateChanged();
    }

    partial void OnLocalPortChanged(int value)
    {
        RaiseDerivedStateChanged();
    }

    partial void OnUseAutomaticTunnelPortChanged(bool value)
    {
        if (value)
        {
            LocalPort = GetSuggestedTunnelPort(ConnectionType);
        }

        RaiseDerivedStateChanged();
    }

    partial void OnSelectedGatewayIdChanged(string value)
    {
        GatewayTestMessage = null;
        RaiseDerivedStateChanged();
    }

    partial void OnDirectConnectionChanged(bool value)
    {
        GatewayTestMessage = null;
        RaiseDerivedStateChanged();
    }

    partial void OnAvailableGatewaysChanged(ObservableCollection<GatewayOption> value)
    {
        RaiseDerivedStateChanged();
    }

    private GatewayOption? SelectedGateway =>
        AvailableGateways.FirstOrDefault(gateway =>
            string.Equals(gateway.Id, SelectedGatewayId, StringComparison.Ordinal));

    private static int GetDefaultEndpointPort(string connectionType)
    {
        if (string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase))
            return DefaultRdpPort;
        if (string.Equals(connectionType, "VNC", StringComparison.OrdinalIgnoreCase))
            return 5900;
        if (string.Equals(connectionType, "FTP", StringComparison.OrdinalIgnoreCase))
            return 21;
        if (string.Equals(connectionType, "Telnet", StringComparison.OrdinalIgnoreCase))
            return DefaultTelnetPort;
        return DefaultSshPort;
    }

    private static int GetSuggestedTunnelPort(string connectionType)
    {
        return string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase)
            ? DefaultRdpTunnelPort
            : DefaultSshTunnelPort;
    }

    private string GetDestinationHost()
    {
        return string.IsNullOrWhiteSpace(RemoteServer) ? "destination server" : RemoteServer;
    }

    private void RaisePortDerivedStateChanged()
    {
        OnPropertyChanged(nameof(EndpointPort));
        OnPropertyChanged(nameof(DestinationNodeCaption));
        OnPropertyChanged(nameof(TunnelSummary));
    }

    private void RaiseDerivedStateChanged()
    {
        OnPropertyChanged(nameof(IsRdpConnection));
        OnPropertyChanged(nameof(IsSshConnection));
        OnPropertyChanged(nameof(IsSftpConnection));
        OnPropertyChanged(nameof(IsFtpConnection));
        OnPropertyChanged(nameof(IsCitrixConnection));
        OnPropertyChanged(nameof(IsTelnetConnection));
        OnPropertyChanged(nameof(IsSshFamilyConnection));
        OnPropertyChanged(nameof(UsesGateway));
        OnPropertyChanged(nameof(CanSelectGateway));
        OnPropertyChanged(nameof(CanEditTunnelPort));
        OnPropertyChanged(nameof(EndpointPort));
        OnPropertyChanged(nameof(EndpointPortLabel));
        OnPropertyChanged(nameof(EndpointPortHelpText));
        OnPropertyChanged(nameof(LocalTunnelPortDisplay));
        OnPropertyChanged(nameof(ConnectionPathHeadline));
        OnPropertyChanged(nameof(GatewayExplanation));
        OnPropertyChanged(nameof(GatewayRouteText));
        OnPropertyChanged(nameof(SelectedGatewayTitle));
        OnPropertyChanged(nameof(SelectedGatewayEndpoint));
        OnPropertyChanged(nameof(SessionKindLabel));
        OnPropertyChanged(nameof(SessionModeSummary));
        OnPropertyChanged(nameof(TunnelSummary));
        OnPropertyChanged(nameof(ClientNodeCaption));
        OnPropertyChanged(nameof(GatewayNodeCaption));
        OnPropertyChanged(nameof(DestinationNodeCaption));
        OnPropertyChanged(nameof(ClientToGatewayLabel));
        OnPropertyChanged(nameof(GatewayToServerLabel));
    }

    private static readonly Dictionary<string, string> ValidationKeyMap = new(StringComparer.Ordinal)
    {
        ["Display name is required."] = "ValidationDisplayNameRequired",
        ["Display name cannot be empty."] = "ValidationDisplayNameEmpty",
        ["Port must be between 1 and 65535."] = "ValidationPortRange",
        ["Local tunnel port must be between 1 and 65535."] = "ValidationLocalPortRange",
        ["SSH port must be between 1 and 65535."] = "ValidationSshPortRange",
        ["Audio mode must be 0 (disabled), 1 (local), or 2 (remote)."] = "ValidationAudioMode",
        ["Color depth must be between 8 and 32."] = "ValidationColorDepth",
        ["FTP port must be between 1 and 65535."] = "ValidationFtpPortRange",
    };

    private string? GetFirstError()
    {
        var firstProperty = GetErrors()
            .OfType<System.ComponentModel.DataAnnotations.ValidationResult>()
            .FirstOrDefault();

        var message = firstProperty?.ErrorMessage;
        if (message is not null && Localizer is not null
            && ValidationKeyMap.TryGetValue(message, out var key))
        {
            return Localizer[key];
        }

        return message;
    }
}

/// <summary>
/// Represents an SSH gateway option in the dialog's gateway dropdown.
/// Additional metadata is carried so the UX can explain the route.
/// </summary>
public record GatewayOption(
    string Id,
    string DisplayText,
    string Name = "",
    string Host = "",
    int Port = 22,
    string RouteText = "")
{
    public string EffectiveName => string.IsNullOrWhiteSpace(Name) ? DisplayText : Name;

    public string EndpointText => string.IsNullOrWhiteSpace(Host)
        ? DisplayText
        : string.Format(CultureInfo.InvariantCulture, "{0}:{1}", Host, Port);

    public string EffectiveRouteText => string.IsNullOrWhiteSpace(RouteText) ? DisplayText : RouteText;
}

/// <summary>
/// Represents a project option in the server dialog's project dropdown.
/// </summary>
public record ProjectOption(string Id, string Name, string Color);

/// <summary>
/// Immutable result returned by the server dialog on close.
/// </summary>
public record ServerDialogResult(ServerProfileDto Server, bool Saved);
