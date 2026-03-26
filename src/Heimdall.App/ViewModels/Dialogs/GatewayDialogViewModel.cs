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
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the SSH gateway add/edit dialog.
/// Supports chained gateways via parent gateway selection.
/// </summary>
public partial class GatewayDialogViewModel : ObservableValidator
{
    /// <summary>
    /// Localizer for translating validation error messages. Set by the dialog service.
    /// </summary>
    public LocalizationManager? Localizer { get; set; }

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

    // Existing encrypted password (preserved on edit if user doesn't change it)
    public string? ExistingSshPasswordEncrypted { get; set; }

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

    // --- Dirty state tracking ---

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// Suppresses dirty tracking during initialization (e.g., FromDto).
    /// </summary>
    private bool _isInitializing;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_isInitializing) return;

        // Mark dirty when any user-editable property changes
        if (e.PropertyName is nameof(Name) or nameof(Host) or nameof(Port)
            or nameof(User) or nameof(KeyPath) or nameof(Password)
            or nameof(SelectedParentGatewayId))
        {
            IsDirty = true;
        }
    }

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

    // --- Live re-validation (only when errors are already showing) ---

    partial void OnNameChanged(string value)
    {
        if (ValidationError is not null)
        {
            ValidateProperty(value, nameof(Name));
            ValidationError = HasErrors ? GetFirstError() : null;
        }
    }

    partial void OnHostChanged(string value)
    {
        if (ValidationError is not null)
        {
            ValidateProperty(value, nameof(Host));
            ValidationError = HasErrors ? GetFirstError() : null;
        }
    }

    partial void OnPortChanged(int value)
    {
        if (ValidationError is not null)
        {
            ValidateProperty(value, nameof(Port));
            ValidationError = HasErrors ? GetFirstError() : null;
        }
    }

    partial void OnUserChanged(string value)
    {
        if (ValidationError is not null)
        {
            ValidateProperty(value, nameof(User));
            ValidationError = HasErrors ? GetFirstError() : null;
        }
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
            SshPasswordEncrypted = string.IsNullOrEmpty(Password)
                ? ExistingSshPasswordEncrypted
                : Heimdall.Core.Security.CredentialProtector.Protect(Password),
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

        var vm = new GatewayDialogViewModel { _isInitializing = true };
        vm.IsEditMode = true;
        vm.Name = dto.Name;
        vm.Host = dto.Host;
        vm.Port = dto.Port;
        vm.User = dto.User;
        vm.KeyPath = dto.KeyPath ?? "";
        vm.SelectedParentGatewayId = dto.ParentGatewayId ?? "";
        vm.HostKeyFingerprint = dto.HostKeyFingerprint ?? "";
        vm.ExistingSshPasswordEncrypted = dto.SshPasswordEncrypted;
        vm._isInitializing = false;
        return vm;
    }

    private static readonly Dictionary<string, string> ValidationKeyMap = new(StringComparer.Ordinal)
    {
        ["Gateway name is required."] = "ValidationGatewayNameRequired",
        ["Gateway name cannot be empty."] = "ValidationGatewayNameEmpty",
        ["Host address is required."] = "ValidationGatewayHostRequired",
        ["Host address cannot be empty."] = "ValidationGatewayHostEmpty",
        ["Port must be between 1 and 65535."] = "ValidationGatewayPortRange",
        ["Username is required."] = "ValidationUsernameRequired",
        ["Username cannot be empty."] = "ValidationUsernameEmpty",
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
/// Immutable result returned by the gateway dialog on close.
/// </summary>
/// <param name="Gateway">The gateway DTO with user-entered values.</param>
/// <param name="Saved">True if the user clicked Save, false if cancelled.</param>
public record GatewayDialogResult(SshGatewayDto Gateway, bool Saved);
