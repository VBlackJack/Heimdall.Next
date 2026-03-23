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

using System.Text.RegularExpressions;

namespace Heimdall.Core.Discovery;

/// <summary>
/// Infers the operating system of a remote host from ICMP TTL values
/// and service banner strings.
/// </summary>
public static class OsFingerprinter
{
    private static readonly (int Low, int High, string Os, int Confidence)[] TtlRanges =
    [
        (113, 128, "Windows", 70),
        (49, 64, "Linux/macOS", 60),
        (241, 255, "Network Equipment", 65),
        (17, 32, "Embedded/Legacy", 50),
    ];

    private static readonly (string Pattern, string Os, int Confidence, RegexOptions Options)[] BannerPatterns =
    [
        (@"SSH-.*Ubuntu", "Ubuntu Linux", 85, RegexOptions.IgnoreCase),
        (@"SSH-.*Debian", "Debian Linux", 85, RegexOptions.IgnoreCase),
        (@"SSH-.*FreeBSD", "FreeBSD", 85, RegexOptions.IgnoreCase),
        (@"SSH-.*CentOS", "CentOS Linux", 85, RegexOptions.IgnoreCase),
        (@"SSH-.*Red\s?Hat", "Red Hat Linux", 85, RegexOptions.IgnoreCase),
        (@"SSH-.*Fedora", "Fedora Linux", 80, RegexOptions.IgnoreCase),
        (@"SSH-.*SUSE", "SUSE Linux", 80, RegexOptions.IgnoreCase),
        (@"SSH-.*Arch", "Arch Linux", 80, RegexOptions.IgnoreCase),
        (@"SSH-.*Raspbian", "Raspbian (Raspberry Pi)", 85, RegexOptions.IgnoreCase),
        (@"SSH-.*OpenWrt", "OpenWrt", 90, RegexOptions.IgnoreCase),
        (@"SSH-.*Cisco", "Cisco IOS", 90, RegexOptions.IgnoreCase),
        (@"SSH-.*dropbear", "Linux (Embedded)", 70, RegexOptions.IgnoreCase),
        (@"SSH-.*libssh", "Linux", 55, RegexOptions.IgnoreCase),
        (@"Microsoft-IIS", "Windows Server", 80, RegexOptions.IgnoreCase),
        (@"Microsoft-HTTPAPI", "Windows", 75, RegexOptions.IgnoreCase),
        (@"Server:\s*Apache.*Ubuntu", "Ubuntu Linux", 80, RegexOptions.IgnoreCase),
        (@"Server:\s*Apache.*Debian", "Debian Linux", 80, RegexOptions.IgnoreCase),
        (@"Server:\s*Apache.*CentOS", "CentOS Linux", 80, RegexOptions.IgnoreCase),
        (@"Server:\s*Apache.*Red\s?Hat", "Red Hat Linux", 80, RegexOptions.IgnoreCase),
        (@"Server:\s*Apache.*Fedora", "Fedora Linux", 75, RegexOptions.IgnoreCase),
        (@"Server:\s*Apache.*Win", "Windows", 70, RegexOptions.IgnoreCase),
        (@"Server:\s*nginx", "Linux", 50, RegexOptions.IgnoreCase),
        (@"RouterOS", "MikroTik RouterOS", 90, RegexOptions.IgnoreCase),
        (@"FortiOS", "Fortinet FortiOS", 90, RegexOptions.IgnoreCase),
        (@"pfSense", "FreeBSD (pfSense)", 90, RegexOptions.IgnoreCase),
        (@"OPNsense", "FreeBSD (OPNsense)", 90, RegexOptions.IgnoreCase),
        (@"DD-WRT", "Linux (DD-WRT)", 90, RegexOptions.IgnoreCase),
        (@"Synology", "Linux (Synology DSM)", 85, RegexOptions.IgnoreCase),
        (@"QNAP", "Linux (QNAP QTS)", 85, RegexOptions.IgnoreCase),
        (@"FreeNAS|TrueNAS", "FreeBSD (TrueNAS)", 85, RegexOptions.IgnoreCase),
        (@"ESXi", "VMware ESXi", 90, RegexOptions.IgnoreCase),
        (@"Proxmox", "Debian Linux (Proxmox)", 90, RegexOptions.IgnoreCase),
        (@"X-AspNet-Version", "Windows Server (ASP.NET)", 85, RegexOptions.IgnoreCase),
    ];

    /// <summary>
    /// Guesses the operating system from the ICMP ping TTL value.
    /// </summary>
    public static OsFingerprint? GuessFromTtl(int ttl)
    {
        if (ttl <= 0) return null;

        foreach (var (low, high, os, confidence) in TtlRanges)
        {
            if (ttl >= low && ttl <= high)
                return new OsFingerprint(os, "TTL", confidence);
        }

        return null;
    }

    /// <summary>
    /// Guesses the operating system from service banners (SSH, HTTP, etc.).
    /// Returns the highest-confidence match found.
    /// </summary>
    public static OsFingerprint? GuessFromBanners(IReadOnlyList<ServiceResult> services)
    {
        OsFingerprint? best = null;

        foreach (var svc in services)
        {
            if (string.IsNullOrEmpty(svc.Banner)) continue;

            foreach (var (pattern, os, confidence, options) in BannerPatterns)
            {
                if (Regex.IsMatch(svc.Banner, pattern, options))
                {
                    if (best is null || confidence > best.Confidence)
                        best = new OsFingerprint(os, "Banner", confidence);
                }
            }

            // Check HTTP headers if available
            if (svc.HttpHeaders is null) continue;

            foreach (var (_, value) in svc.HttpHeaders)
            {
                foreach (var (pattern, os, confidence, options) in BannerPatterns)
                {
                    if (Regex.IsMatch(value, pattern, options))
                    {
                        if (best is null || confidence > best.Confidence)
                            best = new OsFingerprint(os, "HTTP Header", confidence);
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Guesses the operating system from open port patterns.
    /// RDP, WinRM → Windows; SSH-only → Linux (low confidence).
    /// </summary>
    public static OsFingerprint? GuessFromPorts(IReadOnlyList<int> openPorts)
    {
        if (openPorts.Count == 0) return null;

        var portSet = new HashSet<int>(openPorts);

        // Kerberos + LDAP → Windows Active Directory
        if (portSet.Contains(88) && portSet.Contains(389))
            return new OsFingerprint("Windows Server", "Ports", 70);

        // WinRM (HTTP or HTTPS) → Windows
        if (portSet.Contains(5985) || portSet.Contains(5986))
            return new OsFingerprint("Windows", "Ports", 65);

        // RDP → Windows
        if (portSet.Contains(3389))
            return new OsFingerprint("Windows", "Ports", 60);

        // SMB + RPC → Windows (Linux Samba possible but less likely)
        if (portSet.Contains(445) && portSet.Contains(135))
            return new OsFingerprint("Windows", "Ports", 55);

        // SSH-only with no Windows ports → Linux (low confidence)
        if (portSet.Contains(22) && !portSet.Contains(3389) &&
            !portSet.Contains(445) && !portSet.Contains(135) &&
            !portSet.Contains(5985))
            return new OsFingerprint("Linux", "Ports", 40);

        return null;
    }

    private static readonly (string Pattern, string Os, int Confidence)[] SnmpOsPatterns =
    [
        ("VMware ESXi", "VMware ESXi", 90),
        ("Cisco IOS", "Cisco IOS", 90),
        ("Cisco Adaptive", "Cisco ASA", 90),
        ("Juniper", "Juniper JUNOS", 85),
        ("FortiOS", "Fortinet FortiOS", 90),
        ("RouterOS", "MikroTik RouterOS", 90),
        ("Ubuntu", "Ubuntu Linux", 85),
        ("Debian", "Debian Linux", 85),
        ("CentOS", "CentOS Linux", 85),
        ("Red Hat", "Red Hat Linux", 85),
        ("SUSE", "SUSE Linux", 80),
        ("Linux", "Linux", 75),
        ("Microsoft Windows", "Windows", 85),
        ("Windows", "Windows", 80),
        ("FreeBSD", "FreeBSD", 80),
        ("HP ETHERNET", "HP Printer Firmware", 75),
        ("RICOH", "Ricoh Printer Firmware", 75),
        ("APC", "APC UPS Firmware", 75),
        ("Eaton", "Eaton UPS Firmware", 75),
    ];

    /// <summary>
    /// Guesses the operating system from SNMP sysDescr string.
    /// </summary>
    public static OsFingerprint? GuessFromSnmp(string? sysDescr)
    {
        if (string.IsNullOrWhiteSpace(sysDescr)) return null;

        OsFingerprint? best = null;
        foreach (var (pattern, os, confidence) in SnmpOsPatterns)
        {
            if (sysDescr.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                if (best is null || confidence > best.Confidence)
                    best = new OsFingerprint(os, "SNMP", confidence);
            }
        }
        return best;
    }

    /// <summary>
    /// Guesses the OS from the NTLM version field (Major.Minor.Build).
    /// </summary>
    public static OsFingerprint? GuessFromNtlm(string? osBuild)
    {
        if (string.IsNullOrEmpty(osBuild)) return null;

        var parts = osBuild.Split('.');
        if (parts.Length < 3 || !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[2], out var build))
            return null;

        var os = (major, build) switch
        {
            (10, >= 22000) => "Windows 11 / Server 2022+",
            (10, >= 20348) => "Windows Server 2022",
            (10, >= 19041) => "Windows 10 / Server 2019",
            (10, >= 17763) => "Windows 10 1809 / Server 2019",
            (10, >= 14393) => "Windows 10 / Server 2016",
            (10, _) => "Windows 10",
            (6, _) when build >= 9600 => "Windows 8.1 / Server 2012 R2",
            (6, _) when build >= 9200 => "Windows 8 / Server 2012",
            (6, _) when build >= 7601 => "Windows 7 SP1 / Server 2008 R2",
            (6, _) => "Windows Vista / Server 2008",
            (5, _) => "Windows XP / Server 2003",
            _ => $"Windows (Build {osBuild})"
        };

        return new OsFingerprint($"{os} (Build {build})", "NTLM", 90);
    }

    /// <summary>
    /// Merges TTL-based and banner-based OS guesses into the best overall guess.
    /// Banner wins when it has higher confidence (more specific).
    /// Same OS family boosts confidence by 10 (capped at 95).
    /// </summary>
    public static OsFingerprint? Merge(OsFingerprint? ttlGuess, OsFingerprint? bannerGuess)
    {
        return MergeAll(ttlGuess, bannerGuess, null, null, null);
    }

    /// <summary>
    /// Merges OS guesses from all sources (TTL, banner, ports, SNMP).
    /// Multiple agreeing sources boost confidence. Highest confidence wins.
    /// </summary>
    public static OsFingerprint? MergeAll(
        OsFingerprint? ttlGuess,
        OsFingerprint? bannerGuess,
        OsFingerprint? portGuess,
        OsFingerprint? snmpGuess,
        OsFingerprint? ntlmGuess = null)
    {
        var candidates = new[] { ttlGuess, bannerGuess, portGuess, snmpGuess, ntlmGuess }
            .Where(g => g is not null)
            .Cast<OsFingerprint>()
            .ToList();

        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        var best = candidates.OrderByDescending(c => c.Confidence).First();

        // Count how many sources agree on the same OS family → boost confidence
        var agreeing = candidates.Count(c => IsSameOsFamily(c.OsGuess, best.OsGuess));
        if (agreeing >= 2)
        {
            // +10 for first corroboration, +5 for each additional source
            var boost = 10 + (agreeing - 2) * 5;
            var boosted = Math.Min(95, best.Confidence + boost);
            var sources = string.Join("+", candidates
                .Where(c => IsSameOsFamily(c.OsGuess, best.OsGuess))
                .Select(c => c.Source)
                .Distinct());
            return best with { Confidence = boosted, Source = sources };
        }

        return best;
    }

    private static bool IsSameOsFamily(string a, string b)
    {
        if (IsWindows(a) && IsWindows(b)) return true;
        if (IsLinux(a) && IsLinux(b)) return true;
        if (IsBsd(a) && IsBsd(b)) return true;
        if (IsNetworkEquipment(a) && IsNetworkEquipment(b)) return true;
        if (IsEmbeddedFirmware(a) && IsEmbeddedFirmware(b)) return true;
        return false;
    }

    private static bool IsWindows(string os) =>
        os.Contains("Windows", StringComparison.OrdinalIgnoreCase);

    private static bool IsLinux(string os) =>
        os.Contains("Linux", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Debian", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("CentOS", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Red Hat", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Fedora", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Raspbian", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("macOS", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Synology", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("QNAP", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Proxmox", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("OpenWrt", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("DD-WRT", StringComparison.OrdinalIgnoreCase);

    private static bool IsBsd(string os) =>
        os.Contains("FreeBSD", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("pfSense", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("OPNsense", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("TrueNAS", StringComparison.OrdinalIgnoreCase);

    private static bool IsNetworkEquipment(string os) =>
        os.Contains("Network Equipment", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Cisco", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("MikroTik", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Fortinet", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Juniper", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("RouterOS", StringComparison.OrdinalIgnoreCase);

    private static bool IsEmbeddedFirmware(string os) =>
        os.Contains("Printer", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("UPS", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Firmware", StringComparison.OrdinalIgnoreCase) ||
        os.Contains("Embedded", StringComparison.OrdinalIgnoreCase);
}
