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

using System.Collections.Generic;

namespace Heimdall.Core.Network;

/// <summary>
/// Input for one TCP ping probe. Callers must have validated host and port
/// before issuing the probe; the service trusts its input.
/// </summary>
public sealed record TcpPingProbeRequest(
    string Host,
    int Port,
    int Seq,
    int TimeoutMs);

/// <summary>
/// Outcome of a single TCP ping probe. Locale-free.
/// </summary>
public enum TcpPingProbeStatus
{
    Success,
    Failed,
}

/// <summary>
/// Raw result of one TCP ping probe, ready for VM-side formatting.
/// </summary>
public sealed record TcpPingProbeResult(
    int Seq,
    string Host,
    int Port,
    double LatencyMs,
    TcpPingProbeStatus Status,
    string? ErrorMessage)
{
    public static TcpPingProbeResult Ok(int seq, string host, int port, double latencyMs)
        => new(seq, host, port, latencyMs, TcpPingProbeStatus.Success, null);

    public static TcpPingProbeResult Failed(int seq, string host, int port, string? errorMessage)
        => new(seq, host, port, -1.0, TcpPingProbeStatus.Failed, errorMessage ?? string.Empty);
}

/// <summary>
/// Aggregated statistics computed from a sequence of TCP ping probes.
/// </summary>
public sealed record TcpPingSummary(
    double MinMs,
    double AvgMs,
    double MaxMs,
    int Lost,
    int Total)
{
    /// <summary>
    /// Builds a summary from the given probes. Returns null when
    /// <paramref name="probes"/> is null or empty.
    /// </summary>
    public static TcpPingSummary? FromProbes(IReadOnlyList<TcpPingProbeResult>? probes)
    {
        if (probes is null || probes.Count == 0)
        {
            return null;
        }

        double min = double.MaxValue;
        double max = 0.0;
        double total = 0.0;
        var successCount = 0;
        var lostCount = 0;

        foreach (var probe in probes)
        {
            if (probe.Status == TcpPingProbeStatus.Success && probe.LatencyMs >= 0)
            {
                successCount++;
                total += probe.LatencyMs;
                if (probe.LatencyMs < min)
                {
                    min = probe.LatencyMs;
                }

                if (probe.LatencyMs > max)
                {
                    max = probe.LatencyMs;
                }
            }
            else
            {
                lostCount++;
            }
        }

        if (successCount == 0)
        {
            return new TcpPingSummary(0.0, 0.0, 0.0, lostCount, probes.Count);
        }

        var avg = total / successCount;
        return new TcpPingSummary(min, avg, max, lostCount, probes.Count);
    }
}
