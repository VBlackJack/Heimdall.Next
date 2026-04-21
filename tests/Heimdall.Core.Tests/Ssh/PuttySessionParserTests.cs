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

using Heimdall.Core.Ssh;

namespace Heimdall.Core.Tests.Ssh;

public sealed class PuttySessionParserTests
{
    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyResult()
    {
        var result = PuttySessionParser.Parse([]);

        Assert.Empty(result.Candidates);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("Mon%20Serveur", "Mon Serveur")]
    [InlineData("50%25", "50%")]
    [InlineData("keep+plus", "keep+plus")]
    [InlineData("bad%zzname", "bad%zzname")]
    public void DecodeSessionName_UsesStrictPercentDecoding(string encoded, string expected)
    {
        Assert.Equal(expected, PuttySessionParser.DecodeSessionName(encoded));
    }

    [Fact]
    public void Parse_DefaultSettings_EmitsSkipDiagnostic()
    {
        var result = PuttySessionParser.Parse(
        [
            new RawPuttySession("Default%20Settings", new Dictionary<string, object?>())
        ]);

        Assert.Empty(result.Candidates);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PuttyDiagnosticCode.DefaultSettingsKeySkipped, diagnostic.Code);
    }

    [Fact]
    public void Parse_NonSshProtocol_IgnoredWithDiagnostic()
    {
        var result = PuttySessionParser.Parse(
        [
            new RawPuttySession("Telnet%20Host", new Dictionary<string, object?>
            {
                ["Protocol"] = "telnet",
                ["HostName"] = "legacy.example.com"
            })
        ]);

        Assert.Empty(result.Candidates);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PuttyDiagnosticCode.NonSshProtocolIgnored, diagnostic.Code);
        Assert.Equal("telnet", diagnostic.Context);
    }

    [Fact]
    public void Parse_SshSession_MapsCoreFields()
    {
        var result = PuttySessionParser.Parse(
        [
            new RawPuttySession("Prod%20SSH", new Dictionary<string, object?>
            {
                ["Protocol"] = "ssh",
                ["HostName"] = "prod.example.com",
                ["PortNumber"] = 2222,
                ["UserName"] = "alice",
                ["PublicKeyFile"] = @"C:\keys\id_ed25519"
            })
        ]);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Prod SSH", candidate.DisplayName);
        Assert.Equal("prod.example.com", candidate.HostName);
        Assert.Equal(2222, candidate.Port);
        Assert.Equal("alice", candidate.UserName);
        Assert.Equal(@"C:\keys\id_ed25519", candidate.PublicKeyFile);
    }

    [Fact]
    public void Parse_MissingHostName_KeepsCandidateButEmitsDiagnostic()
    {
        var result = PuttySessionParser.Parse(
        [
            new RawPuttySession("Hostless", new Dictionary<string, object?>
            {
                ["Protocol"] = "ssh"
            })
        ]);

        var candidate = Assert.Single(result.Candidates);
        Assert.Null(candidate.HostName);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == PuttyDiagnosticCode.MissingHostName);
    }

    [Fact]
    public void Parse_InvalidPort_FallsBackTo22AndEmitsDiagnostic()
    {
        var result = PuttySessionParser.Parse(
        [
            new RawPuttySession("InvalidPort", new Dictionary<string, object?>
            {
                ["Protocol"] = "ssh",
                ["HostName"] = "prod.example.com",
                ["PortNumber"] = "99999"
            })
        ]);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(22, candidate.Port);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == PuttyDiagnosticCode.InvalidPortNumber);
    }

    [Fact]
    public void Parse_PpkKey_EmitsDiagnosticButPreservesPath()
    {
        var result = PuttySessionParser.Parse(
        [
            new RawPuttySession("Ppk", new Dictionary<string, object?>
            {
                ["Protocol"] = "ssh",
                ["HostName"] = "prod.example.com",
                ["PublicKeyFile"] = @"C:\keys\admin.ppk"
            })
        ]);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(@"C:\keys\admin.ppk", candidate.PublicKeyFile);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == PuttyDiagnosticCode.PpkKeyCapturedNotConverted);
    }

    [Fact]
    public void Parse_Proxy_EmitsDiagnostic()
    {
        var result = PuttySessionParser.Parse(
        [
            new RawPuttySession("Proxy", new Dictionary<string, object?>
            {
                ["Protocol"] = "ssh",
                ["HostName"] = "prod.example.com",
                ["ProxyMethod"] = 3,
                ["ProxyHost"] = "proxy.example.com",
                ["ProxyPort"] = "8080"
            })
        ]);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == PuttyDiagnosticCode.ProxyCapturedButNotMapped);
        Assert.Contains("proxy.example.com", diagnostic.Context);
    }

    [Fact]
    public void Parse_PortForwardings_CountsEntries()
    {
        var result = PuttySessionParser.Parse(
        [
            new RawPuttySession("Fwds", new Dictionary<string, object?>
            {
                ["Protocol"] = "ssh",
                ["HostName"] = "prod.example.com",
                ["PortForwardings"] = "L5901=localhost:5901\0R8080=localhost:80"
            })
        ]);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == PuttyDiagnosticCode.PortForwardingsCapturedButNotMapped);
        Assert.Equal("2", diagnostic.Context);
    }

    [Fact]
    public void Parse_RemoteCommand_TruncatesDiagnosticContext()
    {
        var command = new string('x', 100);
        var result = PuttySessionParser.Parse(
        [
            new RawPuttySession("Cmd", new Dictionary<string, object?>
            {
                ["Protocol"] = "ssh",
                ["HostName"] = "prod.example.com",
                ["RemoteCommand"] = command
            })
        ]);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == PuttyDiagnosticCode.RemoteCommandCapturedButNotMapped);
        Assert.Equal(83, diagnostic.Context!.Length);
        Assert.EndsWith("...", diagnostic.Context, StringComparison.Ordinal);
    }
}
