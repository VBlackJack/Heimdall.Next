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

using Heimdall.Core.Security;

namespace Heimdall.Core.Tests;

public sealed class DnsSecurityEvaluationEngineTests
{
    private static string Identity(string key) => key;

    // ── NormalizeDomainInput ─────────────────────────────────────────

    [Fact]
    public void NormalizeDomainInput_SimpleDomain_ReturnsLowercase()
    {
        Assert.Equal("example.com", DnsSecurityEvaluationEngine.NormalizeDomainInput("Example.COM"));
    }

    [Fact]
    public void NormalizeDomainInput_WithWhitespace_Trims()
    {
        Assert.Equal("example.com", DnsSecurityEvaluationEngine.NormalizeDomainInput("  example.com  "));
    }

    [Fact]
    public void NormalizeDomainInput_WithScheme_StripsScheme()
    {
        Assert.Equal("example.com", DnsSecurityEvaluationEngine.NormalizeDomainInput("https://example.com"));
    }

    [Fact]
    public void NormalizeDomainInput_WithPath_StripsPath()
    {
        Assert.Equal("example.com", DnsSecurityEvaluationEngine.NormalizeDomainInput("example.com/path/to/resource"));
    }

    [Fact]
    public void NormalizeDomainInput_WithPort_StripsPort()
    {
        Assert.Equal("example.com", DnsSecurityEvaluationEngine.NormalizeDomainInput("example.com:8443"));
    }

    [Fact]
    public void NormalizeDomainInput_WithQueryString_StripsQuery()
    {
        Assert.Equal("example.com", DnsSecurityEvaluationEngine.NormalizeDomainInput("example.com?foo=bar"));
    }

    [Fact]
    public void NormalizeDomainInput_EmailLocalPart_StripsLocalPart()
    {
        Assert.Equal("example.com", DnsSecurityEvaluationEngine.NormalizeDomainInput("user@Example.com"));
    }

    [Fact]
    public void NormalizeDomainInput_EmailWithScheme_StripsAll()
    {
        Assert.Equal("example.com", DnsSecurityEvaluationEngine.NormalizeDomainInput("https://User@Example.COM/path"));
    }

    [Fact]
    public void NormalizeDomainInput_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, DnsSecurityEvaluationEngine.NormalizeDomainInput(null));
        Assert.Equal(string.Empty, DnsSecurityEvaluationEngine.NormalizeDomainInput(""));
        Assert.Equal(string.Empty, DnsSecurityEvaluationEngine.NormalizeDomainInput("   "));
    }

    // ── DefaultDkimSelectors ─────────────────────────────────────────

    [Fact]
    public void DefaultDkimSelectors_ContainsCommonSelectors()
    {
        Assert.Contains("default", DnsSecurityEvaluationEngine.DefaultDkimSelectors);
        Assert.Contains("google", DnsSecurityEvaluationEngine.DefaultDkimSelectors);
        Assert.Contains("selector1", DnsSecurityEvaluationEngine.DefaultDkimSelectors);
        Assert.Contains("selector2", DnsSecurityEvaluationEngine.DefaultDkimSelectors);
    }

    // ── ExtractRecord ────────────────────────────────────────────────

    [Fact]
    public void ExtractRecord_DigShortFormat_ReturnsValue()
    {
        var raw = "\"v=spf1 include:_spf.google.com ~all\"";
        Assert.Equal("v=spf1 include:_spf.google.com ~all",
            DnsSecurityEvaluationEngine.ExtractRecord(raw, "v=spf1"));
    }

    [Fact]
    public void ExtractRecord_NslookupTextFormat_ReturnsValue()
    {
        var raw = "example.com\ttext = \"v=spf1 -all\"";
        Assert.Equal("v=spf1 -all",
            DnsSecurityEvaluationEngine.ExtractRecord(raw, "v=spf1"));
    }

    [Fact]
    public void ExtractRecord_MultiLineOutput_FindsMatching()
    {
        var raw = "\"other record\"\n\"v=DMARC1; p=reject\"\n\"yet another\"";
        Assert.Equal("v=DMARC1; p=reject",
            DnsSecurityEvaluationEngine.ExtractRecord(raw, "v=DMARC1"));
    }

    [Fact]
    public void ExtractRecord_CaseInsensitiveMarker_Matches()
    {
        var raw = "\"V=SPF1 ~all\"";
        Assert.Equal("V=SPF1 ~all",
            DnsSecurityEvaluationEngine.ExtractRecord(raw, "v=spf1"));
    }

    [Fact]
    public void ExtractRecord_NoMatch_ReturnsEmpty()
    {
        var raw = "\"nothing interesting here\"";
        Assert.Equal(string.Empty,
            DnsSecurityEvaluationEngine.ExtractRecord(raw, "v=spf1"));
    }

    [Fact]
    public void ExtractRecord_NullOrEmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, DnsSecurityEvaluationEngine.ExtractRecord(null, "v=spf1"));
        Assert.Equal(string.Empty, DnsSecurityEvaluationEngine.ExtractRecord("", "v=spf1"));
        Assert.Equal(string.Empty, DnsSecurityEvaluationEngine.ExtractRecord("v=spf1", ""));
    }

    // ── ExtractTag ───────────────────────────────────────────────────

    [Fact]
    public void ExtractTag_SimpleTag_ReturnsValue()
    {
        Assert.Equal("reject",
            DnsSecurityEvaluationEngine.ExtractTag("v=DMARC1; p=reject; rua=mailto:x@y", "p"));
    }

    [Fact]
    public void ExtractTag_MissingTag_ReturnsEmpty()
    {
        Assert.Equal(string.Empty,
            DnsSecurityEvaluationEngine.ExtractTag("v=DMARC1; rua=mailto:x@y", "p"));
    }

    [Fact]
    public void ExtractTag_CaseInsensitiveTag_Matches()
    {
        Assert.Equal("quarantine",
            DnsSecurityEvaluationEngine.ExtractTag("v=DMARC1; P=quarantine", "p"));
    }

    [Fact]
    public void ExtractTag_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, DnsSecurityEvaluationEngine.ExtractTag(null, "p"));
        Assert.Equal(string.Empty, DnsSecurityEvaluationEngine.ExtractTag("v=DMARC1", ""));
    }

    // ── ParseCaaRecords ──────────────────────────────────────────────

    [Fact]
    public void ParseCaaRecords_IssueRecord_ReturnsIssuer()
    {
        var raw = "0 issue \"letsencrypt.org\"";
        var results = DnsSecurityEvaluationEngine.ParseCaaRecords(raw);
        Assert.Single(results);
        Assert.Contains("letsencrypt.org", results[0]);
    }

    [Fact]
    public void ParseCaaRecords_MultipleDirectives_ReturnsAll()
    {
        var raw = "0 issue \"letsencrypt.org\"\n0 issuewild \";\"\n0 iodef \"mailto:sec@example.com\"";
        var results = DnsSecurityEvaluationEngine.ParseCaaRecords(raw);
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void ParseCaaRecords_Duplicates_Deduplicated()
    {
        var raw = "0 issue \"letsencrypt.org\"\n0 issue \"letsencrypt.org\"";
        var results = DnsSecurityEvaluationEngine.ParseCaaRecords(raw);
        Assert.Single(results);
    }

    [Fact]
    public void ParseCaaRecords_Empty_ReturnsEmptyList()
    {
        Assert.Empty(DnsSecurityEvaluationEngine.ParseCaaRecords(""));
        Assert.Empty(DnsSecurityEvaluationEngine.ParseCaaRecords(null));
        Assert.Empty(DnsSecurityEvaluationEngine.ParseCaaRecords("nothing related"));
    }

    // ── ParseMxRecords ───────────────────────────────────────────────

    [Fact]
    public void ParseMxRecords_DigShortFormat_ReturnsHostnames()
    {
        var raw = "10 mail1.example.com.\n20 mail2.example.com.";
        var results = DnsSecurityEvaluationEngine.ParseMxRecords(raw);
        Assert.Equal(2, results.Count);
        Assert.Equal("mail1.example.com", results[0]);
        Assert.Equal("mail2.example.com", results[1]);
    }

    [Fact]
    public void ParseMxRecords_NslookupFormat_ReturnsHostnames()
    {
        var raw = "example.com\tmail exchanger = 10 mail.example.com.";
        var results = DnsSecurityEvaluationEngine.ParseMxRecords(raw);
        Assert.Single(results);
        Assert.Equal("mail.example.com", results[0]);
    }

    [Fact]
    public void ParseMxRecords_TrailingDot_Stripped()
    {
        var raw = "10 mail.example.com.";
        var results = DnsSecurityEvaluationEngine.ParseMxRecords(raw);
        Assert.Equal("mail.example.com", results[0]);
    }

    [Fact]
    public void ParseMxRecords_Duplicates_Deduplicated()
    {
        var raw = "10 mail.example.com.\n20 mail.example.com.";
        var results = DnsSecurityEvaluationEngine.ParseMxRecords(raw);
        Assert.Single(results);
    }

    [Fact]
    public void ParseMxRecords_Empty_ReturnsEmptyList()
    {
        Assert.Empty(DnsSecurityEvaluationEngine.ParseMxRecords(""));
        Assert.Empty(DnsSecurityEvaluationEngine.ParseMxRecords(null));
    }

    // ── ContainsDnsRecords ───────────────────────────────────────────

    [Fact]
    public void ContainsDnsRecords_RealData_ReturnsTrue()
    {
        var raw = "example.com has DNSKEY record 257 3 8 AwEAAb...";
        Assert.True(DnsSecurityEvaluationEngine.ContainsDnsRecords(raw, "DNSKEY"));
    }

    [Fact]
    public void ContainsDnsRecords_OnlyNslookupHeader_ReturnsFalse()
    {
        var raw = "Server:\t1.1.1.1\nAddress:\t1.1.1.1#53\nNon-authoritative answer:\n";
        Assert.False(DnsSecurityEvaluationEngine.ContainsDnsRecords(raw, "DNSKEY"));
    }

    [Fact]
    public void ContainsDnsRecords_EmptyOutput_ReturnsFalse()
    {
        Assert.False(DnsSecurityEvaluationEngine.ContainsDnsRecords(null, "DNSKEY"));
        Assert.False(DnsSecurityEvaluationEngine.ContainsDnsRecords("", "DNSKEY"));
        Assert.False(DnsSecurityEvaluationEngine.ContainsDnsRecords("data", ""));
    }

    [Fact]
    public void ContainsDnsRecords_DigShortFormat_ReturnsTrue()
    {
        var raw = "257 3 8 AwEAAbGfaDkEHH7sbL9T0tyFJAGJI8O+";
        Assert.True(DnsSecurityEvaluationEngine.ContainsDnsRecords(raw, "DNSKEY"));
    }

    // ── EvaluateSpf ──────────────────────────────────────────────────

    [Fact]
    public void EvaluateSpf_ValidRecord_ReturnsPass()
    {
        var raw = "\"v=spf1 include:_spf.google.com ~all\"";
        var result = DnsSecurityEvaluationEngine.EvaluateSpf(raw);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Contains("v=spf1", result.RawRecord);
        Assert.Equal("ToolDnsSecSpfGood", result.DetailKey);
    }

    [Fact]
    public void EvaluateSpf_PermissiveAll_ReturnsWarn()
    {
        var raw = "\"v=spf1 +all\"";
        var result = DnsSecurityEvaluationEngine.EvaluateSpf(raw);

        Assert.Equal(DnsCheckStatus.Warn, result.Status);
        Assert.Equal("ToolDnsSecSpfPermissive", result.DetailKey);
    }

    [Fact]
    public void EvaluateSpf_NeutralAll_ReturnsWarn()
    {
        string raw = "\"v=spf1 include:a ?all\"";
        DnsCheckResult result = DnsSecurityEvaluationEngine.EvaluateSpf(raw);

        Assert.Equal(DnsCheckStatus.Warn, result.Status);
        Assert.Equal("ToolDnsSecSpfNeutral", result.DetailKey);
    }

    [Fact]
    public void EvaluateSpf_MissingAll_ReturnsWarn()
    {
        string raw = "\"v=spf1 include:_spf.google.com\"";
        DnsCheckResult result = DnsSecurityEvaluationEngine.EvaluateSpf(raw);

        Assert.Equal(DnsCheckStatus.Warn, result.Status);
        Assert.Equal("ToolDnsSecSpfNoAll", result.DetailKey);
    }

    [Fact]
    public void EvaluateSpf_RedirectNoAll_ReturnsPass()
    {
        string raw = "\"v=spf1 redirect=_spf.example.com\"";
        DnsCheckResult result = DnsSecurityEvaluationEngine.EvaluateSpf(raw);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Equal("ToolDnsSecSpfGood", result.DetailKey);
    }

    [Fact]
    public void EvaluateSpf_HardFailAll_ReturnsPass()
    {
        string raw = "\"v=spf1 -all\"";
        DnsCheckResult result = DnsSecurityEvaluationEngine.EvaluateSpf(raw);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Equal("ToolDnsSecSpfGood", result.DetailKey);
    }

    [Fact]
    public void EvaluateSpf_BareAll_ReturnsWarnPermissive()
    {
        string raw = "\"v=spf1 all\"";
        DnsCheckResult result = DnsSecurityEvaluationEngine.EvaluateSpf(raw);

        Assert.Equal(DnsCheckStatus.Warn, result.Status);
        Assert.Equal("ToolDnsSecSpfPermissive", result.DetailKey);
    }

    [Fact]
    public void EvaluateSpf_AllSubstringInDomain_NotMatchedAsAll()
    {
        string raw = "\"v=spf1 include:spf.fall.com -all\"";
        DnsCheckResult result = DnsSecurityEvaluationEngine.EvaluateSpf(raw);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Equal("ToolDnsSecSpfGood", result.DetailKey);
    }

    [Fact]
    public void EvaluateSpf_NoRecord_ReturnsFail()
    {
        var result = DnsSecurityEvaluationEngine.EvaluateSpf("");

        Assert.Equal(DnsCheckStatus.Fail, result.Status);
        Assert.Equal(string.Empty, result.RawRecord);
        Assert.Equal("ToolDnsSecFail", result.DetailKey);
    }

    [Fact]
    public void EvaluateSpf_UnrelatedTxt_ReturnsFail()
    {
        var raw = "\"google-site-verification=abc123\"";
        var result = DnsSecurityEvaluationEngine.EvaluateSpf(raw);

        Assert.Equal(DnsCheckStatus.Fail, result.Status);
    }

    // ── EvaluateDkim ─────────────────────────────────────────────────

    [Fact]
    public void EvaluateDkim_SelectorAndRecord_ReturnsPassWithSelector()
    {
        var raw = "\"v=DKIM1; k=rsa; p=MIIBIjANBgkq...\"";
        var result = DnsSecurityEvaluationEngine.EvaluateDkim("google", raw);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.StartsWith("[google]", result.RawRecord);
        Assert.Equal("ToolDnsSecDkimFound", result.DetailKey);
        Assert.Single(result.DetailArgs);
        Assert.Equal("google", result.DetailArgs[0]);
    }

    [Fact]
    public void EvaluateDkim_NullSelector_ReturnsFail()
    {
        var result = DnsSecurityEvaluationEngine.EvaluateDkim(null, "\"v=DKIM1; p=abc\"");

        Assert.Equal(DnsCheckStatus.Fail, result.Status);
        Assert.Equal("ToolDnsSecNoDkim", result.DetailKey);
    }

    [Fact]
    public void EvaluateDkim_NullResponse_ReturnsFail()
    {
        var result = DnsSecurityEvaluationEngine.EvaluateDkim("default", null);

        Assert.Equal(DnsCheckStatus.Fail, result.Status);
        Assert.Equal("ToolDnsSecNoDkim", result.DetailKey);
    }

    [Fact]
    public void EvaluateDkim_SelectorWithoutDkimMarker_ReturnsFail()
    {
        var result = DnsSecurityEvaluationEngine.EvaluateDkim("default", "\"no dkim marker here\"");

        Assert.Equal(DnsCheckStatus.Fail, result.Status);
    }

    // ── EvaluateDmarc ────────────────────────────────────────────────

    [Fact]
    public void EvaluateDmarc_PolicyReject_ReturnsPass()
    {
        var raw = "\"v=DMARC1; p=reject; rua=mailto:sec@example.com\"";
        var result = DnsSecurityEvaluationEngine.EvaluateDmarc(raw);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Equal("ToolDnsSecDmarcEnforced", result.DetailKey);
        Assert.Equal("reject", result.DetailArgs[0]);
    }

    [Fact]
    public void EvaluateDmarc_PolicyQuarantine_ReturnsPass()
    {
        var raw = "\"v=DMARC1; p=quarantine\"";
        var result = DnsSecurityEvaluationEngine.EvaluateDmarc(raw);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Equal("quarantine", result.DetailArgs[0]);
    }

    [Fact]
    public void EvaluateDmarc_PolicyNone_ReturnsWarn()
    {
        var raw = "\"v=DMARC1; p=none\"";
        var result = DnsSecurityEvaluationEngine.EvaluateDmarc(raw);

        Assert.Equal(DnsCheckStatus.Warn, result.Status);
        Assert.Equal("ToolDnsSecDmarcNone", result.DetailKey);
    }

    [Fact]
    public void EvaluateDmarc_MissingPolicyTag_ReturnsWarn()
    {
        var raw = "\"v=DMARC1; rua=mailto:x@y\"";
        var result = DnsSecurityEvaluationEngine.EvaluateDmarc(raw);

        Assert.Equal(DnsCheckStatus.Warn, result.Status);
        Assert.Equal("ToolDnsSecDmarcNone", result.DetailKey);
    }

    [Fact]
    public void EvaluateDmarc_NoRecord_ReturnsFail()
    {
        var result = DnsSecurityEvaluationEngine.EvaluateDmarc("");

        Assert.Equal(DnsCheckStatus.Fail, result.Status);
        Assert.Equal("ToolDnsSecFail", result.DetailKey);
    }

    // ── EvaluateCaa ──────────────────────────────────────────────────

    [Fact]
    public void EvaluateCaa_WithIssuer_ReturnsPass()
    {
        var raw = "0 issue \"letsencrypt.org\"";
        var result = DnsSecurityEvaluationEngine.EvaluateCaa(raw);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Equal("ToolDnsSecCaaPresent", result.DetailKey);
        Assert.Single(result.DetailArgs);
    }

    [Fact]
    public void EvaluateCaa_MultipleIssuers_ReturnsJoinedList()
    {
        var raw = "0 issue \"letsencrypt.org\"\n0 issue \"digicert.com\"";
        var result = DnsSecurityEvaluationEngine.EvaluateCaa(raw);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Contains(",", result.DetailArgs[0]);
    }

    [Fact]
    public void EvaluateCaa_NoRecord_ReturnsFail()
    {
        var result = DnsSecurityEvaluationEngine.EvaluateCaa("");

        Assert.Equal(DnsCheckStatus.Fail, result.Status);
        Assert.Equal("ToolDnsSecNoCaa", result.DetailKey);
    }

    // ── EvaluateDnssec ───────────────────────────────────────────────

    [Fact]
    public void EvaluateDnssec_DnskeyPresent_ReturnsPass()
    {
        var dnskey = "example.com has DNSKEY record 257 3 8 AwEAAbGfaDkEHH7sbL9T";
        var result = DnsSecurityEvaluationEngine.EvaluateDnssec(dnskey, null);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Equal("ToolDnsSecDnssecPresent", result.DetailKey);
    }

    [Fact]
    public void EvaluateDnssec_OnlyRrsigPresent_ReturnsPass()
    {
        var rrsig = "example.com has RRSIG record DNSKEY 8 2 3600 20260101000000";
        var result = DnsSecurityEvaluationEngine.EvaluateDnssec(null, rrsig);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Equal("ToolDnsSecDnssecPresent", result.DetailKey);
    }

    [Fact]
    public void EvaluateDnssec_BothEmpty_ReturnsFail()
    {
        var result = DnsSecurityEvaluationEngine.EvaluateDnssec(null, null);

        Assert.Equal(DnsCheckStatus.Fail, result.Status);
        Assert.Equal("ToolDnsSecDnssecMissing", result.DetailKey);
    }

    [Fact]
    public void EvaluateDnssec_OnlyNslookupHeaders_ReturnsFail()
    {
        var headersOnly = "Server:\t1.1.1.1\nAddress:\t1.1.1.1#53\n";
        var result = DnsSecurityEvaluationEngine.EvaluateDnssec(headersOnly, headersOnly);

        Assert.Equal(DnsCheckStatus.Fail, result.Status);
    }

    // ── EvaluateMx ───────────────────────────────────────────────────

    [Fact]
    public void EvaluateMx_WithServers_ReturnsPass()
    {
        var raw = "10 mail.example.com.\n20 backup.example.com.";
        var result = DnsSecurityEvaluationEngine.EvaluateMx(raw);

        Assert.Equal(DnsCheckStatus.Pass, result.Status);
        Assert.Equal("ToolDnsSecMxServers", result.DetailKey);
        Assert.Single(result.DetailArgs);
        Assert.Contains("mail.example.com", result.DetailArgs[0]);
    }

    [Fact]
    public void EvaluateMx_NoServers_ReturnsFail()
    {
        var result = DnsSecurityEvaluationEngine.EvaluateMx("");

        Assert.Equal(DnsCheckStatus.Fail, result.Status);
        Assert.Equal("ToolDnsSecNoMx", result.DetailKey);
    }

    // ── BuildErrorResult ─────────────────────────────────────────────

    [Fact]
    public void BuildErrorResult_EmbedsMessage_AsRawRecord()
    {
        var result = DnsSecurityEvaluationEngine.BuildErrorResult(DnsCheckKind.Spf, "Connection refused");

        Assert.Equal(DnsCheckKind.Spf, result.Kind);
        Assert.Equal(DnsCheckStatus.Fail, result.Status);
        Assert.Equal("Connection refused", result.RawRecord);
        Assert.Equal(string.Empty, result.DetailKey);
    }

    [Fact]
    public void BuildErrorResult_NullMessage_UsesEmptyString()
    {
        var result = DnsSecurityEvaluationEngine.BuildErrorResult(DnsCheckKind.Mx, null);
        Assert.Equal(string.Empty, result.RawRecord);
    }

    // ── ComputeSummary ───────────────────────────────────────────────

    [Fact]
    public void ComputeSummary_AllPass_ReturnsAllPass()
    {
        Assert.Equal(DnsSummaryStatus.AllPass, DnsSecurityEvaluationEngine.ComputeSummary(6, 6));
    }

    [Fact]
    public void ComputeSummary_FourOfSix_ReturnsGood()
    {
        Assert.Equal(DnsSummaryStatus.Good, DnsSecurityEvaluationEngine.ComputeSummary(4, 6));
    }

    [Fact]
    public void ComputeSummary_ThreeOfSix_ReturnsPartial()
    {
        Assert.Equal(DnsSummaryStatus.Partial, DnsSecurityEvaluationEngine.ComputeSummary(3, 6));
    }

    [Fact]
    public void ComputeSummary_OneOfSix_ReturnsBad()
    {
        Assert.Equal(DnsSummaryStatus.Bad, DnsSecurityEvaluationEngine.ComputeSummary(1, 6));
    }

    [Fact]
    public void ComputeSummary_NoChecks_ReturnsBad()
    {
        Assert.Equal(DnsSummaryStatus.Bad, DnsSecurityEvaluationEngine.ComputeSummary(0, 0));
    }

    [Fact]
    public void ComputeSummary_ExactTwoThirdsBoundary_ReturnsGood()
    {
        Assert.Equal(DnsSummaryStatus.Good, DnsSecurityEvaluationEngine.ComputeSummary(2, 3));
    }

    // ── BuildReport ──────────────────────────────────────────────────

    [Fact]
    public void BuildReport_CountsPassResults()
    {
        var results = new[]
        {
            DnsSecurityEvaluationEngine.EvaluateSpf("\"v=spf1 ~all\""),
            DnsSecurityEvaluationEngine.EvaluateDmarc("\"v=DMARC1; p=reject\""),
            DnsSecurityEvaluationEngine.EvaluateCaa(""),
        };

        var report = DnsSecurityEvaluationEngine.BuildReport("example.com", results);

        Assert.Equal("example.com", report.Domain);
        Assert.Equal(3, report.Total);
        Assert.Equal(2, report.PassCount);
        Assert.Equal(DnsSummaryStatus.Good, report.Summary);
    }

    [Fact]
    public void BuildReport_NullDomain_UsesEmptyString()
    {
        var report = DnsSecurityEvaluationEngine.BuildReport(null, Array.Empty<DnsCheckResult>());
        Assert.Equal(string.Empty, report.Domain);
    }

    // ── BuildReportText ──────────────────────────────────────────────

    [Fact]
    public void BuildReportText_IncludesDomainAndScore()
    {
        var results = new[]
        {
            DnsSecurityEvaluationEngine.EvaluateSpf("\"v=spf1 ~all\""),
        };
        var report = DnsSecurityEvaluationEngine.BuildReport("example.com", results);

        var text = DnsSecurityEvaluationEngine.BuildReportText(report, Identity);

        Assert.Contains("ToolDnsSecReportTitle", text);
        Assert.Contains("ToolDnsSecReportScore", text);
    }

    [Fact]
    public void BuildReportText_IncludesAllResultRows()
    {
        var results = new[]
        {
            DnsSecurityEvaluationEngine.EvaluateSpf("\"v=spf1 ~all\""),
            DnsSecurityEvaluationEngine.EvaluateDmarc(""),
        };
        var report = DnsSecurityEvaluationEngine.BuildReport("example.com", results);

        var text = DnsSecurityEvaluationEngine.BuildReportText(report, Identity);

        Assert.Contains("ToolDnsSecSpf", text);
        Assert.Contains("ToolDnsSecDmarc", text);
    }

    [Fact]
    public void BuildReportText_OmitsEmptyRawRecord()
    {
        var results = new[] { DnsSecurityEvaluationEngine.EvaluateCaa("") };
        var report = DnsSecurityEvaluationEngine.BuildReport("example.com", results);

        var text = DnsSecurityEvaluationEngine.BuildReportText(report, Identity);

        // ToolDnsSecReportRecord line only renders when RawRecord is non-empty;
        // the "no record" CAA result has empty RawRecord.
        Assert.DoesNotContain("ToolDnsSecReportRecord", text);
    }

    [Fact]
    public void BuildReportText_OmitsEmptyDetailKey()
    {
        var errorResult = DnsSecurityEvaluationEngine.BuildErrorResult(DnsCheckKind.Spf, "Timeout");
        var report = DnsSecurityEvaluationEngine.BuildReport("example.com", new[] { errorResult });

        var text = DnsSecurityEvaluationEngine.BuildReportText(report, Identity);

        Assert.Contains("Timeout", text);
        // Error results have empty DetailKey so "ToolDnsSecReportDetail" label must not appear.
        Assert.DoesNotContain("ToolDnsSecReportDetail", text);
    }

    [Fact]
    public void BuildReportText_NullLocalize_Throws()
    {
        var report = DnsSecurityEvaluationEngine.BuildReport("example.com", Array.Empty<DnsCheckResult>());
        Assert.Throws<ArgumentNullException>(() => DnsSecurityEvaluationEngine.BuildReportText(report, null!));
    }

    // ── StatusToIcon / KindToDisplayKey / StatusToLabelKey ───────────

    [Fact]
    public void StatusToIcon_ReturnsExpectedGlyphs()
    {
        Assert.Equal("\u2713", DnsSecurityEvaluationEngine.StatusToIcon(DnsCheckStatus.Pass));
        Assert.Equal("\u26A0", DnsSecurityEvaluationEngine.StatusToIcon(DnsCheckStatus.Warn));
        Assert.Equal("\u2717", DnsSecurityEvaluationEngine.StatusToIcon(DnsCheckStatus.Fail));
    }

    [Fact]
    public void KindToDisplayKey_ReturnsExpectedKeys()
    {
        Assert.Equal("ToolDnsSecSpf", DnsSecurityEvaluationEngine.KindToDisplayKey(DnsCheckKind.Spf));
        Assert.Equal("ToolDnsSecDkim", DnsSecurityEvaluationEngine.KindToDisplayKey(DnsCheckKind.Dkim));
        Assert.Equal("ToolDnsSecDmarc", DnsSecurityEvaluationEngine.KindToDisplayKey(DnsCheckKind.Dmarc));
        Assert.Equal("ToolDnsSecCaa", DnsSecurityEvaluationEngine.KindToDisplayKey(DnsCheckKind.Caa));
        Assert.Equal("ToolDnsSecDnssec", DnsSecurityEvaluationEngine.KindToDisplayKey(DnsCheckKind.Dnssec));
        Assert.Equal("ToolDnsSecMx", DnsSecurityEvaluationEngine.KindToDisplayKey(DnsCheckKind.Mx));
    }

    [Fact]
    public void StatusToLabelKey_ReturnsExpectedKeys()
    {
        Assert.Equal("ToolDnsSecPass", DnsSecurityEvaluationEngine.StatusToLabelKey(DnsCheckStatus.Pass));
        Assert.Equal("ToolDnsSecWarn", DnsSecurityEvaluationEngine.StatusToLabelKey(DnsCheckStatus.Warn));
        Assert.Equal("ToolDnsSecFail", DnsSecurityEvaluationEngine.StatusToLabelKey(DnsCheckStatus.Fail));
    }
}
