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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// TOTP (Time-based One-Time Password) generator implementing RFC 6238.
/// Accepts a Base32-encoded secret and displays the current 6-digit code
/// with a countdown timer for the 30-second validity window.
/// </summary>
public partial class TotpGeneratorView : UserControl, IToolView
{
    private const int DefaultTimeStep = 30;
    private const int CodeDigits = 6;
    private static readonly int CodeModulus = (int)Math.Pow(10, CodeDigits);

    private LocalizationManager? _localizer;
    private DispatcherTimer? _timer;
    private byte[]? _secretKey;
    private bool _disposed;

    public TotpGeneratorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the tool with localization and optional context.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            TxtSecret.Focus();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolTotpTitle");
        LblSecret.Text = L("ToolTotpSecretLabel");
        BtnGenerate.Content = L("ToolTotpBtnStart");
        LblCode.Text = L("ToolTotpCodeLabel");
        BtnCopy.Content = L("ToolTotpBtnCopy");
        TxtInfo.Text = L("ToolTotpInfo");

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");

        System.Windows.Automation.AutomationProperties.SetName(TxtSecret, L("ToolTotpSecretLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnGenerate, L("ToolTotpBtnStart"));
        System.Windows.Automation.AutomationProperties.SetName(TxtCode, L("ToolTotpCodeLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolTotpBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(ProgressTime, L("ToolTotpTimeRemaining"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
    }

    private void OnInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) OnGenerateClick(sender, e);
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        TxtError.Visibility = Visibility.Collapsed;

        var secret = TxtSecret.Text.Trim().Replace(" ", "").ToUpperInvariant();
        if (string.IsNullOrEmpty(secret))
        {
            TxtError.Text = L("ToolTotpErrorSecretRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _secretKey = Base32Decode(secret);
        }
        catch (FormatException)
        {
            TxtError.Text = L("ToolTotpErrorInvalidBase32");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        CodePanel.Visibility = Visibility.Visible;
        UpdateCode();
        StartTimer();
    }

    private void StartTimer()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        UpdateCode();
    }

    private void UpdateCode()
    {
        if (_secretKey is null) return;

        var now = DateTimeOffset.UtcNow;
        var elapsed = (int)(now.ToUnixTimeSeconds() % DefaultTimeStep);
        var remaining = DefaultTimeStep - elapsed;

        TxtCode.Text = GenerateTotp(_secretKey, DefaultTimeStep);
        ProgressTime.Value = remaining;
        LblTimeRemaining.Text = string.Format(L("ToolTotpTimeRemainingFormat"), remaining);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtCode.Text) && TxtCode.Text != "------")
        {
            try
            {
                Clipboard.SetText(TxtCode.Text);
                CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"TotpGenerator clipboard copy failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Generates a 6-digit TOTP code per RFC 6238.
    /// </summary>
    internal static string GenerateTotp(byte[] key, long timeStep = DefaultTimeStep)
    {
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / timeStep;
        var counterBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(counter));

#pragma warning disable CA5350 // HMAC-SHA1 is required by RFC 6238 TOTP specification
        using var hmac = new HMACSHA1(key);
#pragma warning restore CA5350
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var code = ((hash[offset] & 0x7F) << 24
                  | (hash[offset + 1] & 0xFF) << 16
                  | (hash[offset + 2] & 0xFF) << 8
                  | (hash[offset + 3] & 0xFF)) % CodeModulus;

        return code.ToString("D6");
    }

    /// <summary>
    /// Decodes a Base32-encoded string (RFC 4648) into a byte array.
    /// Uses the standard alphabet: A-Z and 2-7.
    /// </summary>
    internal static byte[] Base32Decode(string base32)
    {
        if (string.IsNullOrEmpty(base32))
            return [];

        // Remove padding
        base32 = base32.TrimEnd('=');

        var output = new byte[base32.Length * 5 / 8];
        var bitBuffer = 0;
        var bitsRemaining = 0;
        var outputIndex = 0;

        foreach (var c in base32)
        {
            var value = CharToBase32Value(c);
            if (value < 0)
                throw new FormatException($"Invalid Base32 character: {c}");

            bitBuffer = (bitBuffer << 5) | value;
            bitsRemaining += 5;

            if (bitsRemaining >= 8)
            {
                bitsRemaining -= 8;
                output[outputIndex++] = (byte)(bitBuffer >> bitsRemaining);
            }
        }

        return output;
    }

    private static int CharToBase32Value(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a',
        >= '2' and <= '7' => c - '2' + 26,
        _ => -1
    };

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpTOTP");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Stop();
        _timer = null;
        _secretKey = null;
        GC.SuppressFinalize(this);
    }
}
