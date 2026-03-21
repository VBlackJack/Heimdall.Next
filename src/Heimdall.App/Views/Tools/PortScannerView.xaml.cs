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
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Port scanner tool that probes TCP ports on a target host
/// and displays results with service identification.
/// </summary>
public partial class PortScannerView : UserControl, IToolView
{
    private const int ConnectTimeoutMs = 2000;
    private const int BannerGrabTimeoutMs = 1000;
    private const int MaxConcurrent = 50;
    private const int LargePortCountWarningThreshold = 10000;

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _isScanning;
    private bool _disposed;

    private readonly ObservableCollection<PortScanResult> _results = [];
    private readonly List<PortScanResult> _allResults = [];

    private static readonly Dictionary<int, string> WellKnownServices = new()
    {
        [DefaultPorts.Ftp] = "FTP",
        [DefaultPorts.Ssh] = "SSH",
        [DefaultPorts.Telnet] = "Telnet",
        [25] = "SMTP",
        [53] = "DNS",
        [DefaultPorts.Tftp] = "TFTP",
        [80] = "HTTP",
        [110] = "POP3",
        [143] = "IMAP",
        [443] = "HTTPS",
        [465] = "SMTPS",
        [587] = "SMTP (Submission)",
        [993] = "IMAPS",
        [995] = "POP3S",
        [1433] = "MSSQL",
        [1521] = "Oracle",
        [3306] = "MySQL",
        [DefaultPorts.Rdp] = "RDP",
        [5432] = "PostgreSQL",
        [DefaultPorts.Vnc] = "VNC",
        [6379] = "Redis",
        [DefaultPorts.Http] = "HTTP-Alt",
        [8443] = "HTTPS-Alt",
        [9090] = "Prometheus",
        [27017] = "MongoDB",
    };

    public PortScannerView()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        TxtHost.KeyDown += OnHostKeyDown;
        TxtPorts.KeyDown += OnHostKeyDown;
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        // Pre-fill with sensible defaults; context overrides if provided
        TxtHost.Text = "localhost";
        TxtPorts.Text = "22,80,443,3389,5900";

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHost.Text = context.TargetHost;
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHost.Focus();
            TxtHost.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolPortScanTitle");
        BtnScan.Content = L("ToolPortScanBtnStart");
        BtnCopy.Content = L("ToolPortScanBtnCopy");
        LblOpen.Text = L("ToolPortScanOpen");
        LblClosed.Text = L("ToolPortScanClosed");
        LblTotal.Text = L("ToolPortScanTotal");

        ColPort.Header = L("ToolPortScanColPort");
        ColStatus.Header = L("ToolPortScanColStatus");
        ColService.Header = L("ToolPortScanColService");
        ColResponseTime.Header = L("ToolPortScanColResponseTime");
        ColBanner.Header = L("ToolPortScanColBanner");
        BtnExportCsv.Content = L("ToolPortScanBtnExportCsv");

        System.Windows.Automation.AutomationProperties.SetName(BtnScan, L("ToolPortScanBtnStart"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolPortScanBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHost, L("ToolPortScanHostLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtPorts, L("ToolPortScanPortsLabel"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
        System.Windows.Automation.AutomationProperties.SetName(BtnExportCsv, L("ToolPortScanBtnExportCsv"));

        BtnPresetWeb.Content = L("ToolPortScanPresetWeb");
        BtnPresetSshRemote.Content = L("ToolPortScanPresetSshRemote");
        BtnPresetDatabase.Content = L("ToolPortScanPresetDatabase");
        BtnPresetCommon.Content = L("ToolPortScanPresetCommon");
        BtnPresetFull.Content = L("ToolPortScanPresetFull");
        ChkOpenOnly.Content = L("ToolPortScanChkOpenOnly");

        System.Windows.Automation.AutomationProperties.SetName(ChkOpenOnly, L("ToolPortScanChkOpenOnly"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetWeb, L("ToolPortScanPresetWeb"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetSshRemote, L("ToolPortScanPresetSshRemote"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetDatabase, L("ToolPortScanPresetDatabase"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetCommon, L("ToolPortScanPresetCommon"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetFull, L("ToolPortScanPresetFull"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpPORTSCAN");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnHostKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ToggleScan();
            e.Handled = true;
        }
    }

    private void OnScanClick(object sender, RoutedEventArgs e)
    {
        ToggleScan();
    }

    private void ToggleScan()
    {
        if (_isScanning)
        {
            StopScan();
        }
        else
        {
            _ = StartScanAsync();
        }
    }

    private async Task StartScanAsync()
    {
        var host = TxtHost.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(host))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        var ports = ParsePorts(TxtPorts.Text.Trim());
        if (ports.Count == 0)
        {
            TxtError.Text = L("ToolValidationPortRangeRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (ports.Count > LargePortCountWarningThreshold)
        {
            var message = string.Format(L("ToolPortScanLargeRangeWarning"), ports.Count);
            var result = MessageBox.Show(
                message,
                L("ToolPortScanTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _results.Clear();
        _allResults.Clear();
        _cts = new CancellationTokenSource();
        _isScanning = true;
        BtnScan.Content = L("ToolPortScanBtnStop");
        BtnScan.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
        BtnScan.Style = (Style)FindResource("SecondaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnScan, L("ToolPortScanBtnStop"));
        TxtHost.IsReadOnly = true;
        TxtPorts.IsReadOnly = true;
        ScanProgress.IsIndeterminate = false;
        ScanProgress.Maximum = ports.Count;
        ScanProgress.Value = 0;
        TxtProgressPercent.Text = "0%";
        TxtProgressCount.Text = string.Format(L("ToolPortScanProgressCount"), 0, ports.Count);
        ProgressPanel.Visibility = Visibility.Visible;

        var openCount = 0;
        var closedCount = 0;
        var completed = 0;

        TxtTotal.Text = ports.Count.ToString();

        var semaphore = new SemaphoreSlim(MaxConcurrent);
        var ct = _cts.Token;

        var tasks = ports.Select(async port =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                var probeResult = await ProbePortAsync(host, port, ct);

                await Dispatcher.InvokeAsync(() =>
                {
                    var statusText = probeResult.IsOpen
                        ? L("ToolPortScanStatusOpen")
                        : L("ToolPortScanStatusClosed");
                    var result = new PortScanResult(
                        probeResult.Port, probeResult.IsOpen, probeResult.Service,
                        probeResult.ResponseTime, statusText, probeResult.Banner ?? "");
                    _allResults.Add(result);
                    completed++;
                    ScanProgress.Value = completed;
                    var percent = (int)(completed * 100.0 / ScanProgress.Maximum);
                    TxtProgressPercent.Text = $"{percent}%";
                    TxtProgressCount.Text = string.Format(
                        L("ToolPortScanProgressCount"), completed, (int)ScanProgress.Maximum);

                    if (result.IsOpen)
                    {
                        openCount++;
                    }
                    else
                    {
                        closedCount++;
                    }

                    // Apply filter: only add to visible collection if matching
                    if (ChkOpenOnly?.IsChecked != true || result.IsOpen)
                    {
                        _results.Add(result);
                    }

                    TxtOpen.Text = openCount.ToString();
                    TxtClosed.Text = closedCount.ToString();
                });
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"PortScanner scan failed: {ex.Message}");
        }

        StopScan();
    }

    private void StopScan()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isScanning = false;
        BtnScan.Content = L("ToolPortScanBtnStart");
        BtnScan.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
        BtnScan.Style = (Style)FindResource("PrimaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnScan, L("ToolPortScanBtnStart"));
        TxtHost.IsReadOnly = false;
        TxtPorts.IsReadOnly = false;
        ProgressPanel.Visibility = Visibility.Collapsed;
    }

    private static async Task<PortProbeResult> ProbePortAsync(string host, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await client.ConnectAsync(host, port, linked.Token);
            sw.Stop();

            var service = WellKnownServices.GetValueOrDefault(port, "");
            var banner = await GrabBannerAsync(client, ct);
            return new PortProbeResult(port, true, service, $"{sw.ElapsedMilliseconds} ms", banner);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            sw.Stop();
            var service = WellKnownServices.GetValueOrDefault(port, "");
            return new PortProbeResult(port, false, service, "\u2014", null);
        }
    }

    /// <summary>
    /// Attempts to read the first line of response from an already-connected TCP client
    /// to identify the service banner.
    /// </summary>
    private static async Task<string?> GrabBannerAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var timeout = new CancellationTokenSource(BannerGrabTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var stream = client.GetStream();
            var buffer = new byte[256];
            var read = await stream.ReadAsync(buffer, linked.Token);
            return read > 0 ? Encoding.ASCII.GetString(buffer, 0, read).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a port specification string supporting comma-separated values and ranges.
    /// Examples: "22,80,443" or "1-1024" or "22,80,443,8000-8100".
    /// </summary>
    private static List<int> ParsePorts(string input)
    {
        var ports = new HashSet<int>();

        foreach (var segment in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.Contains('-'))
            {
                var rangeParts = segment.Split('-', 2);
                if (int.TryParse(rangeParts[0].Trim(), out var start) &&
                    int.TryParse(rangeParts[1].Trim(), out var end) &&
                    start >= 1 && end <= 65535 && start <= end)
                {
                    for (var p = start; p <= end; p++)
                    {
                        ports.Add(p);
                    }
                }
            }
            else if (int.TryParse(segment, out var port) && port >= 1 && port <= 65535)
            {
                ports.Add(port);
            }
        }

        return ports.OrderBy(p => p).ToList();
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string ports)
        {
            TxtPorts.Text = ports;
        }
    }

    private void OnOpenOnlyChanged(object sender, RoutedEventArgs e)
    {
        ApplyOpenOnlyFilter();
    }

    /// <summary>
    /// Applies the "open only" filter to the results grid.
    /// </summary>
    private void ApplyOpenOnlyFilter()
    {
        _results.Clear();

        var filtered = ChkOpenOnly?.IsChecked == true
            ? _allResults.Where(r => r.IsOpen)
            : _allResults.AsEnumerable();

        foreach (var result in filtered)
        {
            _results.Add(result);
        }
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_allResults.Count == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"portscan_{TxtHost.Text.Trim()}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Port,Status,Service,ResponseTime,Banner");

            foreach (var r in _allResults.OrderBy(r => r.Port))
            {
                var banner = r.Banner.Replace("\"", "\"\"");
                sb.AppendLine($"{r.Port},{r.Status},{r.Service},{r.ResponseTime},\"{banner}\"");
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"PortScanner CSV export failed: {ex.Message}");
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{"Port",-8}{"Status",-12}{"Service",-20}{"Response"}");
            sb.AppendLine(new string('-', 52));

            foreach (var r in _results)
            {
                sb.AppendLine($"{r.Port,-8}{r.Status,-12}{r.Service,-20}{r.ResponseTime}");
            }

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"PortScanner clipboard copy failed: {ex.Message}");
        }
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopScan();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents a single port scan result for DataGrid binding.
    /// </summary>
    /// <summary>
    /// Internal result from port probing (before localization).
    /// </summary>
    private sealed record PortProbeResult(int Port, bool IsOpen, string Service, string ResponseTime, string? Banner);

    /// <summary>
    /// Represents a single port scan result for DataGrid binding.
    /// </summary>
    public sealed record PortScanResult(int Port, bool IsOpen, string Service, string ResponseTime, string Status, string Banner);
}
