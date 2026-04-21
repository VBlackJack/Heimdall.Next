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

public class PortScanEngineTests
{
    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal(2000, PortScanEngine.ConnectTimeoutMs);
        Assert.Equal(1000, PortScanEngine.BannerGrabTimeoutMs);
        Assert.Equal(256, PortScanEngine.BannerMaxBytes);
        Assert.Equal(50, PortScanEngine.MaxConcurrent);
        Assert.Equal(10000, PortScanEngine.LargePortCountWarningThreshold);
    }

    [Fact]
    public void BuildCsvExport_EmptyResults_ReturnsHeaderOnly()
    {
        var csv = PortScanEngine.BuildCsvExport([]);
        Assert.Single(csv.Trim().Split('\n'));
    }

    [Fact]
    public void BuildCsvExport_WithResults_IncludesData()
    {
        var results = new[]
        {
            new PortScanResult(22, true, "SSH", "5 ms", "Open", "SSH-2.0-OpenSSH"),
        };

        var csv = PortScanEngine.BuildCsvExport(results);
        var lines = csv.Trim().Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("SSH", lines[1]);
        Assert.Contains("22", lines[1]);
    }

    [Fact]
    public void BuildCsvExport_SortsByPort()
    {
        var results = new[]
        {
            new PortScanResult(443, true, "HTTPS", "10 ms", "Open", string.Empty),
            new PortScanResult(22, true, "SSH", "5 ms", "Open", string.Empty),
        };

        var csv = PortScanEngine.BuildCsvExport(results);
        var lines = csv.Trim().Split('\n');
        Assert.Contains("22", lines[1]);
        Assert.Contains("443", lines[2]);
    }

    [Fact]
    public void BuildCsvExport_SanitizesCsvInjection()
    {
        var results = new[]
        {
            new PortScanResult(80, true, "HTTP", "10 ms", "Open", "=cmd()"),
        };

        var csv = PortScanEngine.BuildCsvExport(results);
        Assert.Contains("\"'=cmd()\"", csv);
        Assert.DoesNotContain(",\"=cmd()\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCsvExport_WithLocalize_UsesLocalizedHeader()
    {
        string Localize(string key) => key == "ToolPortScanCsvHeader" ? "Port,Statut,Service,Temps,Bannière" : key;

        var csv = PortScanEngine.BuildCsvExport([], Localize);
        Assert.StartsWith("Port,Statut,Service,Temps,Bannière", csv.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCsvExport_EscapesQuotesInBanner()
    {
        var results = new[]
        {
            new PortScanResult(22, true, "SSH", "5 ms", "Open", "banner with \"quotes\""),
        };

        var csv = PortScanEngine.BuildCsvExport(results);
        Assert.Contains("\"\"quotes\"\"", csv);
    }

    [Fact]
    public void BuildClipboardText_EmptyResults_ReturnsHeaderAndSeparator()
    {
        var text = PortScanEngine.BuildClipboardText([]);
        var lines = text.Trim().Split('\n');
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void BuildClipboardText_WithResults_IncludesData()
    {
        var results = new[]
        {
            new PortScanResult(22, true, "SSH", "5 ms", "Open", string.Empty),
        };

        var text = PortScanEngine.BuildClipboardText(results);
        Assert.Contains("22", text);
        Assert.Contains("Open", text);
        Assert.Contains("SSH", text);
    }

    [Fact]
    public void BuildClipboardText_WithLocalize_UsesLocalizedHeaders()
    {
        string Localize(string key) => key switch
        {
            "ToolPortScanColPort" => "Port",
            "ToolPortScanColStatus" => "Statut",
            "ToolPortScanColService" => "Service",
            "ToolPortScanColResponseTime" => "Temps",
            _ => key,
        };

        var text = PortScanEngine.BuildClipboardText([], Localize);
        Assert.Contains("Statut", text);
    }

    [Fact]
    public void PortProbeResult_StoresValues()
    {
        var result = new PortProbeResult(22, true, "SSH", "5 ms", "SSH-2.0");
        Assert.Equal(22, result.Port);
        Assert.True(result.IsOpen);
        Assert.Equal("SSH", result.Service);
        Assert.Equal("5 ms", result.ResponseTime);
        Assert.Equal("SSH-2.0", result.Banner);
    }

    [Fact]
    public void PortScanResult_StoresValues()
    {
        var result = new PortScanResult(443, true, "HTTPS", "10 ms", "Open", string.Empty);
        Assert.Equal(443, result.Port);
        Assert.True(result.IsOpen);
        Assert.Equal("Open", result.Status);
    }
}
