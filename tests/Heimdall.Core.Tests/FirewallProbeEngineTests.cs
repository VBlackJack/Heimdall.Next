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

public class FirewallProbeEngineTests
{
    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal(3000, FirewallProbeEngine.ConnectTimeoutMs);
        Assert.Equal(20, FirewallProbeEngine.MaxConcurrentDirect);
        Assert.Equal(10, FirewallProbeEngine.MaxConcurrentTunnel);
        Assert.Equal(50, FirewallProbeEngine.MaxHosts);
        Assert.Equal(50, FirewallProbeEngine.MaxPorts);
    }

    [Fact]
    public void ParseHosts_Empty_ReturnsEmpty()
    {
        var hosts = FirewallProbeEngine.ParseHosts(string.Empty);
        Assert.Empty(hosts);
    }

    [Fact]
    public void ParseHosts_RemovesWhitespaceEmptyAndDuplicates()
    {
        var hosts = FirewallProbeEngine.ParseHosts(" host1 \r\n\r\nhost2\nhost1\n  host3  ");
        Assert.Equal(["host1", "host2", "host3"], hosts);
    }

    [Fact]
    public void ComputeSummary_CountsStatuses()
    {
        var summary = FirewallProbeEngine.ComputeSummary(
        [
            new FwProbeResult("a", 22, ProbeStatus.Open, 10),
            new FwProbeResult("a", 80, ProbeStatus.Closed, 20),
            new FwProbeResult("b", 22, ProbeStatus.Timeout, 30),
            new FwProbeResult("b", 80, ProbeStatus.Open, 40),
        ]);

        Assert.Equal(2, summary.Open);
        Assert.Equal(1, summary.Closed);
        Assert.Equal(1, summary.Timeout);
        Assert.Equal(4, summary.Total);
    }

    [Fact]
    public void BuildMatrixCsv_EmptyResults_ReturnsHeaderAndRows()
    {
        var csv = FirewallProbeEngine.BuildMatrixCsv([], ["host1"], [22]);
        var lines = csv.Trim().Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("ToolFwColHost,22", lines[0].Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMatrixCsv_UsesLocalizedStatusAndResponseTime()
    {
        string Localize(string key) => key switch
        {
            "ToolFwColHost" => "Host",
            "ToolFwStatusOpen" => "Open",
            "ToolFwStatusClosed" => "Blocked",
            "ToolFwStatusTimeout" => "Timeout",
            _ => key,
        };

        var csv = FirewallProbeEngine.BuildMatrixCsv(
        [
            new FwProbeResult("host1", 22, ProbeStatus.Open, 15),
            new FwProbeResult("host1", 80, ProbeStatus.Closed, 5),
        ],
        ["host1"],
        [22, 80],
        Localize);

        Assert.Contains("Open (15ms)", csv);
        Assert.Contains("Blocked", csv);
    }

    [Fact]
    public void BuildMatrixCsv_SanitizesHostsAndCells()
    {
        var csv = FirewallProbeEngine.BuildMatrixCsv(
        [
            new FwProbeResult("=host", 22, ProbeStatus.Open, 10),
        ],
        ["=host"],
        [22]);

        Assert.Contains("\"'=host\"", csv);
        Assert.Contains("ToolFwStatusOpen (10ms)", csv);
    }

    [Fact]
    public void BuildMatrixText_UsesFixedWidthLayout()
    {
        var text = FirewallProbeEngine.BuildMatrixText(
        [
            new FwProbeResult("host1", 22, ProbeStatus.Open, 15),
            new FwProbeResult("host1", 80, ProbeStatus.Closed, 5),
        ],
        ["host1"],
        [22, 80]);

        Assert.Contains("host1", text);
        Assert.Contains("ToolFwStatusOpen", text);
        Assert.Contains("ToolFwStatusClosed", text);
    }

    [Fact]
    public void BuildMatrixText_UsesTimeoutForMissingCells()
    {
        var text = FirewallProbeEngine.BuildMatrixText(
        [
            new FwProbeResult("host1", 22, ProbeStatus.Open, 15),
        ],
        ["host1"],
        [22, 80]);

        Assert.Contains("ToolFwStatusTimeout", text);
    }

    [Fact]
    public void FwProbeResult_StoresValues()
    {
        var result = new FwProbeResult("host1", 443, ProbeStatus.Open, 42);
        Assert.Equal("host1", result.Host);
        Assert.Equal(443, result.Port);
        Assert.Equal(ProbeStatus.Open, result.Status);
        Assert.Equal(42, result.ResponseTimeMs);
    }

    [Fact]
    public void FwProbeSummary_StoresValues()
    {
        var summary = new FwProbeSummary(1, 2, 3, 6);
        Assert.Equal(1, summary.Open);
        Assert.Equal(2, summary.Closed);
        Assert.Equal(3, summary.Timeout);
        Assert.Equal(6, summary.Total);
    }
}
