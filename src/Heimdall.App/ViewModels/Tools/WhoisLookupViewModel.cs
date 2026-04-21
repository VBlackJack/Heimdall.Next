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

using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Network;
using Heimdall.Core.Security;

namespace Heimdall.App.ViewModels.Tools;

/// <summary>
/// ViewModel for the WHOIS lookup tool. The lookup itself lives in
/// <see cref="IWhoisLookupService"/>; this class owns validation, busy state,
/// localization re-projection, and clipboard interaction.
/// </summary>
public sealed partial class WhoisLookupViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(10);

    private readonly IWhoisLookupService _service;
    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _userCancelled;

    private string _lastDomain = string.Empty;
    private long _lastElapsedMs;
    private string? _lastErrorKey;
    private object[] _lastErrorArgs = [];

    [ObservableProperty] private string _domain = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _resultHeader = string.Empty;
    [ObservableProperty] private string _results = string.Empty;
    [ObservableProperty] private bool _isHelpVisible;
    [ObservableProperty] private string _helpText = string.Empty;
    [ObservableProperty] private string _domainWatermark = string.Empty;
    [ObservableProperty] private string _emptyStateText = string.Empty;

    public WhoisLookupViewModel(IWhoisLookupService? service = null)
    {
        _service = service ?? new WhoisLookupService();
    }

    public void Initialize(LocalizationManager? localizer) => UpdateLocalizer(localizer);

    public void SetGateway(SshGatewayDto? gateway) => _service.SetGateway(gateway);

    public void SearchWith(string value)
    {
        Domain = value ?? string.Empty;
    }

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

    [RelayCommand(CanExecute = nameof(CanLookup))]
    private async Task LookupAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ResetTransientState();

        var domain = (Domain ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(domain))
        {
            SetError("ToolWhoisErrorDomainRequired");
            return;
        }

        if (!InputValidator.ValidateDomain(domain) && !IPAddress.TryParse(domain, out _))
        {
            SetError("ToolWhoisErrorInvalidDomain");
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(LookupTimeout);
        var token = _cts.Token;

        _userCancelled = false;
        IsBusy = true;
        StatusText = L("ToolWhoisStatusQuerying");

        try
        {
            var result = await _service.LookupAsync(new WhoisLookupRequest(domain), token);

            if (token.IsCancellationRequested && !result.Success && _userCancelled)
            {
                StatusText = string.Empty;
                return;
            }

            if (result.Success)
            {
                _lastDomain = domain;
                _lastElapsedMs = result.ElapsedMs;
                _lastErrorKey = null;
                _lastErrorArgs = [];

                HasResults = true;
                ResultHeader = string.Format(L("ToolWhoisResultHeader"), _lastDomain);
                Results = result.Output;
                StatusText = string.Format(L("ToolWhoisStatusComplete"), _lastElapsedMs);
                return;
            }

            _lastErrorKey = result.ErrorKey;
            _lastErrorArgs = result.ErrorArg is null ? [] : [result.ErrorArg];
            ShowError = true;
            ErrorText = FormatError(_lastErrorKey, _lastErrorArgs);
        }
        catch (OperationCanceledException)
        {
            if (_userCancelled)
            {
                StatusText = string.Empty;
                return;
            }

            _lastErrorKey = "ToolWhoisErrorTimeout";
            _lastErrorArgs = [];
            ShowError = true;
            ErrorText = L("ToolWhoisErrorTimeout");
        }
        catch (Exception ex)
        {
            _lastErrorKey = "ToolWhoisErrorFailed";
            _lastErrorArgs = [ex.Message];
            ShowError = true;
            ErrorText = string.Format(L("ToolWhoisErrorFailed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            _userCancelled = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _userCancelled = true;
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanCopyResults))]
    private void CopyResults()
    {
        if (string.IsNullOrEmpty(Results))
        {
            return;
        }

        try
        {
            Clipboard.SetText(Results);
        }
        catch (ExternalException)
        {
            // Clipboard can be temporarily locked by another process.
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

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        GC.SuppressFinalize(this);
    }

    private bool CanLookup() => !IsBusy;

    private bool CanCancel() => IsBusy;

    private bool CanCopyResults() => !string.IsNullOrEmpty(Results);

    partial void OnIsBusyChanged(bool value)
    {
        LookupCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnResultsChanged(string value)
    {
        CopyResultsCommand.NotifyCanExecuteChanged();
    }

    private void OnLocaleChanged(string _)
    {
        RefreshLocalizedMessages();
    }

    private void ResetTransientState()
    {
        ShowError = false;
        ErrorText = string.Empty;
        HasResults = false;
        ResultHeader = string.Empty;
        Results = string.Empty;
        StatusText = string.Empty;
        _lastDomain = string.Empty;
        _lastElapsedMs = 0;
        _lastErrorKey = null;
        _lastErrorArgs = [];
    }

    private void SetError(string errorKey)
    {
        _lastErrorKey = errorKey;
        _lastErrorArgs = [];
        ShowError = true;
        ErrorText = L(errorKey);
    }

    private void RefreshLocalizedMessages()
    {
        DomainWatermark = L("ToolWatermarkExampleDomainOrIp");
        EmptyStateText = L("ToolWhoisEmptyState");
        HelpText = L("ToolHelpWHOIS").Replace("\\n", "\n", StringComparison.Ordinal);

        if (ShowError && !string.IsNullOrWhiteSpace(_lastErrorKey))
        {
            ErrorText = FormatError(_lastErrorKey, _lastErrorArgs);
        }

        if (IsBusy)
        {
            StatusText = L("ToolWhoisStatusQuerying");
            return;
        }

        if (HasResults && !string.IsNullOrWhiteSpace(_lastDomain))
        {
            ResultHeader = string.Format(L("ToolWhoisResultHeader"), _lastDomain);
            StatusText = string.Format(L("ToolWhoisStatusComplete"), _lastElapsedMs);
        }
    }

    private string FormatError(string? key, object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var template = L(key);
        return args.Length == 0 ? template : string.Format(template, args);
    }

    private string L(string key) => _localizer?[key] ?? key;
}
