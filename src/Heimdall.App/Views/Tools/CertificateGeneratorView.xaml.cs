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

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Generates self-signed X.509 certificates for internal labs. Supports
/// single self-signed leaf or CA + leaf pair modes with RSA 2048/4096.
/// </summary>
public partial class CertificateGeneratorView : UserControl, IToolView
{
    private const int Rsa2048KeySize = 2048;
    private const int Rsa4096KeySize = 4096;
    private const int KeySizeIndexRsa2048 = 0;
    private const int DefaultValidityDays = 365;
    private const int CaValidityDays = 3650;
    private const int SerialNumberLength = 16;
    private const byte PositiveMsbMask = 0x7F;
    private const string MaskedPlaceholder = "********";
    private const string PemFileFilter = "PEM Certificate (*.pem)|*.pem|All Files (*.*)|*.*";
    private const string PfxFileFilter = "PFX/PKCS#12 (*.pfx)|*.pfx|All Files (*.*)|*.*";

    private LocalizationManager? _localizer;

    // Self-signed mode results
    private string _certPem = string.Empty;
    private string _keyPem = string.Empty;
    private string _fingerprint = string.Empty;
    private bool _keyVisible;

    // CA+Leaf mode results
    private string _caCertPem = string.Empty;
    private string _caKeyPem = string.Empty;
    private string _leafCertPem = string.Empty;
    private string _leafKeyPem = string.Empty;
    private bool _leafKeyVisible;

    // For PFX export
    private X509Certificate2? _exportCert;
    private RSA? _exportKey;
    private bool _isCaLeafMode;

    public CertificateGeneratorView()
    {
        InitializeComponent();
        CnInput.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnGenerateClick(s, e); };
    }

    /// <summary>
    /// Initializes the view with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolCertGenTitle");
        SubjectSectionLabel.Text = L("ToolCertGenSubjectSection");
        CnLabel.Text = L("ToolCertGenCn");
        OrgLabel.Text = L("ToolCertGenOrg");
        CountryLabel.Text = L("ToolCertGenCountry");
        OptionsSectionLabel.Text = L("ToolCertGenOptionsSection");
        KeySizeLabel.Text = L("ToolCertGenKeySize");
        CertRsa2048Item.Content = L("ToolCertGenRsa2048");
        CertRsa4096Item.Content = L("ToolCertGenRsa4096");
        ValidityLabel.Text = L("ToolCertGenValidity");
        SanLabel.Text = L("ToolCertGenSan");
        SanHint.Text = L("ToolCertGenSanHint");
        TypeSectionLabel.Text = L("ToolCertGenTypeSection");
        RadioSelfSigned.Content = L("ToolCertGenTypeSelfSigned");
        RadioCaLeaf.Content = L("ToolCertGenTypeCaLeaf");
        BtnGenerate.Content = L("ToolCertGenBtnGenerate");
        FingerprintLabel.Text = L("ToolCertGenFingerprint");
        BtnCopyFingerprint.Content = L("ToolCertGenBtnCopy");
        CertLabel.Text = L("ToolCertGenCertPem");
        BtnCopyCert.Content = L("ToolCertGenBtnCopy");
        KeyLabel.Text = L("ToolCertGenKeyPem");
        BtnCopyKey.Content = L("ToolCertGenBtnCopy");
        BtnShowKey.Content = L("ToolCertGenBtnShow");
        LeafCertLabel.Text = L("ToolCertGenLeafCertPem");
        BtnCopyLeafCert.Content = L("ToolCertGenBtnCopy");
        LeafKeyLabel.Text = L("ToolCertGenLeafKeyPem");
        BtnCopyLeafKey.Content = L("ToolCertGenBtnCopy");
        BtnShowLeafKey.Content = L("ToolCertGenBtnShow");
        BtnSavePem.Content = L("ToolCertGenBtnSavePem");
        BtnSavePfx.Content = L("ToolCertGenBtnSavePfx");

        // Accessibility
        System.Windows.Automation.AutomationProperties.SetName(BtnGenerate, L("ToolCertGenBtnGenerate"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyFingerprint, L("ToolCertGenBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyCert, L("ToolCertGenBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyKey, L("ToolCertGenBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnShowKey, L("ToolCertGenBtnShow"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyLeafCert, L("ToolCertGenBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyLeafKey, L("ToolCertGenBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnShowLeafKey, L("ToolCertGenBtnShow"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSavePem, L("ToolCertGenBtnSavePem"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSavePfx, L("ToolCertGenBtnSavePfx"));
        System.Windows.Automation.AutomationProperties.SetName(CnInput, L("ToolCertGenCn"));
        System.Windows.Automation.AutomationProperties.SetName(OrgInput, L("ToolCertGenOrg"));
        System.Windows.Automation.AutomationProperties.SetName(CountryInput, L("ToolCertGenCountry"));
        System.Windows.Automation.AutomationProperties.SetName(KeySizeCombo, L("ToolCertGenKeySize"));
        System.Windows.Automation.AutomationProperties.SetName(ValidityInput, L("ToolCertGenValidity"));
        System.Windows.Automation.AutomationProperties.SetName(SanInput, L("ToolCertGenSan"));
        System.Windows.Automation.AutomationProperties.SetName(FingerprintOutput, L("ToolCertGenFingerprint"));
        System.Windows.Automation.AutomationProperties.SetName(CertOutput, L("ToolCertGenCertPem"));
        System.Windows.Automation.AutomationProperties.SetName(KeyOutput, L("ToolCertGenKeyPem"));
        System.Windows.Automation.AutomationProperties.SetName(RadioSelfSigned, L("ToolCertGenTypeSelfSigned"));
        System.Windows.Automation.AutomationProperties.SetName(RadioCaLeaf, L("ToolCertGenTypeCaLeaf"));
        System.Windows.Automation.AutomationProperties.SetName(LeafCertOutput, L("ToolCertGenLeafCertPem"));
        System.Windows.Automation.AutomationProperties.SetName(LeafKeyOutput, L("ToolCertGenLeafKeyPem"));

        BtnCopyFingerprint.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyCert.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyKey.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyLeafCert.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyLeafKey.ToolTip = L("ToolBtnCopyToClipboard");

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        var cn = CnInput.Text.Trim();
        if (string.IsNullOrEmpty(cn))
        {
            MessageBox.Show(
                L("ToolCertGenErrorCnRequired"),
                L("ToolCertGenTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(ValidityInput.Text.Trim(), out var validityDays) || validityDays < 1)
        {
            MessageBox.Show(
                L("ToolCertGenErrorInvalidValidity"),
                L("ToolCertGenTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var org = OrgInput.Text.Trim();
        var country = CountryInput.Text.Trim();
        var keySize = KeySizeCombo.SelectedIndex == KeySizeIndexRsa2048 ? Rsa2048KeySize : Rsa4096KeySize;

        var sans = ParseSans(SanInput.Text);

        _isCaLeafMode = RadioCaLeaf.IsChecked == true;

        try
        {
            if (_isCaLeafMode)
            {
                GenerateCaLeafPair(cn, org, country, keySize, validityDays, sans);
            }
            else
            {
                GenerateSelfSigned(cn, org, country, keySize, validityDays, sans);
            }
        }
        catch (CryptographicException ex)
        {
            MessageBox.Show(
                string.Format(L("ToolCertGenErrorGeneration"), ex.Message),
                L("ToolCertGenTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void GenerateSelfSigned(string cn, string org, string country, int keySize, int validityDays, string[] sans)
    {
        using var rsa = RSA.Create(keySize);
        var subject = BuildDistinguishedName(cn, org, country);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Basic constraints: leaf
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        // Key usage
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

        // Extended key usage: server + client auth
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [
                    new Oid("1.3.6.1.5.5.7.3.1"), // serverAuth
                    new Oid("1.3.6.1.5.5.7.3.2")  // clientAuth
                ],
                false));

        // SANs
        AddSans(request, sans);

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(validityDays));

        _certPem = cert.ExportCertificatePem();
        _keyPem = rsa.ExportPkcs8PrivateKeyPem();
        _fingerprint = ComputeSha256Fingerprint(cert);

        // Store for PFX export
        DisposeExportResources();
        _exportKey = RSA.Create();
        _exportKey.ImportPkcs8PrivateKey(rsa.ExportPkcs8PrivateKey(), out _);
        _exportCert = cert;

        ShowSelfSignedResults();
    }

    private void GenerateCaLeafPair(string cn, string org, string country, int keySize, int validityDays, string[] sans)
    {
        // --- CA certificate ---
        using var caRsa = RSA.Create(keySize);
        var caSubject = BuildDistinguishedName($"{cn} CA", org, country);
        var caRequest = new CertificateRequest(caSubject, caRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        caRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        var caCert = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(CaValidityDays));

        _caCertPem = caCert.ExportCertificatePem();
        _caKeyPem = caRsa.ExportPkcs8PrivateKeyPem();

        // --- Leaf certificate signed by CA ---
        using var leafRsa = RSA.Create(keySize);
        var leafSubject = BuildDistinguishedName(cn, org, country);
        var leafRequest = new CertificateRequest(leafSubject, leafRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        leafRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        leafRequest.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [
                    new Oid("1.3.6.1.5.5.7.3.1"),
                    new Oid("1.3.6.1.5.5.7.3.2")
                ],
                false));

        AddSans(leafRequest, sans);

        var serial = new byte[SerialNumberLength];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= PositiveMsbMask;

        using var leafCert = leafRequest.Create(
            caCert,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(validityDays),
            serial);

        _leafCertPem = leafCert.ExportCertificatePem();
        _leafKeyPem = leafRsa.ExportPkcs8PrivateKeyPem();
        _fingerprint = ComputeSha256Fingerprint(leafCert);

        // Store leaf with key for PFX export
        DisposeExportResources();
        using var leafWithKey = leafCert.CopyWithPrivateKey(leafRsa);
        _exportCert = X509CertificateLoader.LoadPkcs12(
            leafWithKey.Export(X509ContentType.Pfx, string.Empty),
            string.Empty,
            X509KeyStorageFlags.Exportable);
        _exportKey = null; // PFX export uses _exportCert directly

        ShowCaLeafResults();
    }

    private void ShowSelfSignedResults()
    {
        FingerprintOutput.Text = _fingerprint;
        CertOutput.Text = _certPem;

        _keyVisible = false;
        KeyOutput.Text = MaskedPlaceholder;
        BtnShowKey.Content = L("ToolCertGenBtnShow");

        CertLabel.Text = L("ToolCertGenCertPem");

        FingerprintPanel.Visibility = Visibility.Visible;
        CertPanel.Visibility = Visibility.Visible;
        KeyPanel.Visibility = Visibility.Visible;
        LeafPanel.Visibility = Visibility.Collapsed;
        ExportPanel.Visibility = Visibility.Visible;
    }

    private void ShowCaLeafResults()
    {
        FingerprintOutput.Text = _fingerprint;

        // CA cert and key go in the main cert/key panels
        CertOutput.Text = _caCertPem;
        CertLabel.Text = L("ToolCertGenCaCertPem");

        _keyVisible = false;
        KeyOutput.Text = MaskedPlaceholder;
        BtnShowKey.Content = L("ToolCertGenBtnShow");
        KeyLabel.Text = L("ToolCertGenCaKeyPem");

        // Leaf cert and key go in the leaf panel
        LeafCertOutput.Text = _leafCertPem;
        _leafKeyVisible = false;
        LeafKeyOutput.Text = MaskedPlaceholder;
        BtnShowLeafKey.Content = L("ToolCertGenBtnShow");

        FingerprintPanel.Visibility = Visibility.Visible;
        CertPanel.Visibility = Visibility.Visible;
        KeyPanel.Visibility = Visibility.Visible;
        LeafPanel.Visibility = Visibility.Visible;
        ExportPanel.Visibility = Visibility.Visible;
    }

    private void OnCopyFingerprintClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_fingerprint))
        {
            try { Clipboard.SetText(_fingerprint); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnCopyCertClick(object sender, RoutedEventArgs e)
    {
        var text = _isCaLeafMode ? _caCertPem : _certPem;
        if (!string.IsNullOrEmpty(text))
        {
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnCopyKeyClick(object sender, RoutedEventArgs e)
    {
        var text = _isCaLeafMode ? _caKeyPem : _keyPem;
        if (!string.IsNullOrEmpty(text))
        {
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnToggleKeyClick(object sender, RoutedEventArgs e)
    {
        _keyVisible = !_keyVisible;
        var key = _isCaLeafMode ? _caKeyPem : _keyPem;
        KeyOutput.Text = _keyVisible ? key : MaskedPlaceholder;
        BtnShowKey.Content = _keyVisible
            ? L("ToolCertGenBtnHide")
            : L("ToolCertGenBtnShow");
    }

    private void OnCopyLeafCertClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_leafCertPem))
        {
            try { Clipboard.SetText(_leafCertPem); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnCopyLeafKeyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_leafKeyPem))
        {
            try { Clipboard.SetText(_leafKeyPem); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnToggleLeafKeyClick(object sender, RoutedEventArgs e)
    {
        _leafKeyVisible = !_leafKeyVisible;
        LeafKeyOutput.Text = _leafKeyVisible ? _leafKeyPem : MaskedPlaceholder;
        BtnShowLeafKey.Content = _leafKeyVisible
            ? L("ToolCertGenBtnHide")
            : L("ToolCertGenBtnShow");
    }

    private void OnSavePemClick(object sender, RoutedEventArgs e)
    {
        var certToSave = _isCaLeafMode ? _leafCertPem : _certPem;
        if (string.IsNullOrEmpty(certToSave)) return;

        var dialog = new SaveFileDialog
        {
            FileName = "certificate.pem",
            Filter = PemFileFilter,
            DefaultExt = ".pem"
        };

        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, certToSave, System.Text.Encoding.UTF8);
        }
    }

    private void OnSavePfxClick(object sender, RoutedEventArgs e)
    {
        if (_exportCert is null) return;

        var pfxPassword = PromptPfxPassword();
        if (pfxPassword is null) return; // user cancelled

        var dialog = new SaveFileDialog
        {
            FileName = "certificate.pfx",
            Filter = PfxFileFilter,
            DefaultExt = ".pfx"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                byte[] pfxBytes;
                if (_exportKey is not null)
                {
                    // Self-signed mode: combine cert with key
                    using var certWithKey = _exportCert.CopyWithPrivateKey(_exportKey);
                    pfxBytes = certWithKey.Export(X509ContentType.Pfx, pfxPassword);
                }
                else
                {
                    // CA+Leaf mode: _exportCert already has the private key
                    pfxBytes = _exportCert.Export(X509ContentType.Pfx, pfxPassword);
                }

                System.IO.File.WriteAllBytes(dialog.FileName, pfxBytes);
            }
            catch (CryptographicException ex)
            {
                MessageBox.Show(
                    string.Format(L("ToolCertGenErrorExport"), ex.Message),
                    L("ToolCertGenTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Shows a simple input dialog to get the PFX password from the user.
    /// Returns null if the user cancels.
    /// </summary>
    private string? PromptPfxPassword()
    {
        var dialog = new Window
        {
            Title = L("ToolCertGenPfxPasswordTitle"),
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Window.GetWindow(this)
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        var label = new TextBlock
        {
            Text = L("ToolCertGenPfxPasswordPrompt"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var passwordBox = new PasswordBox { Padding = new Thickness(4, 2, 4, 2) };
        var buttonPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        string? result = null;

        var btnOk = new Button
        {
            Content = L("ToolCertGenBtnOk"),
            Padding = new Thickness(16, 4, 16, 4),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        btnOk.Click += (_, _) => { result = passwordBox.Password; dialog.DialogResult = true; };

        var btnCancel = new Button
        {
            Content = L("ToolCertGenBtnCancel"),
            Padding = new Thickness(16, 4, 16, 4),
            IsCancel = true
        };

        buttonPanel.Children.Add(btnOk);
        buttonPanel.Children.Add(btnCancel);
        panel.Children.Add(label);
        panel.Children.Add(passwordBox);
        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        if (dialog.ShowDialog() == true)
        {
            return result;
        }

        return null;
    }

    private static X500DistinguishedName BuildDistinguishedName(string cn, string org, string country)
    {
        var parts = new System.Collections.Generic.List<string> { $"CN={cn}" };
        if (!string.IsNullOrWhiteSpace(org))
            parts.Add($"O={org}");
        if (!string.IsNullOrWhiteSpace(country))
            parts.Add($"C={country}");

        return new X500DistinguishedName(string.Join(", ", parts));
    }

    private static string[] ParseSans(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        return input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToArray();
    }

    private static void AddSans(CertificateRequest request, string[] sans)
    {
        if (sans.Length == 0) return;

        var sanBuilder = new SubjectAlternativeNameBuilder();
        foreach (var san in sans)
        {
            if (IPAddress.TryParse(san, out var ip))
                sanBuilder.AddIpAddress(ip);
            else
                sanBuilder.AddDnsName(san);
        }
        request.CertificateExtensions.Add(sanBuilder.Build());
    }

    private static string ComputeSha256Fingerprint(X509Certificate2 cert)
    {
        var hash = cert.GetCertHash(HashAlgorithmName.SHA256);
        return "SHA256:" + BitConverter.ToString(hash).Replace("-", ":");
    }

    private void DisposeExportResources()
    {
        _exportCert?.Dispose();
        _exportCert = null;
        _exportKey?.Dispose();
        _exportKey = null;
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpCERTGEN");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        DisposeExportResources();
        _keyPem = string.Empty;
        _caKeyPem = string.Empty;
        _leafKeyPem = string.Empty;
        GC.SuppressFinalize(this);
    }
}
