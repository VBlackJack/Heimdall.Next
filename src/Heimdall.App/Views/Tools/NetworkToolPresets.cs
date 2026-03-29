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

using Heimdall.Core.Configuration;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Shared presets and labels used by multiple network tools.
/// </summary>
public static class NetworkToolPresets
{
    public const string BannerGrabberDefaultPorts = "22,80,443,8080";
    public const string FirewallTesterDefaultPorts = "22,80,443,3389";
    public const string PortScannerDefaultPorts = "22,80,443,3389,5900";
    public const string SnmpDefaultCommunity = "public";
    public const string SnmpDefaultOid = "1.3.6.1.2.1.1";

    public static readonly DnsServerPreset[] DnsServers =
    [
        new("System", null),
        new("Google (8.8.8.8)", "8.8.8.8"),
        new("Cloudflare (1.1.1.1)", "1.1.1.1"),
        new("Quad9 (9.9.9.9)", "9.9.9.9"),
    ];

    public static readonly string[] SnmpCommonCommunities =
    [
        "public", "private", "community", "default", "snmp", "monitor",
        "admin", "manager", "test", "cisco", "secret", "write"
    ];

    public static readonly int[] TlsQuickScanPorts =
        [443, 8443, 993, 995, 465, 636, 990, 5986, 3389, 853, 5223, 8883];

    public static readonly int[] TlsExtendedScanPorts =
        [443, 8443, 993, 995, 465, 636, 990, 5986, 3389, 853, 5223, 8883,
         25, 110, 143, 389, 587, 989, 992, 1443, 2083, 2087, 2096, 4443, 5671, 6443, 6697, 9443];

    private static readonly Dictionary<int, string> s_portServiceLabels = new()
    {
        [DefaultPorts.Ftp] = "FTP",
        [DefaultPorts.Ssh] = "SSH",
        [DefaultPorts.Telnet] = "Telnet",
        [25] = "SMTP",
        [53] = "DNS",
        [DefaultPorts.Tftp] = "TFTP",
        [80] = "HTTP",
        [110] = "POP3",
        [143] = "IMAP",
        [443] = "HTTPS",
        [465] = "SMTPS",
        [587] = "SMTP (Submission)",
        [993] = "IMAPS",
        [995] = "POP3S",
        [1433] = "MSSQL",
        [1521] = "Oracle",
        [3306] = "MySQL",
        [DefaultPorts.Rdp] = "RDP",
        [5432] = "PostgreSQL",
        [DefaultPorts.Vnc] = "VNC",
        [6379] = "Redis",
        [DefaultPorts.Http] = "HTTP-Alt",
        [8443] = "HTTPS-Alt",
        [9090] = "Prometheus",
        [27017] = "MongoDB",
    };

    private static readonly Dictionary<int, string> s_tlsServiceLabels = new()
    {
        [443] = "HTTPS",
        [8443] = "HTTPS-Alt",
        [993] = "IMAPS",
        [995] = "POP3S",
        [465] = "SMTPS",
        [636] = "LDAPS",
        [990] = "FTPS",
        [5986] = "WinRM-HTTPS",
        [3389] = "RDP",
        [853] = "DNS-over-TLS",
        [5223] = "XMPP-TLS",
        [8883] = "MQTT-TLS",
        [25] = "SMTP",
        [110] = "POP3",
        [143] = "IMAP",
        [389] = "LDAP",
        [587] = "SMTP-Submission",
        [989] = "FTPS-Data",
        [992] = "Telnets",
        [1443] = "MSSQL-TLS",
        [2083] = "cPanel-TLS",
        [2087] = "WHM-TLS",
        [2096] = "Webmail-TLS",
        [4443] = "HTTPS-Alt",
        [5671] = "AMQPS",
        [6443] = "Kubernetes-API",
        [6697] = "IRC-TLS",
        [9443] = "HTTPS-Alt",
    };

    public static string GetPortServiceLabel(int port)
        => s_portServiceLabels.TryGetValue(port, out var label) ? label : string.Empty;

    public static string GetTlsServiceLabel(int port)
        => s_tlsServiceLabels.TryGetValue(port, out var label) ? label : "TLS";
}

public readonly record struct DnsServerPreset(string Label, string? Address);
