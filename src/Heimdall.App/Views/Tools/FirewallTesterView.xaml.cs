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

using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Ssh;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Firewall rule tester that probes TCP connectivity across a matrix of
/// destinations and ports, displaying results as a color-coded heatmap grid.
/// </summary>
public partial class FirewallTesterView : UserControl, IToolView
{
    private const int ConnectTimeoutMs = 3000;
    private const int MaxConcurrent = 20;
    private const int MaxHosts = 50;
    private const int MaxPorts = 50;

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _isTesting;
    private bool _disposed;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;
    private Action<bool>? _setBusy;
    private readonly ToolAsyncStateController _viewState;

    private readonly List<FwTestResult> _results = [];

    public FirewallTesterView()
    {
        InitializeComponent();
        _viewState = new ToolAsyncStateController(
            null,
            null,
            TxtError,
            EmptyStatePanel,
            ResultsPanel,
            null);
        TxtPorts.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnTestClick(s, e); };
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();
        TxtPorts.Text = NetworkToolPresets.FirewallTesterDefaultPorts;

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHosts.Text = context.TargetHost;
        }

        // Populate SSH gateway selector for tunnel-based testing
        if (context?.SshGateways is IList gateways)
        {
            _gateways = gateways.Cast<SshGatewayDto>().ToList();
        }
        PopulateRouteSelector();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHosts.Focus();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolFwTitle");
        LblHosts.Text = L("ToolFwHostsLabel");
        LblPorts.Text = L("ToolFwPortsLabel");
        BtnTest.Content = L("ToolFwBtnTest");
        BtnCopy.Content = L("ToolFwBtnCopy");
        BtnExportCsv.Content = L("ToolFwBtnExport");

        BtnPresetWeb.Content = L("ToolFwPresetWeb");
        BtnPresetRemote.Content = L("ToolFwPresetRemote");
        BtnPresetCommon.Content = L("ToolFwPresetCommon");

        LblRouteVia.Text = L("ToolTunnelRouteVia");

        TxtEmptyState.Text = L("ToolFwEmptyState");
        TxtHosts.Tag = L("ToolFwTestHostsPlaceholder");
        TxtPorts.Tag = L("ToolWatermarkPortList");

        LblSummary.Text = "";

        // Accessibility
        System.Windows.Automation.AutomationProperties.SetName(BtnTest, L("ToolFwBtnTest"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolFwBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnExportCsv, L("ToolFwBtnExport"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHosts, L("ToolFwHostsLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtPorts, L("ToolFwPortsLabel"));
        System.Windows.Automation.AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetWeb, L("ToolFwPresetWeb"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetRemote, L("ToolFwPresetRemote"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetCommon, L("ToolFwPresetCommon"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(TestProgress, L("ToolFwA11yProgress"));
        System.Windows.Automation.AutomationProperties.SetName(HeatmapGrid, L("ToolFwTitle"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpFWTEST").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (_isTesting)
        {
            StopTest();
        }
        else
        {
            _ = RunTestsAsync();
        }
    }

    private async Task RunTestsAsync()
    {
        if (_isTesting)
        {
            return;
        }

        _viewState.Reset();

        var hosts = ParseHosts(TxtHosts.Text);
        if (hosts.Count == 0)
        {
            _viewState.ShowError(L("ToolFwErrorNoHosts"));
            return;
        }

        hosts = hosts.Where(h => InputValidator.Validate(h, "Address")).ToList();
        if (hosts.Count == 0)
        {
            _viewState.ShowError(L("ErrorInvalidHost"));
            return;
        }

        var ports = ParsePorts(TxtPorts.Text.Trim());
        if (ports.Count == 0)
        {
            _viewState.ShowError(L("ToolFwErrorNoPorts"));
            return;
        }

        // Cap matrix size to prevent excessive resource usage
        if (hosts.Count > MaxHosts)
        {
            hosts = hosts.Take(MaxHosts).ToList();
        }
        if (ports.Count > MaxPorts)
        {
            ports = ports.Take(MaxPorts).ToList();
        }

        _results.Clear();
        _cts = new CancellationTokenSource();
        _isTesting = true;

        var total = hosts.Count * ports.Count;

        try
        {
            _setBusy?.Invoke(true);
            BtnTest.Content = L("ToolFwBtnStop");
            BtnTest.Foreground = (Brush)FindResource("ErrorBrush");
            BtnTest.Style = (Style)FindResource("SecondaryButtonStyle");
            System.Windows.Automation.AutomationProperties.SetName(BtnTest, L("ToolFwBtnStop"));
            SetTestInputsEnabled(false);
            TestProgress.IsIndeterminate = false;
            TestProgress.Maximum = total;
            TestProgress.Value = 0;
            TxtProgressPercent.Text = "0%";
            TxtProgressCount.Text = string.Format(L("ToolFwProgress"), 0, total);
            ProgressPanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Collapsed;
        }
        catch
        {
            _isTesting = false;
            throw;
        }

        Renci.SshNet.SshClient? tunnelClient = null;
        if (_selectedGateway is not null)
        {
            try
            {
                tunnelClient = ToolGatewayConnector.Connect(_selectedGateway);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"FirewallTester gateway connection failed: {ex.Message}");
                StopTest();
                _viewState.ShowError(string.Format(L("ToolTunnelFailed"), ex.Message));
                return;
            }
        }

        var completed = 0;
        var semaphore = new SemaphoreSlim(tunnelClient is not null ? 10 : MaxConcurrent);
        var ct = _cts.Token;

        try
        {
            var tasks = new List<Task>();
            foreach (var host in hosts)
            {
                foreach (var port in ports)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(ct);
                        try
                        {
                            ct.ThrowIfCancellationRequested();
                            var (status, responseMs) = tunnelClient is not null
                                ? await ProbeViaTunnelAsync(tunnelClient, host, port, ct)
                                : await ProbeDirectAsync(host, port, ct);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                _results.Add(new FwTestResult
                                {
                                    Host = host,
                                    Port = port,
                                    Status = status,
                                    ResponseTimeMs = responseMs,
                                });

                                var done = Interlocked.Increment(ref completed);
                                TestProgress.Value = done;
                                var percent = (int)(done * 100.0 / total);
                                TxtProgressPercent.Text = $"{percent}%";
                                TxtProgressCount.Text = string.Format(L("ToolFwProgress"), done, total);
                            });
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Test was cancelled by user
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"FirewallTester test failed: {ex.Message}");
            }
        }
        finally
        {
            semaphore.Dispose();

            if (tunnelClient is not null)
            {
                try { tunnelClient.Disconnect(); } catch { /* best effort */ }
                tunnelClient.Dispose();
            }
        }

        if (_results.Count > 0)
        {
            BuildHeatmap(hosts, ports);
            UpdateSummary();
        }
        else
        {
            _viewState.Reset();
            LblSummary.Text = string.Empty;
        }

        StopTest();
    }

    private void StopTest()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isTesting = false;
        _setBusy?.Invoke(false);
        BtnTest.Content = L("ToolFwBtnTest");
        BtnTest.Foreground = (Brush)FindResource("TextPrimaryBrush");
        BtnTest.Style = (Style)FindResource("PrimaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnTest, L("ToolFwBtnTest"));
        SetTestInputsEnabled(true);
        ProgressPanel.Visibility = Visibility.Collapsed;
    }

    private void SetTestInputsEnabled(bool enabled)
    {
        TxtHosts.IsReadOnly = !enabled;
        TxtPorts.IsReadOnly = !enabled;
        CmbRouteVia.IsEnabled = enabled;
        BtnPresetWeb.IsEnabled = enabled;
        BtnPresetRemote.IsEnabled = enabled;
        BtnPresetCommon.IsEnabled = enabled;
    }

    /// <summary>
    /// Probes a single host:port directly via TCP connect.
    /// Returns the status string ("open", "closed", or "timeout") and response time.
    /// </summary>
    private static async Task<(string Status, long ResponseMs)> ProbeDirectAsync(
        string host, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await client.ConnectAsync(host, port, linked.Token);
            sw.Stop();
            return ("open", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Timeout expired (inner CTS), not user cancellation
            sw.Stop();
            return ("timeout", sw.ElapsedMilliseconds);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            sw.Stop();
            return ("closed", sw.ElapsedMilliseconds);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
        {
            sw.Stop();
            return ("timeout", sw.ElapsedMilliseconds);
        }
        catch
        {
            sw.Stop();
            return ("closed", sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Probes a single host:port remotely via an SSH gateway using /dev/tcp bash built-in.
    /// </summary>
    private static async Task<(string Status, long ResponseMs)> ProbeViaTunnelAsync(
        Renci.SshNet.SshClient sshClient, string host, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await Task.Run(() =>
            {
                var safeHost = InputValidator.EscapeShellArg(host);
                using var cmd = sshClient.CreateCommand(
                    $"(echo >/dev/tcp/{safeHost}/{port}) 2>/dev/null && echo OPEN || echo CLOSED");
                cmd.CommandTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs);
                cmd.Execute();
                return cmd.Result?.Trim();
            }, ct).ConfigureAwait(false);

            sw.Stop();
            var isOpen = string.Equals(result, "OPEN", StringComparison.OrdinalIgnoreCase);
            return (isOpen ? "open" : "closed", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            sw.Stop();
            return ("timeout", sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Builds the heatmap grid dynamically with colored cells for each host:port result.
    /// </summary>
    private void BuildHeatmap(List<string> hosts, List<int> ports)
    {
        HeatmapGrid.Children.Clear();
        HeatmapGrid.RowDefinitions.Clear();
        HeatmapGrid.ColumnDefinitions.Clear();

        var successBrush = FindResource("SuccessBrush") as Brush ?? Brushes.Green;
        var errorBrush = FindResource("ErrorBrush") as Brush ?? Brushes.Red;
        var disabledBrush = FindResource("TextDisabledBrush") as Brush ?? Brushes.Gray;
        var textPrimary = FindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
        var textSecondary = FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        // Host column + one column per port
        HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        foreach (var _ in ports)
        {
            HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        }

        // Header row with port numbers
        HeatmapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Empty top-left corner
        var cornerBlock = new TextBlock
        {
            Text = "",
            Margin = new Thickness(2),
        };
        Grid.SetRow(cornerBlock, 0);
        Grid.SetColumn(cornerBlock, 0);
        HeatmapGrid.Children.Add(cornerBlock);

        for (var c = 0; c < ports.Count; c++)
        {
            var header = new TextBlock
            {
                Text = ports[c].ToString(),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = (double)FindResource("FontSizeCaption"),
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontFamilyMonospace"),
                FontWeight = FontWeights.SemiBold,
                Foreground = textPrimary,
                Margin = new Thickness(2, 4, 2, 4),
            };
            System.Windows.Automation.AutomationProperties.SetName(
                header, string.Format(L("ToolFwA11yPort"), ports[c]));
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, c + 1);
            HeatmapGrid.Children.Add(header);
        }

        // Data rows: one per host
        for (var r = 0; r < hosts.Count; r++)
        {
            HeatmapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Host label
            var hostLabel = new TextBlock
            {
                Text = hosts[r],
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = (double)FindResource("FontSizeCaption"),
                FontFamily = (System.Windows.Media.FontFamily)FindResource("FontFamilyMonospace"),
                Foreground = textSecondary,
                Margin = new Thickness(4, 2, 8, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 150,
            };
            hostLabel.ToolTip = hosts[r];
            System.Windows.Automation.AutomationProperties.SetName(
                hostLabel, hosts[r]);
            Grid.SetRow(hostLabel, r + 1);
            Grid.SetColumn(hostLabel, 0);
            HeatmapGrid.Children.Add(hostLabel);

            // Result cells
            for (var c = 0; c < ports.Count; c++)
            {
                var host = hosts[r];
                var port = ports[c];
                var result = _results.FirstOrDefault(x => x.Host == host && x.Port == port);

                var cellBrush = result?.Status switch
                {
                    "open" => successBrush,
                    "closed" => errorBrush,
                    "timeout" => disabledBrush,
                    _ => disabledBrush,
                };

                var tooltipText = result?.Status switch
                {
                    "open" => $"{L("ToolFwStatusOpen")} — {result.ResponseTimeMs} ms",
                    "closed" => L("ToolFwStatusClosed"),
                    "timeout" => L("ToolFwStatusTimeout"),
                    _ => L("ToolFwStatusTimeout"),
                };

                var localizedStatus = result?.Status switch
                {
                    "open" => L("ToolFwStatusOpen"),
                    "closed" => L("ToolFwStatusClosed"),
                    _ => L("ToolFwStatusTimeout"),
                };
                var automationName = string.Format(L("ToolFwA11yCell"), host, port, localizedStatus);

                var cell = new Border
                {
                    Background = cellBrush,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(1),
                    MinHeight = 24,
                    MinWidth = 24,
                    ToolTip = tooltipText,
                };
                System.Windows.Automation.AutomationProperties.SetName(cell, automationName);
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c + 1);
                HeatmapGrid.Children.Add(cell);
            }
        }

        _viewState.ShowResults();
    }

    /// <summary>
    /// Updates the summary label with counts of open, blocked, and timeout results.
    /// </summary>
    private void UpdateSummary()
    {
        var openCount = _results.Count(r => r.Status == "open");
        var closedCount = _results.Count(r => r.Status == "closed");
        var timeoutCount = _results.Count(r => r.Status == "timeout");
        var total = _results.Count;

        LblSummary.Text = string.Format(L("ToolFwSummary"), openCount, closedCount, timeoutCount, total);
    }

    private void PopulateRouteSelector()
    {
        CmbRouteVia.Items.Clear();
        CmbRouteVia.Items.Add(new ComboBoxItem { Content = L("ToolTunnelDirect") });

        if (_gateways is not null)
        {
            foreach (var gw in _gateways)
            {
                var label = $"{gw.Name} ({gw.Host}:{gw.Port})";
                CmbRouteVia.Items.Add(new ComboBoxItem { Content = label, Tag = gw });
            }
        }

        CmbRouteVia.SelectedIndex = 0;
    }

    private void OnRouteViaChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbRouteVia.SelectedItem is ComboBoxItem item && item.Tag is SshGatewayDto gw)
        {
            _selectedGateway = gw;
        }
        else
        {
            _selectedGateway = null;
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string ports)
        {
            TxtPorts.Text = ports;
        }
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = L("FileDialogCsvFilter"),
            FileName = $"firewall_test_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var hosts = _results.Select(r => r.Host).Distinct().ToList();
            var ports = _results.Select(r => r.Port).Distinct().OrderBy(p => p).ToList();

            var sb = new StringBuilder();

            // Header: Host,Port1,Port2,...
            sb.Append(L("ToolFwColHost"));
            foreach (var port in ports)
            {
                sb.Append($",{port}");
            }
            sb.AppendLine();

            // Data rows
            foreach (var host in hosts)
            {
                sb.Append($"\"{InputValidator.SanitizeCsvCell(host)}\"");
                foreach (var port in ports)
                {
                    var result = _results.FirstOrDefault(r => r.Host == host && r.Port == port);
                    var cellValue = result?.Status switch
                    {
                        "open" => $"{L("ToolFwStatusOpen")} ({result.ResponseTimeMs}ms)",
                        "closed" => L("ToolFwStatusClosed"),
                        "timeout" => L("ToolFwStatusTimeout"),
                        _ => L("ToolFwStatusTimeout"),
                    };
                    sb.Append($",{InputValidator.SanitizeCsvCell(cellValue)}");
                }
                sb.AppendLine();
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"FirewallTester CSV export failed: {ex.Message}");
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
            var hosts = _results.Select(r => r.Host).Distinct().ToList();
            var ports = _results.Select(r => r.Port).Distinct().OrderBy(p => p).ToList();

            var sb = new StringBuilder();

            // Header
            sb.Append($"{L("ToolFwColHost"),-30}");
            foreach (var port in ports)
            {
                sb.Append($"{port,10}");
            }
            sb.AppendLine();
            sb.AppendLine(new string('-', 30 + ports.Count * 10));

            // Data rows
            foreach (var host in hosts)
            {
                sb.Append($"{host,-30}");
                foreach (var port in ports)
                {
                    var result = _results.FirstOrDefault(r => r.Host == host && r.Port == port);
                    var cellValue = result?.Status switch
                    {
                        "open" => L("ToolFwStatusOpen"),
                        "closed" => L("ToolFwStatusClosed"),
                        _ => L("ToolFwStatusTimeout"),
                    };
                    sb.Append($"{cellValue,10}");
                }
                sb.AppendLine();
            }

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"FirewallTester clipboard copy failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a multi-line text into a list of host strings.
    /// Splits by newline, trims whitespace, and filters empty lines.
    /// </summary>
    private static List<string> ParseHosts(string input)
    {
        return input
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.Trim())
            .Where(h => h.Length > 0)
            .Distinct()
            .ToList();
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
                    var remaining = MaxPorts - ports.Count;
                    if (remaining <= 0) break;
                    var rangeEnd = Math.Min(end, start + remaining - 1);
                    for (var p = start; p <= rangeEnd; p++)
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

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isTesting;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopTest();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents a single firewall test result for a host:port combination.
    /// </summary>
    internal sealed class FwTestResult
    {
        public string Host { get; init; } = "";
        public int Port { get; init; }
        public string Status { get; init; } = "";
        public long ResponseTimeMs { get; init; }
    }
}
