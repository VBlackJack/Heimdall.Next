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

namespace Heimdall.Core.Discovery;

/// <summary>
/// Detects VLANs and network segments from scan results.
/// Passive mode infers segments from IP address ranges.
/// Active mode queries network equipment via SSH if available.
/// </summary>
public static class VlanDetector
{
    /// <summary>
    /// Infer VLAN/subnet segments from discovered hosts by grouping IPs
    /// using the scanned CIDR prefix length. Falls back to /24 if no
    /// prefix is provided.
    /// </summary>
    public static List<VlanInfo> InferFromHosts(
        IReadOnlyList<HostScanResult> hosts, string? scannedCidr = null)
    {
        var prefixLen = 24;
        if (scannedCidr is not null)
        {
            var slashIdx = scannedCidr.IndexOf('/');
            if (slashIdx >= 0 && int.TryParse(scannedCidr[(slashIdx + 1)..], out var p) && p >= 16 && p <= 30)
                prefixLen = p;
        }

        var segments = new Dictionary<string, List<string>>();

        foreach (var host in hosts.Where(h => h.IsAlive))
        {
            if (!IPAddress.TryParse(host.IpAddress, out var ip)) continue;
            var bytes = ip.GetAddressBytes();
            if (bytes.Length != 4) continue;

            var subnetKey = ComputeSubnetKey(bytes, prefixLen);

            if (!segments.TryGetValue(subnetKey, out var members))
            {
                members = [];
                segments[subnetKey] = members;
            }
            members.Add(host.IpAddress);
        }

        var vlans = new List<VlanInfo>();
        var vlanId = 1;

        foreach (var (subnet, members) in segments.OrderBy(s => s.Key))
        {
            // Try to detect gateway (first or last usable address)
            string? gateway = null;
            foreach (var member in members)
            {
                if (!IPAddress.TryParse(member, out var mip)) continue;
                var lastOctet = mip.GetAddressBytes()[3];
                if (lastOctet is 1 or 254)
                {
                    gateway = member;
                    break;
                }
            }

            var segmentName = subnet.Replace('/', '-').Replace('.', '-');
            vlans.Add(new VlanInfo(
                VlanId: vlanId++,
                Name: $"Segment-{segmentName}",
                Subnet: subnet,
                Gateway: gateway,
                MemberIps: members.OrderBy(IpSortKey).ToList()));
        }

        return vlans;
    }

    /// <summary>
    /// Computes the network address string for a given IP and prefix length.
    /// </summary>
    private static string ComputeSubnetKey(byte[] ipBytes, int prefixLen)
    {
        var ipUint = (uint)(ipBytes[0] << 24 | ipBytes[1] << 16 | ipBytes[2] << 8 | ipBytes[3]);
        var mask = prefixLen >= 32 ? uint.MaxValue : prefixLen == 0 ? 0u : uint.MaxValue << (32 - prefixLen);
        var network = ipUint & mask;
        return $"{(network >> 24) & 0xFF}.{(network >> 16) & 0xFF}.{(network >> 8) & 0xFF}.{network & 0xFF}/{prefixLen}";
    }

    /// <summary>
    /// Parse the output of "show vlan brief" from a Cisco IOS/NX-OS switch.
    /// Expected format:
    /// <code>
    /// VLAN Name                             Status    Ports
    /// ---- -------------------------------- --------- ---------------------------
    /// 1    default                          active    Gi0/1, Gi0/2
    /// 10   SERVERS                          active    Gi0/3, Gi0/4
    /// 20   MANAGEMENT                       active    Gi0/5
    /// </code>
    /// </summary>
    public static List<VlanInfo> ParseShowVlanBrief(string output)
    {
        var vlans = new List<VlanInfo>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith("VLAN", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("----")) continue;

            // Parse: VLAN_ID  NAME  STATUS  PORTS
            var parts = trimmed.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0], out var vlanId)) continue;

            var name = parts[1];
            var status = parts.Length > 2 ? parts[2] : "";

            if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "act/unsup", StringComparison.OrdinalIgnoreCase))
                continue;

            vlans.Add(new VlanInfo(
                VlanId: vlanId,
                Name: name,
                Subnet: "",
                Gateway: null,
                MemberIps: []));
        }

        return vlans;
    }

    /// <summary>
    /// Parse the output of "show ip interface brief" or "show interface vlan"
    /// to map VLAN IDs to subnet addresses.
    /// </summary>
    public static void EnrichVlansWithSubnets(List<VlanInfo> vlans, string interfaceOutput)
    {
        // Look for lines like: "Vlan10  10.0.10.1  YES manual up  up"
        foreach (var line in interfaceOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Vlan", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = trimmed.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            // Extract VLAN ID from "Vlan10"
            var vlanStr = parts[0];
            if (vlanStr.Length <= 4) continue;
            if (!int.TryParse(vlanStr[4..], out var vlanId)) continue;

            // Extract IP
            if (!IPAddress.TryParse(parts[1], out _)) continue;
            var ip = parts[1];

            var vlan = vlans.FirstOrDefault(v => v.VlanId == vlanId);
            if (vlan is not null)
            {
                // Infer /24 subnet from interface IP
                var subnet = ip[..(ip.LastIndexOf('.'))] + ".0/24";
                var idx = vlans.IndexOf(vlan);
                vlans[idx] = vlan with { Subnet = subnet, Gateway = ip };
            }
        }
    }

    private static long IpSortKey(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return 0;
        var bytes = addr.GetAddressBytes();
        return (long)bytes[0] << 24 | (long)bytes[1] << 16 | (long)bytes[2] << 8 | bytes[3];
    }
}
