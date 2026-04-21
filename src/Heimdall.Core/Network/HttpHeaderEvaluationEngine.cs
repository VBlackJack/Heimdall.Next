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

namespace Heimdall.Core.Network;

/// <summary>
/// Pure HTTP header evaluation engine: URL normalization, response parsing,
/// security/disclosure checks, grading, and report formatting.
/// </summary>
public static class HttpHeaderEvaluationEngine
{
    public const int DefaultHttpPort = 80;
    public const int DefaultHttpsPort = 443;
    public const int MaxResponseBytes = 16384;

    internal const string HeaderStrictTransportSecurity = "Strict-Transport-Security";
    internal const string HeaderContentSecurityPolicy = "Content-Security-Policy";
    internal const string HeaderXFrameOptions = "X-Frame-Options";
    internal const string HeaderXContentTypeOptions = "X-Content-Type-Options";
    internal const string HeaderReferrerPolicy = "Referrer-Policy";
    internal const string HeaderPermissionsPolicy = "Permissions-Policy";
    internal const string HeaderXXssProtection = "X-XSS-Protection";
    internal const string HeaderSetCookie = "Set-Cookie";
    internal const string HeaderServer = "Server";
    internal const string HeaderXPoweredBy = "X-Powered-By";
    internal const string HeaderXAspNetVersion = "X-AspNet-Version";

    private const string NoSniffValue = "nosniff";

    /// <summary>
    /// Normalizes a raw user input into an absolute HTTP or HTTPS URI.
    /// </summary>
    public static bool TryNormalizeUrl(string input, out Uri? uri, out string errorKey)
    {
        uri = null;
        errorKey = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorKey = "ToolHttpHeadersErrorUrlRequired";
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal) &&
            !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            errorKey = "ToolHttpHeadersErrorInvalidUrl";
            return false;
        }

        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            errorKey = "ToolHttpHeadersErrorInvalidUrl";
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>
    /// Parses a raw HTTP response string into status code, headers, and header section.
    /// </summary>
    public static HttpResponseInfo ParseHttpResponse(string rawResponse)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var statusCode = 0;

        if (string.IsNullOrEmpty(rawResponse))
        {
            return new HttpResponseInfo(statusCode, headers, string.Empty);
        }

        var headerEnd = rawResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = headerEnd >= 0 ? rawResponse[..headerEnd] : rawResponse;
        var lines = headerSection.Split(["\r\n"], StringSplitOptions.None);

        if (lines.Length > 0)
        {
            var parts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[1], out var code))
            {
                statusCode = code;
            }
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
            {
                continue;
            }

            var name = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();
            if (headers.TryGetValue(name, out var existing))
            {
                headers[name] = existing + "; " + value;
            }
            else
            {
                headers[name] = value;
            }
        }

        return new HttpResponseInfo(statusCode, headers, headerSection);
    }

    /// <summary>
    /// Evaluates the target response and returns the security/disclosure sections plus grade.
    /// </summary>
    public static HeaderEvaluation EvaluateHeaders(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var security = EvaluateSecurityHeaders(headers);
        var disclosure = EvaluateDisclosureHeaders(headers);
        var grade = CalculateGrade(security);
        return new HeaderEvaluation(security, disclosure, grade);
    }

    /// <summary>
    /// Evaluates the standard security headers against best practices.
    /// </summary>
    public static IReadOnlyList<HeaderCheckResult> EvaluateSecurityHeaders(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return
        [
            CheckPresence(headers, HeaderStrictTransportSecurity, "ToolHttpHeadersHsts", "ToolHttpHeadersRecHsts"),
            CheckPresence(headers, HeaderContentSecurityPolicy, "ToolHttpHeadersCsp", "ToolHttpHeadersRecCsp"),
            CheckPresence(headers, HeaderXFrameOptions, "ToolHttpHeadersXfo", "ToolHttpHeadersRecXfo"),
            CheckExpectedValue(headers, HeaderXContentTypeOptions, "ToolHttpHeadersXcto", NoSniffValue, "ToolHttpHeadersRecXcto"),
            CheckPresence(headers, HeaderReferrerPolicy, "ToolHttpHeadersReferrer", "ToolHttpHeadersRecReferrer"),
            CheckPresence(headers, HeaderPermissionsPolicy, "ToolHttpHeadersPermissions", "ToolHttpHeadersRecPermissions"),
            EvaluateXssProtection(headers),
            EvaluateCookieFlags(headers),
        ];
    }

    /// <summary>
    /// Evaluates disclosure-oriented headers such as Server and X-Powered-By.
    /// </summary>
    public static IReadOnlyList<HeaderCheckResult> EvaluateDisclosureHeaders(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return
        [
            CheckDisclosure(headers, HeaderServer, "ToolHttpHeadersServer"),
            CheckDisclosure(headers, HeaderXPoweredBy, string.Empty),
            CheckDisclosure(headers, HeaderXAspNetVersion, string.Empty),
        ];
    }

    /// <summary>
    /// Computes the overall security grade from the security header results only.
    /// </summary>
    public static HttpGrade CalculateGrade(IReadOnlyList<HeaderCheckResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var passCount = results.Count(static r => r.Status == HeaderCheckStatus.Pass);
        var warnCount = results.Count(static r => r.Status == HeaderCheckStatus.Warn);
        var failCount = results.Count(static r => r.Status == HeaderCheckStatus.Fail);
        var total = results.Count;

        if (total == 0)
        {
            return HttpGrade.F;
        }

        if (failCount == 0 && warnCount == 0)
        {
            return HttpGrade.APlus;
        }

        if (failCount == 0 && warnCount <= 1)
        {
            return HttpGrade.A;
        }

        var score = (double)passCount / total;
        return score switch
        {
            >= 0.85 => HttpGrade.BPlus,
            >= 0.75 => HttpGrade.B,
            >= 0.60 => HttpGrade.C,
            >= 0.45 => HttpGrade.D,
            _ => HttpGrade.F,
        };
    }

    /// <summary>
    /// Formats the enum grade for display.
    /// </summary>
    public static string FormatGrade(HttpGrade grade)
    {
        return grade switch
        {
            HttpGrade.APlus => "A+",
            HttpGrade.BPlus => "B+",
            _ => grade.ToString(),
        };
    }

    /// <summary>
    /// Builds a text report suitable for clipboard export.
    /// </summary>
    public static string BuildTextReport(
        string host,
        HttpGrade grade,
        IReadOnlyList<HeaderCheckResult> securityResults,
        IReadOnlyList<HeaderCheckResult> disclosureResults,
        string rawResponse,
        Func<string, string>? localize = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(securityResults);
        ArgumentNullException.ThrowIfNull(disclosureResults);

        localize ??= static key => key;

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(localize("ToolHttpHeaderReportTitle"), host));
        sb.AppendLine(string.Format(localize("ToolHttpHeaderReportGrade"), FormatGrade(grade)));
        sb.AppendLine();
        sb.AppendLine(localize("ToolHttpHeaderReportSecHeaders"));

        foreach (var result in securityResults)
        {
            AppendReportLine(sb, result, localize);
        }

        sb.AppendLine();
        sb.AppendLine(localize("ToolHttpHeaderReportInfoDisc"));
        foreach (var result in disclosureResults)
        {
            AppendReportLine(sb, result, localize);
        }

        sb.AppendLine();
        sb.AppendLine(localize("ToolHttpHeaderReportRawHeaders"));
        sb.AppendLine(rawResponse);

        return sb.ToString();
    }

    /// <summary>
    /// Formats the cookie missing flags placeholder from a list of missing flags.
    /// </summary>
    public static string FormatCookieMissing(IEnumerable<string> missingFlags, Func<string, string>? localize = null)
    {
        ArgumentNullException.ThrowIfNull(missingFlags);

        localize ??= static key => key;
        var missingText = string.Join(", ", missingFlags);
        return string.Format(localize("ToolHttpHeadersCookieMissing"), missingText);
    }

    private static HeaderCheckResult CheckPresence(
        IReadOnlyDictionary<string, string> headers,
        string headerName,
        string displayNameKey,
        string recommendationKey)
    {
        if (headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return new HeaderCheckResult(headerName, displayNameKey, value, null, HeaderCheckStatus.Pass, null);
        }

        return new HeaderCheckResult(headerName, displayNameKey, string.Empty, "ToolHttpHeadersMissing", HeaderCheckStatus.Fail, recommendationKey);
    }

    private static HeaderCheckResult CheckExpectedValue(
        IReadOnlyDictionary<string, string> headers,
        string headerName,
        string displayNameKey,
        string expectedValue,
        string recommendationKey)
    {
        if (headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            var isCorrect = value.Equals(expectedValue, StringComparison.OrdinalIgnoreCase);
            return new HeaderCheckResult(
                headerName,
                displayNameKey,
                value,
                null,
                isCorrect ? HeaderCheckStatus.Pass : HeaderCheckStatus.Warn,
                isCorrect ? null : recommendationKey);
        }

        return new HeaderCheckResult(headerName, displayNameKey, string.Empty, "ToolHttpHeadersMissing", HeaderCheckStatus.Fail, recommendationKey);
    }

    private static HeaderCheckResult EvaluateXssProtection(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.TryGetValue(HeaderXXssProtection, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return new HeaderCheckResult(
                HeaderXXssProtection,
                string.Empty,
                value,
                null,
                HeaderCheckStatus.Warn,
                "ToolHttpHeadersRecXss");
        }

        return new HeaderCheckResult(
            HeaderXXssProtection,
            string.Empty,
            string.Empty,
            "ToolHttpHeadersMissing",
            HeaderCheckStatus.Pass,
            "ToolHttpHeadersRecXssAbsent");
    }

    private static HeaderCheckResult EvaluateCookieFlags(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(HeaderSetCookie, out var cookieValue) || string.IsNullOrWhiteSpace(cookieValue))
        {
            return new HeaderCheckResult(
                HeaderSetCookie,
                "ToolHttpHeadersCookieFlags",
                string.Empty,
                "ToolHttpHeadersNoCookies",
                HeaderCheckStatus.Pass,
                null);
        }

        var hasSecure = cookieValue.Contains("Secure", StringComparison.OrdinalIgnoreCase);
        var hasHttpOnly = cookieValue.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase);
        var hasSameSite = cookieValue.Contains("SameSite", StringComparison.OrdinalIgnoreCase);

        var missing = new List<string>(3);
        if (!hasSecure)
        {
            missing.Add("Secure");
        }

        if (!hasHttpOnly)
        {
            missing.Add("HttpOnly");
        }

        if (!hasSameSite)
        {
            missing.Add("SameSite");
        }

        if (missing.Count == 0)
        {
            return new HeaderCheckResult(
                HeaderSetCookie,
                "ToolHttpHeadersCookieFlags",
                "Secure, HttpOnly, SameSite",
                null,
                HeaderCheckStatus.Pass,
                null);
        }

        return new HeaderCheckResult(
            HeaderSetCookie,
            "ToolHttpHeadersCookieFlags",
            string.Join(", ", missing),
            "ToolHttpHeadersCookieMissing",
            missing.Count >= 2 ? HeaderCheckStatus.Fail : HeaderCheckStatus.Warn,
            "ToolHttpHeadersRecCookies");
    }

    private static HeaderCheckResult CheckDisclosure(
        IReadOnlyDictionary<string, string> headers,
        string headerName,
        string displayNameKey)
    {
        if (headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return new HeaderCheckResult(
                headerName,
                displayNameKey,
                value,
                null,
                HeaderCheckStatus.Warn,
                "ToolHttpHeadersDisclosureWarn");
        }

        return new HeaderCheckResult(
            headerName,
            displayNameKey,
            string.Empty,
            "ToolHttpHeadersNotPresent",
            HeaderCheckStatus.Pass,
            null);
    }

    private static void AppendReportLine(StringBuilder sb, HeaderCheckResult result, Func<string, string> localize)
    {
        var icon = result.Status switch
        {
            HeaderCheckStatus.Pass => "\u2713",
            HeaderCheckStatus.Warn => "\u26A0",
            _ => "\u2717",
        };

        var name = string.IsNullOrWhiteSpace(result.DisplayNameKey)
            ? result.HeaderName
            : localize(result.DisplayNameKey);

        var value = result.ActualValueKey switch
        {
            "ToolHttpHeadersCookieMissing" => FormatCookieMissing(
                result.ActualValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                localize),
            not null => localize(result.ActualValueKey),
            _ => result.ActualValue,
        };

        sb.AppendLine($"  {icon} {name}: {value}");
        if (!string.IsNullOrWhiteSpace(result.RecommendationKey))
        {
            sb.AppendLine($"    -> {localize(result.RecommendationKey)}");
        }
    }
}

/// <summary>
/// Evaluation status of a single HTTP header check.
/// </summary>
public enum HeaderCheckStatus
{
    Pass,
    Warn,
    Fail,
}

/// <summary>
/// Overall HTTP security grade.
/// </summary>
public enum HttpGrade
{
    APlus,
    A,
    BPlus,
    B,
    C,
    D,
    F,
}

/// <summary>
/// Result of evaluating a single header.
/// </summary>
public sealed record HeaderCheckResult(
    string HeaderName,
    string DisplayNameKey,
    string ActualValue,
    string? ActualValueKey,
    HeaderCheckStatus Status,
    string? RecommendationKey);

/// <summary>
/// Parsed HTTP response envelope.
/// </summary>
public sealed record HttpResponseInfo(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string RawHeaderSection);

/// <summary>
/// Aggregated evaluation for a target response.
/// </summary>
public sealed record HeaderEvaluation(
    IReadOnlyList<HeaderCheckResult> SecurityHeaders,
    IReadOnlyList<HeaderCheckResult> DisclosureHeaders,
    HttpGrade Grade);
