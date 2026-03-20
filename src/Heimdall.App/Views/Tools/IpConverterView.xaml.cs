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

using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// IP address converter tool that displays an IPv4 address in multiple formats:
/// dotted decimal, integer, hexadecimal, binary, and IPv4-mapped IPv6.
/// Accepts input in any of those formats and converts to all others.
/// </summary>
public partial class IpConverterView : UserControl, IDisposable
{
    private LocalizationManager? _localizer;

    public IpConverterView()
    {
        InitializeComponent();
        TxtInput.TextChanged += OnInputTextChanged;
    }

    /// <summary>
    /// Initializes the tool with optional context and localization.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        // Pre-fill with a sensible default (auto-converts via TextChanged); context overrides if provided
        TxtInput.Text = "192.168.1.1";

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtInput.Text = context.TargetHost;
        }
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolIpConvTitle");
        LblInput.Text = L("ToolIpConvInputLabel");
        LblDotted.Text = L("ToolIpConvDottedDecimal");
        LblDecimal.Text = L("ToolIpConvInteger");
        LblHex.Text = L("ToolIpConvHexadecimal");
        LblBinary.Text = L("ToolIpConvBinary");
        LblMappedIpv6.Text = L("ToolIpConvMappedIpv6");

        var copyLabel = L("ToolBtnCopyValue");
        var copyTooltip = L("ToolBtnCopyToClipboard");
        BtnCopyDotted.Content = copyLabel;
        BtnCopyDecimal.Content = copyLabel;
        BtnCopyHex.Content = copyLabel;
        BtnCopyBinary.Content = copyLabel;
        BtnCopyIpv6.Content = copyLabel;
        BtnCopyDotted.ToolTip = copyTooltip;
        BtnCopyDecimal.ToolTip = copyTooltip;
        BtnCopyHex.ToolTip = copyTooltip;
        BtnCopyBinary.ToolTip = copyTooltip;
        BtnCopyIpv6.ToolTip = copyTooltip;

        System.Windows.Automation.AutomationProperties.SetName(TxtInput, L("ToolIpConvInputLabel"));
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        Convert();
    }

    private void Convert()
    {
        var input = TxtInput.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (!TryParseToUint32(input, out var ipValue))
        {
            TxtError.Text = L("ToolIpConvErrorInvalid");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        var bytes = new byte[]
        {
            (byte)(ipValue >> 24),
            (byte)(ipValue >> 16),
            (byte)(ipValue >> 8),
            (byte)ipValue
        };

        TxtDotted.Text = new IPAddress(bytes).ToString();
        TxtDecimal.Text = ipValue.ToString(CultureInfo.InvariantCulture);
        TxtHex.Text = $"0x{ipValue:X8}";
        TxtBinary.Text = string.Join(".",
            System.Convert.ToString(bytes[0], 2).PadLeft(8, '0'),
            System.Convert.ToString(bytes[1], 2).PadLeft(8, '0'),
            System.Convert.ToString(bytes[2], 2).PadLeft(8, '0'),
            System.Convert.ToString(bytes[3], 2).PadLeft(8, '0'));
        TxtMappedIpv6.Text = $"::ffff:{bytes[0]:x02}{bytes[1]:x02}:{bytes[2]:x02}{bytes[3]:x02}";

        ResultsPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Tries to parse a string as an IPv4 address in dotted decimal, integer,
    /// hexadecimal (0x prefix), or dotted binary format.
    /// </summary>
    private static bool TryParseToUint32(string input, out uint result)
    {
        result = 0;

        // Hexadecimal: 0x prefix
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(input.AsSpan(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out result);
        }

        // Dotted binary: e.g. 11000000.10101000.00000001.00000001
        if (input.Contains('.') && input.Replace(".", "").All(c => c is '0' or '1') &&
            input.Split('.') is { Length: 4 } binaryParts)
        {
            try
            {
                var b0 = System.Convert.ToByte(binaryParts[0], 2);
                var b1 = System.Convert.ToByte(binaryParts[1], 2);
                var b2 = System.Convert.ToByte(binaryParts[2], 2);
                var b3 = System.Convert.ToByte(binaryParts[3], 2);
                result = ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Dotted decimal: standard IPv4
        if (IPAddress.TryParse(input, out var addr) &&
            addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = addr.GetAddressBytes();
            result = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) |
                     ((uint)bytes[2] << 8) | bytes[3];
            return true;
        }

        // Plain integer
        if (uint.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        return false;
    }

    private void OnCopyValueClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var text = btn.Tag?.ToString() switch
        {
            "Dotted" => TxtDotted.Text,
            "Decimal" => TxtDecimal.Text,
            "Hex" => TxtHex.Text,
            "Binary" => TxtBinary.Text,
            "Ipv6" => TxtMappedIpv6.Text,
            _ => null
        };

        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
            CopyFeedbackHelper.ShowCopyFeedback(btn);
        }
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        // Reserved for future resource cleanup.
    }
}
