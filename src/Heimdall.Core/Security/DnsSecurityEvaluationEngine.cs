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

using System.Globalization;
using System.Text;

namespace Heimdall.Core.Security;

/// <summary>
/// Pure, I/O-free evaluator for the six DNS security controls (SPF, DKIM, DMARC,
/// CAA, DNSSEC, MX). Parses raw <c>dig +short</c> or <c>nslookup</c> output and
/// classifies each domain's posture. Locale-neutral: localization keys travel on
/// <see cref="DnsCheckResult.DetailKey"/>; only <see cref="BuildReport"/> consumes
/// a <see cref="Func{T, TResult}"/> localizer to emit the clipboard text.
/// </summary>
public static class DnsSecurityEvaluationEngine
{
    private const string IconPass = "\u2713";
    private const string IconWarn = "\u26A0";
    private const string IconFail = "\u2717";

    /// <summary>
    /// Common DKIM selectors probed when no explicit selector is known.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultDkimSelectors =
        new[] { "default", "google", "selector1", "selector2" };

    // ── Normalization ────────────────────────────────────────────────

    /// <summary>
    /// Normalizes raw user input into a canonical lowercase domain name by
    /// stripping whitespace, an optional scheme (<c>http://</c>, <c>https://</c>, ...),
    /// an optional email local part (<c>user@domain</c> → <c>domain</c>), and any
    /// trailing path or port segment. Returns the empty string when the input has
    /// no domain-shaped content.
    /// </summary>
    public static string NormalizeDomainInput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim().ToLowerInvariant();

        var schemeIdx = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
        {
            value = value[(schemeIdx + 3)..];
        }

        var atIdx = value.LastIndexOf('@');
        if (atIdx >= 0)
        {
            value = value[(atIdx + 1)..];
        }

        value = value.TrimEnd('/');
        var slashIdx = value.IndexOf('/');
        if (slashIdx >= 0)
        {
            value = value[..slashIdx];
        }

        var questionIdx = value.IndexOf('?');
        if (questionIdx >= 0)
        {
            value = value[..questionIdx];
        }

        var colonIdx = value.IndexOf(':');
        if (colonIdx >= 0)
        {
            value = value[..colonIdx];
        }

        return value.Trim();
    }

    // ── Parsing helpers ──────────────────────────────────────────────

    /// <summary>
    /// Extracts the first TXT record line containing the supplied marker from raw
    /// DNS output (<c>dig +short</c> or <c>nslookup</c> format).
    /// </summary>
    public static string ExtractRecord(string? raw, string marker)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrEmpty(marker))
        {
            return string.Empty;
        }

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim().Trim('"');

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
    /// Extracts the value assigned to a single tag inside a semicolon-delimited
    /// DMARC or SPF record (for example <c>p=reject</c> returns <c>reject</c>).
    /// </summary>
    public static string ExtractTag(string? record, string tag)
    {
        if (string.IsNullOrWhiteSpace(record) || string.IsNullOrEmpty(tag))
        {
            return string.Empty;
        }

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
    /// Parses CAA record values (issuer / issuewild / iodef directives) from raw
    /// DNS output, preserving insertion order and dropping duplicates.
    /// </summary>
    public static IReadOnlyList<string> ParseCaaRecords(string? raw)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return results;
        }

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim();

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
    /// Parses MX records (mail server hostnames) from raw DNS output. Handles
    /// both <c>dig +short</c> (<c>"10 mail.example.com."</c>) and <c>nslookup</c>
    /// (<c>"mail exchanger = 10 mail.example.com."</c>) formats, strips trailing
    /// dots, and removes duplicates.
    /// </summary>
    public static IReadOnlyList<string> ParseMxRecords(string? raw)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return results;
        }

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim();

            var mxIdx = line.IndexOf("mail exchanger", StringComparison.OrdinalIgnoreCase);
            if (mxIdx >= 0)
            {
                var eqIdx = line.IndexOf('=', mxIdx);
                if (eqIdx >= 0)
                {
                    line = line[(eqIdx + 1)..].Trim();
                }
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                var server = parts[1].TrimEnd('.');
                if (!string.IsNullOrWhiteSpace(server) && !results.Contains(server))
                {
                    results.Add(server);
                }
            }
            else if (parts.Length == 1 && line.Contains('.') && !line.Contains(' '))
            {
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
    /// Returns <c>true</c> when the raw DNS output contains at least one real
    /// record for <paramref name="recordType"/>, filtering out <c>nslookup</c>
    /// header noise and empty responses.
    /// </summary>
    public static bool ContainsDnsRecords(string? raw, string recordType)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrEmpty(recordType))
        {
            return false;
        }

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("Server:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Address:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Non-authoritative", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("***", StringComparison.Ordinal) ||
                line.StartsWith(";;", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains(recordType, StringComparison.OrdinalIgnoreCase) ||
                line.Length > 10)
            {
                return true;
            }
        }

        return false;
    }

    // ── Evaluators ───────────────────────────────────────────────────

    /// <summary>
    /// Evaluates an SPF TXT response by parsing the SPF <c>all</c> mechanism
    /// qualifier. Missing records fail; permissive, neutral, or incomplete
    /// policies warn; hardfail, softfail, or redirect-backed records pass.
    /// </summary>
    public static DnsCheckResult EvaluateSpf(string? raw)
    {
        string spf = ExtractRecord(raw, "v=spf1");
        if (string.IsNullOrEmpty(spf))
        {
            return new DnsCheckResult(
                DnsCheckKind.Spf,
                DnsCheckStatus.Fail,
                string.Empty,
                "ToolDnsSecFail",
                Array.Empty<string>());
        }

        if (TryGetSpfAllQualifier(spf, out char qualifier))
        {
            if (qualifier == '+' || qualifier == '?')
            {
                string detailKey = qualifier == '+'
                    ? "ToolDnsSecSpfPermissive"
                    : "ToolDnsSecSpfNeutral";

                return new DnsCheckResult(
                    DnsCheckKind.Spf,
                    DnsCheckStatus.Warn,
                    spf,
                    detailKey,
                    Array.Empty<string>());
            }

            return new DnsCheckResult(
                DnsCheckKind.Spf,
                DnsCheckStatus.Pass,
                spf,
                "ToolDnsSecSpfGood",
                Array.Empty<string>());
        }

        if (ContainsSpfRedirectModifier(spf))
        {
            return new DnsCheckResult(
                DnsCheckKind.Spf,
                DnsCheckStatus.Pass,
                spf,
                "ToolDnsSecSpfGood",
                Array.Empty<string>());
        }

        return new DnsCheckResult(
            DnsCheckKind.Spf,
            DnsCheckStatus.Warn,
            spf,
            "ToolDnsSecSpfNoAll",
            Array.Empty<string>());
    }

    private static bool TryGetSpfAllQualifier(string spf, out char qualifier)
    {
        qualifier = '\0';
        int tokenStart = 0;

        while (tokenStart < spf.Length)
        {
            while (tokenStart < spf.Length && char.IsWhiteSpace(spf[tokenStart]))
            {
                tokenStart++;
            }

            int tokenEnd = tokenStart;
            while (tokenEnd < spf.Length && !char.IsWhiteSpace(spf[tokenEnd]))
            {
                tokenEnd++;
            }

            ReadOnlySpan<char> token = spf.AsSpan(tokenStart, tokenEnd - tokenStart);
            if (IsSpfAllMechanism(token, out char tokenQualifier))
            {
                qualifier = tokenQualifier;
                return true;
            }

            tokenStart = tokenEnd + 1;
        }

        return false;
    }

    private static bool IsSpfAllMechanism(ReadOnlySpan<char> token, out char qualifier)
    {
        qualifier = '\0';

        if (token.Length == 3 && token.Equals("all".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            qualifier = '+';
            return true;
        }

        if (token.Length == 4 &&
            IsSpfAllQualifier(token[0]) &&
            token[1..].Equals("all".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            qualifier = token[0];
            return true;
        }

        return false;
    }

    private static bool IsSpfAllQualifier(char qualifier)
    {
        return qualifier == '-' || qualifier == '~' || qualifier == '?' || qualifier == '+';
    }

    private static bool ContainsSpfRedirectModifier(string spf)
    {
        int tokenStart = 0;

        while (tokenStart < spf.Length)
        {
            while (tokenStart < spf.Length && char.IsWhiteSpace(spf[tokenStart]))
            {
                tokenStart++;
            }

            int tokenEnd = tokenStart;
            while (tokenEnd < spf.Length && !char.IsWhiteSpace(spf[tokenEnd]))
            {
                tokenEnd++;
            }

            ReadOnlySpan<char> token = spf.AsSpan(tokenStart, tokenEnd - tokenStart);
            if (token.StartsWith("redirect=".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            tokenStart = tokenEnd + 1;
        }

        return false;
    }

    /// <summary>
    /// Evaluates a DKIM TXT response for the supplied selector. Missing records
    /// fail; records with a missing or empty <c>p</c> public-key tag warn because
    /// the key is revoked or invalid; records with a public key pass.
    /// </summary>
    /// <param name="selector">
    /// Selector that produced <paramref name="rawResponse"/>, or <c>null</c>/empty
    /// when no hit was found after iterating all selectors.
    /// </param>
    /// <param name="rawResponse">
    /// Raw <c>dig +short</c> or <c>nslookup</c> response for the
    /// <c>{selector}._domainkey.{domain}</c> TXT query.
    /// </param>
    public static DnsCheckResult EvaluateDkim(string? selector, string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(selector) || string.IsNullOrWhiteSpace(rawResponse))
        {
            return new DnsCheckResult(
                DnsCheckKind.Dkim,
                DnsCheckStatus.Fail,
                string.Empty,
                "ToolDnsSecNoDkim",
                Array.Empty<string>());
        }

        string dkim = ExtractRecord(rawResponse, "v=DKIM1");
        if (string.IsNullOrEmpty(dkim))
        {
            return new DnsCheckResult(
                DnsCheckKind.Dkim,
                DnsCheckStatus.Fail,
                string.Empty,
                "ToolDnsSecNoDkim",
                Array.Empty<string>());
        }

        string publicKey = ExtractTag(dkim, "p");
        if (string.IsNullOrEmpty(publicKey))
        {
            return new DnsCheckResult(
                DnsCheckKind.Dkim,
                DnsCheckStatus.Warn,
                $"[{selector}] {dkim}",
                "ToolDnsSecDkimRevoked",
                new[] { selector });
        }

        return new DnsCheckResult(
            DnsCheckKind.Dkim,
            DnsCheckStatus.Pass,
            $"[{selector}] {dkim}",
            "ToolDnsSecDkimFound",
            new[] { selector });
    }

    /// <summary>
    /// Evaluates a DMARC TXT response. Missing record fails; <c>p=none</c> (or
    /// missing <c>p</c> tag) warns; enforcing policies with <c>pct=0</c> warn
    /// because no messages are filtered; any other enforcement policy passes.
    /// </summary>
    public static DnsCheckResult EvaluateDmarc(string? raw)
    {
        string dmarc = ExtractRecord(raw, "v=DMARC1");
        if (string.IsNullOrEmpty(dmarc))
        {
            return new DnsCheckResult(
                DnsCheckKind.Dmarc,
                DnsCheckStatus.Fail,
                string.Empty,
                "ToolDnsSecFail",
                Array.Empty<string>());
        }

        string policy = ExtractTag(dmarc, "p");
        if (string.IsNullOrEmpty(policy) ||
            policy.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return new DnsCheckResult(
                DnsCheckKind.Dmarc,
                DnsCheckStatus.Warn,
                dmarc,
                "ToolDnsSecDmarcNone",
                Array.Empty<string>());
        }

        string pct = ExtractTag(dmarc, "pct");
        if (!string.IsNullOrEmpty(pct) &&
            int.TryParse(pct, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pctValue) &&
            pctValue == 0)
        {
            return new DnsCheckResult(
                DnsCheckKind.Dmarc,
                DnsCheckStatus.Warn,
                dmarc,
                "ToolDnsSecDmarcPctZero",
                new[] { policy });
        }

        return new DnsCheckResult(
            DnsCheckKind.Dmarc,
            DnsCheckStatus.Pass,
            dmarc,
            "ToolDnsSecDmarcEnforced",
            new[] { policy });
    }

    /// <summary>
    /// Evaluates a CAA response. At least one <c>issue</c>, <c>issuewild</c>, or
    /// <c>iodef</c> directive is required to pass.
    /// </summary>
    public static DnsCheckResult EvaluateCaa(string? raw)
    {
        var records = ParseCaaRecords(raw);
        if (records.Count == 0)
        {
            return new DnsCheckResult(
                DnsCheckKind.Caa,
                DnsCheckStatus.Fail,
                string.Empty,
                "ToolDnsSecNoCaa",
                Array.Empty<string>());
        }

        var issuers = string.Join(", ", records);
        return new DnsCheckResult(
            DnsCheckKind.Caa,
            DnsCheckStatus.Pass,
            issuers,
            "ToolDnsSecCaaPresent",
            new[] { issuers });
    }

    /// <summary>
    /// Evaluates DNSSEC presence from raw DNSKEY and (fallback) RRSIG responses.
    /// A hit on either indicates DNSSEC is configured; both absent fails.
    /// </summary>
    public static DnsCheckResult EvaluateDnssec(string? dnskeyRaw, string? rrsigRaw)
    {
        if (ContainsDnsRecords(dnskeyRaw, "DNSKEY"))
        {
            return new DnsCheckResult(
                DnsCheckKind.Dnssec,
                DnsCheckStatus.Pass,
                (dnskeyRaw ?? string.Empty).Trim(),
                "ToolDnsSecDnssecPresent",
                Array.Empty<string>());
        }

        if (ContainsDnsRecords(rrsigRaw, "RRSIG"))
        {
            return new DnsCheckResult(
                DnsCheckKind.Dnssec,
                DnsCheckStatus.Pass,
                (rrsigRaw ?? string.Empty).Trim(),
                "ToolDnsSecDnssecPresent",
                Array.Empty<string>());
        }

        return new DnsCheckResult(
            DnsCheckKind.Dnssec,
            DnsCheckStatus.Fail,
            string.Empty,
            "ToolDnsSecDnssecMissing",
            Array.Empty<string>());
    }

    /// <summary>
    /// Evaluates an MX response. At least one mail exchanger must be present to pass.
    /// </summary>
    public static DnsCheckResult EvaluateMx(string? raw)
    {
        var servers = ParseMxRecords(raw);
        if (servers.Count == 0)
        {
            return new DnsCheckResult(
                DnsCheckKind.Mx,
                DnsCheckStatus.Fail,
                string.Empty,
                "ToolDnsSecNoMx",
                Array.Empty<string>());
        }

        var serverList = string.Join(", ", servers);
        return new DnsCheckResult(
            DnsCheckKind.Mx,
            DnsCheckStatus.Pass,
            serverList,
            "ToolDnsSecMxServers",
            new[] { serverList });
    }

    // ── Error / summary / report helpers ─────────────────────────────

    /// <summary>
    /// Builds a <see cref="DnsCheckStatus.Fail"/> result representing an I/O
    /// or protocol error raised while querying <paramref name="kind"/>. The raw
    /// exception message travels on <see cref="DnsCheckResult.RawRecord"/> and no
    /// localized detail is produced.
    /// </summary>
    public static DnsCheckResult BuildErrorResult(DnsCheckKind kind, string? errorMessage)
        => new(
            kind,
            DnsCheckStatus.Fail,
            errorMessage ?? string.Empty,
            string.Empty,
            Array.Empty<string>());

    /// <summary>
    /// Classifies the overall posture using the four thresholds that matched the
    /// original view-layer colouring (1.0 / 0.66 / 0.33 / everything else, with
    /// a total of zero falling into <see cref="DnsSummaryStatus.Bad"/>).
    /// </summary>
    public static DnsSummaryStatus ComputeSummary(int passCount, int total)
    {
        if (total <= 0)
        {
            return DnsSummaryStatus.Bad;
        }

        var ratio = (double)passCount / total;
        return ratio switch
        {
            >= 1.0 => DnsSummaryStatus.AllPass,
            >= 0.66 => DnsSummaryStatus.Good,
            >= 0.33 => DnsSummaryStatus.Partial,
            _ => DnsSummaryStatus.Bad,
        };
    }

    /// <summary>
    /// Builds the aggregate report from an ordered list of check results.
    /// </summary>
    public static DnsSecurityReport BuildReport(string? domain, IReadOnlyList<DnsCheckResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var passCount = 0;
        foreach (var r in results)
        {
            if (r.Status == DnsCheckStatus.Pass)
            {
                passCount++;
            }
        }

        return new DnsSecurityReport(
            domain ?? string.Empty,
            results,
            passCount,
            results.Count,
            ComputeSummary(passCount, results.Count));
    }

    /// <summary>
    /// Renders the clipboard text report using a localizer delegate. The caller
    /// owns every translation: the engine only routes keys through
    /// <paramref name="localize"/> and formats args.
    /// </summary>
    public static string BuildReportText(DnsSecurityReport report, Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localize);

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(CultureInfo.CurrentCulture, localize("ToolDnsSecReportTitle"), report.Domain));
        sb.AppendLine(string.Format(CultureInfo.CurrentCulture, localize("ToolDnsSecReportScore"), report.PassCount, report.Total));
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        foreach (var r in report.Results)
        {
            var icon = StatusToIcon(r.Status);
            var name = localize(KindToDisplayKey(r.Kind));
            sb.AppendLine($"{icon} {name}");

            if (!string.IsNullOrEmpty(r.RawRecord))
            {
                sb.AppendLine($"  {localize("ToolDnsSecReportRecord")}: {r.RawRecord}");
            }

            if (!string.IsNullOrEmpty(r.DetailKey))
            {
                var detail = r.DetailArgs.Count == 0
                    ? localize(r.DetailKey)
                    : string.Format(CultureInfo.CurrentCulture, localize(r.DetailKey), ToObjectArray(r.DetailArgs));
                sb.AppendLine($"  {localize("ToolDnsSecReportDetail")}: {detail}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the Unicode glyph used to represent a check status in plain text.
    /// </summary>
    public static string StatusToIcon(DnsCheckStatus status) => status switch
    {
        DnsCheckStatus.Pass => IconPass,
        DnsCheckStatus.Warn => IconWarn,
        _ => IconFail,
    };

    /// <summary>
    /// Maps a <see cref="DnsCheckKind"/> to the localization key of its display name.
    /// </summary>
    public static string KindToDisplayKey(DnsCheckKind kind) => kind switch
    {
        DnsCheckKind.Spf => "ToolDnsSecSpf",
        DnsCheckKind.Dkim => "ToolDnsSecDkim",
        DnsCheckKind.Dmarc => "ToolDnsSecDmarc",
        DnsCheckKind.Caa => "ToolDnsSecCaa",
        DnsCheckKind.Dnssec => "ToolDnsSecDnssec",
        DnsCheckKind.Mx => "ToolDnsSecMx",
        _ => string.Empty,
    };

    /// <summary>
    /// Maps a <see cref="DnsCheckStatus"/> to the localization key of its label.
    /// </summary>
    public static string StatusToLabelKey(DnsCheckStatus status) => status switch
    {
        DnsCheckStatus.Pass => "ToolDnsSecPass",
        DnsCheckStatus.Warn => "ToolDnsSecWarn",
        _ => "ToolDnsSecFail",
    };

    private static object[] ToObjectArray(IReadOnlyList<string> args)
    {
        var result = new object[args.Count];
        for (var i = 0; i < args.Count; i++)
        {
            result[i] = args[i];
        }

        return result;
    }
}
