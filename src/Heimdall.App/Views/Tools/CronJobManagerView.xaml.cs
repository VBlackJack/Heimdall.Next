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
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Cron job manager tool for parsing pasted crontab output and viewing
/// Windows Scheduled Tasks on the local machine.
/// </summary>
public partial class CronJobManagerView : UserControl, IToolView
{
    private static readonly int NextRunsCount = 5;

    private LocalizationManager? _localizer;
    private Action<bool>? _setBusy;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isLoading;

    private readonly ObservableCollection<CrontabDisplayEntry> _cronEntries = [];
    private readonly ObservableCollection<WindowsTaskEntry> _taskEntries = [];
    private readonly List<CrontabParsedEntry> _parsedEntries = [];

    public CronJobManagerView()
    {
        InitializeComponent();
        CronResultsGrid.ItemsSource = _cronEntries;
        TasksResultsGrid.ItemsSource = _taskEntries;
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolCronJobTitle");
        TabPasteCrontab.Header = L("ToolCronJobTabPaste");
        TabWindowsTasks.Header = L("ToolCronJobTabWindows");
        LblPasteInstructions.Text = L("ToolCronJobPasteInstructions");
        BtnParse.Content = L("ToolCronJobBtnParse");
        BtnClearPaste.Content = L("ToolCronJobBtnClear");
        BtnRefreshTasks.Content = L("ToolCronJobBtnRefresh");
        BtnCopyAll.Content = L("ToolCronJobBtnCopy");
        TxtStatus.Text = string.Empty;

        ColCronSchedule.Header = L("ToolCronJobColSchedule");
        ColCronCommand.Header = L("ToolCronJobColCommand");
        ColCronNextRun.Header = L("ToolCronJobColNextRun");
        ColCronDescription.Header = L("ToolCronJobColDescription");

        ColTaskName.Header = L("ToolCronJobColTaskName");
        ColTaskStatus.Header = L("ToolCronJobColTaskStatus");
        ColTaskNextRun.Header = L("ToolCronJobColNextRun");
        ColTaskLastRun.Header = L("ToolCronJobColLastRun");
        ColTaskLastResult.Header = L("ToolCronJobColLastResult");

        TxtDetailHeader.Text = L("ToolCronJobDetailHeader");

        BtnCopyAll.ToolTip = L("ToolBtnCopyToClipboard");

        AutomationProperties.SetName(TxtCrontabInput, L("ToolCronJobPasteInstructions"));
        AutomationProperties.SetName(BtnParse, L("ToolCronJobBtnParse"));
        AutomationProperties.SetName(BtnClearPaste, L("ToolCronJobBtnClear"));
        AutomationProperties.SetName(BtnRefreshTasks, L("ToolCronJobBtnRefresh"));
        AutomationProperties.SetName(BtnCopyAll, L("ToolCronJobBtnCopy"));
        AutomationProperties.SetName(CronResultsGrid, L("ToolCronJobTabPaste"));
        AutomationProperties.SetName(TasksResultsGrid, L("ToolCronJobTabWindows"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        AutomationProperties.SetName(LoadingBar, L("ToolCronJobA11yLoading"));
        TxtEmptyState.Text = L("ToolCronJobEmptyState");
    }

    // ── Paste Crontab Mode ──────────────────────────────────────

    private void OnParseClick(object sender, RoutedEventArgs e)
    {
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        var content = TxtCrontabInput.Text;
        TxtCronError.Visibility = Visibility.Collapsed;
        CronDetailPanel.Visibility = Visibility.Collapsed;
        _cronEntries.Clear();
        _parsedEntries.Clear();

        if (string.IsNullOrWhiteSpace(content))
        {
            TxtCronError.Text = L("ToolCronJobErrorEmpty");
            TxtCronError.Visibility = Visibility.Visible;
            return;
        }

        var entries = ParseCrontab(content);

        if (entries.Count == 0)
        {
            TxtCronError.Text = L("ToolCronJobErrorNoParsed");
            TxtCronError.Visibility = Visibility.Visible;
            return;
        }

        _parsedEntries.AddRange(entries);

        foreach (var entry in entries)
        {
            var fields = new[] { entry.Minute, entry.Hour, entry.DayOfMonth, entry.Month, entry.DayOfWeek };
            var description = DescribeCron(fields);
            var nextRuns = CalculateNextRuns(fields, 1);
            var nextRun = nextRuns.Count > 0 ? nextRuns[0] : "—";

            _cronEntries.Add(new CrontabDisplayEntry(
                $"{entry.Minute} {entry.Hour} {entry.DayOfMonth} {entry.Month} {entry.DayOfWeek}",
                entry.Command,
                nextRun,
                description));
        }

        TxtStatus.Text = string.Format(L("ToolCronJobStatusParsed"), entries.Count);
    }

    private void OnClearPasteClick(object sender, RoutedEventArgs e)
    {
        TxtCrontabInput.Text = string.Empty;
        _cronEntries.Clear();
        _parsedEntries.Clear();
        TxtCronError.Visibility = Visibility.Collapsed;
        CronDetailPanel.Visibility = Visibility.Collapsed;
        TxtStatus.Text = string.Empty;
    }

    private void OnCronEntrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (CronResultsGrid.SelectedItem is not CrontabDisplayEntry selected)
        {
            CronDetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var index = _cronEntries.IndexOf(selected);
        if (index < 0 || index >= _parsedEntries.Count)
        {
            CronDetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var parsed = _parsedEntries[index];
        var fields = new[] { parsed.Minute, parsed.Hour, parsed.DayOfMonth, parsed.Month, parsed.DayOfWeek };
        var nextRuns = CalculateNextRuns(fields, NextRunsCount);
        var nextRunsText = nextRuns.Count > 0 ? string.Join("\n", nextRuns) : "—";

        TxtDetailHeader.Text = L("ToolCronJobDetailHeader");
        TxtDetailSchedule.Text = string.Format(
            L("ToolCronJobDetailSchedule"),
            $"{parsed.Minute} {parsed.Hour} {parsed.DayOfMonth} {parsed.Month} {parsed.DayOfWeek}");
        TxtDetailCommand.Text = string.Format(L("ToolCronJobDetailCommand"), parsed.Command);
        TxtDetailDescription.Text = string.Format(L("ToolCronJobDetailNextRuns"), nextRunsText);
        CronDetailPanel.Visibility = Visibility.Visible;
    }

    // ── Windows Tasks Mode ──────────────────────────────────────

    private void OnRefreshTasksClick(object sender, RoutedEventArgs e)
    {
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        _ = LoadWindowsTasksAsync();
    }

    private async Task LoadWindowsTasksAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(TimeSpan.FromSeconds(30));

        _isLoading = true;
        BtnRefreshTasks.IsEnabled = false;
        _setBusy?.Invoke(true);
        LoadingBar.Visibility = Visibility.Visible;
        TxtTasksLoading.Text = L("ToolCronJobTasksLoading");
        TxtTasksLoading.Visibility = Visibility.Visible;
        TxtTasksError.Visibility = Visibility.Collapsed;
        _taskEntries.Clear();

        try
        {
            var tasks = await GetWindowsTasksAsync(_cts.Token);

            if (_cts.IsCancellationRequested) return;

            foreach (var task in tasks)
            {
                _taskEntries.Add(task);
            }

            TxtStatus.Text = string.Format(L("ToolCronJobStatusTasks"), tasks.Count);
        }
        catch (OperationCanceledException)
        {
            TxtTasksError.Text = L("ToolCronJobErrorTimeout");
            TxtTasksError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            TxtTasksError.Text = string.Format(L("ToolCronJobErrorFailed"), ex.Message);
            TxtTasksError.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoading = false;
            _setBusy?.Invoke(false);
            LoadingBar.Visibility = Visibility.Collapsed;
            BtnRefreshTasks.IsEnabled = true;
            TxtTasksLoading.Visibility = Visibility.Collapsed;
        }
    }

    private static async Task<List<WindowsTaskEntry>> GetWindowsTasksAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = "/query /fo CSV /v",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var proc = Process.Start(psi);
        if (proc is null) return [];

        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return ParseSchtasksCsv(output);
    }

    private static List<WindowsTaskEntry> ParseSchtasksCsv(string csv)
    {
        var results = new List<WindowsTaskEntry>();
        if (string.IsNullOrWhiteSpace(csv)) return results;

        var lines = csv.Split('\n');
        if (lines.Length < 2) return results;

        // Parse CSV header to find column indices
        var header = ParseCsvLine(lines[0]);
        var nameIdx = FindColumnIndex(header, "TaskName");
        var statusIdx = FindColumnIndex(header, "Status");
        var nextRunIdx = FindColumnIndex(header, "Next Run Time");
        var lastRunIdx = FindColumnIndex(header, "Last Run Time");
        var lastResultIdx = FindColumnIndex(header, "Last Result");

        if (nameIdx < 0) return results;

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var fields = ParseCsvLine(line);

            var name = GetField(fields, nameIdx);
            if (string.IsNullOrEmpty(name)) continue;

            results.Add(new WindowsTaskEntry(
                name,
                GetField(fields, statusIdx),
                GetField(fields, nextRunIdx),
                GetField(fields, lastRunIdx),
                GetField(fields, lastResultIdx)));
        }

        return results;
    }

    private static int FindColumnIndex(List<string> header, string columnName)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (header[i].Contains(columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string GetField(List<string> fields, int index)
    {
        return index >= 0 && index < fields.Count ? fields[index] : string.Empty;
    }

    /// <summary>
    /// Parses a single CSV line with proper quote handling.
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }

    // ── Crontab Parsing ─────────────────────────────────────────

    private static List<CrontabParsedEntry> ParseCrontab(string content)
    {
        var entries = new List<CrontabParsedEntry>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

            // Skip variable assignments (NAME=value without spaces before =)
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx > 0 && !trimmed[..eqIdx].Contains(' '))
                continue;

            var parts = trimmed.Split([' ', '\t'], 6, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 6)
            {
                entries.Add(new CrontabParsedEntry(
                    parts[0], parts[1], parts[2], parts[3], parts[4],
                    string.Join(" ", parts[5..]), trimmed));
            }
        }
        return entries;
    }

    // ── Cron Description (reused from CrontabBuilderView pattern) ──

    private string DescribeCron(string[] fields)
    {
        var minute = fields[0];
        var hour = fields[1];
        var dom = fields[2];
        var month = fields[3];
        var dow = fields[4];

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

        var validMinutes = ExpandField(fields[0], 0, 59);
        var validHours = ExpandField(fields[1], 0, 23);
        var validDoms = ExpandField(fields[2], 1, 31);
        var validMonths = ExpandField(fields[3], 1, 12);
        var validDows = ExpandField(fields[4], 0, 6);

        if (validMinutes == null || validHours == null || validDoms == null || validMonths == null || validDows == null)
            return results;

        current = current.AddMinutes(1);
        current = new DateTime(current.Year, current.Month, current.Day, current.Hour, current.Minute, 0);

        var maxIterations = 525960;
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

        foreach (var part in field.Split(','))
        {
            if (part.Contains('/'))
            {
                var stepParts = part.Split('/');
                if (stepParts.Length != 2 || !int.TryParse(stepParts[1], out var step) || step <= 0) return null;
                var start = stepParts[0] == "*" ? min : int.TryParse(stepParts[0], out var s) ? s : min;
                for (var i = start; i <= max; i += step) values.Add(i);
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length != 2 ||
                    !int.TryParse(rangeParts[0], out var rangeStart) ||
                    !int.TryParse(rangeParts[1], out var rangeEnd)) return null;
                for (var i = rangeStart; i <= rangeEnd; i++) values.Add(i);
            }
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

    // ── Copy ────────────────────────────────────────────────────

    private void OnCopyAllClick(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();

        if (ModeTabControl.SelectedItem == TabPasteCrontab)
        {
            foreach (var entry in _cronEntries)
            {
                sb.AppendLine($"{entry.Schedule}\t{entry.Command}\t{entry.NextRun}\t{entry.Description}");
            }
        }
        else
        {
            foreach (var task in _taskEntries)
            {
                sb.AppendLine($"{task.Name}\t{task.Status}\t{task.NextRun}\t{task.LastRun}\t{task.LastResult}");
            }
        }

        if (sb.Length > 0)
        {
            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpCRONJOB");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isLoading;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _setBusy?.Invoke(false);
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }

    // ── Data Models ─────────────────────────────────────────────

    private sealed record CrontabParsedEntry(
        string Minute, string Hour, string DayOfMonth, string Month, string DayOfWeek,
        string Command, string RawLine);

    private sealed record CrontabDisplayEntry(
        string Schedule, string Command, string NextRun, string Description);

    private sealed record WindowsTaskEntry(
        string Name, string Status, string NextRun, string LastRun, string LastResult);
}
