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
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Service status dashboard for viewing and managing Windows services on the local machine.
/// </summary>
public partial class ServiceStatusView : UserControl, IToolView
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(5);

    private LocalizationManager? _localizer;
    private Action<bool>? _setBusy;
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _autoRefreshTimer;
    private bool _disposed;
    private bool _isLoading;
    private readonly ToolAsyncStateController _viewState;

    private readonly ObservableCollection<ServiceEntry> _displayedServices = [];
    private readonly List<ServiceEntry> _allServices = [];

    public ServiceStatusView()
    {
        InitializeComponent();
        _viewState = new ToolAsyncStateController(
            isBusy => _setBusy?.Invoke(isBusy),
            LoadingBar,
            TxtError,
            EmptyStatePanel,
            ServicesPanel,
            null,
            BtnRefresh,
            TxtFilter,
            ChkRunningOnly,
            ChkAutoRefresh);
        ServicesGrid.ItemsSource = _displayedServices;
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();

        // Auto-load on first display
        _ = LoadServicesAsync();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolServicesTitle");
        TxtFilter.ToolTip = L("ToolServicesFilterTooltip");
        ChkRunningOnly.Content = L("ToolServicesRunningOnly");
        ChkAutoRefresh.Content = L("ToolServicesAutoRefresh");
        BtnRefresh.Content = L("ToolServicesBtnRefresh");
        BtnCopy.Content = L("ToolServicesBtnCopy");

        ColServiceName.Header = L("ToolServicesColName");
        ColDisplayName.Header = L("ToolServicesColDisplayName");
        ColStatus.Header = L("ToolServicesColStatus");
        ColStartType.Header = L("ToolServicesColStartType");

        LblTotal.Text = L("ToolServicesTotal");
        LblRunning.Text = L("ToolServicesRunningLabel");
        LblStopped.Text = L("ToolServicesStoppedLabel");

        MenuStart.Header = L("ToolServicesMenuStart");
        MenuStop.Header = L("ToolServicesMenuStop");
        MenuRestart.Header = L("ToolServicesMenuRestart");

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");

        AutomationProperties.SetName(TxtFilter, L("ToolServicesFilterTooltip"));
        AutomationProperties.SetName(BtnRefresh, L("ToolServicesBtnRefresh"));
        AutomationProperties.SetName(BtnCopy, L("ToolServicesBtnCopy"));
        AutomationProperties.SetName(ChkRunningOnly, L("ToolServicesRunningOnly"));
        AutomationProperties.SetName(ChkAutoRefresh, L("ToolServicesAutoRefresh"));
        AutomationProperties.SetName(ServicesGrid, L("ToolServicesTitle"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
        AutomationProperties.SetName(LoadingBar, L("ToolServicesA11yLoading"));
        TxtEmptyState.Text = L("ToolServiceStatusEmptyState");
    }

    // ── Data Loading ────────────────────────────────────────────

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _ = LoadServicesAsync();
    }

    private async Task LoadServicesAsync()
    {
        if (_disposed || _isLoading)
        {
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(TimeSpan.FromSeconds(30));

        _isLoading = true;
        _viewState.Begin();

        try
        {
            var services = await GetServicesAsync(_cts.Token);

            if (_cts.IsCancellationRequested) return;

            _allServices.Clear();
            _allServices.AddRange(services);
            ApplyFilter();
            if (_allServices.Count == 0)
            {
                _viewState.Reset();
            }
            else
            {
                _viewState.ShowResults();
            }
        }
        catch (OperationCanceledException)
        {
            _viewState.ShowError(
                L("ToolServicesErrorTimeout"),
                showEmptyState: _allServices.Count == 0,
                keepResultsVisible: _allServices.Count > 0);
        }
        catch (Exception ex)
        {
            _viewState.ShowError(
                string.Format(L("ToolServicesErrorFailed"), ex.Message),
                showEmptyState: _allServices.Count == 0,
                keepResultsVisible: _allServices.Count > 0);
        }
        finally
        {
            _isLoading = false;
            _viewState.End();
        }
    }

    private static async Task<List<ServiceEntry>> GetServicesAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -Command \"Get-Service | Select-Object Name,DisplayName,Status,StartType | ConvertTo-Csv -NoTypeInformation\"",
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

        return ParseServiceCsv(output);
    }

    private static List<ServiceEntry> ParseServiceCsv(string csv)
    {
        var results = new List<ServiceEntry>();
        if (string.IsNullOrWhiteSpace(csv)) return results;

        var lines = csv.Split('\n');
        if (lines.Length < 2) return results;

        // Skip header line (first line is column names)
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var fields = ParseCsvLine(line);
            if (fields.Count < 4) continue;

            results.Add(new ServiceEntry(fields[0], fields[1], fields[2], fields[3]));
        }

        return results.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
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

    // ── Filtering ───────────────────────────────────────────────

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void OnRunningOnlyChanged(object sender, RoutedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = TxtFilter.Text.Trim();
        var runningOnly = ChkRunningOnly.IsChecked == true;

        _displayedServices.Clear();

        var totalCount = _allServices.Count;
        var runningCount = 0;
        var stoppedCount = 0;

        foreach (var svc in _allServices)
        {
            if (svc.Status.Contains("Running", StringComparison.OrdinalIgnoreCase))
                runningCount++;
            else if (svc.Status.Contains("Stopped", StringComparison.OrdinalIgnoreCase))
                stoppedCount++;
        }

        foreach (var svc in _allServices)
        {
            if (runningOnly && !svc.Status.Contains("Running", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(filter) &&
                !svc.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !svc.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            _displayedServices.Add(svc);
        }

        TxtTotal.Text = totalCount.ToString();
        TxtRunning.Text = runningCount.ToString();
        TxtStopped.Text = stoppedCount.ToString();
    }

    // ── Auto Refresh ────────────────────────────────────────────

    private void OnAutoRefreshChanged(object sender, RoutedEventArgs e)
    {
        if (ChkAutoRefresh.IsChecked == true)
        {
            _autoRefreshTimer ??= new DispatcherTimer { Interval = AutoRefreshInterval };
            _autoRefreshTimer.Tick += OnAutoRefreshTick;
            _autoRefreshTimer.Start();
        }
        else
        {
            if (_autoRefreshTimer is not null)
            {
                _autoRefreshTimer.Stop();
                _autoRefreshTimer.Tick -= OnAutoRefreshTick;
            }
        }
    }

    private void OnAutoRefreshTick(object? sender, EventArgs e)
    {
        _ = LoadServicesAsync();
    }

    // ── Service Actions ─────────────────────────────────────────

    private void OnStartServiceClick(object sender, RoutedEventArgs e)
    {
        ExecuteServiceAction("Start-Service");
    }

    private void OnStopServiceClick(object sender, RoutedEventArgs e)
    {
        ExecuteServiceAction("Stop-Service");
    }

    private void OnRestartServiceClick(object sender, RoutedEventArgs e)
    {
        ExecuteServiceAction("Restart-Service");
    }

    private void ExecuteServiceAction(string command)
    {
        if (ServicesGrid.SelectedItem is not ServiceEntry selected) return;

        var safeName = selected.Name.Replace("'", "''", StringComparison.Ordinal);
        var script = $"{command} '{safeName}'";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -EncodedCommand {encoded}",
                Verb = "runas",
                UseShellExecute = true
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC was declined or not available
        }

        // Refresh after a brief delay to allow the action to complete
        Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            await Task.Delay(2000);
            await LoadServicesAsync();
        });
    }

    // ── Copy ────────────────────────────────────────────────────

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (var svc in _displayedServices)
        {
            sb.AppendLine($"{InputValidator.SanitizeCsvCell(svc.Name)}\t{InputValidator.SanitizeCsvCell(svc.DisplayName)}\t{InputValidator.SanitizeCsvCell(svc.Status)}\t{InputValidator.SanitizeCsvCell(svc.StartType)}");
        }

        if (sb.Length > 0)
        {
            try { Clipboard.SetText(sb.ToString()); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpSERVICES").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isLoading;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _setBusy?.Invoke(false);

        if (_autoRefreshTimer is not null)
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Tick -= OnAutoRefreshTick;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }

    // ── Data Model ──────────────────────────────────────────────

    private sealed record ServiceEntry(string Name, string DisplayName, string Status, string StartType);
}
