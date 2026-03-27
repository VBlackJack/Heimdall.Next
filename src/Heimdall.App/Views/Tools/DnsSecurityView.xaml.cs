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

using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Security;
using Heimdall.Ssh;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// DNS Security Checker tool that evaluates a domain's email and DNS security
/// posture by inspecting SPF, DKIM, DMARC, CAA, DNSSEC, and MX records.
/// </summary>
public partial class DnsSecurityView : UserControl, IToolView
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Common DKIM selectors to probe when no specific selector is known.</summary>
    private static readonly string[] DkimSelectors =
        ["default", "google", "selector1", "selector2"];

    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isChecking;
    private Action<bool>? _setBusy;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;
    private string _lastReport = string.Empty;

    public DnsSecurityView()
    {
        InitializeComponent();
        TxtDomain.KeyDown += OnDomainKeyDown;
    }

    // ── IToolView ────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the tool with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _setBusy = context?.SetBusyAction;
        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtDomain.Text = context.TargetHost;
        }

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            TxtDomain.Text = context.Argument;
        }

        // Populate SSH gateway selector for tunnel-based queries
        if (context?.SshGateways is IList gateways)
        {
            _gateways = gateways.Cast<SshGatewayDto>().ToList();
        }
        PopulateRouteSelector();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtDomain.Focus();
            TxtDomain.SelectAll();
        });
    }

    public bool CanClose() => !_isChecking;

    // ── Localization ─────────────────────────────────────────────────

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolDnsSecTitle");
        LblDomain.Text = L("ToolDnsSecDomain");
        BtnCheck.Content = L("ToolDnsSecBtnCheck");
        LblRouteVia.Text = L("ToolTunnelRouteVia");
        TxtEmptyState.Text = L("ToolDnsSecEmptyState");
        BtnCopyReport.Content = L("ToolDnsSecBtnCopy");
        BtnCopyReport.ToolTip = L("ToolBtnCopyToClipboard");
        TxtStatus.Text = string.Empty;

        AutomationProperties.SetName(TxtDomain, L("ToolDnsSecDomain"));
        AutomationProperties.SetName(BtnCheck, L("ToolDnsSecBtnCheck"));
        AutomationProperties.SetName(BtnCopyReport, L("ToolDnsSecBtnCopy"));
        AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));

        TxtDomain.Tag = L("ToolWatermarkExampleDomain");
    }

    // ── Gateway routing ──────────────────────────────────────────────

    private void PopulateRouteSelector()
    {
        CmbRouteVia.Items.Clear();
        CmbRouteVia.Items.Add(new ComboBoxItem { Content = L("ToolTunnelDirect") });

        if (_gateways is not null)
        {
            foreach (var gw in _gateways)
            {
                var label = $"{gw.Name} ({gw.Host}:{gw.Port})";
                CmbRouteVia.Items.Add(new ComboBoxItem { Content = label, Tag = gw });
            }
        }

        CmbRouteVia.SelectedIndex = 0;
    }

    private void OnRouteViaChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbRouteVia.SelectedItem is ComboBoxItem item && item.Tag is SshGatewayDto gw)
        {
            _selectedGateway = gw;
        }
        else
        {
            _selectedGateway = null;
        }
    }

    // ── Event handlers ───────────────────────────────────────────────

    private void OnDomainKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = PerformCheckAsync();
            e.Handled = true;
        }
    }

    private void OnCheckClick(object sender, RoutedEventArgs e)
    {
        _ = PerformCheckAsync();
    }

    // ── Core check flow ──────────────────────────────────────────────

    private async Task PerformCheckAsync()
    {
        var domain = TxtDomain.Text.Trim().ToLowerInvariant();
        TxtError.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Visible;
        TxtStatus.Text = string.Empty;
        _lastReport = string.Empty;

        if (string.IsNullOrWhiteSpace(domain))
        {
            TxtError.Text = L("ToolValidationHostRequired");
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        // Strip protocol prefix if user pasted a URL
        if (domain.Contains("://"))
        {
            domain = domain[(domain.IndexOf("://", StringComparison.Ordinal) + 3)..];
        }
        domain = domain.TrimEnd('/').Split('/')[0].Split(':')[0];

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(QueryTimeout);

        _isChecking = true;
        _setBusy?.Invoke(true);
        BtnCheck.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        TxtStatus.Text = L("ToolTunnelConnecting");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var results = await RunAllChecksAsync(domain, _cts.Token);
            stopwatch.Stop();

            if (_cts.IsCancellationRequested) return;

            DisplayResults(results, domain, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            TxtError.Text = L("ToolDnsErrorTimeout");
            TxtError.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            TxtError.Text = string.Format(L("ToolDnsErrorLookupFailed"), ex.Message);
            TxtError.Visibility = Visibility.Visible;
        }
        finally
        {
            _isChecking = false;
            _setBusy?.Invoke(false);
            BtnCheck.IsEnabled = true;
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    // ── DNS queries ──────────────────────────────────────────────────

    /// <summary>
    /// Runs all six DNS security checks in parallel.
    /// </summary>
    private async Task<List<DnsCheckResult>> RunAllChecksAsync(string domain, CancellationToken ct)
    {
        var spfTask = CheckSpfAsync(domain, ct);
        var dkimTask = CheckDkimAsync(domain, ct);
        var dmarcTask = CheckDmarcAsync(domain, ct);
        var caaTask = CheckCaaAsync(domain, ct);
        var dnssecTask = CheckDnssecAsync(domain, ct);
        var mxTask = CheckMxAsync(domain, ct);

        await Task.WhenAll(spfTask, dkimTask, dmarcTask, caaTask, dnssecTask, mxTask);

        return
        [
            await spfTask,
            await dkimTask,
            await dmarcTask,
            await caaTask,
            await dnssecTask,
            await mxTask
        ];
    }

    /// <summary>
    /// Queries DNS records via local nslookup process or via an SSH gateway.
    /// </summary>
    private async Task<string> QueryDnsAsync(string type, string domain, CancellationToken ct)
    {
        if (_selectedGateway is not null)
        {
            return await QueryDnsViaTunnelAsync(_selectedGateway, type, domain, ct);
        }

        return await QueryDnsLocalAsync(type, domain, ct);
    }

    /// <summary>
    /// Performs a local DNS query using nslookup.
    /// </summary>
    private static async Task<string> QueryDnsLocalAsync(string type, string domain, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "nslookup",
            Arguments = $"-type={type} {domain}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        var output = await outputTask;
        var error = await errorTask;

        // Combine output and error: nslookup sometimes writes to stderr
        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    /// <summary>
    /// Performs a DNS query remotely via an SSH gateway using dig (preferred) or nslookup.
    /// </summary>
    private static async Task<string> QueryDnsViaTunnelAsync(
        SshGatewayDto gateway, string type, string domain, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var client = ToolGatewayConnector.Connect(gateway);
            try
            {
                // Try dig first (more structured output)
                var digCommand = $"dig {type} {domain} +short 2>/dev/null";
                using var digCmd = client.CreateCommand(digCommand);
                digCmd.CommandTimeout = TimeSpan.FromSeconds(8);
                var digResult = digCmd.Execute()?.Trim();

                if (!string.IsNullOrWhiteSpace(digResult))
                {
                    return digResult;
                }

                // Fall back to nslookup
                using var nsCmd = client.CreateCommand($"nslookup -type={type} {domain} 2>&1");
                nsCmd.CommandTimeout = TimeSpan.FromSeconds(8);
                return nsCmd.Execute()?.Trim() ?? string.Empty;
            }
            finally
            {
                try { client.Disconnect(); } catch { /* best effort */ }
            }
        }, ct).ConfigureAwait(false);
    }

    // ── Individual checks ────────────────────────────────────────────

    private async Task<DnsCheckResult> CheckSpfAsync(string domain, CancellationToken ct)
    {
        var name = L("ToolDnsSecSpf");
        try
        {
            var raw = await QueryDnsAsync("TXT", domain, ct);
            var spfRecord = ExtractRecord(raw, "v=spf1");

            if (string.IsNullOrEmpty(spfRecord))
            {
                return MakeResult(name, "fail", L("ToolDnsSecNoRecord"), L("ToolDnsSecFail"));
            }

            if (spfRecord.Contains("+all", StringComparison.OrdinalIgnoreCase))
            {
                return MakeResult(name, "warn", spfRecord, L("ToolDnsSecSpfPermissive"));
            }

            return MakeResult(name, "pass", spfRecord, L("ToolDnsSecSpfGood"));
        }
        catch (Exception ex)
        {
            return MakeResult(name, "fail", ex.Message, string.Empty);
        }
    }

    private async Task<DnsCheckResult> CheckDkimAsync(string domain, CancellationToken ct)
    {
        var name = L("ToolDnsSecDkim");
        try
        {
            foreach (var selector in DkimSelectors)
            {
                var dkimDomain = $"{selector}._domainkey.{domain}";
                var raw = await QueryDnsAsync("TXT", dkimDomain, ct);
                var dkimRecord = ExtractRecord(raw, "v=DKIM1");

                if (!string.IsNullOrEmpty(dkimRecord))
                {
                    return MakeResult(name, "pass", $"[{selector}] {dkimRecord}",
                        string.Format(L("ToolDnsSecDmarcEnforced"), selector));
                }
            }

            return MakeResult(name, "fail", L("ToolDnsSecNoRecord"), L("ToolDnsSecNoDkim"));
        }
        catch (Exception ex)
        {
            return MakeResult(name, "fail", ex.Message, string.Empty);
        }
    }

    private async Task<DnsCheckResult> CheckDmarcAsync(string domain, CancellationToken ct)
    {
        var name = L("ToolDnsSecDmarc");
        try
        {
            var dmarcDomain = $"_dmarc.{domain}";
            var raw = await QueryDnsAsync("TXT", dmarcDomain, ct);
            var dmarcRecord = ExtractRecord(raw, "v=DMARC1");

            if (string.IsNullOrEmpty(dmarcRecord))
            {
                return MakeResult(name, "fail", L("ToolDnsSecNoRecord"), L("ToolDnsSecFail"));
            }

            var policy = ExtractTag(dmarcRecord, "p");
            if (string.IsNullOrEmpty(policy) ||
                policy.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return MakeResult(name, "warn", dmarcRecord, L("ToolDnsSecDmarcNone"));
            }

            return MakeResult(name, "pass", dmarcRecord,
                string.Format(L("ToolDnsSecDmarcEnforced"), policy));
        }
        catch (Exception ex)
        {
            return MakeResult(name, "fail", ex.Message, string.Empty);
        }
    }

    private async Task<DnsCheckResult> CheckCaaAsync(string domain, CancellationToken ct)
    {
        var name = L("ToolDnsSecCaa");
        try
        {
            var raw = await QueryDnsAsync("CAA", domain, ct);
            var records = ParseCaaRecords(raw);

            if (records.Count == 0)
            {
                return MakeResult(name, "fail", L("ToolDnsSecNoRecord"), L("ToolDnsSecNoCaa"));
            }

            var issuers = string.Join(", ", records);
            return MakeResult(name, "pass", issuers,
                string.Format(L("ToolDnsSecCaaPresent"), issuers));
        }
        catch (Exception ex)
        {
            return MakeResult(name, "fail", ex.Message, string.Empty);
        }
    }

    private async Task<DnsCheckResult> CheckDnssecAsync(string domain, CancellationToken ct)
    {
        var name = L("ToolDnsSecDnssec");
        try
        {
            // Query DNSKEY: presence indicates DNSSEC is configured
            var raw = await QueryDnsAsync("DNSKEY", domain, ct);

            // Also try RRSIG as a fallback indicator
            if (string.IsNullOrWhiteSpace(raw) || !ContainsDnsRecords(raw, "DNSKEY"))
            {
                raw = await QueryDnsAsync("RRSIG", domain, ct);
                if (!string.IsNullOrWhiteSpace(raw) && ContainsDnsRecords(raw, "RRSIG"))
                {
                    return MakeResult(name, "pass", raw.Trim(), L("ToolDnsSecDnssecPresent"));
                }

                return MakeResult(name, "fail", L("ToolDnsSecNoRecord"), L("ToolDnsSecDnssecMissing"));
            }

            return MakeResult(name, "pass", raw.Trim(), L("ToolDnsSecDnssecPresent"));
        }
        catch (Exception ex)
        {
            return MakeResult(name, "fail", ex.Message, string.Empty);
        }
    }

    private async Task<DnsCheckResult> CheckMxAsync(string domain, CancellationToken ct)
    {
        var name = L("ToolDnsSecMx");
        try
        {
            var raw = await QueryDnsAsync("MX", domain, ct);
            var servers = ParseMxRecords(raw);

            if (servers.Count == 0)
            {
                return MakeResult(name, "fail", L("ToolDnsSecNoRecord"), L("ToolDnsSecNoMx"));
            }

            var serverList = string.Join(", ", servers);
            return MakeResult(name, "pass", serverList,
                string.Format(L("ToolDnsSecMxServers"), serverList));
        }
        catch (Exception ex)
        {
            return MakeResult(name, "fail", ex.Message, string.Empty);
        }
    }

    // ── Parsing helpers ──────────────────────────────────────────────

    /// <summary>
    /// Extracts a TXT record value containing the specified marker from raw nslookup/dig output.
    /// </summary>
    private static string ExtractRecord(string raw, string marker)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // dig +short returns quoted strings, one per line
        // nslookup returns multi-line output with "text =" or quoted values
        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim().Trim('"');

            // Handle nslookup "text = ..." format
            var textIdx = line.IndexOf("text =", StringComparison.OrdinalIgnoreCase);
            if (textIdx >= 0)
            {
                line = line[(textIdx + 6)..].Trim().Trim('"');
            }

            if (line.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Extracts a tag value from a DMARC/SPF record (e.g., "p=reject" returns "reject").
    /// </summary>
    private static string ExtractTag(string record, string tag)
    {
        var parts = record.Split(';', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith(tag + "=", StringComparison.OrdinalIgnoreCase))
            {
                return part[(tag.Length + 1)..].Trim();
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Parses CAA record values from raw DNS output.
    /// </summary>
    private static List<string> ParseCaaRecords(string raw)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return results;

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim();

            // dig +short: 0 issue "letsencrypt.org"
            // nslookup: domain CAA 0 issue "letsencrypt.org"
            if (line.Contains("issue", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("issuewild", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("iodef", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(value) && !results.Contains(value))
                {
                    results.Add(value);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Parses MX record values (mail server hostnames) from raw DNS output.
    /// </summary>
    private static List<string> ParseMxRecords(string raw)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return results;

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim();

            // dig +short: "10 mail.example.com."
            // nslookup: "mail exchanger = 10 mail.example.com."
            var mxIdx = line.IndexOf("mail exchanger", StringComparison.OrdinalIgnoreCase);
            if (mxIdx >= 0)
            {
                var eqIdx = line.IndexOf('=', mxIdx);
                if (eqIdx >= 0)
                {
                    line = line[(eqIdx + 1)..].Trim();
                }
            }

            // Extract hostname from "priority hostname" format
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out _))
            {
                var server = parts[1].TrimEnd('.');
                if (!string.IsNullOrWhiteSpace(server) && !results.Contains(server))
                {
                    results.Add(server);
                }
            }
            else if (parts.Length == 1 && line.Contains('.') && !line.Contains(' '))
            {
                // Single hostname from dig +short
                var server = parts[0].TrimEnd('.');
                if (!string.IsNullOrWhiteSpace(server) && !results.Contains(server))
                {
                    results.Add(server);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Checks whether raw DNS output contains actual record data for the given type,
    /// filtering out nslookup header noise and error messages.
    /// </summary>
    private static bool ContainsDnsRecords(string raw, string recordType)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Skip nslookup header lines
            if (line.StartsWith("Server:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Address:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Non-authoritative", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("***", StringComparison.Ordinal) ||
                line.StartsWith(";;", StringComparison.Ordinal))
            {
                continue;
            }

            // For dig +short, any non-empty line is a record
            // For nslookup, look for the record type in the output
            if (line.Contains(recordType, StringComparison.OrdinalIgnoreCase) ||
                line.Length > 10) // heuristic: real records are typically longer
            {
                return true;
            }
        }

        return false;
    }

    // ── Result builder ───────────────────────────────────────────────

    private DnsCheckResult MakeResult(string name, string status, string value, string detail)
    {
        var (brush, icon, label) = status switch
        {
            "pass" => (FindGreenBrush(), "\u2713", L("ToolDnsSecPass")),
            "warn" => (FindYellowBrush(), "\u26A0", L("ToolDnsSecWarn")),
            _ => (FindRedBrush(), "\u2717", L("ToolDnsSecFail"))
        };

        return new DnsCheckResult
        {
            Name = name,
            Status = status,
            StatusBrush = brush,
            StatusIcon = icon,
            StatusLabel = label,
            Value = value,
            ValueVisibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible,
            Detail = detail,
            DetailVisibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible
        };
    }

    // ── Display ──────────────────────────────────────────────────────

    private void DisplayResults(List<DnsCheckResult> results, string domain, long elapsedMs)
    {
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Visible;

        var passCount = results.Count(r => r.Status == "pass");
        var total = results.Count;

        // Summary badge
        TxtSummary.Text = string.Format(L("ToolDnsSecSummary"), passCount, total);
        SummaryBanner.Background = GetSummaryBrush(passCount, total);

        // Check results
        CheckResultsList.ItemsSource = results;

        // Status bar
        TxtStatus.Text = string.Format(L("ToolDnsStatusComplete"), elapsedMs);

        // Build text report for copy
        _lastReport = BuildTextReport(domain, passCount, total, results);
    }

    private static Brush GetSummaryBrush(int passCount, int total)
    {
        var ratio = (double)passCount / total;
        return ratio switch
        {
            >= 1.0 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 139, 34)),   // Forest green
            >= 0.66 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 139, 87)),  // Sea green
            >= 0.33 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 165, 32)), // Goldenrod
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69))         // Red
        };
    }

    private static string BuildTextReport(
        string domain, int passCount, int total, List<DnsCheckResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DNS Security Report: {domain}");
        sb.AppendLine($"Score: {passCount} / {total} checks passed");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        foreach (var r in results)
        {
            sb.AppendLine($"{r.StatusIcon} {r.Name}");
            if (r.ValueVisibility == Visibility.Visible && !string.IsNullOrEmpty(r.Value))
            {
                sb.AppendLine($"  Record: {r.Value}");
            }
            if (r.DetailVisibility == Visibility.Visible && !string.IsNullOrEmpty(r.Detail))
            {
                sb.AppendLine($"  Detail: {r.Detail}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ── Actions ──────────────────────────────────────────────────────

    private void OnCopyReportClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastReport))
        {
            try
            {
                Clipboard.SetText(_lastReport);
                CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"DnsSecurity clipboard copy failed: {ex.Message}");
            }
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpDNSSEC");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Theme brush helpers ──────────────────────────────────────────

    private Brush FindGreenBrush()
        => TryFindResource("SuccessTextBrush") as Brush ?? Brushes.Green;

    private Brush FindYellowBrush()
        => TryFindResource("WarningTextBrush") as Brush ?? Brushes.Orange;

    private Brush FindRedBrush()
        => TryFindResource("ErrorTextBrush") as Brush ?? Brushes.Red;

    private string L(string key) => _localizer?[key] ?? key;

    // ── Lifecycle ────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _setBusy?.Invoke(false);
        GC.SuppressFinalize(this);
    }
}

// ── Data model for template binding ──────────────────────────────────

/// <summary>
/// Represents the evaluation result for a single DNS security check.
/// </summary>
public sealed class DnsCheckResult
{
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public Brush StatusBrush { get; init; } = Brushes.Transparent;
    public string StatusIcon { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public Visibility ValueVisibility { get; init; } = Visibility.Visible;
    public string Detail { get; init; } = string.Empty;
    public Visibility DetailVisibility { get; init; } = Visibility.Visible;
}
