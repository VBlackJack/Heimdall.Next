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

using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Parses SSH keys (public and private, OpenSSH and PEM formats) and audits
/// their security strength. Displays algorithm, key size, fingerprint, format,
/// encryption status, and actionable security recommendations.
/// </summary>
public partial class SshKeyAuditView : UserControl, IToolView
{
    private const int DebounceDelayMs = 200;
    private const int MinRsaKeySize = 2048;
    private const int StrongRsaKeySize = 3072;
    private const int StrongEcdsaKeySize = 384;
    private const int Ed25519KeySize = 256;
    private const int MaxKeyFileSize = 1_048_576; // 1 MB

    private const string OpenSshRsaPrefix = "ssh-rsa";
    private const string OpenSshDsaPrefix = "ssh-dss";
    private const string OpenSshEd25519Prefix = "ssh-ed25519";
    private const string OpenSshEcdsaNistp256Prefix = "ecdsa-sha2-nistp256";
    private const string OpenSshEcdsaNistp384Prefix = "ecdsa-sha2-nistp384";
    private const string OpenSshEcdsaNistp521Prefix = "ecdsa-sha2-nistp521";

    private const string PemBeginOpenSshPrivate = "-----BEGIN OPENSSH PRIVATE KEY-----";
    private const string PemBeginRsaPrivate = "-----BEGIN RSA PRIVATE KEY-----";
    private const string PemBeginDsaPrivate = "-----BEGIN DSA PRIVATE KEY-----";
    private const string PemBeginEcPrivate = "-----BEGIN EC PRIVATE KEY-----";
    private const string PemBeginPrivate = "-----BEGIN PRIVATE KEY-----";
    private const string PemBeginEncryptedPrivate = "-----BEGIN ENCRYPTED PRIVATE KEY-----";
    private const string PemBeginPublic = "-----BEGIN PUBLIC KEY-----";
    private const string PemBeginRsaPublic = "-----BEGIN RSA PUBLIC KEY-----";

    private const string ProcTypeEncrypted = "Proc-Type: 4,ENCRYPTED";
    private const string OpenSshKdfBcrypt = "bcrypt";

    private const string KeyFileFilter =
        "SSH Key Files (*.pem;*.pub;*.key)|*.pem;*.pub;*.key|All Files (*.*)|*.*";

    private const string RatingStrong = "strong";
    private const string RatingAcceptable = "acceptable";
    private const string RatingWeak = "weak";
    private const string RatingDeprecated = "deprecated";

    private const string FindingIconPass = "\u2713";   // checkmark
    private const string FindingIconWarn = "\u26A0";   // warning
    private const string FindingIconFail = "\u2717";   // cross

    private LocalizationManager? _localizer;
    private DispatcherTimer? _debounceTimer;
    private string _fingerprint = string.Empty;

    public SshKeyAuditView()
    {
        InitializeComponent();
        InitializeDebounceTimer();
    }

    /// <inheritdoc/>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolSshAuditTitle");
        InputLabel.Text = L("ToolSshAuditInput");
        BtnBrowse.Content = L("ToolSshAuditBtnBrowse");
        EmptyState.Text = L("ToolSshAuditEmptyState");
        BtnCopyFingerprint.Content = L("ToolSshAuditBtnCopy");
        FindingsTitle.Text = L("ToolSshAuditRating");

        DetailAlgorithmLabel.Text = L("ToolSshAuditAlgorithm");
        DetailKeySizeLabel.Text = L("ToolSshAuditKeySize");
        DetailFingerprintLabel.Text = L("ToolSshAuditFingerprint");
        DetailFormatLabel.Text = L("ToolSshAuditFormat");
        DetailTypeLabel.Text = L("ToolSshAuditType");
        DetailEncryptedLabel.Text = L("ToolSshAuditEncrypted");

        // Accessibility
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnBrowse, L("ToolSshAuditBtnBrowse"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyFingerprint, L("ToolSshAuditBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(KeyInput, L("ToolSshAuditInput"));
        System.Windows.Automation.AutomationProperties.SetName(DetailFingerprintValue, L("ToolSshAuditFingerprint"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        BtnCopyFingerprint.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void InitializeDebounceTimer()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DebounceDelayMs)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            RunAudit();
        };
    }

    private void OnKeyInputTextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = KeyFileFilter
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var fileInfo = new FileInfo(dialog.FileName);
            if (fileInfo.Length > MaxKeyFileSize)
            {
                return;
            }

            var content = File.ReadAllText(dialog.FileName, Encoding.UTF8);
            KeyInput.Text = content;
            // TextChanged fires automatically, triggering the debounce -> audit
        }
        catch (IOException)
        {
            // Silently ignore read failures
        }
        catch (UnauthorizedAccessException)
        {
            // Silently ignore permission failures
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpSSHAUDIT");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void RunAudit()
    {
        var keyText = KeyInput.Text;

        if (string.IsNullOrWhiteSpace(keyText))
        {
            ShowEmptyState();
            return;
        }

        var result = ParseAndAudit(keyText, _localizer);

        if (result is null)
        {
            ShowParseError();
            return;
        }

        _fingerprint = result.Fingerprint;
        ShowResults(result);
    }

    private void ShowEmptyState()
    {
        EmptyState.Visibility = Visibility.Visible;
        ParseError.Visibility = Visibility.Collapsed;
        BadgePanel.Visibility = Visibility.Collapsed;
        DetailsPanel.Visibility = Visibility.Collapsed;
        FindingsPanel.Visibility = Visibility.Collapsed;
        _fingerprint = string.Empty;
    }

    private void ShowParseError()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ParseError.Text = L("ToolSshAuditParseError");
        ParseError.Visibility = Visibility.Visible;
        BadgePanel.Visibility = Visibility.Collapsed;
        DetailsPanel.Visibility = Visibility.Collapsed;
        FindingsPanel.Visibility = Visibility.Collapsed;
        _fingerprint = string.Empty;
    }

    private void ShowResults(KeyAuditResult result)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ParseError.Visibility = Visibility.Collapsed;

        // Badge panel
        AlgorithmBadge.Background = GetAlgorithmBrush(result.Algorithm);
        AlgorithmBadgeText.Text = result.Algorithm;
        RatingBadge.Background = result.RatingBrush;
        RatingBadgeText.Text = GetRatingDisplayText(result.Rating);
        BadgePanel.Visibility = Visibility.Visible;

        // Details
        DetailAlgorithmValue.Text = result.Algorithm;
        DetailKeySizeValue.Text = string.Format(L("ToolSshAuditBitsLabel"), result.KeySize);
        DetailFingerprintValue.Text = result.Fingerprint;
        DetailFormatValue.Text = result.Format;
        DetailTypeValue.Text = result.IsPrivateKey ? L("ToolSshAuditPrivate") : L("ToolSshAuditPublic");
        DetailEncryptedValue.Text = result.IsEncrypted ? L("ToolSshAuditYes") : L("ToolSshAuditNo");
        DetailsPanel.Visibility = Visibility.Visible;

        // Findings
        FindingsList.ItemsSource = result.Findings;
        FindingsPanel.Visibility = result.Findings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetRatingDisplayText(string rating) => rating switch
    {
        RatingStrong => L("ToolSshAuditStrong"),
        RatingAcceptable => L("ToolSshAuditAcceptable"),
        RatingWeak => L("ToolSshAuditWeak"),
        RatingDeprecated => L("ToolSshAuditDeprecated"),
        _ => rating
    };

    private static SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush GetAlgorithmBrush(string algorithm) => algorithm switch
    {
        "Ed25519" => CreateFrozen(0x10, 0xB9, 0x81),   // green
        "RSA" => CreateFrozen(0x3B, 0x82, 0xF6),       // blue
        "ECDSA" => CreateFrozen(0x8B, 0x5C, 0xF6),     // purple
        "DSA" => CreateFrozen(0xEF, 0x44, 0x44),       // red
        _ => CreateFrozen(0x6B, 0x72, 0x80)             // gray
    };

    private static readonly SolidColorBrush BrushStrong = CreateFrozen(0x10, 0xB9, 0x81);
    private static readonly SolidColorBrush BrushAcceptable = CreateFrozen(0xF5, 0x9E, 0x0B);
    private static readonly SolidColorBrush BrushWeak = CreateFrozen(0xEF, 0x44, 0x44);
    private static readonly SolidColorBrush BrushDeprecated = CreateFrozen(0xEF, 0x44, 0x44);

    // ──────────────────────────────────────────────────
    // Key parsing and audit logic
    // ──────────────────────────────────────────────────

    private static KeyAuditResult? ParseAndAudit(string keyText, LocalizationManager? loc)
    {
        keyText = keyText.Trim();
        if (string.IsNullOrEmpty(keyText)) return null;

        // Try OpenSSH public key format first (single line starting with algorithm prefix)
        var firstLine = keyText.Split('\n')[0].TrimEnd('\r').Trim();

        if (firstLine.StartsWith(OpenSshRsaPrefix + " ", StringComparison.Ordinal) ||
            firstLine.StartsWith(OpenSshDsaPrefix + " ", StringComparison.Ordinal) ||
            firstLine.StartsWith(OpenSshEd25519Prefix + " ", StringComparison.Ordinal) ||
            firstLine.StartsWith(OpenSshEcdsaNistp256Prefix + " ", StringComparison.Ordinal) ||
            firstLine.StartsWith(OpenSshEcdsaNistp384Prefix + " ", StringComparison.Ordinal) ||
            firstLine.StartsWith(OpenSshEcdsaNistp521Prefix + " ", StringComparison.Ordinal))
        {
            return ParseOpenSshPublicKey(firstLine, loc);
        }

        // PEM formats
        if (keyText.Contains(PemBeginOpenSshPrivate, StringComparison.Ordinal))
            return ParseOpenSshPrivateKey(keyText, loc);

        if (keyText.Contains(PemBeginRsaPrivate, StringComparison.Ordinal))
            return ParsePkcs1RsaPrivateKey(keyText, loc);

        if (keyText.Contains(PemBeginDsaPrivate, StringComparison.Ordinal))
            return ParseLegacyDsaPrivateKey(keyText, loc);

        if (keyText.Contains(PemBeginEcPrivate, StringComparison.Ordinal))
            return ParseLegacyEcPrivateKey(keyText, loc);

        if (keyText.Contains(PemBeginEncryptedPrivate, StringComparison.Ordinal))
            return ParsePkcs8PrivateKey(keyText, isEncrypted: true, loc);

        if (keyText.Contains(PemBeginPrivate, StringComparison.Ordinal))
            return ParsePkcs8PrivateKey(keyText, isEncrypted: false, loc);

        if (keyText.Contains(PemBeginPublic, StringComparison.Ordinal))
            return ParseSpkiPublicKey(keyText, loc);

        if (keyText.Contains(PemBeginRsaPublic, StringComparison.Ordinal))
            return ParsePkcs1RsaPublicKey(keyText, loc);

        return null;
    }

    // ──── OpenSSH public key ──────────────────────────

    private static KeyAuditResult? ParseOpenSshPublicKey(string line, LocalizationManager? loc)
    {
        var parts = line.Split(' ', 3);
        if (parts.Length < 2) return null;

        var algorithmTag = parts[0];
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        return ParseOpenSshBlob(blob, algorithmTag, isPrivate: false, isEncrypted: false, "OpenSSH", loc);
    }

    /// <summary>
    /// Parses an OpenSSH wire-format public key blob and extracts algorithm and key size.
    /// </summary>
    private static KeyAuditResult? ParseOpenSshBlob(
        byte[] blob, string algorithmTag, bool isPrivate, bool isEncrypted, string format,
        LocalizationManager? loc)
    {
        try
        {
            using var ms = new MemoryStream(blob);

            // Read the algorithm string from the blob
            var blobAlgorithm = ReadOpenSshString(ms);
            if (blobAlgorithm is null) return null;

            string algorithm;
            int keySize;
            byte[]? publicKeyBlob = blob; // For fingerprint computation

            if (blobAlgorithm == OpenSshRsaPrefix)
            {
                algorithm = "RSA";
                // Read e (exponent), then n (modulus)
                var e = ReadOpenSshBytes(ms);
                var n = ReadOpenSshBytes(ms);
                if (e is null || n is null) return null;

                // Key size = bit length of modulus (strip leading zero byte if present)
                keySize = GetMpintBitLength(n);
            }
            else if (blobAlgorithm == OpenSshDsaPrefix)
            {
                algorithm = "DSA";
                // Read p parameter to determine key size
                var p = ReadOpenSshBytes(ms);
                if (p is null) return null;
                keySize = GetMpintBitLength(p);
            }
            else if (blobAlgorithm == OpenSshEd25519Prefix)
            {
                algorithm = "Ed25519";
                keySize = Ed25519KeySize;
            }
            else if (blobAlgorithm.StartsWith("ecdsa-sha2-", StringComparison.Ordinal))
            {
                algorithm = "ECDSA";
                // Read curve identifier
                var curve = ReadOpenSshString(ms);
                keySize = curve switch
                {
                    "nistp256" => 256,
                    "nistp384" => 384,
                    "nistp521" => 521,
                    _ => 0
                };
                if (keySize == 0) return null;
            }
            else
            {
                return null;
            }

            var fingerprint = ComputeSha256Fingerprint(publicKeyBlob);
            var findings = BuildFindings(algorithm, keySize, isPrivate, isEncrypted, false, loc);
            var rating = DetermineRating(algorithm, keySize);

            return new KeyAuditResult
            {
                Algorithm = algorithm,
                KeySize = keySize,
                Fingerprint = fingerprint,
                Format = format,
                IsPrivateKey = isPrivate,
                IsEncrypted = isEncrypted,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = findings
            };
        }
        catch (Exception ex) when (IsExpectedParseException(ex))
        {
            return null;
        }
    }

    // ──── OpenSSH private key ─────────────────────────

    private static KeyAuditResult? ParseOpenSshPrivateKey(string pem, LocalizationManager? loc)
    {
        try
        {
            var base64 = ExtractPemBase64(pem, "OPENSSH PRIVATE KEY");
            if (base64 is null) return null;

            var data = Convert.FromBase64String(base64);
            using var ms = new MemoryStream(data);

            // Magic: "openssh-key-v1\0"
            var magic = new byte[15];
            if (ms.Read(magic, 0, 15) != 15) return null;
            if (Encoding.ASCII.GetString(magic) != "openssh-key-v1\0") return null;

            // ciphername
            var cipherName = ReadOpenSshString(ms);
            // kdfname
            var kdfName = ReadOpenSshString(ms);
            // kdfoptions (skip)
            var kdfOptions = ReadOpenSshBytes(ms);
            // number of keys
            var numKeysBuf = new byte[4];
            if (ms.Read(numKeysBuf, 0, 4) != 4) return null;
            var numKeys = BinaryPrimitives.ReadInt32BigEndian(numKeysBuf);
            if (numKeys < 1) return null;

            // Public key blob
            var pubKeyBlob = ReadOpenSshBytes(ms);
            if (pubKeyBlob is null) return null;

            var isEncrypted = cipherName != "none" || kdfName == OpenSshKdfBcrypt;

            // Parse public key blob to determine algorithm and key size
            using var pubMs = new MemoryStream(pubKeyBlob);
            var algorithmTag = ReadOpenSshString(pubMs);
            if (algorithmTag is null) return null;

            return ParseOpenSshBlob(pubKeyBlob, algorithmTag, isPrivate: true, isEncrypted, "OpenSSH", loc);
        }
        catch (Exception ex) when (IsExpectedParseException(ex))
        {
            return null;
        }
    }

    // ──── PKCS#1 RSA private key (legacy PEM) ────────

    private static KeyAuditResult? ParsePkcs1RsaPrivateKey(string pem, LocalizationManager? loc)
    {
        var isEncrypted = pem.Contains(ProcTypeEncrypted, StringComparison.Ordinal);
        var isOldFormat = true;

        if (isEncrypted)
        {
            // Cannot parse encrypted legacy key to get key size, but we can still report
            return new KeyAuditResult
            {
                Algorithm = "RSA",
                KeySize = 0,
                Fingerprint = "",
                Format = "PEM (PKCS#1)",
                IsPrivateKey = true,
                IsEncrypted = true,
                Rating = RatingAcceptable,
                RatingBrush = GetRatingBrush(RatingAcceptable),
                Findings = BuildFindings("RSA", 0, true, true, isOldFormat, loc)
            };
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            var keySize = rsa.KeySize;

            // Build public key blob for fingerprint
            var publicKeyBlob = BuildRsaPublicKeyBlob(rsa);
            var fingerprint = ComputeSha256Fingerprint(publicKeyBlob);
            var rating = DetermineRating("RSA", keySize);

            return new KeyAuditResult
            {
                Algorithm = "RSA",
                KeySize = keySize,
                Fingerprint = fingerprint,
                Format = "PEM (PKCS#1)",
                IsPrivateKey = true,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("RSA", keySize, true, false, isOldFormat, loc)
            };
        }
        catch (Exception ex) when (IsExpectedParseException(ex))
        {
            return null;
        }
    }

    // ──── Legacy DSA private key ──────────────────────

    private static KeyAuditResult? ParseLegacyDsaPrivateKey(string pem, LocalizationManager? loc)
    {
        var isEncrypted = pem.Contains(ProcTypeEncrypted, StringComparison.Ordinal);

        if (isEncrypted)
        {
            return new KeyAuditResult
            {
                Algorithm = "DSA",
                KeySize = 0,
                Fingerprint = "",
                Format = "PEM (Legacy DSA)",
                IsPrivateKey = true,
                IsEncrypted = true,
                Rating = RatingDeprecated,
                RatingBrush = GetRatingBrush(RatingDeprecated),
                Findings = BuildFindings("DSA", 0, true, true, true, loc)
            };
        }

        try
        {
            using var dsa = DSA.Create();
            dsa.ImportFromPem(pem);
            var keySize = dsa.KeySize;
            var rating = DetermineRating("DSA", keySize);

            return new KeyAuditResult
            {
                Algorithm = "DSA",
                KeySize = keySize,
                Fingerprint = "",
                Format = "PEM (Legacy DSA)",
                IsPrivateKey = true,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("DSA", keySize, true, false, true, loc)
            };
        }
        catch (Exception ex) when (IsExpectedParseException(ex))
        {
            return null;
        }
    }

    // ──── Legacy EC private key ───────────────────────

    private static KeyAuditResult? ParseLegacyEcPrivateKey(string pem, LocalizationManager? loc)
    {
        var isEncrypted = pem.Contains(ProcTypeEncrypted, StringComparison.Ordinal);

        if (isEncrypted)
        {
            return new KeyAuditResult
            {
                Algorithm = "ECDSA",
                KeySize = 0,
                Fingerprint = "",
                Format = "PEM (Legacy EC)",
                IsPrivateKey = true,
                IsEncrypted = true,
                Rating = RatingAcceptable,
                RatingBrush = GetRatingBrush(RatingAcceptable),
                Findings = BuildFindings("ECDSA", 0, true, true, true, loc)
            };
        }

        try
        {
            using var ec = ECDsa.Create();
            ec.ImportFromPem(pem);
            var keySize = ec.KeySize;
            var rating = DetermineRating("ECDSA", keySize);

            // Build public key blob for fingerprint
            var parameters = ec.ExportParameters(false);
            var curveName = keySize switch
            {
                256 => OpenSshEcdsaNistp256Prefix,
                384 => OpenSshEcdsaNistp384Prefix,
                521 => OpenSshEcdsaNistp521Prefix,
                _ => null
            };

            var fingerprint = "";
            if (curveName is not null && parameters.Q.X is not null && parameters.Q.Y is not null)
            {
                var blob = BuildEcdsaPublicKeyBlob(curveName, keySize, parameters.Q.X, parameters.Q.Y);
                fingerprint = ComputeSha256Fingerprint(blob);
            }

            return new KeyAuditResult
            {
                Algorithm = "ECDSA",
                KeySize = keySize,
                Fingerprint = fingerprint,
                Format = "PEM (Legacy EC)",
                IsPrivateKey = true,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("ECDSA", keySize, true, false, true, loc)
            };
        }
        catch (Exception ex) when (IsExpectedParseException(ex))
        {
            return null;
        }
    }

    // ──── PKCS#8 private key ──────────────────────────

    private static KeyAuditResult? ParsePkcs8PrivateKey(string pem, bool isEncrypted, LocalizationManager? loc)
    {
        if (isEncrypted)
        {
            // Cannot determine algorithm/size without passphrase
            return new KeyAuditResult
            {
                Algorithm = "Unknown",
                KeySize = 0,
                Fingerprint = "",
                Format = "PEM (PKCS#8, encrypted)",
                IsPrivateKey = true,
                IsEncrypted = true,
                Rating = RatingAcceptable,
                RatingBrush = GetRatingBrush(RatingAcceptable),
                Findings = BuildFindings("Unknown", 0, true, true, false, loc)
            };
        }

        // Try RSA first, then ECDSA, then DSA
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            var keySize = rsa.KeySize;
            var publicKeyBlob = BuildRsaPublicKeyBlob(rsa);
            var fingerprint = ComputeSha256Fingerprint(publicKeyBlob);
            var rating = DetermineRating("RSA", keySize);

            return new KeyAuditResult
            {
                Algorithm = "RSA",
                KeySize = keySize,
                Fingerprint = fingerprint,
                Format = "PEM (PKCS#8)",
                IsPrivateKey = true,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("RSA", keySize, true, false, false, loc)
            };
        }
        catch (CryptographicException) { /* not RSA */ }

        try
        {
            using var ec = ECDsa.Create();
            ec.ImportFromPem(pem);
            var keySize = ec.KeySize;
            var rating = DetermineRating("ECDSA", keySize);

            var parameters = ec.ExportParameters(false);
            var curveName = keySize switch
            {
                256 => OpenSshEcdsaNistp256Prefix,
                384 => OpenSshEcdsaNistp384Prefix,
                521 => OpenSshEcdsaNistp521Prefix,
                _ => null
            };

            var fingerprint = "";
            if (curveName is not null && parameters.Q.X is not null && parameters.Q.Y is not null)
            {
                var blob = BuildEcdsaPublicKeyBlob(curveName, keySize, parameters.Q.X, parameters.Q.Y);
                fingerprint = ComputeSha256Fingerprint(blob);
            }

            return new KeyAuditResult
            {
                Algorithm = "ECDSA",
                KeySize = keySize,
                Fingerprint = fingerprint,
                Format = "PEM (PKCS#8)",
                IsPrivateKey = true,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("ECDSA", keySize, true, false, false, loc)
            };
        }
        catch (CryptographicException) { /* not ECDSA */ }

        try
        {
            using var dsa = DSA.Create();
            dsa.ImportFromPem(pem);
            var keySize = dsa.KeySize;
            var rating = DetermineRating("DSA", keySize);

            return new KeyAuditResult
            {
                Algorithm = "DSA",
                KeySize = keySize,
                Fingerprint = "",
                Format = "PEM (PKCS#8)",
                IsPrivateKey = true,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("DSA", keySize, true, false, false, loc)
            };
        }
        catch (CryptographicException) { /* not DSA either */ }

        // Try Ed25519 via PKCS#8 SubjectPublicKeyInfo extraction approach
        return TryParseEd25519Pkcs8(pem, loc);
    }

    /// <summary>
    /// Attempts to parse a PKCS#8 PEM as Ed25519 by importing via the generic API
    /// and inspecting the DER-encoded SubjectPublicKeyInfo.
    /// </summary>
    private static KeyAuditResult? TryParseEd25519Pkcs8(string pem, LocalizationManager? loc)
    {
        try
        {
            // Extract DER bytes from PEM
            var base64 = ExtractPemBase64(pem, "PRIVATE KEY");
            if (base64 is null) return null;

            var derBytes = Convert.FromBase64String(base64);

            // Ed25519 OID: 1.3.101.112 -> encoded as 06 03 2B 65 70
            var ed25519Oid = new byte[] { 0x06, 0x03, 0x2B, 0x65, 0x70 };
            if (!ContainsSequence(derBytes, ed25519Oid)) return null;

            var rating = DetermineRating("Ed25519", Ed25519KeySize);

            return new KeyAuditResult
            {
                Algorithm = "Ed25519",
                KeySize = Ed25519KeySize,
                Fingerprint = "",
                Format = "PEM (PKCS#8)",
                IsPrivateKey = true,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("Ed25519", Ed25519KeySize, true, false, false, loc)
            };
        }
        catch (Exception ex) when (IsExpectedParseException(ex))
        {
            return null;
        }
    }

    // ──── SPKI public key (BEGIN PUBLIC KEY) ──────────

    private static KeyAuditResult? ParseSpkiPublicKey(string pem, LocalizationManager? loc)
    {
        // Try RSA
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            var keySize = rsa.KeySize;
            var publicKeyBlob = BuildRsaPublicKeyBlob(rsa);
            var fingerprint = ComputeSha256Fingerprint(publicKeyBlob);
            var rating = DetermineRating("RSA", keySize);

            return new KeyAuditResult
            {
                Algorithm = "RSA",
                KeySize = keySize,
                Fingerprint = fingerprint,
                Format = "PEM (SPKI)",
                IsPrivateKey = false,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("RSA", keySize, false, false, false, loc)
            };
        }
        catch (CryptographicException) { /* not RSA */ }

        // Try ECDSA
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportFromPem(pem);
            var keySize = ec.KeySize;
            var rating = DetermineRating("ECDSA", keySize);

            var parameters = ec.ExportParameters(false);
            var curveName = keySize switch
            {
                256 => OpenSshEcdsaNistp256Prefix,
                384 => OpenSshEcdsaNistp384Prefix,
                521 => OpenSshEcdsaNistp521Prefix,
                _ => null
            };

            var fingerprint = "";
            if (curveName is not null && parameters.Q.X is not null && parameters.Q.Y is not null)
            {
                var blob = BuildEcdsaPublicKeyBlob(curveName, keySize, parameters.Q.X, parameters.Q.Y);
                fingerprint = ComputeSha256Fingerprint(blob);
            }

            return new KeyAuditResult
            {
                Algorithm = "ECDSA",
                KeySize = keySize,
                Fingerprint = fingerprint,
                Format = "PEM (SPKI)",
                IsPrivateKey = false,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("ECDSA", keySize, false, false, false, loc)
            };
        }
        catch (CryptographicException) { /* not ECDSA */ }

        // Try Ed25519 via OID detection
        try
        {
            var base64 = ExtractPemBase64(pem, "PUBLIC KEY");
            if (base64 is null) return null;

            var derBytes = Convert.FromBase64String(base64);
            var ed25519Oid = new byte[] { 0x06, 0x03, 0x2B, 0x65, 0x70 };
            if (!ContainsSequence(derBytes, ed25519Oid)) return null;

            var rating = DetermineRating("Ed25519", Ed25519KeySize);

            return new KeyAuditResult
            {
                Algorithm = "Ed25519",
                KeySize = Ed25519KeySize,
                Fingerprint = "",
                Format = "PEM (SPKI)",
                IsPrivateKey = false,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("Ed25519", Ed25519KeySize, false, false, false, loc)
            };
        }
        catch (Exception ex) when (IsExpectedParseException(ex))
        {
            /* not Ed25519 */
        }

        // Try DSA
        try
        {
            using var dsa = DSA.Create();
            dsa.ImportFromPem(pem);
            var keySize = dsa.KeySize;
            var rating = DetermineRating("DSA", keySize);

            return new KeyAuditResult
            {
                Algorithm = "DSA",
                KeySize = keySize,
                Fingerprint = "",
                Format = "PEM (SPKI)",
                IsPrivateKey = false,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("DSA", keySize, false, false, false, loc)
            };
        }
        catch (CryptographicException) { /* not DSA */ }

        return null;
    }

    // ──── PKCS#1 RSA public key (BEGIN RSA PUBLIC KEY) ─

    private static KeyAuditResult? ParsePkcs1RsaPublicKey(string pem, LocalizationManager? loc)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            var keySize = rsa.KeySize;
            var publicKeyBlob = BuildRsaPublicKeyBlob(rsa);
            var fingerprint = ComputeSha256Fingerprint(publicKeyBlob);
            var rating = DetermineRating("RSA", keySize);

            return new KeyAuditResult
            {
                Algorithm = "RSA",
                KeySize = keySize,
                Fingerprint = fingerprint,
                Format = "PEM (PKCS#1)",
                IsPrivateKey = false,
                IsEncrypted = false,
                Rating = rating,
                RatingBrush = GetRatingBrush(rating),
                Findings = BuildFindings("RSA", keySize, false, false, false, loc)
            };
        }
        catch (Exception ex) when (IsExpectedParseException(ex))
        {
            return null;
        }
    }

    // ──────────────────────────────────────────────────
    // Security assessment
    // ──────────────────────────────────────────────────

    private static string DetermineRating(string algorithm, int keySize) => algorithm switch
    {
        "DSA" => RatingDeprecated,
        "Ed25519" => RatingStrong,
        "RSA" when keySize > 0 && keySize < MinRsaKeySize => RatingWeak,
        "RSA" when keySize >= StrongRsaKeySize => RatingStrong,
        "RSA" when keySize >= MinRsaKeySize => RatingAcceptable,
        "ECDSA" when keySize >= StrongEcdsaKeySize => RatingStrong,
        "ECDSA" when keySize > 0 => RatingAcceptable,
        "Unknown" => RatingAcceptable,
        _ => RatingAcceptable
    };

    private static SolidColorBrush GetRatingBrush(string rating) => rating switch
    {
        RatingStrong => BrushStrong,
        RatingAcceptable => BrushAcceptable,
        RatingWeak => BrushWeak,
        RatingDeprecated => BrushDeprecated,
        _ => BrushAcceptable
    };

    private static List<AuditFinding> BuildFindings(
        string algorithm, int keySize, bool isPrivate, bool isEncrypted, bool isOldFormat,
        LocalizationManager? loc)
    {
        var findings = new List<AuditFinding>();

        // Algorithm-specific findings
        switch (algorithm)
        {
            case "Ed25519":
                findings.Add(new AuditFinding
                {
                    Icon = FindingIconPass,
                    Text = Loc(loc, "ToolSshAuditFindingEd25519"),
                    IconBrush = BrushStrong
                });
                break;

            case "RSA" when keySize >= StrongRsaKeySize:
                findings.Add(new AuditFinding
                {
                    Icon = FindingIconPass,
                    Text = string.Format(Loc(loc, "ToolSshAuditFindingRsaStrong"), keySize),
                    IconBrush = BrushStrong
                });
                break;

            case "RSA" when keySize >= MinRsaKeySize:
                findings.Add(new AuditFinding
                {
                    Icon = FindingIconWarn,
                    Text = Loc(loc, "ToolSshAuditFindingRsaOk"),
                    IconBrush = BrushAcceptable
                });
                break;

            case "RSA" when keySize > 0:
                findings.Add(new AuditFinding
                {
                    Icon = FindingIconFail,
                    Text = string.Format(Loc(loc, "ToolSshAuditFindingRsaWeak"), keySize),
                    IconBrush = BrushWeak
                });
                break;

            case "DSA":
                findings.Add(new AuditFinding
                {
                    Icon = FindingIconFail,
                    Text = Loc(loc, "ToolSshAuditFindingDsa"),
                    IconBrush = BrushDeprecated
                });
                break;

            case "ECDSA" when keySize >= StrongEcdsaKeySize:
                findings.Add(new AuditFinding
                {
                    Icon = FindingIconPass,
                    Text = string.Format(Loc(loc, "ToolSshAuditFindingEcdsaStrong"), keySize),
                    IconBrush = BrushStrong
                });
                break;

            case "ECDSA" when keySize > 0:
                findings.Add(new AuditFinding
                {
                    Icon = FindingIconWarn,
                    Text = Loc(loc, "ToolSshAuditFindingEcdsaOk"),
                    IconBrush = BrushAcceptable
                });
                break;
        }

        // Private key encryption findings
        if (isPrivate)
        {
            if (isEncrypted)
            {
                findings.Add(new AuditFinding
                {
                    Icon = FindingIconPass,
                    Text = Loc(loc, "ToolSshAuditFindingEncrypted"),
                    IconBrush = BrushStrong
                });
            }
            else
            {
                findings.Add(new AuditFinding
                {
                    Icon = FindingIconWarn,
                    Text = Loc(loc, "ToolSshAuditFindingUnencrypted"),
                    IconBrush = BrushAcceptable
                });
            }
        }

        // Old format warning
        if (isOldFormat && isPrivate)
        {
            findings.Add(new AuditFinding
            {
                Icon = FindingIconWarn,
                Text = Loc(loc, "ToolSshAuditFindingOldFormat"),
                IconBrush = BrushAcceptable
            });
        }

        return findings;
    }

    // ──────────────────────────────────────────────────
    // Wire-format helpers
    // ──────────────────────────────────────────────────

    /// <summary>
    /// Builds the OpenSSH wire-format public key blob for an RSA key.
    /// </summary>
    private static byte[] BuildRsaPublicKeyBlob(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        using var ms = new MemoryStream();
        WriteOpenSshString(ms, OpenSshRsaPrefix);
        WriteOpenSshMpint(ms, parameters.Exponent!);
        WriteOpenSshMpint(ms, parameters.Modulus!);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds the OpenSSH wire-format public key blob for an ECDSA key.
    /// </summary>
    private static byte[] BuildEcdsaPublicKeyBlob(string curveName, int keySize, byte[] x, byte[] y)
    {
        var curveId = keySize switch
        {
            256 => "nistp256",
            384 => "nistp384",
            521 => "nistp521",
            _ => "nistp256"
        };

        using var ms = new MemoryStream();
        WriteOpenSshString(ms, curveName);
        WriteOpenSshString(ms, curveId);

        // Uncompressed EC point: 0x04 || X || Y
        var point = new byte[1 + x.Length + y.Length];
        point[0] = 0x04;
        Array.Copy(x, 0, point, 1, x.Length);
        Array.Copy(y, 0, point, 1 + x.Length, y.Length);
        WriteOpenSshBytes(ms, point);

        return ms.ToArray();
    }

    /// <summary>
    /// Computes the SHA256 fingerprint of a public key blob in OpenSSH format.
    /// </summary>
    private static string ComputeSha256Fingerprint(byte[] publicKeyBlob)
    {
        var hash = SHA256.HashData(publicKeyBlob);
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }

    /// <summary>
    /// Reads an OpenSSH wire-format string from a stream.
    /// </summary>
    private static string? ReadOpenSshString(Stream stream)
    {
        var bytes = ReadOpenSshBytes(stream);
        return bytes is null ? null : Encoding.ASCII.GetString(bytes);
    }

    /// <summary>
    /// Reads an OpenSSH wire-format byte string from a stream.
    /// </summary>
    private static byte[]? ReadOpenSshBytes(Stream stream)
    {
        Span<byte> lengthBuf = stackalloc byte[4];
        if (stream.Read(lengthBuf) != 4) return null;
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuf);
        if (length < 0 || length > stream.Length - stream.Position) return null;

        var data = new byte[length];
        if (stream.Read(data) != length) return null;
        return data;
    }

    /// <summary>
    /// Writes an OpenSSH wire-format string.
    /// </summary>
    private static void WriteOpenSshString(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        Span<byte> lengthBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBuf, bytes.Length);
        stream.Write(lengthBuf);
        stream.Write(bytes);
    }

    /// <summary>
    /// Writes an OpenSSH wire-format byte string.
    /// </summary>
    private static void WriteOpenSshBytes(Stream stream, byte[] value)
    {
        Span<byte> lengthBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBuf, value.Length);
        stream.Write(lengthBuf);
        stream.Write(value);
    }

    /// <summary>
    /// Writes an OpenSSH wire-format mpint (multi-precision integer).
    /// </summary>
    private static void WriteOpenSshMpint(Stream stream, byte[] value)
    {
        var needsPadding = value.Length > 0 && (value[0] & 0x80) != 0;
        var length = value.Length + (needsPadding ? 1 : 0);

        Span<byte> lengthBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBuf, length);
        stream.Write(lengthBuf);

        if (needsPadding)
        {
            stream.WriteByte(0);
        }

        stream.Write(value);
    }

    /// <summary>
    /// Gets the bit length of an mpint (stripping any leading zero padding byte).
    /// </summary>
    private static int GetMpintBitLength(byte[] mpint)
    {
        var offset = 0;
        while (offset < mpint.Length && mpint[offset] == 0)
            offset++;

        if (offset >= mpint.Length) return 0;

        var byteCount = mpint.Length - offset;
        var topByte = mpint[offset];
        var topBits = 0;
        while (topByte > 0)
        {
            topBits++;
            topByte >>= 1;
        }

        return ((byteCount - 1) * 8) + topBits;
    }

    /// <summary>
    /// Extracts the base64-encoded body from a PEM block with the given label.
    /// </summary>
    private static string? ExtractPemBase64(string pem, string label)
    {
        var beginMarker = $"-----BEGIN {label}-----";
        var endMarker = $"-----END {label}-----";

        var beginIdx = pem.IndexOf(beginMarker, StringComparison.Ordinal);
        if (beginIdx < 0) return null;

        var bodyStart = beginIdx + beginMarker.Length;
        var endIdx = pem.IndexOf(endMarker, bodyStart, StringComparison.Ordinal);
        if (endIdx < 0) return null;

        var body = pem[bodyStart..endIdx];
        // Strip PEM headers (e.g., Proc-Type, DEK-Info) and whitespace
        var sb = new StringBuilder(body.Length);
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.Contains(':', StringComparison.Ordinal)) continue; // Skip header lines
            sb.Append(trimmed);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Checks whether a byte sequence contains another byte sequence.
    /// </summary>
    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    found = false;
                    break;
                }
            }
            if (found) return true;
        }
        return false;
    }

    /// <summary>
    /// Exception filter for expected parse failures.
    /// </summary>
    private static bool IsExpectedParseException(Exception ex) =>
        ex is FormatException or ArgumentException or CryptographicException or IOException or NotSupportedException;

    private static string Loc(LocalizationManager? loc, string key) => loc?[key] ?? key;

    private string L(string key) => _localizer?[key] ?? key;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_debounceTimer is not null)
        {
            _debounceTimer.Stop();
            _debounceTimer = null;
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents the result of auditing an SSH key.
/// </summary>
public sealed class KeyAuditResult
{
    public string Algorithm { get; init; } = "";
    public int KeySize { get; init; }
    public string Fingerprint { get; init; } = "";
    public string Format { get; init; } = "";
    public bool IsPrivateKey { get; init; }
    public bool IsEncrypted { get; init; }
    public string Rating { get; init; } = "";
    public Brush RatingBrush { get; init; } = Brushes.Transparent;
    public List<AuditFinding> Findings { get; init; } = [];
}

/// <summary>
/// Represents a single security finding from the key audit.
/// </summary>
public sealed class AuditFinding
{
    public string Icon { get; init; } = "";
    public string Text { get; init; } = "";
    public Brush IconBrush { get; init; } = Brushes.Transparent;
}
