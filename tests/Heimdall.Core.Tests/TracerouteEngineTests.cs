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

using System.Net;
using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public class TracerouteEngineTests
{
    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal(30, TracerouteEngine.DefaultMaxHops);
        Assert.Equal(3, TracerouteEngine.ProbesPerHop);
        Assert.Equal(1, TracerouteEngine.MinMaxHops);
        Assert.Equal(128, TracerouteEngine.MaxMaxHops);
        Assert.Equal(3000, TracerouteEngine.PingTimeoutMs);
        Assert.Equal(32, TracerouteEngine.PingBufferSize);
    }

    [Fact]
    public void ValidateInputs_EmptyHost_ReturnsHostRequired()
    {
        var (inputs, errorKey) = TracerouteEngine.ValidateInputs("", "30");

        Assert.Null(inputs);
        Assert.Equal("ToolValidationHostRequired", errorKey);
    }

    [Fact]
    public void ValidateInputs_InvalidHost_ReturnsInvalid()
    {
        var (inputs, errorKey) = TracerouteEngine.ValidateInputs("not a valid host!@#", "30");

        Assert.Null(inputs);
        Assert.Equal("ErrorInvalidHost", errorKey);
    }

    [Fact]
    public void ValidateInputs_ValidHost_ReturnsInputs()
    {
        var (inputs, errorKey) = TracerouteEngine.ValidateInputs("1.1.1.1", "25");

        Assert.Null(errorKey);
        Assert.NotNull(inputs);
        Assert.Equal("1.1.1.1", inputs!.Host);
        Assert.Equal(25, inputs.MaxHops);
    }

    [Fact]
    public void ValidateInputs_TrimsHost()
    {
        var (inputs, _) = TracerouteEngine.ValidateInputs("  1.1.1.1  ", "10");
        Assert.Equal("1.1.1.1", inputs!.Host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("129")]
    [InlineData("-5")]
    public void ValidateInputs_BadMaxHops_FallsBackToDefault(string maxHopsText)
    {
        var (inputs, errorKey) = TracerouteEngine.ValidateInputs("1.1.1.1", maxHopsText);

        Assert.Null(errorKey);
        Assert.Equal(TracerouteEngine.DefaultMaxHops, inputs!.MaxHops);
    }

    [Fact]
    public void FormatLatency_Long_Empty_Star()
    {
        Assert.Equal("*", TracerouteEngine.FormatLatency(Array.Empty<long>()));
    }

    [Fact]
    public void FormatLatency_Long_Single_OneValue()
    {
        Assert.Equal("12 ms", TracerouteEngine.FormatLatency(new long[] { 12 }));
    }

    [Fact]
    public void FormatLatency_Long_Multiple_MinAvgMax()
    {
        var result = TracerouteEngine.FormatLatency(new long[] { 10, 20, 30 });
        Assert.Equal("10/20/30 ms", result);
    }

    [Fact]
    public void FormatLatency_Double_Multiple_Rounded()
    {
        var result = TracerouteEngine.FormatLatency(new[] { 1.4, 1.6, 2.0 });
        Assert.Equal("1/2/2 ms", result);
    }

    [Fact]
    public void AggregateHopProbes_AllTimeout_StatusIsTimeout()
    {
        var target = IPAddress.Parse("8.8.8.8");
        var probes = new[]
        {
            new HopProbeResult(-1, null),
            new HopProbeResult(-1, null),
            new HopProbeResult(-1, null),
        };

        var hop = TracerouteEngine.AggregateHopProbes(1, probes, target);

        Assert.Equal(HopStatus.Timeout, hop.Status);
        Assert.Equal("*", hop.Address);
        Assert.Equal("*", hop.Latency);
    }

    [Fact]
    public void AggregateHopProbes_DestinationMatch_StatusIsDestination()
    {
        var target = IPAddress.Parse("8.8.8.8");
        var probes = new[]
        {
            new HopProbeResult(10, target),
            new HopProbeResult(20, target),
            new HopProbeResult(30, target),
        };

        var hop = TracerouteEngine.AggregateHopProbes(5, probes, target);

        Assert.Equal(HopStatus.Destination, hop.Status);
        Assert.Equal("8.8.8.8", hop.Address);
        Assert.Equal("10/20/30 ms", hop.Latency);
    }

    [Fact]
    public void AggregateHopProbes_IntermediateHop_StatusIsReply()
    {
        var target = IPAddress.Parse("8.8.8.8");
        var hopAddress = IPAddress.Parse("10.0.0.1");
        var probes = new[]
        {
            new HopProbeResult(5, hopAddress),
            new HopProbeResult(-1, null),
            new HopProbeResult(6, hopAddress),
        };

        var hop = TracerouteEngine.AggregateHopProbes(1, probes, target);

        Assert.Equal(HopStatus.Reply, hop.Status);
        Assert.Equal("10.0.0.1", hop.Address);
        Assert.Contains("5", hop.Latency, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseLine_LinuxStandard_ParsesCorrectly()
    {
        var hop = 0;
        var result = TracerouteEngine.ParseTracerouteLine(
            " 1  10.0.0.1  1.234 ms  1.123 ms  0.987 ms",
            ref hop);

        Assert.NotNull(result);
        Assert.Equal(1, hop);
        Assert.Equal(1, result!.Hop);
        Assert.Equal("10.0.0.1", result.Address);
        Assert.Equal(HopStatus.Reply, result.Status);
    }

    [Fact]
    public void ParseLine_LinuxAllTimeouts_IsTimeout()
    {
        var hop = 0;
        var result = TracerouteEngine.ParseTracerouteLine(" 3  * * *", ref hop);

        Assert.NotNull(result);
        Assert.Equal(HopStatus.Timeout, result!.Status);
        Assert.Equal("*", result.Address);
    }

    [Fact]
    public void ParseLine_LinuxMixedTimeout_ExtractsAddressAndLatency()
    {
        var hop = 0;
        var result = TracerouteEngine.ParseTracerouteLine(
            " 2  10.0.0.1  1.234 ms  *  0.987 ms",
            ref hop);

        Assert.NotNull(result);
        Assert.Equal("10.0.0.1", result!.Address);
        Assert.Equal(HopStatus.Reply, result.Status);
    }

    [Fact]
    public void ParseLine_WindowsStandard_ParsesCorrectly()
    {
        var hop = 0;
        var result = TracerouteEngine.ParseTracerouteLine(
            "  1    <1 ms    <1 ms    <1 ms  10.0.0.1",
            ref hop);

        Assert.NotNull(result);
        Assert.Equal(1, hop);
        Assert.Equal("10.0.0.1", result!.Address);
        Assert.Equal(HopStatus.Reply, result.Status);
    }

    [Fact]
    public void ParseLine_WindowsLessThanMs_StripsPrefix()
    {
        var hop = 0;
        var result = TracerouteEngine.ParseTracerouteLine(
            "  2    5 ms    <1 ms    3 ms  10.0.0.2",
            ref hop);

        Assert.NotNull(result);
        Assert.Contains("ms", result!.Latency, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("traceroute to 8.8.8.8, 30 hops max, 60 byte packets")]
    [InlineData("Tracing route to 8.8.8.8 [8.8.8.8]")]
    [InlineData("over a maximum of 30 hops:")]
    [InlineData("Trace complete.")]
    [InlineData("")]
    [InlineData("  ")]
    public void ParseLine_SkipsHeadersAndEmpty(string line)
    {
        var hop = 0;
        var result = TracerouteEngine.ParseTracerouteLine(line, ref hop);
        Assert.Null(result);
    }

    [Fact]
    public void ParseLinuxHopData_SkipsLeadingStar()
    {
        var result = TracerouteEngine.ParseLinuxHopData(5, "* 10.0.0.5  2.1 ms  2.2 ms");

        Assert.Equal("10.0.0.5", result.Address);
        Assert.Equal(HopStatus.Reply, result.Status);
    }

    [Fact]
    public void BuildClipboardText_Empty_ReturnsHeaderAndSeparator()
    {
        var text = TracerouteEngine.BuildClipboardText([]);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains("ToolTraceColHop", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("----", lines[1].Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildClipboardText_WithHops_IncludesRows()
    {
        var hops = new[]
        {
            new TraceHopResult(1, "10.0.0.1", "gw.local", "1 ms", HopStatus.Reply),
            new TraceHopResult(2, "8.8.8.8", "dns.google", "20 ms", HopStatus.Destination),
        };

        var text = TracerouteEngine.BuildClipboardText(hops);

        Assert.Contains("10.0.0.1", text, StringComparison.Ordinal);
        Assert.Contains("dns.google", text, StringComparison.Ordinal);
        Assert.Contains("ToolTraceStatusDestination", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildClipboardText_UsesLocalize_WhenProvided()
    {
        static string Localize(string key) => key switch
        {
            "ToolTraceColHop" => "Saut",
            "ToolTraceStatusReply" => "Reply",
            _ => key,
        };

        var hops = new[]
        {
            new TraceHopResult(1, "10.0.0.1", string.Empty, "1 ms", HopStatus.Reply),
        };

        var text = TracerouteEngine.BuildClipboardText(hops, Localize);

        Assert.Contains("Saut", text, StringComparison.Ordinal);
        Assert.Contains("Reply", text, StringComparison.Ordinal);
    }
}
