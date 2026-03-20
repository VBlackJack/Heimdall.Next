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
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// HMAC generator tool that computes keyed-hash message authentication codes.
/// Supports HMAC-SHA256, HMAC-SHA384, HMAC-SHA512, HMAC-SHA1, and HMAC-MD5.
/// </summary>
public partial class HmacGeneratorView : UserControl, IDisposable
{
    private LocalizationManager? _localizer;
    private DispatcherTimer? _debounceTimer;

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
        BtnCopy.Content = L("ToolHmacBtnCopy");

        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolHmacBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(CmbAlgorithm, L("ToolHmacAlgorithmLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtKey, L("ToolHmacKeyLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtInput, L("ToolHmacInputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtOutput, L("ToolHmacOutputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(RdoHex, L("ToolHmacHexOutput"));
        System.Windows.Automation.AutomationProperties.SetName(RdoBase64, L("ToolHmacBase64Output"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
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

    private void ComputeHmac()
    {
        var input = TxtInput?.Text;
        var key = TxtKey?.Text;

        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(key))
        {
            if (TxtOutput is not null) TxtOutput.Text = string.Empty;
            if (TxtByteLength is not null) TxtByteLength.Text = string.Empty;
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

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        if (_debounceTimer is not null)
        {
            _debounceTimer.Stop();
            _debounceTimer = null;
        }
    }
}
