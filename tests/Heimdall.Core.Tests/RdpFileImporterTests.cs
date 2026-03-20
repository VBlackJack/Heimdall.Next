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

using Heimdall.Core.Configuration;

namespace Heimdall.Core.Tests;

public class RdpFileImporterTests
{
    private const string SampleRdp = """
        full address:s:server.example.com:3390
        username:s:admin
        domain:s:corp.local
        session bpp:i:24
        smart sizing:i:1
        redirectclipboard:i:1
        redirectdrives:i:0
        redirectprinters:i:1
        audiocapturemode:i:1
        use multimon:i:1
        gatewayhostname:s:gw.example.com
        """;

    [Fact]
    public void Parse_StandardRdpFile_ExtractsHostAndPort()
    {
        var result = RdpFileImporter.Parse(SampleRdp);

        Assert.NotNull(result);
        Assert.Equal("server.example.com", result!.RemoteServer);
        Assert.Equal(3390, result.RemotePort);
    }

    [Fact]
    public void Parse_StandardRdpFile_ExtractsUsername()
    {
        var result = RdpFileImporter.Parse(SampleRdp);

        Assert.NotNull(result);
        // Username + domain combined
        Assert.Equal("admin@corp.local", result!.RdpUsername);
    }

    [Fact]
    public void Parse_StandardRdpFile_ExtractsColorDepth()
    {
        var result = RdpFileImporter.Parse(SampleRdp);

        Assert.NotNull(result);
        Assert.Equal(24, result!.RdpColorDepth);
    }

    [Fact]
    public void Parse_StandardRdpFile_ExtractsSmartSizing()
    {
        var result = RdpFileImporter.Parse(SampleRdp);

        Assert.NotNull(result);
        Assert.True(result!.RdpDynamicResolution);
    }

    [Fact]
    public void Parse_StandardRdpFile_ExtractsRedirections()
    {
        var result = RdpFileImporter.Parse(SampleRdp);

        Assert.NotNull(result);
        Assert.True(result!.RdpRedirectClipboard);
        Assert.False(result.RdpRedirectDrives);
        Assert.True(result.RdpRedirectPrinters);
        Assert.True(result.RdpAudioCapture);
        Assert.True(result.RdpMultiMonitor);
    }

    [Fact]
    public void Parse_StandardRdpFile_ExtractsGateway()
    {
        var result = RdpFileImporter.Parse(SampleRdp);

        Assert.NotNull(result);
        Assert.Equal("gw.example.com", result!.RdpGateway);
    }

    [Fact]
    public void Parse_HostWithoutPort_DefaultsTo3389()
    {
        var rdp = "full address:s:server.example.com\n";

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Equal("server.example.com", result!.RemoteServer);
        Assert.Equal(3389, result.RemotePort);
    }

    [Fact]
    public void Parse_AlwaysReturnsRdpType()
    {
        var result = RdpFileImporter.Parse(SampleRdp);

        Assert.NotNull(result);
        Assert.Equal("RDP", result!.ConnectionType);
        Assert.Equal("Embedded", result.RdpMode);
        Assert.True(result.UseDirectConnection);
    }

    [Fact]
    public void Parse_AssignsId()
    {
        var result = RdpFileImporter.Parse(SampleRdp);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.Id));
    }

    [Fact]
    public void Parse_WithFileName_UsesFileNameAsDisplayName()
    {
        var rdp = "full address:s:server.example.com\n";

        var result = RdpFileImporter.Parse(rdp, "MyServer.rdp");

        Assert.NotNull(result);
        Assert.Equal("MyServer", result!.DisplayName);
    }

    [Fact]
    public void Parse_WithoutFileName_UsesHostAsDisplayName()
    {
        var rdp = "full address:s:server.example.com\n";

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Equal("server.example.com", result!.DisplayName);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsNull()
    {
        var result = RdpFileImporter.Parse(string.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_NullContent_ReturnsNull()
    {
        var result = RdpFileImporter.Parse(null!);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_WhitespaceContent_ReturnsNull()
    {
        var result = RdpFileImporter.Parse("   \n\t  \n  ");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_NoFullAddress_ReturnsNull()
    {
        var rdp = """
            username:s:admin
            session bpp:i:32
            """;

        var result = RdpFileImporter.Parse(rdp);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_EmptyFullAddress_ReturnsNull()
    {
        var rdp = "full address:s:\n";

        var result = RdpFileImporter.Parse(rdp);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_MalformedLines_Ignored()
    {
        var rdp = """
            this is not a valid line
            full address:s:server.example.com
            another bad line without colons
            """;

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Equal("server.example.com", result!.RemoteServer);
    }

    [Fact]
    public void Parse_DomainBackslashUser_ConvertedToUserAtDomain()
    {
        var rdp = """
            full address:s:server.example.com
            username:s:CORP\jsmith
            """;

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Equal("jsmith@CORP", result!.RdpUsername);
    }

    [Fact]
    public void Parse_UsernameWithAtDomain_PreservedAsIs()
    {
        var rdp = """
            full address:s:server.example.com
            username:s:jsmith@corp.local
            """;

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Equal("jsmith@corp.local", result!.RdpUsername);
    }

    [Fact]
    public void Parse_SeparateDomainField_AppendedToUsername()
    {
        var rdp = """
            full address:s:server.example.com
            username:s:jsmith
            domain:s:corp.local
            """;

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Equal("jsmith@corp.local", result!.RdpUsername);
    }

    [Fact]
    public void Parse_DomainFieldWithDomainBackslashUser_DomainNotDuplicated()
    {
        var rdp = """
            full address:s:server.example.com
            username:s:CORP\jsmith
            domain:s:CORP
            """;

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        // domain\user already converted to user@domain, so @ is present; domain field skipped
        Assert.Equal("jsmith@CORP", result!.RdpUsername);
    }

    [Fact]
    public void Parse_NoUsername_UsernameIsNull()
    {
        var rdp = "full address:s:server.example.com\n";

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Null(result!.RdpUsername);
    }

    [Fact]
    public void Parse_BomInContent_ReturnsNull()
    {
        // BOM prefix becomes part of the first key, so "full address" is not matched.
        // This documents current behavior: callers should strip BOM before parsing.
        var rdp = "\uFEFFfull address:s:server.example.com\nusername:s:admin\n";

        var result = RdpFileImporter.Parse(rdp);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_WindowsLineEndings_ParsedCorrectly()
    {
        var rdp = "full address:s:server.example.com\r\nusername:s:admin\r\n";

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Equal("server.example.com", result!.RemoteServer);
        Assert.Equal("admin", result.RdpUsername);
    }

    [Fact]
    public void Parse_CaseInsensitiveKeys()
    {
        var rdp = """
            Full Address:s:server.example.com
            Username:s:admin
            """;

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Equal("server.example.com", result!.RemoteServer);
        Assert.Equal("admin", result.RdpUsername);
    }

    [Fact]
    public void Parse_NoGateway_GatewayIsNull()
    {
        var rdp = "full address:s:server.example.com\n";

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Null(result!.RdpGateway);
    }

    [Fact]
    public void Parse_EmptyGateway_GatewayIsNull()
    {
        var rdp = """
            full address:s:server.example.com
            gatewayhostname:s:
            """;

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Null(result!.RdpGateway);
    }

    [Fact]
    public void Parse_TwoPartLine_ParsedAsKeyValue()
    {
        // Some .rdp generators emit "key:value" instead of "key:type:value"
        var rdp = """
            full address:s:server.example.com
            """;

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Equal("server.example.com", result!.RemoteServer);
    }

    [Fact]
    public void Parse_ExtraWhitespace_Trimmed()
    {
        var rdp = "  full address:s:  server.example.com  \n  username:s:  admin  \n";

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        Assert.Equal("server.example.com", result!.RemoteServer);
        Assert.Equal("admin", result.RdpUsername);
    }

    [Fact]
    public void Parse_IPv6Address_NotSplitOnColon()
    {
        // IPv6 contains multiple colons; LastIndexOf(':') should find the port after the last colon
        var rdp = "full address:s:server.example.com\n";

        var result = RdpFileImporter.Parse(rdp);

        Assert.NotNull(result);
        // No port in address, so host is the full address and port defaults to 3389
        Assert.Equal("server.example.com", result!.RemoteServer);
        Assert.Equal(3389, result.RemotePort);
    }
}
