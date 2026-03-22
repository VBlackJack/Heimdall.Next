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
    /// Merges TTL-based and banner-based OS guesses into the best overall guess.
    /// Banner wins when it has higher confidence (more specific).
    /// Same OS family boosts confidence by 10 (capped at 95).
    /// </summary>
    public static OsFingerprint? Merge(OsFingerprint? ttlGuess, OsFingerprint? bannerGuess)
    {
        if (ttlGuess is null) return bannerGuess;
        if (bannerGuess is null) return ttlGuess;

        var sameFamily = IsSameOsFamily(ttlGuess.OsGuess, bannerGuess.OsGuess);

        if (sameFamily)
        {
            // Both agree: use the more specific one (banner) with boosted confidence
            var boosted = Math.Min(95, bannerGuess.Confidence + 10);
            return bannerGuess with { Confidence = boosted, Source = "TTL+Banner" };
        }

        // Disagreement: trust the higher confidence source
        return bannerGuess.Confidence >= ttlGuess.Confidence ? bannerGuess : ttlGuess;
    }

    private static bool IsSameOsFamily(string a, string b)
    {
        if (IsWindows(a) && IsWindows(b)) return true;
        if (IsLinux(a) && IsLinux(b)) return true;
        if (IsBsd(a) && IsBsd(b)) return true;
        if (IsNetworkEquipment(a) && IsNetworkEquipment(b)) return true;
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
        os.Contains("RouterOS", StringComparison.OrdinalIgnoreCase);
}
