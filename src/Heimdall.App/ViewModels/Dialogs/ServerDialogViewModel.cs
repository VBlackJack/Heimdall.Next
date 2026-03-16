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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.Core.Configuration;

namespace Heimdall.App.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the server add/edit dialog. Supports RDP, SSH, and SFTP
/// connection types with full validation via data annotations.
/// </summary>
public partial class ServerDialogViewModel : ObservableValidator
{
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
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Remote server address is required.")]
    [MinLength(1, ErrorMessage = "Remote server address cannot be empty.")]
    private string _remoteServer = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    private int _remotePort = 3389;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 65535, ErrorMessage = "Local port must be between 0 and 65535.")]
    private int _localPort;

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
    private int _sshPort = 22;

    [ObservableProperty]
    private string _sshKeyPath = "";

    [ObservableProperty]
    private string _sshPassword = "";

    [ObservableProperty]
    private bool _sshCompression;

    [ObservableProperty]
    private bool _sshX11Forwarding;

    [ObservableProperty]
    private bool _sshAgentForwarding;

    [ObservableProperty]
    private string _sshMode = "Embedded";

    // --- RDP settings ---

    [ObservableProperty]
    private string _rdpUsername = "";

    [ObservableProperty]
    private string _rdpPassword = "";

    [ObservableProperty]
    private string _rdpMode = "Embedded";

    [ObservableProperty]
    private bool _redirectClipboard = true;

    [ObservableProperty]
    private bool _redirectDrives;

    [ObservableProperty]
    private bool _redirectPrinters;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 2, ErrorMessage = "Audio mode must be 0 (disabled), 1 (local), or 2 (remote).")]
    private int _rdpAudioMode;

    [ObservableProperty]
    private bool _rdpMultiMonitor;

    [ObservableProperty]
    private bool _rdpNla = true;

    [ObservableProperty]
    private string _rdpAspectRatio = "Stretch";

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

    // --- Validation ---

    [ObservableProperty]
    private string? _validationError;

    /// <summary>
    /// Triggers full validation of all annotated properties.
    /// Sets <see cref="ValidationError"/> to the first error found, or null if valid.
    /// </summary>
    [RelayCommand]
    private void Validate()
    {
        ValidateAllProperties();
        ValidationError = HasErrors ? GetFirstError() : null;
    }

    /// <summary>
    /// Opens a file picker for SSH key selection.
    /// The View layer handles the actual file dialog; this command signals intent.
    /// </summary>
    [RelayCommand]
    private void BrowseSshKey()
    {
        // Intentionally empty: the View subscribes to this command's CanExecuteChanged
        // or binds via interaction trigger to open a file dialog and set SshKeyPath.
    }

    /// <summary>
    /// Maps the current ViewModel state to a flat DTO for persistence.
    /// </summary>
    public RdpServerDto ToDto()
    {
        return new RdpServerDto
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
            SshCompression = SshCompression,
            SshX11Forwarding = SshX11Forwarding,
            SshAgentForwarding = SshAgentForwarding,
            SshMode = SshMode,
            RdpUsername = string.IsNullOrWhiteSpace(RdpUsername) ? null : RdpUsername,
            RdpMode = RdpMode,
            RdpRedirectClipboard = RedirectClipboard,
            RdpRedirectDrives = RedirectDrives,
            RdpRedirectPrinters = RedirectPrinters,
            RdpAudioMode = RdpAudioMode,
            RdpMultiMonitor = RdpMultiMonitor,
            RdpNla = RdpNla,
            RdpAspectRatio = RdpAspectRatio,
            SshGatewayId = string.IsNullOrWhiteSpace(SelectedGatewayId) ? null : SelectedGatewayId,
            UseDirectConnection = DirectConnection,
            ProjectId = string.IsNullOrWhiteSpace(SelectedProjectId) ? null : SelectedProjectId,
            Tags = string.IsNullOrWhiteSpace(Tags) ? null : Tags,
            Environment = Environment == "None" ? null : Environment,
            IsFavorite = IsFavorite
        };
    }

    /// <summary>
    /// Creates a ViewModel pre-populated from an existing DTO (for edit mode).
    /// </summary>
    /// <param name="dto">The server DTO to load values from.</param>
    /// <returns>A populated ServerDialogViewModel in edit mode.</returns>
    public static ServerDialogViewModel FromDto(RdpServerDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new ServerDialogViewModel
        {
            IsEditMode = true,
            DisplayName = dto.DisplayName,
            RemoteServer = dto.RemoteServer,
            RemotePort = dto.RemotePort,
            LocalPort = dto.LocalPort,
            Group = dto.Group ?? "",
            ConnectionType = dto.ConnectionType,
            SshUsername = dto.SshUsername ?? "",
            SshPort = dto.SshPort,
            SshKeyPath = dto.SshKeyPath ?? "",
            SshCompression = dto.SshCompression,
            SshX11Forwarding = dto.SshX11Forwarding,
            SshAgentForwarding = dto.SshAgentForwarding,
            SshMode = dto.SshMode,
            RdpUsername = dto.RdpUsername ?? "",
            RdpMode = dto.RdpMode,
            RedirectClipboard = dto.RdpRedirectClipboard,
            RedirectDrives = dto.RdpRedirectDrives,
            RedirectPrinters = dto.RdpRedirectPrinters,
            RdpAudioMode = dto.RdpAudioMode,
            RdpMultiMonitor = dto.RdpMultiMonitor,
            RdpNla = dto.RdpNla,
            RdpAspectRatio = dto.RdpAspectRatio,
            SelectedGatewayId = dto.SshGatewayId ?? "",
            DirectConnection = dto.UseDirectConnection,
            SelectedProjectId = dto.ProjectId ?? "",
            Tags = dto.Tags ?? "",
            MacAddress = "",
            Environment = dto.Environment ?? "None",
            IsFavorite = dto.IsFavorite
        };
    }

    private string? GetFirstError()
    {
        var firstProperty = GetErrors()
            .OfType<System.ComponentModel.DataAnnotations.ValidationResult>()
            .FirstOrDefault();

        return firstProperty?.ErrorMessage;
    }
}

/// <summary>
/// Represents an SSH gateway option in the server dialog's gateway dropdown.
/// </summary>
/// <param name="Id">The gateway identifier.</param>
/// <param name="DisplayText">Human-readable gateway label (e.g., "user@host:port").</param>
public record GatewayOption(string Id, string DisplayText);

/// <summary>
/// Represents a project option in the server dialog's project dropdown.
/// </summary>
/// <param name="Id">The project identifier.</param>
/// <param name="Name">Human-readable project name.</param>
/// <param name="Color">Hex color code for visual identification.</param>
public record ProjectOption(string Id, string Name, string Color);

/// <summary>
/// Immutable result returned by the server dialog on close.
/// </summary>
/// <param name="Server">The server DTO with user-entered values.</param>
/// <param name="Saved">True if the user clicked Save, false if cancelled.</param>
public record ServerDialogResult(RdpServerDto Server, bool Saved);
