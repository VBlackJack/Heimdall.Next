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
using Heimdall.App.Services;
using Heimdall.Core.Localization;
using Heimdall.Core.Logging;
using Heimdall.Core.Otp;

namespace Heimdall.App.ViewModels.Tools;

public sealed partial class TotpGeneratorViewModel : ObservableObject, IDisposable
{
    private const string PlaceholderCode = "------";

    private LocalizationManager? _localizer;
    private byte[]? _secretBytes;
    private bool _disposed;

    [ObservableProperty] private string _secretInput = string.Empty;
    [ObservableProperty] private string _currentCode = PlaceholderCode;
    [ObservableProperty] private int _remainingSeconds = TotpParameters.DefaultTimeStepSeconds;
    [ObservableProperty] private string _timeRemainingText = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isErrorVisible;
    [ObservableProperty] private bool _isCodePanelVisible;

    public TotpGeneratorViewModel(IOtpGeneratorService? service = null) { }

    public int TimeStepSeconds => TotpParameters.DefaultTimeStepSeconds;

    public void Initialize(LocalizationManager? localizer)
    {
        if (!ReferenceEquals(_localizer, localizer))
        {
            if (_localizer is not null)
            {
                _localizer.LocaleChanged -= OnLocaleChanged;
            }

            _localizer = localizer;
            if (_localizer is not null)
            {
                _localizer.LocaleChanged += OnLocaleChanged;
            }
        }

        RefreshTimeRemainingText();
    }

    [RelayCommand]
    private void Start()
    {
        ClearError();

        var sanitized = (SecretInput ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (string.IsNullOrEmpty(sanitized))
        {
            ShowError(L("ToolTotpErrorSecretRequired"));
            return;
        }

        try
        {
            _secretBytes = Base32Codec.Decode(sanitized);
        }
        catch (FormatException)
        {
            _secretBytes = null;
            ShowError(L("ToolTotpErrorInvalidBase32"));
            return;
        }

        IsCodePanelVisible = true;
        RefreshCode(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public void RefreshCode(long unixTimeSeconds)
    {
        if (_disposed || _secretBytes is null)
        {
            return;
        }

        try
        {
            CurrentCode = TotpGenerator.Generate(
                _secretBytes,
                unixTimeSeconds,
                TotpParameters.DefaultAlgorithm,
                TotpParameters.DefaultDigits,
                TotpParameters.DefaultTimeStepSeconds);
            RemainingSeconds = TotpGenerator.RemainingInStep(unixTimeSeconds, TotpParameters.DefaultTimeStepSeconds);
            RefreshTimeRemainingText();
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"TotpGenerator refresh failed: {ex.Message}");
            CurrentCode = PlaceholderCode;
        }
    }

    public bool CanCopy() => !string.IsNullOrEmpty(CurrentCode) && CurrentCode != PlaceholderCode;

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

        _secretBytes = null;
        GC.SuppressFinalize(this);
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        IsErrorVisible = true;
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        IsErrorVisible = false;
    }

    private void RefreshTimeRemainingText()
    {
        TimeRemainingText = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            L("ToolTotpTimeRemainingFormat"),
            RemainingSeconds);
    }

    partial void OnRemainingSecondsChanged(int value)
    {
        RefreshTimeRemainingText();
    }

    private void OnLocaleChanged(string _)
    {
        RefreshTimeRemainingText();
    }

    private string L(string key) => _localizer?[key] ?? key;
}
