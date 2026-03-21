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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// DateTime converter tool that converts between Unix timestamps and ISO 8601 datetime strings.
/// Displays results in both UTC and local time.
/// </summary>
public partial class DateTimeConverterView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private DispatcherTimer? _debounceTimer;
    private DateTimeOffset? _lastParsedDto;

    public DateTimeConverterView()
    {
        InitializeComponent();
        InitializeDebounceTimer();
        PopulateTimezones();
    }

    /// <summary>
    /// Initializes the tool with localization and optional context.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            TxtInput.Text = context.Argument;
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtInput.Focus();
            TxtInput.SelectAll();
        });
    }

    private void InitializeDebounceTimer()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            ConvertDateTime();
        };
    }

    private void PopulateTimezones()
    {
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            CmbTimezone.Items.Add(new ComboBoxItem
            {
                Content = tz.DisplayName,
                Tag = tz.Id,
            });
        }

        // Select UTC by default
        for (int i = 0; i < CmbTimezone.Items.Count; i++)
        {
            if (CmbTimezone.Items[i] is ComboBoxItem item && item.Tag is string id && id == "UTC")
            {
                CmbTimezone.SelectedIndex = i;
                break;
            }
        }
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolDateTimeTitle");
        LblInput.Text = L("ToolDateTimeInputLabel");
        BtnNow.Content = L("ToolDateTimeBtnNow");
        LblUnixTimestamp.Text = L("ToolDateTimeUnixLabel");
        LblIsoUtc.Text = L("ToolDateTimeIsoUtcLabel");
        LblIsoLocal.Text = L("ToolDateTimeIsoLocalLabel");
        LblLocalTime.Text = L("ToolDateTimeLocalTimeLabel");
        LblTimezone.Text = L("ToolDateTimeTimezoneLabel");
        LblRelativeTime.Text = L("ToolDateTimeRelativeTimeLabel");
        BtnCopyUnix.Content = L("ToolDateTimeBtnCopy");
        BtnCopyIsoUtc.Content = L("ToolDateTimeBtnCopy");
        BtnCopyIsoLocal.Content = L("ToolDateTimeBtnCopy");
        BtnCopyLocalTime.Content = L("ToolDateTimeBtnCopy");
        BtnCopyTzTime.Content = L("ToolDateTimeBtnCopy");

        System.Windows.Automation.AutomationProperties.SetName(BtnNow, L("ToolDateTimeBtnNow"));
        System.Windows.Automation.AutomationProperties.SetName(TxtInput, L("ToolDateTimeInputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyUnix, L("ToolDateTimeBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyIsoUtc, L("ToolDateTimeBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyIsoLocal, L("ToolDateTimeBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyLocalTime, L("ToolDateTimeBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyTzTime, L("ToolDateTimeBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(TxtUnixTimestamp, L("ToolDateTimeUnixLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtIsoUtc, L("ToolDateTimeIsoUtcLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtIsoLocal, L("ToolDateTimeIsoLocalLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtLocalTime, L("ToolDateTimeLocalTimeLabel"));
        System.Windows.Automation.AutomationProperties.SetName(CmbTimezone, L("ToolDateTimeTimezoneLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtTzTime, L("ToolDateTimeTimezoneLabel"));

        BtnCopyUnix.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyIsoUtc.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyIsoLocal.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyLocalTime.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyTzTime.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnNowClick(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        TxtInput.Text = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    private void ConvertDateTime()
    {
        var input = TxtInput.Text?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            ClearOutput();
            return;
        }

        try
        {
            DateTimeOffset dto;

            if (TryParseUnixTimestamp(input, out var unixDto, out var isMilliseconds))
            {
                dto = unixDto;
                TxtDetectedFormat.Text = isMilliseconds
                    ? L("ToolDateTimeDetectedMs")
                    : L("ToolDateTimeDetectedUnix");
            }
            else if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture,
                         DateTimeStyles.None, out var parsedDto))
            {
                dto = parsedDto;
                TxtDetectedFormat.Text = L("ToolDateTimeDetectedIso");
            }
            else
            {
                ClearOutput();
                TxtDetectedFormat.Text = L("ToolDateTimeErrorInvalid");
                return;
            }

            _lastParsedDto = dto;

            TxtUnixTimestamp.Text = dto.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            TxtIsoUtc.Text = dto.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
            TxtIsoLocal.Text = dto.ToLocalTime().ToString("o", CultureInfo.InvariantCulture);
            TxtLocalTime.Text = dto.ToLocalTime().ToString("F", CultureInfo.CurrentCulture);
            UpdateTimezoneDisplay(dto);
            TxtRelativeTime.Text = FormatRelativeTime(dto);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"DateTimeConverter conversion failed: {ex.Message}");
            ClearOutput();
            TxtDetectedFormat.Text = L("ToolDateTimeErrorInvalid");
        }
    }

    private static bool TryParseUnixTimestamp(string input, out DateTimeOffset result, out bool isMilliseconds)
    {
        result = default;
        isMilliseconds = false;

        if (!long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return false;

        // Reasonable range for seconds: 1970-01-01 to 3000-01-01
        const long maxSeconds = 32503680000L;
        const long minSeconds = -62135596800L;

        // If 13+ digits, treat as milliseconds
        if (input.Length >= 13 || value > maxSeconds)
        {
            const long maxMilliseconds = maxSeconds * 1000;
            const long minMilliseconds = minSeconds * 1000;

            if (value < minMilliseconds || value > maxMilliseconds)
                return false;

            result = DateTimeOffset.FromUnixTimeMilliseconds(value);
            isMilliseconds = true;
            return true;
        }

        if (value < minSeconds || value > maxSeconds)
            return false;

        result = DateTimeOffset.FromUnixTimeSeconds(value);
        return true;
    }

    private void ClearOutput()
    {
        _lastParsedDto = null;
        TxtUnixTimestamp.Text = string.Empty;
        TxtIsoUtc.Text = string.Empty;
        TxtIsoLocal.Text = string.Empty;
        TxtLocalTime.Text = string.Empty;
        TxtTzTime.Text = string.Empty;
        TxtRelativeTime.Text = string.Empty;
        TxtDetectedFormat.Text = string.Empty;
    }

    private void OnTimezoneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_lastParsedDto.HasValue)
        {
            UpdateTimezoneDisplay(_lastParsedDto.Value);
        }
    }

    private void UpdateTimezoneDisplay(DateTimeOffset dto)
    {
        if (CmbTimezone.SelectedItem is not ComboBoxItem item || item.Tag is not string tzId)
        {
            TxtTzTime.Text = string.Empty;
            return;
        }

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            var converted = TimeZoneInfo.ConvertTime(dto, tz);
            TxtTzTime.Text = converted.ToString("F", CultureInfo.CurrentCulture);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"DateTimeConverter timezone conversion failed: {ex.Message}");
            TxtTzTime.Text = string.Empty;
        }
    }

    private string FormatRelativeTime(DateTimeOffset dto)
    {
        var now = DateTimeOffset.UtcNow;
        var diff = now - dto;
        bool isPast = diff.TotalSeconds >= 0;
        var absDiff = isPast ? diff : diff.Negate();

        string relative;
        if (absDiff.TotalSeconds < 60)
        {
            relative = string.Format(L("ToolDateTimeRelativeSeconds"), (int)absDiff.TotalSeconds);
        }
        else if (absDiff.TotalMinutes < 60)
        {
            relative = string.Format(L("ToolDateTimeRelativeMinutes"), (int)absDiff.TotalMinutes);
        }
        else if (absDiff.TotalHours < 24)
        {
            relative = string.Format(L("ToolDateTimeRelativeHours"), (int)absDiff.TotalHours);
        }
        else if (absDiff.TotalDays < 30)
        {
            relative = string.Format(L("ToolDateTimeRelativeDays"), (int)absDiff.TotalDays);
        }
        else if (absDiff.TotalDays < 365)
        {
            relative = string.Format(L("ToolDateTimeRelativeMonths"), (int)(absDiff.TotalDays / 30));
        }
        else
        {
            relative = string.Format(L("ToolDateTimeRelativeYears"), (int)(absDiff.TotalDays / 365));
        }

        return isPast
            ? string.Format(L("ToolDateTimeRelativeAgo"), relative)
            : string.Format(L("ToolDateTimeRelativeIn"), relative);
    }

    private void OnCopyUnixClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtUnixTimestamp.Text, sender as Button);
    private void OnCopyIsoUtcClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtIsoUtc.Text, sender as Button);
    private void OnCopyIsoLocalClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtIsoLocal.Text, sender as Button);
    private void OnCopyLocalTimeClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtLocalTime.Text, sender as Button);
    private void OnCopyTzTimeClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtTzTime.Text, sender as Button);

    private static void CopyToClipboard(string? text, Button? btn)
    {
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                Clipboard.SetText(text);
                CopyFeedbackHelper.ShowCopyFeedback(btn);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"DateTimeConverter clipboard copy failed: {ex.Message}");
            }
        }
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        if (_debounceTimer is not null)
        {
            _debounceTimer.Stop();
            _debounceTimer = null;
        }
    }
}
