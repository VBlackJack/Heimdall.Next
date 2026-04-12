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
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Lists all open TCP and UDP ports on the local machine with process info.
/// Uses P/Invoke <c>GetExtendedTcpTable</c> / <c>GetExtendedUdpTable</c> — no external tool required.
/// </summary>
public partial class OpenPortsView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private readonly ObservableCollection<PortEntry> _entries = [];
    private ICollectionView? _view;

    public OpenPortsView()
    {
        InitializeComponent();
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        PortsGrid.ItemsSource = _entries;
        _view = CollectionViewSource.GetDefaultView(_entries);
        _view.Filter = FilterPredicate;

        Refresh();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtFilter.Focus();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolOpenPortsTitle");
        BtnRefresh.Content = L("ToolOpenPortsBtnRefresh");
        BtnCopy.Content = L("ToolBtnCopyToClipboard");
        TxtFilter.Tag = L("ToolOpenPortsFilterPlaceholder");

        ColProtocol.Header = L("ToolOpenPortsColProtocol");
        ColLocalAddress.Header = L("ToolOpenPortsColLocalAddr");
        ColLocalPort.Header = L("ToolOpenPortsColLocalPort");
        ColRemoteAddress.Header = L("ToolOpenPortsColRemoteAddr");
        ColRemotePort.Header = L("ToolOpenPortsColRemotePort");
        ColState.Header = L("ToolOpenPortsColState");
        ColPid.Header = L("ToolOpenPortsColPid");
        ColProcessName.Header = L("ToolOpenPortsColProcess");

        System.Windows.Automation.AutomationProperties.SetName(BtnRefresh, L("ToolOpenPortsBtnRefresh"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolBtnCopyToClipboard"));
        System.Windows.Automation.AutomationProperties.SetName(TxtFilter, L("ToolOpenPortsFilterPlaceholder"));
        System.Windows.Automation.AutomationProperties.SetName(PortsGrid, L("ToolOpenPortsTitle"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

    private bool FilterPredicate(object obj)
    {
        if (obj is not PortEntry entry) return false;
        var filter = TxtFilter.Text;
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return entry.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.LocalAddress.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.LocalPort.ToString(CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal)
            || entry.RemoteAddress.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Protocol.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.State.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Pid.ToString(CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal);
    }

    private void Refresh()
    {
        _entries.Clear();
        var processCache = new Dictionary<int, string>();

        // TCP connections
        foreach (var row in GetTcpConnections())
        {
            _entries.Add(new PortEntry
            {
                Protocol = "TCP",
                LocalAddress = new IPAddress(row.dwLocalAddr).ToString(),
                LocalPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort),
                RemoteAddress = new IPAddress(row.dwRemoteAddr).ToString(),
                RemotePort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwRemotePort),
                State = GetTcpStateName(row.dwState),
                Pid = (int)row.dwOwningPid,
                ProcessName = GetProcessName((int)row.dwOwningPid, processCache),
            });
        }

        // UDP listeners
        foreach (var row in GetUdpListeners())
        {
            _entries.Add(new PortEntry
            {
                Protocol = "UDP",
                LocalAddress = new IPAddress(row.dwLocalAddr).ToString(),
                LocalPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort),
                RemoteAddress = "*",
                RemotePort = 0,
                State = "LISTENING",
                Pid = (int)row.dwOwningPid,
                ProcessName = GetProcessName((int)row.dwOwningPid, processCache),
            });
        }

        // TCP6 connections
        foreach (var row in GetTcp6Connections())
        {
            _entries.Add(new PortEntry
            {
                Protocol = "TCP6",
                LocalAddress = new IPAddress(row.LocalAddr).ToString(),
                LocalPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort),
                RemoteAddress = new IPAddress(row.RemoteAddr).ToString(),
                RemotePort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwRemotePort),
                State = GetTcpStateName(row.dwState),
                Pid = (int)row.dwOwningPid,
                ProcessName = GetProcessName((int)row.dwOwningPid, processCache),
            });
        }

        // UDP6 listeners
        foreach (var row in GetUdp6Listeners())
        {
            _entries.Add(new PortEntry
            {
                Protocol = "UDP6",
                LocalAddress = new IPAddress(row.LocalAddr).ToString(),
                LocalPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort),
                RemoteAddress = "*",
                RemotePort = 0,
                State = "LISTENING",
                Pid = (int)row.dwOwningPid,
                ProcessName = GetProcessName((int)row.dwOwningPid, processCache),
            });
        }

        TxtStatus.Text = string.Format(CultureInfo.InvariantCulture,
            L("ToolOpenPortsStatus"), _entries.Count,
            DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }

    private static string GetProcessName(int pid, Dictionary<int, string> cache)
    {
        if (cache.TryGetValue(pid, out var name)) return name;
        try
        {
            using var proc = Process.GetProcessById(pid);
            name = proc.ProcessName;
        }
        catch { name = pid == 0 ? "System Idle" : $"[{pid}]"; }
        cache[pid] = name;
        return name;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Protocol\tLocal Address\tLocal Port\tRemote Address\tRemote Port\tState\tPID\tProcess");
        foreach (var entry in _entries)
        {
            sb.Append(entry.Protocol).Append('\t')
              .Append(entry.LocalAddress).Append('\t')
              .Append(entry.LocalPort).Append('\t')
              .Append(entry.RemoteAddress).Append('\t')
              .Append(entry.RemotePort).Append('\t')
              .Append(entry.State).Append('\t')
              .Append(entry.Pid).Append('\t')
              .AppendLine(entry.ProcessName);
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
        TxtHelpContent.Text = L("ToolHelpOPENPORTS").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
        => HelpPanel.Visibility = Visibility.Collapsed;

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose() => GC.SuppressFinalize(this);

    // ── P/Invoke ──────────────────────────────────────────────────────

    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int dwOutBufLen, bool sort,
        int ipVersion, int tblClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref int dwOutBufLen, bool sort,
        int ipVersion, int tblClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    private static List<MibTcpRowOwnerPid> GetTcpConnections()
    {
        var result = new List<MibTcpRowOwnerPid>();
        var bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableOwnerPidAll, 0);

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedTcpTable(buffer, ref bufferSize, true, AfInet, TcpTableOwnerPidAll, 0) != 0)
                return result;

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = buffer + 4;
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                result.Add(row);
                rowPtr += rowSize;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }

        return result;
    }

    private static List<MibUdpRowOwnerPid> GetUdpListeners()
    {
        var result = new List<MibUdpRowOwnerPid>();
        var bufferSize = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AfInet, UdpTableOwnerPid, 0);

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedUdpTable(buffer, ref bufferSize, true, AfInet, UdpTableOwnerPid, 0) != 0)
                return result;

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = buffer + 4;
            var rowSize = Marshal.SizeOf<MibUdpRowOwnerPid>();

            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(rowPtr);
                result.Add(row);
                rowPtr += rowSize;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }

        return result;
    }

    // ── IPv6 structs and helpers ────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint dwRemoteScopeId;
        public uint dwRemotePort;
        public uint dwState;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    private static List<MibTcp6RowOwnerPid> GetTcp6Connections()
    {
        var result = new List<MibTcp6RowOwnerPid>();
        var bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet6, TcpTableOwnerPidAll, 0);
        if (bufferSize == 0) return result;

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedTcpTable(buffer, ref bufferSize, true, AfInet6, TcpTableOwnerPidAll, 0) != 0)
                return result;

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = buffer + 4;
            var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();

            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(rowPtr);
                result.Add(row);
                rowPtr += rowSize;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    private static List<MibUdp6RowOwnerPid> GetUdp6Listeners()
    {
        var result = new List<MibUdp6RowOwnerPid>();
        var bufferSize = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AfInet6, UdpTableOwnerPid, 0);
        if (bufferSize == 0) return result;

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedUdpTable(buffer, ref bufferSize, true, AfInet6, UdpTableOwnerPid, 0) != 0)
                return result;

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = buffer + 4;
            var rowSize = Marshal.SizeOf<MibUdp6RowOwnerPid>();

            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibUdp6RowOwnerPid>(rowPtr);
                result.Add(row);
                rowPtr += rowSize;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    private static string GetTcpStateName(uint state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTEN",
        3 => "SYN_SENT",
        4 => "SYN_RCVD",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT1",
        7 => "FIN_WAIT2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "LAST_ACK",
        11 => "TIME_WAIT",
        12 => "DELETE_TCB",
        _ => state.ToString(CultureInfo.InvariantCulture),
    };

    // ── Data model ───────────────────────────────────────────────────

    public sealed class PortEntry
    {
        public required string Protocol { get; init; }
        public required string LocalAddress { get; init; }
        public required int LocalPort { get; init; }
        public required string RemoteAddress { get; init; }
        public required int RemotePort { get; init; }
        public required string State { get; init; }
        public required int Pid { get; init; }
        public required string ProcessName { get; init; }
    }
}
