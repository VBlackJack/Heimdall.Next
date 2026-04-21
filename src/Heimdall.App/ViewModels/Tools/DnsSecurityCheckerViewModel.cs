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
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Security;

namespace Heimdall.App.ViewModels.Tools;

/// <summary>
/// Localized display projection for a single DNS security check.
/// </summary>
public sealed record DnsCheckDisplayItem(
    DnsCheckKind Kind,
    DnsCheckStatus Status,
    string Name,
    string Value,
    string Detail)
{
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}

/// <summary>
/// ViewModel for the DNS security checker tool.
/// </summary>
public sealed partial class DnsSecurityCheckerViewModel : ObservableObject, IDisposable
{
    private readonly IDnsSecurityService _service;
    private readonly ObservableCollection<DnsCheckDisplayItem> _checkResults = [];
    private CancellationTokenSource? _cts;
    private LocalizationManager? _localizer;
    private bool _disposed;
    private bool _userCancelled;
    private DnsSecurityReport? _lastReport;
    private long _lastElapsedMs;
    private string? _lastErrorKey;
    private object[] _lastErrorArgs = [];

    [ObservableProperty] private string _input = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private DnsSummaryStatus _summaryStatus = DnsSummaryStatus.Bad;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private string _reportText = string.Empty;
    [ObservableProperty] private bool _isHelpVisible;
    [ObservableProperty] private string _helpText = string.Empty;

    public DnsSecurityCheckerViewModel(IDnsSecurityService? service = null)
    {
        _service = service ?? new DnsSecurityService();
    }

    /// <summary>
    /// Gets the localized check results.
    /// </summary>
    public ObservableCollection<DnsCheckDisplayItem> CheckResults => _checkResults;

    public void Initialize(LocalizationManager? localizer) => UpdateLocalizer(localizer);

    public void SetGateway(SshGatewayDto? gateway) => _service.SetGateway(gateway);

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

        ReprojectResults();
        RefreshLocalizedMessages();
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ResetState();

        var domain = DnsSecurityEvaluationEngine.NormalizeDomainInput(Input);
        if (string.IsNullOrWhiteSpace(domain))
        {
            _lastErrorKey = "ToolValidationHostRequired";
            _lastErrorArgs = [];
            ShowError = true;
            ErrorText = L("ToolValidationHostRequired");
            return;
        }

        if (!InputValidator.ValidateDomain(domain))
        {
            _lastErrorKey = "ErrorInvalidDomain";
            _lastErrorArgs = [];
            ShowError = true;
            ErrorText = L("ErrorInvalidDomain");
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(TimeSpan.FromSeconds(30));

        _userCancelled = false;
        IsBusy = true;
        StatusText = L("ToolTunnelConnecting");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var results = await _service.RunAllChecksAsync(domain, _cts.Token);
            if (_cts.Token.IsCancellationRequested)
            {
                return;
            }

            stopwatch.Stop();

            _lastReport = DnsSecurityEvaluationEngine.BuildReport(domain, results);
            _lastElapsedMs = stopwatch.ElapsedMilliseconds;
            _lastErrorKey = null;
            _lastErrorArgs = [];

            SummaryStatus = _lastReport.Summary;
            HasResults = true;
            ReprojectResults();
            StatusText = string.Format(L("ToolDnsStatusComplete"), _lastElapsedMs);
        }
        catch (OperationCanceledException)
        {
            if (_userCancelled)
            {
                StatusText = string.Empty;
                return;
            }

            _lastErrorKey = "ToolDnsErrorTimeout";
            _lastErrorArgs = [];
            ShowError = true;
            ErrorText = L("ToolDnsErrorTimeout");
        }
        catch (Exception ex)
        {
            _lastErrorKey = "ToolDnsErrorLookupFailed";
            _lastErrorArgs = [ex.Message];
            ShowError = true;
            ErrorText = string.Format(L("ToolDnsErrorLookupFailed"), ex.Message);
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

    [RelayCommand(CanExecute = nameof(CanCopyReport))]
    private void CopyReport()
    {
        if (string.IsNullOrWhiteSpace(ReportText))
        {
            return;
        }

        try
        {
            Clipboard.SetText(ReportText);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard locked by another process.
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

    private bool CanCheck() => !IsBusy;

    private bool CanCancel() => IsBusy;

    private bool CanCopyReport() => !string.IsNullOrWhiteSpace(ReportText);

    partial void OnIsBusyChanged(bool value)
    {
        CheckCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnReportTextChanged(string value)
    {
        CopyReportCommand.NotifyCanExecuteChanged();
    }

    private void OnLocaleChanged(string _)
    {
        ReprojectResults();
        RefreshLocalizedMessages();
    }

    private void ResetState()
    {
        ShowError = false;
        ErrorText = string.Empty;
        StatusText = string.Empty;
        HasResults = false;
        SummaryStatus = DnsSummaryStatus.Bad;
        SummaryText = string.Empty;
        ReportText = string.Empty;
        _lastReport = null;
        _lastElapsedMs = 0;
        _lastErrorKey = null;
        _lastErrorArgs = [];
        _checkResults.Clear();
    }

    private void ReprojectResults()
    {
        _checkResults.Clear();

        if (_lastReport is null)
        {
            ReportText = string.Empty;
            SummaryText = string.Empty;
            return;
        }

        foreach (var result in _lastReport.Results)
        {
            _checkResults.Add(new DnsCheckDisplayItem(
                Kind: result.Kind,
                Status: result.Status,
                Name: L(DnsSecurityEvaluationEngine.KindToDisplayKey(result.Kind)),
                Value: string.IsNullOrEmpty(result.RawRecord) ? L("ToolDnsSecNoRecord") : result.RawRecord,
                Detail: BuildDetail(result)));
        }

        SummaryStatus = _lastReport.Summary;
        SummaryText = string.Format(L("ToolDnsSecSummary"), _lastReport.PassCount, _lastReport.Total);
        ReportText = DnsSecurityEvaluationEngine.BuildReportText(_lastReport, L);
    }

    private string BuildDetail(DnsCheckResult result)
    {
        if (string.IsNullOrEmpty(result.DetailKey))
        {
            return string.Empty;
        }

        var template = L(result.DetailKey);
        return result.DetailArgs.Count == 0
            ? template
            : string.Format(template, result.DetailArgs.Cast<object>().ToArray());
    }

    private void RefreshLocalizedMessages()
    {
        HelpText = L("ToolHelpDNSSEC").Replace("\\n", "\n", StringComparison.Ordinal);

        if (ShowError && !string.IsNullOrWhiteSpace(_lastErrorKey))
        {
            ErrorText = _lastErrorArgs.Length > 0
                ? string.Format(L(_lastErrorKey), _lastErrorArgs)
                : L(_lastErrorKey);
        }

        if (IsBusy)
        {
            StatusText = L("ToolTunnelConnecting");
            return;
        }

        if (HasResults && _lastElapsedMs > 0)
        {
            StatusText = string.Format(L("ToolDnsStatusComplete"), _lastElapsedMs);
        }
    }

    private string L(string key) => _localizer?[key] ?? key;
}
