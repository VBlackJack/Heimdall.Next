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

namespace Heimdall.Core.Network;

/// <summary>
/// Supported DNS record types for the lookup tool. Names match the canonical
/// wire-format tokens accepted by <c>dig</c>, <c>nslookup</c>, and <c>host</c>.
/// </summary>
public enum DnsRecordType
{
    A,
    AAAA,
    MX,
    CNAME,
    TXT,
    NS,
    PTR,
    SOA,
    ANY,
}

/// <summary>
/// Helpers that convert a <see cref="DnsRecordType"/> to and from its
/// wire-format string representation.
/// </summary>
public static class DnsRecordTypeExtensions
{
    /// <summary>
    /// Canonical uppercase token (e.g. "A", "AAAA", "MX") used with CLI tools.
    /// </summary>
    public static string ToWireFormat(this DnsRecordType type) => type switch
    {
        DnsRecordType.A => "A",
        DnsRecordType.AAAA => "AAAA",
        DnsRecordType.MX => "MX",
        DnsRecordType.CNAME => "CNAME",
        DnsRecordType.TXT => "TXT",
        DnsRecordType.NS => "NS",
        DnsRecordType.PTR => "PTR",
        DnsRecordType.SOA => "SOA",
        DnsRecordType.ANY => "ANY",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>
    /// Parses a wire-format token (case-insensitive, whitespace-tolerant) into a
    /// <see cref="DnsRecordType"/>. Returns <c>false</c> for null, empty,
    /// numeric, or unknown tokens.
    /// </summary>
    public static bool TryParseWireFormat(string? value, out DnsRecordType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        // Enum.TryParse accepts numeric strings (e.g. "5" parses to TXT). Reject
        // those so only wire-format identifiers round-trip through this helper.
        foreach (var ch in trimmed)
        {
            if (!char.IsLetter(ch))
            {
                return false;
            }
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out type)
            && Enum.IsDefined(type);
    }
}

/// <summary>
/// Input for a single DNS lookup.
/// </summary>
/// <param name="Hostname">Target domain or hostname. Callers must validate.</param>
/// <param name="RecordType">Which record type to query.</param>
/// <param name="DnsServer">
/// Optional custom DNS server IP. When null, the platform default resolver is used.
/// </param>
public sealed record DnsLookupRequest(
    string Hostname,
    DnsRecordType RecordType,
    string? DnsServer);

/// <summary>
/// Outcome of a DNS lookup. Locale-free: the view layer localizes
/// <see cref="ErrorKey"/> with <see cref="ErrorArg"/> when <see cref="Success"/>
/// is <c>false</c>.
/// </summary>
/// <param name="Output">
/// Formatted result text, ready for direct rendering. Empty string when
/// <see cref="Success"/> is <c>false</c>.
/// </param>
/// <param name="ElapsedMs">Wall-clock duration of the lookup in milliseconds.</param>
/// <param name="Success">True when the lookup completed without error.</param>
/// <param name="ErrorKey">
/// Localization key describing the error class (e.g. <c>ToolDnsErrorTimeout</c>).
/// Empty string when <see cref="Success"/> is <c>true</c>.
/// </param>
/// <param name="ErrorArg">
/// Positional argument for the error key template when it accepts one,
/// or <c>null</c> when the template takes no argument.
/// </param>
public sealed record DnsLookupResult(
    string Output,
    long ElapsedMs,
    bool Success,
    string ErrorKey,
    string? ErrorArg)
{
    /// <summary>
    /// Builds a success result with the given output and elapsed time.
    /// </summary>
    public static DnsLookupResult Ok(string output, long elapsedMs)
        => new(output ?? string.Empty, elapsedMs, Success: true, ErrorKey: string.Empty, ErrorArg: null);

    /// <summary>
    /// Builds a failure result with the given error key, elapsed time, and
    /// optional positional argument.
    /// </summary>
    public static DnsLookupResult Error(string errorKey, long elapsedMs, string? errorArg = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorKey);
        return new(string.Empty, elapsedMs, Success: false, ErrorKey: errorKey, ErrorArg: errorArg);
    }
}
