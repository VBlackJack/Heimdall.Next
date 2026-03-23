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
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// SSH key pair generator supporting RSA (2048/4096) and Ed25519 with OpenSSH public key
/// and PKCS#8 PEM private key export. Optional passphrase encryption via AES-256-CBC.
/// Ed25519 support is detected at runtime via reflection (requires .NET 10+).
/// </summary>
public partial class SshKeyGeneratorView : UserControl, IToolView
{
    private const int Rsa2048KeySize = 2048;
    private const int Rsa4096KeySize = 4096;
    private const int AlgorithmIndexRsa2048 = 0;
    private const int AlgorithmIndexRsa4096 = 1;
    private const int AlgorithmIndexEd25519 = 2;
    private const int Ed25519PublicKeyLength = 32;
    private const int PbeIterationCount = 16;
    private const string MaskedPlaceholder = "********";
    private const string OpenSshRsaPrefix = "ssh-rsa";
    private const string OpenSshEd25519Prefix = "ssh-ed25519";
    private const string PublicKeyFileExtension = ".pub";
    private const string PrivateKeyFileExtension = ".pem";
    private const string PublicKeyFileFilter = "SSH Public Key (*.pub)|*.pub|All Files (*.*)|*.*";
    private const string PrivateKeyFileFilter = "PEM Private Key (*.pem)|*.pem|All Files (*.*)|*.*";

    /// <summary>
    /// Cached reflection lookup for System.Security.Cryptography.EdDSA (available in .NET 10+).
    /// Null if the runtime does not support EdDSA.
    /// </summary>
    private static readonly Type? EdDsaType = Type.GetType("System.Security.Cryptography.EdDSA, System.Security.Cryptography");
    private static readonly Type? EdDsaParametersType = Type.GetType("System.Security.Cryptography.EdDSAParameters, System.Security.Cryptography");

    private LocalizationManager? _localizer;
    private string _privateKeyPem = string.Empty;
    private string _publicKeyOpenSsh = string.Empty;
    private string _fingerprint = string.Empty;
    private bool _privateKeyVisible;
    private int _lastGeneratedAlgorithmIndex;

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
        HeaderTitle.Text = L("ToolSshKeyGenTitle");
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

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpSSHKEY");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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

        var defaultName = _lastGeneratedAlgorithmIndex == AlgorithmIndexEd25519
            ? "id_ed25519"
            : "id_rsa";

        var dialog = new SaveFileDialog
        {
            FileName = defaultName + PublicKeyFileExtension,
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

        var defaultName = _lastGeneratedAlgorithmIndex == AlgorithmIndexEd25519
            ? "id_ed25519"
            : "id_rsa";

        var dialog = new SaveFileDialog
        {
            FileName = defaultName + PrivateKeyFileExtension,
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
        _lastGeneratedAlgorithmIndex = AlgorithmCombo.SelectedIndex;

        if (_lastGeneratedAlgorithmIndex == AlgorithmIndexEd25519)
        {
            GenerateEd25519KeyPair();
        }
        else
        {
            GenerateRsaKeyPair();
        }
    }

    private void GenerateRsaKeyPair()
    {
        var keySize = _lastGeneratedAlgorithmIndex switch
        {
            AlgorithmIndexRsa4096 => Rsa4096KeySize,
            _ => Rsa2048KeySize
        };

        using var rsa = RSA.Create(keySize);
        var comment = CommentInput.Text.Trim();
        var passphrase = PassphraseInput.Password;

        // Build wire-format public key blob
        var publicKeyBlob = BuildRsaPublicKeyBlob(rsa);

        // Generate OpenSSH public key
        var base64 = Convert.ToBase64String(publicKeyBlob);
        _publicKeyOpenSsh = string.IsNullOrWhiteSpace(comment)
            ? $"{OpenSshRsaPrefix} {base64}"
            : $"{OpenSshRsaPrefix} {base64} {comment}";

        // Generate PEM private key
        _privateKeyPem = ExportPrivateKeyPem(rsa, passphrase);

        // Compute SHA256 fingerprint from wire-format blob
        _fingerprint = ComputeSha256Fingerprint(publicKeyBlob);

        ShowGeneratedKeys();
    }

    /// <summary>
    /// Generates an Ed25519 key pair using runtime reflection to access the EdDSA API.
    /// Falls back to an error message if the current .NET runtime does not support EdDSA.
    /// </summary>
    private void GenerateEd25519KeyPair()
    {
        if (EdDsaType is null || EdDsaParametersType is null)
        {
            MessageBox.Show(
                L("ToolSshKeyGenEd25519Unsupported"),
                L("ToolSshKeyGenTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var comment = CommentInput.Text.Trim();
        var passphrase = PassphraseInput.Password;

        try
        {
            // Resolve EdDSAParameters.Ed25519 field (Oid)
            var ed25519Field = EdDsaParametersType.GetField("Ed25519", BindingFlags.Public | BindingFlags.Static);
            if (ed25519Field is null)
            {
                ShowEd25519UnsupportedError();
                return;
            }

            var ed25519Oid = ed25519Field.GetValue(null);

            // Create EdDSAParameters instance with the Ed25519 Oid
            var edDsaParams = Activator.CreateInstance(EdDsaParametersType, ed25519Oid);

            // Call EdDSA.Create(EdDSAParameters)
            var createMethod = EdDsaType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, [EdDsaParametersType]);
            if (createMethod is null)
            {
                ShowEd25519UnsupportedError();
                return;
            }

            using var edKey = createMethod.Invoke(null, [edDsaParams]) as AsymmetricAlgorithm;
            if (edKey is null)
            {
                ShowEd25519UnsupportedError();
                return;
            }

            // Export public key bytes via ExportEdDSAPublicKey(Span<byte>)
            var publicKeyBytes = ExportEd25519PublicKeyViaReflection(edKey);
            if (publicKeyBytes is null)
            {
                ShowEd25519UnsupportedError();
                return;
            }

            // Build wire-format public key blob
            var publicKeyBlob = BuildEd25519PublicKeyBlob(publicKeyBytes);

            // Format OpenSSH public key
            var base64 = Convert.ToBase64String(publicKeyBlob);
            _publicKeyOpenSsh = string.IsNullOrWhiteSpace(comment)
                ? $"{OpenSshEd25519Prefix} {base64}"
                : $"{OpenSshEd25519Prefix} {base64} {comment}";

            // Export private key PEM
            _privateKeyPem = ExportAsymmetricKeyPem(edKey, passphrase);

            // Compute fingerprint
            _fingerprint = ComputeSha256Fingerprint(publicKeyBlob);

            ShowGeneratedKeys();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is PlatformNotSupportedException or NotSupportedException)
        {
            ShowEd25519UnsupportedError();
        }
        catch (PlatformNotSupportedException)
        {
            ShowEd25519UnsupportedError();
        }
        catch (NotSupportedException)
        {
            ShowEd25519UnsupportedError();
        }
    }

    private void ShowEd25519UnsupportedError()
    {
        MessageBox.Show(
            L("ToolSshKeyGenEd25519Unsupported"),
            L("ToolSshKeyGenTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// Exports the Ed25519 public key bytes via reflection on ExportEdDSAPublicKey.
    /// </summary>
    private static byte[]? ExportEd25519PublicKeyViaReflection(AsymmetricAlgorithm edKey)
    {
        // Look for ExportEdDSAPublicKey that takes byte[] (or a similar overload)
        var exportMethod = edKey.GetType().GetMethod("ExportEdDSAPublicKey", BindingFlags.Public | BindingFlags.Instance, [typeof(byte[])]);
        if (exportMethod is not null)
        {
            var buffer = new byte[Ed25519PublicKeyLength];
            exportMethod.Invoke(edKey, [buffer]);
            return buffer;
        }

        // Try the Span<byte> overload via a byte array wrapper:
        // Use ExportSubjectPublicKeyInfo and parse the Ed25519 public key from the DER encoding
        var derBytes = edKey.ExportSubjectPublicKeyInfo();
        return ExtractEd25519PublicKeyFromDer(derBytes);
    }

    /// <summary>
    /// Extracts the 32-byte Ed25519 public key from a DER-encoded SubjectPublicKeyInfo.
    /// Ed25519 SPKI has a fixed structure: the last 32 bytes are the raw public key.
    /// </summary>
    private static byte[]? ExtractEd25519PublicKeyFromDer(byte[] derBytes)
    {
        // Ed25519 SubjectPublicKeyInfo is 44 bytes:
        // SEQUENCE { SEQUENCE { OID 1.3.101.112 }, BIT STRING (32 bytes) }
        // The raw 32-byte public key is always the last 32 bytes
        if (derBytes.Length < Ed25519PublicKeyLength)
        {
            return null;
        }

        var publicKey = new byte[Ed25519PublicKeyLength];
        Array.Copy(derBytes, derBytes.Length - Ed25519PublicKeyLength, publicKey, 0, Ed25519PublicKeyLength);
        return publicKey;
    }

    private void ShowGeneratedKeys()
    {
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
    /// Builds the OpenSSH wire-format public key blob for an RSA key.
    /// Format: string "ssh-rsa", mpint e, mpint n.
    /// </summary>
    private static byte[] BuildRsaPublicKeyBlob(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);

        using var ms = new System.IO.MemoryStream();
        WriteOpenSshString(ms, OpenSshRsaPrefix);
        WriteOpenSshMpint(ms, parameters.Exponent!);
        WriteOpenSshMpint(ms, parameters.Modulus!);

        return ms.ToArray();
    }

    /// <summary>
    /// Builds the OpenSSH wire-format public key blob for an Ed25519 key.
    /// Format: string "ssh-ed25519", string (32-byte public key).
    /// </summary>
    private static byte[] BuildEd25519PublicKeyBlob(byte[] publicKeyBytes)
    {
        using var ms = new System.IO.MemoryStream();
        WriteOpenSshString(ms, OpenSshEd25519Prefix);
        WriteOpenSshBytes(ms, publicKeyBytes);

        return ms.ToArray();
    }

    /// <summary>
    /// Exports a private key as PKCS#8 PEM, optionally encrypted with AES-256-CBC.
    /// Works with any AsymmetricAlgorithm (RSA, EdDSA, etc.).
    /// </summary>
    private static string ExportAsymmetricKeyPem(AsymmetricAlgorithm key, string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            return key.ExportPkcs8PrivateKeyPem();
        }

        var pbeParams = new PbeParameters(
            PbeEncryptionAlgorithm.Aes256Cbc,
            HashAlgorithmName.SHA256,
            PbeIterationCount);

        return key.ExportEncryptedPkcs8PrivateKeyPem(
            passphrase.AsSpan(),
            pbeParams);
    }

    /// <summary>
    /// Exports the RSA private key as PKCS#8 PEM, optionally encrypted with AES-256-CBC.
    /// </summary>
    private static string ExportPrivateKeyPem(RSA rsa, string passphrase)
    {
        return ExportAsymmetricKeyPem(rsa, passphrase);
    }

    /// <summary>
    /// Computes the SHA256 fingerprint of a public key blob in the format: SHA256:BASE64_HASH
    /// </summary>
    private static string ComputeSha256Fingerprint(byte[] publicKeyBlob)
    {
        var hash = SHA256.HashData(publicKeyBlob);
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
    /// Writes an OpenSSH wire-format byte string (4-byte big-endian length + raw bytes).
    /// </summary>
    private static void WriteOpenSshBytes(System.IO.Stream stream, byte[] value)
    {
        Span<byte> lengthBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBuf, value.Length);
        stream.Write(lengthBuf);
        stream.Write(value);
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
