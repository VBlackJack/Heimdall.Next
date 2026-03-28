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

using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Offline CVE lookup tool that matches software names and versions against
/// an embedded vulnerability database covering common infrastructure products.
/// </summary>
public partial class CveLookupView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private List<CveMatch> _lastResults = [];
    private string _lastQuery = string.Empty;
    private bool _disposed;

    // ── CVE entry record ─────────────────────────────────────────────

    private sealed record CveEntry(
        string Id,
        double CvssScore,
        string Severity,
        string Summary,
        string AffectedVersions,
        Func<string, bool> IsAffected);

    // ── Embedded CVE database ────────────────────────────────────────

    private static readonly Dictionary<string, List<CveEntry>> CveDatabase = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OpenSSH"] =
        [
            new("CVE-2024-6387", 8.1, "High", "regreSSHion: Race condition in signal handler allows unauthenticated RCE", "8.5p1 - 9.7p1", v => VersionInRange(v, "8.5", "9.7")),
            new("CVE-2023-38408", 9.8, "Critical", "PKCS#11 provider: RCE via forwarded ssh-agent", "< 9.3p2", v => VersionBelow(v, "9.3.2")),
            new("CVE-2023-48795", 5.9, "Medium", "Terrapin: Prefix truncation attack on SSH BPP (strict kex bypass)", "< 9.6", v => VersionBelow(v, "9.6")),
            new("CVE-2021-41617", 7.0, "High", "Privilege escalation via AuthorizedKeysCommand/AuthorizedPrincipalsCommand", "6.2 - 8.7", v => VersionInRange(v, "6.2", "8.7")),
            new("CVE-2020-15778", 7.8, "High", "Command injection via crafted filenames in scp", "< 8.8", v => VersionBelow(v, "8.8")),
        ],
        ["Apache"] =
        [
            new("CVE-2021-44228", 10.0, "Critical", "Log4Shell: RCE via JNDI lookup in log messages (affects mod_jk/Tomcat behind Apache)", "N/A (Log4j)", _ => false),
            new("CVE-2021-41773", 7.5, "High", "Path traversal and file disclosure in Apache HTTP Server 2.4.49", "2.4.49", v => VersionEquals(v, "2.4.49")),
            new("CVE-2021-42013", 9.8, "Critical", "RCE via path traversal (incomplete fix for CVE-2021-41773)", "2.4.49 - 2.4.50", v => VersionInRange(v, "2.4.49", "2.4.50")),
            new("CVE-2023-25690", 9.8, "Critical", "HTTP request smuggling via mod_proxy with RewriteRule", "< 2.4.56", v => VersionBelow(v, "2.4.56")),
            new("CVE-2023-43622", 7.5, "High", "HTTP/2 initial window zero DoS (rapid reset variant)", "< 2.4.58", v => VersionBelow(v, "2.4.58")),
        ],
        ["nginx"] =
        [
            new("CVE-2021-23017", 7.7, "High", "DNS resolver off-by-one heap write allows RCE", "0.6.18 - 1.20.0", v => VersionInRange(v, "0.6.18", "1.20.0")),
            new("CVE-2022-41741", 7.8, "High", "Memory corruption via crafted mp4 file (ngx_http_mp4_module)", "< 1.23.2", v => VersionBelow(v, "1.23.2")),
            new("CVE-2022-41742", 7.1, "High", "Memory disclosure via mp4 module (same root cause as CVE-2022-41741)", "< 1.23.2", v => VersionBelow(v, "1.23.2")),
            new("CVE-2024-7347", 4.7, "Medium", "Out-of-bounds read in ngx_http_mp4_module with crafted mp4", "< 1.27.1", v => VersionBelow(v, "1.27.1")),
        ],
        ["Apache Tomcat"] =
        [
            new("CVE-2024-50379", 9.8, "Critical", "RCE via partial PUT and concurrent read on case-insensitive FS", "< 11.0.2 / < 10.1.34 / < 9.0.98", v => VersionBelow(v, "11.0.2")),
            new("CVE-2024-52317", 6.5, "Medium", "Response mix-up due to incorrect HTTP/2 recycling", "< 11.0.1 / < 10.1.33 / < 9.0.97", v => VersionBelow(v, "11.0.1")),
            new("CVE-2023-46589", 7.5, "High", "HTTP request smuggling via malformed trailer headers", "< 10.1.18 / < 9.0.83 / < 8.5.96", v => VersionBelow(v, "10.1.18")),
            new("CVE-2023-44487", 7.5, "High", "HTTP/2 rapid reset DoS attack", "< 10.1.14 / < 9.0.81 / < 8.5.94", v => VersionBelow(v, "10.1.14")),
        ],
        ["MySQL"] =
        [
            new("CVE-2024-21047", 4.9, "Medium", "Server: InnoDB DoS via crafted queries", "< 8.0.37", v => VersionBelow(v, "8.0.37")),
            new("CVE-2023-22008", 4.9, "Medium", "Server optimizer DoS vulnerability", "< 8.0.34", v => VersionBelow(v, "8.0.34")),
            new("CVE-2023-21980", 7.1, "High", "Client: buffer overflow in C API", "< 8.0.33", v => VersionBelow(v, "8.0.33")),
            new("CVE-2024-20960", 6.5, "Medium", "Server: RAPID memory DoS in optimizer", "< 8.0.36", v => VersionBelow(v, "8.0.36")),
        ],
        ["PostgreSQL"] =
        [
            new("CVE-2024-10979", 8.8, "High", "Arbitrary code execution via environment variable manipulation in PL/Perl", "< 17.1 / < 16.5 / < 15.9", v => VersionBelow(v, "17.1")),
            new("CVE-2023-5868", 4.3, "Medium", "Memory disclosure via aggregate function calls", "< 16.1", v => VersionBelow(v, "16.1")),
            new("CVE-2023-5869", 8.8, "High", "Buffer overflow in integer overflow in array modification", "< 16.1", v => VersionBelow(v, "16.1")),
            new("CVE-2023-39417", 8.8, "High", "SQL injection in extension script @extowner@, @extschema@ replacements", "< 15.4", v => VersionBelow(v, "15.4")),
        ],
        ["Redis"] =
        [
            new("CVE-2024-31449", 8.8, "High", "Lua library: heap buffer overflow from crafted script", "< 7.4.1", v => VersionBelow(v, "7.4.1")),
            new("CVE-2023-45145", 3.6, "Low", "Unix socket permission race condition on startup", "< 7.2.4", v => VersionBelow(v, "7.2.4")),
            new("CVE-2023-41056", 8.1, "High", "Heap overflow in Cluster-mode pattern matching", "< 7.2.1", v => VersionBelow(v, "7.2.1")),
            new("CVE-2023-28856", 6.5, "Medium", "AUTH command crash allows authenticated DoS", "< 7.0.12", v => VersionBelow(v, "7.0.12")),
        ],
        ["MongoDB"] =
        [
            new("CVE-2024-1351", 8.1, "High", "Incorrect validation of SCRAM authentication leading to auth bypass", "< 7.0.5 / < 6.0.13", v => VersionBelow(v, "7.0.5")),
            new("CVE-2023-1409", 7.5, "High", "TLS certificate validation bypass on non-default configurations", "< 6.0.7 / < 5.0.18", v => VersionBelow(v, "6.0.7")),
            new("CVE-2024-3372", 7.5, "High", "Server crash via malformed BSON in validated collection", "< 7.0.9 / < 6.0.15", v => VersionBelow(v, "7.0.9")),
        ],
        ["Elasticsearch"] =
        [
            new("CVE-2023-31419", 7.5, "High", "Stack overflow DoS from malformed _search API query", "< 8.9.1 / < 7.17.13", v => VersionBelow(v, "8.9.1")),
            new("CVE-2023-46674", 8.8, "High", "File descriptor leak allows local file read", "< 8.11.1 / < 7.17.15", v => VersionBelow(v, "8.11.1")),
            new("CVE-2024-23450", 7.5, "High", "DoS via excessive recursion in nested document expansion", "< 8.13.0 / < 7.17.19", v => VersionBelow(v, "8.13.0")),
        ],
        ["Microsoft IIS"] =
        [
            new("CVE-2023-36899", 8.8, "High", "Elevation of privilege via ASP.NET identity spoofing", "IIS 10.0", v => v.StartsWith("10.", StringComparison.Ordinal) || v == "10"),
            new("CVE-2024-38169", 8.8, "High", "RCE via crafted HTTP request in ASP.NET IIS module", "IIS 10.0", v => v.StartsWith("10.", StringComparison.Ordinal) || v == "10"),
            new("CVE-2022-21907", 9.8, "Critical", "HTTP Protocol Stack (http.sys) RCE in trailer support", "IIS 10.0 (Win Server 2022)", v => v.StartsWith("10.", StringComparison.Ordinal) || v == "10"),
        ],
        ["ProFTPD"] =
        [
            new("CVE-2023-51713", 7.5, "High", "Out-of-bounds read in mod_sftp during key exchange", "< 1.3.8b", v => VersionBelow(v, "1.3.8.2")),
            new("CVE-2021-46854", 7.5, "High", "Memory disclosure in mod_radius via crafted RADIUS response", "< 1.3.7d", v => VersionBelow(v, "1.3.7.4")),
            new("CVE-2020-9273", 8.8, "High", "Use-after-free in memory pool transfer leading to RCE", "< 1.3.6c", v => VersionBelow(v, "1.3.6.3")),
        ],
        ["vsftpd"] =
        [
            new("CVE-2021-3618", 7.4, "High", "ALPACA: Application-layer protocol cross-protocol attack on TLS", "< 3.0.4", v => VersionBelow(v, "3.0.4")),
            new("CVE-2015-1419", 5.3, "Medium", "Config parsing allows directory listing bypass", "< 3.0.3", v => VersionBelow(v, "3.0.3")),
        ],
        ["Postfix"] =
        [
            new("CVE-2023-51764", 5.3, "Medium", "SMTP smuggling allows email spoofing via multi-line pipelining", "< 3.8.4", v => VersionBelow(v, "3.8.4")),
            new("CVE-2023-32182", 5.3, "Medium", "Incorrect permission on mail directories during creation", "< 3.7.5", v => VersionBelow(v, "3.7.5")),
            new("CVE-2021-28085", 4.3, "Medium", "Denial of service via large MIME attachment processing", "< 3.6.0", v => VersionBelow(v, "3.6.0")),
        ],
        ["Exim"] =
        [
            new("CVE-2023-42117", 8.1, "High", "Improper neutralization of special elements in SMTP AUTH", "< 4.96.2", v => VersionBelow(v, "4.96.2")),
            new("CVE-2023-42119", 3.1, "Low", "DNS name parsing out-of-bounds read", "< 4.96.2", v => VersionBelow(v, "4.96.2")),
            new("CVE-2023-51766", 5.3, "Medium", "SMTP smuggling via multi-line pipelining", "< 4.97.1", v => VersionBelow(v, "4.97.1")),
            new("CVE-2024-39929", 9.1, "Critical", "RFC 2231 header filename parsing bypass allows attachment filter evasion", "< 4.98", v => VersionBelow(v, "4.98")),
        ],
        ["PHP"] =
        [
            new("CVE-2024-4577", 9.8, "Critical", "CGI argument injection on Windows (Best-Fit mapping bypass)", "< 8.3.8 / < 8.2.20 / < 8.1.29", v => VersionBelow(v, "8.3.8")),
            new("CVE-2024-2961", 8.8, "High", "Buffer overflow in glibc iconv via PHP filters", "< 8.3.8", v => VersionBelow(v, "8.3.8")),
            new("CVE-2023-3824", 9.8, "Critical", "Buffer overflow in PHAR reading for tar/zip/phar", "< 8.2.10 / < 8.1.23", v => VersionBelow(v, "8.2.10")),
            new("CVE-2024-8932", 9.8, "Critical", "Heap overflow in ldap_escape on 32-bit systems", "< 8.3.14 / < 8.2.26 / < 8.1.31", v => VersionBelow(v, "8.3.14")),
        ],
        ["Node.js"] =
        [
            new("CVE-2024-22019", 7.5, "High", "Reading unprocessed HTTP request with unbounded chunk extension causes DoS", "< 21.6.1 / < 20.11.1 / < 18.19.1", v => VersionBelow(v, "21.6.1")),
            new("CVE-2024-22025", 6.5, "Medium", "Denial of service via fetch with long Content-Length in Undici", "< 21.6.1 / < 20.11.1", v => VersionBelow(v, "21.6.1")),
            new("CVE-2023-44487", 7.5, "High", "HTTP/2 rapid reset attack causing DoS", "< 20.8.1 / < 18.18.2", v => VersionBelow(v, "20.8.1")),
            new("CVE-2023-32002", 9.8, "Critical", "Policy bypass via Module._load", "< 20.5.1 / < 18.17.1", v => VersionBelow(v, "20.5.1")),
        ],
        ["Jenkins"] =
        [
            new("CVE-2024-23897", 9.8, "Critical", "Arbitrary file read via CLI command parser (args4j @-file expansion)", "< 2.442 / < 2.426.3", v => VersionBelow(v, "2.442")),
            new("CVE-2024-43044", 8.8, "High", "Arbitrary file read on agent via Remoting library ClassLoaderProxy", "< 2.471 / < 2.452.4", v => VersionBelow(v, "2.471")),
            new("CVE-2023-27898", 8.8, "High", "Stored XSS via crafted plugin name leading to RCE", "< 2.394 / < 2.375.4", v => VersionBelow(v, "2.394")),
        ],
        ["Plink"] =
        [
            new("CVE-2024-31497", 5.9, "Medium", "Biased ECDSA nonce generation allows private key recovery (P-521)", "< 0.81", v => VersionBelow(v, "0.81")),
            new("CVE-2023-48795", 5.9, "Medium", "Terrapin: Prefix truncation attack on SSH BPP (strict kex bypass)", "< 0.80", v => VersionBelow(v, "0.80")),
        ],
        ["PuTTY"] =
        [
            new("CVE-2024-31497", 5.9, "Medium", "Biased ECDSA nonce generation allows private key recovery (P-521)", "< 0.81", v => VersionBelow(v, "0.81")),
            new("CVE-2023-48795", 5.9, "Medium", "Terrapin: Prefix truncation attack on SSH BPP (strict kex bypass)", "< 0.80", v => VersionBelow(v, "0.80")),
            new("CVE-2021-36367", 7.4, "High", "Host key verification bypass allows MITM via fake authentication prompts", "< 0.76", v => VersionBelow(v, "0.76")),
        ],
    };

    // ── Total CVE count (for empty state info line) ──────────────────

    private static readonly int TotalCveCount = CveDatabase.Values.Sum(list => list.Count);
    private static readonly int TotalProductCount = CveDatabase.Count;

    // ── Banner parsing patterns ──────────────────────────────────────

    private static readonly (Regex Pattern, string Software)[] BannerPatterns =
    [
        // SSH banners: SSH-2.0-OpenSSH_8.9p1 Ubuntu-3ubuntu0.6
        (new Regex(@"OpenSSH[_/ ]?(\d[\d.p]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "OpenSSH"),

        // PuTTY / Plink in SSH banner: SSH-2.0-PuTTY_Release_0.80
        (new Regex(@"PuTTY[_/ ]?(?:Release[_/ ]?)?(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "PuTTY"),
        (new Regex(@"Plink[_/ ]?(?:Release[_/ ]?)?(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Plink"),

        // HTTP Server headers: Apache/2.4.52 (Ubuntu)
        (new Regex(@"Apache(?:/| HTTPD |httpd[ /])(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Apache"),
        (new Regex(@"Apache Tomcat/(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Apache Tomcat"),

        // nginx/1.18.0
        (new Regex(@"nginx/(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "nginx"),

        // Microsoft-IIS/10.0
        (new Regex(@"Microsoft-IIS/(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Microsoft IIS"),

        // FTP banners: 220 ProFTPD 1.3.5 Server ready
        (new Regex(@"ProFTPD[/ ](\d[\d.a-z]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ProFTPD"),
        (new Regex(@"vsftpd[/ ](\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "vsftpd"),

        // SMTP banners: 220 mail.example.com ESMTP Postfix (Ubuntu)
        (new Regex(@"Postfix(?:.*?version )?(\d[\d.]*)?", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Postfix"),
        (new Regex(@"Exim[/ ](\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Exim"),

        // Database banners
        (new Regex(@"MySQL(?:.*?)?(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "MySQL"),
        (new Regex(@"PostgreSQL[/ ](\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "PostgreSQL"),
        (new Regex(@"Redis(?:.*?v=?| server v=?)(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Redis"),
        (new Regex(@"MongoDB(?:.*?v?| server v?)(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "MongoDB"),
        (new Regex(@"Elasticsearch[/ ](\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Elasticsearch"),

        // Jenkins: X-Jenkins: 2.426.3
        (new Regex(@"Jenkins[/ :]*(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Jenkins"),

        // PHP/8.2.0
        (new Regex(@"PHP[/ ](\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "PHP"),

        // Node.js
        (new Regex(@"Node\.?js[/ ]v?(\d[\d.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Node.js"),
    ];

    // ── Simple name + version pattern as fallback ────────────────────

    private static readonly Regex SimpleNameVersion = new(
        @"^([A-Za-z][A-Za-z0-9 .+_-]*?)\s+v?(\d[\d.p]*\d)$",
        RegexOptions.Compiled);

    // ── Display model ────────────────────────────────────────────────

    public sealed class CveMatch
    {
        public string CveId { get; init; } = "";
        public double CvssScore { get; init; }
        public string Severity { get; init; } = "";
        public string Summary { get; init; } = "";
        public string AffectedVersions { get; init; } = "";
        public Brush SeverityBrush { get; init; } = Brushes.Transparent;
        public Brush CvssBrush { get; init; } = Brushes.Transparent;
        public string SeverityLabel { get; init; } = "";
        public string CvssLabel { get; init; } = "";
        public string AffectedLabel { get; init; } = "";
    }

    // ── Constructor ──────────────────────────────────────────────────

    public CveLookupView()
    {
        InitializeComponent();
        TxtInput.KeyDown += OnInputKeyDown;
    }

    /// <summary>
    /// Initializes the tool with optional context and localization.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        // If opened with a context argument (e.g. from Banner Grabber), prefill and search
        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            TxtInput.Text = context.Argument;
            PerformSearch();
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtInput.Focus();
        });
    }

    // ── Localization ─────────────────────────────────────────────────

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolCveTitle");
        TxtInput.Tag = L("ToolCveInput");
        BtnSearch.Content = L("ToolCveBtnSearch");
        BtnCopy.Content = L("ToolCveBtnCopy");
        TxtEmptyState.Text = L("ToolCveEmptyState");
        TxtNoResults.Text = L("ToolCveNoResults");
        TxtDbInfo.Text = string.Format(L("ToolCveDbInfo"), TotalCveCount, TotalProductCount);
        TxtFooterInfo.Text = string.Format(L("ToolCveDbInfo"), TotalCveCount, TotalProductCount);

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(TxtInput, L("ToolCveInput"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSearch, L("ToolCveBtnSearch"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolCveBtnCopy"));
    }

    // ── Event handlers ───────────────────────────────────────────────

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpCVELOOKUP");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PerformSearch();
            e.Handled = true;
        }
    }

    private void OnSearchClick(object sender, RoutedEventArgs e)
    {
        PerformSearch();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_lastResults.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(L("ToolCveSummary"), _lastResults.Count, _lastQuery));
        sb.AppendLine(new string('=', 72));
        sb.AppendLine();

        foreach (var cve in _lastResults)
        {
            sb.AppendLine($"{cve.CveId}  [{cve.Severity}]  CVSS {cve.CvssScore:F1}");
            sb.AppendLine($"  {cve.Summary}");
            sb.AppendLine($"  {L("ToolCveColAffected")}: {cve.AffectedVersions}");
            sb.AppendLine();
        }

        try
        {
            Clipboard.SetText(sb.ToString());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard locked by another process
        }
    }

    // ── Search engine ────────────────────────────────────────────────

    private void PerformSearch()
    {
        var rawInput = TxtInput.Text.Trim();
        if (string.IsNullOrEmpty(rawInput))
        {
            ShowEmptyState();
            return;
        }

        var results = Search(rawInput);

        _lastResults = results;

        if (results.Count == 0)
        {
            _lastQuery = rawInput;
            ShowNoResults();
        }
        else
        {
            ShowResults(results);
        }
    }

    private List<CveMatch> Search(string input)
    {
        // Try banner parsing first
        var parsed = ParseBanner(input);

        string software;
        string version;

        if (parsed is not null)
        {
            software = parsed.Value.Software;
            version = parsed.Value.Version;
        }
        else
        {
            // Try simple "name version" split
            var simpleMatch = SimpleNameVersion.Match(input);
            if (simpleMatch.Success)
            {
                software = simpleMatch.Groups[1].Value.Trim();
                version = simpleMatch.Groups[2].Value.Trim();
            }
            else
            {
                // Treat the whole input as a software name (no version)
                software = input;
                version = "";
            }
        }

        _lastQuery = string.IsNullOrEmpty(version) ? software : $"{software} {version}";

        // Try exact key match first
        if (CveDatabase.TryGetValue(software, out var entries))
        {
            return BuildMatches(entries, version);
        }

        // Fuzzy match: find keys that contain the input or vice versa
        var fuzzyKey = CveDatabase.Keys.FirstOrDefault(k =>
            k.Contains(software, StringComparison.OrdinalIgnoreCase) ||
            software.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (fuzzyKey is not null)
        {
            _lastQuery = string.IsNullOrEmpty(version) ? fuzzyKey : $"{fuzzyKey} {version}";
            return BuildMatches(CveDatabase[fuzzyKey], version);
        }

        return [];
    }

    private List<CveMatch> BuildMatches(List<CveEntry> entries, string version)
    {
        var affectedLabel = L("ToolCveColAffected") + ": ";
        var matches = new List<CveMatch>();

        foreach (var entry in entries)
        {
            // If no version provided, show all CVEs for the product
            bool isAffected = string.IsNullOrEmpty(version) || entry.IsAffected(version);
            if (!isAffected && !string.IsNullOrEmpty(version)) continue;

            matches.Add(new CveMatch
            {
                CveId = entry.Id,
                CvssScore = entry.CvssScore,
                Severity = entry.Severity,
                Summary = entry.Summary,
                AffectedVersions = entry.AffectedVersions,
                SeverityBrush = GetSeverityBrush(entry.CvssScore),
                CvssBrush = GetCvssBrush(entry.CvssScore),
                SeverityLabel = GetLocalizedSeverity(entry.Severity),
                CvssLabel = $"CVSS {entry.CvssScore:F1}",
                AffectedLabel = affectedLabel,
            });
        }

        // Sort by CVSS descending
        matches.Sort((a, b) => b.CvssScore.CompareTo(a.CvssScore));
        return matches;
    }

    // ── Banner parsing ───────────────────────────────────────────────

    private static (string Software, string Version)? ParseBanner(string input)
    {
        foreach (var (pattern, software) in BannerPatterns)
        {
            var match = pattern.Match(input);
            if (match.Success)
            {
                var version = match.Groups.Count > 1 && match.Groups[1].Success
                    ? NormalizeVersion(match.Groups[1].Value)
                    : "";
                return (software, version);
            }
        }

        return null;
    }

    /// <summary>
    /// Strips non-version suffixes like "p1" from OpenSSH versions for comparison
    /// while keeping the numeric segments. "8.9p1" becomes "8.9".
    /// </summary>
    private static string NormalizeVersion(string raw)
    {
        // Keep only leading numeric segments separated by dots
        var sb = new StringBuilder();
        bool lastWasDot = false;

        foreach (var c in raw)
        {
            if (char.IsDigit(c))
            {
                sb.Append(c);
                lastWasDot = false;
            }
            else if (c == '.' && !lastWasDot && sb.Length > 0)
            {
                sb.Append('.');
                lastWasDot = true;
            }
            else
            {
                break;
            }
        }

        // Trim trailing dot
        var result = sb.ToString().TrimEnd('.');
        return result;
    }

    // ── Version comparison helpers ───────────────────────────────────

    private static bool VersionBelow(string version, string threshold)
    {
        var v = ParseVersion(version);
        var t = ParseVersion(threshold);
        return v.Length > 0 && t.Length > 0 && CompareVersions(v, t) < 0;
    }

    private static bool VersionInRange(string version, string from, string to)
    {
        var v = ParseVersion(version);
        var f = ParseVersion(from);
        var t = ParseVersion(to);
        return v.Length > 0 && f.Length > 0 && t.Length > 0
            && CompareVersions(v, f) >= 0 && CompareVersions(v, t) <= 0;
    }

    private static bool VersionEquals(string version, string target)
    {
        var v = ParseVersion(version);
        var t = ParseVersion(target);
        return v.Length > 0 && t.Length > 0 && CompareVersions(v, t) == 0;
    }

    /// <summary>
    /// Parses a version string like "2.4.49" or "8.9" into an integer array.
    /// Non-numeric segments are skipped; "8.9p1" normalizes to [8, 9].
    /// </summary>
    private static int[] ParseVersion(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return [];

        var normalized = NormalizeVersion(v);
        if (string.IsNullOrEmpty(normalized)) return [];

        var parts = normalized.Split('.');
        var result = new List<int>(parts.Length);

        foreach (var part in parts)
        {
            if (int.TryParse(part, out var n))
            {
                result.Add(n);
            }
        }

        return [.. result];
    }

    private static int CompareVersions(int[] a, int[] b)
    {
        var maxLen = Math.Max(a.Length, b.Length);
        for (int i = 0; i < maxLen; i++)
        {
            var va = i < a.Length ? a[i] : 0;
            var vb = i < b.Length ? b[i] : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        return 0;
    }

    // ── Severity coloring ────────────────────────────────────────────

    private Brush GetSeverityBrush(double cvss) => cvss switch
    {
        >= 9.0 => FindBrush("ErrorBrush"),
        >= 7.0 => FindBrush("WarningBrush"),
        >= 4.0 => FindBrush("WarningTextBrush"),
        _ => FindBrush("SuccessBrush"),
    };

    private Brush GetCvssBrush(double cvss) => GetSeverityBrush(cvss);

    private string GetLocalizedSeverity(string severity) => severity switch
    {
        "Critical" => L("ToolCveSeverityCritical"),
        "High" => L("ToolCveSeverityHigh"),
        "Medium" => L("ToolCveSeverityMedium"),
        "Low" => L("ToolCveSeverityLow"),
        _ => severity,
    };

    // ── UI state management ──────────────────────────────────────────

    private void ShowEmptyState()
    {
        EmptyStatePanel.Visibility = Visibility.Visible;
        NoResultsPanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        BtnCopy.IsEnabled = false;
        _lastResults = [];
    }

    private void ShowNoResults()
    {
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        NoResultsPanel.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Collapsed;
        BtnCopy.IsEnabled = false;
    }

    private void ShowResults(List<CveMatch> results)
    {
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        NoResultsPanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Visible;
        BtnCopy.IsEnabled = true;

        TxtSummary.Text = string.Format(L("ToolCveSummary"), results.Count, _lastQuery);
        ResultsList.ItemsSource = results;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private Brush FindBrush(string key)
    {
        return TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    private string L(string key) => _localizer?[key] ?? key;

    // ── IDisposable ──────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
