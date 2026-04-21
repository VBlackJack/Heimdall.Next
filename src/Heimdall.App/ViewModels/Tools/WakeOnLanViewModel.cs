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

using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Localization;
using Heimdall.Core.Network;

namespace Heimdall.App.ViewModels.Tools;

/// <summary>
/// ViewModel for the Wake-on-LAN tool. The send operation itself lives in
/// <see cref="IWakeOnLanService"/>; this class owns observable state,
/// localization re-projection, and history.
/// </summary>
public sealed partial class WakeOnLanViewModel : ObservableObject, IDisposable
{
    public const string DefaultBroadcastAddress = "255.255.255.255";
    public const int DefaultPort = 9;

    private readonly IWakeOnLanService _service;
    private LocalizationManager? _localizer;
    private bool _disposed;

    private string _lastMacAddress = string.Empty;
    private string _lastBroadcastAddress = DefaultBroadcastAddress;
    private int _lastPort = DefaultPort;
    private string? _lastErrorKey;
    private object[] _lastErrorArgs = [];

    [ObservableProperty] private string _macAddress = string.Empty;
    [ObservableProperty] private string _broadcastAddress = DefaultBroadcastAddress;
    [ObservableProperty] private string _port = DefaultPort.ToString(CultureInfo.InvariantCulture);
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private WakeOnLanStatusKind _statusKind;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _history = string.Empty;
    [ObservableProperty] private bool _isHelpVisible;
    [ObservableProperty] private string _helpText = string.Empty;

    public WakeOnLanViewModel(IWakeOnLanService? service = null)
    {
        _service = service ?? new WakeOnLanService();
    }

    public void Initialize(LocalizationManager? localizer) => UpdateLocalizer(localizer);

    public void UpdateLocalizer(LocalizationManager? localizer)
    {
        if (ReferenceEquals(_localizer, localizer))
        {
            return;
        }

        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
        }

        _localizer = localizer;
        if (_localizer is not null)
        {
            _localizer.LocaleChanged += OnLocaleChanged;
        }

        RefreshLocalizedMessages();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        var macAddress = (MacAddress ?? string.Empty).Trim();
        var broadcastAddress = (BroadcastAddress ?? string.Empty).Trim();
        var port = ParsePortOrDefault();

        if (!MacAddressParser.TryNormalize(macAddress, out _))
        {
            _lastErrorKey = "ToolWolErrorInvalidMac";
            _lastErrorArgs = [];
            StatusKind = WakeOnLanStatusKind.Error;
            StatusText = L("ToolWolErrorInvalidMac");
            return;
        }

        if (!IPAddress.TryParse(broadcastAddress, out _))
        {
            _lastErrorKey = "ToolWolErrorInvalidBroadcast";
            _lastErrorArgs = [];
            StatusKind = WakeOnLanStatusKind.Error;
            StatusText = L("ToolWolErrorInvalidBroadcast");
            return;
        }

        if (!string.Equals(Port, port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            Port = port.ToString(CultureInfo.InvariantCulture);
        }

        IsBusy = true;
        StatusKind = WakeOnLanStatusKind.Sending;
        StatusText = L("ToolWolStatusSending");
        _lastErrorKey = null;
        _lastErrorArgs = [];

        try
        {
            var result = await _service.SendAsync(new WakeOnLanRequest(macAddress, broadcastAddress, port), CancellationToken.None)
                .ConfigureAwait(true);

            if (result.Success)
            {
                _lastMacAddress = result.MacAddress;
                _lastBroadcastAddress = result.BroadcastAddress;
                _lastPort = result.Port;
                _lastErrorKey = null;
                _lastErrorArgs = [];

                StatusKind = WakeOnLanStatusKind.Sent;
                StatusText = string.Format(
                    CultureInfo.InvariantCulture,
                    L("ToolWolStatusSent"),
                    _lastMacAddress,
                    _lastBroadcastAddress,
                    _lastPort);

                var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                var entry = $"[{timestamp}] {_lastMacAddress} \u2192 {_lastBroadcastAddress}:{_lastPort.ToString(CultureInfo.InvariantCulture)}";
                History = string.IsNullOrWhiteSpace(History)
                    ? entry
                    : entry + Environment.NewLine + History;
            }
            else
            {
                _lastMacAddress = result.MacAddress;
                _lastBroadcastAddress = result.BroadcastAddress;
                _lastPort = result.Port;
                _lastErrorKey = result.ErrorKey;
                _lastErrorArgs = string.IsNullOrWhiteSpace(result.ErrorArg) ? [] : [result.ErrorArg!];
                StatusKind = WakeOnLanStatusKind.Error;
                StatusText = FormatError(_lastErrorKey, _lastErrorArgs);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleHelp()
    {
        IsHelpVisible = !IsHelpVisible;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
            _localizer = null;
        }

        GC.SuppressFinalize(this);
    }

    private bool CanSend() => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        SendCommand.NotifyCanExecuteChanged();
    }

    private void OnLocaleChanged(string _)
    {
        RefreshLocalizedMessages();
    }

    private void RefreshLocalizedMessages()
    {
        HelpText = L("ToolHelpWOL").Replace("\\n", "\n", StringComparison.Ordinal);

        switch (StatusKind)
        {
            case WakeOnLanStatusKind.Sending:
                StatusText = L("ToolWolStatusSending");
                break;
            case WakeOnLanStatusKind.Sent when !string.IsNullOrWhiteSpace(_lastMacAddress):
                StatusText = string.Format(
                    CultureInfo.InvariantCulture,
                    L("ToolWolStatusSent"),
                    _lastMacAddress,
                    _lastBroadcastAddress,
                    _lastPort);
                break;
            case WakeOnLanStatusKind.Error when !string.IsNullOrWhiteSpace(_lastErrorKey):
                StatusText = FormatError(_lastErrorKey, _lastErrorArgs);
                break;
        }
    }

    private int ParsePortOrDefault()
    {
        if (!int.TryParse((Port ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            return DefaultPort;
        }

        return port;
    }

    private string FormatError(string? key, object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var template = L(key);
        return args.Length == 0 ? template : string.Format(CultureInfo.InvariantCulture, template, args);
    }

    private string L(string key) => _localizer?[key] ?? key;
}
