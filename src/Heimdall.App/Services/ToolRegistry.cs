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

using System.Collections.Frozen;
using Heimdall.Core.Models;

namespace Heimdall.App.Services;

/// <summary>
/// Single source of truth for all built-in tools. Replaces the three
/// duplicate lists (menu definitions, palette commands, view factory switch)
/// with one ordered registry. Adding a new tool = one entry here + one View class.
/// </summary>
public sealed class ToolRegistry
{
    private readonly record struct ToolEntry(ToolDescriptor Descriptor, Func<IToolView> Factory);

    private readonly IReadOnlyList<ToolEntry> _entries;
    private readonly FrozenDictionary<string, ToolEntry> _byId;

    /// <summary>All tool descriptors in display order (for menus and palette).</summary>
    public IReadOnlyList<ToolDescriptor> All { get; }

    public ToolRegistry()
    {
        var entries = new List<ToolEntry>
        {
            // ── Network ───────────────────────────────────────────────
            Entry("PING",     ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolPing",     "PaletteToolPingWith",     ["ping"],                true,  () => new Views.Tools.PingToolView(),           "Geo.Tool.NetworkScanner"),
            Entry("DNS",      ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolDns",      "PaletteToolDnsWith",      ["dns","nslookup","dig"],true,  () => new Views.Tools.DnsLookupView(),          "Geo.Tool.DnsLookup"),
            Entry("CERT",     ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolCert",     "PaletteToolCertWith",     ["cert","ssl"],          true,  () => new Views.Tools.CertInspectorView(),     "Geo.Tool.CertInspector"),
            Entry("PORTSCAN", ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolPortScan", "PaletteToolPortScanWith", ["portscan","scan"],     true,  () => new Views.Tools.PortScannerView(),        "Geo.Tool.PortScanner"),
            Entry("SUBNET",   ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolSubnet",   "PaletteToolSubnetWith",   ["subnet"],              false, () => new Views.Tools.SubnetCalculatorView(),   "Geo.Tool.NetworkScanner"),
            Entry("IPCONV",   ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolIpConv",   "PaletteToolIpConvWith",   ["ip","ipconv"],         false, () => new Views.Tools.IpConverterView(),        "Geo.Tool.IpConverter"),
            Entry("HTTP",     ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolHttp",     "PaletteToolHttpWith",     ["http","status"],       false, () => new Views.Tools.HttpStatusCodesView(),   "Geo.Tool.HttpStatus"),
            Entry("WHOIS",    ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolWhois",    "PaletteToolWhoisWith",    ["whois"],               true,  () => new Views.Tools.WhoisLookupView(),       "Geo.Tool.Whois"),
            Entry("HTTPHEADERS", ToolCategory.Network, "ToolCategoryNetwork", "PaletteToolHttpHeaders", "PaletteToolHttpHeadersWith", ["headers","httpheaders","secheaders"], true, () => new Views.Tools.HttpHeaderAnalyzerView(), "Geo.Tool.HttpHeaders"),
            Entry("BANNER",   ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolBanner",   "PaletteToolBannerWith",   ["banner","grab"],               true,  () => new Views.Tools.BannerGrabberView(),      "Geo.Tool.BannerGrabber"),
            Entry("TCPTRACE", ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolTrace",    "PaletteToolTraceWith",    ["trace","traceroute","tracert"], true,  () => new Views.Tools.TcpTracerouteView(),      "Geo.Tool.Traceroute"),
            Entry("SNMPWALK", ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolSnmp",     "PaletteToolSnmpWith",     ["snmp","snmpwalk"],             true,  () => new Views.Tools.SnmpWalkerView(),         "Geo.Tool.SnmpWalker"),
            Entry("ARPMON",   ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolArp",      "PaletteToolArpWith",      ["arp","arpmon"],                false, () => new Views.Tools.ArpMonitorView(),         "Geo.Tool.ArpMonitor"),
            Entry("FWTEST",   ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolFwTest",   "PaletteToolFwTestWith",   ["fw","firewall","fwtest"],      false, () => new Views.Tools.FirewallTesterView(),     "Geo.Tool.FirewallTester"),
            Entry("NETMAP",   ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolNetMap",   "PaletteToolNetMapWith",   ["netmap","cartography","discover"], false, () => new Views.Tools.NetworkCartographyView(), "Geo.Tool.NetMap"),
            Entry("NETCALC",  ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolNetCalc",  "PaletteToolNetCalcWith",  ["netcalc","vlan","supernet"], false, () => new Views.Tools.NetworkCalculatorView(), "Geo.Tool.NetworkCalculator"),

            // ── Security ──────────────────────────────────────────────
            Entry("HASH",     ToolCategory.Security, "ToolCategorySecurity", "PaletteToolHash",     "PaletteToolHashWith",     ["hash"],                false, () => new Views.Tools.HashGeneratorView(),      "Geo.Tool.HashGenerator"),
            Entry("HMAC",     ToolCategory.Security, "ToolCategorySecurity", "PaletteToolHmac",     "PaletteToolHmacWith",     ["hmac"],                false, () => new Views.Tools.HmacGeneratorView(),      "Geo.Tool.HashGenerator"),
            Entry("PASSWORD", ToolCategory.Security, "ToolCategorySecurity", "PaletteToolPassword", "PaletteToolPasswordWith", ["password","pwgen"],    false, () => new Views.Tools.PasswordGeneratorView(),  "Geo.Tool.PasswordGenerator"),
            Entry("SSHKEY",   ToolCategory.Security, "ToolCategorySecurity", "PaletteToolSshKey",   "PaletteToolSshKeyWith",   ["sshkey","keygen"],     false, () => new Views.Tools.SshKeyGeneratorView(),    "Geo.Tool.SshKeyGenerator"),
            Entry("CERTGEN",  ToolCategory.Security, "ToolCategorySecurity", "PaletteToolCertGen",  "PaletteToolCertGenWith",  ["certgen","certificate","openssl"], false, () => new Views.Tools.CertificateGeneratorView(), "Geo.Tool.CertificateGenerator"),
            Entry("JWT",      ToolCategory.Security, "ToolCategorySecurity", "PaletteToolJwt",      "PaletteToolJwtWith",      ["jwt"],                 false, () => new Views.Tools.JwtParserView(),         "Geo.Tool.Jwt"),
            Entry("TOTP",     ToolCategory.Security, "ToolCategorySecurity", "PaletteToolTotp",     "PaletteToolTotpWith",     ["totp","otp","2fa"],    false, () => new Views.Tools.TotpGeneratorView(),     "Geo.Tool.Totp"),
            Entry("PWDAUDIT", ToolCategory.Security, "ToolCategorySecurity", "PaletteToolPwdAudit", "PaletteToolPwdAuditWith", ["pwdaudit","password-audit","passcheck"], false, () => new Views.Tools.PasswordAuditView(), "Geo.Tool.PasswordAudit"),
            Entry("SSHAUDIT",ToolCategory.Security, "ToolCategorySecurity", "PaletteToolSshAudit", "PaletteToolSshAuditWith", ["sshaudit","keyaudit"],          false, () => new Views.Tools.SshKeyAuditView(),        "Geo.Tool.SshKeyAudit"),
            Entry("TLSAUDIT",ToolCategory.Security, "ToolCategorySecurity", "PaletteToolTlsAudit", "PaletteToolTlsAuditWith", ["tlsaudit","sslaudit"],          true,  () => new Views.Tools.TlsAuditView(),           "Geo.Tool.TlsAudit"),
            Entry("DNSSEC",  ToolCategory.Security, "ToolCategorySecurity", "PaletteToolDnsSec",   "PaletteToolDnsSecWith",   ["dnssec","spf","dmarc","dkim"],  true,  () => new Views.Tools.DnsSecurityView(),         "Geo.Tool.DnsSecurity"),
            Entry("SMBENUM", ToolCategory.Security, "ToolCategorySecurity", "PaletteToolSmb",      "PaletteToolSmbWith",      ["smb","ntlm","netbios"],         true,  () => new Views.Tools.SmbEnumeratorView(),       "Geo.Tool.SmbEnumerator"),
            Entry("DEFAULTCREDS", ToolCategory.Security, "ToolCategorySecurity", "PaletteToolDefCred", "PaletteToolDefCredWith", ["defaultcreds","creds"],       true,  () => new Views.Tools.DefaultCredentialView(),   "Geo.Tool.DefaultCreds"),
            Entry("CVELOOKUP",ToolCategory.Security, "ToolCategorySecurity", "PaletteToolCve",     "PaletteToolCveWith",      ["cve","vuln","vulnerability"],   false, () => new Views.Tools.CveLookupView(),           "Geo.Tool.CveLookup"),
            Entry("SECNUMCLOUD",ToolCategory.Security, "ToolCategorySecurity", "PaletteToolAudit",  "PaletteToolAuditWith",    ["secnumcloud","audit","compliance","anssi"], false, () => new Views.Tools.SecNumCloudAuditView(), "Geo.Tool.SecNumCloud"),

            // ── Encoding & Format ─────────────────────────────────────
            Entry("BASE64",   ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolBase64",   "PaletteToolBase64With",   ["base64"],              false, () => new Views.Tools.Base64ToolView(),         "Geo.Tool.Base64Encoding"),
            Entry("URLENC",   ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolUrlEnc",   "PaletteToolUrlEncWith",   ["url","urlencode"],     false, () => new Views.Tools.UrlEncoderView(),        "Geo.Tool.UrlEncoder"),
            Entry("JSON",     ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolJson",     "PaletteToolJsonWith",     ["json"],                false, () => new Views.Tools.JsonFormatterView(),      "Geo.Tool.JsonFormatter"),
            Entry("REGEX",    ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolRegex",    "PaletteToolRegexWith",    ["regex"],               false, () => new Views.Tools.RegexTesterView(),        "Geo.Tool.RegexTester"),
            Entry("DIFF",     ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolDiff",     "PaletteToolDiffWith",     ["diff"],                false, () => new Views.Tools.TextDiffView(),          "Geo.Tool.Diff"),
            Entry("TEXTCASE", ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolTextCase", "PaletteToolTextCaseWith", ["case","textcase"],     false, () => new Views.Tools.TextCaseConverterView(), "Geo.Tool.TextCase"),

            // ── System ────────────────────────────────────────────────
            Entry("CHMOD",    ToolCategory.System,   "ToolCategorySystem",   "PaletteToolChmod",    "PaletteToolChmodWith",    ["chmod"],               false, () => new Views.Tools.ChmodCalculatorView(),   "Geo.Tool.Chmod"),
            Entry("DATETIME", ToolCategory.System,   "ToolCategorySystem",   "PaletteToolDateTime", "PaletteToolDateTimeWith", ["datetime","epoch"],    false, () => new Views.Tools.DateTimeConverterView(), "Geo.Tool.DateTime"),
            Entry("UUID",     ToolCategory.System,   "ToolCategorySystem",   "PaletteToolUuid",     "PaletteToolUuidWith",     ["uuid","guid"],         false, () => new Views.Tools.UuidGeneratorView(),     "Geo.Tool.Uuid"),
            Entry("CRONTAB",  ToolCategory.System,   "ToolCategorySystem",   "PaletteToolCron",     "PaletteToolCronWith",     ["cron","crontab"],      false, () => new Views.Tools.CrontabBuilderView(),    "Geo.Tool.Crontab"),
            Entry("LOGVIEW",  ToolCategory.System,   "ToolCategorySystem",   "PaletteToolLogView",  "PaletteToolLogViewWith",  ["log","tail","logview"], false, () => new Views.Tools.LogViewerView(),          "Geo.Tool.LogViewer"),
            Entry("HOSTS",    ToolCategory.System,   "ToolCategorySystem",   "PaletteToolHosts",    "PaletteToolHostsWith",    ["hosts"],               false, () => new Views.Tools.HostsFileEditorView(),    "Geo.Tool.HostsFileEditor"),
            Entry("SSHCONFIG",ToolCategory.System,   "ToolCategorySystem",   "PaletteToolSshConfig","PaletteToolSshConfigWith",["sshconfig","ssh-config"], false, () => new Views.Tools.SshConfigGeneratorView(), "Geo.Tool.SshConfigGenerator"),
            Entry("CRONJOB",  ToolCategory.System,   "ToolCategorySystem",   "PaletteToolCronJob",  "PaletteToolCronJobWith",  ["cronjob","crontab-manager","tasks"], false, () => new Views.Tools.CronJobManagerView(), "Geo.Tool.CronJobManager"),
            Entry("SERVICES", ToolCategory.System,   "ToolCategorySystem",   "PaletteToolServices", "PaletteToolServicesWith", ["services","svc","systemctl"],        false, () => new Views.Tools.ServiceStatusView(),  "Geo.Tool.ServiceStatusDashboard"),
            Entry("NOTES",    ToolCategory.System,   "ToolCategorySystem",   "PaletteToolNotes",    "PaletteToolNotesWith",    ["notes","note","markdown","md","confluence"], false, () => new Views.Tools.NotesToolView(), "Geo.Tool.Notes"),
            Entry("DIAGRAM",  ToolCategory.System,   "ToolCategorySystem",   "PaletteToolDiagram",  "PaletteToolDiagramWith",  ["diagram","drawio","schema"],         false, () => new Views.Tools.DiagramEditorView(),  "Geo.Tool.Diagram"),
            Entry("HACKERSIM",ToolCategory.System,   "ToolCategorySystem",   "PaletteToolHackerSim","PaletteToolHackerSimWith",["hacker","matrix","hackersim"],        false, () => new Views.Tools.HackerSimulatorView(),"Geo.Tool.HackerSimulator"),
        };

        _entries = entries;
        All = entries.Select(e => e.Descriptor).ToList();
        _byId = entries.ToFrozenDictionary(
            e => e.Descriptor.Id,
            e => e,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Looks up a tool by its short ID (e.g. "PING").
    /// Also accepts the prefixed form "TOOL:PING" — the prefix is stripped automatically.
    /// </summary>
    public ToolDescriptor? GetById(string toolId)
    {
        var key = StripPrefix(toolId);
        return _byId.TryGetValue(key, out var entry) ? entry.Descriptor : null;
    }

    /// <summary>
    /// Creates a new instance of the tool's view control.
    /// Must be called on the UI thread.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the tool ID is unknown.</exception>
    public IToolView CreateView(string toolId)
    {
        var key = StripPrefix(toolId);
        if (!_byId.TryGetValue(key, out var entry))
        {
            throw new ArgumentException($"Unknown tool ID: {toolId}", nameof(toolId));
        }
        return entry.Factory();
    }

    /// <summary>
    /// Returns true if the given tool type (e.g. "TOOL:PING" or "PING") is a network tool
    /// that should prompt for a target host when opened standalone.
    /// </summary>
    public bool IsNetworkTool(string toolId)
    {
        var key = StripPrefix(toolId);
        return _byId.TryGetValue(key, out var entry) && entry.Descriptor.IsNetworkTool;
    }

    // ── Static lookups for XAML converters (no DI access) ──────────────

    private static readonly FrozenDictionary<string, string> s_geometryKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["PING"]      = "Geo.Tool.NetworkScanner",
        ["DNS"]       = "Geo.Tool.DnsLookup",
        ["CERT"]      = "Geo.Tool.CertInspector",
        ["PORTSCAN"]  = "Geo.Tool.PortScanner",
        ["SUBNET"]    = "Geo.Tool.NetworkScanner",
        ["IPCONV"]    = "Geo.Tool.IpConverter",
        ["HTTP"]      = "Geo.Tool.HttpStatus",
        ["WHOIS"]     = "Geo.Tool.Whois",
        ["HTTPHEADERS"] = "Geo.Tool.HttpHeaders",
        ["BANNER"]    = "Geo.Tool.BannerGrabber",
        ["TCPTRACE"]  = "Geo.Tool.Traceroute",
        ["SNMPWALK"]  = "Geo.Tool.SnmpWalker",
        ["ARPMON"]    = "Geo.Tool.ArpMonitor",
        ["FWTEST"]    = "Geo.Tool.FirewallTester",
        ["NETMAP"]    = "Geo.Tool.NetMap",
        ["NETCALC"]   = "Geo.Tool.NetworkCalculator",
        ["HASH"]      = "Geo.Tool.HashGenerator",
        ["HMAC"]      = "Geo.Tool.HashGenerator",
        ["PASSWORD"]  = "Geo.Tool.PasswordGenerator",
        ["SSHKEY"]    = "Geo.Tool.SshKeyGenerator",
        ["CERTGEN"]   = "Geo.Tool.CertificateGenerator",
        ["JWT"]       = "Geo.Tool.Jwt",
        ["TOTP"]      = "Geo.Tool.Totp",
        ["PWDAUDIT"]  = "Geo.Tool.PasswordAudit",
        ["SSHAUDIT"]  = "Geo.Tool.SshKeyAudit",
        ["TLSAUDIT"]  = "Geo.Tool.TlsAudit",
        ["DNSSEC"]    = "Geo.Tool.DnsSecurity",
        ["SMBENUM"]   = "Geo.Tool.SmbEnumerator",
        ["DEFAULTCREDS"] = "Geo.Tool.DefaultCreds",
        ["CVELOOKUP"] = "Geo.Tool.CveLookup",
        ["SECNUMCLOUD"] = "Geo.Tool.SecNumCloud",
        ["BASE64"]    = "Geo.Tool.Base64Encoding",
        ["URLENC"]    = "Geo.Tool.UrlEncoder",
        ["JSON"]      = "Geo.Tool.JsonFormatter",
        ["REGEX"]     = "Geo.Tool.RegexTester",
        ["DIFF"]      = "Geo.Tool.Diff",
        ["TEXTCASE"]  = "Geo.Tool.TextCase",
        ["CHMOD"]     = "Geo.Tool.Chmod",
        ["DATETIME"]  = "Geo.Tool.DateTime",
        ["UUID"]      = "Geo.Tool.Uuid",
        ["CRONTAB"]   = "Geo.Tool.Crontab",
        ["LOGVIEW"]   = "Geo.Tool.LogViewer",
        ["HOSTS"]     = "Geo.Tool.HostsFileEditor",
        ["SSHCONFIG"] = "Geo.Tool.SshConfigGenerator",
        ["CRONJOB"]   = "Geo.Tool.CronJobManager",
        ["SERVICES"]  = "Geo.Tool.ServiceStatusDashboard",
        ["NOTES"]     = "Geo.Tool.Notes",
        ["DIAGRAM"]   = "Geo.Tool.Diagram",
        ["HACKERSIM"] = "Geo.Tool.HackerSimulator",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> s_categoryBrushKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["PING"]      = "ToolNetworkBrush",  ["DNS"]      = "ToolNetworkBrush",
        ["CERT"]      = "ToolNetworkBrush",  ["PORTSCAN"] = "ToolNetworkBrush",
        ["SUBNET"]    = "ToolNetworkBrush",  ["IPCONV"]   = "ToolNetworkBrush",
        ["HTTP"]      = "ToolNetworkBrush",  ["WHOIS"]    = "ToolNetworkBrush",
        ["HTTPHEADERS"] = "ToolNetworkBrush",
        ["BANNER"]    = "ToolNetworkBrush",  ["TCPTRACE"] = "ToolNetworkBrush",
        ["SNMPWALK"]  = "ToolNetworkBrush",  ["ARPMON"]   = "ToolNetworkBrush",
        ["FWTEST"]    = "ToolNetworkBrush",
        ["NETMAP"]    = "ToolNetworkBrush",  ["NETCALC"]  = "ToolNetworkBrush",
        ["HASH"]      = "ToolSecurityBrush", ["HMAC"]     = "ToolSecurityBrush",
        ["PASSWORD"]  = "ToolSecurityBrush", ["SSHKEY"]   = "ToolSecurityBrush",
        ["CERTGEN"]   = "ToolSecurityBrush", ["JWT"]      = "ToolSecurityBrush",
        ["TOTP"]      = "ToolSecurityBrush",
        ["PWDAUDIT"]  = "ToolSecurityBrush",
        ["SSHAUDIT"]  = "ToolSecurityBrush", ["TLSAUDIT"] = "ToolSecurityBrush",
        ["DNSSEC"]    = "ToolSecurityBrush", ["SMBENUM"]  = "ToolSecurityBrush",
        ["DEFAULTCREDS"] = "ToolSecurityBrush", ["CVELOOKUP"] = "ToolSecurityBrush",
        ["SECNUMCLOUD"] = "ToolSecurityBrush",
        ["BASE64"]    = "ToolEncodingBrush", ["URLENC"]   = "ToolEncodingBrush",
        ["JSON"]      = "ToolEncodingBrush", ["REGEX"]    = "ToolEncodingBrush",
        ["DIFF"]      = "ToolEncodingBrush", ["TEXTCASE"] = "ToolEncodingBrush",
        ["CHMOD"]     = "ToolSystemBrush",   ["DATETIME"] = "ToolSystemBrush",
        ["UUID"]      = "ToolSystemBrush",   ["CRONTAB"]  = "ToolSystemBrush",
        ["LOGVIEW"]   = "ToolSystemBrush",   ["HOSTS"]    = "ToolSystemBrush",
        ["SSHCONFIG"] = "ToolSystemBrush",   ["CRONJOB"]  = "ToolSystemBrush",
        ["SERVICES"]  = "ToolSystemBrush",   ["NOTES"]    = "ToolSystemBrush",
        ["DIAGRAM"]   = "ToolSystemBrush",
        ["HACKERSIM"] = "ToolSystemBrush",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the Geometry resource key for a tool (e.g. "TOOL:PING" → "Geo.Tool.NetworkScanner").
    /// Used by XAML converters that lack DI access.
    /// </summary>
    internal static string? GetGeometryKey(string toolId)
    {
        var key = StripPrefix(toolId);
        return s_geometryKeys.TryGetValue(key, out var geoKey) ? geoKey : null;
    }

    /// <summary>
    /// Returns the category brush resource key for a tool (e.g. "TOOL:PING" → "ToolNetworkBrush").
    /// Used by XAML converters that lack DI access.
    /// </summary>
    internal static string GetCategoryBrushKey(string toolId)
    {
        var key = StripPrefix(toolId);
        return s_categoryBrushKeys.TryGetValue(key, out var brushKey) ? brushKey : "ToolBadgeBrush";
    }

    private static string StripPrefix(string toolId)
        => toolId.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase)
            ? toolId["TOOL:".Length..]
            : toolId;

    private static ToolEntry Entry(
        string id,
        ToolCategory category,
        string categoryLabelKey,
        string labelKey,
        string? labelWithArgKey,
        string[] prefixes,
        bool isNetworkTool,
        Func<IToolView> factory,
        string? iconKey = null)
        => new(new ToolDescriptor(id, category, categoryLabelKey, labelKey, labelWithArgKey, prefixes, isNetworkTool, iconKey), factory);
}
