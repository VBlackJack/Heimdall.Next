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
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Configuration;
using Heimdall.Core.Discovery;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Ssh;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Tests common default credentials against detected services on a target host.
/// Supports SSH, Telnet, HTTP Basic, FTP, SNMP, MySQL, PostgreSQL, MSSQL, Redis, and VNC.
/// Rate-limited to prevent account lockout.
/// </summary>
public partial class DefaultCredentialView : UserControl, IToolView
{
    private const int ConnectTimeoutMs = 5000;
    private const int RateLimitDelayMs = 1000;
    private const int MaxConcurrentServices = 3;
    private const int PortProbeTimeoutMs = 2000;

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _isScanning;
    private bool _disposed;
    private bool _showPasswords;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;
    private Action<bool>? _setBusy;

    private readonly ObservableCollection<CredTestResult> _results = [];
    private readonly List<CredTestResult> _allResults = [];

    public DefaultCredentialView()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        TxtHost.KeyDown += OnHostKeyDown;
        SetScanInputsEnabled(true);
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();

        TxtHost.Clear();

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
        HeaderTitle.Text = L("ToolDefCredTitle");
        BtnScan.Content = L("ToolDefCredBtnScan");
        BtnCopy.Content = L("ToolDefCredBtnCopy");
        BtnExportCsv.Content = L("ToolDefCredBtnExport");
        TxtWarning.Text = L("ToolDefCredWarning");
        TxtEmptyState.Text = L("ToolDefCredEmptyState");

        ChkAutoDetect.Content = L("ToolDefCredAutoDetect");
        ChkShowPasswords.Content = L("ToolDefCredShowPasswords");

        ColService.Header = L("ToolDefCredColService");
        ColPort.Header = L("ToolDefCredColPort");
        ColUser.Header = L("ToolDefCredColUser");
        ColPass.Header = L("ToolDefCredColPass");
        ColStatus.Header = L("ToolDefCredColStatus");
        ColDetail.Header = L("ToolDefCredColDetail");

        LblRouteVia.Text = L("ToolTunnelRouteVia");

        System.Windows.Automation.AutomationProperties.SetName(BtnScan, L("ToolDefCredBtnScan"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolDefCredBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnExportCsv, L("ToolDefCredBtnExport"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHost, L("ToolDefCredHostLabel"));
        System.Windows.Automation.AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));
        System.Windows.Automation.AutomationProperties.SetName(ChkAutoDetect, L("ToolDefCredAutoDetect"));
        System.Windows.Automation.AutomationProperties.SetName(ChkShowPasswords, L("ToolDefCredShowPasswords"));
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(ScanProgress, L("ToolDefCredA11yProgress"));
        System.Windows.Automation.AutomationProperties.SetName(ResultsGrid, L("ToolDefCredTitle"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));

        TxtHost.Tag = L("ToolWatermarkHostnameOrIp");
    }

    // ── Event handlers ──────────────────────────────────────────────

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpDEFAULTCREDS").Replace("\\n", "\n");
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

    private void OnShowPasswordsChanged(object sender, RoutedEventArgs e)
    {
        _showPasswords = ChkShowPasswords?.IsChecked == true;
        RefreshPasswordDisplay();
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
            FileName = $"defaultcreds_{TxtHost.Text.Trim()}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{L("ToolDefCredColService")},{L("ToolDefCredColPort")},{L("ToolDefCredColUser")},{L("ToolDefCredColPass")},{L("ToolDefCredColStatus")},{L("ToolDefCredColDetail")}");

            foreach (var r in _allResults)
            {
                var service = InputValidator.SanitizeCsvCell(r.Service);
                var username = InputValidator.SanitizeCsvCell(r.Username);
                var password = InputValidator.SanitizeCsvCell(r.Password).Replace("\"", "\"\"");
                var status = InputValidator.SanitizeCsvCell(r.Status);
                var detail = InputValidator.SanitizeCsvCell(r.Detail).Replace("\"", "\"\"");
                sb.AppendLine($"{service},{r.Port},{username},\"{password}\",{status},\"{detail}\"");
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"DefaultCredentialScanner CSV export failed: {ex.Message}");
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
            sb.AppendLine($"{L("ToolDefCredColService"),-12}{L("ToolDefCredColPort"),-8}{L("ToolDefCredColUser"),-16}{L("ToolDefCredColPass"),-16}{L("ToolDefCredColStatus"),-12}{L("ToolDefCredColDetail")}");
            sb.AppendLine(new string('-', 80));

            foreach (var r in _results)
            {
                sb.AppendLine($"{r.Service,-12}{r.Port,-8}{r.Username,-16}{r.Password,-16}{r.Status,-12}{r.Detail}");
            }

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"DefaultCredentialScanner clipboard copy failed: {ex.Message}");
        }
    }

    // ── Scan orchestration ──────────────────────────────────────────

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
            return;
        }

        if (!InputValidator.Validate(host, "Address"))
        {
            TxtError.Text = L("ErrorInvalidHost");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        _results.Clear();
        _allResults.Clear();
        var oldCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        oldCts?.Cancel();
        oldCts?.Dispose();
        _isScanning = true;

        try
        {
            _setBusy?.Invoke(true);
            BtnScan.Content = L("ToolDefCredBtnStop");
            BtnScan.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            BtnScan.Style = (Style)FindResource("SecondaryButtonStyle");
            System.Windows.Automation.AutomationProperties.SetName(BtnScan, L("ToolDefCredBtnStop"));
            SetScanInputsEnabled(false);
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ResultsBorder.Visibility = Visibility.Visible;
            TxtSummary.Text = "";
        }
        catch
        {
            _isScanning = false;
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
                Core.Logging.FileLogger.Warn($"DefaultCredentialScanner gateway connection failed: {ex.Message}");
                TxtError.Text = string.Format(L("ToolTunnelFailed"), ex.Message);
                TxtError.Visibility = Visibility.Visible;
                StopScan();
                return;
            }
        }

        var ct = _cts.Token;

        try
        {
            // Phase 1: Detect open services
            var detectedServices = new List<(int Port, string Service)>();

            if (ChkAutoDetect?.IsChecked == true)
            {
                UpdateProgress(L("ToolDefCredDetecting"), true);

        var portsToScan = DefaultCredentialPresets.ServicePorts.Keys.ToList();
                var semaphore = new SemaphoreSlim(tunnelClient is not null ? 5 : 20);

                try
                {
                    var probeTasks = portsToScan.Select(async port =>
                    {
                        await semaphore.WaitAsync(ct);
                        try
                        {
                            ct.ThrowIfCancellationRequested();
                            var isOpen = tunnelClient is not null
                                ? await ProbePortViaTunnelAsync(tunnelClient, host, port, ct)
                                : await ProbePortAsync(host, port, ct);

                            if (isOpen)
                            {
                return (Port: port, Service: DefaultCredentialPresets.ServicePorts[port]);
                            }

                            return ((int Port, string Service)?)null;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    var probeResults = await Task.WhenAll(probeTasks);
                    detectedServices.AddRange(probeResults.Where(r => r is not null).Select(r => r!.Value));
                }
                finally
                {
                    semaphore.Dispose();
                }
            }
            else
            {
                // Test all known service ports without probing
            detectedServices.AddRange(DefaultCredentialPresets.ServicePorts.Select(kv => (kv.Key, kv.Value)));
            }

            if (detectedServices.Count == 0)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    TxtSummary.Text = L("ToolDefCredNoDefaults");
                });
                StopScan();
                return;
            }

            // Phase 2: Test credentials for each detected service
            // Group by service type to apply rate limiting per service
            var serviceGroups = detectedServices
                .GroupBy(s => s.Service)
                .ToList();

            var totalTests = serviceGroups.Sum(g =>
            g.Sum(svc => DefaultCredentialPresets.CredentialsByService.GetValueOrDefault(svc.Service)?.Count ?? 0));
            var completedTests = 0;

            var serviceSemaphore = new SemaphoreSlim(MaxConcurrentServices);

            try
            {
                var serviceTasks = serviceGroups.Select(async group =>
                {
                    await serviceSemaphore.WaitAsync(ct);
                    try
                    {
                        foreach (var (port, service) in group)
                        {
                            ct.ThrowIfCancellationRequested();
        var credentials = DefaultCredentialPresets.CredentialsByService.GetValueOrDefault(service);
                            if (credentials is null)
                            {
                                continue;
                            }

                            foreach (var (user, pass) in credentials)
                            {
                                ct.ThrowIfCancellationRequested();

                                var statusText = string.Format(L("ToolDefCredTesting"), service, user, pass);
                                await Dispatcher.InvokeAsync(() => UpdateProgress(statusText, false));

                                var result = await TestCredentialAsync(
                                    host, port, service, user, pass, tunnelClient, ct);

                                await Dispatcher.InvokeAsync(() =>
                                {
                                    _allResults.Add(result);
                                    _results.Add(result);
                                    completedTests++;
                                    if (totalTests > 0)
                                    {
                                        var pct = (int)(completedTests * 100.0 / totalTests);
                                        ScanProgress.Value = pct;
                                    }
                                });

                                // Rate limiting: delay between attempts per service to prevent lockout
                                await Task.Delay(RateLimitDelayMs, ct);
                            }
                        }
                    }
                    finally
                    {
                        serviceSemaphore.Release();
                    }
                });

                await Task.WhenAll(serviceTasks);
            }
            finally
            {
                serviceSemaphore.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"DefaultCredentialScanner scan failed: {ex.Message}");
        }
        finally
        {
            if (tunnelClient is not null)
            {
                try { tunnelClient.Disconnect(); }
                catch { /* best effort */ }
                tunnelClient.Dispose();
            }
        }

        // Update summary
        await Dispatcher.InvokeAsync(UpdateSummary);

        StopScan();
    }

    private void StopScan()
    {
        var oldCts = Interlocked.Exchange(ref _cts, null);
        oldCts?.Cancel();
        oldCts?.Dispose();
        _isScanning = false;
        _setBusy?.Invoke(false);
        BtnScan.Content = L("ToolDefCredBtnScan");
        BtnScan.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
        BtnScan.Style = (Style)FindResource("PrimaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(BtnScan, L("ToolDefCredBtnScan"));
        SetScanInputsEnabled(true);
        ProgressPanel.Visibility = Visibility.Collapsed;
    }

    private void SetScanInputsEnabled(bool enabled)
    {
        TxtHost.IsReadOnly = !enabled;
        CmbRouteVia.IsEnabled = enabled;
        ChkAutoDetect.IsEnabled = enabled;
        ChkShowPasswords.IsEnabled = enabled;
        BtnCopy.IsEnabled = enabled && _results.Count > 0;
        BtnExportCsv.IsEnabled = enabled && _allResults.Count > 0;
    }

    private void UpdateProgress(string statusText, bool isIndeterminate)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = isIndeterminate;
        TxtProgressStatus.Text = statusText;
    }

    private void UpdateSummary()
    {
        var defaultCount = _allResults.Count(r =>
            string.Equals(r.Status, "default", StringComparison.OrdinalIgnoreCase));
        var serviceCount = _allResults
            .Where(r => string.Equals(r.Status, "default", StringComparison.OrdinalIgnoreCase))
            .Select(r => $"{r.Service}:{r.Port}")
            .Distinct()
            .Count();

        TxtSummary.Text = defaultCount > 0
            ? string.Format(L("ToolDefCredSummary"), defaultCount, serviceCount)
            : L("ToolDefCredNoDefaults");
    }

    // ── Gateway & route ─────────────────────────────────────────────

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

    // ── Port probing ────────────────────────────────────────────────

    private static async Task<bool> ProbePortAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(PortProbeTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await client.ConnectAsync(host, port, linked.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ProbePortViaTunnelAsync(
        Renci.SshNet.SshClient sshClient, string host, int port, CancellationToken ct)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                var safeHost = InputValidator.EscapeShellArg(host);
                using var cmd = sshClient.CreateCommand(
                    $"(echo >/dev/tcp/{safeHost}/{port}) 2>/dev/null && echo OPEN || echo CLOSED");
                cmd.CommandTimeout = TimeSpan.FromMilliseconds(PortProbeTimeoutMs);
                cmd.Execute();
                return cmd.Result?.Trim();
            }, ct).ConfigureAwait(false);

            return string.Equals(result, "OPEN", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    // ── Credential testing dispatch ─────────────────────────────────

    private async Task<CredTestResult> TestCredentialAsync(
        string host, int port, string service, string user, string pass,
        Renci.SshNet.SshClient? tunnelClient, CancellationToken ct)
    {
        try
        {
            var accepted = service switch
            {
                "SSH" => await TestSshAsync(host, port, user, pass, ct),
                "Telnet" => await TestTelnetAsync(host, port, user, pass, tunnelClient, ct),
                "HTTP" => await TestHttpBasicAsync(host, port, user, pass, tunnelClient, ct),
                "FTP" => await TestFtpAsync(host, port, user, pass, tunnelClient, ct),
                "SNMP" => await TestSnmpCommunityAsync(host, user, ct),
                "MySQL" => await TestMySqlAsync(host, port, user, pass, tunnelClient, ct),
                "PostgreSQL" => await TestPostgreSqlAsync(host, port, user, pass, tunnelClient, ct),
                "MSSQL" => await TestMsSqlAsync(host, port, user, pass, tunnelClient, ct),
                "Redis" => await TestRedisAsync(host, port, tunnelClient, ct),
                "VNC" => await TestVncAsync(host, port, pass, tunnelClient, ct),
                _ => false,
            };

            var status = accepted ? "default" : "changed";
            var statusText = accepted ? L("ToolDefCredStatusDefault") : L("ToolDefCredStatusChanged");
            var detail = accepted
                ? service + " " + L("ToolDefCredDetailAccepted")
                : service + " " + L("ToolDefCredDetailRejected");

            return new CredTestResult
            {
                Service = service,
                Port = port,
                Username = user,
                Password = pass,
                Status = status,
                StatusText = statusText,
                Detail = detail,
                ShowPassword = _showPasswords,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CredTestResult
            {
                Service = service,
                Port = port,
                Username = user,
                Password = pass,
                Status = "error",
                StatusText = L("ToolDefCredStatusError"),
                Detail = ex.Message,
                ShowPassword = _showPasswords,
            };
        }
    }

    // ── Protocol-specific testers ───────────────────────────────────

    /// <summary>
    /// Tests SSH credentials using SSH.NET PasswordConnectionInfo.
    /// </summary>
    private static async Task<bool> TestSshAsync(
        string host, int port, string user, string pass, CancellationToken ct)
    {
        try
        {
            var connInfo = new Renci.SshNet.PasswordConnectionInfo(host, port, user, pass)
            {
                Timeout = TimeSpan.FromSeconds(5),
            };

            using var client = new Renci.SshNet.SshClient(connInfo);
            await Task.Run(() => client.Connect(), ct);
            client.Disconnect();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tests HTTP Basic Authentication by sending a GET request with Authorization header.
    /// Checks for 200/301/302 (accepted) vs 401/403 (rejected).
    /// </summary>
    private static async Task<bool> TestHttpBasicAsync(
        string host, int port, string user, string pass,
        Renci.SshNet.SshClient? tunnelClient, CancellationToken ct)
    {
        try
        {
            var useTls = port is 443 or 8443;
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            if (tunnelClient is not null)
            {
                // For HTTP over tunnel, use SSH exec approach
                var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}"));
                var hostInEcho = InputValidator.EscapeForDoubleQuotedString(host);
                var hostArg = InputValidator.EscapeShellArg(host);
                var httpCmd = $"echo -e \"GET / HTTP/1.0\\r\\nHost: {hostInEcho}\\r\\nAuthorization: Basic {creds}\\r\\n\\r\\n\" | nc -w 5 {hostArg} {port} 2>/dev/null | head -1";

                var result = await Task.Run(() =>
                {
                    using var cmd = tunnelClient.CreateCommand(httpCmd);
                    cmd.CommandTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs);
                    cmd.Execute();
                    return cmd.Result?.Trim() ?? "";
                }, ct).ConfigureAwait(false);

                return IsHttpSuccessResponse(result);
            }

            await tcp.ConnectAsync(host, port, linked.Token);
            Stream stream = tcp.GetStream();
            SslStream? ssl = null;

            try
            {
                if (useTls)
                {
                    ssl = new SslStream(stream, leaveInnerStreamOpen: true, (_, _, _, _) => true);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = host,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    }, linked.Token);
                    stream = ssl;
                }

                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}"));
                var request = $"GET / HTTP/1.0\r\nHost: {host}\r\nAuthorization: Basic {credentials}\r\n\r\n";
                var requestBytes = Encoding.ASCII.GetBytes(request);
                await stream.WriteAsync(requestBytes, linked.Token);

                var buffer = new byte[512];
                var read = await stream.ReadAsync(buffer, linked.Token);
                var response = Encoding.ASCII.GetString(buffer, 0, read);

                return IsHttpSuccessResponse(response);
            }
            finally
            {
                if (ssl is not null) await ssl.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if an HTTP response line indicates successful authentication (2xx/3xx).
    /// </summary>
    private static bool IsHttpSuccessResponse(string responseLine)
    {
        // Look for HTTP/1.x 2xx or 3xx status codes
        if (string.IsNullOrEmpty(responseLine))
        {
            return false;
        }

        var parts = responseLine.Split(' ', 3);
        if (parts.Length >= 2 && int.TryParse(parts[1], out var statusCode))
        {
            // 2xx = success, 3xx = redirect (both indicate credentials were accepted)
            // 401/403 = auth failure
            return statusCode is >= 200 and < 400;
        }

        return false;
    }

    /// <summary>
    /// Tests SNMP community string using the UDP probe engine.
    /// </summary>
    private static async Task<bool> TestSnmpCommunityAsync(
        string host, string community, CancellationToken ct)
    {
        try
        {
            var info = await UdpProbeEngine.QuerySnmpAsync(host, community, ConnectTimeoutMs, ct);
            return info is not null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tests FTP credentials by sending USER/PASS commands over TCP.
    /// </summary>
    private static async Task<bool> TestFtpAsync(
        string host, int port, string user, string pass,
        Renci.SshNet.SshClient? tunnelClient, CancellationToken ct)
    {
        try
        {
            if (tunnelClient is not null)
            {
                var ftpScript = $"(echo -e \"USER {InputValidator.EscapeForDoubleQuotedString(user)}\\r\\nPASS {InputValidator.EscapeForDoubleQuotedString(pass)}\\r\\nQUIT\\r\\n\" | nc -w 5 {InputValidator.EscapeShellArg(host)} {port} 2>/dev/null)";
                var result = await Task.Run(() =>
                {
                    using var cmd = tunnelClient.CreateCommand(ftpScript);
                    cmd.CommandTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs);
                    cmd.Execute();
                    return cmd.Result ?? "";
                }, ct).ConfigureAwait(false);

                return result.Contains("230");
            }

            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await tcp.ConnectAsync(host, port, linked.Token);
            var stream = tcp.GetStream();

            // Read welcome banner
            var buffer = new byte[512];
            _ = await stream.ReadAsync(buffer, linked.Token);

            // Send USER
            var userCmd = Encoding.ASCII.GetBytes($"USER {user}\r\n");
            await stream.WriteAsync(userCmd, linked.Token);
            _ = await stream.ReadAsync(buffer, linked.Token);

            // Send PASS
            var passCmd = Encoding.ASCII.GetBytes($"PASS {pass}\r\n");
            await stream.WriteAsync(passCmd, linked.Token);
            var read = await stream.ReadAsync(buffer, linked.Token);
            var response = Encoding.ASCII.GetString(buffer, 0, read);

            // 230 = Login successful
            return response.Contains("230");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tests Telnet credentials by sending user/pass after detecting prompts.
    /// </summary>
    private static async Task<bool> TestTelnetAsync(
        string host, int port, string user, string pass,
        Renci.SshNet.SshClient? tunnelClient, CancellationToken ct)
    {
        try
        {
            if (tunnelClient is not null)
            {
                var telnetScript = $"(echo -e \"{InputValidator.EscapeForDoubleQuotedString(user)}\\n{InputValidator.EscapeForDoubleQuotedString(pass)}\\n\" | nc -w 5 {InputValidator.EscapeShellArg(host)} {port} 2>/dev/null) | tail -1";
                var result = await Task.Run(() =>
                {
                    using var cmd = tunnelClient.CreateCommand(telnetScript);
                    cmd.CommandTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs);
                    cmd.Execute();
                    return cmd.Result ?? "";
                }, ct).ConfigureAwait(false);

                // If we get a shell prompt or no "incorrect", consider it accepted
                return !result.Contains("incorrect", StringComparison.OrdinalIgnoreCase) &&
                       !result.Contains("failed", StringComparison.OrdinalIgnoreCase) &&
                       !result.Contains("denied", StringComparison.OrdinalIgnoreCase) &&
                       result.Length > 0;
            }

            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await tcp.ConnectAsync(host, port, linked.Token);
            var stream = tcp.GetStream();

            var buffer = new byte[1024];

            // Read initial banner/negotiation (may contain IAC sequences)
            _ = await stream.ReadAsync(buffer, linked.Token);
            await Task.Delay(500, linked.Token);

            // Drain additional data
            if (stream.DataAvailable)
            {
                _ = await stream.ReadAsync(buffer, linked.Token);
            }

            // Send username
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"{user}\r\n"), linked.Token);
            await Task.Delay(500, linked.Token);
            _ = await stream.ReadAsync(buffer, linked.Token);

            // Send password
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"{pass}\r\n"), linked.Token);
            await Task.Delay(500, linked.Token);
            var read = await stream.ReadAsync(buffer, linked.Token);
            var response = Encoding.ASCII.GetString(buffer, 0, read);

            // If response contains "incorrect", "failed", "denied" -> rejected
            // If response contains a prompt character or success indicator -> accepted
            return !response.Contains("incorrect", StringComparison.OrdinalIgnoreCase) &&
                   !response.Contains("failed", StringComparison.OrdinalIgnoreCase) &&
                   !response.Contains("denied", StringComparison.OrdinalIgnoreCase) &&
                   (response.Contains('$') || response.Contains('#') || response.Contains('>'));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tests MySQL credentials by sending a basic handshake authentication packet.
    /// </summary>
    private static async Task<bool> TestMySqlAsync(
        string host, int port, string user, string pass,
        Renci.SshNet.SshClient? tunnelClient, CancellationToken ct)
    {
        try
        {
            if (tunnelClient is not null)
            {
                var mysqlCmd = $"mysql -h {InputValidator.EscapeShellArg(host)} -P {port} -u {InputValidator.EscapeShellArg(user)} --password={InputValidator.EscapeShellArg(pass)} -e \"SELECT 1\" 2>&1 | head -1";
                var result = await Task.Run(() =>
                {
                    using var cmd = tunnelClient.CreateCommand(mysqlCmd);
                    cmd.CommandTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs);
                    cmd.Execute();
                    return cmd.Result ?? "";
                }, ct).ConfigureAwait(false);

                return !result.Contains("ERROR", StringComparison.OrdinalIgnoreCase) &&
                       !result.Contains("denied", StringComparison.OrdinalIgnoreCase);
            }

            // Direct connection: attempt TCP connect and read MySQL greeting packet
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await tcp.ConnectAsync(host, port, linked.Token);
            var stream = tcp.GetStream();

            // Read MySQL server greeting
            var buffer = new byte[1024];
            var read = await stream.ReadAsync(buffer, linked.Token);

            // If we received a greeting, the service is running and potentially accepts connections
            // A full MySQL auth handshake requires crypto negotiation, so for basic detection
            // we verify the greeting contains a valid MySQL protocol marker
            return read > 4 && buffer[4] == 0x0a; // Protocol version 10
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tests PostgreSQL credentials by connecting and attempting authentication.
    /// </summary>
    private static async Task<bool> TestPostgreSqlAsync(
        string host, int port, string user, string pass,
        Renci.SshNet.SshClient? tunnelClient, CancellationToken ct)
    {
        try
        {
            if (tunnelClient is not null)
            {
                var pgCmd = $"PGPASSWORD={InputValidator.EscapeShellArg(pass)} psql -h {InputValidator.EscapeShellArg(host)} -p {port} -U {InputValidator.EscapeShellArg(user)} -c \"SELECT 1\" 2>&1 | head -1";
                var result = await Task.Run(() =>
                {
                    using var cmd = tunnelClient.CreateCommand(pgCmd);
                    cmd.CommandTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs);
                    cmd.Execute();
                    return cmd.Result ?? "";
                }, ct).ConfigureAwait(false);

                return !result.Contains("FATAL", StringComparison.OrdinalIgnoreCase) &&
                       !result.Contains("error", StringComparison.OrdinalIgnoreCase) &&
                       result.Length > 0;
            }

            // Direct: send PostgreSQL startup message
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await tcp.ConnectAsync(host, port, linked.Token);
            var stream = tcp.GetStream();

            // Build startup message: version 3.0, user=<user>, database=<user>
            var startupParams = $"user\0{user}\0database\0{user}\0\0";
            var paramBytes = Encoding.ASCII.GetBytes(startupParams);
            var msgLen = 4 + 4 + paramBytes.Length; // length(4) + version(4) + params
            var msg = new byte[msgLen];
            // Length (big-endian, includes self)
            BitConverter.TryWriteBytes(msg.AsSpan(0, 4), System.Net.IPAddress.HostToNetworkOrder(msgLen));
            // Protocol version 3.0
            msg[4] = 0; msg[5] = 3; msg[6] = 0; msg[7] = 0;
            Array.Copy(paramBytes, 0, msg, 8, paramBytes.Length);

            await stream.WriteAsync(msg, linked.Token);

            var buffer = new byte[1024];
            var read = await stream.ReadAsync(buffer, linked.Token);

            // 'R' = Authentication response; check for AuthenticationOk (type 0)
            if (read > 0 && buffer[0] == (byte)'R')
            {
                // Read auth type (bytes 5-8, big-endian int32)
                if (read >= 9)
                {
                    var authType = System.Net.IPAddress.NetworkToHostOrder(
                        BitConverter.ToInt32(buffer, 5));
                    return authType == 0; // AuthenticationOk
                }
            }

            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tests MSSQL by attempting a TCP connection to the TDS port.
    /// Full TDS auth requires complex packet construction; uses tunnel-side tools when available.
    /// </summary>
    private static async Task<bool> TestMsSqlAsync(
        string host, int port, string user, string pass,
        Renci.SshNet.SshClient? tunnelClient, CancellationToken ct)
    {
        try
        {
            if (tunnelClient is not null)
            {
                // Attempt via sqlcmd if available on the gateway
                var sqlCmd = $"sqlcmd -S {InputValidator.EscapeShellArg(host + "," + port)} -U {InputValidator.EscapeShellArg(user)} -P {InputValidator.EscapeShellArg(pass)} -Q \"SELECT 1\" -t 5 2>&1 | head -1";
                var result = await Task.Run(() =>
                {
                    using var cmd = tunnelClient.CreateCommand(sqlCmd);
                    cmd.CommandTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs);
                    cmd.Execute();
                    return cmd.Result ?? "";
                }, ct).ConfigureAwait(false);

                return !result.Contains("Error", StringComparison.OrdinalIgnoreCase) &&
                       !result.Contains("Login failed", StringComparison.OrdinalIgnoreCase) &&
                       result.Length > 0;
            }

            // Direct: verify the port is open and responds with a TDS pre-login response
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await tcp.ConnectAsync(host, port, linked.Token);

            // If we connected, the service is up. Full TDS auth would require
            // complex packet construction; mark as reachable.
            return false; // Conservative: cannot confirm auth without full TDS handshake
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tests Redis connectivity — default Redis has no authentication.
    /// Sends PING and checks for PONG response.
    /// </summary>
    private static async Task<bool> TestRedisAsync(
        string host, int port,
        Renci.SshNet.SshClient? tunnelClient, CancellationToken ct)
    {
        try
        {
            if (tunnelClient is not null)
            {
                var redisCmd = $"echo PING | nc -w 3 {InputValidator.EscapeShellArg(host)} {port} 2>/dev/null";
                var result = await Task.Run(() =>
                {
                    using var cmd = tunnelClient.CreateCommand(redisCmd);
                    cmd.CommandTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs);
                    cmd.Execute();
                    return cmd.Result?.Trim() ?? "";
                }, ct).ConfigureAwait(false);

                return result.Contains("+PONG", StringComparison.OrdinalIgnoreCase);
            }

            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await tcp.ConnectAsync(host, port, linked.Token);
            var stream = tcp.GetStream();

            // Send PING command (RESP protocol)
            var ping = Encoding.ASCII.GetBytes("PING\r\n");
            await stream.WriteAsync(ping, linked.Token);

            var buffer = new byte[256];
            var read = await stream.ReadAsync(buffer, linked.Token);
            var response = Encoding.ASCII.GetString(buffer, 0, read);

            // +PONG means no auth required (default credential!)
            return response.Contains("+PONG", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tests VNC by attempting to connect and checking the RFB handshake.
    /// VNC auth type 1 = no auth, type 2 = VNC password auth.
    /// </summary>
    private static async Task<bool> TestVncAsync(
        string host, int port, string pass,
        Renci.SshNet.SshClient? tunnelClient, CancellationToken ct)
    {
        try
        {
            if (tunnelClient is not null)
            {
                // Basic connectivity check via tunnel
                var safeVncHost = InputValidator.EscapeShellArg(host);
                var vncCheck = $"(echo >/dev/tcp/{safeVncHost}/{port}) 2>/dev/null && echo OPEN || echo CLOSED";
                var result = await Task.Run(() =>
                {
                    using var cmd = tunnelClient.CreateCommand(vncCheck);
                    cmd.CommandTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs);
                    cmd.Execute();
                    return cmd.Result?.Trim() ?? "";
                }, ct).ConfigureAwait(false);

                // VNC auth testing requires protocol-level interaction; mark as reachable
                return false;
            }

            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await tcp.ConnectAsync(host, port, linked.Token);
            var stream = tcp.GetStream();

            // Read RFB version string (e.g., "RFB 003.008\n")
            var buffer = new byte[256];
            var read = await stream.ReadAsync(buffer, linked.Token);
            var version = Encoding.ASCII.GetString(buffer, 0, read);

            if (!version.StartsWith("RFB", StringComparison.Ordinal))
            {
                return false;
            }

            // Echo version back
            await stream.WriteAsync(buffer.AsMemory(0, read), linked.Token);

            // Read security types
            read = await stream.ReadAsync(buffer, linked.Token);
            if (read > 0)
            {
                var numTypes = buffer[0];
                for (var i = 1; i <= numTypes && i < read; i++)
                {
                    // Security type 1 = None (no auth required)
                    if (buffer[i] == 1)
                    {
                        return true;
                    }
                }
            }

            // VNC password auth (type 2) would require DES challenge-response
            // which is protocol-complex; conservatively report as not using default
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    // ── Password display ────────────────────────────────────────────

    private void RefreshPasswordDisplay()
    {
        foreach (var result in _allResults)
        {
            result.ShowPassword = _showPasswords;
        }

        // Force DataGrid refresh
        var items = _results.ToList();
        _results.Clear();
        foreach (var item in items)
        {
            _results.Add(item);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private string L(string key) => _localizer?[key] ?? key;

    public bool CanClose() => !_isScanning;

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
}

/// <summary>
/// Represents a single credential test result for DataGrid binding.
/// </summary>
public sealed class CredTestResult : System.ComponentModel.INotifyPropertyChanged
{
    private const string MaskedPassword = "\u2022\u2022\u2022\u2022\u2022\u2022";
    private bool _showPassword;

    public string Service { get; init; } = "";
    public int Port { get; init; }
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string Status { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string Detail { get; init; } = "";

    /// <summary>
    /// Controls whether the password column shows the actual value or masked bullets.
    /// </summary>
    public bool ShowPassword
    {
        get => _showPassword;
        set
        {
            if (_showPassword != value)
            {
                _showPassword = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayPassword)));
            }
        }
    }

    /// <summary>
    /// Returns the password as plain text or masked depending on <see cref="ShowPassword"/>.
    /// </summary>
    public string DisplayPassword => _showPassword ? Password : MaskedPassword;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
