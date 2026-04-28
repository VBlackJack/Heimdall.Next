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
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Dialogs;

/// <summary>
/// ViewModel for creating an ad-hoc SSH local-forward tunnel from the
/// Tunnel Manager.
/// </summary>
public sealed partial class NewTunnelDialogViewModel : ObservableObject
{
    private readonly LocalizationManager _localizer;
    private readonly HashSet<int> _activeLocalPorts;

    public NewTunnelDialogViewModel(
        IReadOnlyList<SshGatewayDto> gateways,
        LocalizationManager localizer,
        IReadOnlySet<int>? activeLocalPorts = null)
    {
        ArgumentNullException.ThrowIfNull(gateways);
        ArgumentNullException.ThrowIfNull(localizer);

        _localizer = localizer;
        _activeLocalPorts = activeLocalPorts is null
            ? []
            : new HashSet<int>(activeLocalPorts);

        Gateways = gateways;
        SelectedGateway = gateways.FirstOrDefault();
        HasGateways = gateways.Count > 0;
        HasNoGateways = !HasGateways;
        RefreshValidation();
    }

    public IReadOnlyList<SshGatewayDto> Gateways { get; }

    public bool HasGateways { get; }

    public bool HasNoGateways { get; }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private SshGatewayDto? _selectedGateway;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _remoteHost = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private int _remotePort = 22;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private int _localPort = 9090;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string? _validationMessage;

    public bool? Decision { get; private set; }

    public event EventHandler? CloseRequested;

    partial void OnSelectedGatewayChanged(SshGatewayDto? value) => RefreshValidation();

    partial void OnRemoteHostChanged(string value) => RefreshValidation();

    partial void OnRemotePortChanged(int value) => RefreshValidation();

    partial void OnLocalPortChanged(int value) => RefreshValidation();

    partial void OnValidationMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasValidationMessage));
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (!ValidateInputs())
        {
            return;
        }

        Decision = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Decision = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanConfirm()
    {
        return HasGateways
            && SelectedGateway is not null
            && GetValidationMessage() is null;
    }

    private bool ValidateInputs()
    {
        var message = GetValidationMessage();
        ValidationMessage = message;
        return message is null;
    }

    private void RefreshValidation()
    {
        ValidationMessage = HasGateways ? GetValidationMessage() : null;
    }

    private string? GetValidationMessage()
    {
        if (!HasGateways)
        {
            return null;
        }

        if (SelectedGateway is null)
        {
            return _localizer["NewTunnelValidationGateway"];
        }

        if (string.IsNullOrWhiteSpace(RemoteHost))
        {
            return _localizer["NewTunnelValidationRemoteHost"];
        }

        if (RemotePort is < 1 or > 65535)
        {
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _localizer["NewTunnelValidationRemotePort"],
                1,
                65535);
        }

        if (LocalPort is < 1024 or > 65535)
        {
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _localizer["NewTunnelValidationLocalPort"],
                1024,
                65535);
        }

        if (_activeLocalPorts.Contains(LocalPort))
        {
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _localizer["NewTunnelValidationLocalPortInUse"],
                LocalPort);
        }

        return null;
    }
}
