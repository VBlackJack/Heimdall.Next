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
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
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
/// Banner grabber tool that connects to TCP ports on a target host
/// and displays raw service identification banners.
/// </summary>
public partial class BannerGrabberView : UserControl, IToolView
{
    private const int ConnectTimeoutMs = 2000;
    private const int BannerReadTimeoutMs = 2000;
    private const int BannerMaxBytes = 512;
    private const int MaxConcurrent = 20;

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _isGrabbing;
    private bool _disposed;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;
    private Action<string, string, ToolContext?>? _openToolAction;
    private Action<bool>? _setBusy;

    private readonly ObservableCollection<BannerResult> _results = [];
    private readonly List<BannerResult> _allResults = [];

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
        [3306] = "MySQL",
        [DefaultPorts.Rdp] = "RDP",
        [5432] = "PostgreSQL",
        [DefaultPorts.Vnc] = "VNC",
        [6379] = "Redis",
        [DefaultPorts.Http] = "HTTP-Alt",
        [8443] = "HTTPS-Alt",
        [27017] = "MongoDB",
    };

    /// <summary>
    /// Regex to strip control characters (except newline/tab) from banner text.
    /// </summary>
    private static readonly Regex ControlCharRegex = new(
        @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]",
        RegexOptions.Compiled);

    public BannerGrabberView()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        TxtHost.KeyDown += OnHostKeyDown;
        TxtPorts.KeyDown += OnHostKeyDown;
        ResultsGrid.PreviewMouseRightButtonDown += ToolContextMenuHelper.SelectRowOnRightClick;
        ResultsGrid.ContextMenuOpening += OnResultsContextMenuOpening;
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

        TxtHost.Text = "localhost";
        TxtPorts.Text = "22,80,443,8080";

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHost.Text = context.TargetHost;
        }

        if (context?.SshGateways is IList gateways)
        {
            _gateways = gateways.Cast<SshGatewayDto>().ToList();
        }
        PopulateRouteSelector();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHost.Focus();
            TxtHost.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolBannerTitle");
        BtnGrab.Content = L("ToolBannerBtnGrab");
        BtnCopy.Content = L("ToolBannerBtnCopy");
        BtnExportCsv.Content = L("ToolBannerBtnExport");

        ColPort.Header = L("ToolBannerColPort");
        ColService.Header = L("ToolBannerColService");
        ColBanner.Header = L("ToolBannerColBanner");
        ColResponseTime.Header = L("ToolBannerColTime");

        System.Windows.Automation.AutomationProperties.SetName(BtnGrab, L("ToolBannerBtnGrab"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolBannerBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHost, L("ToolBannerHostLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtPorts, L("ToolBannerPortsLabel"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
        System.Windows.Automation.AutomationProperties.SetName(BtnExportCsv, L("ToolBannerBtnExport"));

        BtnPresetWeb.Content = L("ToolBannerPresetWeb");
        BtnPresetRemote.Content = L("ToolBannerPresetRemote");
        BtnPresetMail.Content = L("ToolBannerPresetMail");
        BtnPresetDatabase.Content = L("ToolBannerPresetDatabase");
        ChkBannerOnly.Content = L("ToolBannerChkBannerOnly");

        System.Windows.Automation.AutomationProperties.SetName(ChkBannerOnly, L("ToolBannerChkBannerOnly"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetWeb, L("ToolBannerPresetWeb"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetRemote, L("ToolBannerPresetRemote"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetMail, L("ToolBannerPresetMail"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetDatabase, L("ToolBannerPresetDatabase"));

        LblRouteVia.Text = L("ToolTunnelRouteVia");
        System.Windows.Automation.AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));

        TxtEmptyState.Text = L("ToolBannerEmptyState");

        TxtHost.Tag = L("ToolWatermarkHostnameOrIp");
        TxtPorts.Tag = L("ToolWatermarkPortList");
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpBANNER");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnHostKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ToggleGrab();
            e.Handled = true;
        }
    }

    private void OnGrabClick(object sender, RoutedEventArgs e)
    {
        ToggleGrab();
    }

    private void ToggleGrab()
    {
        if (_isGrabbing)
        {
            StopGrab();
        }
        else
        {
            _ = StartGrabAsync();
        }
    }

    private async Task StartGrabAsync()
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

        _results.Clear();
        _allResults.Clear();
        _cts = new CancellationTokenSource();
        _isGrabbing = true;
        try
        {
            _setBusy?.Invoke(true);
            BtnGrab.Content = L("ToolBannerBtnStop");
            BtnGrab.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            BtnGrab.Style = (Style)FindResource("SecondaryButtonStyle");
            System.Windows.Automation.AutomationProperties.SetName(BtnGrab, L("ToolBannerBtnStop"));
            TxtHost.IsReadOnly = true;
            TxtPorts.IsReadOnly = true;
            GrabProgress.IsIndeterminate = false;
            GrabProgress.Maximum = ports.Count;
            GrabProgress.Value = 0;
            TxtProgressPercent.Text = "0%";
            TxtProgressCount.Text = string.Format(L("ToolBannerProgress"), 0, ports.Count);
            ProgressPanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
        }
        catch
        {
            _isGrabbing = false;
            throw;
        }

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
                Core.Logging.FileLogger.Warn($"BannerGrabber gateway connection failed: {ex.Message}");
                TxtError.Text = string.Format(L("ToolTunnelFailed"), ex.Message);
                TxtError.Visibility = Visibility.Visible;
                StopGrab();
                return;
            }
        }

        var semaphore = new SemaphoreSlim(tunnelClient is not null ? 10 : MaxConcurrent);
        var ct = _cts.Token;

        var tasks = ports.Select(async port =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                var probeResult = tunnelClient is not null
                    ? await ProbePortViaTunnelAsync(tunnelClient, host, port, ct)
                    : await ProbePortAsync(host, port, ct);

                await Dispatcher.InvokeAsync(() =>
                {
                    var result = new BannerResult
                    {
                        Port = probeResult.Port,
                        Service = probeResult.Service,
                        Banner = probeResult.Banner ?? "",
                        ResponseTime = probeResult.ResponseTime,
                        HasBanner = !string.IsNullOrWhiteSpace(probeResult.Banner),
                    };
                    _allResults.Add(result);
                    completed++;
                    GrabProgress.Value = completed;
                    var percent = (int)(completed * 100.0 / GrabProgress.Maximum);
                    TxtProgressPercent.Text = $"{percent}%";
                    TxtProgressCount.Text = string.Format(
                        L("ToolBannerProgress"), completed, (int)GrabProgress.Maximum);

                    if (ChkBannerOnly?.IsChecked != true || result.HasBanner)
                    {
                        _results.Add(result);
                    }

                    UpdateResultCount();
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
            // Grab was cancelled
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"BannerGrabber scan failed: {ex.Message}");
        }
        finally
        {
            if (tunnelClient is not null)
            {
                try { tunnelClient.Disconnect(); } catch { /* best effort */ }
                tunnelClient.Dispose();
            }
        }

        StopGrab();
    }

    private void StopGrab()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isGrabbing = false;
        _setBusy?.Invoke(false);
        BtnGrab.Content = L("ToolBannerBtnGrab");
        BtnGrab.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
        BtnGrab.Style = (Style)FindResource("PrimaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnGrab, L("ToolBannerBtnGrab"));
        TxtHost.IsReadOnly = false;
        TxtPorts.IsReadOnly = false;
        ProgressPanel.Visibility = Visibility.Collapsed;
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
    /// Probes a single port directly via TCP, reads up to 512 bytes of banner data.
    /// </summary>
    private static async Task<BannerProbeResult> ProbePortAsync(string host, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await client.ConnectAsync(host, port, linked.Token);
            sw.Stop();

            var service = IdentifyService(port, null);
            var banner = await GrabBannerAsync(client, ct);
            var parsed = ParseBanner(banner);
            var enrichedService = IdentifyService(port, parsed);
            return new BannerProbeResult(port, enrichedService, $"{sw.ElapsedMilliseconds} ms", parsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            sw.Stop();
            var service = IdentifyService(port, null);
            return new BannerProbeResult(port, service, "\u2014", null);
        }
    }

    /// <summary>
    /// Reads the first 512 bytes from an already-connected TCP client.
    /// </summary>
    private static async Task<string?> GrabBannerAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var timeout = new CancellationTokenSource(BannerReadTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var stream = client.GetStream();
            var buffer = new byte[BannerMaxBytes];
            var read = await stream.ReadAsync(buffer, linked.Token);
            return read > 0 ? Encoding.ASCII.GetString(buffer, 0, read).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Probes a single port remotely via an SSH gateway using /dev/tcp bash built-in.
    /// First checks connectivity, then attempts to read banner data.
    /// </summary>
    private static async Task<BannerProbeResult> ProbePortViaTunnelAsync(
        Renci.SshNet.SshClient sshClient, string host, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // First check connectivity
            var connectResult = await Task.Run(() =>
            {
                using var cmd = sshClient.CreateCommand(
                    $"(echo >/dev/tcp/{host}/{port}) 2>/dev/null && echo OPEN || echo CLOSED");
                cmd.CommandTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs);
                cmd.Execute();
                return cmd.Result?.Trim();
            }, ct).ConfigureAwait(false);

            if (!string.Equals(connectResult, "OPEN", StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                var service = IdentifyService(port, null);
                return new BannerProbeResult(port, service, "\u2014", null);
            }

            // Attempt banner grab via /dev/tcp
            var bannerRaw = await Task.Run(() =>
            {
                using var cmd = sshClient.CreateCommand(
                    $"timeout 2 bash -c \"cat < /dev/tcp/{host}/{port}\" 2>/dev/null | head -c {BannerMaxBytes}");
                cmd.CommandTimeout = TimeSpan.FromSeconds(5);
                cmd.Execute();
                return cmd.Result?.Trim();
            }, ct).ConfigureAwait(false);

            sw.Stop();
            var banner = ParseBanner(bannerRaw);
            var enrichedService = IdentifyService(port, banner);
            return new BannerProbeResult(port, enrichedService, $"{sw.ElapsedMilliseconds} ms", banner);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            sw.Stop();
            var service = IdentifyService(port, null);
            return new BannerProbeResult(port, service, "\u2014", null);
        }
    }

    /// <summary>
    /// Cleans control characters from raw banner text, preserving printable content.
    /// </summary>
    private static string? ParseBanner(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Replace control chars with spaces, collapse multiple spaces
        var cleaned = ControlCharRegex.Replace(raw, " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// Maps a port to a known service name, enriching with banner content when available.
    /// </summary>
    private static string IdentifyService(int port, string? banner)
    {
        var baseService = WellKnownServices.GetValueOrDefault(port, "");

        if (string.IsNullOrWhiteSpace(banner))
        {
            return baseService;
        }

        var bannerLower = banner.ToLowerInvariant();

        // Enhance service identification from banner content
        if (bannerLower.Contains("openssh"))
        {
            return "OpenSSH";
        }

        if (bannerLower.Contains("dropbear"))
        {
            return "Dropbear SSH";
        }

        if (bannerLower.Contains("apache"))
        {
            return "Apache HTTP";
        }

        if (bannerLower.Contains("nginx"))
        {
            return "nginx";
        }

        if (bannerLower.Contains("microsoft-iis"))
        {
            return "IIS";
        }

        if (bannerLower.Contains("postfix"))
        {
            return "Postfix SMTP";
        }

        if (bannerLower.Contains("exim"))
        {
            return "Exim SMTP";
        }

        if (bannerLower.Contains("dovecot"))
        {
            return "Dovecot";
        }

        if (bannerLower.Contains("mysql"))
        {
            return "MySQL";
        }

        if (bannerLower.Contains("postgresql") || bannerLower.Contains("pgsql"))
        {
            return "PostgreSQL";
        }

        if (bannerLower.Contains("redis"))
        {
            return "Redis";
        }

        if (bannerLower.Contains("mongodb") || bannerLower.Contains("mongod"))
        {
            return "MongoDB";
        }

        if (bannerLower.Contains("proftpd"))
        {
            return "ProFTPD";
        }

        if (bannerLower.Contains("vsftpd"))
        {
            return "vsftpd";
        }

        if (bannerLower.Contains("filezilla"))
        {
            return "FileZilla FTP";
        }

        return baseService;
    }

    private static Renci.SshNet.SshClient ConnectToGateway(SshGatewayDto gateway)
        => ToolGatewayConnector.Connect(gateway);

    /// <summary>
    /// Parses a port specification string supporting comma-separated values and ranges.
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

    private void OnBannerOnlyChanged(object sender, RoutedEventArgs e)
    {
        ApplyBannerOnlyFilter();
    }

    /// <summary>
    /// Applies the "banner only" filter to the results grid.
    /// </summary>
    private void ApplyBannerOnlyFilter()
    {
        _results.Clear();

        var filtered = ChkBannerOnly?.IsChecked == true
            ? _allResults.Where(r => r.HasBanner)
            : _allResults.AsEnumerable();

        foreach (var result in filtered)
        {
            _results.Add(result);
        }

        UpdateResultCount();
    }

    private void UpdateResultCount()
    {
        var withBanner = _allResults.Count(r => r.HasBanner);
        TxtResultCount.Text = $"{withBanner} / {_allResults.Count}";
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
            FileName = $"banners_{TxtHost.Text.Trim()}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Port,Service,Banner,ResponseTime");

            foreach (var r in _allResults.OrderBy(r => r.Port))
            {
                var banner = r.Banner.Replace("\"", "\"\"");
                sb.AppendLine($"{r.Port},{r.Service},\"{banner}\",{r.ResponseTime}");
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"BannerGrabber CSV export failed: {ex.Message}");
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
            sb.AppendLine($"{"Port",-8}{"Service",-20}{"Time",-12}{"Banner"}");
            sb.AppendLine(new string('-', 72));

            foreach (var r in _results)
            {
                sb.AppendLine($"{r.Port,-8}{r.Service,-20}{r.ResponseTime,-12}{r.Banner}");
            }

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"BannerGrabber clipboard copy failed: {ex.Message}");
        }
    }

    private void OnResultsContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not BannerResult row)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();
        var host = TxtHost.Text.Trim();

        // Copy Port
        var copyPort = new MenuItem { Header = L("ToolCtxCopyPort") };
        copyPort.Click += (_, _) => Clipboard.SetText(row.Port.ToString());
        menu.Items.Add(copyPort);

        // Copy Service
        if (!string.IsNullOrWhiteSpace(row.Service))
        {
            var copyService = new MenuItem { Header = L("ToolCtxCopyService") };
            copyService.Click += (_, _) => Clipboard.SetText(row.Service);
            menu.Items.Add(copyService);
        }

        // Copy Banner
        if (!string.IsNullOrWhiteSpace(row.Banner))
        {
            var copyBanner = new MenuItem { Header = L("ToolCtxCopyBanner") };
            copyBanner.Click += (_, _) => Clipboard.SetText(row.Banner);
            menu.Items.Add(copyBanner);
        }

        if (_openToolAction is not null && row.HasBanner)
        {
            menu.Items.Add(new Separator());

            // Open Port Scanner for deeper analysis
            var portScan = new MenuItem { Header = L("ToolCtxOpenPortScan") };
            portScan.Click += (_, _) => _openToolAction("PORTSCAN", L("PaletteToolPortScan"),
                new ToolContext(TargetHost: host, TargetPort: row.Port));
            menu.Items.Add(portScan);
        }

        // Copy row
        menu.Items.Add(new Separator());
        var csvText = $"{row.Port}\t{row.Service}\t{row.Banner}\t{row.ResponseTime}";
        menu.Items.Add(ToolContextMenuHelper.BuildCopyRowAction(csvText, _localizer));
        menu.Items.Add(ToolContextMenuHelper.BuildCopyAllAction(ResultsGrid, _localizer));
        menu.Items.Add(new Separator());
        menu.Items.Add(ToolContextMenuHelper.BuildExportCsvAction(ResultsGrid, _localizer));

        ResultsGrid.ContextMenu = menu;
    }

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isGrabbing;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopGrab();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Internal result from port probing (before UI binding).
    /// </summary>
    private sealed record BannerProbeResult(int Port, string Service, string ResponseTime, string? Banner);

    /// <summary>
    /// Represents a single banner grab result for DataGrid binding.
    /// </summary>
    public sealed class BannerResult
    {
        public int Port { get; init; }
        public string Service { get; init; } = "";
        public string Banner { get; init; } = "";
        public string ResponseTime { get; init; } = "";
        public bool HasBanner { get; init; }
    }
}
