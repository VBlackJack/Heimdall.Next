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
using Heimdall.Core.Security;

namespace Heimdall.Core.Network;

/// <summary>
/// Pure banner-grab engine: banner parsing, service fingerprinting,
/// port specification parsing, and export formatting.
/// </summary>
public static class BannerGrabEngine
{
    public const int ConnectTimeoutMs = 2000;
    public const int BannerReadTimeoutMs = 2000;
    public const int BannerMaxBytes = 512;
    public const int MaxConcurrent = 20;

    private static readonly Regex ControlCharRegex = new(
        @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]",
        RegexOptions.Compiled);

    /// <summary>
    /// Cleans control characters from raw banner text, preserving printable content.
    /// Returns null if the result is empty after cleaning.
    /// </summary>
    public static string? ParseBanner(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = ControlCharRegex.Replace(raw, " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// Maps a port to a service name. When a banner is available, enriches the
    /// identification by matching known server signatures.
    /// </summary>
    public static string IdentifyService(int port, string? banner, Func<int, string>? portLabelResolver = null)
    {
        var baseService = portLabelResolver?.Invoke(port) ?? string.Empty;
        if (string.IsNullOrEmpty(baseService))
        {
            baseService = port.ToString();
        }

        if (string.IsNullOrWhiteSpace(banner))
        {
            return baseService;
        }

        var bannerLower = banner.ToLowerInvariant();

        if (bannerLower.Contains("openssh", StringComparison.Ordinal))
        {
            return "OpenSSH";
        }

        if (bannerLower.Contains("dropbear", StringComparison.Ordinal))
        {
            return "Dropbear SSH";
        }

        if (bannerLower.Contains("apache", StringComparison.Ordinal))
        {
            return "Apache HTTP";
        }

        if (bannerLower.Contains("nginx", StringComparison.Ordinal))
        {
            return "nginx";
        }

        if (bannerLower.Contains("microsoft-iis", StringComparison.Ordinal))
        {
            return "IIS";
        }

        if (bannerLower.Contains("postfix", StringComparison.Ordinal))
        {
            return "Postfix SMTP";
        }

        if (bannerLower.Contains("exim", StringComparison.Ordinal))
        {
            return "Exim SMTP";
        }

        if (bannerLower.Contains("dovecot", StringComparison.Ordinal))
        {
            return "Dovecot";
        }

        if (bannerLower.Contains("mysql", StringComparison.Ordinal))
        {
            return "MySQL";
        }

        if (bannerLower.Contains("postgresql", StringComparison.Ordinal) ||
            bannerLower.Contains("pgsql", StringComparison.Ordinal))
        {
            return "PostgreSQL";
        }

        if (bannerLower.Contains("redis", StringComparison.Ordinal))
        {
            return "Redis";
        }

        if (bannerLower.Contains("mongodb", StringComparison.Ordinal) ||
            bannerLower.Contains("mongod", StringComparison.Ordinal))
        {
            return "MongoDB";
        }

        if (bannerLower.Contains("proftpd", StringComparison.Ordinal))
        {
            return "ProFTPD";
        }

        if (bannerLower.Contains("vsftpd", StringComparison.Ordinal))
        {
            return "vsftpd";
        }

        if (bannerLower.Contains("filezilla", StringComparison.Ordinal))
        {
            return "FileZilla FTP";
        }

        return baseService;
    }

    /// <summary>
    /// Parses a port specification string supporting comma-separated values and ranges.
    /// </summary>
    public static List<int> ParsePorts(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var ports = new HashSet<int>();

        foreach (var segment in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.Contains('-', StringComparison.Ordinal))
            {
                var rangeParts = segment.Split('-', 2);
                if (int.TryParse(rangeParts[0].Trim(), out var start) &&
                    int.TryParse(rangeParts[1].Trim(), out var end) &&
                    start >= 1 &&
                    end <= 65535 &&
                    start <= end)
                {
                    for (var port = start; port <= end; port++)
                    {
                        ports.Add(port);
                    }
                }
            }
            else if (int.TryParse(segment, out var port) && port is >= 1 and <= 65535)
            {
                ports.Add(port);
            }
        }

        return [.. ports.OrderBy(port => port)];
    }

    /// <summary>
    /// Builds CSV content from banner grab results.
    /// </summary>
    public static string BuildCsvExport(IReadOnlyList<BannerResult> results, Func<string, string>? localize = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{L(localize, "ToolBannerColPort")},{L(localize, "ToolBannerColService")},{L(localize, "ToolBannerColBanner")},{L(localize, "ToolBannerColTime")}");

        foreach (var result in results.OrderBy(result => result.Port))
        {
            var banner = InputValidator.SanitizeCsvCell(result.Banner).Replace("\"", "\"\"", StringComparison.Ordinal);
            var service = InputValidator.SanitizeCsvCell(result.Service);
            var responseTime = InputValidator.SanitizeCsvCell(result.ResponseTime);
            builder.AppendLine($"{result.Port},{service},\"{banner}\",{responseTime}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds formatted text for clipboard copy.
    /// </summary>
    public static string BuildClipboardText(IReadOnlyList<BannerResult> results, Func<string, string>? localize = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{L(localize, "ToolBannerColPort"),-8}{L(localize, "ToolBannerColService"),-20}{L(localize, "ToolBannerColTime"),-12}{L(localize, "ToolBannerColBanner")}");
        builder.AppendLine(new string('-', 72));

        foreach (var result in results)
        {
            builder.AppendLine($"{result.Port,-8}{result.Service,-20}{result.ResponseTime,-12}{result.Banner}");
        }

        return builder.ToString();
    }

    private static string L(Func<string, string>? localize, string key)
        => localize?.Invoke(key) ?? key;
}

/// <summary>
/// Raw result from probing a single port (before UI projection).
/// </summary>
public sealed record BannerProbeResult(int Port, string Service, string ResponseTime, string? Banner);

/// <summary>
/// Display-ready banner grab result for DataGrid binding.
/// </summary>
public sealed record BannerResult
{
    public int Port { get; init; }
    public string Service { get; init; } = string.Empty;
    public string Banner { get; init; } = string.Empty;
    public string ResponseTime { get; init; } = string.Empty;
    public bool HasBanner { get; init; }
}
