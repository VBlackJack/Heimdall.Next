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

public class BannerGrabEngineTests
{
    [Fact]
    public void ParseBanner_NullInput_ReturnsNull()
    {
        Assert.Null(BannerGrabEngine.ParseBanner(null));
    }

    [Fact]
    public void ParseBanner_EmptyInput_ReturnsNull()
    {
        Assert.Null(BannerGrabEngine.ParseBanner(string.Empty));
        Assert.Null(BannerGrabEngine.ParseBanner("   "));
    }

    [Fact]
    public void ParseBanner_CleanText_ReturnsTrimmed()
    {
        Assert.Equal("SSH-2.0-OpenSSH_8.9", BannerGrabEngine.ParseBanner("  SSH-2.0-OpenSSH_8.9  "));
    }

    [Fact]
    public void ParseBanner_ControlChars_AreRemoved()
    {
        var result = BannerGrabEngine.ParseBanner("Hello\x01World\x7F Test");

        Assert.NotNull(result);
        Assert.Equal("Hello World Test", result);
    }

    [Fact]
    public void ParseBanner_OnlyControlChars_ReturnsNull()
    {
        Assert.Null(BannerGrabEngine.ParseBanner("\x01\x02\x03"));
    }

    [Fact]
    public void ParseBanner_MultipleSpaces_Collapsed()
    {
        Assert.Equal("Hello World", BannerGrabEngine.ParseBanner("Hello    World"));
    }

    [Theory]
    [InlineData("SSH-2.0-OpenSSH_8.9p1", "OpenSSH")]
    [InlineData("SSH-2.0-dropbear_2022.83", "Dropbear SSH")]
    [InlineData("Server: Apache/2.4.52", "Apache HTTP")]
    [InlineData("Server: nginx/1.24.0", "nginx")]
    [InlineData("Server: Microsoft-IIS/10.0", "IIS")]
    [InlineData("220 mail.example.com ESMTP Postfix", "Postfix SMTP")]
    [InlineData("220 mail.example.com Exim 4.96", "Exim SMTP")]
    [InlineData("* OK [CAPABILITY IMAP4rev1] Dovecot ready.", "Dovecot")]
    [InlineData("5.7.38-log MySQL Community Server", "MySQL")]
    [InlineData("PostgreSQL 15.2 on x86_64-pc-linux-gnu", "PostgreSQL")]
    [InlineData("$6\r\nredis_version:7.0.11", "Redis")]
    [InlineData("It looks like you are trying to access MongoDB", "MongoDB")]
    [InlineData("220 ProFTPD 1.3.8", "ProFTPD")]
    [InlineData("220 (vsFTPd 3.0.5)", "vsftpd")]
    [InlineData("220 FileZilla Server 1.7.3", "FileZilla FTP")]
    public void IdentifyService_WithBanner_ReturnsEnriched(string banner, string expected)
    {
        Assert.Equal(expected, BannerGrabEngine.IdentifyService(22, banner));
    }

    [Fact]
    public void IdentifyService_NoBanner_ReturnsBaseLabel()
    {
        var result = BannerGrabEngine.IdentifyService(22, null, port => port == 22 ? "SSH" : string.Empty);
        Assert.Equal("SSH", result);
    }

    [Fact]
    public void IdentifyService_NoBannerNoResolver_ReturnsPortNumber()
    {
        Assert.Equal("9999", BannerGrabEngine.IdentifyService(9999, null));
    }

    [Fact]
    public void IdentifyService_UnknownBanner_ReturnsBaseLabel()
    {
        var result = BannerGrabEngine.IdentifyService(80, "some random banner", _ => "HTTP");
        Assert.Equal("HTTP", result);
    }

    [Fact]
    public void IdentifyService_CaseInsensitive()
    {
        Assert.Equal("OpenSSH", BannerGrabEngine.IdentifyService(22, "ssh-2.0-OPENSSH_9.0"));
    }

    [Fact]
    public void IdentifyService_PgsqlAlias()
    {
        Assert.Equal("PostgreSQL", BannerGrabEngine.IdentifyService(5432, "pgsql driver error"));
    }

    [Fact]
    public void IdentifyService_MongodAlias()
    {
        Assert.Equal("MongoDB", BannerGrabEngine.IdentifyService(27017, "mongod version 6.0"));
    }

    [Fact]
    public void ParsePorts_CommaSeparated_ParsesCorrectly()
    {
        var result = BannerGrabEngine.ParsePorts("22,80,443");
        Assert.Equal([22, 80, 443], result);
    }

    [Fact]
    public void ParsePorts_WithRange_Expands()
    {
        var result = BannerGrabEngine.ParsePorts("80-82");
        Assert.Equal([80, 81, 82], result);
    }

    [Fact]
    public void ParsePorts_Mixed_ParsesAndSorts()
    {
        var result = BannerGrabEngine.ParsePorts("443,22,80-82");
        Assert.Equal([22, 80, 81, 82, 443], result);
    }

    [Fact]
    public void ParsePorts_Duplicates_Deduped()
    {
        var result = BannerGrabEngine.ParsePorts("22,22,80,22");
        Assert.Equal([22, 80], result);
    }

    [Fact]
    public void ParsePorts_Invalid_Skipped()
    {
        var result = BannerGrabEngine.ParsePorts("22,abc,80");
        Assert.Equal([22, 80], result);
    }

    [Fact]
    public void ParsePorts_OutOfRange_Skipped()
    {
        var result = BannerGrabEngine.ParsePorts("0,22,70000");
        Assert.Equal([22], result);
    }

    [Fact]
    public void ParsePorts_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(BannerGrabEngine.ParsePorts(string.Empty));
        Assert.Empty(BannerGrabEngine.ParsePorts("   "));
    }

    [Fact]
    public void ParsePorts_ReversedRange_Skipped()
    {
        Assert.Empty(BannerGrabEngine.ParsePorts("443-80"));
    }

    [Fact]
    public void ParsePorts_WithSpaces_Trims()
    {
        var result = BannerGrabEngine.ParsePorts(" 22 , 80 , 443 ");
        Assert.Equal([22, 80, 443], result);
    }

    [Fact]
    public void BuildCsvExport_EmptyResults_ReturnsHeaderOnly()
    {
        var csv = BannerGrabEngine.BuildCsvExport([]);
        Assert.Single(csv.Trim().Split('\n'));
    }

    [Fact]
    public void BuildCsvExport_WithResults_IncludesData()
    {
        var results = new[]
        {
            new BannerResult { Port = 22, Service = "OpenSSH", Banner = "SSH-2.0", ResponseTime = "5 ms", HasBanner = true },
        };

        var csv = BannerGrabEngine.BuildCsvExport(results);
        var lines = csv.Trim().Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("OpenSSH", lines[1]);
    }

    [Fact]
    public void BuildCsvExport_SanitizesCsvInjection()
    {
        var results = new[]
        {
            new BannerResult { Port = 80, Service = "HTTP", Banner = "=cmd()", ResponseTime = "10 ms", HasBanner = true },
        };

        var csv = BannerGrabEngine.BuildCsvExport(results);
        Assert.Contains("\"'=cmd()\"", csv);
        Assert.DoesNotContain(",\"=cmd()\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCsvExport_ResultsSortedByPort()
    {
        var results = new[]
        {
            new BannerResult { Port = 443, Service = "HTTPS", Banner = string.Empty, ResponseTime = "5 ms" },
            new BannerResult { Port = 22, Service = "SSH", Banner = string.Empty, ResponseTime = "3 ms" },
        };

        var csv = BannerGrabEngine.BuildCsvExport(results);
        var lines = csv.Trim().Split('\n');
        Assert.Contains("22", lines[1]);
        Assert.Contains("443", lines[2]);
    }

    [Fact]
    public void BuildCsvExport_WithLocalize_UsesLocalizedHeaders()
    {
        string Localize(string key) => key switch
        {
            "ToolBannerColPort" => "Port",
            "ToolBannerColService" => "Service",
            "ToolBannerColBanner" => "Bannière",
            "ToolBannerColTime" => "Temps",
            _ => key,
        };

        var csv = BannerGrabEngine.BuildCsvExport([], Localize);
        Assert.StartsWith("Port,Service,Bannière,Temps", csv.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildClipboardText_WithResults_IncludesHeaderAndData()
    {
        var results = new[]
        {
            new BannerResult { Port = 22, Service = "SSH", Banner = "OpenSSH", ResponseTime = "5 ms", HasBanner = true },
        };

        var text = BannerGrabEngine.BuildClipboardText(results);
        Assert.Contains("22", text);
        Assert.Contains("SSH", text);
        Assert.Contains("OpenSSH", text);
    }

    [Fact]
    public void BuildClipboardText_EmptyResults_ReturnsHeaderOnly()
    {
        var text = BannerGrabEngine.BuildClipboardText([]);
        var lines = text.Trim().Split('\n');
        Assert.Equal(2, lines.Length);
    }
}
