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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Heimdall.App.Services;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Ssh;

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
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;
    private Action<string, string, ToolContext?>? _openToolAction;
    private Action<bool>? _setBusy;

    private readonly ObservableCollection<PortScanResult> _results = [];
    private readonly List<PortScanResult> _allResults = [];

    public PortScannerView()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        TxtHost.KeyDown += OnHostKeyDown;
        TxtPorts.KeyDown += OnHostKeyDown;
        ResultsGrid.PreviewMouseRightButtonDown += ToolContextMenuHelper.SelectRowOnRightClick;
        ResultsGrid.ContextMenuOpening += OnResultsContextMenuOpening;
        SizeChanged += OnViewSizeChanged;
        UpdateResultsSurface();
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _openToolAction = ToolContextMenuHelper.GetOpenToolAction(context);
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();

        TxtHost.Clear();
        TxtPorts.Text = NetworkToolPresets.PortScannerDefaultPorts;

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHost.Text = context.TargetHost;
        }

        // Populate SSH gateway selector for tunnel-based scanning
        if (context?.SshGateways is IList gateways)
        {
            _gateways = gateways.Cast<SshGatewayDto>().ToList();
        }
        PopulateRouteSelector();
        UpdateResponsiveLayout(ActualWidth);
        UpdateResultsSurface();

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

        LblHost.Text = L("ToolPortScanHostLabel");
        LblPorts.Text = L("ToolPortScanPortsLabel");

        LblRouteVia.Text = L("ToolTunnelRouteVia");
        System.Windows.Automation.AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));

        TxtEmptyState.Text = L("ToolEmptyStatePortScan");

        TxtHost.Tag = L("ToolWatermarkHostnameOrIp");
        TxtPorts.Tag = L("ToolWatermarkPortList");

        System.Windows.Automation.AutomationProperties.SetName(ScanProgress, L("ToolPortScanA11yProgress"));

        System.Windows.Automation.AutomationProperties.SetName(ResultsGrid, L("ToolPortScanTitle"));
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpPORTSCAN").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
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
        if (_disposed || _isScanning)
        {
            return;
        }

        var host = TxtHost.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(host))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            UpdateResultsSurface();
            return;
        }

        if (!InputValidator.Validate(host, "Address"))
        {
            TxtError.Text = L("ToolValidationInvalidHost");
            TxtError.Visibility = Visibility.Visible;
            UpdateResultsSurface();
            return;
        }

        var ports = ParsePorts(TxtPorts.Text.Trim());
        if (ports.Count == 0)
        {
            TxtError.Text = L("ToolValidationPortRangeRequired");
            TxtError.Visibility = Visibility.Visible;
            UpdateResultsSurface();
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
        try
        {
            _setBusy?.Invoke(true);
            BtnScan.Content = L("ToolPortScanBtnStop");
            BtnScan.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            BtnScan.Style = (Style)FindResource("SecondaryButtonStyle");
            System.Windows.Automation.AutomationProperties.SetName(BtnScan, L("ToolPortScanBtnStop"));
            SetScanInputsEnabled(false);
            ScanProgress.IsIndeterminate = false;
            ScanProgress.Maximum = ports.Count;
            ScanProgress.Value = 0;
            TxtProgressPercent.Text = "0%";
            TxtProgressCount.Text = string.Format(L("ToolPortScanProgressCount"), 0, ports.Count);
            ProgressPanel.Visibility = Visibility.Visible;
            TxtOpen.Text = "0";
            TxtClosed.Text = "0";
            TxtTotal.Text = ports.Count.ToString();
            UpdateResultsSurface();
        }
        catch
        {
            _isScanning = false;
            throw;
        }

        var openCount = 0;
        var closedCount = 0;
        var completed = 0;

        Renci.SshNet.SshClient? tunnelClient = null;
        if (_selectedGateway is not null)
        {
            try
            {
                tunnelClient = ConnectToGateway(_selectedGateway);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"PortScanner gateway connection failed: {ex.Message}");
                TxtError.Text = string.Format(L("ToolTunnelFailed"), ex.Message);
                TxtError.Visibility = Visibility.Visible;
                UpdateResultsSurface();
                StopScan();
                return;
            }
        }

        var semaphore = new SemaphoreSlim(tunnelClient is not null ? 10 : MaxConcurrent);
        var ct = _cts.Token;

        try
        {
            var tasks = ports.Select(async port =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var probeResult = tunnelClient is not null
                        ? await ProbePortViaTunnelAsync(tunnelClient, host, port, ConnectTimeoutMs, ct)
                        : await ProbePortAsync(host, port, ct);

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
                        UpdateResultsSurface();
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
            finally
            {
                if (tunnelClient is not null)
                {
                    try { tunnelClient.Disconnect(); } catch { /* best effort */ }
                    tunnelClient.Dispose();
                }
            }
        }
        finally
        {
            semaphore.Dispose();
        }

        StopScan();
    }

    private void StopScan()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isScanning = false;
        _setBusy?.Invoke(false);
        BtnScan.Content = L("ToolPortScanBtnStart");
        BtnScan.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
        BtnScan.Style = (Style)FindResource("PrimaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnScan, L("ToolPortScanBtnStart"));
        SetScanInputsEnabled(true);
        ProgressPanel.Visibility = Visibility.Collapsed;
        UpdateResultsSurface();
    }

    private void SetScanInputsEnabled(bool enabled)
    {
        TxtHost.IsReadOnly = !enabled;
        TxtPorts.IsReadOnly = !enabled;
        CmbRouteVia.IsEnabled = enabled;
        BtnPresetWeb.IsEnabled = enabled;
        BtnPresetSshRemote.IsEnabled = enabled;
        BtnPresetDatabase.IsEnabled = enabled;
        BtnPresetCommon.IsEnabled = enabled;
        BtnPresetFull.IsEnabled = enabled;
        ChkOpenOnly.IsEnabled = enabled;
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

    /// <summary>
    /// Probes a single port remotely via an SSH gateway using /dev/tcp bash built-in.
    /// Falls back to nc (netcat) if /dev/tcp is unavailable.
    /// </summary>
    private static async Task<PortProbeResult> ProbePortViaTunnelAsync(
        Renci.SshNet.SshClient sshClient, string host, int port, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await Task.Run(() =>
            {
                var safeHost = InputValidator.EscapeShellArg(host);
                // Use 'timeout' to prevent filtered ports from leaving zombie bash
                // processes on the gateway. Explicit bash for /dev/tcp support.
                using var cmd = sshClient.CreateCommand(
                    $"timeout 2 bash -c \"echo >/dev/tcp/{safeHost}/{port}\" 2>/dev/null && echo OPEN || echo CLOSED");
                cmd.CommandTimeout = TimeSpan.FromSeconds(5);
                cmd.Execute();
                return cmd.Result?.Trim();
            }, ct).ConfigureAwait(false);

            sw.Stop();
            var isOpen = string.Equals(result, "OPEN", StringComparison.OrdinalIgnoreCase);
            var service = NetworkToolPresets.GetPortServiceLabel(port);
            return new PortProbeResult(port, isOpen, service, isOpen ? $"{sw.ElapsedMilliseconds} ms" : "\u2014", null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            sw.Stop();
            var service = NetworkToolPresets.GetPortServiceLabel(port);
            return new PortProbeResult(port, false, service, "\u2014", null);
        }
    }

    private static Renci.SshNet.SshClient ConnectToGateway(SshGatewayDto gateway)
        => ToolGatewayConnector.Connect(gateway);

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

            var service = NetworkToolPresets.GetPortServiceLabel(port);
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
            var service = NetworkToolPresets.GetPortServiceLabel(port);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

        UpdateResultsSurface();
    }

    private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        var showBanner = width >= 1100;
        var showResponseTime = width >= 880;
        var showService = width >= 720;

        ColBanner.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
        ColResponseTime.Visibility = showResponseTime ? Visibility.Visible : Visibility.Collapsed;
        ColService.Visibility = showService ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateResultsSurface()
    {
        var hasResults = _results.Count > 0;
        var hasError = TxtError.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(TxtError.Text);

        ResultsPanel.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasResults || _isScanning || hasError
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_allResults.Count == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = L("FileDialogCsvFilter"),
            FileName = $"portscan_{TxtHost.Text.Trim()}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(L("ToolPortScanCsvHeader"));

            foreach (var r in _allResults.OrderBy(r => r.Port))
            {
                var status = InputValidator.SanitizeCsvCell(r.Status);
                var service = InputValidator.SanitizeCsvCell(r.Service);
                var responseTime = InputValidator.SanitizeCsvCell(r.ResponseTime);
                var banner = InputValidator.SanitizeCsvCell(r.Banner).Replace("\"", "\"\"");
                sb.AppendLine($"{r.Port},{status},{service},{responseTime},\"{banner}\"");
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
            sb.AppendLine($"{L("ToolPortScanColPort"),-8}{L("ToolPortScanColStatus"),-12}{L("ToolPortScanColService"),-20}{L("ToolPortScanColResponseTime")}");
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

    private void OnResultsContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not PortScanResult row)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();
        var host = TxtHost.Text.Trim();

        // Copy Port
        var copyPort = new MenuItem { Header = L("ToolCtxCopyPort") };
        copyPort.Click += (_, _) => { try { Clipboard.SetText(row.Port.ToString()); } catch (System.Runtime.InteropServices.ExternalException) { /* clipboard locked */ } };
        menu.Items.Add(copyPort);

        // Copy Service
        if (!string.IsNullOrWhiteSpace(row.Service))
        {
            var copyService = new MenuItem { Header = L("ToolCtxCopyService") };
            copyService.Click += (_, _) => { try { Clipboard.SetText(row.Service); } catch (System.Runtime.InteropServices.ExternalException) { /* clipboard locked */ } };
            menu.Items.Add(copyService);
        }

        // Copy Banner
        if (!string.IsNullOrWhiteSpace(row.Banner))
        {
            var copyBanner = new MenuItem { Header = L("ToolCtxCopyBanner") };
            copyBanner.Click += (_, _) => { try { Clipboard.SetText(row.Banner); } catch (System.Runtime.InteropServices.ExternalException) { /* clipboard locked */ } };
            menu.Items.Add(copyBanner);
        }

        if (_openToolAction is not null && row.IsOpen)
        {
            menu.Items.Add(new Separator());

            // Open Cert Inspector (if TLS port)
            if (row.Port is 443 or 8443 or 636 or 993 or 995)
            {
                var cert = new MenuItem { Header = L("ToolCtxOpenCertInspector") };
                cert.Click += (_, _) => _openToolAction("CERT", L("PaletteToolCert"),
                    new ToolContext(TargetHost: host, TargetPort: row.Port));
                menu.Items.Add(cert);
            }

            // Open in Browser (if HTTP port)
            if (row.Port is 80 or 443 or 8080 or 8443 or 8006 or 5000 or 3000 or 9090)
            {
                var scheme = row.Port is 443 or 8443 ? "https" : "http";
                var url = $"{scheme}://{host}:{row.Port}";
                var browser = new MenuItem { Header = string.Format(L("ToolCtxOpenBrowser"), url) };
                browser.Click += (_, _) =>
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    })?.Dispose();
                };
                menu.Items.Add(browser);
            }
        }

        // Copy row
        menu.Items.Add(new Separator());
        var csvText = $"{row.Port}\t{row.Status}\t{row.Service}\t{row.ResponseTime}\t{row.Banner}";
        menu.Items.Add(ToolContextMenuHelper.BuildCopyRowAction(csvText, _localizer));
        menu.Items.Add(ToolContextMenuHelper.BuildCopyAllAction(ResultsGrid, _localizer));
        menu.Items.Add(new Separator());
        menu.Items.Add(ToolContextMenuHelper.BuildExportCsvAction(ResultsGrid, _localizer));

        ResultsGrid.ContextMenu = menu;
    }

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isScanning;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TxtHost.KeyDown -= OnHostKeyDown;
        TxtPorts.KeyDown -= OnHostKeyDown;
        ResultsGrid.PreviewMouseRightButtonDown -= ToolContextMenuHelper.SelectRowOnRightClick;
        ResultsGrid.ContextMenuOpening -= OnResultsContextMenuOpening;
        SizeChanged -= OnViewSizeChanged;
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
