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

using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Heimdall.Core.Network;

namespace Heimdall.App.Services;

/// <summary>
/// Synchronous loader for open TCP/UDP ports backed by Win32 IP helper APIs.
/// </summary>
public interface IOpenPortsService
{
    IReadOnlyList<PortEntry> Load();
}

public sealed class OpenPortsService : IOpenPortsService
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;

    private readonly Func<IReadOnlyList<Tcp4RawRow>> _loadTcp4;
    private readonly Func<IReadOnlyList<Udp4RawRow>> _loadUdp4;
    private readonly Func<IReadOnlyList<Tcp6RawRow>> _loadTcp6;
    private readonly Func<IReadOnlyList<Udp6RawRow>> _loadUdp6;
    private readonly Func<int, string> _resolveProcessName;

    public OpenPortsService()
        : this(LoadTcp4Rows, LoadUdp4Rows, LoadTcp6Rows, LoadUdp6Rows, ResolveProcessName)
    {
    }

    internal OpenPortsService(
        Func<IReadOnlyList<Tcp4RawRow>> loadTcp4,
        Func<IReadOnlyList<Udp4RawRow>> loadUdp4,
        Func<IReadOnlyList<Tcp6RawRow>> loadTcp6,
        Func<IReadOnlyList<Udp6RawRow>> loadUdp6,
        Func<int, string> resolveProcessName)
    {
        ArgumentNullException.ThrowIfNull(loadTcp4);
        ArgumentNullException.ThrowIfNull(loadUdp4);
        ArgumentNullException.ThrowIfNull(loadTcp6);
        ArgumentNullException.ThrowIfNull(loadUdp6);
        ArgumentNullException.ThrowIfNull(resolveProcessName);

        _loadTcp4 = loadTcp4;
        _loadUdp4 = loadUdp4;
        _loadTcp6 = loadTcp6;
        _loadUdp6 = loadUdp6;
        _resolveProcessName = resolveProcessName;
    }

    public IReadOnlyList<PortEntry> Load()
    {
        var entries = new List<PortEntry>();
        var cache = new Dictionary<int, string>();
        string ResolveCached(int pid)
        {
            if (cache.TryGetValue(pid, out var name))
            {
                return name;
            }

            name = _resolveProcessName(pid);
            cache[pid] = name;
            return name;
        }

        TryAdd(entries, _loadTcp4, row => PortMapper.FromTcp4Row(row, ResolveCached));
        TryAdd(entries, _loadUdp4, row => PortMapper.FromUdp4Row(row, ResolveCached));
        TryAdd(entries, _loadTcp6, row => PortMapper.FromTcp6Row(row, ResolveCached));
        TryAdd(entries, _loadUdp6, row => PortMapper.FromUdp6Row(row, ResolveCached));
        return entries;
    }

    private static void TryAdd<TRow>(
        List<PortEntry> entries,
        Func<IReadOnlyList<TRow>> loader,
        Func<TRow, PortEntry> map)
    {
        try
        {
            foreach (var row in loader())
            {
                entries.Add(map(row));
            }
        }
        catch
        {
            // Preserve current behavior: one failing source should not crash the view.
        }
    }

    private static string ResolveProcessName(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName;
        }
        catch
        {
            return pid == 0 ? "System Idle" : $"[{pid}]";
        }
    }

    private static IReadOnlyList<Tcp4RawRow> LoadTcp4Rows()
    {
        var result = new List<Tcp4RawRow>();
        var bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableOwnerPidAll, 0);
        if (bufferSize == 0)
        {
            return result;
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedTcpTable(buffer, ref bufferSize, true, AfInet, TcpTableOwnerPidAll, 0) != 0)
            {
                return result;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = buffer + 4;
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                result.Add(new Tcp4RawRow(row.dwLocalAddr, row.dwLocalPort, row.dwRemoteAddr, row.dwRemotePort, row.dwState, row.dwOwningPid));
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    private static IReadOnlyList<Udp4RawRow> LoadUdp4Rows()
    {
        var result = new List<Udp4RawRow>();
        var bufferSize = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AfInet, UdpTableOwnerPid, 0);
        if (bufferSize == 0)
        {
            return result;
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedUdpTable(buffer, ref bufferSize, true, AfInet, UdpTableOwnerPid, 0) != 0)
            {
                return result;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = buffer + 4;
            var rowSize = Marshal.SizeOf<MibUdpRowOwnerPid>();
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(rowPtr);
                result.Add(new Udp4RawRow(row.dwLocalAddr, row.dwLocalPort, row.dwOwningPid));
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    private static IReadOnlyList<Tcp6RawRow> LoadTcp6Rows()
    {
        var result = new List<Tcp6RawRow>();
        var bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet6, TcpTableOwnerPidAll, 0);
        if (bufferSize == 0)
        {
            return result;
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedTcpTable(buffer, ref bufferSize, true, AfInet6, TcpTableOwnerPidAll, 0) != 0)
            {
                return result;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = buffer + 4;
            var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(rowPtr);
                result.Add(new Tcp6RawRow((byte[])row.LocalAddr.Clone(), row.dwLocalPort, (byte[])row.RemoteAddr.Clone(), row.dwRemotePort, row.dwState, row.dwOwningPid));
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    private static IReadOnlyList<Udp6RawRow> LoadUdp6Rows()
    {
        var result = new List<Udp6RawRow>();
        var bufferSize = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AfInet6, UdpTableOwnerPid, 0);
        if (bufferSize == 0)
        {
            return result;
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedUdpTable(buffer, ref bufferSize, true, AfInet6, UdpTableOwnerPid, 0) != 0)
            {
                return result;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = buffer + 4;
            var rowSize = Marshal.SizeOf<MibUdp6RowOwnerPid>();
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibUdp6RowOwnerPid>(rowPtr);
                result.Add(new Udp6RawRow((byte[])row.LocalAddr.Clone(), row.dwLocalPort, row.dwOwningPid));
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        int tblClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        int tblClass,
        uint reserved);

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
}
