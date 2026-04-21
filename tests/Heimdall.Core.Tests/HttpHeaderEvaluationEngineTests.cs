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

using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public class HttpHeaderEvaluationEngineTests
{
    [Fact]
    public void TryNormalizeUrl_Empty_ReturnsRequiredError()
    {
        var success = HttpHeaderEvaluationEngine.TryNormalizeUrl(string.Empty, out var uri, out var errorKey);

        Assert.False(success);
        Assert.Null(uri);
        Assert.Equal("ToolHttpHeadersErrorUrlRequired", errorKey);
    }

    [Fact]
    public void TryNormalizeUrl_WithoutScheme_PrependsHttps()
    {
        var success = HttpHeaderEvaluationEngine.TryNormalizeUrl("example.com", out var uri, out var errorKey);

        Assert.True(success);
        Assert.Equal(string.Empty, errorKey);
        Assert.NotNull(uri);
        Assert.Equal("https://example.com/", uri!.ToString());
    }

    [Fact]
    public void TryNormalizeUrl_InvalidScheme_ReturnsError()
    {
        var success = HttpHeaderEvaluationEngine.TryNormalizeUrl("ftp://example.com", out var uri, out var errorKey);

        Assert.False(success);
        Assert.Null(uri);
        Assert.Equal("ToolHttpHeadersErrorInvalidUrl", errorKey);
    }

    [Fact]
    public void ParseHttpResponse_ParsesStatusAndHeaders()
    {
        const string raw =
            "HTTP/1.1 200 OK\r\n" +
            "Strict-Transport-Security: max-age=31536000\r\n" +
            "Set-Cookie: a=1; Secure\r\n" +
            "Set-Cookie: b=2; HttpOnly\r\n\r\n" +
            "<html></html>";

        var info = HttpHeaderEvaluationEngine.ParseHttpResponse(raw);

        Assert.Equal(200, info.StatusCode);
        Assert.Equal("max-age=31536000", info.Headers["Strict-Transport-Security"]);
        Assert.Equal("a=1; Secure; b=2; HttpOnly", info.Headers["Set-Cookie"]);
        Assert.DoesNotContain("<html>", info.RawHeaderSection, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateSecurityHeaders_AllPresent_ReturnsPasses()
    {
        var headers = CreateSecureHeaders();

        var results = HttpHeaderEvaluationEngine.EvaluateSecurityHeaders(headers);

        Assert.All(results.Where(static r => r.HeaderName != "X-XSS-Protection"), static result => Assert.Equal(HeaderCheckStatus.Pass, result.Status));
        Assert.Contains(results, static result => result.HeaderName == "X-XSS-Protection" && result.Status == HeaderCheckStatus.Pass);
    }

    [Fact]
    public void EvaluateSecurityHeaders_XContentTypeOptionsWrongValue_ReturnsWarn()
    {
        var headers = CreateSecureHeaders();
        headers["X-Content-Type-Options"] = "wrong";

        var result = HttpHeaderEvaluationEngine.EvaluateSecurityHeaders(headers)
            .Single(static r => r.HeaderName == "X-Content-Type-Options");

        Assert.Equal(HeaderCheckStatus.Warn, result.Status);
        Assert.Equal("ToolHttpHeadersRecXcto", result.RecommendationKey);
    }

    [Fact]
    public void EvaluateSecurityHeaders_CookieFlagsMissingTwo_ReturnsFail()
    {
        var headers = CreateSecureHeaders();
        headers["Set-Cookie"] = "sid=1; HttpOnly";

        var result = HttpHeaderEvaluationEngine.EvaluateSecurityHeaders(headers)
            .Single(static r => r.HeaderName == "Set-Cookie");

        Assert.Equal(HeaderCheckStatus.Fail, result.Status);
        Assert.Equal("Secure, SameSite", result.ActualValue);
        Assert.Equal("ToolHttpHeadersCookieMissing", result.ActualValueKey);
    }

    [Fact]
    public void EvaluateDisclosureHeaders_PresentServerHeader_ReturnsWarn()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Server"] = "nginx",
        };

        var result = HttpHeaderEvaluationEngine.EvaluateDisclosureHeaders(headers)
            .Single(static r => r.HeaderName == "Server");

        Assert.Equal(HeaderCheckStatus.Warn, result.Status);
        Assert.Equal("ToolHttpHeadersDisclosureWarn", result.RecommendationKey);
    }

    [Theory]
    [InlineData(0, 0, HttpGrade.APlus)]
    [InlineData(0, 1, HttpGrade.A)]
    public void CalculateGrade_NoFailures_ReturnsExpected(int failCount, int warnCount, HttpGrade expected)
    {
        var results = BuildStatusResults(8, failCount, warnCount);

        var grade = HttpHeaderEvaluationEngine.CalculateGrade(results);

        Assert.Equal(expected, grade);
    }

    [Theory]
    [InlineData(7, 1, 0, HttpGrade.BPlus)]
    [InlineData(6, 2, 0, HttpGrade.B)]
    [InlineData(5, 3, 0, HttpGrade.C)]
    [InlineData(4, 4, 0, HttpGrade.D)]
    [InlineData(3, 5, 0, HttpGrade.F)]
    public void CalculateGrade_WithFailuresAndWarnings_ReturnsExpected(
        int passCount,
        int failCount,
        int warnCount,
        HttpGrade expected)
    {
        var results = Enumerable.Repeat(new HeaderCheckResult("A", string.Empty, string.Empty, null, HeaderCheckStatus.Pass, null), passCount)
            .Concat(Enumerable.Repeat(new HeaderCheckResult("B", string.Empty, string.Empty, null, HeaderCheckStatus.Fail, "rec"), failCount))
            .Concat(Enumerable.Repeat(new HeaderCheckResult("C", string.Empty, string.Empty, null, HeaderCheckStatus.Warn, "rec"), warnCount))
            .ToList();

        var grade = HttpHeaderEvaluationEngine.CalculateGrade(results);

        Assert.Equal(expected, grade);
    }

    [Theory]
    [InlineData(HttpGrade.APlus, "A+")]
    [InlineData(HttpGrade.BPlus, "B+")]
    [InlineData(HttpGrade.C, "C")]
    public void FormatGrade_ReturnsExpected(HttpGrade grade, string expected)
    {
        Assert.Equal(expected, HttpHeaderEvaluationEngine.FormatGrade(grade));
    }

    [Fact]
    public void BuildTextReport_UsesLocalizedValuesAndRecommendations()
    {
        string Localize(string key) => key switch
        {
            "ToolHttpHeaderReportTitle" => "Report {0}",
            "ToolHttpHeaderReportGrade" => "Grade {0}",
            "ToolHttpHeaderReportSecHeaders" => "Security",
            "ToolHttpHeaderReportInfoDisc" => "Disclosure",
            "ToolHttpHeaderReportRawHeaders" => "Raw",
            "ToolHttpHeadersHsts" => "HSTS",
            "ToolHttpHeadersMissing" => "Missing",
            "ToolHttpHeadersRecHsts" => "Add HSTS",
            _ => key,
        };

        var report = HttpHeaderEvaluationEngine.BuildTextReport(
            "example.com",
            HttpGrade.C,
            [new HeaderCheckResult("Strict-Transport-Security", "ToolHttpHeadersHsts", string.Empty, "ToolHttpHeadersMissing", HeaderCheckStatus.Fail, "ToolHttpHeadersRecHsts")],
            [],
            "HTTP/1.1 200 OK",
            Localize);

        Assert.Contains("Report example.com", report);
        Assert.Contains("Grade C", report);
        Assert.Contains("HSTS: Missing", report);
        Assert.Contains("Add HSTS", report);
        Assert.Contains("HTTP/1.1 200 OK", report);
    }

    [Fact]
    public void FormatCookieMissing_FormatsMissingFlags()
    {
        string Localize(string key) => key == "ToolHttpHeadersCookieMissing" ? "Missing flags: {0}" : key;

        var text = HttpHeaderEvaluationEngine.FormatCookieMissing(["Secure", "SameSite"], Localize);

        Assert.Equal("Missing flags: Secure, SameSite", text);
    }

    [Fact]
    public void EvaluateHeaders_ReturnsAggregatedSections()
    {
        var evaluation = HttpHeaderEvaluationEngine.EvaluateHeaders(CreateSecureHeaders());

        Assert.Equal(8, evaluation.SecurityHeaders.Count);
        Assert.Equal(3, evaluation.DisclosureHeaders.Count);
        Assert.Equal(HttpGrade.APlus, evaluation.Grade);
    }

    private static Dictionary<string, string> CreateSecureHeaders()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains",
            ["Content-Security-Policy"] = "default-src 'self'",
            ["X-Frame-Options"] = "DENY",
            ["X-Content-Type-Options"] = "nosniff",
            ["Referrer-Policy"] = "strict-origin-when-cross-origin",
            ["Permissions-Policy"] = "geolocation=()",
            ["Set-Cookie"] = "sid=1; Secure; HttpOnly; SameSite=Lax",
        };
    }

    private static IReadOnlyList<HeaderCheckResult> BuildStatusResults(int total, int failCount, int warnCount)
    {
        var passCount = total - failCount - warnCount;
        return Enumerable.Repeat(new HeaderCheckResult("A", string.Empty, string.Empty, null, HeaderCheckStatus.Pass, null), passCount)
            .Concat(Enumerable.Repeat(new HeaderCheckResult("B", string.Empty, string.Empty, null, HeaderCheckStatus.Fail, "rec"), failCount))
            .Concat(Enumerable.Repeat(new HeaderCheckResult("C", string.Empty, string.Empty, null, HeaderCheckStatus.Warn, "rec"), warnCount))
            .ToList();
    }
}
