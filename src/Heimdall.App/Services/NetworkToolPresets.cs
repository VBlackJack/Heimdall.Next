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

using Heimdall.Core.Models;

namespace Heimdall.App.Services;

/// <summary>
/// Shared presets and labels used by multiple network tools.
/// </summary>
public static class NetworkToolPresets
{
    public static readonly string BannerGrabberDefaultPorts = string.Join(',',
        DefaultPorts.Ssh, DefaultPorts.HttpStd, DefaultPorts.HttpsStd, DefaultPorts.Http);
    public static readonly string FirewallTesterDefaultPorts = string.Join(',',
        DefaultPorts.Ssh, DefaultPorts.HttpStd, DefaultPorts.HttpsStd, DefaultPorts.Rdp);
    public static readonly string PortScannerDefaultPorts = string.Join(',',
        DefaultPorts.Ssh, DefaultPorts.HttpStd, DefaultPorts.HttpsStd, DefaultPorts.Rdp, DefaultPorts.Vnc);
    public const string SnmpDefaultCommunity = "public";
    public const string SnmpDefaultOid = "1.3.6.1.2.1.1";

    public static readonly DnsServerPreset[] DnsServers =
    [
        new("ToolDnsPresetSystem", null),
        new("ToolDnsPresetGoogle", "8.8.8.8"),
        new("ToolDnsPresetCloudflare", "1.1.1.1"),
        new("ToolDnsPresetQuad9", "9.9.9.9"),
    ];

    public static readonly string[] SnmpCommonCommunities =
    [
        "public", "private", "community", "default", "snmp", "monitor",
        "admin", "manager", "test", "cisco", "secret", "write"
    ];

    public static readonly int[] TlsQuickScanPorts =
        [DefaultPorts.HttpsStd, DefaultPorts.HttpsAlt, DefaultPorts.Imaps,
         DefaultPorts.Pop3s, DefaultPorts.Smtps, DefaultPorts.Ldaps,
         990, 5986, DefaultPorts.Rdp, 853, 5223, 8883];

    public static readonly int[] TlsExtendedScanPorts =
        [DefaultPorts.HttpsStd, DefaultPorts.HttpsAlt, DefaultPorts.Imaps,
         DefaultPorts.Pop3s, DefaultPorts.Smtps, DefaultPorts.Ldaps,
         990, 5986, DefaultPorts.Rdp, 853, 5223, 8883,
         DefaultPorts.Smtp, DefaultPorts.Pop3, DefaultPorts.Imap,
         389, DefaultPorts.SmtpSubmission, 989, 992, 1443,
         2083, 2087, 2096, 4443, 5671, 6443, 6697, 9443];

    private static readonly Dictionary<int, string> s_portServiceLabels = new()
    {
        [DefaultPorts.Ftp] = "FTP",
        [DefaultPorts.Ssh] = "SSH",
        [DefaultPorts.Telnet] = "Telnet",
        [DefaultPorts.Smtp] = "SMTP",
        [DefaultPorts.Dns] = "DNS",
        [DefaultPorts.Tftp] = "TFTP",
        [DefaultPorts.HttpStd] = "HTTP",
        [DefaultPorts.Pop3] = "POP3",
        [DefaultPorts.Imap] = "IMAP",
        [DefaultPorts.HttpsStd] = "HTTPS",
        [DefaultPorts.Smtps] = "SMTPS",
        [DefaultPorts.SmtpSubmission] = "SMTP (Submission)",
        [DefaultPorts.Imaps] = "IMAPS",
        [DefaultPorts.Pop3s] = "POP3S",
        [DefaultPorts.Mssql] = "MSSQL",
        [DefaultPorts.OracleDb] = "Oracle",
        [DefaultPorts.MySql] = "MySQL",
        [DefaultPorts.Rdp] = "RDP",
        [DefaultPorts.PostgreSql] = "PostgreSQL",
        [DefaultPorts.Vnc] = "VNC",
        [DefaultPorts.Redis] = "Redis",
        [DefaultPorts.Http] = "HTTP-Alt",
        [DefaultPorts.HttpsAlt] = "HTTPS-Alt",
        [DefaultPorts.Prometheus] = "Prometheus",
        [DefaultPorts.MongoDb] = "MongoDB",
    };

    private static readonly Dictionary<int, string> s_tlsServiceLabels = new()
    {
        [DefaultPorts.HttpsStd] = "HTTPS",
        [DefaultPorts.HttpsAlt] = "HTTPS-Alt",
        [DefaultPorts.Imaps] = "IMAPS",
        [DefaultPorts.Pop3s] = "POP3S",
        [DefaultPorts.Smtps] = "SMTPS",
        [DefaultPorts.Ldaps] = "LDAPS",
        [990] = "FTPS",
        [5986] = "WinRM-HTTPS",
        [DefaultPorts.Rdp] = "RDP",
        [853] = "DNS-over-TLS",
        [5223] = "XMPP-TLS",
        [8883] = "MQTT-TLS",
        [DefaultPorts.Smtp] = "SMTP",
        [DefaultPorts.Pop3] = "POP3",
        [DefaultPorts.Imap] = "IMAP",
        [389] = "LDAP",
        [DefaultPorts.SmtpSubmission] = "SMTP-Submission",
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

    public readonly record struct DnsServerPreset(string LabelKey, string? Address);
}
