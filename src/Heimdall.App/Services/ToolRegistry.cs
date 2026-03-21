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
            Entry("PING",     ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolPing",     "PaletteToolPingWith",     ["ping"],                true,  () => new Views.Tools.PingToolView(),           "Icon.Tool.NetworkScanner"),
            Entry("DNS",      ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolDns",      "PaletteToolDnsWith",      ["dns","nslookup","dig"],true,  () => new Views.Tools.DnsLookupView(),          "Icon.Tool.DnsLookup"),
            Entry("CERT",     ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolCert",     "PaletteToolCertWith",     ["cert","ssl"],          true,  () => new Views.Tools.CertInspectorView()),
            Entry("PORTSCAN", ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolPortScan", "PaletteToolPortScanWith", ["portscan","scan"],     true,  () => new Views.Tools.PortScannerView(),        "Icon.Tool.PortScanner"),
            Entry("SUBNET",   ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolSubnet",   "PaletteToolSubnetWith",   ["subnet"],              false, () => new Views.Tools.SubnetCalculatorView(),   "Icon.Tool.NetworkScanner"),
            Entry("IPCONV",   ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolIpConv",   null,                      ["ip","ipconv"],         false, () => new Views.Tools.IpConverterView()),
            Entry("HTTP",     ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolHttp",     null,                      ["http","status"],       false, () => new Views.Tools.HttpStatusCodesView()),
            Entry("WHOIS",    ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolWhois",    "PaletteToolWhoisWith",    ["whois"],               true,  () => new Views.Tools.WhoisLookupView()),
            Entry("NETCALC",  ToolCategory.Network,  "ToolCategoryNetwork",  "PaletteToolNetCalc",  null,                      ["netcalc","vlan","supernet"], false, () => new Views.Tools.NetworkCalculatorView(), "Icon.Tool.NetworkCalculator"),

            // ── Security ──────────────────────────────────────────────
            Entry("HASH",     ToolCategory.Security, "ToolCategorySecurity", "PaletteToolHash",     "PaletteToolHashWith",     ["hash"],                false, () => new Views.Tools.HashGeneratorView(),      "Icon.Tool.HashGenerator"),
            Entry("HMAC",     ToolCategory.Security, "ToolCategorySecurity", "PaletteToolHmac",     null,                      ["hmac"],                false, () => new Views.Tools.HmacGeneratorView(),      "Icon.Tool.HashGenerator"),
            Entry("PASSWORD", ToolCategory.Security, "ToolCategorySecurity", "PaletteToolPassword", null,                      ["password","pwgen"],    false, () => new Views.Tools.PasswordGeneratorView(),  "Icon.Tool.PasswordGenerator"),
            Entry("SSHKEY",   ToolCategory.Security, "ToolCategorySecurity", "PaletteToolSshKey",   null,                      ["sshkey","keygen"],     false, () => new Views.Tools.SshKeyGeneratorView(),    "Icon.Tool.SshKeyGenerator"),
            Entry("CERTGEN",  ToolCategory.Security, "ToolCategorySecurity", "PaletteToolCertGen",  null,                      ["certgen","certificate","openssl"], false, () => new Views.Tools.CertificateGeneratorView(), "Icon.Tool.CertificateGenerator"),
            Entry("JWT",      ToolCategory.Security, "ToolCategorySecurity", "PaletteToolJwt",      "PaletteToolJwtWith",      ["jwt"],                 false, () => new Views.Tools.JwtParserView()),
            Entry("TOTP",     ToolCategory.Security, "ToolCategorySecurity", "PaletteToolTotp",     null,                      ["totp","otp","2fa"],    false, () => new Views.Tools.TotpGeneratorView()),

            // ── Encoding & Format ─────────────────────────────────────
            Entry("BASE64",   ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolBase64",   "PaletteToolBase64With",   ["base64"],              false, () => new Views.Tools.Base64ToolView(),         "Icon.Tool.Base64Encoding"),
            Entry("URLENC",   ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolUrlEnc",   null,                      ["url","urlencode"],     false, () => new Views.Tools.UrlEncoderView()),
            Entry("JSON",     ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolJson",     "PaletteToolJsonWith",     ["json"],                false, () => new Views.Tools.JsonFormatterView(),      "Icon.Tool.JsonFormatter"),
            Entry("REGEX",    ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolRegex",    "PaletteToolRegexWith",    ["regex"],               false, () => new Views.Tools.RegexTesterView(),        "Icon.Tool.RegexTester"),
            Entry("DIFF",     ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolDiff",     null,                      ["diff"],                false, () => new Views.Tools.TextDiffView()),
            Entry("TEXTCASE", ToolCategory.Encoding, "ToolCategoryEncoding", "PaletteToolTextCase", null,                      ["case","textcase"],     false, () => new Views.Tools.TextCaseConverterView()),

            // ── System ────────────────────────────────────────────────
            Entry("CHMOD",    ToolCategory.System,   "ToolCategorySystem",   "PaletteToolChmod",    "PaletteToolChmodWith",    ["chmod"],               false, () => new Views.Tools.ChmodCalculatorView()),
            Entry("DATETIME", ToolCategory.System,   "ToolCategorySystem",   "PaletteToolDateTime", "PaletteToolDateTimeWith", ["datetime","epoch"],    false, () => new Views.Tools.DateTimeConverterView()),
            Entry("UUID",     ToolCategory.System,   "ToolCategorySystem",   "PaletteToolUuid",     null,                      ["uuid","guid"],         false, () => new Views.Tools.UuidGeneratorView()),
            Entry("CRONTAB",  ToolCategory.System,   "ToolCategorySystem",   "PaletteToolCron",     "PaletteToolCronWith",     ["cron","crontab"],      false, () => new Views.Tools.CrontabBuilderView()),
            Entry("LOGVIEW",  ToolCategory.System,   "ToolCategorySystem",   "PaletteToolLogView",  null,                      ["log","tail","logview"], false, () => new Views.Tools.LogViewerView(),          "Icon.Tool.LogViewer"),
            Entry("HOSTS",    ToolCategory.System,   "ToolCategorySystem",   "PaletteToolHosts",    null,                      ["hosts"],               false, () => new Views.Tools.HostsFileEditorView(),    "Icon.Tool.HostsFileEditor"),
            Entry("SSHCONFIG",ToolCategory.System,   "ToolCategorySystem",   "PaletteToolSshConfig",null,                      ["sshconfig","ssh-config"], false, () => new Views.Tools.SshConfigGeneratorView(), "Icon.Tool.SshConfigGenerator"),
            Entry("CRONJOB",  ToolCategory.System,   "ToolCategorySystem",   "PaletteToolCronJob",  null,                      ["cronjob","crontab-manager","tasks"], false, () => new Views.Tools.CronJobManagerView(), "Icon.Tool.CronJobManager"),
            Entry("SERVICES", ToolCategory.System,   "ToolCategorySystem",   "PaletteToolServices", null,                      ["services","svc","systemctl"],        false, () => new Views.Tools.ServiceStatusView(),  "Icon.Tool.ServiceStatusDashboard"),
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
