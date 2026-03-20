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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// IPv4 subnet calculator tool that displays network information for a given CIDR notation.
/// </summary>
public partial class SubnetCalculatorView : UserControl, IDisposable
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
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolSubnetCalculatorTitle");
        LblCidrInput.Text = L("ToolSubnetCidrInputLabel");
        BtnCalculate.Content = L("ToolSubnetBtnCalculate");
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

        System.Windows.Automation.AutomationProperties.SetName(BtnCalculate, L("ToolSubnetBtnCalculate"));
        System.Windows.Automation.AutomationProperties.SetName(TxtCidrInput, L("ToolSubnetCidrInputLabel"));
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

    private void OnCalculateClick(object sender, RoutedEventArgs e)
    {
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

        if (!TryParseCidr(input, out var address, out var prefixLength))
        {
            TxtError.Text = L("ToolSubnetErrorInvalidCidr");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        var maskBytes = PrefixToMask(prefixLength);
        var ipBytes = address!.GetAddressBytes();

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

        ResultsPanel.Visibility = Visibility.Visible;
    }

    private static bool TryParseCidr(string input, out IPAddress? address, out int prefixLength)
    {
        address = null;
        prefixLength = 0;

        var parts = input.Split('/');
        if (parts.Length == 1)
        {
            // Bare IP address defaults to /32
            if (!IPAddress.TryParse(parts[0], out address))
            {
                return false;
            }

            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
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

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out prefixLength) || prefixLength < 0 || prefixLength > 32)
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
