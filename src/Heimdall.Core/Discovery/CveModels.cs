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

namespace Heimdall.Core.Discovery;

/// <summary>
/// Severity assigned to a CVE entry.
/// </summary>
public enum CveSeverity
{
    None,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// Neutral CVE match returned by the lookup engine.
/// </summary>
public sealed record CveMatch(
    string Id,
    double CvssScore,
    CveSeverity Severity,
    string Summary,
    string AffectedVersions);

/// <summary>
/// Complete search result including the query resolved by banner parsing or fuzzy matching.
/// </summary>
public sealed record CveSearchResult(
    string ResolvedQuery,
    IReadOnlyList<CveMatch> Matches);
