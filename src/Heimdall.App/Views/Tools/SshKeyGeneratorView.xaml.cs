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
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// SSH key pair generator supporting RSA (2048/4096) with OpenSSH public key
/// and PKCS#8 PEM private key export. Optional passphrase encryption via AES-256-CBC.
/// </summary>
public partial class SshKeyGeneratorView : UserControl, IDisposable
{
    private const int Rsa2048KeySize = 2048;
    private const int Rsa4096KeySize = 4096;
    private const int PbeIterationCount = 16;
    private const string MaskedPlaceholder = "********";
    private const string OpenSshPubKeyPrefix = "ssh-rsa";
    private const string PublicKeyFileExtension = ".pub";
    private const string PrivateKeyFileExtension = ".pem";
    private const string PublicKeyFileFilter = "SSH Public Key (*.pub)|*.pub|All Files (*.*)|*.*";
    private const string PrivateKeyFileFilter = "PEM Private Key (*.pem)|*.pem|All Files (*.*)|*.*";

    private LocalizationManager? _localizer;
    private string _privateKeyPem = string.Empty;
    private string _publicKeyOpenSsh = string.Empty;
    private string _fingerprint = string.Empty;
    private bool _privateKeyVisible;

    public SshKeyGeneratorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the view with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        // Default comment: user@hostname
        CommentInput.Text = $"{Environment.UserName}@{Environment.MachineName}";
    }

    private void ApplyLocalization()
    {
        TitleText.Text = L("ToolSshKeyGenTitle");
        AlgorithmLabel.Text = L("ToolSshKeyGenAlgorithm");
        CommentLabel.Text = L("ToolSshKeyGenComment");
        PassphraseLabel.Text = L("ToolSshKeyGenPassphrase");
        PassphraseHint.Text = L("ToolSshKeyGenPassphraseHint");
        BtnGenerate.Content = L("ToolSshKeyGenBtnGenerate");
        FingerprintLabel.Text = L("ToolSshKeyGenFingerprint");
        BtnCopyFingerprint.Content = L("ToolSshKeyGenBtnCopy");
        PublicKeyLabel.Text = L("ToolSshKeyGenPublicKey");
        BtnCopyPublicKey.Content = L("ToolSshKeyGenBtnCopy");
        BtnSavePublicKey.Content = L("ToolSshKeyGenBtnSave");
        PrivateKeyLabel.Text = L("ToolSshKeyGenPrivateKey");
        BtnCopyPrivateKey.Content = L("ToolSshKeyGenBtnCopy");
        BtnShowPrivateKey.Content = L("ToolSshKeyGenBtnShow");
        BtnSavePrivateKey.Content = L("ToolSshKeyGenBtnSave");
        Ed25519Notice.Text = L("ToolSshKeyGenEd25519Notice");

        // Ed25519 combo item tooltip
        Ed25519Item.ToolTip = L("ToolSshKeyGenEd25519Tooltip");

        // Accessibility
        System.Windows.Automation.AutomationProperties.SetName(BtnGenerate, L("ToolSshKeyGenBtnGenerate"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyFingerprint, L("ToolSshKeyGenBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyPublicKey, L("ToolSshKeyGenBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyPrivateKey, L("ToolSshKeyGenBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSavePublicKey, L("ToolSshKeyGenBtnSave"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSavePrivateKey, L("ToolSshKeyGenBtnSave"));
        System.Windows.Automation.AutomationProperties.SetName(BtnShowPrivateKey, L("ToolSshKeyGenBtnShow"));
        System.Windows.Automation.AutomationProperties.SetName(AlgorithmCombo, L("ToolSshKeyGenAlgorithm"));
        System.Windows.Automation.AutomationProperties.SetName(CommentInput, L("ToolSshKeyGenComment"));
        System.Windows.Automation.AutomationProperties.SetName(PassphraseInput, L("ToolSshKeyGenPassphrase"));
        System.Windows.Automation.AutomationProperties.SetName(FingerprintOutput, L("ToolSshKeyGenFingerprint"));
        System.Windows.Automation.AutomationProperties.SetName(PublicKeyOutput, L("ToolSshKeyGenPublicKey"));
        System.Windows.Automation.AutomationProperties.SetName(PrivateKeyOutput, L("ToolSshKeyGenPrivateKey"));

        BtnCopyFingerprint.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyPublicKey.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyPrivateKey.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        GenerateKeyPair();
    }

    private void OnCopyPublicKeyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_publicKeyOpenSsh))
        {
            Clipboard.SetText(_publicKeyOpenSsh);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnCopyPrivateKeyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_privateKeyPem))
        {
            Clipboard.SetText(_privateKeyPem);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnCopyFingerprintClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_fingerprint))
        {
            Clipboard.SetText(_fingerprint);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnTogglePrivateKeyClick(object sender, RoutedEventArgs e)
    {
        _privateKeyVisible = !_privateKeyVisible;
        PrivateKeyOutput.Text = _privateKeyVisible ? _privateKeyPem : MaskedPlaceholder;
        BtnShowPrivateKey.Content = _privateKeyVisible
            ? L("ToolSshKeyGenBtnHide")
            : L("ToolSshKeyGenBtnShow");
    }

    private void OnSavePublicKeyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_publicKeyOpenSsh)) return;

        var dialog = new SaveFileDialog
        {
            FileName = "id_rsa" + PublicKeyFileExtension,
            Filter = PublicKeyFileFilter,
            DefaultExt = PublicKeyFileExtension
        };

        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, _publicKeyOpenSsh + Environment.NewLine, Encoding.UTF8);
        }
    }

    private void OnSavePrivateKeyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_privateKeyPem)) return;

        var dialog = new SaveFileDialog
        {
            FileName = "id_rsa" + PrivateKeyFileExtension,
            Filter = PrivateKeyFileFilter,
            DefaultExt = PrivateKeyFileExtension
        };

        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, _privateKeyPem, Encoding.UTF8);
        }
    }

    private void GenerateKeyPair()
    {
        var keySize = AlgorithmCombo.SelectedIndex switch
        {
            1 => Rsa4096KeySize,
            _ => Rsa2048KeySize
        };

        using var rsa = RSA.Create(keySize);
        var comment = CommentInput.Text.Trim();
        var passphrase = PassphraseInput.Password;

        // Generate OpenSSH public key
        _publicKeyOpenSsh = FormatOpenSshPublicKey(rsa, comment);

        // Generate PEM private key
        _privateKeyPem = ExportPrivateKeyPem(rsa, passphrase);

        // Compute SHA256 fingerprint
        _fingerprint = ComputeSha256Fingerprint(rsa);

        // Update UI
        PublicKeyOutput.Text = _publicKeyOpenSsh;
        FingerprintOutput.Text = _fingerprint;

        _privateKeyVisible = false;
        PrivateKeyOutput.Text = MaskedPlaceholder;
        BtnShowPrivateKey.Content = L("ToolSshKeyGenBtnShow");

        FingerprintPanel.Visibility = Visibility.Visible;
        PublicKeyPanel.Visibility = Visibility.Visible;
        PrivateKeyPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Formats the RSA public key in OpenSSH format: ssh-rsa BASE64_DATA comment
    /// </summary>
    private static string FormatOpenSshPublicKey(RSA rsa, string comment)
    {
        var parameters = rsa.ExportParameters(false);

        // OpenSSH wire format: string "ssh-rsa", mpint e, mpint n
        using var ms = new System.IO.MemoryStream();
        WriteOpenSshString(ms, OpenSshPubKeyPrefix);
        WriteOpenSshMpint(ms, parameters.Exponent!);
        WriteOpenSshMpint(ms, parameters.Modulus!);

        var base64 = Convert.ToBase64String(ms.ToArray());
        return string.IsNullOrWhiteSpace(comment)
            ? $"{OpenSshPubKeyPrefix} {base64}"
            : $"{OpenSshPubKeyPrefix} {base64} {comment}";
    }

    /// <summary>
    /// Exports the private key as PKCS#8 PEM, optionally encrypted with AES-256-CBC.
    /// </summary>
    private static string ExportPrivateKeyPem(RSA rsa, string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            return rsa.ExportPkcs8PrivateKeyPem();
        }

        var pbeParams = new PbeParameters(
            PbeEncryptionAlgorithm.Aes256Cbc,
            HashAlgorithmName.SHA256,
            PbeIterationCount);

        return rsa.ExportEncryptedPkcs8PrivateKeyPem(
            passphrase.AsSpan(),
            pbeParams);
    }

    /// <summary>
    /// Computes the SHA256 fingerprint of the public key in the format: SHA256:BASE64_HASH
    /// </summary>
    private static string ComputeSha256Fingerprint(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);

        using var ms = new System.IO.MemoryStream();
        WriteOpenSshString(ms, OpenSshPubKeyPrefix);
        WriteOpenSshMpint(ms, parameters.Exponent!);
        WriteOpenSshMpint(ms, parameters.Modulus!);

        var hash = SHA256.HashData(ms.ToArray());
        // OpenSSH fingerprint format: SHA256:base64url (no padding)
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }

    /// <summary>
    /// Writes an OpenSSH wire-format string (4-byte big-endian length + raw bytes).
    /// </summary>
    private static void WriteOpenSshString(System.IO.Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        Span<byte> lengthBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBuf, bytes.Length);
        stream.Write(lengthBuf);
        stream.Write(bytes);
    }

    /// <summary>
    /// Writes an OpenSSH wire-format mpint (multi-precision integer).
    /// Prepends a zero byte if the high bit is set (to avoid negative interpretation).
    /// </summary>
    private static void WriteOpenSshMpint(System.IO.Stream stream, byte[] value)
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

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        // Clear sensitive data from memory
        _privateKeyPem = string.Empty;
        GC.SuppressFinalize(this);
    }
}
