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
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Heimdall.Core.Security;

/// <summary>
/// Lightweight network scanner that performs ICMP ping sweeps and common port probes
/// to discover hosts on a subnet. Designed for sysadmin network discovery workflows.
/// </summary>
public static class NetworkScanner
{
    private static readonly int PingTimeoutMs = 1000;
    private static readonly int PortProbeTimeoutMs = 500;
    private static readonly int[] CommonPorts = [22, 3389, 80, 443, 5900];

    /// <summary>
    /// Result of scanning a single host.
    /// </summary>
    public record ScanResult(
        string IpAddress,
        bool IsAlive,
        long RoundtripMs,
        string? Hostname,
        List<int> OpenPorts);

    /// <summary>
    /// Scans a CIDR subnet (e.g., "192.168.1.0/24") and returns discovered hosts.
    /// Uses parallel ICMP pings followed by port probes on responsive hosts.
    /// </summary>
    /// <param name="cidr">CIDR notation subnet (e.g., "192.168.1.0/24").</param>
    /// <param name="progress">Optional progress callback: (completed, total).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of scan results for responsive hosts.</returns>
    public static async Task<List<ScanResult>> ScanSubnetAsync(
        string cidr,
        Action<int, int>? progress = null,
        CancellationToken ct = default)
    {
        var (network, prefixLength) = ParseCidr(cidr);
        var addresses = GenerateAddresses(network, prefixLength);
        var results = new List<ScanResult>();
        var completed = 0;
        var total = addresses.Count;

        // Parallel ping sweep (max 64 concurrent)
        var semaphore = new SemaphoreSlim(64);
        var tasks = addresses.Select(async ip =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await PingHostAsync(ip, ct);
                Interlocked.Increment(ref completed);
                progress?.Invoke(completed, total);
                return result;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var pingResults = await Task.WhenAll(tasks);

        // Port probe on alive hosts
        foreach (var result in pingResults.Where(r => r is { IsAlive: true }))
        {
            ct.ThrowIfCancellationRequested();
            var openPorts = await ProbePortsAsync(result!.IpAddress, ct);
            results.Add(result with { OpenPorts = openPorts });
        }

        return results.OrderBy(r => IPAddress.Parse(r.IpAddress).GetAddressBytes(),
            new IpComparer()).ToList();
    }

    private static async Task<ScanResult?> PingHostAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, PingTimeoutMs);

            if (reply.Status != IPStatus.Success)
                return null;

            string? hostname = null;
            try
            {
                var entry = await Dns.GetHostEntryAsync(ip, ct);
                hostname = entry.HostName;
            }
            catch { /* DNS resolution is best-effort */ }

            return new ScanResult(ip, true, reply.RoundtripTime, hostname, []);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<int>> ProbePortsAsync(string ip, CancellationToken ct)
    {
        var open = new List<int>();

        foreach (var port in CommonPorts)
        {
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(PortProbeTimeoutMs);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

                await client.ConnectAsync(ip, port, linked.Token);
                open.Add(port);
            }
            catch
            {
                // Port closed or filtered
            }
        }

        return open;
    }

    private static (string Network, int PrefixLength) ParseCidr(string cidr)
    {
        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix) || prefix < 16 || prefix > 30)
            throw new ArgumentException($"Invalid CIDR notation: {cidr}. Use format like 192.168.1.0/24 (prefix 16-30).");

        return (parts[0], prefix);
    }

    private static List<string> GenerateAddresses(string network, int prefixLength)
    {
        var ip = IPAddress.Parse(network);
        var bytes = ip.GetAddressBytes();
        var networkInt = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);

        var hostBits = 32 - prefixLength;
        var hostCount = (1u << hostBits) - 2; // exclude network and broadcast
        var addresses = new List<string>((int)hostCount);

        for (uint i = 1; i <= hostCount; i++)
        {
            var hostIp = networkInt + i;
            addresses.Add($"{(hostIp >> 24) & 0xFF}.{(hostIp >> 16) & 0xFF}.{(hostIp >> 8) & 0xFF}.{hostIp & 0xFF}");
        }

        return addresses;
    }

    private sealed class IpComparer : IComparer<byte[]>
    {
        public int Compare(byte[]? x, byte[]? y)
        {
            if (x is null || y is null) return 0;
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                var cmp = x[i].CompareTo(y[i]);
                if (cmp != 0) return cmp;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
