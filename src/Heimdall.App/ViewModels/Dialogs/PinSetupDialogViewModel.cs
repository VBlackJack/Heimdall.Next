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
using CommunityToolkit.Mvvm.Input;
using Heimdall.Core.Security;

namespace Heimdall.App.ViewModels.Dialogs;

/// <summary>
/// Result kind produced by the PIN setup dialog.
/// </summary>
public enum PinSetupOutcome
{
    /// <summary>A PIN was set or changed.</summary>
    Set,

    /// <summary>The existing PIN was removed.</summary>
    Removed
}

/// <summary>
/// Validation error produced by the PIN setup dialog.
/// </summary>
public enum PinSetupError
{
    /// <summary>The current PIN is missing or incorrect.</summary>
    WrongCurrentPin,

    /// <summary>The new PIN is shorter than the minimum length.</summary>
    PinTooShort,

    /// <summary>The new PIN exceeds the maximum length.</summary>
    PinTooLong,

    /// <summary>The new PIN contains characters other than ASCII digits.</summary>
    PinInvalidChars,

    /// <summary>The new PIN and confirmation PIN do not match.</summary>
    ConfirmMismatch
}

/// <summary>
/// Input collected by the PIN setup dialog.
/// </summary>
/// <param name="CurrentPin">Current PIN when changing an existing PIN, or null for first-time setup.</param>
/// <param name="NewPin">New PIN to set.</param>
/// <param name="ConfirmPin">Confirmation for the new PIN.</param>
public sealed record PinSetupInput(string? CurrentPin, string NewPin, string ConfirmPin);

/// <summary>
/// Result produced by a completed PIN setup operation.
/// </summary>
/// <param name="Outcome">Completed operation kind.</param>
/// <param name="Hash">New stored PIN hash, or null when the PIN was removed.</param>
/// <param name="Salt">New stored PIN salt, or null when the PIN was removed.</param>
public sealed record PinSetupResult(PinSetupOutcome Outcome, string? Hash, string? Salt);

/// <summary>
/// ViewModel for setting, changing, or removing the application PIN.
/// </summary>
/// <remarks>
/// This ViewModel intentionally exposes error states as enum values and does not depend on
/// localization; the view maps those values to localized text.
/// </remarks>
public sealed partial class PinSetupDialogViewModel : ObservableObject
{
    private readonly PinManager _pinManager;
    private readonly string? _storedHash;
    private readonly string? _storedSalt;

    [ObservableProperty]
    private PinSetupError? _error;

    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>
    /// Gets the completed setup result, or null until the operation succeeds.
    /// </summary>
    public PinSetupResult? Result { get; private set; }

    /// <summary>
    /// Gets whether a PIN is currently configured.
    /// </summary>
    public bool IsPinSet => _storedHash is not null && _storedSalt is not null;

    /// <summary>
    /// Initializes a new instance of the <see cref="PinSetupDialogViewModel"/> class.
    /// </summary>
    /// <param name="pinManager">PIN hashing and verification service.</param>
    /// <param name="storedHash">Stored PIN hash when a PIN already exists.</param>
    /// <param name="storedSalt">Stored PIN salt when a PIN already exists.</param>
    public PinSetupDialogViewModel(PinManager pinManager, string? storedHash, string? storedSalt)
    {
        _pinManager = pinManager;
        _storedHash = storedHash;
        _storedSalt = storedSalt;
    }

    /// <summary>
    /// Validates and stores the requested PIN setup or change operation.
    /// </summary>
    /// <param name="input">PIN setup input collected by the view.</param>
    [RelayCommand]
    private void Submit(PinSetupInput input)
    {
        Error = null;

        if (IsPinSet
            && (string.IsNullOrEmpty(input.CurrentPin)
                || !_pinManager.Verify(input.CurrentPin, _storedHash!, _storedSalt!)))
        {
            Error = PinSetupError.WrongCurrentPin;
            return;
        }

        PinValidationResult format = _pinManager.ValidateFormat(input.NewPin);
        if (!format.IsValid)
        {
            Error = MapValidationError(format.Error);
            return;
        }

        if (!string.Equals(input.NewPin, input.ConfirmPin, StringComparison.Ordinal))
        {
            Error = PinSetupError.ConfirmMismatch;
            return;
        }

        string salt = _pinManager.GenerateSalt();
        string hash = _pinManager.Hash(input.NewPin, salt);

        Result = new PinSetupResult(PinSetupOutcome.Set, hash, salt);
        Error = null;
        IsCompleted = true;
    }

    /// <summary>
    /// Validates the current PIN and removes the stored PIN.
    /// </summary>
    /// <param name="currentPin">Current PIN entered by the user.</param>
    [RelayCommand]
    private void Remove(string currentPin)
    {
        Error = null;

        if (!IsPinSet)
        {
            return;
        }

        if (string.IsNullOrEmpty(currentPin)
            || !_pinManager.Verify(currentPin, _storedHash!, _storedSalt!))
        {
            Error = PinSetupError.WrongCurrentPin;
            return;
        }

        Result = new PinSetupResult(PinSetupOutcome.Removed, null, null);
        Error = null;
        IsCompleted = true;
    }

    private static PinSetupError MapValidationError(PinValidationError? error)
    {
        return error switch
        {
            PinValidationError.TooShort => PinSetupError.PinTooShort,
            PinValidationError.TooLong => PinSetupError.PinTooLong,
            PinValidationError.InvalidChars => PinSetupError.PinInvalidChars,
            _ => PinSetupError.PinInvalidChars
        };
    }
}
