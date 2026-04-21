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

using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public sealed class TcpPingModelsTests
{
    [Fact]
    public void TcpPingProbeRequest_StoresCtorValues()
    {
        var request = new TcpPingProbeRequest("example.com", 443, 3, 5000);

        Assert.Equal("example.com", request.Host);
        Assert.Equal(443, request.Port);
        Assert.Equal(3, request.Seq);
        Assert.Equal(5000, request.TimeoutMs);
    }

    [Fact]
    public void TcpPingProbeResult_Ok_SetsSuccessFields()
    {
        var result = TcpPingProbeResult.Ok(1, "example.com", 443, 42.5);

        Assert.Equal(TcpPingProbeStatus.Success, result.Status);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(42.5, result.LatencyMs);
    }

    [Fact]
    public void TcpPingProbeResult_Failed_SetsFailedFields()
    {
        var result = TcpPingProbeResult.Failed(2, "example.com", 443, "Timeout");

        Assert.Equal(TcpPingProbeStatus.Failed, result.Status);
        Assert.Equal(-1.0, result.LatencyMs);
        Assert.Equal("Timeout", result.ErrorMessage);
    }

    [Fact]
    public void TcpPingProbeResult_Failed_NullMessage_NormalizesToEmpty()
    {
        var result = TcpPingProbeResult.Failed(2, "example.com", 443, null);

        Assert.Equal(string.Empty, result.ErrorMessage);
    }

    [Fact]
    public void TcpPingSummary_FromProbes_Null_ReturnsNull()
    {
        var summary = TcpPingSummary.FromProbes(null);

        Assert.Null(summary);
    }

    [Fact]
    public void TcpPingSummary_FromProbes_Empty_ReturnsNull()
    {
        var summary = TcpPingSummary.FromProbes([]);

        Assert.Null(summary);
    }

    [Fact]
    public void TcpPingSummary_FromProbes_AllSuccess_ComputesAggregates()
    {
        var probes = new[]
        {
            TcpPingProbeResult.Ok(1, "h", 443, 10.0),
            TcpPingProbeResult.Ok(2, "h", 443, 20.0),
            TcpPingProbeResult.Ok(3, "h", 443, 30.0),
        };

        var summary = TcpPingSummary.FromProbes(probes);

        Assert.NotNull(summary);
        Assert.Equal(10.0, summary!.MinMs);
        Assert.Equal(20.0, summary.AvgMs);
        Assert.Equal(30.0, summary.MaxMs);
        Assert.Equal(0, summary.Lost);
        Assert.Equal(3, summary.Total);
    }

    [Fact]
    public void TcpPingSummary_FromProbes_MixedSuccessAndLoss_ComputesAggregates()
    {
        var probes = new[]
        {
            TcpPingProbeResult.Ok(1, "h", 443, 5.0),
            TcpPingProbeResult.Failed(2, "h", 443, "Timeout"),
            TcpPingProbeResult.Ok(3, "h", 443, 15.0),
        };

        var summary = TcpPingSummary.FromProbes(probes);

        Assert.NotNull(summary);
        Assert.Equal(5.0, summary!.MinMs);
        Assert.Equal(10.0, summary.AvgMs);
        Assert.Equal(15.0, summary.MaxMs);
        Assert.Equal(1, summary.Lost);
        Assert.Equal(3, summary.Total);
    }

    [Fact]
    public void TcpPingSummary_FromProbes_AllLost_UsesZeroSentinel()
    {
        var probes = new[]
        {
            TcpPingProbeResult.Failed(1, "h", 443, "Timeout"),
            TcpPingProbeResult.Failed(2, "h", 443, "Timeout"),
            TcpPingProbeResult.Failed(3, "h", 443, "Timeout"),
        };

        var summary = TcpPingSummary.FromProbes(probes);

        Assert.NotNull(summary);
        Assert.Equal(0.0, summary!.MinMs);
        Assert.Equal(0.0, summary.AvgMs);
        Assert.Equal(0.0, summary.MaxMs);
        Assert.Equal(3, summary.Lost);
        Assert.Equal(3, summary.Total);
    }

    [Fact]
    public void TcpPingSummary_FromProbes_SingleSuccess_UsesSameValueForAllAggregates()
    {
        var summary = TcpPingSummary.FromProbes(
        [
            TcpPingProbeResult.Ok(1, "h", 443, 42.5),
        ]);

        Assert.NotNull(summary);
        Assert.Equal(42.5, summary!.MinMs);
        Assert.Equal(42.5, summary.AvgMs);
        Assert.Equal(42.5, summary.MaxMs);
        Assert.Equal(0, summary.Lost);
        Assert.Equal(1, summary.Total);
    }

    [Fact]
    public void TcpPingSummary_RecordEquality_WorksForIdenticalValues()
    {
        var left = new TcpPingSummary(1.0, 2.0, 3.0, 4, 5);
        var right = new TcpPingSummary(1.0, 2.0, 3.0, 4, 5);

        Assert.Equal(left, right);
    }
}
