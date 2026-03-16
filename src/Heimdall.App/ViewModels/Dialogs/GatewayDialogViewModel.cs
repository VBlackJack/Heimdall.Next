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
/// ViewModel for the SSH gateway add/edit dialog.
/// Supports chained gateways via parent gateway selection.
/// </summary>
public partial class GatewayDialogViewModel : ObservableValidator
{
    // --- Dialog state ---

    [ObservableProperty]
    private string _dialogTitle = "";

    [ObservableProperty]
    private bool _isEditMode;

    // --- Gateway fields ---

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Gateway name is required.")]
    [MinLength(1, ErrorMessage = "Gateway name cannot be empty.")]
    private string _name = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Host address is required.")]
    [MinLength(1, ErrorMessage = "Host address cannot be empty.")]
    private string _host = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    private int _port = 22;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Username is required.")]
    [MinLength(1, ErrorMessage = "Username cannot be empty.")]
    private string _user = "";

    [ObservableProperty]
    private string _keyPath = "";

    [ObservableProperty]
    private string _password = "";

    /// <summary>
    /// Optional parent gateway for chained SSH tunnels.
    /// Empty string means no parent (direct connection).
    /// </summary>
    [ObservableProperty]
    private string _selectedParentGatewayId = "";

    /// <summary>
    /// Read-only display of the SSH host key fingerprint (populated after first connection).
    /// </summary>
    [ObservableProperty]
    private string _hostKeyFingerprint = "";

    /// <summary>
    /// Available parent gateways for chaining. Excludes the current gateway to prevent cycles.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GatewayOption> _availableParents = [];

    // --- Validation ---

    [ObservableProperty]
    private string? _validationError;

    /// <summary>
    /// Triggers full validation of all annotated properties.
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
    private void BrowseKey()
    {
        // Intentionally empty: View binds via interaction trigger to open file dialog.
    }

    /// <summary>
    /// Maps the current ViewModel state to a flat DTO for persistence.
    /// </summary>
    public SshGatewayDto ToDto()
    {
        return new SshGatewayDto
        {
            Name = Name,
            Host = Host,
            Port = Port,
            User = User,
            KeyPath = string.IsNullOrWhiteSpace(KeyPath) ? null : KeyPath,
            ParentGatewayId = string.IsNullOrWhiteSpace(SelectedParentGatewayId)
                ? null
                : SelectedParentGatewayId,
            HostKeyFingerprint = string.IsNullOrWhiteSpace(HostKeyFingerprint)
                ? null
                : HostKeyFingerprint
        };
    }

    /// <summary>
    /// Creates a ViewModel pre-populated from an existing DTO (for edit mode).
    /// </summary>
    /// <param name="dto">The gateway DTO to load values from.</param>
    /// <returns>A populated GatewayDialogViewModel in edit mode.</returns>
    public static GatewayDialogViewModel FromDto(SshGatewayDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new GatewayDialogViewModel
        {
            IsEditMode = true,
            Name = dto.Name,
            Host = dto.Host,
            Port = dto.Port,
            User = dto.User,
            KeyPath = dto.KeyPath ?? "",
            SelectedParentGatewayId = dto.ParentGatewayId ?? "",
            HostKeyFingerprint = dto.HostKeyFingerprint ?? ""
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
/// Immutable result returned by the gateway dialog on close.
/// </summary>
/// <param name="Gateway">The gateway DTO with user-entered values.</param>
/// <param name="Saved">True if the user clicked Save, false if cancelled.</param>
public record GatewayDialogResult(SshGatewayDto Gateway, bool Saved);
