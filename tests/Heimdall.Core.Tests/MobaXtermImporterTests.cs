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

public class MobaXtermImporterTests
{
    private const string SampleIni = """
        [Bookmarks]
        SubRep=Production
        ImgNum=42
        WebServer= #109#0%web01.example.com%22%admin%-1%-1%%%22%%0%0%0%%%-1%0%0%0%%1080%%0%0%1
        DbServer= #109#0%db01.example.com%2222%root%-1%-1%%%22%%0%0%0%%%-1%0%0%0%%1080%%0%0%1
        WinBox= #91#0%win01.example.com%3389%administrator%
        [Bookmarks_1]
        SubRep=Staging
        ImgNum=41
        StagingSftp= #140#0%staging.example.com%22%deploy%
        StagingFtp= #130#0%ftp.example.com%21%ftpuser%1%0
        """;

    [Fact]
    public void Parse_SshSession_ExtractsHostPortUser()
    {
        var result = MobaXtermImporter.Parse(SampleIni);

        var webServer = result.Servers.First(s => s.DisplayName == "WebServer");
        Assert.Equal("web01.example.com", webServer.RemoteServer);
        Assert.Equal(22, webServer.SshPort);
        Assert.Equal("admin", webServer.SshUsername);
        Assert.Equal("SSH", webServer.ConnectionType);
        Assert.Equal("Production", webServer.Group);
    }

    [Fact]
    public void Parse_SshSession_CustomPort()
    {
        var result = MobaXtermImporter.Parse(SampleIni);

        var dbServer = result.Servers.First(s => s.DisplayName == "DbServer");
        Assert.Equal("db01.example.com", dbServer.RemoteServer);
        Assert.Equal(2222, dbServer.SshPort);
        Assert.Equal("root", dbServer.SshUsername);
    }

    [Fact]
    public void Parse_RdpSession_ExtractsFields()
    {
        var result = MobaXtermImporter.Parse(SampleIni);

        var winBox = result.Servers.First(s => s.DisplayName == "WinBox");
        Assert.Equal("win01.example.com", winBox.RemoteServer);
        Assert.Equal(3389, winBox.RemotePort);
        Assert.Equal("administrator", winBox.RdpUsername);
        Assert.Equal("RDP", winBox.ConnectionType);
        Assert.Equal("Production", winBox.Group);
    }

    [Fact]
    public void Parse_SftpSession_ExtractsFields()
    {
        var result = MobaXtermImporter.Parse(SampleIni);

        var sftp = result.Servers.First(s => s.DisplayName == "StagingSftp");
        Assert.Equal("staging.example.com", sftp.RemoteServer);
        Assert.Equal(22, sftp.SshPort);
        Assert.Equal("deploy", sftp.SshUsername);
        Assert.Equal("SFTP", sftp.ConnectionType);
        Assert.Equal("Staging", sftp.Group);
    }

    [Fact]
    public void Parse_FtpSession_ExtractsFields()
    {
        var result = MobaXtermImporter.Parse(SampleIni);

        var ftp = result.Servers.First(s => s.DisplayName == "StagingFtp");
        Assert.Equal("ftp.example.com", ftp.RemoteServer);
        Assert.Equal(21, ftp.FtpPort);
        Assert.Equal("ftpuser", ftp.FtpUsername);
        Assert.Equal("FTP", ftp.ConnectionType);
    }

    [Fact]
    public void Parse_MultipleSections_ReturnsAllServers()
    {
        var result = MobaXtermImporter.Parse(SampleIni);

        Assert.Equal(5, result.Servers.Count);
        Assert.Equal(3, result.Servers.Count(s => s.Group == "Production"));
        Assert.Equal(2, result.Servers.Count(s => s.Group == "Staging"));
    }

    [Fact]
    public void Parse_AssignsUniqueIds()
    {
        var result = MobaXtermImporter.Parse(SampleIni);

        var ids = result.Servers.Select(s => s.Id).ToHashSet();
        Assert.Equal(result.Servers.Count, ids.Count);
        Assert.All(result.Servers, s => Assert.False(string.IsNullOrEmpty(s.Id)));
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmptyResult()
    {
        var result = MobaXtermImporter.Parse(string.Empty);

        Assert.Empty(result.Servers);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_NullContent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MobaXtermImporter.Parse(null!));
    }

    [Fact]
    public void Parse_NonBookmarkSections_AreIgnored()
    {
        var ini = """
            [Misc]
            SomeKey=SomeValue
            [Bookmarks]
            SubRep=
            ImgNum=42
            Server1= #109#0%host.example.com%22%user%
            """;

        var result = MobaXtermImporter.Parse(ini);

        Assert.Single(result.Servers);
    }

    [Fact]
    public void Parse_UnsupportedProtocol_Skipped()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            BrowserSession= #999#0%host.example.com%8080%user%
            """;

        var result = MobaXtermImporter.Parse(ini);

        Assert.Empty(result.Servers);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_SessionWithoutHost_Skipped()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            NoHost= #109#0%%22%user%
            """;

        var result = MobaXtermImporter.Parse(ini);

        Assert.Empty(result.Servers);
    }

    [Fact]
    public void Parse_VncSession_ExtractsFields()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            VncServer= #128#0%vnc.example.com%5901%
            """;

        var result = MobaXtermImporter.Parse(ini);

        var vnc = result.Servers.Single();
        Assert.Equal("vnc.example.com", vnc.RemoteServer);
        Assert.Equal(5901, vnc.VncPort);
        Assert.Equal("VNC", vnc.ConnectionType);
    }

    [Fact]
    public void Parse_TelnetSession_ExtractsFields()
    {
        var ini = """
            [Bookmarks]
            SubRep=Switches
            ImgNum=42
            Switch1= #98#0%switch01.local%23%admin%
            """;

        var result = MobaXtermImporter.Parse(ini);

        var telnet = result.Servers.Single();
        Assert.Equal("switch01.local", telnet.RemoteServer);
        Assert.Equal(23, telnet.TelnetPort);
        Assert.Equal("admin", telnet.TelnetUsername);
        Assert.Equal("Telnet", telnet.ConnectionType);
        Assert.Equal("Switches", telnet.Group);
    }

    [Fact]
    public void Parse_SshWithKeyPath_MapsKeyPath()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            KeyServer= #109#0%host.example.com%22%user%%C:\Users\me\.ssh\id_rsa
            """;

        var result = MobaXtermImporter.Parse(ini);

        var server = result.Servers.Single();
        Assert.Equal(@"C:\Users\me\.ssh\id_rsa", server.SshKeyPath);
    }

    [Fact]
    public void Parse_SshDefaultPort_WhenPortEmpty()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            Server1= #109#0%host.example.com%%user%
            """;

        var result = MobaXtermImporter.Parse(ini);

        var server = result.Servers.Single();
        Assert.Equal(22, server.SshPort);
    }

    [Fact]
    public void Parse_EmptyFolder_SetsGroupToNull()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            Server1= #109#0%host.example.com%22%user%
            """;

        var result = MobaXtermImporter.Parse(ini);

        Assert.Null(result.Servers.Single().Group);
    }

    [Fact]
    public void Parse_SetsDirectConnectionTrue()
    {
        var result = MobaXtermImporter.Parse(SampleIni);

        Assert.All(result.Servers, s => Assert.True(s.UseDirectConnection));
    }

    [Fact]
    public void ExtractProtocolCode_ValidInput_ReturnsCode()
    {
        Assert.Equal(109, MobaXtermImporter.ExtractProtocolCode("#109#0%host%22%user%"));
        Assert.Equal(91, MobaXtermImporter.ExtractProtocolCode("#91#0%host%3389%user%"));
        Assert.Equal(130, MobaXtermImporter.ExtractProtocolCode(" #130#0%host%22%user%"));
    }

    [Fact]
    public void ExtractProtocolCode_InvalidInput_ReturnsNegative()
    {
        Assert.Equal(-1, MobaXtermImporter.ExtractProtocolCode("no hash here"));
        Assert.Equal(-1, MobaXtermImporter.ExtractProtocolCode(string.Empty));
    }

    [Fact]
    public void ExtractFieldsPart_ValidInput_ReturnsFields()
    {
        var fields = MobaXtermImporter.ExtractFieldsPart("#109#0%host%22%user%extra");
        Assert.Equal("host%22%user%extra", fields);
    }

    [Fact]
    public void Parse_FtpPassiveAndSsl_MappedCorrectly()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            FtpSsl= #130#0%ftp.example.com%21%ftpuser%1%1
            """;

        var result = MobaXtermImporter.Parse(ini);

        var ftp = result.Servers.Single();
        Assert.True(ftp.FtpPassiveMode);
        Assert.True(ftp.FtpUseSsl);
    }

    [Fact]
    public void Parse_SshEmbeddedMode_SetByDefault()
    {
        var result = MobaXtermImporter.Parse(SampleIni);

        var sshServers = result.Servers.Where(s => s.ConnectionType == "SSH");
        Assert.All(sshServers, s => Assert.Equal("Embedded", s.SshMode));
    }

    [Fact]
    public void Parse_RdpEmbeddedMode_SetByDefault()
    {
        var result = MobaXtermImporter.Parse(SampleIni);

        var rdpServers = result.Servers.Where(s => s.ConnectionType == "RDP");
        Assert.All(rdpServers, s => Assert.Equal("Embedded", s.RdpMode));
    }

    // --- Security: path traversal ---

    [Fact]
    public void SanitizeGroupName_PathTraversal_Stripped()
    {
        Assert.Equal("Startup", MobaXtermImporter.SanitizeGroupName(@"..\..\..\Startup"));
        Assert.Equal("Startup", MobaXtermImporter.SanitizeGroupName("../../../Startup"));
    }

    [Fact]
    public void SanitizeGroupName_BackslashHierarchy_ConvertedToSlash()
    {
        Assert.Equal("ADSEC/A_ADSEC_GW", MobaXtermImporter.SanitizeGroupName(@"ADSEC\A_ADSEC_GW"));
        Assert.Equal("Parent/Child/Leaf", MobaXtermImporter.SanitizeGroupName(@"Parent\Child\Leaf"));
    }

    [Fact]
    public void SanitizeGroupName_ForwardSlashHierarchy_Preserved()
    {
        Assert.Equal("PROD/Linux/Web", MobaXtermImporter.SanitizeGroupName("PROD/Linux/Web"));
    }

    [Fact]
    public void SanitizeGroupName_NormalName_Unchanged()
    {
        Assert.Equal("Production", MobaXtermImporter.SanitizeGroupName("Production"));
        Assert.Equal("My Servers", MobaXtermImporter.SanitizeGroupName("My Servers"));
    }

    [Fact]
    public void SanitizeGroupName_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MobaXtermImporter.SanitizeGroupName(""));
        Assert.Equal(string.Empty, MobaXtermImporter.SanitizeGroupName("   "));
    }

    [Fact]
    public void SanitizeFilePath_ShellMetachars_ReturnsNull()
    {
        Assert.Null(MobaXtermImporter.SanitizeFilePath("key.pub; rm -rf /"));
        Assert.Null(MobaXtermImporter.SanitizeFilePath("key|evil"));
        Assert.Null(MobaXtermImporter.SanitizeFilePath("key&cmd"));
        Assert.Null(MobaXtermImporter.SanitizeFilePath("$(whoami)"));
        Assert.Null(MobaXtermImporter.SanitizeFilePath("`id`"));
    }

    [Fact]
    public void SanitizeFilePath_PathTraversal_ReturnsNull()
    {
        Assert.Null(MobaXtermImporter.SanitizeFilePath(@"C:\..\..\etc\passwd"));
        Assert.Null(MobaXtermImporter.SanitizeFilePath("../../etc/passwd"));
    }

    [Fact]
    public void SanitizeFilePath_ValidPath_Returned()
    {
        Assert.Equal(@"C:\Users\me\.ssh\id_rsa", MobaXtermImporter.SanitizeFilePath(@"C:\Users\me\.ssh\id_rsa"));
        Assert.Equal("/home/user/.ssh/id_ed25519", MobaXtermImporter.SanitizeFilePath("/home/user/.ssh/id_ed25519"));
    }

    [Fact]
    public void SanitizeFilePath_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(MobaXtermImporter.SanitizeFilePath(null));
        Assert.Null(MobaXtermImporter.SanitizeFilePath(""));
        Assert.Null(MobaXtermImporter.SanitizeFilePath("   "));
    }

    // --- Edge cases: INI comments ---

    [Fact]
    public void Parse_IniComments_AreIgnored()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ; This is a comment
            ImgNum=42
            Server1= #109#0%host.example.com%22%user%
            """;

        var result = MobaXtermImporter.Parse(ini);

        Assert.Single(result.Servers);
    }

    // --- Edge cases: special characters in session names ---

    [Fact]
    public void Parse_SpecialCharsInSessionName_Preserved()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            Server [Prod] (main)= #109#0%host.example.com%22%user%
            """;

        var result = MobaXtermImporter.Parse(ini);

        Assert.Single(result.Servers);
        Assert.Equal("Server [Prod] (main)", result.Servers[0].DisplayName);
    }

    // --- Edge cases: truncated field data ---

    [Fact]
    public void Parse_TruncatedFields_HostOnly()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            Minimal= #109#0%host.example.com
            """;

        var result = MobaXtermImporter.Parse(ini);

        var server = result.Servers.Single();
        Assert.Equal("host.example.com", server.RemoteServer);
        Assert.Equal(22, server.SshPort);
        Assert.Null(server.SshUsername);
    }

    // --- Edge cases: duplicate section names ---

    [Fact]
    public void Parse_DuplicateSectionNames_LastWins()
    {
        var ini = """
            [Bookmarks]
            SubRep=First
            ImgNum=42
            Server1= #109#0%first.example.com%22%user%
            [Bookmarks]
            SubRep=Second
            ImgNum=42
            Server2= #109#0%second.example.com%22%user%
            """;

        var result = MobaXtermImporter.Parse(ini);

        // Last section with same name overwrites
        Assert.Single(result.Servers);
        Assert.Equal("second.example.com", result.Servers[0].RemoteServer);
    }

    // --- Security: key path with traversal is rejected ---

    [Fact]
    public void Parse_SshKeyPathWithTraversal_Rejected()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            EvilKey= #109#0%host.example.com%22%user%%..\..\etc\passwd
            """;

        var result = MobaXtermImporter.Parse(ini);

        var server = result.Servers.Single();
        Assert.Null(server.SshKeyPath);
    }

    // --- Security: group path traversal is sanitized ---

    [Fact]
    public void Parse_GroupPathTraversal_Sanitized()
    {
        var ini = """
            [Bookmarks]
            SubRep=..\..\Startup
            ImgNum=42
            Server1= #109#0%host.example.com%22%user%
            """;

        var result = MobaXtermImporter.Parse(ini);

        var server = result.Servers.Single();
        Assert.DoesNotContain("..", server.Group);
    }

    // --- Real MobaXterm .mobaconf format ---

    [Fact]
    public void Parse_RealMobaconfFormat_SshWithGateway()
    {
        var ini = """
            [Bookmarks_2]
            SubRep=ADSEC\A_ADSEC_GW
            ImgNum=41
            sncagat01d  (GW)=#109#0%sncagat01d%22%root%%-1%-1%%ypsgat01s.priv.atos.fr%22%a150058%0%0%0%%%-1%0%0%0%%1080%%0%0%1%%0%%%%0%-1%-1%0%%#MobaFont%10%0%0%-1%15%248,248,242%40,42,54%153,153,153%0%-1%0%%xterm%-1%0%_Std_Colors_0_%80%24%0%1%-1%<none>%%0%0%-1%-1%#0# #-1
            """;

        var result = MobaXtermImporter.Parse(ini);

        var server = result.Servers.Single();
        Assert.Equal("sncagat01d", server.RemoteServer);
        Assert.Equal(22, server.SshPort);
        Assert.Equal("root", server.SshUsername);
        Assert.Equal("SSH", server.ConnectionType);
        Assert.Equal("ADSEC/A_ADSEC_GW", server.Group);
    }

    [Fact]
    public void Parse_RealMobaconfFormat_RdpWithDomainUser()
    {
        var ini = """
            [Bookmarks_3]
            SubRep=ADSEC\B_ADSEC_SERVICES
            ImgNum=41
            opadsb03d (WSUS)=#91#4%opadsb03d%3389%Administrator@admin.adsec.net%0%-1%-1%-1%-1%0%0%-1%%ypsgat01s.priv.atos.fr%22%a150058%0%0%%-1%%-1%-1%0%-1%0%-1%0%0%0%0%#MobaFont%10%0%0
            """;

        var result = MobaXtermImporter.Parse(ini);

        var server = result.Servers.Single();
        Assert.Equal("opadsb03d", server.RemoteServer);
        Assert.Equal(3389, server.RemotePort);
        Assert.Equal("Administrator@admin.adsec.net", server.RdpUsername);
        Assert.Equal("RDP", server.ConnectionType);
        Assert.Equal("ADSEC/B_ADSEC_SERVICES", server.Group);
    }

    [Fact]
    public void Parse_WslSession_Skipped()
    {
        var ini = """
            [Bookmarks]
            SubRep=
            ImgNum=42
            WSL-Dev-box=#151#14%Dev-box-daemon%
            """;

        var result = MobaXtermImporter.Parse(ini);

        Assert.Empty(result.Servers);
    }
}
