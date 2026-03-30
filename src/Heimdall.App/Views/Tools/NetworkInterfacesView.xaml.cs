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

using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Lists all network interfaces with IP, MAC, speed, status, and DHCP info.
/// Uses <see cref="NetworkInterface.GetAllNetworkInterfaces"/> — no P/Invoke or external tool.
/// </summary>
public partial class NetworkInterfacesView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private readonly ObservableCollection<NicEntry> _entries = [];

    public NetworkInterfacesView()
    {
        InitializeComponent();
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        InterfacesGrid.ItemsSource = _entries;
        Refresh();
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolNetIfTitle");
        BtnRefresh.Content = L("ToolNetIfBtnRefresh");
        BtnCopy.Content = L("ToolBtnCopyToClipboard");

        ColName.Header = L("ToolNetIfColName");
        ColType.Header = L("ToolNetIfColType");
        ColStatus.Header = L("ToolNetIfColStatus");
        ColSpeed.Header = L("ToolNetIfColSpeed");
        ColMac.Header = L("ToolNetIfColMac");
        ColIpv4.Header = L("ToolNetIfColIpv4");
        ColSubnet.Header = L("ToolNetIfColSubnet");
        ColGateway.Header = L("ToolNetIfColGateway");
        ColDhcp.Header = L("ToolNetIfColDhcp");

        System.Windows.Automation.AutomationProperties.SetName(BtnRefresh, L("ToolNetIfBtnRefresh"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolBtnCopyToClipboard"));
        System.Windows.Automation.AutomationProperties.SetName(InterfacesGrid, L("ToolNetIfTitle"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        _entries.Clear();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var props = nic.GetIPProperties();

            var ipv4 = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

            var gateway = props.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

            var dhcp = props.DhcpServerAddresses.Count > 0;

            _entries.Add(new NicEntry
            {
                Name = nic.Name,
                InterfaceType = nic.NetworkInterfaceType.ToString(),
                Status = nic.OperationalStatus.ToString(),
                Speed = FormatSpeed(nic.Speed),
                Mac = FormatMac(nic.GetPhysicalAddress()),
                Ipv4 = ipv4?.Address.ToString() ?? "",
                Subnet = ipv4?.IPv4Mask?.ToString() ?? "",
                Gateway = gateway?.Address.ToString() ?? "",
                Dhcp = dhcp ? "DHCP" : "Static",
            });
        }

        TxtStatus.Text = string.Format(CultureInfo.InvariantCulture,
            L("ToolNetIfStatus"), _entries.Count,
            DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }

    private static string FormatSpeed(long bitsPerSecond)
    {
        return bitsPerSecond switch
        {
            <= 0 => "-",
            < 1_000_000 => $"{bitsPerSecond / 1_000} Kbps",
            < 1_000_000_000 => $"{bitsPerSecond / 1_000_000} Mbps",
            _ => $"{bitsPerSecond / 1_000_000_000} Gbps",
        };
    }

    private static string FormatMac(PhysicalAddress mac)
    {
        var bytes = mac.GetAddressBytes();
        if (bytes.Length == 0) return "";
        return string.Join(":", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name\tType\tStatus\tSpeed\tMAC\tIPv4\tSubnet\tGateway\tDHCP");
        foreach (var entry in _entries)
        {
            sb.Append(entry.Name).Append('\t')
              .Append(entry.InterfaceType).Append('\t')
              .Append(entry.Status).Append('\t')
              .Append(entry.Speed).Append('\t')
              .Append(entry.Mac).Append('\t')
              .Append(entry.Ipv4).Append('\t')
              .Append(entry.Subnet).Append('\t')
              .Append(entry.Gateway).Append('\t')
              .AppendLine(entry.Dhcp);
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible) { HelpPanel.Visibility = Visibility.Collapsed; return; }
        TxtHelpContent.Text = L("ToolHelpNETIF").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
        => HelpPanel.Visibility = Visibility.Collapsed;

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose() => GC.SuppressFinalize(this);

    public sealed class NicEntry
    {
        public required string Name { get; init; }
        public required string InterfaceType { get; init; }
        public required string Status { get; init; }
        public required string Speed { get; init; }
        public required string Mac { get; init; }
        public required string Ipv4 { get; init; }
        public required string Subnet { get; init; }
        public required string Gateway { get; init; }
        public required string Dhcp { get; init; }
    }
}
