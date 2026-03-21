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
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// SSL/TLS certificate inspector that retrieves and displays certificate details
/// for any host:port combination.
/// </summary>
public partial class CertInspectorView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private string _lastDetails = string.Empty;

    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private const int DaysWarningThreshold = 30;

    /// <summary>
    /// Holds the full result of an SSL/TLS certificate inspection.
    /// </summary>
    private sealed record CertInspectionResult(
        X509Certificate2 Certificate,
        SslProtocols TlsProtocol,
        List<ChainCertInfo> ChainElements);

    /// <summary>
    /// Holds summary information for a single certificate in the chain.
    /// </summary>
    public sealed class ChainCertInfo
    {
        public string Subject { get; init; } = string.Empty;
        public string Expiry { get; init; } = string.Empty;
    }

    public CertInspectorView()
    {
        InitializeComponent();
        TxtHost.KeyDown += OnInputKeyDown;
        TxtPort.KeyDown += OnInputKeyDown;
    }

    /// <summary>
    /// Initializes the view with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        // Pre-fill with a sensible default; context overrides if provided
        TxtHost.Text = "google.com";

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

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHost.Focus();
            TxtHost.SelectAll();
        });
    }

    private void ParseArgument(string argument)
    {
        var trimmed = argument.Trim();

        // Try host:port format
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

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolCertTitle");
        LblHost.Text = L("ToolCertHostLabel");
        BtnCheck.Content = L("ToolCertBtnCheck");
        LblSubject.Text = L("ToolCertSubject");
        LblIssuer.Text = L("ToolCertIssuer");
        LblValidFrom.Text = L("ToolCertValidFrom");
        LblValidTo.Text = L("ToolCertValidTo");
        LblSerial.Text = L("ToolCertSerial");
        LblThumbprint.Text = L("ToolCertThumbprint");
        LblSigAlg.Text = L("ToolCertSigAlg");
        LblKeySize.Text = L("ToolCertKeySize");
        LblSans.Text = L("ToolCertSans");
        BtnCopy.Content = L("ToolCertBtnCopy");
        LblTlsVersion.Text = L("ToolCertTlsVersion");
        LblChainTitle.Text = L("ToolCertChainTitle");

        AutomationProperties.SetName(BtnCheck, L("ToolCertBtnCheck"));
        AutomationProperties.SetName(TxtHost, L("ToolCertHostLabel"));
        AutomationProperties.SetName(TxtPort, L("ToolCertPortLabel"));
        AutomationProperties.SetName(BtnCopy, L("ToolCertBtnCopy"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = CheckCertificateAsync();
            e.Handled = true;
        }
    }

    private void OnCheckClick(object sender, RoutedEventArgs e)
    {
        _ = CheckCertificateAsync();
    }

    private async Task CheckCertificateAsync()
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

        // Cancel any previous request
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource(ConnectionTimeout);

        TxtError.Visibility = Visibility.Collapsed;
        DetailsPanel.Visibility = Visibility.Collapsed;
        ExpirationBanner.Visibility = Visibility.Collapsed;
        TlsHostPanel.Visibility = Visibility.Collapsed;
        ChainPanel.Visibility = Visibility.Collapsed;
        BtnCopy.Visibility = Visibility.Collapsed;
        LoadingBar.Visibility = Visibility.Visible;
        BtnCheck.IsEnabled = false;

        try
        {
            var result = await Task.Run(() => RetrieveCertificate(host, port, _cts.Token), _cts.Token);
            DisplayCertificate(result, host);
        }
        catch (OperationCanceledException)
        {
            TxtError.Text = L("ToolCertErrorTimeout");
            TxtError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"CertInspector certificate retrieval failed: {ex.Message}");
            TxtError.Text = string.Format(L("ToolCertErrorConnection"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            BtnCheck.IsEnabled = true;
        }
    }

    private static CertInspectionResult RetrieveCertificate(string host, int port, CancellationToken ct)
    {
        X509Certificate? remoteCert = null;

        using var tcp = new TcpClient();
        tcp.ConnectAsync(host, port, ct).AsTask().GetAwaiter().GetResult();

        using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) =>
        {
            remoteCert = cert;
            return true;
        });

        ssl.AuthenticateAsClientAsync(host).GetAwaiter().GetResult();

        if (remoteCert == null)
        {
            throw new InvalidOperationException("No certificate received from the remote host.");
        }

        var cert2 = new X509Certificate2(remoteCert);
        var tlsProtocol = ssl.SslProtocol;

        // Build certificate chain
        var chainElements = new List<ChainCertInfo>();
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(cert2);

        foreach (var element in chain.ChainElements)
        {
            chainElements.Add(new ChainCertInfo
            {
                Subject = element.Certificate.Subject,
                Expiry = element.Certificate.NotAfter.ToString("yyyy-MM-dd HH:mm:ss UTC")
            });
        }

        return new CertInspectionResult(cert2, tlsProtocol, chainElements);
    }

    private void DisplayCertificate(CertInspectionResult result, string host)
    {
        using var cert = result.Certificate;

        TxtSubject.Text = cert.Subject;
        TxtIssuer.Text = cert.Issuer;
        TxtValidFrom.Text = cert.NotBefore.ToString("yyyy-MM-dd HH:mm:ss UTC");
        TxtSerial.Text = cert.SerialNumber;
        TxtSigAlg.Text = cert.SignatureAlgorithm.FriendlyName ?? cert.SignatureAlgorithm.Value ?? "-";

        // Key size
        var keySize = GetPublicKeySize(cert);
        TxtKeySize.Text = keySize > 0 ? $"{keySize} bits" : "-";

        // SHA-256 thumbprint
        var sha256Bytes = cert.GetCertHash(HashAlgorithmName.SHA256);
        TxtThumbprint.Text = Convert.ToHexString(sha256Bytes);

        // Validity and expiration
        var daysRemaining = (cert.NotAfter - DateTime.UtcNow).Days;
        TxtValidTo.Text = $"{cert.NotAfter:yyyy-MM-dd HH:mm:ss UTC} ({daysRemaining} {L("ToolCertDaysRemaining")})";

        UpdateExpirationBanner(daysRemaining);

        // Subject Alternative Names (OID 2.5.29.17)
        var sans = ExtractSans(cert);
        SansList.ItemsSource = sans.Count > 0 ? sans : ["-"];

        // TLS version
        TxtTlsVersion.Text = FormatTlsProtocol(result.TlsProtocol);

        // Hostname match validation
        var hostnameMatches = CheckHostnameMatch(cert, sans, host);
        if (hostnameMatches)
        {
            TxtHostnameMatch.Text = "\u2714 " + L("ToolCertHostnameMatch");
            TxtHostnameMatch.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            TxtHostnameMatch.Text = "\u2716 " + L("ToolCertHostnameMismatch");
            TxtHostnameMatch.Foreground = (Brush)FindResource("ErrorBrush");
        }

        TlsHostPanel.Visibility = Visibility.Visible;

        // Certificate chain
        ChainList.ItemsSource = result.ChainElements;
        ChainPanel.Visibility = result.ChainElements.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Build copyable details
        _lastDetails = BuildDetailsText(cert, host, sha256Bytes, sans, daysRemaining, result.TlsProtocol, hostnameMatches);

        DetailsPanel.Visibility = Visibility.Visible;
        BtnCopy.Visibility = Visibility.Visible;
    }

    private static string FormatTlsProtocol(SslProtocols protocol)
    {
        return protocol switch
        {
            SslProtocols.Tls12 => "TLS 1.2",
            SslProtocols.Tls13 => "TLS 1.3",
#pragma warning disable CA5397, CS0618, SYSLIB0039
            SslProtocols.Tls11 => "TLS 1.1",
            SslProtocols.Tls => "TLS 1.0",
            SslProtocols.Ssl3 => "SSL 3.0",
            SslProtocols.Ssl2 => "SSL 2.0",
#pragma warning restore CA5397, CS0618, SYSLIB0039
            _ => protocol.ToString()
        };
    }

    private static bool CheckHostnameMatch(X509Certificate2 cert, List<string> sans, string host)
    {
        // Check SANs first (preferred per RFC 6125)
        foreach (var san in sans)
        {
            if (MatchesHostname(san, host))
            {
                return true;
            }
        }

        // Fall back to CN in Subject
        var cn = ExtractCn(cert.Subject);
        if (!string.IsNullOrEmpty(cn) && MatchesHostname(cn, host))
        {
            return true;
        }

        return false;
    }

    private static bool MatchesHostname(string pattern, string host)
    {
        if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Wildcard matching: *.example.com matches sub.example.com
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..]; // .example.com
            var dotIndex = host.IndexOf('.', StringComparison.Ordinal);
            if (dotIndex > 0)
            {
                var hostSuffix = host[dotIndex..];
                return string.Equals(suffix, hostSuffix, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static string ExtractCn(string subject)
    {
        // Parse CN= from the subject string
        const string cnPrefix = "CN=";
        var startIndex = subject.IndexOf(cnPrefix, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        startIndex += cnPrefix.Length;
        var endIndex = subject.IndexOf(',', startIndex);
        return endIndex < 0 ? subject[startIndex..].Trim() : subject[startIndex..endIndex].Trim();
    }

    private void UpdateExpirationBanner(int daysRemaining)
    {
        ExpirationBanner.Visibility = Visibility.Visible;

        if (daysRemaining < 0)
        {
            ExpirationBanner.Background = (Brush)FindResource("ErrorBrush");
            TxtExpiration.Text = L("ToolCertExpired");
        }
        else if (daysRemaining <= DaysWarningThreshold)
        {
            ExpirationBanner.Background = (Brush)FindResource("WarningBrush");
            TxtExpiration.Text = string.Format(L("ToolCertExpiringSoon"), daysRemaining);
        }
        else
        {
            ExpirationBanner.Background = (Brush)FindResource("SuccessBrush");
            TxtExpiration.Text = string.Format(L("ToolCertValid"), daysRemaining);
        }
    }

    private static List<string> ExtractSans(X509Certificate2 cert)
    {
        var sans = new List<string>();
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value != "2.5.29.17") continue;

            var sanExt = (X509SubjectAlternativeNameExtension)ext;
            foreach (var name in sanExt.EnumerateDnsNames())
            {
                sans.Add(name);
            }

            foreach (var ip in sanExt.EnumerateIPAddresses())
            {
                sans.Add(ip.ToString());
            }
        }

        return sans;
    }

    private static int GetPublicKeySize(X509Certificate2 cert)
    {
        using var rsa = cert.GetRSAPublicKey();
        if (rsa != null) return rsa.KeySize;

        using var ecdsa = cert.GetECDsaPublicKey();
        if (ecdsa != null) return ecdsa.KeySize;

        return 0;
    }

    private string BuildDetailsText(X509Certificate2 cert, string host, byte[] sha256Bytes, List<string> sans, int daysRemaining, SslProtocols tlsProtocol, bool hostnameMatches)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{L("ToolCertDetailHost")}: {host}");
        sb.AppendLine($"{L("ToolCertTlsVersion")}: {FormatTlsProtocol(tlsProtocol)}");
        sb.AppendLine($"{(hostnameMatches ? L("ToolCertHostnameMatch") : L("ToolCertHostnameMismatch"))}");
        sb.AppendLine($"{L("ToolCertSubject")}: {cert.Subject}");
        sb.AppendLine($"{L("ToolCertIssuer")}: {cert.Issuer}");
        sb.AppendLine($"{L("ToolCertValidFrom")}: {cert.NotBefore:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"{L("ToolCertValidTo")}: {cert.NotAfter:yyyy-MM-dd HH:mm:ss UTC} ({daysRemaining} {L("ToolCertDaysRemaining")})");
        sb.AppendLine($"{L("ToolCertSerial")}: {cert.SerialNumber}");
        sb.AppendLine($"SHA-256: {Convert.ToHexString(sha256Bytes)}");
        sb.AppendLine($"{L("ToolCertSigAlg")}: {cert.SignatureAlgorithm.FriendlyName}");
        sb.AppendLine($"{L("ToolCertKeySize")}: {GetPublicKeySize(cert)} bits");

        if (sans.Count > 0)
        {
            sb.AppendLine($"{L("ToolCertSans")}: {string.Join(", ", sans)}");
        }

        return sb.ToString();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastDetails))
        {
            Clipboard.SetText(_lastDetails);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }
}
