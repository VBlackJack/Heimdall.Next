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

// ── Enumerations ─────────────────────────────────────────────────────

/// <summary>
/// Controls the breadth and duration of the audit scan.
/// Quick skips slow probes; Deep enables brute-force credential checks
/// and extended cipher enumeration.
/// </summary>
public enum AuditDepth { Quick, Standard, Deep }

/// <summary>
/// Outcome status of a single audit check.
/// </summary>
public enum AuditStatus { Pass, Warning, Fail, Error, Skipped }

// ── Scope & Options ──────────────────────────────────────────────────

/// <summary>
/// Defines what the audit targets: an explicit host list, a CIDR subnet,
/// and/or a gateway to tunnel through.
/// </summary>
public record AuditScope(
    List<string> Targets,
    string? Subnet,
    string? GatewayId);

/// <summary>
/// Toggles for individual audit chapters and overall depth.
/// </summary>
public record AuditOptions(
    AuditDepth Depth = AuditDepth.Standard,
    bool CheckNetwork = true,
    bool CheckCrypto = true,
    bool CheckAccess = true,
    bool CheckOperations = true);

// ── Report hierarchy ─────────────────────────────────────────────────

/// <summary>
/// Root container for a complete SecNumCloud audit run.
/// <see cref="NetworkSnapshot"/> is typed as <c>object?</c> to avoid
/// a circular reference from Security back into Discovery models;
/// callers cast to <c>NetworkScanSnapshot</c> at the App layer.
/// </summary>
public sealed class AuditReport
{
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; set; }
    public required AuditScope Scope { get; init; }
    public List<AuditChapter> Chapters { get; init; } = [];
    public object? NetworkSnapshot { get; set; }
}

/// <summary>
/// A chapter groups related checks under a single SecNumCloud reference section
/// (e.g. "Network Security", "Cryptography").
/// </summary>
public sealed class AuditChapter
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string SecNumCloudRef { get; init; }
    public List<AuditCheck> Checks { get; init; } = [];

    public int PassCount => Checks.Count(c => c.Status == AuditStatus.Pass);
    public int WarnCount => Checks.Count(c => c.Status == AuditStatus.Warning);
    public int FailCount => Checks.Count(c => c.Status == AuditStatus.Fail);
}

/// <summary>
/// A single verifiable audit control mapped to a SecNumCloud clause.
/// Status and Summary are populated by the engine after execution.
/// </summary>
public sealed class AuditCheck
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string SecNumCloudClause { get; init; }
    public AuditStatus Status { get; set; } = AuditStatus.Skipped;
    public string Summary { get; set; } = "";
    public List<AuditEvidence> Evidence { get; init; } = [];
}

/// <summary>
/// A single piece of evidence collected during a check, tied to a specific host.
/// <see cref="RawData"/> may contain banner text, header dumps, or probe output.
/// </summary>
public sealed class AuditEvidence
{
    public required string Host { get; init; }
    public required string Detail { get; init; }
    public string RawData { get; init; } = "";
}
