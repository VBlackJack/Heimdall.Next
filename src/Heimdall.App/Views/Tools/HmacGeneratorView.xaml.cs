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
/// HMAC generator tool that computes keyed-hash message authentication codes.
/// Supports HMAC-SHA256, HMAC-SHA384, HMAC-SHA512, HMAC-SHA1, and HMAC-MD5.
/// Includes verify mode and show/hide key toggle.
/// </summary>
public partial class HmacGeneratorView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private DispatcherTimer? _debounceTimer;
    private bool _keyVisible;
    private bool _syncingKey;

    private static readonly string[] Algorithms =
        ["HMAC-SHA256", "HMAC-SHA384", "HMAC-SHA512", "HMAC-SHA1", "HMAC-MD5"];

    public HmacGeneratorView()
    {
        InitializeComponent();
        InitializeAlgorithms();
        InitializeDebounceTimer();
    }

    /// <summary>
    /// Initializes the tool with localization and optional context.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            TxtInput.Text = context.Argument;
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtInput.Focus();
            TxtInput.SelectAll();
        });
    }

    private void InitializeAlgorithms()
    {
        foreach (var algo in Algorithms)
        {
            CmbAlgorithm.Items.Add(algo);
        }

        CmbAlgorithm.SelectedIndex = 0; // HMAC-SHA256
    }

    private void InitializeDebounceTimer()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            ComputeHmac();
        };
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolHmacTitle");
        LblAlgorithm.Text = L("ToolHmacAlgorithmLabel");
        LblKey.Text = L("ToolHmacKeyLabel");
        LblInput.Text = L("ToolHmacInputLabel");
        LblOutput.Text = L("ToolHmacOutputLabel");
        LblFormat.Text = L("ToolHmacFormatLabel");
        RdoHex.Content = L("ToolHmacFormatHex");
        RdoBase64.Content = L("ToolHmacFormatBase64");
        BtnCopy.Content = L("ToolHmacBtnCopy");
        LblVerify.Text = L("ToolHmacVerifyLabel");

        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolHmacBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(CmbAlgorithm, L("ToolHmacAlgorithmLabel"));
        System.Windows.Automation.AutomationProperties.SetName(PwdKey, L("ToolHmacKeyLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtKey, L("ToolHmacKeyLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtInput, L("ToolHmacInputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtOutput, L("ToolHmacOutputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(RdoHex, L("ToolHmacHexOutput"));
        System.Windows.Automation.AutomationProperties.SetName(RdoBase64, L("ToolHmacBase64Output"));
        System.Windows.Automation.AutomationProperties.SetName(BtnToggleKey, L("ToolHmacToggleKeyVisibility"));
        System.Windows.Automation.AutomationProperties.SetName(TxtVerify, L("ToolHmacVerifyLabel"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
        BtnToggleKey.ToolTip = L("ToolHmacToggleKeyVisibility");

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));

        TxtKey.Tag = L("ToolWatermarkSecretKey");
        TxtInput.Tag = L("ToolWatermarkMessage");
        TxtVerify.Tag = L("ToolWatermarkPasteHmacVerify");
    }

    private void OnToggleKeyVisibility(object sender, RoutedEventArgs e)
    {
        _keyVisible = !_keyVisible;
        _syncingKey = true;

        if (_keyVisible)
        {
            // Show TextBox, hide PasswordBox
            TxtKey.Text = PwdKey.Password;
            TxtKey.Visibility = Visibility.Visible;
            PwdKey.Visibility = Visibility.Collapsed;
            // Eye-off icon
            BtnToggleKey.Content = "\uED1A";
        }
        else
        {
            // Show PasswordBox, hide TextBox
            PwdKey.Password = TxtKey.Text;
            PwdKey.Visibility = Visibility.Visible;
            TxtKey.Visibility = Visibility.Collapsed;
            // Eye icon
            BtnToggleKey.Content = "\uE7B3";
        }

        _syncingKey = false;
    }

    private void OnKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingKey) return;

        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingKey) return;

        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnAlgorithmChanged(object sender, SelectionChangedEventArgs e)
    {
        ComputeHmac();
    }

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        ComputeHmac();
    }

    /// <summary>
    /// Gets the current key text from whichever control is visible.
    /// </summary>
    private string GetCurrentKey()
    {
        return _keyVisible ? (TxtKey?.Text ?? string.Empty) : (PwdKey?.Password ?? string.Empty);
    }

    private void ComputeHmac()
    {
        var input = TxtInput?.Text;
        var key = GetCurrentKey();

        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(key))
        {
            if (TxtOutput is not null) TxtOutput.Text = string.Empty;
            if (TxtByteLength is not null) TxtByteLength.Text = string.Empty;
            UpdateVerifyResult();
            return;
        }

        var algorithmName = CmbAlgorithm?.SelectedItem as string ?? "HMAC-SHA256";

        try
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            using var hmac = CreateHmacAlgorithm(algorithmName, keyBytes);

            if (hmac is null)
            {
                TxtOutput.Text = L("ToolHmacErrorUnsupported");
                TxtByteLength.Text = string.Empty;
                return;
            }

            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = hmac.ComputeHash(inputBytes);

            var useBase64 = RdoBase64?.IsChecked == true;
            TxtOutput.Text = useBase64
                ? Convert.ToBase64String(hashBytes)
                : Convert.ToHexStringLower(hashBytes);

            TxtByteLength.Text = string.Format(
                L("ToolHmacByteLengthFormat"),
                hashBytes.Length,
                hashBytes.Length * 8);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"HmacGenerator computation failed: {ex.Message}");
            TxtOutput.Text = string.Empty;
            TxtByteLength.Text = string.Empty;
        }

        UpdateVerifyResult();
    }

    private void OnVerifyTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateVerifyResult();
    }

    private void UpdateVerifyResult()
    {
        if (TxtVerifyResult is null || TxtVerify is null || TxtOutput is null) return;

        var expected = TxtVerify.Text.Trim();

        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(GetCurrentKey()) || string.IsNullOrEmpty(TxtInput?.Text))
        {
            TxtVerifyResult.Text = string.Empty;
            return;
        }

        // Compute both hex and base64 outputs for comparison
        var hexOutput = ComputeHmacFormatted(useBase64: false);
        var b64Output = ComputeHmacFormatted(useBase64: true);

        bool match = (!string.IsNullOrEmpty(hexOutput) && string.Equals(expected, hexOutput, StringComparison.OrdinalIgnoreCase))
                  || (!string.IsNullOrEmpty(b64Output) && string.Equals(expected, b64Output, StringComparison.OrdinalIgnoreCase));

        if (match)
        {
            TxtVerifyResult.Text = L("ToolHmacVerifyMatch");
            TxtVerifyResult.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            TxtVerifyResult.Text = L("ToolHmacVerifyNoMatch");
            TxtVerifyResult.Foreground = (Brush)FindResource("ErrorBrush");
        }
    }

    /// <summary>
    /// Computes the HMAC in the specified format without updating the UI output.
    /// </summary>
    private string? ComputeHmacFormatted(bool useBase64)
    {
        var input = TxtInput?.Text;
        var key = GetCurrentKey();
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(key)) return null;

        var algorithmName = CmbAlgorithm?.SelectedItem as string ?? "HMAC-SHA256";

        try
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            using var hmac = CreateHmacAlgorithm(algorithmName, keyBytes);
            if (hmac is null) return null;

            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = hmac.ComputeHash(inputBytes);

            return useBase64
                ? Convert.ToBase64String(hashBytes)
                : Convert.ToHexStringLower(hashBytes);
        }
        catch
        {
            return null;
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var hash = TxtOutput.Text;
        if (!string.IsNullOrEmpty(hash))
        {
            try
            {
                Clipboard.SetText(hash);
                CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"HmacGenerator clipboard copy failed: {ex.Message}");
            }
        }
    }

    private static HMAC? CreateHmacAlgorithm(string name, byte[] key) => name switch
    {
        "HMAC-MD5" => new HMACMD5(key),
        "HMAC-SHA1" => new HMACSHA1(key),
        "HMAC-SHA256" => new HMACSHA256(key),
        "HMAC-SHA384" => new HMACSHA384(key),
        "HMAC-SHA512" => new HMACSHA512(key),
        _ => null
    };

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpHMAC").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private string L(string key) => _localizer?[key] ?? key;

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
