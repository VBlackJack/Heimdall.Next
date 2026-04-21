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
/// Input for a single WHOIS lookup. The service trusts its input; callers are
/// responsible for validating the domain or IP before issuing the lookup.
/// </summary>
/// <param name="Domain">Domain name or IP address to query.</param>
public sealed record WhoisLookupRequest(string Domain);

/// <summary>
/// Outcome of a WHOIS lookup. Locale-free: the view layer localizes
/// <see cref="ErrorKey"/> with <see cref="ErrorArg"/> when <see cref="Success"/>
/// is false.
/// </summary>
/// <param name="Output">
/// Raw WHOIS response text, ready for rendering. Empty when <see cref="Success"/>
/// is false.
/// </param>
/// <param name="ElapsedMs">Wall-clock duration of the lookup in milliseconds.</param>
/// <param name="Success">True when the lookup completed without error.</param>
/// <param name="ErrorKey">Localization key describing the error class.</param>
/// <param name="ErrorArg">Optional positional argument for the error key template.</param>
public sealed record WhoisLookupResult(
    string Output,
    long ElapsedMs,
    bool Success,
    string ErrorKey,
    string? ErrorArg)
{
    /// <summary>
    /// Builds a successful result. Null output is normalized to the empty string.
    /// </summary>
    public static WhoisLookupResult Ok(string? output, long elapsedMs)
        => new(output ?? string.Empty, elapsedMs, Success: true, ErrorKey: string.Empty, ErrorArg: null);

    /// <summary>
    /// Builds a failure result. <paramref name="errorKey"/> must be non-empty.
    /// </summary>
    public static WhoisLookupResult Error(string errorKey, long elapsedMs, string? errorArg = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorKey);
        return new(string.Empty, elapsedMs, Success: false, ErrorKey: errorKey, ErrorArg: errorArg);
    }
}
