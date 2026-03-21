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

namespace Heimdall.Core.Discovery;

/// <summary>
/// Heuristic role classification based on open port patterns.
/// Maps common port combinations to server roles (e.g., Active Directory,
/// Web Server, Database) with confidence scoring.
/// </summary>
public static class RoleClassifier
{
    private static readonly RoleDefinition[] Definitions =
    [
        // ── Enterprise servers ─────────────────────────────────────────
        new("Active Directory", [53, 88, 389], [636, 445, 135, 464, 3268, 3269], 95),
        new("DNS Server", [53], [], 60),
        new("Web Server", [80, 443], [8080, 8443, 9090], 80),
        new("Database (MySQL)", [3306], [33060], 85),
        new("Database (PostgreSQL)", [5432], [], 85),
        new("Database (MSSQL)", [1433], [1434], 85),
        new("Database (Oracle)", [1521], [], 85),
        new("Database (MongoDB)", [27017], [27018, 27019], 85),
        new("Mail Server", [25], [587, 465, 993, 995, 143, 110], 80),
        new("SSH Server", [22], [], 50),
        new("Windows RDP", [3389], [445, 135, 139], 70),
        new("VNC Server", [5900], [5901, 5902], 65),
        new("Proxy/Load Balancer", [8080], [8443, 3128, 80, 443], 55),
        new("Syslog Server", [514], [6514], 55),
        new("DHCP Server", [67], [68], 55),
        new("FTP Server", [21], [990], 60),
        new("Redis", [6379], [], 75),
        new("Elasticsearch", [9200], [9300], 75),
        new("Docker/Container Host", [2375], [2376], 60),
        new("Kubernetes API", [6443], [10250], 70),

        // ── Network equipment ──────────────────────────────────────────
        new("Network Equipment (SNMP)", [161], [162, 80, 443], 60),
        new("Router/Gateway", [53, 67, 80], [443, 8080, 161, 23], 75),
        new("Managed Switch", [161, 22], [23, 80, 443], 65),
        new("MikroTik Router", [8291], [80, 443, 22, 8728, 8729], 80),
        new("Wireless Access Point", [80], [443, 22, 161], 40),

        // ── NAS / Storage ──────────────────────────────────────────────
        new("NAS (Synology)", [5000], [5001, 80, 443, 22, 139, 445], 85),
        new("NAS (QNAP)", [8080], [443, 22, 139, 445, 6881], 70),
        new("NAS/File Server", [139, 445], [21, 22, 80, 111, 2049], 60),
        new("NFS Server", [2049], [111], 70),
        new("iSCSI Target", [3260], [], 75),

        // ── IP Camera / Surveillance ───────────────────────────────────
        new("IP Camera (RTSP)", [554], [80, 443, 8080], 80),
        new("IP Camera (ONVIF)", [80, 8899], [554, 443], 75),
        new("DVR/NVR", [554, 80], [443, 37777, 34567], 75),

        // ── Printers ───────────────────────────────────────────────────
        new("Network Printer", [9100], [631, 515, 80], 80),
        new("Print Server (IPP)", [631], [9100, 515, 80], 70),

        // ── IoT / Smart devices ────────────────────────────────────────
        new("Smart Home Hub", [8123], [80, 443, 1883], 65),
        new("MQTT Broker", [1883], [8883, 9001], 75),
        new("Apple Device", [62078], [5353, 3689], 70),
        new("Chromecast/Smart TV", [8008], [8443, 9000], 55),
        new("UPnP Device", [1900], [5000, 80], 50),

        // ── Telephony / VoIP ───────────────────────────────────────────
        new("VoIP/SIP Server", [5060], [5061, 80, 443], 75),
        new("Asterisk PBX", [5060, 5038], [8088, 8089], 85),

        // ── Virtualization ─────────────────────────────────────────────
        new("VMware ESXi", [443, 902], [80, 22], 80),
        new("Proxmox VE", [8006], [22, 3128], 80),
        new("Hyper-V Host", [3389, 445], [135, 139, 5985, 5986], 60),

        // ── Monitoring ─────────────────────────────────────────────────
        new("Zabbix Server", [10051], [10050, 80, 443], 75),
        new("Prometheus", [9090], [9093, 9094], 65),
        new("Grafana", [3000], [80, 443], 60),
        new("Nagios/Icinga", [80, 5665], [443, 5667], 65),
    ];

    /// <summary>
    /// Banner-based fingerprints for device identification beyond port analysis.
    /// Applied after port-based classification to refine or override roles.
    /// </summary>
    private static readonly (string Pattern, string Role, int Confidence)[] BannerFingerprints =
    [
        // Cameras
        ("Hikvision", "IP Camera (Hikvision)", 90),
        ("Dahua", "IP Camera (Dahua)", 90),
        ("AXIS", "IP Camera (AXIS)", 90),
        ("Vivotek", "IP Camera (Vivotek)", 85),
        ("Foscam", "IP Camera (Foscam)", 85),
        ("Reolink", "IP Camera (Reolink)", 85),
        ("ONVIF", "IP Camera (ONVIF)", 80),

        // NAS
        ("Synology", "NAS (Synology DSM)", 92),
        ("QNAP", "NAS (QNAP QTS)", 92),
        ("FreeNAS", "NAS (TrueNAS/FreeNAS)", 85),
        ("TrueNAS", "NAS (TrueNAS)", 85),
        ("Netgear ReadyNAS", "NAS (Netgear)", 85),
        ("Western Digital", "NAS (WD My Cloud)", 80),

        // Routers / Network
        ("MikroTik", "Router (MikroTik)", 90),
        ("RouterOS", "Router (MikroTik RouterOS)", 90),
        ("AVM FRITZ", "Router (Fritz!Box)", 90),
        ("Ubiquiti", "Network (Ubiquiti)", 85),
        ("UniFi", "Network (UniFi)", 85),
        ("OpenWrt", "Router (OpenWrt)", 85),
        ("DD-WRT", "Router (DD-WRT)", 85),
        ("pfSense", "Firewall (pfSense)", 90),
        ("OPNsense", "Firewall (OPNsense)", 90),
        ("Fortinet", "Firewall (FortiGate)", 90),
        ("FortiOS", "Firewall (FortiGate)", 90),
        ("Cisco IOS", "Router (Cisco IOS)", 90),
        ("Cisco Adaptive", "Firewall (Cisco ASA)", 90),
        ("TP-LINK", "Router (TP-Link)", 80),
        ("NETGEAR", "Router (Netgear)", 80),
        ("Linksys", "Router (Linksys)", 80),
        ("D-Link", "Router (D-Link)", 80),
        ("Zyxel", "Network (Zyxel)", 80),
        ("Aruba", "Network (Aruba)", 85),
        ("Juniper", "Network (Juniper)", 85),
        ("Palo Alto", "Firewall (Palo Alto)", 90),

        // Printers
        ("HP LaserJet", "Printer (HP LaserJet)", 90),
        ("HP Color LaserJet", "Printer (HP Color LaserJet)", 90),
        ("Brother", "Printer (Brother)", 85),
        ("Canon", "Printer (Canon)", 80),
        ("Epson", "Printer (Epson)", 80),
        ("Xerox", "Printer (Xerox)", 85),
        ("Ricoh", "Printer (Ricoh)", 85),
        ("Lexmark", "Printer (Lexmark)", 85),
        ("Kyocera", "Printer (Kyocera)", 85),
        ("CUPS", "Print Server (CUPS)", 75),

        // Hypervisors
        ("VMware", "Hypervisor (VMware)", 85),
        ("ESXi", "Hypervisor (VMware ESXi)", 90),
        ("Proxmox", "Hypervisor (Proxmox VE)", 90),
        ("XenServer", "Hypervisor (XenServer)", 85),

        // Smart Home / IoT
        ("Home Assistant", "Smart Home (Home Assistant)", 90),
        ("Philips Hue", "IoT (Philips Hue Bridge)", 85),
        ("Sonos", "IoT (Sonos Speaker)", 80),

        // NTP
        ("ntpd", "NTP Server", 80),
        ("chrony", "NTP Server (Chrony)", 80),
    ];

    /// <summary>
    /// Classifies a host based on its open ports, returning all matching roles
    /// sorted by descending confidence.
    /// </summary>
    public static List<RoleMatch> Classify(IReadOnlyList<int> openPorts)
    {
        var matches = new List<RoleMatch>();
        var portSet = new HashSet<int>(openPorts);

        foreach (var def in Definitions)
        {
            var requiredHits = def.RequiredPorts.Count(p => portSet.Contains(p));
            if (requiredHits == 0) continue;

            var optionalHits = def.OptionalPorts.Count(p => portSet.Contains(p));

            var requiredCoverage = (double)requiredHits / def.RequiredPorts.Length;
            var confidence = (int)(def.BaseConfidence * requiredCoverage);
            if (optionalHits > 0) confidence += optionalHits * 5;
            confidence = Math.Min(confidence, 99);

            if (requiredCoverage < 1.0 && confidence < 40) continue;

            var evidence = new List<string>();
            foreach (var p in def.RequiredPorts.Where(p => portSet.Contains(p)))
                evidence.Add($"port:{p} ({GetPortServiceName(p)})");
            foreach (var p in def.OptionalPorts.Where(p => portSet.Contains(p)))
                evidence.Add($"port:{p} ({GetPortServiceName(p)}) [optional]");

            matches.Add(new RoleMatch(def.RoleName, confidence, [.. evidence]));
        }

        return [.. matches.OrderByDescending(m => m.Confidence)];
    }

    /// <summary>
    /// Enhanced classification using both open ports AND service banners.
    /// Banner fingerprints can identify specific devices (cameras, NAS, routers, printers)
    /// that port-only analysis would classify as generic "Web Server" or "Unknown".
    /// </summary>
    public static List<RoleMatch> ClassifyWithBanners(
        IReadOnlyList<int> openPorts,
        IReadOnlyList<string?> banners)
    {
        // Start with port-based classification
        var matches = Classify(openPorts);

        // Scan all banners for device fingerprints
        foreach (var banner in banners)
        {
            if (string.IsNullOrWhiteSpace(banner)) continue;

            foreach (var (pattern, role, confidence) in BannerFingerprints)
            {
                if (banner.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if this role is already matched (avoid duplicates)
                    if (matches.Any(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    matches.Add(new RoleMatch(
                        role,
                        confidence,
                        [$"banner: \"{pattern}\" detected"]));
                }
            }
        }

        return [.. matches.OrderByDescending(m => m.Confidence)];
    }

    /// <summary>
    /// Returns a human-readable service name for a well-known port number.
    /// </summary>
    public static string GetPortServiceName(int port) => port switch
    {
        21 => "FTP", 22 => "SSH", 25 => "SMTP", 53 => "DNS",
        67 => "DHCP", 68 => "DHCP", 80 => "HTTP", 88 => "Kerberos",
        110 => "POP3", 135 => "RPC", 139 => "NetBIOS", 143 => "IMAP",
        161 => "SNMP", 162 => "SNMP-Trap", 389 => "LDAP", 443 => "HTTPS",
        445 => "SMB", 464 => "Kerberos-Change", 465 => "SMTPS",
        514 => "Syslog", 587 => "SMTP-Submission", 636 => "LDAPS",
        993 => "IMAPS", 995 => "POP3S", 990 => "FTPS",
        1433 => "MSSQL", 1434 => "MSSQL-Browser", 1521 => "Oracle",
        2375 => "Docker", 2376 => "Docker-TLS",
        3128 => "Squid", 3268 => "Global-Catalog", 3269 => "GC-SSL",
        3306 => "MySQL", 3389 => "RDP", 5432 => "PostgreSQL",
        5900 => "VNC", 6379 => "Redis", 6443 => "K8s-API",
        6514 => "Syslog-TLS", 8080 => "HTTP-Alt", 8443 => "HTTPS-Alt",
        9090 => "Web-Console", 9200 => "Elasticsearch", 9300 => "ES-Transport",
        10250 => "Kubelet", 27017 => "MongoDB",
        554 => "RTSP", 631 => "IPP", 515 => "LPD",
        902 => "VMware-Auth", 1883 => "MQTT", 1900 => "UPnP/SSDP",
        2049 => "NFS", 3000 => "Grafana/Dev", 3260 => "iSCSI",
        3689 => "DAAP", 5000 => "Synology-HTTP", 5001 => "Synology-HTTPS",
        5038 => "Asterisk-AMI", 5060 => "SIP", 5061 => "SIP-TLS",
        5353 => "mDNS", 5665 => "Icinga2", 5667 => "NSCA",
        5985 => "WinRM-HTTP", 5986 => "WinRM-HTTPS",
        8006 => "Proxmox-Web", 8008 => "Chromecast",
        8088 => "Asterisk-HTTP", 8123 => "Home-Assistant",
        8291 => "MikroTik-Winbox", 8728 => "MikroTik-API",
        8883 => "MQTT-TLS", 8899 => "ONVIF",
        9001 => "MQTT-WS", 9100 => "RAW-Print",
        10050 => "Zabbix-Agent", 10051 => "Zabbix-Server",
        27018 => "MongoDB-Shard", 27019 => "MongoDB-Config",
        33060 => "MySQL-X", 34567 => "DVR-Admin",
        37777 => "Dahua-DVR", 62078 => "Apple-Lockdown",
        _ => $"Port-{port}"
    };

    private record RoleDefinition(
        string RoleName,
        int[] RequiredPorts,
        int[] OptionalPorts,
        int BaseConfidence);
}
