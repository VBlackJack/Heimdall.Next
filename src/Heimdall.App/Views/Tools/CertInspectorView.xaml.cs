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

using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
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
public partial class CertInspectorView : UserControl, IDisposable
{
    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private string _lastDetails = string.Empty;

    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private const int DaysWarningThreshold = 30;

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
        BtnCopy.Visibility = Visibility.Collapsed;
        LoadingBar.Visibility = Visibility.Visible;
        BtnCheck.IsEnabled = false;

        try
        {
            var cert = await Task.Run(() => RetrieveCertificate(host, port, _cts.Token), _cts.Token);
            DisplayCertificate(cert, host);
        }
        catch (OperationCanceledException)
        {
            TxtError.Text = L("ToolCertErrorTimeout");
            TxtError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Certificate retrieval failed: {ex.Message}");
            TxtError.Text = string.Format(L("ToolCertErrorConnection"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            BtnCheck.IsEnabled = true;
        }
    }

    private static X509Certificate2 RetrieveCertificate(string host, int port, CancellationToken ct)
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

        return new X509Certificate2(remoteCert);
    }

    private void DisplayCertificate(X509Certificate2 cert, string host)
    {
        using (cert)
        {
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

            // Build copyable details
            _lastDetails = BuildDetailsText(cert, host, sha256Bytes, sans, daysRemaining);

            DetailsPanel.Visibility = Visibility.Visible;
            BtnCopy.Visibility = Visibility.Visible;
        }
    }

    private void UpdateExpirationBanner(int daysRemaining)
    {
        ExpirationBanner.Visibility = Visibility.Visible;

        if (daysRemaining < 0)
        {
            ExpirationBanner.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69));
            TxtExpiration.Text = L("ToolCertExpired");
        }
        else if (daysRemaining <= DaysWarningThreshold)
        {
            ExpirationBanner.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0));
            TxtExpiration.Text = string.Format(L("ToolCertExpiringSoon"), daysRemaining);
        }
        else
        {
            ExpirationBanner.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69));
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

    private string BuildDetailsText(X509Certificate2 cert, string host, byte[] sha256Bytes, List<string> sans, int daysRemaining)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{L("ToolCertDetailHost")}: {host}");
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
