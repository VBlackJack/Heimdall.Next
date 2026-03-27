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
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Ssh;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// SSL/TLS Auditor tool that evaluates a server's TLS configuration by testing
/// protocol version support, enumerating cipher suites, and grading the results.
/// </summary>
public partial class TlsAuditView : UserControl, IToolView
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isAuditing;
    private Action<bool>? _setBusy;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;
    private string _lastReport = string.Empty;

    // ── Protocol definitions ──────────────────────────────────────────

#pragma warning disable CA5397, CS0618, SYSLIB0039 // Obsolete TLS/SSL versions — needed to detect insecure configs
    private static readonly (string Name, SslProtocols Protocol, string Rating)[] Protocols =
    [
        ("SSL 3.0", SslProtocols.Ssl3,  "critical"),
        ("TLS 1.0", SslProtocols.Tls,   "weak"),
        ("TLS 1.1", SslProtocols.Tls11, "weak"),
        ("TLS 1.2", SslProtocols.Tls12, "strong"),
        ("TLS 1.3", SslProtocols.Tls13, "strong"),
    ];
#pragma warning restore CA5397, CS0618, SYSLIB0039

    // ── Known TLS 1.2 cipher suites to probe ─────────────────────────

    private static readonly TlsCipherSuite[] Tls12CipherSuites =
    [
        // Strong: ECDHE + AEAD
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
        // Strong: DHE + AEAD
        TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_GCM_SHA256,
        // Acceptable: Static RSA + AEAD (no PFS)
        TlsCipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
        // Acceptable: CBC suites
        TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256,
        TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA256,
        TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA,
        TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
        TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
        // Weak: 3DES / RC4
        TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA,
        TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA,
        TlsCipherSuite.TLS_RSA_WITH_RC4_128_MD5,
    ];

    // ── Display models ────────────────────────────────────────────────

    public sealed class ProtocolResult
    {
        public string Name { get; init; } = "";
        public bool Supported { get; init; }
        public string Rating { get; init; } = "";
        public Brush RatingBrush { get; init; } = Brushes.Transparent;
        public string Icon { get; init; } = "";
        public string StatusText { get; init; } = "";
    }

    public sealed class CipherResult
    {
        public string Name { get; init; } = "";
        public string Strength { get; init; } = "";
        public Brush StrengthBrush { get; init; } = Brushes.Transparent;
        public string KeyExchange { get; init; } = "";
        public string Authentication { get; init; } = "";
        public string Encryption { get; init; } = "";
    }

    public sealed class FindingItem
    {
        public string Icon { get; init; } = "";
        public string Message { get; init; } = "";
        public Brush Brush { get; init; } = Brushes.Transparent;
    }

    // ── Constructor ───────────────────────────────────────────────────

    public TlsAuditView()
    {
        InitializeComponent();
        TxtHost.KeyDown += OnInputKeyDown;
        TxtPort.KeyDown += OnInputKeyDown;
    }

    // ── IToolView ─────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHost.Text = context.TargetHost;
        }

        if (context?.TargetPort is > 0)
        {
            TxtPort.Text = context.TargetPort.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            ParseArgument(context.Argument);
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

    private void ParseArgument(string argument)
    {
        var trimmed = argument.Trim();
        var colonIndex = trimmed.LastIndexOf(':');
        if (colonIndex > 0 && int.TryParse(trimmed[(colonIndex + 1)..], out var port) && port is > 0 and <= 65535)
        {
            TxtHost.Text = trimmed[..colonIndex];
            TxtPort.Text = port.ToString();
        }
        else
        {
            TxtHost.Text = trimmed;
        }
    }

    // ── Localization ──────────────────────────────────────────────────

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolTlsAuditTitle");
        LblHost.Text = L("ToolTlsAuditHostLabel");
        BtnAudit.Content = L("ToolTlsAuditBtnAudit");
        LblProtocols.Text = L("ToolTlsAuditProtocols");
        LblCiphers.Text = L("ToolTlsAuditCiphers");
        LblCertSection.Text = L("ToolTlsAuditCertSection");
        LblFindings.Text = L("ToolTlsAuditFindings");
        BtnCopy.Content = L("ToolTlsAuditBtnCopy");
        TxtEmptyState.Text = L("ToolTlsAuditEmptyState");
        LblGradeCaption.Text = L("ToolTlsAuditGrade");

        LblCertSubject.Text = L("ToolCertSubject");
        LblCertIssuer.Text = L("ToolCertIssuer");
        LblCertExpiry.Text = L("ToolCertValidTo");
        LblCertKeySize.Text = L("ToolCertKeySize");

        LblRouteVia.Text = L("ToolTunnelRouteVia");
        AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));

        AutomationProperties.SetName(BtnAudit, L("ToolTlsAuditBtnAudit"));
        AutomationProperties.SetName(TxtHost, L("ToolTlsAuditHostLabel"));
        AutomationProperties.SetName(TxtPort, L("ToolTlsAuditPortLabel"));
        AutomationProperties.SetName(BtnCopy, L("ToolTlsAuditBtnCopy"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");

        TxtHost.Tag = L("ToolWatermarkHostname");
        TxtPort.Tag = L("ToolTlsAuditPortLabel");
    }

    // ── Event handlers ────────────────────────────────────────────────

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpTLSAUDIT");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_isAuditing)
            {
                CancelAudit();
            }
            else
            {
                _ = RunAuditAsync();
            }
            e.Handled = true;
        }
    }

    private void OnAuditClick(object sender, RoutedEventArgs e)
    {
        if (_isAuditing)
        {
            CancelAudit();
            return;
        }
        _ = RunAuditAsync();
    }

    private void CancelAudit()
    {
        _cts?.Cancel();
    }

    // ── Gateway selector ──────────────────────────────────────────────

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
        _selectedGateway = CmbRouteVia.SelectedItem is ComboBoxItem item && item.Tag is SshGatewayDto gw
            ? gw
            : null;
    }

    private static Renci.SshNet.SshClient ConnectToGateway(SshGatewayDto gateway)
        => ToolGatewayConnector.Connect(gateway);

    // ── Main audit workflow ───────────────────────────────────────────

    private async Task RunAuditAsync()
    {
        var host = TxtHost.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (!int.TryParse(TxtPort.Text.Trim(), out var port) || port is <= 0 or > 65535)
        {
            TxtError.Text = L("ToolCertErrorInvalidPort");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Reset UI
        TxtError.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        BtnCopy.Visibility = Visibility.Collapsed;
        CipherPanel.Visibility = Visibility.Collapsed;
        CertPanel.Visibility = Visibility.Collapsed;
        FindingsPanel.Visibility = Visibility.Collapsed;

        AuditProgress.Value = 0;
        TxtProgressStatus.Text = "";
        ProgressPanel.Visibility = Visibility.Visible;

        _isAuditing = true;
        _setBusy?.Invoke(true);
        BtnAudit.Content = L("ToolCertBtnStop");
        BtnAudit.Foreground = (Brush)FindResource("ErrorBrush");
        BtnAudit.Style = (Style)FindResource("SecondaryButtonStyle");
        AutomationProperties.SetName(BtnAudit, L("ToolCertBtnStop"));
        TxtHost.IsReadOnly = true;
        TxtPort.IsReadOnly = true;
        CmbRouteVia.IsEnabled = false;

        Renci.SshNet.SshClient? tunnelClient = null;
        if (_selectedGateway is not null)
        {
            try
            {
                tunnelClient = await Task.Run(() => ConnectToGateway(_selectedGateway), ct);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"TlsAudit gateway connection failed: {ex.Message}");
                TxtError.Text = string.Format(L("ToolTunnelFailed"), ex.Message);
                TxtError.Visibility = Visibility.Visible;
                ResetUi();
                return;
            }
        }

        try
        {
            // Step 1: Test protocol versions
            var protocolResults = await TestAllProtocolsAsync(host, port, tunnelClient, ct);

            // Step 2: Enumerate TLS 1.2 cipher suites (if TLS 1.2 is supported)
            var cipherResults = new List<CipherResult>();
            var tls12Supported = protocolResults.Any(p => p.Name == "TLS 1.2" && p.Supported);
            if (tls12Supported)
            {
                cipherResults = await EnumerateCipherSuitesAsync(host, port, tunnelClient, ct);
            }

            // Step 3: Retrieve certificate summary
            X509Certificate2? cert = null;
            try
            {
                cert = await RetrieveCertificateAsync(host, port, tunnelClient, ct);
            }
            catch
            {
                // Certificate retrieval is best-effort; audit continues without it
            }

            // Step 4: Grade and display
            var findings = BuildFindings(protocolResults, cipherResults);
            var grade = CalculateGrade(protocolResults, cipherResults);

            DisplayResults(protocolResults, cipherResults, cert, findings, grade, host, port);

            cert?.Dispose();
        }
        catch (OperationCanceledException)
        {
            TxtError.Text = L("ToolCertErrorTimeout");
            TxtError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"TlsAudit failed: {ex.Message}");
            TxtError.Text = string.Format(L("ToolTlsAuditErrorConnection"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        finally
        {
            if (tunnelClient is not null)
            {
                try { tunnelClient.Disconnect(); } catch { /* best effort */ }
                tunnelClient.Dispose();
            }
            ResetUi();
        }
    }

    private void ResetUi()
    {
        _isAuditing = false;
        _setBusy?.Invoke(false);
        ProgressPanel.Visibility = Visibility.Collapsed;
        BtnAudit.Content = L("ToolTlsAuditBtnAudit");
        BtnAudit.Foreground = (Brush)FindResource("TextPrimaryBrush");
        BtnAudit.Style = (Style)FindResource("PrimaryButtonStyle");
        AutomationProperties.SetName(BtnAudit, L("ToolTlsAuditBtnAudit"));
        TxtHost.IsReadOnly = false;
        TxtPort.IsReadOnly = false;
        CmbRouteVia.IsEnabled = true;
    }

    // ── Protocol testing ──────────────────────────────────────────────

    private async Task<List<ProtocolResult>> TestAllProtocolsAsync(
        string host, int port, Renci.SshNet.SshClient? tunnel, CancellationToken ct)
    {
        var results = new List<ProtocolResult>();
        var totalSteps = Protocols.Length + 2; // protocols + cipher enum + cert
        var currentStep = 0;

        foreach (var (name, protocol, rating) in Protocols)
        {
            ct.ThrowIfCancellationRequested();

            await Dispatcher.InvokeAsync(() =>
            {
                TxtProgressStatus.Text = string.Format(L("ToolTlsAuditProgress"), name);
                AuditProgress.Value = (int)(++currentStep * 100.0 / totalSteps);
            });

            bool supported;
            if (tunnel is not null)
            {
                supported = await Task.Run(() => TestProtocolViaTunnel(tunnel, host, port, name, ct), ct);
            }
            else
            {
                supported = await TestProtocolAsync(host, port, protocol, ct);
            }

            var ratingBrush = GetRatingBrush(supported, rating);
            var statusText = supported ? L("ToolTlsAuditSupported") : L("ToolTlsAuditNotSupported");

            results.Add(new ProtocolResult
            {
                Name = name,
                Supported = supported,
                Rating = rating,
                RatingBrush = (Brush)FindResource(ratingBrush),
                Icon = supported ? "\u2714" : "\u2716",
                StatusText = statusText,
            });
        }

        return results;
    }

    private static async Task<bool> TestProtocolAsync(
        string host, int port, SslProtocols protocol, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, ct);
#pragma warning disable CA5397, CS0618, SYSLIB0039
            using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = protocol,
            }, ct);
#pragma warning restore CA5397, CS0618, SYSLIB0039
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TestProtocolViaTunnel(
        Renci.SshNet.SshClient sshClient, string host, int port, string protocolName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var flag = protocolName switch
        {
            "SSL 3.0" => "-ssl3",
            "TLS 1.0" => "-tls1",
            "TLS 1.1" => "-tls1_1",
            "TLS 1.2" => "-tls1_2",
            "TLS 1.3" => "-tls1_3",
            _ => ""
        };

        if (string.IsNullOrEmpty(flag)) return false;

        try
        {
            using var cmd = sshClient.CreateCommand(
                $"echo | openssl s_client -connect {host}:{port} {flag} 2>&1 | head -5");
            cmd.CommandTimeout = TimeSpan.FromSeconds(10);
            cmd.Execute();
            var output = cmd.Result ?? string.Empty;
            // A successful TLS handshake produces output containing "CONNECTED" and no "error"
            return output.Contains("CONNECTED", StringComparison.OrdinalIgnoreCase)
                && !output.Contains("error", StringComparison.OrdinalIgnoreCase)
                && !output.Contains("wrong version", StringComparison.OrdinalIgnoreCase)
                && !output.Contains("no protocols available", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // ── Cipher suite enumeration ──────────────────────────────────────

    private async Task<List<CipherResult>> EnumerateCipherSuitesAsync(
        string host, int port, Renci.SshNet.SshClient? tunnel, CancellationToken ct)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            TxtProgressStatus.Text = L("ToolTlsAuditProgressCiphers");
            AuditProgress.Value = (int)(Protocols.Length * 100.0 / (Protocols.Length + 2));
        });

        if (tunnel is not null)
        {
            return await Task.Run(() => EnumerateCiphersViaTunnel(tunnel, host, port, ct), ct);
        }

        return await EnumerateCiphersDirectAsync(host, port, ct);
    }

    private async Task<List<CipherResult>> EnumerateCiphersDirectAsync(
        string host, int port, CancellationToken ct)
    {
        var results = new List<CipherResult>();

        foreach (var suite in Tls12CipherSuites)
        {
            ct.ThrowIfCancellationRequested();

            var supported = await TestCipherSuiteAsync(host, port, suite, ct);
            if (supported)
            {
                results.Add(BuildCipherResult(suite));
            }
        }

        return results;
    }

    private static async Task<bool> TestCipherSuiteAsync(
        string host, int port, TlsCipherSuite suite, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, ct);
            using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);

            var options = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12,
            };

            // CipherSuitesPolicy may throw PlatformNotSupportedException on some Windows versions
            try
            {
#pragma warning disable CA1416 // CipherSuitesPolicy is not supported on all Windows versions — guarded by try/catch
                options.CipherSuitesPolicy = new CipherSuitesPolicy([suite]);
#pragma warning restore CA1416
            }
            catch (PlatformNotSupportedException)
            {
                // Cannot restrict cipher suites on this platform — skip individual testing
                return false;
            }

            await ssl.AuthenticateAsClientAsync(options, ct);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private List<CipherResult> EnumerateCiphersViaTunnel(
        Renci.SshNet.SshClient sshClient, string host, int port, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var results = new List<CipherResult>();

        try
        {
            using var cmd = sshClient.CreateCommand(
                $"echo | openssl s_client -connect {host}:{port} -tls1_2 -cipher ALL 2>/dev/null | openssl ciphers -v 2>/dev/null; echo | openssl s_client -connect {host}:{port} -tls1_2 2>&1");
            cmd.CommandTimeout = TimeSpan.FromSeconds(15);
            cmd.Execute();
            var output = cmd.Result ?? string.Empty;

            // Try to parse the "Cipher is" line from s_client output to find the negotiated cipher
            // Also run: openssl s_client -connect host:port -tls1_2 2>&1 to get negotiated cipher
            // A more reliable approach: test each cipher individually via tunnel
            results = EnumerateCiphersViaTunnelIndividually(sshClient, host, port, ct);
        }
        catch
        {
            // Fall back to individual cipher testing
            results = EnumerateCiphersViaTunnelIndividually(sshClient, host, port, ct);
        }

        return results;
    }

    private List<CipherResult> EnumerateCiphersViaTunnelIndividually(
        Renci.SshNet.SshClient sshClient, string host, int port, CancellationToken ct)
    {
        var results = new List<CipherResult>();

        // Map TlsCipherSuite enum names to OpenSSL cipher names
        var opensslNames = new Dictionary<TlsCipherSuite, string>
        {
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384] = "ECDHE-RSA-AES256-GCM-SHA384",
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256] = "ECDHE-RSA-AES128-GCM-SHA256",
            [TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384] = "ECDHE-ECDSA-AES256-GCM-SHA384",
            [TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256] = "ECDHE-ECDSA-AES128-GCM-SHA256",
            [TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_GCM_SHA384] = "DHE-RSA-AES256-GCM-SHA384",
            [TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_GCM_SHA256] = "DHE-RSA-AES128-GCM-SHA256",
            [TlsCipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384] = "AES256-GCM-SHA384",
            [TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256] = "AES128-GCM-SHA256",
            [TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256] = "AES256-SHA256",
            [TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA256] = "AES128-SHA256",
            [TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA] = "AES256-SHA",
            [TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA] = "AES128-SHA",
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384] = "ECDHE-RSA-AES256-SHA384",
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256] = "ECDHE-RSA-AES128-SHA256",
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA] = "ECDHE-RSA-AES256-SHA",
            [TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA] = "ECDHE-RSA-AES128-SHA",
            [TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA] = "DES-CBC3-SHA",
            [TlsCipherSuite.TLS_RSA_WITH_RC4_128_SHA] = "RC4-SHA",
            [TlsCipherSuite.TLS_RSA_WITH_RC4_128_MD5] = "RC4-MD5",
        };

        foreach (var suite in Tls12CipherSuites)
        {
            ct.ThrowIfCancellationRequested();

            if (!opensslNames.TryGetValue(suite, out var opensslName))
                continue;

            try
            {
                using var cmd = sshClient.CreateCommand(
                    $"echo | openssl s_client -connect {host}:{port} -tls1_2 -cipher {opensslName} 2>&1 | head -5");
                cmd.CommandTimeout = TimeSpan.FromSeconds(8);
                cmd.Execute();
                var output = cmd.Result ?? string.Empty;

                if (output.Contains("CONNECTED", StringComparison.OrdinalIgnoreCase)
                    && !output.Contains("error", StringComparison.OrdinalIgnoreCase)
                    && !output.Contains("handshake failure", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(BuildCipherResult(suite));
                }
            }
            catch
            {
                // Individual cipher test failed — skip
            }
        }

        return results;
    }

    private CipherResult BuildCipherResult(TlsCipherSuite suite)
    {
        var info = ClassifyCipher(suite);
        var strengthBrush = info.Strength switch
        {
            "Strong" => "SuccessBrush",
            "Acceptable" => "WarningBrush",
            _ => "ErrorBrush",
        };

        return new CipherResult
        {
            Name = suite.ToString(),
            Strength = info.Strength switch
            {
                "Strong" => L("ToolTlsAuditStrong"),
                "Acceptable" => L("ToolTlsAuditAcceptable"),
                _ => L("ToolTlsAuditWeak"),
            },
            StrengthBrush = (Brush)FindResource(strengthBrush),
            KeyExchange = info.KeyExchange,
            Authentication = info.Auth,
            Encryption = info.Encryption,
        };
    }

    // ── Cipher classification ─────────────────────────────────────────

    private static (string Strength, string KeyExchange, string Auth, string Encryption) ClassifyCipher(
        TlsCipherSuite suite)
    {
        var name = suite.ToString();
        var kx = "RSA";
        var auth = "RSA";
        var enc = "AES";
        var strength = "Acceptable";

        if (name.Contains("ECDHE", StringComparison.Ordinal))
            kx = "ECDHE";
        else if (name.Contains("DHE", StringComparison.Ordinal))
            kx = "DHE";

        if (name.Contains("ECDSA", StringComparison.Ordinal))
            auth = "ECDSA";

        if (name.Contains("AES_256_GCM", StringComparison.Ordinal))
            enc = "AES-256-GCM";
        else if (name.Contains("AES_128_GCM", StringComparison.Ordinal))
            enc = "AES-128-GCM";
        else if (name.Contains("AES_256_CBC", StringComparison.Ordinal))
            enc = "AES-256-CBC";
        else if (name.Contains("AES_128_CBC", StringComparison.Ordinal))
            enc = "AES-128-CBC";
        else if (name.Contains("3DES", StringComparison.Ordinal))
            enc = "3DES-CBC";
        else if (name.Contains("RC4", StringComparison.Ordinal))
            enc = "RC4";

        // Determine strength: ECDHE/DHE + AEAD = Strong, static RSA or CBC = Acceptable, 3DES/RC4 = Weak
        if (enc.Contains("RC4", StringComparison.Ordinal) || enc.Contains("3DES", StringComparison.Ordinal))
        {
            strength = "Weak";
        }
        else if ((kx == "ECDHE" || kx == "DHE") && enc.Contains("GCM", StringComparison.Ordinal))
        {
            strength = "Strong";
        }

        return (strength, kx, auth, enc);
    }

    // ── Certificate retrieval ─────────────────────────────────────────

    private static async Task<X509Certificate2?> RetrieveCertificateAsync(
        string host, int port, Renci.SshNet.SshClient? tunnel, CancellationToken ct)
    {
        if (tunnel is not null)
        {
            return await Task.Run(() => RetrieveCertificateViaTunnel(tunnel, host, port, ct), ct);
        }

        return await Task.Run(() => RetrieveCertificateDirect(host, port, ct), ct);
    }

    private static X509Certificate2 RetrieveCertificateDirect(string host, int port, CancellationToken ct)
    {
        X509Certificate? remoteCert = null;
        using var tcp = new TcpClient();
        tcp.ConnectAsync(host, port, ct).AsTask().GetAwaiter().GetResult();

        using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) =>
        {
            remoteCert = cert;
            return true;
        });

        ssl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = host }, ct).GetAwaiter().GetResult();

        if (remoteCert == null)
        {
            throw new InvalidOperationException("No certificate received from the remote host.");
        }

        return new X509Certificate2(remoteCert);
    }

    private static X509Certificate2 RetrieveCertificateViaTunnel(
        Renci.SshNet.SshClient sshClient, string host, int port, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var cmd = sshClient.CreateCommand(
            $"echo | openssl s_client -connect {host}:{port} -servername {host} 2>/dev/null");
        cmd.CommandTimeout = TimeSpan.FromSeconds(10);
        cmd.Execute();
        var pemOutput = cmd.Result ?? string.Empty;

        var beginMarker = "-----BEGIN CERTIFICATE-----";
        var endMarker = "-----END CERTIFICATE-----";
        var beginIdx = pemOutput.IndexOf(beginMarker, StringComparison.Ordinal);
        var endIdx = pemOutput.IndexOf(endMarker, StringComparison.Ordinal);

        if (beginIdx < 0 || endIdx < 0)
        {
            throw new InvalidOperationException("No certificate received via tunnel.");
        }

        var pemBlock = pemOutput[beginIdx..(endIdx + endMarker.Length)];
        var base64 = pemBlock
            .Replace(beginMarker, "")
            .Replace(endMarker, "")
            .Replace("\r", "")
            .Replace("\n", "");

        var certBytes = Convert.FromBase64String(base64);
        return X509CertificateLoader.LoadCertificate(certBytes);
    }

    // ── Findings ──────────────────────────────────────────────────────

    private List<FindingItem> BuildFindings(List<ProtocolResult> protocols, List<CipherResult> ciphers)
    {
        var findings = new List<FindingItem>();
        var errorBrush = (Brush)FindResource("ErrorBrush");
        var warningBrush = (Brush)FindResource("WarningBrush");
        var successBrush = (Brush)FindResource("SuccessBrush");

        // Protocol findings
        if (protocols.Any(p => p is { Name: "SSL 3.0", Supported: true }))
        {
            findings.Add(new FindingItem
            {
                Icon = "\u26A0",
                Message = L("ToolTlsAuditFindingSsl3"),
                Brush = errorBrush,
            });
        }

        if (protocols.Any(p => p is { Name: "TLS 1.0", Supported: true }))
        {
            findings.Add(new FindingItem
            {
                Icon = "\u26A0",
                Message = L("ToolTlsAuditFindingTls10"),
                Brush = warningBrush,
            });
        }

        if (protocols.Any(p => p is { Name: "TLS 1.1", Supported: true }))
        {
            findings.Add(new FindingItem
            {
                Icon = "\u26A0",
                Message = L("ToolTlsAuditFindingTls11"),
                Brush = warningBrush,
            });
        }

        if (protocols.All(p => p.Name != "TLS 1.3" || !p.Supported))
        {
            findings.Add(new FindingItem
            {
                Icon = "\u2139",
                Message = L("ToolTlsAuditFindingNoTls13"),
                Brush = warningBrush,
            });
        }

        // Cipher findings
        var weakCiphers = ciphers.Where(c =>
            c.Name.Contains("3DES", StringComparison.Ordinal)
            || c.Name.Contains("RC4", StringComparison.Ordinal)).ToList();

        foreach (var wc in weakCiphers)
        {
            findings.Add(new FindingItem
            {
                Icon = "\u26A0",
                Message = string.Format(L("ToolTlsAuditFindingWeakCipher"), wc.Name),
                Brush = errorBrush,
            });
        }

        // PFS check: if any accepted cipher uses static RSA key exchange (no ECDHE/DHE)
        var nonPfsCiphers = ciphers.Where(c =>
            !c.Name.Contains("ECDHE", StringComparison.Ordinal)
            && !c.Name.Contains("DHE", StringComparison.Ordinal)).ToList();

        if (nonPfsCiphers.Count > 0)
        {
            findings.Add(new FindingItem
            {
                Icon = "\u2139",
                Message = L("ToolTlsAuditFindingNoPfs"),
                Brush = warningBrush,
            });
        }

        // All-good finding
        if (findings.Count == 0)
        {
            findings.Add(new FindingItem
            {
                Icon = "\u2714",
                Message = L("ToolTlsAuditFindingAllGood"),
                Brush = successBrush,
            });
        }

        return findings;
    }

    // ── Grading ───────────────────────────────────────────────────────

    private static string CalculateGrade(List<ProtocolResult> protocols, List<CipherResult> ciphers)
    {
        var ssl3 = protocols.Any(p => p is { Name: "SSL 3.0", Supported: true });
        var tls10 = protocols.Any(p => p is { Name: "TLS 1.0", Supported: true });
        var tls11 = protocols.Any(p => p is { Name: "TLS 1.1", Supported: true });
        var tls12 = protocols.Any(p => p is { Name: "TLS 1.2", Supported: true });
        var tls13 = protocols.Any(p => p is { Name: "TLS 1.3", Supported: true });

        var hasWeakCiphers = ciphers.Any(c =>
            c.Name.Contains("3DES", StringComparison.Ordinal)
            || c.Name.Contains("RC4", StringComparison.Ordinal));

        var hasCbcCiphers = ciphers.Any(c =>
            c.Name.Contains("CBC", StringComparison.Ordinal));

        var hasNonPfs = ciphers.Any(c =>
            !c.Name.Contains("ECDHE", StringComparison.Ordinal)
            && !c.Name.Contains("DHE", StringComparison.Ordinal));

        // F: SSL 3.0 supported
        if (ssl3) return "F";

        // D: Weak ciphers (RC4, 3DES)
        if (hasWeakCiphers) return "D";

        // C: Legacy TLS 1.0 or 1.1 enabled
        if (tls10 || tls11) return "C";

        // B: TLS 1.2+ with some CBC ciphers
        if (hasCbcCiphers) return "B";

        // A+: Only TLS 1.2+1.3, PFS-only, no weak ciphers
        if (tls12 && tls13 && !hasNonPfs) return "A+";

        // A: TLS 1.2+1.3, no weak ciphers
        if (tls12 || tls13) return "A";

        // T: No supported protocols (unreachable in practice)
        return "T";
    }

    // ── Display ───────────────────────────────────────────────────────

    private void DisplayResults(
        List<ProtocolResult> protocols,
        List<CipherResult> ciphers,
        X509Certificate2? cert,
        List<FindingItem> findings,
        string grade,
        string host,
        int port)
    {
        ResultsPanel.Visibility = Visibility.Visible;
        EmptyStatePanel.Visibility = Visibility.Collapsed;

        // Grade badge
        TxtGrade.Text = grade;
        GradeBadge.Background = GetGradeBrush(grade);

        // Protocols
        ProtocolList.ItemsSource = protocols;

        // Ciphers
        if (ciphers.Count > 0)
        {
            CipherList.ItemsSource = ciphers;
            CipherPanel.Visibility = Visibility.Visible;
        }

        // Certificate summary
        if (cert is not null)
        {
            TxtCertSubject.Text = cert.Subject;
            TxtCertIssuer.Text = cert.Issuer;
            var daysRemaining = (cert.NotAfter - DateTime.UtcNow).Days;
            TxtCertExpiry.Text = $"{cert.NotAfter:yyyy-MM-dd HH:mm:ss UTC} ({daysRemaining}d)";

            var keySize = GetPublicKeySize(cert);
            TxtCertKeySize.Text = keySize > 0 ? $"{keySize} bits" : "-";

            CertPanel.Visibility = Visibility.Visible;
        }

        // Findings
        if (findings.Count > 0)
        {
            FindingsList.ItemsSource = findings;
            FindingsPanel.Visibility = Visibility.Visible;
        }

        // Build report text
        _lastReport = BuildReportText(protocols, ciphers, cert, findings, grade, host, port);
        BtnCopy.Visibility = Visibility.Visible;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string GetRatingBrush(bool supported, string rating)
    {
        if (!supported)
            return "TextDisabledBrush";

        return rating switch
        {
            "critical" => "ErrorBrush",
            "weak" => "WarningBrush",
            "strong" => "SuccessBrush",
            _ => "TextPrimaryBrush",
        };
    }

    private Brush GetGradeBrush(string grade)
    {
        return grade switch
        {
            "A+" or "A" => (Brush)FindResource("SuccessBrush"),
            "B" => (Brush)FindResource("AccentBrush"),
            "C" => (Brush)FindResource("WarningBrush"),
            _ => (Brush)FindResource("ErrorBrush"),
        };
    }

    private static int GetPublicKeySize(X509Certificate2 cert)
    {
        try
        {
            var rsa = cert.GetRSAPublicKey();
            if (rsa is not null) return rsa.KeySize;

            var ecdsa = cert.GetECDsaPublicKey();
            if (ecdsa is not null) return ecdsa.KeySize;
        }
        catch
        {
            // Key algorithm not supported — return 0
        }
        return 0;
    }

    private static string BuildReportText(
        List<ProtocolResult> protocols,
        List<CipherResult> ciphers,
        X509Certificate2? cert,
        List<FindingItem> findings,
        string grade,
        string host,
        int port)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"SSL/TLS Audit Report: {host}:{port}");
        sb.AppendLine($"Grade: {grade}");
        sb.AppendLine();

        sb.AppendLine("=== Protocol Support ===");
        foreach (var p in protocols)
        {
            sb.AppendLine($"  {p.Name}: {(p.Supported ? "Supported" : "Not supported")}");
        }
        sb.AppendLine();

        if (ciphers.Count > 0)
        {
            sb.AppendLine("=== Cipher Suites (TLS 1.2) ===");
            foreach (var c in ciphers)
            {
                sb.AppendLine($"  {c.Name}  [{c.KeyExchange}/{c.Authentication}/{c.Encryption}]");
            }
            sb.AppendLine();
        }

        if (cert is not null)
        {
            sb.AppendLine("=== Certificate ===");
            sb.AppendLine($"  Subject: {cert.Subject}");
            sb.AppendLine($"  Issuer: {cert.Issuer}");
            sb.AppendLine($"  Expires: {cert.NotAfter:yyyy-MM-dd HH:mm:ss UTC}");
            var keySize = GetPublicKeySize(cert);
            if (keySize > 0)
                sb.AppendLine($"  Key size: {keySize} bits");
            sb.AppendLine();
        }

        if (findings.Count > 0)
        {
            sb.AppendLine("=== Findings ===");
            foreach (var f in findings)
            {
                sb.AppendLine($"  {f.Icon} {f.Message}");
            }
        }

        return sb.ToString();
    }

    // ── Copy ──────────────────────────────────────────────────────────

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastReport))
        {
            Clipboard.SetText(_lastReport);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────

    public bool CanClose() => !_isAuditing;

    private string L(string key) => _localizer?[key] ?? key;

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
}
