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

namespace Heimdall.Core.SessionHealth;

/// <summary>
/// Immutable snapshot of a server's last probe attempt. <see cref="Reason"/>
/// carries a short machine-friendly tag (e.g. "timeout", "refused",
/// "behind-gateway", "no-port", "no-host") so the sidebar tooltip can localize
/// it without parsing free-text exception messages.
/// </summary>
public sealed record HealthState(
    HealthStatus Status,
    DateTime LastCheckUtc,
    int? LatencyMs,
    string? Reason)
{
    /// <summary>Initial state used before the first probe cycle runs.</summary>
    public static readonly HealthState Initial = new(HealthStatus.Unknown, DateTime.MinValue, null, null);
}
