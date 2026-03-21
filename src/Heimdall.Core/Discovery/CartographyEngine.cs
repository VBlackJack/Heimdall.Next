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
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Heimdall.Core.Discovery;

/// <summary>
/// Orchestrates network cartography scans: ping sweep, port scan, banner grab,
/// reverse DNS, and heuristic role classification.
/// </summary>
public sealed class CartographyEngine
{
    /// <summary>Ports that typically serve TLS-encrypted traffic.</summary>
    private static readonly HashSet<int> TlsPorts = [443, 8443, 636, 993, 995, 465, 990, 3269, 9443];

    /// <summary>Top 20 ports for quick reconnaissance.</summary>
    public static readonly int[] QuickPorts =
        [22, 80, 443, 3389, 8080, 53, 25, 3306, 5432, 445, 139, 88, 389, 161, 21, 5900, 8443, 1433, 27017, 6379];

    /// <summary>~50 ports covering the most common enterprise services.</summary>
    public static readonly int[] StandardPorts =
        [21, 22, 23, 25, 53, 67, 80, 88, 110, 111, 135, 139, 143, 161, 162, 389, 443, 445, 464, 465,
         514, 554, 587, 631, 636, 993, 995, 1433, 1434, 1521, 1883, 1900, 2049, 2375, 2376, 3000,
         3128, 3260, 3268, 3306, 3389, 5000, 5060, 5432, 5665, 5900, 5901, 5985, 6379, 6443, 6514,
         8006, 8080, 8123, 8291, 8443, 8899, 9090, 9100, 9200, 9300, 10050, 10250, 27017, 33060, 62078];

    /// <summary>Fires during the ICMP ping sweep phase.</summary>
    public event Action<int, int>? HostDiscoveryProgress;

    /// <summary>Fires during port scanning of an individual host.</summary>
    public event Action<string, int, int>? PortScanProgress;

    /// <summary>Fires when a single host has been fully scanned.</summary>
    public event Action<HostScanResult>? HostCompleted;

    /// <summary>
    /// Runs a full cartography scan against the specified profile.
    /// </summary>
    public async Task<NetworkScanSnapshot> ScanAsync(
        ScanProfile profile,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var hosts = new List<HostScanResult>();

        var ipList = ParseCidr(profile.Subnet);

        List<string> aliveHosts;
        if (profile.SkipPing)
        {
            aliveHosts = ipList;
        }
        else
        {
            aliveHosts = await PingSweepAsync(ipList, profile, ct).ConfigureAwait(false);
        }

        var ports = profile.CustomPorts ?? profile.Depth switch
        {
            ScanDepth.Quick => QuickPorts,
            ScanDepth.Standard => StandardPorts,
            _ => StandardPorts
        };

        var semaphore = new SemaphoreSlim(profile.MaxConcurrency);
        var tasks = aliveHosts.Select(async ip =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var result = await ScanHostAsync(ip, ports, profile, ct).ConfigureAwait(false);
                lock (hosts) hosts.Add(result);
                HostCompleted?.Invoke(result);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var orderedHosts = hosts.OrderBy(h => IpToLong(h.IpAddress)).ToList();

        // Passive VLAN detection from discovered host IPs
        var detectedVlans = VlanDetector.InferFromHosts(orderedHosts);

        return new NetworkScanSnapshot(
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            profile,
            null,
            DateTime.UtcNow - startTime,
            orderedHosts,
            detectedVlans);
    }

    private async Task<List<string>> PingSweepAsync(
        List<string> ips, ScanProfile profile, CancellationToken ct)
    {
        var alive = new List<string>();
        var completed = 0;
        var semaphore = new SemaphoreSlim(64);

        var tasks = ips.Select(async ip =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, Math.Min(profile.TimeoutMs, 1000))
                    .ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    lock (alive) alive.Add(ip);
                }
                Interlocked.Increment(ref completed);
                HostDiscoveryProgress?.Invoke(completed, ips.Count);
            }
            catch
            {
                Interlocked.Increment(ref completed);
                HostDiscoveryProgress?.Invoke(completed, ips.Count);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return alive;
    }

    private async Task<HostScanResult> ScanHostAsync(
        string ip, int[] ports, ScanProfile profile, CancellationToken ct)
    {
        var services = new List<ServiceResult>();
        var portCompleted = 0;

        var hostSemaphore = new SemaphoreSlim(Math.Min(20, profile.MaxConcurrency));
        var portTasks = ports.Select(async port =>
        {
            await hostSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var result = await ProbePortAsync(ip, port, profile.TimeoutMs, ct)
                    .ConfigureAwait(false);
                if (result.IsOpen)
                {
                    lock (services) services.Add(result);
                }
                Interlocked.Increment(ref portCompleted);
                PortScanProgress?.Invoke(ip, portCompleted, ports.Length);
            }
            finally { hostSemaphore.Release(); }
        });

        await Task.WhenAll(portTasks).ConfigureAwait(false);

        string? hostname = null;
        if (profile.ReverseDns)
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(ip, ct).ConfigureAwait(false);
                hostname = entry.HostName;
            }
            catch { /* DNS reverse failed */ }
        }

        var openServices = services.Where(s => s.IsOpen).ToList();
        var openPorts = openServices.Select(s => s.Port).ToList();
        var banners = openServices.Select(s => s.Banner).ToList();
        var roles = RoleClassifier.ClassifyWithBanners(openPorts, banners);
        var primaryRole = roles.Count > 0 ? roles[0] : null;

        return new HostScanResult(ip, hostname, true, 0, services, primaryRole, roles);
    }

    private static async Task<ServiceResult> ProbePortAsync(
        string host, int port, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(timeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            sw.Stop();

            string? banner = null;
            try
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 1000;
                var buf = new byte[512];

                if (stream.DataAvailable || await WaitForDataAsync(stream, 800, ct).ConfigureAwait(false))
                {
                    var read = await stream.ReadAsync(buf, linked.Token).ConfigureAwait(false);
                    if (read > 0)
                        banner = Encoding.ASCII.GetString(buf, 0, read).Trim();
                }

                if (banner is null && IsLikelyHttpPort(port))
                {
                    var probe = Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\nHost: " + host + "\r\n\r\n");
                    await stream.WriteAsync(probe, linked.Token).ConfigureAwait(false);
                    await stream.FlushAsync(linked.Token).ConfigureAwait(false);

                    if (await WaitForDataAsync(stream, 1000, ct).ConfigureAwait(false))
                    {
                        var read = await stream.ReadAsync(buf, linked.Token).ConfigureAwait(false);
                        if (read > 0)
                            banner = Encoding.ASCII.GetString(buf, 0, read).Trim();
                    }
                }
            }
            catch { /* banner grab failed, port is still open */ }

            var (serviceName, version) = ParseBanner(banner, port);

            CertificateInfo? certInfo = null;
            if (TlsPorts.Contains(port))
            {
                certInfo = await InspectTlsAsync(host, port, ct).ConfigureAwait(false);
            }

            return new ServiceResult(port, true, serviceName, banner, version, sw.ElapsedMilliseconds, certInfo);
        }
        catch
        {
            return new ServiceResult(port, false, null, null, null, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<CertificateInfo?> InspectTlsAsync(
        string host, int port, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(3000);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
            await tcp.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);

            using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host
            }, linked.Token).ConfigureAwait(false);

            var remoteCert = ssl.RemoteCertificate;
            if (remoteCert is null) return null;

            using var x509 = new X509Certificate2(remoteCert);

            var sans = new List<string>();
            foreach (var ext in x509.Extensions)
            {
                if (ext.Oid?.Value == "2.5.29.17")
                {
                    var sanStr = ext.Format(false);
                    foreach (var part in sanStr.Split(','))
                    {
                        var trimmed = part.Trim();
                        if (trimmed.StartsWith("DNS Name=", StringComparison.OrdinalIgnoreCase))
                            sans.Add(trimmed["DNS Name=".Length..]);
                        else if (trimmed.StartsWith("IP Address=", StringComparison.OrdinalIgnoreCase))
                            sans.Add(trimmed["IP Address=".Length..]);
                    }
                }
            }

            var keyAlg = x509.PublicKey.Oid.FriendlyName ?? "Unknown";
            var keySize = 0;
            try
            {
                keySize = x509.PublicKey.GetRSAPublicKey()?.KeySize
                    ?? x509.PublicKey.GetECDsaPublicKey()?.KeySize
                    ?? 0;
            }
            catch { /* key type may not support size extraction */ }

            return new CertificateInfo(
                x509.Subject,
                x509.Issuer,
                x509.NotBefore,
                x509.NotAfter,
                x509.NotAfter < DateTime.UtcNow,
                x509.NotAfter < DateTime.UtcNow.AddDays(30) && x509.NotAfter >= DateTime.UtcNow,
                keySize > 0 ? $"{keyAlg} {keySize}" : keyAlg,
                x509.SignatureAlgorithm.FriendlyName ?? "",
                [.. sans],
                ssl.SslProtocol.ToString(),
                x509.GetCertHashString(HashAlgorithmName.SHA256));
        }
        catch { return null; }
    }

    private static (string? service, string? version) ParseBanner(string? banner, int port)
    {
        if (string.IsNullOrWhiteSpace(banner))
        {
            var fallback = RoleClassifier.GetPortServiceName(port);
            return (fallback != $"Port-{port}" ? fallback : null, null);
        }

        if (banner.StartsWith("SSH-", StringComparison.Ordinal))
        {
            var parts = banner.Split('-', 3);
            return ("SSH", parts.Length >= 3 ? parts[2].Split(' ')[0] : null);
        }

        if (banner.StartsWith("220", StringComparison.Ordinal))
            return ("FTP", banner.Length > 4 ? banner[4..].Trim() : null);

        if (banner.Contains("SMTP", StringComparison.OrdinalIgnoreCase) ||
            banner.Contains("Postfix", StringComparison.OrdinalIgnoreCase) ||
            banner.Contains("Sendmail", StringComparison.OrdinalIgnoreCase))
            return ("SMTP", banner);

        if (banner.StartsWith("HTTP/", StringComparison.Ordinal))
        {
            var serverMatch = Regex.Match(banner, @"Server:\s*(.+)", RegexOptions.IgnoreCase);
            return ("HTTP", serverMatch.Success ? serverMatch.Groups[1].Value.Trim() : null);
        }

        if (banner.Contains("mysql", StringComparison.OrdinalIgnoreCase) ||
            (port == 3306 && banner.Length > 5))
            return ("MySQL", banner);

        if (banner.StartsWith('+') || banner.StartsWith("-ERR", StringComparison.Ordinal))
            return ("Redis", banner);

        if (banner.StartsWith("RFB ", StringComparison.Ordinal))
            return ("VNC", banner[4..]);

        return (null, banner);
    }

    private static bool IsLikelyHttpPort(int port) =>
        port is 80 or 443 or 8080 or 8443 or 9090 or 3000 or 8000 or 8888;

    private static async Task<bool> WaitForDataAsync(
        NetworkStream stream, int maxWaitMs, CancellationToken ct)
    {
        var waited = 0;
        while (waited < maxWaitMs && !ct.IsCancellationRequested)
        {
            if (stream.DataAvailable) return true;
            await Task.Delay(50, ct).ConfigureAwait(false);
            waited += 50;
        }
        return stream.DataAvailable;
    }

    /// <summary>
    /// Generates the list of host IPs from a CIDR notation string.
    /// Safety limit: refuses to scan larger than /16.
    /// </summary>
    public static List<string> ParseCidr(string cidr)
    {
        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var ip) ||
            !int.TryParse(parts[1], out var prefix))
            return [];
        if (prefix < 16 || prefix > 30) return [];

        var ipBytes = ip.GetAddressBytes();
        var ipUint = (uint)(ipBytes[0] << 24 | ipBytes[1] << 16 | ipBytes[2] << 8 | ipBytes[3]);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var network = ipUint & mask;
        var broadcast = network | ~mask;

        var result = new List<string>();
        for (var addr = network + 1; addr < broadcast; addr++)
        {
            result.Add($"{(addr >> 24) & 0xFF}.{(addr >> 16) & 0xFF}.{(addr >> 8) & 0xFF}.{addr & 0xFF}");
        }
        return result;
    }

    public static long IpToLong(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4) return 0;
        return long.Parse(parts[0]) << 24 | long.Parse(parts[1]) << 16 |
               long.Parse(parts[2]) << 8 | long.Parse(parts[3]);
    }
}
