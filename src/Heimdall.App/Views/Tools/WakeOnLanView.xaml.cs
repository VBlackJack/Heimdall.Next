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
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Wake-on-LAN tool. Sends a magic packet (6x 0xFF + 16x MAC address)
/// via UDP broadcast to wake a remote machine from sleep/standby.
/// </summary>
public partial class WakeOnLanView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private const int DefaultWolPort = 9;
    private const string DefaultBroadcastAddress = "255.255.255.255";
    private const int MagicPacketLength = 102;

    public WakeOnLanView()
    {
        InitializeComponent();
        TxtMac.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnSendClick(s, e); };
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        TxtBroadcast.Text = DefaultBroadcastAddress;
        TxtPort.Text = DefaultWolPort.ToString(CultureInfo.InvariantCulture);

        // Only prefill if the value is actually a valid MAC address
        if (!string.IsNullOrWhiteSpace(context?.Argument) && TryParseMac(context.Argument, out _))
            TxtMac.Text = context.Argument;
        else if (!string.IsNullOrWhiteSpace(context?.TargetHost) && TryParseMac(context.TargetHost, out _))
            TxtMac.Text = context.TargetHost;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtMac.Focus();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolWolTitle");
        LblMac.Text = L("ToolWolMacLabel");
        LblBroadcast.Text = L("ToolWolBroadcastLabel");
        LblPort.Text = L("ToolWolPortLabel");
        BtnSend.Content = L("ToolWolBtnSend");
        LblHistory.Text = L("ToolWolHistoryLabel");

        System.Windows.Automation.AutomationProperties.SetName(TxtMac, L("ToolWolMacLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtBroadcast, L("ToolWolBroadcastLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtPort, L("ToolWolPortLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSend, L("ToolWolBtnSend"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHistory, L("ToolWolHistoryLabel"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
    }

    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        var macInput = TxtMac.Text.Trim();
        var broadcastInput = TxtBroadcast.Text.Trim();

        if (!int.TryParse(TxtPort.Text.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            port = DefaultWolPort;
            TxtPort.Text = port.ToString(CultureInfo.InvariantCulture);
        }

        if (!TryParseMac(macInput, out var macBytes))
        {
            TxtStatus.Text = L("ToolWolErrorInvalidMac");
            TxtStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            return;
        }

        if (!IPAddress.TryParse(broadcastInput, out var broadcastAddress))
        {
            TxtStatus.Text = L("ToolWolErrorInvalidBroadcast");
            TxtStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            return;
        }

        BtnSend.IsEnabled = false;
        TxtStatus.Text = L("ToolWolStatusSending");
        TxtStatus.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");

        try
        {
            var packet = BuildMagicPacket(macBytes);
            await SendMagicPacketAsync(packet, broadcastAddress, port);

            var formattedMac = FormatMac(macBytes);
            var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            TxtStatus.Text = string.Format(CultureInfo.InvariantCulture,
                L("ToolWolStatusSent"), formattedMac, broadcastAddress, port);
            TxtStatus.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");

            // Append to history
            var entry = $"[{timestamp}] {formattedMac} → {broadcastAddress}:{port}";
            TxtHistory.Text = string.IsNullOrEmpty(TxtHistory.Text)
                ? entry
                : entry + Environment.NewLine + TxtHistory.Text;
        }
        catch (SocketException ex)
        {
            TxtStatus.Text = string.Format(CultureInfo.InvariantCulture,
                L("ToolWolErrorSocket"), ex.Message);
            TxtStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
        }
        finally
        {
            BtnSend.IsEnabled = true;
        }
    }

    /// <summary>
    /// Builds a WoL magic packet: 6 bytes of 0xFF followed by the target
    /// MAC address repeated 16 times (102 bytes total).
    /// </summary>
    private static byte[] BuildMagicPacket(byte[] mac)
    {
        var packet = new byte[MagicPacketLength];
        // 6 bytes of 0xFF header
        for (var i = 0; i < 6; i++)
            packet[i] = 0xFF;
        // 16 repetitions of the MAC address
        for (var i = 0; i < 16; i++)
            Buffer.BlockCopy(mac, 0, packet, 6 + (i * 6), 6);
        return packet;
    }

    private static async Task SendMagicPacketAsync(byte[] packet, IPAddress broadcast, int port)
    {
        using var client = new UdpClient();
        client.EnableBroadcast = true;
        var endpoint = new IPEndPoint(broadcast, port);
        await client.SendAsync(packet, packet.Length, endpoint);
    }

    /// <summary>
    /// Parses MAC address from common formats:
    /// AA:BB:CC:DD:EE:FF, AA-BB-CC-DD-EE-FF, AABBCCDDEEFF
    /// </summary>
    private static bool TryParseMac(string input, out byte[] mac)
    {
        mac = [];
        if (string.IsNullOrWhiteSpace(input)) return false;

        // Strip separators
        var hex = Regex.Replace(input, @"[:\-.\s]", "");
        if (hex.Length != 12) return false;
        if (!Regex.IsMatch(hex, @"^[0-9A-Fa-f]{12}$")) return false;

        mac = new byte[6];
        for (var i = 0; i < 6; i++)
        {
            mac[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return true;
    }

    private static string FormatMac(byte[] mac)
    {
        var sb = new StringBuilder(17);
        for (var i = 0; i < mac.Length; i++)
        {
            if (i > 0) sb.Append(':');
            sb.Append(mac[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpWOL").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
