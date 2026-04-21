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

namespace Heimdall.Core.Security;

/// <summary>
/// Identifies which DNS security control a <see cref="DnsCheckResult"/> represents.
/// </summary>
public enum DnsCheckKind
{
    Spf,
    Dkim,
    Dmarc,
    Caa,
    Dnssec,
    Mx,
}

/// <summary>
/// Outcome of a single DNS security check.
/// </summary>
public enum DnsCheckStatus
{
    Pass,
    Warn,
    Fail,
}

/// <summary>
/// Overall posture of a domain across the six DNS security checks, mapped to
/// the four thresholds used for the summary banner colour.
/// </summary>
public enum DnsSummaryStatus
{
    /// <summary>All checks passed (ratio &gt;= 1.0).</summary>
    AllPass,

    /// <summary>At least two thirds of checks passed (ratio &gt;= 0.66).</summary>
    Good,

    /// <summary>At least one third of checks passed (ratio &gt;= 0.33).</summary>
    Partial,

    /// <summary>Fewer than one third of checks passed, or no checks evaluated.</summary>
    Bad,
}

/// <summary>
/// Neutral result for a single DNS security check. Locale-free: the view layer
/// is responsible for localizing <see cref="DetailKey"/> with <see cref="DetailArgs"/>
/// and for substituting a "no record" placeholder when <see cref="RawRecord"/> is empty.
/// </summary>
/// <param name="Kind">Which control was evaluated.</param>
/// <param name="Status">Outcome classification.</param>
/// <param name="RawRecord">
/// Raw record text (e.g. <c>v=spf1 ...</c>), or the raw error message when the
/// check failed due to an I/O exception. Empty string means "no record was found";
/// the view layer must render a localized placeholder in that case.
/// </param>
/// <param name="DetailKey">
/// Localization key describing the finding. Empty string means no detail line
/// should be rendered (used for I/O error results whose message is already in
/// <paramref name="RawRecord"/>).
/// </param>
/// <param name="DetailArgs">
/// Positional arguments for <see cref="string.Format(System.IFormatProvider, string, object?[])"/>
/// when the localized detail template is resolved. Empty when unused.
/// </param>
public sealed record DnsCheckResult(
    DnsCheckKind Kind,
    DnsCheckStatus Status,
    string RawRecord,
    string DetailKey,
    IReadOnlyList<string> DetailArgs);

/// <summary>
/// Neutral aggregate of the six DNS security checks for a domain.
/// </summary>
/// <param name="Domain">Normalized domain that was evaluated.</param>
/// <param name="Results">All check results in a stable order (SPF, DKIM, DMARC, CAA, DNSSEC, MX).</param>
/// <param name="PassCount">Number of <see cref="DnsCheckStatus.Pass"/> results.</param>
/// <param name="Total">Total number of checks performed (typically 6).</param>
/// <param name="Summary">Overall posture derived from the pass ratio.</param>
public sealed record DnsSecurityReport(
    string Domain,
    IReadOnlyList<DnsCheckResult> Results,
    int PassCount,
    int Total,
    DnsSummaryStatus Summary);
