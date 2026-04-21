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

using System.Net.NetworkInformation;
using System.Net.Sockets;
using Heimdall.Core.Network;

namespace Heimdall.App.Services;

/// <summary>
/// Synchronous loader for local network interface snapshots.
/// </summary>
public interface INetworkInterfacesService
{
    IReadOnlyList<NicSnapshot> Load();
}

public sealed class NetworkInterfacesService : INetworkInterfacesService
{
    private readonly Func<IReadOnlyList<NicSnapshot>> _load;

    public NetworkInterfacesService()
        : this(DefaultLoad)
    {
    }

    internal NetworkInterfacesService(Func<IReadOnlyList<NicSnapshot>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
    }

    public IReadOnlyList<NicSnapshot> Load() => _load() ?? [];

    private static IReadOnlyList<NicSnapshot> DefaultLoad()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Select(ToSnapshot)
            .ToList();
    }

    private static NicSnapshot ToSnapshot(NetworkInterface nic)
    {
        var props = nic.GetIPProperties();

        var ipv4 = props.UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

        var gateway = props.GatewayAddresses
            .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

        var dhcp = props.DhcpServerAddresses.Count > 0;

        return new NicSnapshot(
            nic.Name,
            nic.NetworkInterfaceType.ToString(),
            nic.OperationalStatus.ToString(),
            NicFormatter.FormatSpeed(nic.Speed),
            NicFormatter.FormatMac(nic.GetPhysicalAddress()),
            ipv4?.Address.ToString() ?? string.Empty,
            ipv4?.IPv4Mask?.ToString() ?? string.Empty,
            gateway?.Address.ToString() ?? string.Empty,
            NicFormatter.FormatDhcp(dhcp));
    }
}
