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
using System.Windows.Automation;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Visual cron expression builder with bidirectional editing and human-readable preview.
/// Supports standard 5-field cron format (minute, hour, day-of-month, month, day-of-week).
/// </summary>
public partial class CrontabBuilderView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private bool _initialized;
    private bool _updatingFromCode;

    private const int NextRunsCount = 5;

    public CrontabBuilderView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the view with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();
        PopulateComboBoxes();

        _initialized = true;

        if (!string.IsNullOrWhiteSpace(context?.Argument) && TryParseCron(context.Argument.Trim(), out var fields))
        {
            ApplyCronToSelectors(fields);
            TxtManualInput.Text = context.Argument.Trim();
        }
        else
        {
            // Default: every minute
            CmbMinute.SelectedIndex = 0;
            CmbHour.SelectedIndex = 0;
            CmbDayOfMonth.SelectedIndex = 0;
            CmbMonth.SelectedIndex = 0;
            CmbDayOfWeek.SelectedIndex = 0;
        }

        UpdateFromSelectors();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolCronTitle");
        LblPresets.Text = L("ToolCronPresetsLabel");
        LblMinute.Text = L("ToolCronMinute");
        LblHour.Text = L("ToolCronHour");
        LblDayOfMonth.Text = L("ToolCronDayOfMonth");
        LblMonth.Text = L("ToolCronMonth");
        LblDayOfWeek.Text = L("ToolCronDayOfWeek");
        BtnCopy.Content = L("ToolCronBtnCopy");
        LblManualEdit.Text = L("ToolCronManualEdit");
        LblNextRuns.Text = L("ToolCronNextRuns");

        BtnPresetEveryMin.Content = L("ToolCronPresetEveryMin");
        BtnPresetEveryHour.Content = L("ToolCronPresetEveryHour");
        BtnPresetDailyMidnight.Content = L("ToolCronPresetDailyMidnight");
        BtnPresetWeekdays9am.Content = L("ToolCronPresetWeekdays9am");
        BtnPresetWeeklySunday.Content = L("ToolCronPresetWeeklySunday");
        BtnPresetMonthly1st.Content = L("ToolCronPresetMonthly1st");

        AutomationProperties.SetName(BtnCopy, L("ToolCronBtnCopy"));
        AutomationProperties.SetName(TxtManualInput, L("ToolCronManualEdit"));
        AutomationProperties.SetName(TxtCronExpression, L("ToolCronExpression"));
        AutomationProperties.SetName(CmbMinute, L("ToolCronMinute"));
        AutomationProperties.SetName(CmbHour, L("ToolCronHour"));
        AutomationProperties.SetName(CmbDayOfMonth, L("ToolCronDayOfMonth"));
        AutomationProperties.SetName(CmbMonth, L("ToolCronMonth"));
        AutomationProperties.SetName(CmbDayOfWeek, L("ToolCronDayOfWeek"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
    }

    private void PopulateComboBoxes()
    {
        // Minute options
        var minuteItems = new List<ComboBoxItem>
        {
            MakeItem(L("ToolCronEveryMinute"), "*"),
            MakeItem(L("ToolCronEvery5Min"), "*/5"),
            MakeItem(L("ToolCronEvery15Min"), "*/15"),
            MakeItem(L("ToolCronEvery30Min"), "*/30"),
        };
        for (var i = 0; i < 60; i++)
        {
            minuteItems.Add(MakeItem(i.ToString(), i.ToString()));
        }
        CmbMinute.ItemsSource = minuteItems;

        // Hour options
        var hourItems = new List<ComboBoxItem>
        {
            MakeItem(L("ToolCronEveryHour"), "*"),
        };
        for (var i = 0; i < 24; i++)
        {
            hourItems.Add(MakeItem($"{i:D2}:00", i.ToString()));
        }
        CmbHour.ItemsSource = hourItems;

        // Day of month options
        var domItems = new List<ComboBoxItem>
        {
            MakeItem(L("ToolCronEveryDay"), "*"),
        };
        for (var i = 1; i <= 31; i++)
        {
            domItems.Add(MakeItem(i.ToString(), i.ToString()));
        }
        CmbDayOfMonth.ItemsSource = domItems;

        // Month options
        var monthItems = new List<ComboBoxItem>
        {
            MakeItem(L("ToolCronEveryMonth"), "*"),
        };
        string[] monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        for (var i = 1; i <= 12; i++)
        {
            monthItems.Add(MakeItem($"{i} ({monthNames[i - 1]})", i.ToString()));
        }
        CmbMonth.ItemsSource = monthItems;

        // Day of week options
        var dowItems = new List<ComboBoxItem>
        {
            MakeItem(L("ToolCronEveryDayOfWeek"), "*"),
        };
        string[] dayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        for (var i = 0; i < 7; i++)
        {
            dowItems.Add(MakeItem($"{dayNames[i]} ({i})", i.ToString()));
        }
        CmbDayOfWeek.ItemsSource = dowItems;
    }

    private static ComboBoxItem MakeItem(string display, string value)
    {
        return new ComboBoxItem { Content = display, Tag = value };
    }

    private void OnSelectorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingFromCode) return;

        _updatingFromCode = true;
        try
        {
            UpdateFromSelectors();
        }
        finally
        {
            _updatingFromCode = false;
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string cronExpr) return;
        if (!TryParseCron(cronExpr, out var fields)) return;

        _updatingFromCode = true;
        try
        {
            ApplyCronToSelectors(fields);
            TxtCronExpression.Text = cronExpr;
            TxtManualInput.Text = cronExpr;
            TxtDescription.Text = DescribeCron(fields);
            NextRunsList.ItemsSource = CalculateNextRuns(fields, NextRunsCount);
            TxtValidationError.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _updatingFromCode = false;
        }
    }

    private void OnManualInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _updatingFromCode) return;

        var text = TxtManualInput.Text.Trim();

        if (string.IsNullOrEmpty(text))
        {
            TxtValidationError.Visibility = Visibility.Collapsed;
            return;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
        {
            TxtValidationError.Text = L("ToolCronValidationFieldCount");
            TxtValidationError.Visibility = Visibility.Visible;
            return;
        }

        // Validate each field individually
        string[] fieldLabels = [L("ToolCronMinute"), L("ToolCronHour"), L("ToolCronDayOfMonth"), L("ToolCronMonth"), L("ToolCronDayOfWeek")];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!IsValidCronField(parts[i]))
            {
                TxtValidationError.Text = string.Format(L("ToolCronValidationInvalidField"), fieldLabels[i], parts[i]);
                TxtValidationError.Visibility = Visibility.Visible;
                return;
            }
        }

        // Validate ranges
        int[][] ranges = [[0, 59], [0, 23], [1, 31], [1, 12], [0, 6]];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!ValidateFieldRange(parts[i], ranges[i][0], ranges[i][1]))
            {
                TxtValidationError.Text = string.Format(L("ToolCronValidationOutOfRange"), fieldLabels[i], ranges[i][0], ranges[i][1]);
                TxtValidationError.Visibility = Visibility.Visible;
                return;
            }
        }

        TxtValidationError.Visibility = Visibility.Collapsed;

        if (!TryParseCron(text, out var fields)) return;

        _updatingFromCode = true;
        try
        {
            ApplyCronToSelectors(fields);
            TxtCronExpression.Text = text;
            TxtDescription.Text = DescribeCron(fields);
            NextRunsList.ItemsSource = CalculateNextRuns(fields, NextRunsCount);
        }
        finally
        {
            _updatingFromCode = false;
        }
    }

    /// <summary>
    /// Validates that numeric values in a cron field fall within the allowed range.
    /// </summary>
    private static bool ValidateFieldRange(string field, int min, int max)
    {
        if (field == "*") return true;

        foreach (var part in field.Split(','))
        {
            if (part.Contains('/'))
            {
                var stepParts = part.Split('/');
                if (stepParts.Length != 2) return false;
                if (stepParts[0] != "*" && int.TryParse(stepParts[0], out var start) && (start < min || start > max))
                    return false;
                if (!int.TryParse(stepParts[1], out var step) || step <= 0)
                    return false;
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length != 2) return false;
                if (!int.TryParse(rangeParts[0], out var rStart) || rStart < min || rStart > max) return false;
                if (!int.TryParse(rangeParts[1], out var rEnd) || rEnd < min || rEnd > max) return false;
            }
            else if (int.TryParse(part, out var val))
            {
                if (val < min || val > max) return false;
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    private void UpdateFromSelectors()
    {
        var minute = GetSelectedTag(CmbMinute) ?? "*";
        var hour = GetSelectedTag(CmbHour) ?? "*";
        var dom = GetSelectedTag(CmbDayOfMonth) ?? "*";
        var month = GetSelectedTag(CmbMonth) ?? "*";
        var dow = GetSelectedTag(CmbDayOfWeek) ?? "*";

        var cron = $"{minute} {hour} {dom} {month} {dow}";
        TxtCronExpression.Text = cron;
        TxtManualInput.Text = cron;

        var fields = new[] { minute, hour, dom, month, dow };
        TxtDescription.Text = DescribeCron(fields);
        NextRunsList.ItemsSource = CalculateNextRuns(fields, NextRunsCount);
    }

    private static string? GetSelectedTag(System.Windows.Controls.ComboBox cmb)
    {
        return (cmb.SelectedItem as ComboBoxItem)?.Tag as string;
    }

    private void ApplyCronToSelectors(string[] fields)
    {
        SelectByTag(CmbMinute, fields[0]);
        SelectByTag(CmbHour, fields[1]);
        SelectByTag(CmbDayOfMonth, fields[2]);
        SelectByTag(CmbMonth, fields[3]);
        SelectByTag(CmbDayOfWeek, fields[4]);
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox cmb, string value)
    {
        if (cmb.ItemsSource is not IEnumerable<ComboBoxItem> items) return;

        foreach (var item in items)
        {
            if (item.Tag is string tag && tag == value)
            {
                cmb.SelectedItem = item;
                return;
            }
        }

        // If no exact match, select "every" (first item, which has tag "*")
        cmb.SelectedIndex = 0;
    }

    private static bool TryParseCron(string input, out string[] fields)
    {
        fields = [];
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return false;

        // Basic validation: each field must contain valid cron characters
        foreach (var part in parts)
        {
            if (!IsValidCronField(part)) return false;
        }

        fields = parts;
        return true;
    }

    private static bool IsValidCronField(string field)
    {
        foreach (var c in field)
        {
            if (!char.IsDigit(c) && c != '*' && c != '/' && c != '-' && c != ',')
            {
                return false;
            }
        }
        return field.Length > 0;
    }

    private string DescribeCron(string[] fields)
    {
        var minute = fields[0];
        var hour = fields[1];
        var dom = fields[2];
        var month = fields[3];
        var dow = fields[4];

        // Common patterns
        if (minute == "*" && hour == "*" && dom == "*" && month == "*" && dow == "*")
            return L("ToolCronDescEveryMinute");

        if (minute == "0" && hour == "*" && dom == "*" && month == "*" && dow == "*")
            return L("ToolCronDescEveryHour");

        if (minute == "0" && hour == "0" && dom == "*" && month == "*" && dow == "*")
            return L("ToolCronDescEveryDay");

        if (minute.StartsWith("*/", StringComparison.Ordinal) && hour == "*" && dom == "*" && month == "*" && dow == "*")
        {
            var interval = minute[2..];
            return string.Format(L("ToolCronDescEveryNMin"), interval);
        }

        // Specific time
        if (int.TryParse(minute, out var m) && int.TryParse(hour, out var h) && dom == "*" && month == "*" && dow == "*")
            return string.Format(L("ToolCronDescDailyAt"), $"{h:D2}:{m:D2}");

        if (int.TryParse(minute, out m) && int.TryParse(hour, out h) && dom == "*" && month == "*" && dow != "*")
        {
            var dayName = GetDayName(dow);
            return string.Format(L("ToolCronDescWeeklyAt"), dayName, $"{h:D2}:{m:D2}");
        }

        if (int.TryParse(minute, out m) && int.TryParse(hour, out h) && int.TryParse(dom, out var d) && month == "*" && dow == "*")
            return string.Format(L("ToolCronDescMonthlyAt"), d, $"{h:D2}:{m:D2}");

        return string.Format(L("ToolCronDescCustom"), string.Join(" ", fields));
    }

    private static string GetDayName(string field)
    {
        string[] days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
        if (int.TryParse(field, out var idx) && idx >= 0 && idx < 7)
            return days[idx];
        return field;
    }

    private static List<string> CalculateNextRuns(string[] fields, int count)
    {
        var results = new List<string>();
        var current = DateTime.Now;

        // Parse each field into a set of valid values
        var validMinutes = ExpandField(fields[0], 0, 59);
        var validHours = ExpandField(fields[1], 0, 23);
        var validDoms = ExpandField(fields[2], 1, 31);
        var validMonths = ExpandField(fields[3], 1, 12);
        var validDows = ExpandField(fields[4], 0, 6);

        if (validMinutes == null || validHours == null || validDoms == null || validMonths == null || validDows == null)
        {
            return results;
        }

        // Advance by one minute from now
        current = current.AddMinutes(1);
        current = new DateTime(current.Year, current.Month, current.Day, current.Hour, current.Minute, 0);

        var maxIterations = 525960; // ~1 year in minutes
        var iterations = 0;

        while (results.Count < count && iterations < maxIterations)
        {
            iterations++;

            if (validMonths.Contains(current.Month) &&
                validDoms.Contains(current.Day) &&
                validHours.Contains(current.Hour) &&
                validMinutes.Contains(current.Minute) &&
                validDows.Contains((int)current.DayOfWeek))
            {
                results.Add(current.ToString("yyyy-MM-dd HH:mm (ddd)", CultureInfo.InvariantCulture));
            }

            current = current.AddMinutes(1);
        }

        return results;
    }

    private static HashSet<int>? ExpandField(string field, int min, int max)
    {
        var values = new HashSet<int>();

        if (field == "*")
        {
            for (var i = min; i <= max; i++) values.Add(i);
            return values;
        }

        // Handle comma-separated lists
        foreach (var part in field.Split(','))
        {
            // Handle step values: */N or N/N
            if (part.Contains('/'))
            {
                var stepParts = part.Split('/');
                if (stepParts.Length != 2 || !int.TryParse(stepParts[1], out var step) || step <= 0) return null;

                var start = stepParts[0] == "*" ? min : int.TryParse(stepParts[0], out var s) ? s : min;
                for (var i = start; i <= max; i += step) values.Add(i);
            }
            // Handle ranges: N-M
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length != 2 ||
                    !int.TryParse(rangeParts[0], out var rangeStart) ||
                    !int.TryParse(rangeParts[1], out var rangeEnd)) return null;

                for (var i = rangeStart; i <= rangeEnd; i++) values.Add(i);
            }
            // Single value
            else if (int.TryParse(part, out var val))
            {
                values.Add(val);
            }
            else
            {
                return null;
            }
        }

        return values;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtCronExpression.Text))
        {
            Clipboard.SetText(TxtCronExpression.Text);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpCRONTAB");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
