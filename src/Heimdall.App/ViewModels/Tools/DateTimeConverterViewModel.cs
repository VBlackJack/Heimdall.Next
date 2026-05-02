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
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Localization;
using Heimdall.Core.Temporal;

namespace Heimdall.App.ViewModels.Tools;

public sealed partial class DateTimeConverterViewModel : ObservableObject, IDisposable
{
    private readonly IDateTimeConverterToolService _service;
    private readonly Func<IEnumerable<TimeZoneInfo>> _timeZoneProvider;
    private LocalizationManager? _localizer;
    private bool _initialized;
    private bool _disposed;
    private DateTimeOffset? _lastParsedDto;
    private DateTimeFormat _lastDetectedFormat = DateTimeFormat.Invalid;

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _detectedFormatText = string.Empty;
    [ObservableProperty] private string _unixTimestampText = string.Empty;
    [ObservableProperty] private string _isoUtcText = string.Empty;
    [ObservableProperty] private string _isoLocalText = string.Empty;
    [ObservableProperty] private string _localTimeText = string.Empty;
    [ObservableProperty] private string _timezoneTimeText = string.Empty;
    [ObservableProperty] private string _relativeTimeText = string.Empty;
    [ObservableProperty] private bool _showEmptyState = true;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _selectedTimezoneId = "UTC";

    public ObservableCollection<TimezoneItem> Timezones { get; } = [];

    public DateTimeConverterViewModel(
        IDateTimeConverterToolService? service = null,
        Func<IEnumerable<TimeZoneInfo>>? timeZoneProvider = null)
    {
        _service = service ?? new DateTimeConverterToolService();
        _timeZoneProvider = timeZoneProvider ?? TimeZoneInfo.GetSystemTimeZones;
    }

    public void Initialize(LocalizationManager? localizer)
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

        LoadTimezones();
        RebuildLocalizedOutput();
    }

    public void MarkInitialized() => _initialized = true;

    public void PrefillInput(string? input)
    {
        if (!string.IsNullOrWhiteSpace(input))
        {
            InputText = input;
        }
    }

    [RelayCommand]
    private void UseNow()
    {
        InputText = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    public void ConvertCurrentInput()
    {
        var input = InputText?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            ClearAll(showEmptyState: true);
            return;
        }

        var outcome = _service.Parse(input);
        if (!outcome.IsSuccess)
        {
            ClearAll(showEmptyState: false);
            DetectedFormatText = L("ToolDateTimeErrorInvalid");
            return;
        }

        _lastParsedDto = outcome.Value;
        _lastDetectedFormat = outcome.Detected;
        HasResult = true;
        ShowEmptyState = false;
        RebuildOutputs(outcome.Value, outcome.Detected);
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

    partial void OnInputTextChanged(string value)
    {
        if (!_initialized || _disposed)
        {
            return;
        }

        ClearOutputsOnly();
        ShowEmptyState = true;
        HasResult = false;
    }

    partial void OnSelectedTimezoneIdChanged(string value)
    {
        if (_disposed || !_initialized || !_lastParsedDto.HasValue)
        {
            return;
        }

        UpdateTimezoneDisplay(_lastParsedDto.Value);
    }

    private void LoadTimezones()
    {
        if (Timezones.Count > 0)
        {
            return;
        }

        foreach (var timezone in _timeZoneProvider())
        {
            Timezones.Add(new TimezoneItem(
                timezone.Id,
                timezone.DisplayName,
                BuildSearchableName(timezone.DisplayName),
                timezone));
        }

        if (Timezones.Any(tz => tz.Id == "UTC"))
        {
            SelectedTimezoneId = "UTC";
        }
        else if (Timezones.Count > 0)
        {
            SelectedTimezoneId = Timezones[0].Id;
        }
    }

    private void RebuildOutputs(DateTimeOffset dto, DateTimeFormat detectedFormat)
    {
        DetectedFormatText = detectedFormat switch
        {
            DateTimeFormat.UnixSeconds => L("ToolDateTimeDetectedUnix"),
            DateTimeFormat.UnixMilliseconds => L("ToolDateTimeDetectedMs"),
            DateTimeFormat.Iso8601 => L("ToolDateTimeDetectedIso"),
            _ => string.Empty,
        };

        UnixTimestampText = dto.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        IsoUtcText = dto.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        IsoLocalText = dto.ToLocalTime().ToString("o", CultureInfo.InvariantCulture);
        LocalTimeText = dto.ToLocalTime().ToString("F", CultureInfo.CurrentCulture);
        UpdateTimezoneDisplay(dto);
        RelativeTimeText = FormatRelative(_service.ComputeRelative(dto, DateTimeOffset.UtcNow));
    }

    private void UpdateTimezoneDisplay(DateTimeOffset dto)
    {
        if (string.IsNullOrWhiteSpace(SelectedTimezoneId))
        {
            TimezoneTimeText = string.Empty;
            return;
        }

        try
        {
            var timezone = Timezones.FirstOrDefault(item => item.Id == SelectedTimezoneId)?.TimeZone;
            if (timezone is null)
            {
                TimezoneTimeText = string.Empty;
                return;
            }

            var converted = TimeZoneInfo.ConvertTime(dto, timezone);
            TimezoneTimeText = converted.ToString("F", CultureInfo.CurrentCulture);
        }
        catch
        {
            TimezoneTimeText = string.Empty;
        }
    }

    private string FormatRelative(RelativeDuration duration)
    {
        var key = duration.Unit switch
        {
            RelativeTimeUnit.Seconds => "ToolDateTimeRelativeSeconds",
            RelativeTimeUnit.Minutes => "ToolDateTimeRelativeMinutes",
            RelativeTimeUnit.Hours => "ToolDateTimeRelativeHours",
            RelativeTimeUnit.Days => "ToolDateTimeRelativeDays",
            RelativeTimeUnit.Months => "ToolDateTimeRelativeMonths",
            RelativeTimeUnit.Years => "ToolDateTimeRelativeYears",
            _ => throw new ArgumentOutOfRangeException(nameof(duration)),
        };

        var relative = string.Format(CultureInfo.CurrentCulture, L(key), duration.Value);
        return duration.IsPast
            ? string.Format(CultureInfo.CurrentCulture, L("ToolDateTimeRelativeAgo"), relative)
            : string.Format(CultureInfo.CurrentCulture, L("ToolDateTimeRelativeIn"), relative);
    }

    private void RebuildLocalizedOutput()
    {
        if (_lastParsedDto.HasValue)
        {
            RebuildOutputs(_lastParsedDto.Value, _lastDetectedFormat);
            return;
        }

        if (string.IsNullOrWhiteSpace(InputText))
        {
            DetectedFormatText = string.Empty;
            return;
        }

        DetectedFormatText = L("ToolDateTimeErrorInvalid");
    }

    private void ClearAll(bool showEmptyState)
    {
        _lastParsedDto = null;
        _lastDetectedFormat = DateTimeFormat.Invalid;
        ClearOutputsOnly();
        HasResult = false;
        ShowEmptyState = showEmptyState;
    }

    private void ClearOutputsOnly()
    {
        DetectedFormatText = string.Empty;
        UnixTimestampText = string.Empty;
        IsoUtcText = string.Empty;
        IsoLocalText = string.Empty;
        LocalTimeText = string.Empty;
        TimezoneTimeText = string.Empty;
        RelativeTimeText = string.Empty;
    }

    private void OnLocaleChanged(string _)
    {
        RebuildLocalizedOutput();
    }

    /// <summary>
    /// Builds a prefix-search value for WPF TextSearch by biasing standard
    /// timezone display names toward their last listed city. TextSearch is
    /// still prefix-based, so only one city can be made searchable this way.
    /// </summary>
    public static string BuildSearchableName(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        int closingParenIndex = displayName.IndexOf(')');
        if (closingParenIndex < 0)
        {
            return displayName;
        }

        string cityList = displayName[(closingParenIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(cityList))
        {
            return displayName;
        }

        string? lastCity = cityList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(lastCity))
        {
            return displayName;
        }

        return $"{lastCity} - {displayName}";
    }

    private string L(string key) => _localizer?[key] ?? key;
}

public sealed record TimezoneItem(string Id, string DisplayName, string SearchableName, TimeZoneInfo TimeZone);
