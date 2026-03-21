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
using System.Net.Sockets;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// IPv4/IPv6 subnet calculator tool that displays network information for a given CIDR notation.
/// </summary>
public partial class SubnetCalculatorView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private bool _initialized;

    public SubnetCalculatorView()
    {
        InitializeComponent();
        TxtCidrInput.KeyDown += OnInputKeyDown;
        TxtCidrInput.TextChanged += OnInputChanged;
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        // Pre-fill with a sensible default and auto-calculate; context overrides if provided
        TxtCidrInput.Text = "192.168.1.0/24";

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtCidrInput.Text = context.TargetHost;
        }

        _initialized = true;
        Calculate();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtCidrInput.Focus();
            TxtCidrInput.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolSubnetCalculatorTitle");
        LblCidrInput.Text = L("ToolSubnetCidrInputLabel");
        LblNetwork.Text = L("ToolSubnetNetworkAddress");
        LblBroadcast.Text = L("ToolSubnetBroadcastAddress");
        LblSubnetMask.Text = L("ToolSubnetMask");
        LblFirstHost.Text = L("ToolSubnetFirstHost");
        LblLastHost.Text = L("ToolSubnetLastHost");
        LblTotalHosts.Text = L("ToolSubnetTotalHosts");
        LblCidr.Text = L("ToolSubnetCidrNotation");
        LblWildcard.Text = L("ToolSubnetWildcardMask");

        var copyLabel = L("ToolBtnCopyValue");
        var copyTooltip = L("ToolBtnCopyToClipboard");
        BtnCopyNetwork.Content = copyLabel;
        BtnCopyBroadcast.Content = copyLabel;
        BtnCopyMask.Content = copyLabel;
        BtnCopyFirstHost.Content = copyLabel;
        BtnCopyLastHost.Content = copyLabel;
        BtnCopyTotalHosts.Content = copyLabel;
        BtnCopyCidr.Content = copyLabel;
        BtnCopyWildcard.Content = copyLabel;

        BtnCopyNetwork.ToolTip = copyTooltip;
        BtnCopyBroadcast.ToolTip = copyTooltip;
        BtnCopyMask.ToolTip = copyTooltip;
        BtnCopyFirstHost.ToolTip = copyTooltip;
        BtnCopyLastHost.ToolTip = copyTooltip;
        BtnCopyTotalHosts.ToolTip = copyTooltip;
        BtnCopyCidr.ToolTip = copyTooltip;
        BtnCopyWildcard.ToolTip = copyTooltip;

        System.Windows.Automation.AutomationProperties.SetName(TxtCidrInput, L("ToolSubnetCidrInputLabel"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpSUBNET");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Calculate();
            e.Handled = true;
        }
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        Calculate();
    }

    private void Calculate()
    {
        var input = TxtCidrInput.Text.Trim();
        TxtError.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (!TryParseCidr(input, out var address, out var prefixLength, out var maxPrefix))
        {
            TxtError.Text = L("ToolSubnetErrorInvalidCidr");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        bool isIpv6 = address!.AddressFamily == AddressFamily.InterNetworkV6;

        if (isIpv6)
        {
            CalculateIpv6(address, prefixLength);
        }
        else
        {
            CalculateIpv4(address, prefixLength);
        }

        ResultsPanel.Visibility = Visibility.Visible;
    }

    private void CalculateIpv4(IPAddress address, int prefixLength)
    {
        var maskBytes = PrefixToMask(prefixLength);
        var ipBytes = address.GetAddressBytes();

        // Network address = IP AND mask
        var networkBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
        }

        // Broadcast address = network OR ~mask
        var broadcastBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            broadcastBytes[i] = (byte)(networkBytes[i] | ~maskBytes[i]);
        }

        // Wildcard mask = ~mask
        var wildcardBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            wildcardBytes[i] = (byte)~maskBytes[i];
        }

        // First and last usable hosts
        var firstHostBytes = (byte[])networkBytes.Clone();
        var lastHostBytes = (byte[])broadcastBytes.Clone();

        long totalHosts;
        if (prefixLength == 32)
        {
            totalHosts = 1;
        }
        else if (prefixLength == 31)
        {
            totalHosts = 2;
        }
        else
        {
            firstHostBytes[3] += 1;
            lastHostBytes[3] -= 1;
            totalHosts = (1L << (32 - prefixLength)) - 2;
        }

        TxtNetwork.Text = new IPAddress(networkBytes).ToString();
        TxtBroadcast.Text = new IPAddress(broadcastBytes).ToString();
        TxtSubnetMask.Text = new IPAddress(maskBytes).ToString();
        TxtFirstHost.Text = new IPAddress(firstHostBytes).ToString();
        TxtLastHost.Text = new IPAddress(lastHostBytes).ToString();
        TxtTotalHosts.Text = totalHosts.ToString("N0");
        TxtCidr.Text = $"{new IPAddress(networkBytes)}/{prefixLength}";
        TxtWildcard.Text = new IPAddress(wildcardBytes).ToString();

        // Show IPv4-specific rows
        LblBroadcast.Visibility = Visibility.Visible;
        TxtBroadcast.Visibility = Visibility.Visible;
        BtnCopyBroadcast.Visibility = Visibility.Visible;
        LblSubnetMask.Visibility = Visibility.Visible;
        TxtSubnetMask.Visibility = Visibility.Visible;
        BtnCopyMask.Visibility = Visibility.Visible;
        LblWildcard.Visibility = Visibility.Visible;
        TxtWildcard.Visibility = Visibility.Visible;
        BtnCopyWildcard.Visibility = Visibility.Visible;
    }

    private void CalculateIpv6(IPAddress address, int prefixLength)
    {
        var ipBytes = address.GetAddressBytes();
        const int byteLen = 16;

        // Build mask bytes
        var maskBytes = new byte[byteLen];
        for (int i = 0; i < byteLen; i++)
        {
            int bitsInByte = Math.Max(0, Math.Min(8, prefixLength - i * 8));
            maskBytes[i] = (byte)(0xFF << (8 - bitsInByte));
        }

        // Network address = IP AND mask
        var networkBytes = new byte[byteLen];
        for (int i = 0; i < byteLen; i++)
        {
            networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
        }

        // Last address = network OR ~mask
        var lastBytes = new byte[byteLen];
        for (int i = 0; i < byteLen; i++)
        {
            lastBytes[i] = (byte)(networkBytes[i] | (byte)~maskBytes[i]);
        }

        // First host = network + 1
        var firstHostBytes = (byte[])networkBytes.Clone();
        IncrementIpv6Bytes(firstHostBytes);

        // Last host = last - 1
        var lastHostBytes = (byte[])lastBytes.Clone();
        DecrementIpv6Bytes(lastHostBytes);

        // Total hosts
        int hostBits = 128 - prefixLength;
        string totalHostsDisplay;
        if (prefixLength == 128)
        {
            totalHostsDisplay = "1";
        }
        else if (prefixLength == 127)
        {
            totalHostsDisplay = "2";
        }
        else if (hostBits > 64)
        {
            totalHostsDisplay = L("ToolSubnetIpv6TooManyHosts");
        }
        else
        {
            // For hostBits <= 64, compute (2^hostBits - 2)
            var total = (BigInteger.One << hostBits) - 2;
            totalHostsDisplay = total.ToString("N0");
        }

        TxtNetwork.Text = new IPAddress(networkBytes).ToString();
        TxtFirstHost.Text = new IPAddress(firstHostBytes).ToString();
        TxtLastHost.Text = new IPAddress(lastHostBytes).ToString();
        TxtTotalHosts.Text = totalHostsDisplay;
        TxtCidr.Text = $"{new IPAddress(networkBytes)}/{prefixLength}";

        // Hide IPv4-specific rows
        LblBroadcast.Visibility = Visibility.Collapsed;
        TxtBroadcast.Visibility = Visibility.Collapsed;
        BtnCopyBroadcast.Visibility = Visibility.Collapsed;
        LblSubnetMask.Visibility = Visibility.Collapsed;
        TxtSubnetMask.Visibility = Visibility.Collapsed;
        BtnCopyMask.Visibility = Visibility.Collapsed;
        LblWildcard.Visibility = Visibility.Collapsed;
        TxtWildcard.Visibility = Visibility.Collapsed;
        BtnCopyWildcard.Visibility = Visibility.Collapsed;

        TxtBroadcast.Text = string.Empty;
        TxtSubnetMask.Text = string.Empty;
        TxtWildcard.Text = string.Empty;
    }

    private static void IncrementIpv6Bytes(byte[] bytes)
    {
        for (int i = bytes.Length - 1; i >= 0; i--)
        {
            if (bytes[i] < 0xFF)
            {
                bytes[i]++;
                return;
            }
            bytes[i] = 0;
        }
    }

    private static void DecrementIpv6Bytes(byte[] bytes)
    {
        for (int i = bytes.Length - 1; i >= 0; i--)
        {
            if (bytes[i] > 0)
            {
                bytes[i]--;
                return;
            }
            bytes[i] = 0xFF;
        }
    }

    private static bool TryParseCidr(string input, out IPAddress? address, out int prefixLength, out int maxPrefix)
    {
        address = null;
        prefixLength = 0;
        maxPrefix = 32;

        var parts = input.Split('/');
        if (parts.Length == 1)
        {
            // Bare IP address
            if (!IPAddress.TryParse(parts[0], out address))
            {
                return false;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                maxPrefix = 128;
                prefixLength = 128;
                return true;
            }

            if (address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            prefixLength = 32;
            return true;
        }

        if (parts.Length != 2)
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[0], out address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            maxPrefix = 128;
        }
        else if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out prefixLength) || prefixLength < 0 || prefixLength > maxPrefix)
        {
            return false;
        }

        return true;
    }

    private static byte[] PrefixToMask(int prefixLength)
    {
        uint mask = prefixLength == 0 ? 0 : 0xFFFFFFFF << (32 - prefixLength);
        return
        [
            (byte)(mask >> 24),
            (byte)(mask >> 16),
            (byte)(mask >> 8),
            (byte)mask
        ];
    }

    private void OnCopyValueClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var text = btn.Tag?.ToString() switch
        {
            "Network" => TxtNetwork.Text,
            "Broadcast" => TxtBroadcast.Text,
            "Mask" => TxtSubnetMask.Text,
            "FirstHost" => TxtFirstHost.Text,
            "LastHost" => TxtLastHost.Text,
            "TotalHosts" => TxtTotalHosts.Text,
            "Cidr" => TxtCidr.Text,
            "Wildcard" => TxtWildcard.Text,
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
