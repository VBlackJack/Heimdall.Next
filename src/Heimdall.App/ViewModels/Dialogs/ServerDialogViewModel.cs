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
    private int _defaultRdpTunnelPort = 33890;
    private int _defaultSshTunnelPort = 2222;

    /// <summary>
    /// Localizer for translating validation error messages. Set by the dialog service.
    /// </summary>
    public LocalizationManager? Localizer { get; set; }

    /// <summary>Application settings for configurable defaults.</summary>
    public AppSettings? Settings
    {
        set
        {
            if (value is null) return;
            _defaultRdpTunnelPort = value.DefaultRdpTunnelPort;
            _defaultSshTunnelPort = value.DefaultSshTunnelPort;
        }
    }

    private string L(string key) => Localizer?[key] ?? key;

    // --- Dialog state ---

    [ObservableProperty]
    private string _dialogTitle = "";

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private bool _isAdvancedMode;

    /// <summary>
    /// Whether the user has chosen a protocol (Step 1 complete).
    /// In edit mode this is always true. In add mode it starts false.
    /// </summary>
    [ObservableProperty]
    private bool _isProtocolSelected;

    /// <summary>
    /// Whether the protocol selector step should be displayed.
    /// True only in add mode before a protocol has been chosen.
    /// </summary>
    public bool ShowProtocolSelector => !IsEditMode && !IsProtocolSelected;

    /// <summary>
    /// Whether the form fields (Step 2) should be displayed.
    /// True after a protocol is selected, or always in edit mode.
    /// </summary>
    public bool ShowFormFields => IsEditMode || IsProtocolSelected;

    public bool IsLocalConnection => string.Equals(ConnectionType, "Local", StringComparison.OrdinalIgnoreCase);

    partial void OnIsProtocolSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowProtocolSelector));
        OnPropertyChanged(nameof(ShowFormFields));
    }

    partial void OnIsEditModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowProtocolSelector));
        OnPropertyChanged(nameof(ShowFormFields));
    }

    /// <summary>
    /// Selects a protocol and transitions from Step 1 to Step 2.
    /// </summary>
    [RelayCommand]
    private void SelectProtocol(string protocol)
    {
        ConnectionType = protocol;
        IsProtocolSelected = true;
    }

    /// <summary>
    /// Returns to the protocol selector (Step 1) from Step 2 in add mode.
    /// </summary>
    [RelayCommand]
    private void BackToProtocolSelector()
    {
        ClearValidationState();
        IsProtocolSelected = false;
    }

    // --- Identity ---

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Display name is required.")]
    [MinLength(1, ErrorMessage = "Display name cannot be empty.")]
    private string _displayName = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Server address is required.")]
    [MinLength(1, ErrorMessage = "Server address cannot be empty.")]
    private string _remoteServer = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    private int _remotePort = DefaultRdpPort;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "Local tunnel port must be between 1 and 65535.")]
    private int _localPort = 33890;

    [ObservableProperty]
    private bool _useAutomaticTunnelPort = true;

    [ObservableProperty]
    private int _socksProxyPort;

    public string SocksProxyDisplay => SocksProxyPort > 0
        ? $"127.0.0.1:{SocksProxyPort}"
        : L("TunnelingNoSocks");

    partial void OnSocksProxyPortChanged(int value)
        => OnPropertyChanged(nameof(SocksProxyDisplay));

    [ObservableProperty]
    private int _remoteBindPort;

    [ObservableProperty]
    private int _remoteLocalPort;

    public string RemoteForwardDisplay => RemoteBindPort > 0
        ? $"server:{RemoteBindPort} \u2192 local:{(RemoteLocalPort > 0 ? RemoteLocalPort : RemoteBindPort)}"
        : L("TunnelingNoRemoteFwd");

    partial void OnRemoteBindPortChanged(int value)
        => OnPropertyChanged(nameof(RemoteForwardDisplay));

    partial void OnRemoteLocalPortChanged(int value)
        => OnPropertyChanged(nameof(RemoteForwardDisplay));

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

    [ObservableProperty]
    private string _postConnectCommand = "";

    [ObservableProperty]
    private int _postConnectDelayMs = 800;

    /// <summary>
    /// Returns a context-aware label for the SSH password field:
    /// "Password" when no key is configured, "Passphrase" when a key is set.
    /// </summary>
    public string SshPasswordLabel => string.IsNullOrWhiteSpace(SshKeyPath)
        ? L("ServerDialogLabelPassword")
        : L("ServerDialogLabelPassphrase");

    /// <summary>
    /// Returns a context-aware hint explaining the SSH auth mode:
    /// password-centric hint when no key is configured, key-centric hint otherwise.
    /// </summary>
    public string SshAuthHint => string.IsNullOrWhiteSpace(SshKeyPath)
        ? L("ServerDialogSshAuthHintPassword")
        : L("ServerDialogSshAuthHintKey");

    partial void OnSshKeyPathChanged(string value)
    {
        OnPropertyChanged(nameof(SshPasswordLabel));
        OnPropertyChanged(nameof(SshAuthHint));
    }

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
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "VNC port must be between 1 and 65535.")]
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
    private int _rdpPerformanceFlags;

    [ObservableProperty]
    private bool _rdpDisableUdp;

    [ObservableProperty]
    private string _rdpGateway = "";

    // --- Gateway ---

    [ObservableProperty]
    private string _selectedGatewayId = "";

    [ObservableProperty]
    private bool _directConnection;

    [ObservableProperty]
    private ObservableCollection<GatewayOption> _availableGateways = [];

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

    // --- Dirty state tracking ---

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// Suppresses dirty tracking during initialization (e.g., FromDto).
    /// </summary>
    private bool _isInitializing;

    /// <summary>
    /// Properties excluded from dirty tracking (dialog state, validation, computed).
    /// </summary>
    private static readonly HashSet<string> DirtyExcludedProperties = new(StringComparer.Ordinal)
    {
        nameof(IsDirty),
        nameof(DialogTitle),
        nameof(IsEditMode),
        nameof(IsAdvancedMode),
        nameof(IsProtocolSelected),
        nameof(ValidationError),
        nameof(DisplayNameError),
        nameof(RemoteServerError),
        nameof(EndpointPortError),
        nameof(LocalPortError),
        nameof(AudioModeError),
        nameof(ColorDepthError),
        nameof(TunnelingTabErrorCount),
        nameof(OptionsTabErrorCount),
        nameof(FirstInvalidField),
        nameof(AvailableGateways),
        nameof(AvailableProjects),
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_isInitializing) return;
        if (e.PropertyName is null) return;
        if (DirtyExcludedProperties.Contains(e.PropertyName)) return;

        IsDirty = true;
    }

    // --- Validation ---

    [ObservableProperty]
    private string? _validationError;

    // Per-field inline validation errors (populated by Validate)
    [ObservableProperty]
    private string? _displayNameError;

    [ObservableProperty]
    private string? _remoteServerError;

    [ObservableProperty]
    private string? _endpointPortError;

    [ObservableProperty]
    private string? _localPortError;

    [ObservableProperty]
    private string? _audioModeError;

    [ObservableProperty]
    private string? _colorDepthError;

    // Tab error counts for badge display
    [ObservableProperty]
    private int _tunnelingTabErrorCount;

    [ObservableProperty]
    private int _optionsTabErrorCount;

    // Name of the first property with an error (for focus management by the View)
    [ObservableProperty]
    private string? _firstInvalidField;

    public bool HasTunnelingTabErrors => TunnelingTabErrorCount > 0;

    public bool HasOptionsTabErrors => OptionsTabErrorCount > 0;

    partial void OnTunnelingTabErrorCountChanged(int value) => OnPropertyChanged(nameof(HasTunnelingTabErrors));

    partial void OnOptionsTabErrorCountChanged(int value) => OnPropertyChanged(nameof(HasOptionsTabErrors));

    public bool IsRdpConnection => string.Equals(ConnectionType, "RDP", StringComparison.OrdinalIgnoreCase);

    public bool IsSshConnection => string.Equals(ConnectionType, "SSH", StringComparison.OrdinalIgnoreCase);

    public bool IsSftpConnection => string.Equals(ConnectionType, "SFTP", StringComparison.OrdinalIgnoreCase);

    public bool IsCitrixConnection => string.Equals(ConnectionType, "Citrix", StringComparison.OrdinalIgnoreCase);

    public bool IsFtpConnection => string.Equals(ConnectionType, "FTP", StringComparison.OrdinalIgnoreCase);

    public bool IsVncConnection => string.Equals(ConnectionType, "VNC", StringComparison.OrdinalIgnoreCase);

    public bool IsTelnetConnection => string.Equals(ConnectionType, "Telnet", StringComparison.OrdinalIgnoreCase);

    public bool IsSshFamilyConnection => IsSshConnection || IsSftpConnection;

    public bool RequiresNetworkEndpoint =>
        !string.Equals(ConnectionType, "Local", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(ConnectionType, "Citrix", StringComparison.OrdinalIgnoreCase);

    public bool UsesGateway => !DirectConnection && !string.IsNullOrWhiteSpace(SelectedGatewayId);

    public bool CanSelectGateway => !DirectConnection;

    public string GatewayComboHelpText => CanSelectGateway
        ? ""
        : L("ServerDialogGatewayDisabledHint");

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

    public string EndpointPortLabel => IsRdpConnection ? L("ServerDialogPortLabelRdp")
        : IsVncConnection ? L("ServerDialogPortLabelVnc")
        : IsFtpConnection ? L("ServerDialogPortLabelFtp")
        : IsTelnetConnection ? L("ServerDialogPortLabelTelnet")
        : L("ServerDialogPortLabelSsh");

    public string EndpointPortHelpText => IsRdpConnection
        ? L("ServerDialogPortHelpRdp")
        : IsVncConnection ? L("ServerDialogPortHelpVnc")
        : IsFtpConnection ? L("ServerDialogPortHelpFtp")
        : IsTelnetConnection ? L("ServerDialogPortHelpTelnet")
        : L("ServerDialogPortHelpSsh");

    public string LocalTunnelPortDisplay => UseAutomaticTunnelPort
        ? string.Format(CultureInfo.InvariantCulture, L("ServerDialogTunnelPortAuto"), LocalPort)
        : LocalPort.ToString(CultureInfo.InvariantCulture);

    public string ConnectionPathHeadline => UsesGateway
        ? L("ServerDialogPathHeadlineTunnel")
        : L("ServerDialogPathHeadlineDirect");

    public string GatewayExplanation => UsesGateway
        ? L("ServerDialogGatewayExplainTunnel")
        : L("ServerDialogGatewayExplainDirect");

    public string GatewayRouteText => SelectedGateway?.EffectiveRouteText ?? L("ServerDialogNoGatewaySelected");

    public string SelectedGatewayTitle => SelectedGateway?.EffectiveName ?? L("ServerDialogNoGatewaySelected");

    public string SelectedGatewayEndpoint => SelectedGateway?.EndpointText ?? L("ServerDialogNoSshGateway");

    public string SessionKindLabel => IsRdpConnection ? L("ServerDialogSessionRdp")
        : IsFtpConnection ? L("ServerDialogSessionFtp")
        : IsSftpConnection ? L("ServerDialogSessionSftp")
        : IsVncConnection ? L("ServerDialogSessionVnc")
        : IsTelnetConnection ? L("ServerDialogSessionTelnet")
        : IsCitrixConnection ? L("ServerDialogSessionCitrix")
        : IsLocalConnection ? L("ServerDialogSessionLocal")
        : L("ServerDialogSessionSsh");

    public string SessionModeSummary => IsRdpConnection ? L("ServerDialogModeSummaryRdp")
        : IsFtpConnection ? L("ServerDialogModeSummaryFtp")
        : IsSftpConnection ? L("ServerDialogModeSummarySftp")
        : IsVncConnection ? L("ServerDialogModeSummaryVnc")
        : IsTelnetConnection ? L("ServerDialogModeSummaryTelnet")
        : IsCitrixConnection ? L("ServerDialogModeSummaryCitrix")
        : IsLocalConnection ? L("ServerDialogModeSummaryLocal")
        : L("ServerDialogModeSummarySsh");

    public string TunnelSummary => UsesGateway
        ? string.Format(
            CultureInfo.InvariantCulture,
            L("ServerDialogTunnelSummaryFormat"),
            LocalTunnelPortDisplay,
            GetDestinationHost(),
            EndpointPort,
            SelectedGateway?.EffectiveName ?? L("ServerDialogTunnelSummaryFallbackGw"))
        : L("ServerDialogTunnelSummaryNone");

    public string ClientNodeCaption => UsesGateway
        ? string.Format(CultureInfo.InvariantCulture, L("ServerDialogClientNodeTunnel"), LocalTunnelPortDisplay)
        : L("ServerDialogClientNodeDirect");

    public string GatewayNodeCaption => UsesGateway
        ? string.Format(CultureInfo.InvariantCulture, "{0}", SelectedGateway?.EffectiveName ?? L("ServerDialogGatewayNodeDefault"))
        : L("ServerDialogGatewayNodeUnused");

    public string DestinationNodeCaption => string.IsNullOrWhiteSpace(RemoteServer)
        ? L("ServerDialogDestinationNode")
        : string.Format(CultureInfo.InvariantCulture, "{0}:{1}", RemoteServer, EndpointPort);

    public string ClientToGatewayLabel => UsesGateway ? L("ServerDialogLabelSshTunnel") : L("ServerDialogLabelDirectTransport");

    public string GatewayToServerLabel => SessionKindLabel;

    /// <summary>
    /// Triggers full validation of all annotated properties.
    /// Populates per-field errors, tab error counts, and first invalid field for focus.
    /// </summary>
    private void ClearValidationState()
    {
        ClearErrors();
        DisplayNameError = null;
        RemoteServerError = null;
        EndpointPortError = null;
        LocalPortError = null;
        AudioModeError = null;
        ColorDepthError = null;
        TunnelingTabErrorCount = 0;
        OptionsTabErrorCount = 0;
        FirstInvalidField = null;
        ValidationError = null;
    }

    private void RefreshValidationSummary()
    {
        ValidationError = DisplayNameError ?? RemoteServerError ?? EndpointPortError
            ?? LocalPortError ?? AudioModeError ?? ColorDepthError;
        TunnelingTabErrorCount = LocalPortError is not null ? 1 : 0;
        OptionsTabErrorCount = (AudioModeError is not null ? 1 : 0) + (ColorDepthError is not null ? 1 : 0);
    }

    [RelayCommand]
    private void Validate()
    {
        ValidateAllProperties();

        // Clear annotation errors for fields not relevant to this protocol,
        // so HasErrors stays consistent with the displayed validation state.
        if (!RequiresNetworkEndpoint)
        {
            ClearErrors(nameof(RemoteServer));
            ClearErrors(nameof(RemotePort));
        }
        if (!IsSshFamilyConnection) ClearErrors(nameof(SshPort));
        if (!IsFtpConnection) ClearErrors(nameof(FtpPort));
        if (!IsVncConnection) ClearErrors(nameof(VncPort));
        if (!IsRdpConnection)
        {
            ClearErrors(nameof(RdpAudioMode));
            ClearErrors(nameof(RdpColorDepth));
        }
        if (!UsesGateway) ClearErrors(nameof(LocalPort));

        // Per-field inline errors (localized, ConnectionType-aware)
        DisplayNameError = GetLocalizedFieldError(nameof(DisplayName));
        RemoteServerError = RequiresNetworkEndpoint ? GetLocalizedFieldError(nameof(RemoteServer)) : null;
        EndpointPortError = RequiresNetworkEndpoint ? GetEndpointPortError() : null;
        LocalPortError = UsesGateway ? GetLocalizedFieldError(nameof(LocalPort)) : null;

        // Custom tunnel port check
        if (LocalPortError is null && UsesGateway && !UseAutomaticTunnelPort && LocalPort <= 0)
        {
            LocalPortError = L("ValidationTunnelPortRequired");
        }

        // Options tab errors (RDP-specific)
        AudioModeError = IsRdpConnection ? GetLocalizedFieldError(nameof(RdpAudioMode)) : null;
        ColorDepthError = IsRdpConnection ? GetLocalizedFieldError(nameof(RdpColorDepth)) : null;

        // Tab error counts
        TunnelingTabErrorCount = LocalPortError is not null ? 1 : 0;
        OptionsTabErrorCount = (AudioModeError is not null ? 1 : 0) + (ColorDepthError is not null ? 1 : 0);

        // First invalid field for auto-focus
        FirstInvalidField = DisplayNameError is not null ? nameof(DisplayName)
            : RemoteServerError is not null ? nameof(RemoteServer)
            : EndpointPortError is not null ? "EndpointPort"
            : LocalPortError is not null ? nameof(LocalPort)
            : AudioModeError is not null ? nameof(RdpAudioMode)
            : ColorDepthError is not null ? nameof(RdpColorDepth)
            : null;

        // Aggregate summary
        ValidationError = DisplayNameError ?? RemoteServerError ?? EndpointPortError
            ?? LocalPortError ?? AudioModeError ?? ColorDepthError;
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
            SocksProxyPort = SocksProxyPort,
            RemoteBindPort = RemoteBindPort,
            RemoteLocalPort = RemoteLocalPort,
            SshMode = SshMode,
            PostConnectCommand = PostConnectCommand,
            PostConnectDelayMs = PostConnectDelayMs,
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
            RdpPerformanceFlags = RdpPerformanceFlags,
            RdpDisableUdp = RdpDisableUdp,
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
        var suggestedTunnelPort = string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase)
            ? 33890 : 2222;
        var storedLocalPort = dto.LocalPort <= 0 ? suggestedTunnelPort : dto.LocalPort;

        var vm = new ServerDialogViewModel { _isInitializing = true };
        vm.IsEditMode = true;
        vm.IsProtocolSelected = true;
        vm.DisplayName = dto.DisplayName;
        vm.RemoteServer = dto.RemoteServer;
        vm.RemotePort = string.Equals(connectionType, "Telnet", StringComparison.OrdinalIgnoreCase)
            ? (dto.TelnetPort > 0 ? dto.TelnetPort : DefaultTelnetPort)
            : dto.RemotePort;
        vm.LocalPort = storedLocalPort;
        vm.UseAutomaticTunnelPort = dto.LocalPort <= 0 || dto.LocalPort == suggestedTunnelPort;
        vm.Group = dto.Group ?? "";
        vm.ConnectionType = connectionType;
        vm.SshUsername = dto.SshUsername ?? "";
        vm.SshPort = dto.SshPort;
        vm.SshKeyPath = dto.SshKeyPath ?? "";
        vm.SshCompression = dto.SshCompression;
        vm.SshX11Forwarding = dto.SshX11Forwarding;
        vm.SshAgentForwarding = dto.SshAgentForwarding;
        vm.SocksProxyPort = dto.SocksProxyPort;
        vm.RemoteBindPort = dto.RemoteBindPort;
        vm.RemoteLocalPort = dto.RemoteLocalPort;
        vm.SshMode = dto.SshMode;
        vm.PostConnectCommand = dto.PostConnectCommand;
        vm.PostConnectDelayMs = dto.PostConnectDelayMs;
        vm.LocalShellExecutable = dto.LocalShellExecutable ?? "powershell.exe";
        vm.LocalShellArguments = dto.LocalShellArguments ?? "";
        vm.LocalShellWorkingDirectory = dto.LocalShellWorkingDirectory ?? "";
        vm.LocalShellElevated = dto.LocalShellElevated;
        vm.ElevationMode = dto.ElevationMode;
        vm.CitrixStoreFrontUrl = dto.CitrixStoreFrontUrl ?? "";
        vm.CitrixAppName = dto.CitrixAppName ?? "";
        vm.CitrixIcaFilePath = dto.CitrixIcaFilePath ?? "";
        vm.CitrixSeamlessMode = dto.CitrixSeamlessMode;
        vm.CitrixUseSso = dto.CitrixUseSso;
        vm.FtpPort = dto.FtpPort > 0 ? dto.FtpPort : 21;
        vm.FtpUsername = dto.FtpUsername ?? "";
        vm.ExistingFtpPasswordEncrypted = dto.FtpPasswordEncrypted;
        vm.FtpPassiveMode = dto.FtpPassiveMode;
        vm.FtpUseSsl = dto.FtpUseSsl;
        vm.VncPort = dto.VncPort > 0 ? dto.VncPort : 5900;
        vm.VncViewOnly = dto.VncViewOnly;
        vm.ExistingVncPasswordEncrypted = dto.VncPassword;
        vm.TelnetUsername = dto.TelnetUsername ?? "";
        vm.ExistingTelnetPasswordEncrypted = dto.TelnetPasswordEncrypted;
        vm.RdpUsername = dto.RdpUsername ?? "";
        vm.ExistingRdpPasswordEncrypted = dto.RdpPasswordEncrypted;
        vm.ExistingSshPasswordEncrypted = dto.SshPasswordEncrypted;
        vm.RdpMode = dto.RdpMode;
        vm.RdpUseGlobalDefaults = dto.RdpUseGlobalDefaults;
        vm.RdpAntiIdle = dto.RdpAntiIdle;
        vm.RedirectClipboard = dto.RdpRedirectClipboard;
        vm.RedirectDrives = dto.RdpRedirectDrives;
        vm.RedirectPrinters = dto.RdpRedirectPrinters;
        vm.RdpRedirectComPorts = dto.RdpRedirectComPorts;
        vm.RdpRedirectSmartCards = dto.RdpRedirectSmartCards;
        vm.RdpRedirectWebcam = dto.RdpRedirectWebcam;
        vm.RdpRedirectUsb = dto.RdpRedirectUsb;
        vm.RdpAudioMode = dto.RdpAudioMode;
        vm.RdpAudioCapture = dto.RdpAudioCapture;
        vm.RdpMultiMonitor = dto.RdpMultiMonitor;
        vm.RdpDynamicResolution = dto.RdpDynamicResolution;
        vm.RdpNla = dto.RdpNla;
        vm.RdpAspectRatio = dto.RdpAspectRatio;
        vm.RdpColorDepth = dto.RdpColorDepth;
        vm.RdpBitmapCaching = dto.RdpBitmapCaching;
        vm.RdpCompression = dto.RdpCompression;
        vm.RdpAutoReconnect = dto.RdpAutoReconnect;
        vm.RdpPerformanceFlags = dto.RdpPerformanceFlags;
        vm.RdpDisableUdp = dto.RdpDisableUdp;
        vm.RdpGateway = dto.RdpGateway ?? "";
        vm.SelectedGatewayId = dto.SshGatewayId ?? "";
        vm.DirectConnection = dto.UseDirectConnection;
        vm.SelectedProjectId = dto.ProjectId ?? "";
        vm.Tags = dto.Tags ?? "";
        vm.MacAddress = dto.MacAddress ?? "";
        vm.Environment = dto.Environment ?? "None";
        vm.IsFavorite = dto.IsFavorite;
        vm._isInitializing = false;
        return vm;
    }

    partial void OnConnectionTypeChanged(string value)
    {
        ClearValidationState();

        // In edit mode, preserve the loaded port (FromDto already set the correct value)
        if (!IsEditMode)
        {
            EndpointPort = GetDefaultEndpointPort(value);

            if (UseAutomaticTunnelPort)
            {
                LocalPort = GetSuggestedTunnelPort(value);
            }
        }

        RaiseDerivedStateChanged();
    }

    partial void OnDisplayNameChanged(string value)
    {
        if (DisplayNameError is not null)
        {
            ValidateProperty(value, nameof(DisplayName));
            DisplayNameError = GetLocalizedFieldError(nameof(DisplayName));
            RefreshValidationSummary();
        }
    }

    partial void OnRemoteServerChanged(string value)
    {
        if (RemoteServerError is not null)
        {
            ValidateProperty(value, nameof(RemoteServer));
            RemoteServerError = RequiresNetworkEndpoint ? GetLocalizedFieldError(nameof(RemoteServer)) : null;
            RefreshValidationSummary();
        }
        RaiseDerivedStateChanged();
    }

    partial void OnRemotePortChanged(int value)
    {
        if (EndpointPortError is not null)
        {
            ValidateProperty(value, nameof(RemotePort));
            EndpointPortError = RequiresNetworkEndpoint ? GetEndpointPortError() : null;
            RefreshValidationSummary();
        }
        RaisePortDerivedStateChanged();
    }

    partial void OnSshPortChanged(int value)
    {
        if (EndpointPortError is not null)
        {
            ValidateProperty(value, nameof(SshPort));
            EndpointPortError = IsSshFamilyConnection ? GetLocalizedFieldError(nameof(SshPort)) : null;
            RefreshValidationSummary();
        }
        RaisePortDerivedStateChanged();
    }

    partial void OnVncPortChanged(int value)
    {
        if (EndpointPortError is not null)
        {
            ValidateProperty(value, nameof(VncPort));
            EndpointPortError = IsVncConnection ? GetLocalizedFieldError(nameof(VncPort)) : null;
            RefreshValidationSummary();
        }
        RaisePortDerivedStateChanged();
    }

    partial void OnFtpPortChanged(int value)
    {
        if (EndpointPortError is not null)
        {
            ValidateProperty(value, nameof(FtpPort));
            EndpointPortError = IsFtpConnection ? GetLocalizedFieldError(nameof(FtpPort)) : null;
            RefreshValidationSummary();
        }
        RaisePortDerivedStateChanged();
    }

    partial void OnLocalPortChanged(int value)
    {
        if (LocalPortError is not null)
        {
            ValidateProperty(value, nameof(LocalPort));
            LocalPortError = UsesGateway ? GetLocalizedFieldError(nameof(LocalPort)) : null;
            RefreshValidationSummary();
        }
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
        if (LocalPortError is not null) { LocalPortError = null; RefreshValidationSummary(); }
        RaiseDerivedStateChanged();
    }

    partial void OnDirectConnectionChanged(bool value)
    {
        if (LocalPortError is not null) { LocalPortError = null; RefreshValidationSummary(); }
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

    private int GetSuggestedTunnelPort(string connectionType)
    {
        return string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase)
            ? _defaultRdpTunnelPort
            : _defaultSshTunnelPort;
    }

    private string GetDestinationHost()
    {
        return string.IsNullOrWhiteSpace(RemoteServer) ? L("ServerDialogDestinationNode") : RemoteServer;
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
        OnPropertyChanged(nameof(IsLocalConnection));
        OnPropertyChanged(nameof(IsSshFamilyConnection));
        OnPropertyChanged(nameof(UsesGateway));
        OnPropertyChanged(nameof(CanSelectGateway));
        OnPropertyChanged(nameof(GatewayComboHelpText));
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
        OnPropertyChanged(nameof(IsVncConnection));
        OnPropertyChanged(nameof(RequiresNetworkEndpoint));
    }

    private static readonly Dictionary<string, string> ValidationKeyMap = new(StringComparer.Ordinal)
    {
        ["Display name is required."] = "ValidationDisplayNameRequired",
        ["Display name cannot be empty."] = "ValidationDisplayNameEmpty",
        ["Server address is required."] = "ValidationServerAddressRequired",
        ["Server address cannot be empty."] = "ValidationServerAddressEmpty",
        ["Port must be between 1 and 65535."] = "ValidationPortRange",
        ["Local tunnel port must be between 1 and 65535."] = "ValidationLocalPortRange",
        ["SSH port must be between 1 and 65535."] = "ValidationSshPortRange",
        ["Audio mode must be 0 (disabled), 1 (local), or 2 (remote)."] = "ValidationAudioMode",
        ["Color depth must be between 8 and 32."] = "ValidationColorDepth",
        ["FTP port must be between 1 and 65535."] = "ValidationFtpPortRange",
        ["VNC port must be between 1 and 65535."] = "ValidationVncPortRange",
    };

    private string? GetEndpointPortError()
    {
        if (IsRdpConnection || IsTelnetConnection) return GetLocalizedFieldError(nameof(RemotePort));
        if (IsFtpConnection) return GetLocalizedFieldError(nameof(FtpPort));
        if (IsVncConnection) return GetLocalizedFieldError(nameof(VncPort));
        if (IsSshFamilyConnection) return GetLocalizedFieldError(nameof(SshPort));
        return null;
    }

    private string? GetLocalizedFieldError(string propertyName)
    {
        var error = GetErrors(propertyName)
            .OfType<System.ComponentModel.DataAnnotations.ValidationResult>()
            .FirstOrDefault();

        var message = error?.ErrorMessage;
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
public sealed record GatewayOption(
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
public sealed record ProjectOption(string Id, string Name, string Color);

/// <summary>
/// Immutable result returned by the server dialog on close.
/// </summary>
public sealed record ServerDialogResult(ServerProfileDto Server, bool Saved);
