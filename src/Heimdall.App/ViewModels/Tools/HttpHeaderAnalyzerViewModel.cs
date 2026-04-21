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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Network;

namespace Heimdall.App.ViewModels.Tools;

/// <summary>
/// ViewModel for the HTTP header analyzer.
/// </summary>
public sealed partial class HttpHeaderAnalyzerViewModel : ObservableObject, IDisposable
{
    private readonly IHttpHeaderService _service;
    private readonly ObservableCollection<HeaderCheckDisplayItem> _securityHeaders = [];
    private readonly ObservableCollection<HeaderCheckDisplayItem> _disclosureHeaders = [];
    private CancellationTokenSource? _cts;
    private LocalizationManager? _localizer;
    private bool _disposed;
    private bool _userCancelled;
    private IReadOnlyList<HeaderCheckResult> _lastSecurityResults = [];
    private IReadOnlyList<HeaderCheckResult> _lastDisclosureResults = [];
    private string _lastHost = string.Empty;
    private long _lastElapsedMs;
    private string? _lastErrorKey;
    private object[] _lastErrorArgs = [];

    [ObservableProperty] private string _urlInput = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(GradeText))] private HttpGrade _grade = HttpGrade.F;
    [ObservableProperty] private string _rawResponse = string.Empty;
    [ObservableProperty] private string _reportText = string.Empty;

    public HttpHeaderAnalyzerViewModel(IHttpHeaderService? service = null)
    {
        _service = service ?? new HttpHeaderService();
    }

    /// <summary>
    /// Gets the projected security header results for binding.
    /// </summary>
    public ObservableCollection<HeaderCheckDisplayItem> SecurityHeaders => _securityHeaders;

    /// <summary>
    /// Gets the projected disclosure header results for binding.
    /// </summary>
    public ObservableCollection<HeaderCheckDisplayItem> DisclosureHeaders => _disclosureHeaders;

    /// <summary>
    /// Gets the formatted grade string.
    /// </summary>
    public string GradeText => HttpHeaderEvaluationEngine.FormatGrade(Grade);

    /// <summary>
    /// Initializes or updates the localization source.
    /// </summary>
    public void Initialize(LocalizationManager? localizer)
    {
        UpdateLocalizer(localizer);
    }

    /// <summary>
    /// Updates the gateway used for tunnel-mode fetches.
    /// </summary>
    public void SetGateway(SshGatewayDto? gateway)
    {
        _service.SetGateway(gateway);
    }

    /// <summary>
    /// Updates the localization source and reprojects the current results if any.
    /// </summary>
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

        if (!HttpHeaderEvaluationEngine.TryNormalizeUrl(UrlInput, out var uri, out var errorKey))
        {
            _lastErrorKey = errorKey;
            _lastErrorArgs = [];
            ShowError = true;
            ErrorText = L(errorKey);
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _userCancelled = false;

        IsBusy = true;
        StatusText = L("ToolHttpHeadersStatusAnalyzing");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await _service.FetchAsync(uri!, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            stopwatch.Stop();

            var evaluation = HttpHeaderEvaluationEngine.EvaluateHeaders(response.Headers);
            _lastSecurityResults = evaluation.SecurityHeaders;
            _lastDisclosureResults = evaluation.DisclosureHeaders;
            _lastHost = uri!.Host;
            _lastElapsedMs = stopwatch.ElapsedMilliseconds;
            _lastErrorKey = null;
            _lastErrorArgs = [];

            Grade = evaluation.Grade;
            RawResponse = response.RawHeaderSection;
            ReportText = HttpHeaderEvaluationEngine.BuildTextReport(
                uri.Host,
                evaluation.Grade,
                evaluation.SecurityHeaders,
                evaluation.DisclosureHeaders,
                response.RawHeaderSection,
                L);

            HasResults = true;
            ReprojectResults();
            StatusText = string.Format(L("ToolHttpHeadersStatusComplete"), _lastElapsedMs);
        }
        catch (OperationCanceledException)
        {
            if (_userCancelled)
            {
                StatusText = string.Empty;
                return;
            }

            _lastErrorKey = "ToolHttpHeadersErrorTimeout";
            _lastErrorArgs = [];
            ShowError = true;
            ErrorText = L("ToolHttpHeadersErrorTimeout");
        }
        catch (Exception ex)
        {
            _lastErrorKey = "ToolHttpHeadersErrorConnection";
            _lastErrorArgs = [ex.Message];
            ShowError = true;
            ErrorText = string.Format(L("ToolHttpHeadersErrorConnection"), ex.Message);
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
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private bool CanCheck() => !IsBusy;

    private bool CanCancel() => IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        CheckCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
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
        HasResults = false;
        RawResponse = string.Empty;
        ReportText = string.Empty;
        Grade = HttpGrade.F;
        _lastSecurityResults = [];
        _lastDisclosureResults = [];
        _lastHost = string.Empty;
        _lastElapsedMs = 0;
        _lastErrorKey = null;
        _lastErrorArgs = [];
        _securityHeaders.Clear();
        _disclosureHeaders.Clear();
    }

    private void ReprojectResults()
    {
        _securityHeaders.Clear();
        foreach (var result in _lastSecurityResults)
        {
            _securityHeaders.Add(ProjectResult(result));
        }

        _disclosureHeaders.Clear();
        foreach (var result in _lastDisclosureResults)
        {
            _disclosureHeaders.Add(ProjectResult(result));
        }

        if (_lastSecurityResults.Count > 0 || _lastDisclosureResults.Count > 0)
        {
            ReportText = HttpHeaderEvaluationEngine.BuildTextReport(
                _lastHost,
                Grade,
                _lastSecurityResults,
                _lastDisclosureResults,
                RawResponse,
                L);
        }
    }

    private HeaderCheckDisplayItem ProjectResult(HeaderCheckResult result)
    {
        var displayName = string.IsNullOrWhiteSpace(result.DisplayNameKey)
            ? result.HeaderName
            : L(result.DisplayNameKey);

        var displayValue = result.ActualValueKey switch
        {
            "ToolHttpHeadersCookieMissing" => HttpHeaderEvaluationEngine.FormatCookieMissing(
                result.ActualValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                L),
            not null => L(result.ActualValueKey),
            _ => result.ActualValue,
        };

        var recommendation = string.IsNullOrWhiteSpace(result.RecommendationKey)
            ? null
            : L(result.RecommendationKey);

        return new HeaderCheckDisplayItem(displayName, displayValue, result.Status, recommendation);
    }

    private void RefreshLocalizedMessages()
    {
        if (ShowError && !string.IsNullOrWhiteSpace(_lastErrorKey))
        {
            ErrorText = _lastErrorArgs.Length > 0
                ? string.Format(L(_lastErrorKey), _lastErrorArgs)
                : L(_lastErrorKey);
        }

        if (IsBusy)
        {
            StatusText = L("ToolHttpHeadersStatusAnalyzing");
            return;
        }

        if (HasResults && _lastElapsedMs > 0)
        {
            StatusText = string.Format(L("ToolHttpHeadersStatusComplete"), _lastElapsedMs);
        }
    }

    private string L(string key) => _localizer?[key] ?? key;
}

/// <summary>
/// Localized display projection for a single header check result.
/// </summary>
public sealed record HeaderCheckDisplayItem(
    string DisplayName,
    string DisplayValue,
    HeaderCheckStatus Status,
    string? Recommendation);
