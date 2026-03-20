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
public partial class DateTimeConverterView : UserControl, IDisposable
{
    private LocalizationManager? _localizer;
    private DispatcherTimer? _debounceTimer;

    public DateTimeConverterView()
    {
        InitializeComponent();
        InitializeDebounceTimer();
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

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolDateTimeTitle");
        LblInput.Text = L("ToolDateTimeInputLabel");
        BtnNow.Content = L("ToolDateTimeBtnNow");
        LblUnixTimestamp.Text = L("ToolDateTimeUnixLabel");
        LblIsoUtc.Text = L("ToolDateTimeIsoUtcLabel");
        LblIsoLocal.Text = L("ToolDateTimeIsoLocalLabel");
        LblLocalTime.Text = L("ToolDateTimeLocalTimeLabel");
        BtnCopyUnix.Content = L("ToolDateTimeBtnCopy");
        BtnCopyIsoUtc.Content = L("ToolDateTimeBtnCopy");
        BtnCopyIsoLocal.Content = L("ToolDateTimeBtnCopy");

        System.Windows.Automation.AutomationProperties.SetName(BtnNow, L("ToolDateTimeBtnNow"));
        System.Windows.Automation.AutomationProperties.SetName(TxtInput, L("ToolDateTimeInputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyUnix, L("ToolDateTimeBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyIsoUtc, L("ToolDateTimeBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyIsoLocal, L("ToolDateTimeBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(TxtUnixTimestamp, L("ToolDateTimeUnixLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtIsoUtc, L("ToolDateTimeIsoUtcLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtIsoLocal, L("ToolDateTimeIsoLocalLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtLocalTime, L("ToolDateTimeLocalTimeLabel"));

        BtnCopyUnix.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyIsoUtc.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyIsoLocal.ToolTip = L("ToolBtnCopyToClipboard");
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

            TxtUnixTimestamp.Text = dto.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            TxtIsoUtc.Text = dto.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
            TxtIsoLocal.Text = dto.ToLocalTime().ToString("o", CultureInfo.InvariantCulture);
            TxtLocalTime.Text = dto.ToLocalTime().ToString("F", CultureInfo.CurrentCulture);
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
        TxtUnixTimestamp.Text = string.Empty;
        TxtIsoUtc.Text = string.Empty;
        TxtIsoLocal.Text = string.Empty;
        TxtLocalTime.Text = string.Empty;
        TxtDetectedFormat.Text = string.Empty;
    }

    private void OnCopyUnixClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtUnixTimestamp.Text, sender as Button);
    private void OnCopyIsoUtcClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtIsoUtc.Text, sender as Button);
    private void OnCopyIsoLocalClick(object sender, RoutedEventArgs e) => CopyToClipboard(TxtIsoLocal.Text, sender as Button);

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
