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
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Advanced network calculator with three modes: supernet computation,
/// IP range to CIDR conversion, and VLAN capacity planning.
/// All calculations are IPv4-only.
/// </summary>
public partial class NetworkCalculatorView : UserControl, IToolView
{
    private const int Ipv4Bits = 32;
    private const int MinPrefix = 0;
    private const int MaxPrefix = 32;

    private LocalizationManager? _localizer;

    public NetworkCalculatorView()
    {
        InitializeComponent();
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        // Default sample data
        TxtSupernetInput.Text = "192.168.1.0/24\n192.168.2.0/24";
        TxtStartIp.Text = "10.0.0.1";
        TxtEndIp.Text = "10.0.0.254";
        TxtHostsNeeded.Text = "200";
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolNetCalcTitle");
        LblMode.Text = L("ToolNetCalcModeLabel");
        RbSupernet.Content = L("ToolNetCalcModeSupernet");
        RbRangeToCidr.Content = L("ToolNetCalcModeRangeToCidr");
        RbVlanPlanner.Content = L("ToolNetCalcModeVlanPlanner");

        LblSupernetInput.Text = L("ToolNetCalcSupernetInputLabel");
        BtnSupernetCompute.Content = L("ToolNetCalcBtnCompute");

        LblStartIp.Text = L("ToolNetCalcStartIp");
        LblEndIp.Text = L("ToolNetCalcEndIp");
        BtnRangeCompute.Content = L("ToolNetCalcBtnCompute");

        LblHostsNeeded.Text = L("ToolNetCalcHostsNeeded");
        LblBaseNetwork.Text = L("ToolNetCalcBaseNetwork");
        BtnVlanCompute.Content = L("ToolNetCalcBtnCompute");

        BtnCopyResult.Content = L("ToolBtnCopyValue");
        BtnCopyResult.ToolTip = L("ToolBtnCopyToClipboard");

        AutomationProperties.SetName(TxtSupernetInput, L("ToolNetCalcSupernetInputLabel"));
        AutomationProperties.SetName(TxtStartIp, L("ToolNetCalcStartIp"));
        AutomationProperties.SetName(TxtEndIp, L("ToolNetCalcEndIp"));
        AutomationProperties.SetName(TxtHostsNeeded, L("ToolNetCalcHostsNeeded"));
        AutomationProperties.SetName(TxtBaseNetwork, L("ToolNetCalcBaseNetwork"));
        AutomationProperties.SetName(BtnSupernetCompute, L("ToolNetCalcBtnCompute"));
        AutomationProperties.SetName(BtnRangeCompute, L("ToolNetCalcBtnCompute"));
        AutomationProperties.SetName(BtnVlanCompute, L("ToolNetCalcBtnCompute"));
        AutomationProperties.SetName(BtnCopyResult, L("ToolBtnCopyValue"));
        AutomationProperties.SetName(TxtResult, L("ToolNetCalcResult"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (PanelSupernet is null) return; // Guard against InitializeComponent firing

        PanelSupernet.Visibility = RbSupernet.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelRangeToCidr.Visibility = RbRangeToCidr.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelVlanPlanner.Visibility = RbVlanPlanner.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        TxtError.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
    }

    // ── Supernet ────────────────────────────────────────────────

    private void OnSupernetComputeClick(object sender, RoutedEventArgs e)
    {
        ClearResults();

        var lines = TxtSupernetInput.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
        {
            ShowError(L("ToolNetCalcErrorNoCidrs"));
            return;
        }

        var networks = new List<(uint Network, int Prefix)>();
        foreach (var line in lines)
        {
            if (!TryParseCidr(line, out var network, out var prefix))
            {
                ShowError(string.Format(L("ToolNetCalcErrorInvalidCidr"), line));
                return;
            }
            networks.Add((network, prefix));
        }

        // Find the smallest supernet covering all networks
        uint minIp = uint.MaxValue;
        uint maxIp = uint.MinValue;

        foreach (var (network, prefix) in networks)
        {
            var mask = PrefixToMask(prefix);
            var netAddr = network & mask;
            var broadcast = netAddr | ~mask;

            if (netAddr < minIp) minIp = netAddr;
            if (broadcast > maxIp) maxIp = broadcast;
        }

        // Find the common prefix length
        var supernetPrefix = CommonPrefixLength(minIp, maxIp);

        // Align to supernet boundary
        var supernetMask = PrefixToMask(supernetPrefix);
        var supernetNetwork = minIp & supernetMask;
        var supernetBroadcast = supernetNetwork | ~supernetMask;

        // Verify the supernet covers the max IP
        while (supernetBroadcast < maxIp && supernetPrefix > MinPrefix)
        {
            supernetPrefix--;
            supernetMask = PrefixToMask(supernetPrefix);
            supernetNetwork = minIp & supernetMask;
            supernetBroadcast = supernetNetwork | ~supernetMask;
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(L("ToolNetCalcSupernetResult"),
            UintToIp(supernetNetwork), supernetPrefix));
        sb.AppendLine(string.Format(L("ToolNetCalcSupernetRange"),
            UintToIp(supernetNetwork), UintToIp(supernetBroadcast)));

        long totalHosts = (1L << (Ipv4Bits - supernetPrefix)) - 2;
        if (totalHosts < 0) totalHosts = 0;
        sb.AppendLine(string.Format(L("ToolNetCalcSupernetHosts"), totalHosts.ToString("N0")));

        ShowResult(sb.ToString().TrimEnd());
    }

    // ── Range to CIDR ───────────────────────────────────────────

    private void OnRangeComputeClick(object sender, RoutedEventArgs e)
    {
        ClearResults();

        if (!IPAddress.TryParse(TxtStartIp.Text.Trim(), out var startAddr) ||
            !IPAddress.TryParse(TxtEndIp.Text.Trim(), out var endAddr))
        {
            ShowError(L("ToolNetCalcErrorInvalidIpRange"));
            return;
        }

        var start = IpToUint(startAddr);
        var end = IpToUint(endAddr);

        if (start > end)
        {
            ShowError(L("ToolNetCalcErrorStartAfterEnd"));
            return;
        }

        var cidrs = RangeToCidrs(start, end);
        var sb = new StringBuilder();
        sb.AppendLine(L("ToolNetCalcRangeResult"));
        foreach (var cidr in cidrs)
        {
            sb.AppendLine($"  {cidr}");
        }

        ShowResult(sb.ToString().TrimEnd());
    }

    // ── VLAN Planner ────────────────────────────────────────────

    private void OnVlanComputeClick(object sender, RoutedEventArgs e)
    {
        ClearResults();

        if (!int.TryParse(TxtHostsNeeded.Text.Trim(), out var hostsNeeded) || hostsNeeded <= 0)
        {
            ShowError(L("ToolNetCalcErrorInvalidHostCount"));
            return;
        }

        if (!IPAddress.TryParse(TxtBaseNetwork.Text.Trim(), out var baseAddr))
        {
            ShowError(L("ToolNetCalcErrorInvalidBaseNetwork"));
            return;
        }

        // Find the smallest prefix that accommodates hostsNeeded + 2 (network + broadcast)
        int requiredAddresses = hostsNeeded + 2;
        int hostBits = 0;
        while ((1 << hostBits) < requiredAddresses && hostBits < Ipv4Bits)
        {
            hostBits++;
        }

        int prefix = Ipv4Bits - hostBits;
        if (prefix < MinPrefix) prefix = MinPrefix;

        var mask = PrefixToMask(prefix);
        var networkUint = IpToUint(baseAddr) & mask;
        var broadcastUint = networkUint | ~mask;
        long usableHosts = (1L << hostBits) - 2;
        if (usableHosts < 0) usableHosts = 0;

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(L("ToolNetCalcVlanNetwork"),
            UintToIp(networkUint), prefix));
        sb.AppendLine(string.Format(L("ToolNetCalcVlanSubnetMask"),
            UintToIp(mask)));
        sb.AppendLine(string.Format(L("ToolNetCalcVlanBroadcast"),
            UintToIp(broadcastUint)));
        sb.AppendLine(string.Format(L("ToolNetCalcVlanUsableRange"),
            UintToIp(networkUint + 1), UintToIp(broadcastUint - 1)));
        sb.AppendLine(string.Format(L("ToolNetCalcVlanUsableHosts"),
            usableHosts.ToString("N0")));
        sb.AppendLine(string.Format(L("ToolNetCalcVlanRequested"),
            hostsNeeded.ToString("N0")));

        double utilization = hostsNeeded * 100.0 / Math.Max(1, usableHosts);
        sb.AppendLine(string.Format(L("ToolNetCalcVlanUtilization"),
            utilization.ToString("F1")));

        ShowResult(sb.ToString().TrimEnd());
    }

    // ── Helpers ─────────────────────────────────────────────────

    private void OnCopyResultClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtResult.Text))
        {
            Clipboard.SetText(TxtResult.Text);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void ClearResults()
    {
        TxtError.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
    }

    private void ShowResult(string text)
    {
        TxtResult.Text = text;
        ResultsPanel.Visibility = Visibility.Visible;
    }

    private static bool TryParseCidr(string input, out uint network, out int prefix)
    {
        network = 0;
        prefix = 0;

        var parts = input.Split('/');
        if (parts.Length != 2) return false;

        if (!IPAddress.TryParse(parts[0].Trim(), out var addr)) return false;
        if (!int.TryParse(parts[1].Trim(), out prefix)) return false;
        if (prefix < MinPrefix || prefix > MaxPrefix) return false;

        network = IpToUint(addr);
        return true;
    }

    private static uint IpToUint(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
    }

    private static string UintToIp(uint value)
    {
        return $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
    }

    private static uint PrefixToMask(int prefix)
    {
        return prefix == 0 ? 0 : 0xFFFFFFFF << (Ipv4Bits - prefix);
    }

    private static int CommonPrefixLength(uint a, uint b)
    {
        var xor = a ^ b;
        int common = 0;
        for (int i = Ipv4Bits - 1; i >= 0; i--)
        {
            if ((xor & (1u << i)) == 0)
            {
                common++;
            }
            else
            {
                break;
            }
        }
        return common;
    }

    private static List<string> RangeToCidrs(uint start, uint end)
    {
        var result = new List<string>();
        while (start <= end)
        {
            // Find the largest block starting at 'start' that fits within [start, end]
            int maxBits = Ipv4Bits;

            // Limit by alignment: how many trailing zeros in start
            if (start != 0)
            {
                int trailingZeros = 0;
                uint temp = start;
                while ((temp & 1) == 0 && trailingZeros < Ipv4Bits)
                {
                    trailingZeros++;
                    temp >>= 1;
                }
                maxBits = Math.Min(maxBits, trailingZeros);
            }

            // Limit by range size
            long rangeSize = (long)end - start + 1;
            while (maxBits > 0 && (1L << maxBits) > rangeSize)
            {
                maxBits--;
            }

            int prefix = Ipv4Bits - maxBits;
            result.Add($"{UintToIp(start)}/{prefix}");

            // Advance past this block
            var blockSize = 1L << maxBits;
            start += (uint)blockSize;

            // Overflow protection
            if (start == 0) break;
        }
        return result;
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpNETCALC");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        // Reserved for future resource cleanup.
    }
}
