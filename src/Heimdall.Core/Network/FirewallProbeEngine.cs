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
using Heimdall.Core.Security;

namespace Heimdall.Core.Network;

/// <summary>
/// Pure firewall-probe engine: constants, data records, host parsing,
/// summary computation, and matrix export formatting.
/// Port parsing is delegated to <see cref="BannerGrabEngine.ParsePorts"/>.
/// </summary>
public static class FirewallProbeEngine
{
    public const int ConnectTimeoutMs = 3000;
    public const int MaxConcurrentDirect = 20;
    public const int MaxConcurrentTunnel = 10;
    public const int MaxHosts = 50;
    public const int MaxPorts = 50;

    /// <summary>
    /// Parses a multi-line text into a list of host strings.
    /// Splits by newline, trims whitespace, removes empties and duplicates.
    /// </summary>
    public static List<string> ParseHosts(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(host => host.Trim())
            .Where(host => host.Length > 0)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Aggregates probe results into open/closed/timeout counts.
    /// </summary>
    public static FwProbeSummary ComputeSummary(IReadOnlyList<FwProbeResult> results)
    {
        var open = 0;
        var closed = 0;
        var timeout = 0;

        foreach (var result in results)
        {
            switch (result.Status)
            {
                case ProbeStatus.Open:
                    open++;
                    break;
                case ProbeStatus.Closed:
                    closed++;
                    break;
                case ProbeStatus.Timeout:
                    timeout++;
                    break;
            }
        }

        return new FwProbeSummary(open, closed, timeout, results.Count);
    }

    /// <summary>
    /// Builds CSV content laid out as a matrix: one row per host, one column per port.
    /// Cell values are localized status labels with response time suffix for open ports.
    /// </summary>
    public static string BuildMatrixCsv(
        IReadOnlyList<FwProbeResult> results,
        IReadOnlyList<string> hosts,
        IReadOnlyList<int> ports,
        Func<string, string>? localize = null)
    {
        var builder = new StringBuilder();
        builder.Append(L(localize, "ToolFwColHost"));

        foreach (var port in ports)
        {
            builder.Append(',');
            builder.Append(port);
        }

        builder.AppendLine();

        foreach (var host in hosts)
        {
            builder.Append('"');
            builder.Append(InputValidator.SanitizeCsvCell(host));
            builder.Append('"');

            foreach (var port in ports)
            {
                var result = results.FirstOrDefault(item => item.Host == host && item.Port == port);
                var cell = FormatCell(result, localize, includeResponseMs: true);
                builder.Append(',');
                builder.Append(InputValidator.SanitizeCsvCell(cell));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds a fixed-width text matrix for clipboard copy.
    /// </summary>
    public static string BuildMatrixText(
        IReadOnlyList<FwProbeResult> results,
        IReadOnlyList<string> hosts,
        IReadOnlyList<int> ports,
        Func<string, string>? localize = null)
    {
        var builder = new StringBuilder();
        builder.Append($"{L(localize, "ToolFwColHost"),-30}");
        foreach (var port in ports)
        {
            builder.Append($"{port,10}");
        }

        builder.AppendLine();
        builder.AppendLine(new string('-', 30 + (ports.Count * 10)));

        foreach (var host in hosts)
        {
            builder.Append($"{host,-30}");

            foreach (var port in ports)
            {
                var result = results.FirstOrDefault(item => item.Host == host && item.Port == port);
                var cell = FormatCell(result, localize, includeResponseMs: false);
                builder.Append($"{cell,10}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatCell(FwProbeResult? result, Func<string, string>? localize, bool includeResponseMs)
    {
        if (result is null)
        {
            return L(localize, "ToolFwStatusTimeout");
        }

        return result.Status switch
        {
            ProbeStatus.Open when includeResponseMs
                => $"{L(localize, "ToolFwStatusOpen")} ({result.ResponseTimeMs}ms)",
            ProbeStatus.Open => L(localize, "ToolFwStatusOpen"),
            ProbeStatus.Closed => L(localize, "ToolFwStatusClosed"),
            ProbeStatus.Timeout => L(localize, "ToolFwStatusTimeout"),
            _ => L(localize, "ToolFwStatusTimeout"),
        };
    }

    private static string L(Func<string, string>? localize, string key)
        => localize?.Invoke(key) ?? key;
}

/// <summary>
/// TCP connectivity probe outcome.
/// </summary>
public enum ProbeStatus
{
    Open,
    Closed,
    Timeout,
}

/// <summary>
/// Raw result from probing a single host:port combination.
/// Status localization is performed by the view layer.
/// </summary>
public sealed record FwProbeResult(string Host, int Port, ProbeStatus Status, long ResponseTimeMs);

/// <summary>
/// Aggregated counts from a matrix probe run.
/// </summary>
public sealed record FwProbeSummary(int Open, int Closed, int Timeout, int Total);
