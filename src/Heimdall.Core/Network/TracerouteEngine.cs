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
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Heimdall.Core.Security;

namespace Heimdall.Core.Network;

/// <summary>
/// Pure traceroute engine: constants, data records, input validation,
/// hop probe aggregation, output parsing, and clipboard export formatting.
/// </summary>
public static partial class TracerouteEngine
{
    public const int DefaultMaxHops = 30;
    public const int ProbesPerHop = 3;
    public const int MinMaxHops = 1;
    public const int MaxMaxHops = 128;
    public const int PingTimeoutMs = 3000;
    public const int PingBufferSize = 32;

    [GeneratedRegex(@"^\s*(\d+)\s+(.+)$")]
    private static partial Regex LinuxTracerouteRegex();

    [GeneratedRegex(@"^\s*(\d+)\s+([\d<*]+\s*ms|\*)\s+([\d<*]+\s*ms|\*)\s+([\d<*]+\s*ms|\*)\s+(\S+)\s*$")]
    private static partial Regex WindowsTracertRegex();

    /// <summary>
    /// Validates raw traceroute inputs and normalizes max-hops to a safe range.
    /// </summary>
    public static (TraceInputs? Inputs, string? ErrorKey) ValidateInputs(
        string? hostText,
        string? maxHopsText)
    {
        var host = (hostText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return (null, "ToolValidationHostRequired");
        }

        if (!InputValidator.Validate(host, "Address"))
        {
            return (null, "ErrorInvalidHost");
        }

        var maxHops = DefaultMaxHops;
        var maxRaw = (maxHopsText ?? string.Empty).Trim();
        if (int.TryParse(maxRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= MinMaxHops &&
            parsed <= MaxMaxHops)
        {
            maxHops = parsed;
        }

        return (new TraceInputs(host, maxHops), null);
    }

    /// <summary>
    /// Formats successful latencies into "*" / "N ms" / "min/avg/max ms".
    /// </summary>
    public static string FormatLatency(IReadOnlyList<long> successfulMs)
    {
        if (successfulMs.Count == 0)
        {
            return "*";
        }

        if (successfulMs.Count == 1)
        {
            return $"{successfulMs[0].ToString(CultureInfo.InvariantCulture)} ms";
        }

        var min = successfulMs.Min();
        var max = successfulMs.Max();
        var avg = successfulMs.Average();
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}/{1:F0}/{2} ms",
            min,
            avg,
            max);
    }

    /// <summary>
    /// Formats successful latencies into "*" / "N ms" / "min/avg/max ms".
    /// </summary>
    public static string FormatLatency(IReadOnlyList<double> successfulMs)
    {
        if (successfulMs.Count == 0)
        {
            return "*";
        }

        if (successfulMs.Count == 1)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F0} ms", successfulMs[0]);
        }

        var min = successfulMs.Min();
        var max = successfulMs.Max();
        var avg = successfulMs.Average();
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:F0}/{1:F0}/{2:F0} ms",
            min,
            avg,
            max);
    }

    /// <summary>
    /// Aggregates the N probe results for a single TTL into a display-ready hop.
    /// </summary>
    public static TraceHopResult AggregateHopProbes(
        int hop,
        IReadOnlyList<HopProbeResult> probes,
        IPAddress targetIp)
    {
        var hopAddress = probes
            .Select(p => p.Address)
            .FirstOrDefault(a => a is not null);

        var successful = probes
            .Where(p => p.LatencyMs >= 0)
            .Select(p => p.LatencyMs)
            .ToList();

        var isDestination = hopAddress is not null && hopAddress.Equals(targetIp);

        HopStatus status;
        if (successful.Count == 0)
        {
            status = HopStatus.Timeout;
        }
        else if (isDestination)
        {
            status = HopStatus.Destination;
        }
        else
        {
            status = HopStatus.Reply;
        }

        var addressText = hopAddress?.ToString() ?? "*";
        var latencyText = FormatLatency(successful);

        return new TraceHopResult(hop, addressText, string.Empty, latencyText, status);
    }

    /// <summary>
    /// Parses a single Linux or Windows traceroute line into a hop result.
    /// </summary>
    public static TraceHopResult? ParseTracerouteLine(string line, ref int hopNumber)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        if (line.StartsWith("traceroute", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Tracing", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("over a maximum", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Trace complete", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var windowsMatch = WindowsTracertRegex().Match(line);
        if (windowsMatch.Success)
        {
            hopNumber = int.Parse(windowsMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var latency1 = windowsMatch.Groups[2].Value.Trim();
            var latency2 = windowsMatch.Groups[3].Value.Trim();
            var latency3 = windowsMatch.Groups[4].Value.Trim();
            var address = windowsMatch.Groups[5].Value.Trim();
            return ParseWindowsHopData(hopNumber, address, latency1, latency2, latency3);
        }

        var linuxMatch = LinuxTracerouteRegex().Match(line);
        if (linuxMatch.Success)
        {
            hopNumber = int.Parse(linuxMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var remainder = linuxMatch.Groups[2].Value.Trim();
            return ParseLinuxHopData(hopNumber, remainder);
        }

        return null;
    }

    /// <summary>
    /// Parses the remainder of a Linux traceroute row.
    /// </summary>
    public static TraceHopResult ParseLinuxHopData(int hop, string remainder)
    {
        var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var address = "*";
        var latencies = new List<double>();
        var allTimeout = true;

        var index = 0;
        while (index < parts.Length)
        {
            if (parts[index] == "*")
            {
                index++;
                continue;
            }

            if (LooksLikeAddress(parts[index]) && IPAddress.TryParse(parts[index], out _))
            {
                address = parts[index];
                index++;
                continue;
            }

            if (double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
            {
                latencies.Add(milliseconds);
                allTimeout = false;
                index++;
                if (index < parts.Length && parts[index].Equals("ms", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                }

                continue;
            }

            index++;
        }

        return BuildParsedHop(hop, address, latencies, allTimeout);
    }

    /// <summary>
    /// Parses a Windows tracert row.
    /// </summary>
    public static TraceHopResult ParseWindowsHopData(
        int hop,
        string address,
        params string[] latencyStrings)
    {
        var latencies = new List<double>();
        var allTimeout = true;

        foreach (var latencyString in latencyStrings)
        {
            if (latencyString == "*")
            {
                continue;
            }

            var cleaned = latencyString
                .Replace("ms", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("<", string.Empty, StringComparison.Ordinal)
                .Trim();

            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
            {
                latencies.Add(milliseconds);
                allTimeout = false;
            }
        }

        if (string.IsNullOrWhiteSpace(address) || address == "*")
        {
            address = "*";
        }

        return BuildParsedHop(hop, address, latencies, allTimeout);
    }

    /// <summary>
    /// Builds fixed-width clipboard text from hop results.
    /// </summary>
    public static string BuildClipboardText(
        IReadOnlyList<TraceHopResult> results,
        Func<string, string>? localize = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-5}{1,-18}{2,-30}{3,-20}{4}",
            L(localize, "ToolTraceColHop"),
            L(localize, "ToolTraceColAddress"),
            L(localize, "ToolTraceColHostname"),
            L(localize, "ToolTraceColLatency"),
            L(localize, "ToolTraceColStatus")));
        builder.AppendLine(new string('-', 85));

        foreach (var result in results)
        {
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-5}{1,-18}{2,-30}{3,-20}{4}",
                result.Hop,
                result.Address,
                result.Hostname,
                result.Latency,
                L(localize, StatusToKey(result.Status))));
        }

        return builder.ToString();
    }

    private static TraceHopResult BuildParsedHop(
        int hop,
        string address,
        IReadOnlyList<double> latencies,
        bool allTimeout)
    {
        var latencyText = FormatLatency(latencies);
        var status = (allTimeout && address == "*")
            ? HopStatus.Timeout
            : HopStatus.Reply;

        return new TraceHopResult(hop, address, string.Empty, latencyText, status);
    }

    private static string StatusToKey(HopStatus status) => status switch
    {
        HopStatus.Reply => "ToolTraceStatusReply",
        HopStatus.Destination => "ToolTraceStatusDestination",
        HopStatus.Timeout => "ToolTraceStatusTimeout",
        _ => "ToolTraceStatusReply",
    };

    private static string L(Func<string, string>? localize, string key)
        => localize?.Invoke(key) ?? key;

    private static bool LooksLikeAddress(string token)
    {
        var dotCount = token.Count(static c => c == '.');
        return dotCount == 3 || token.Contains(':', StringComparison.Ordinal);
    }
}

/// <summary>
/// Terminal status of a traceroute hop.
/// </summary>
public enum HopStatus
{
    Reply,
    Destination,
    Timeout,
}

/// <summary>
/// Raw single-probe result used by the direct TTL aggregator.
/// </summary>
public readonly record struct HopProbeResult(long LatencyMs, IPAddress? Address);

/// <summary>
/// Aggregated, display-ready hop result.
/// </summary>
public sealed record TraceHopResult(
    int Hop,
    string Address,
    string Hostname,
    string Latency,
    HopStatus Status);

/// <summary>
/// Late-arriving reverse DNS update for an existing hop row.
/// </summary>
public readonly record struct HopHostnameUpdate(int HopIndex, string Address, string Hostname);

/// <summary>
/// Validated traceroute input bundle.
/// </summary>
public sealed record TraceInputs(string Host, int MaxHops);
