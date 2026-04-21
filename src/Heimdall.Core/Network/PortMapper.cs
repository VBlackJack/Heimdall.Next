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

namespace Heimdall.Core.Network;

/// <summary>
/// Pure mapping helpers from raw Win32 rows to UI-friendly entries.
/// </summary>
public static class PortMapper
{
    public static PortEntry FromTcp4Row(Tcp4RawRow row, Func<int, string> resolveProcessName)
    {
        ArgumentNullException.ThrowIfNull(resolveProcessName);

        var pid = unchecked((int)row.OwningPid);
        return new PortEntry(
            "TCP",
            new IPAddress(row.LocalAddr).ToString(),
            ToPort(row.LocalPort),
            new IPAddress(row.RemoteAddr).ToString(),
            ToPort(row.RemotePort),
            TcpConnectionStateTable.NameOf(row.State),
            pid,
            resolveProcessName(pid));
    }

    public static PortEntry FromUdp4Row(Udp4RawRow row, Func<int, string> resolveProcessName)
    {
        ArgumentNullException.ThrowIfNull(resolveProcessName);

        var pid = unchecked((int)row.OwningPid);
        return new PortEntry(
            "UDP",
            new IPAddress(row.LocalAddr).ToString(),
            ToPort(row.LocalPort),
            "*",
            0,
            "LISTENING",
            pid,
            resolveProcessName(pid));
    }

    public static PortEntry FromTcp6Row(Tcp6RawRow row, Func<int, string> resolveProcessName)
    {
        ArgumentNullException.ThrowIfNull(resolveProcessName);
        ArgumentNullException.ThrowIfNull(row);

        var pid = unchecked((int)row.OwningPid);
        return new PortEntry(
            "TCP6",
            new IPAddress(row.LocalAddr).ToString(),
            ToPort(row.LocalPort),
            new IPAddress(row.RemoteAddr).ToString(),
            ToPort(row.RemotePort),
            TcpConnectionStateTable.NameOf(row.State),
            pid,
            resolveProcessName(pid));
    }

    public static PortEntry FromUdp6Row(Udp6RawRow row, Func<int, string> resolveProcessName)
    {
        ArgumentNullException.ThrowIfNull(resolveProcessName);
        ArgumentNullException.ThrowIfNull(row);

        var pid = unchecked((int)row.OwningPid);
        return new PortEntry(
            "UDP6",
            new IPAddress(row.LocalAddr).ToString(),
            ToPort(row.LocalPort),
            "*",
            0,
            "LISTENING",
            pid,
            resolveProcessName(pid));
    }

    private static int ToPort(uint port) => (ushort)IPAddress.NetworkToHostOrder((short)port);
}
