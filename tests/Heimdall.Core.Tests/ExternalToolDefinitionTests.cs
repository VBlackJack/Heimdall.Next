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

public class ExternalToolDefinitionTests
{
    // ── ResolveArguments ────────────────────────────────────────────────

    [Fact]
    public void ResolveArguments_ReplacesAllPlaceholders()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "--host {Host} --port {Port} --user {User}"
        };

        var result = tool.ResolveArguments("server.local", 22, "admin");

        Assert.Equal("--host server.local --port 22 --user admin", result);
    }

    [Fact]
    public void ResolveArguments_IsCaseInsensitive()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "{host} {HOST} {Host}"
        };

        var result = tool.ResolveArguments("myhost", 80, "user");

        Assert.Equal("myhost myhost myhost", result);
    }

    [Fact]
    public void ResolveArguments_PortConvertedToString()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "-p {Port}"
        };

        var result = tool.ResolveArguments("host", 3389, "user");

        Assert.Equal("-p 3389", result);
    }

    [Fact]
    public void ResolveArguments_NoPlaceholders_ReturnsUnchanged()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "--verbose --timeout 30"
        };

        var result = tool.ResolveArguments("host", 22, "user");

        Assert.Equal("--verbose --timeout 30", result);
    }

    [Fact]
    public void ResolveArguments_EmptyHost_ReplacesWithEmpty()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "--host {Host}"
        };

        var result = tool.ResolveArguments("", 22, "admin");

        Assert.Equal("--host ", result);
    }

    [Fact]
    public void ResolveArguments_NullHost_ReplacesWithEmpty()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "--host {Host}"
        };

        var result = tool.ResolveArguments(null!, 22, "admin");

        Assert.Equal("--host ", result);
    }

    [Fact]
    public void ResolveArguments_EmptyUser_ReplacesWithEmpty()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "--user {User}"
        };

        var result = tool.ResolveArguments("host", 22, "");

        Assert.Equal("--user ", result);
    }

    // ── SanitizeValue (via ResolveArguments) ────────────────────────────

    [Theory]
    [InlineData("host;rm -rf /", "hostrm -rf /")]
    [InlineData("host|cat /etc/passwd", "hostcat /etc/passwd")]
    [InlineData("host&whoami", "hostwhoami")]
    [InlineData("host`id`", "hostid")]
    [InlineData("host$PATH", "hostPATH")]
    [InlineData("host<file", "hostfile")]
    [InlineData("host>file", "hostfile")]
    public void ResolveArguments_SanitizesShellMetacharacters(string maliciousHost, string expectedHost)
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "{Host}"
        };

        var result = tool.ResolveArguments(maliciousHost, 22, "user");

        Assert.Equal(expectedHost, result);
    }

    [Fact]
    public void ResolveArguments_SanitizesUserValue()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "{User}"
        };

        var result = tool.ResolveArguments("host", 22, "admin;whoami");

        Assert.Equal("adminwhoami", result);
    }

    [Fact]
    public void ResolveArguments_PortNotSanitized_AcceptsAnyInt()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "{Port}"
        };

        var result = tool.ResolveArguments("host", 0, "user");

        Assert.Equal("0", result);
    }

    // ── Extended placeholder tests ──────────────────────────────────────

    [Fact]
    public void ResolveArguments_ReplacesServerNamePlaceholder()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "--name {ServerName}"
        };

        var result = tool.ResolveArguments("host", 22, "user", serverName: "Production DB");

        Assert.Equal("--name Production DB", result);
    }

    [Fact]
    public void ResolveArguments_ReplacesProtocolPlaceholder()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "--protocol {Protocol}"
        };

        var result = tool.ResolveArguments("host", 22, "user", protocol: "SSH");

        Assert.Equal("--protocol SSH", result);
    }

    [Fact]
    public void ResolveArguments_ReplacesKeyFilePlaceholder()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "--key {KeyFile}"
        };

        var result = tool.ResolveArguments("host", 22, "user", keyFile: "C:\\keys\\id_rsa");

        Assert.Equal("--key C:\\keys\\id_rsa", result); // Backslashes are preserved
    }

    [Fact]
    public void ResolveArguments_ReplacesProjectPlaceholder()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "--project {Project}"
        };

        var result = tool.ResolveArguments("host", 22, "user", project: "MyProject");

        Assert.Equal("--project MyProject", result);
    }

    [Fact]
    public void ResolveArguments_ReplacesGatewayPlaceholder()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "--gateway {Gateway}"
        };

        var result = tool.ResolveArguments("host", 22, "user", gateway: "bastion.example.com");

        Assert.Equal("--gateway bastion.example.com", result);
    }

    [Fact]
    public void ResolveArguments_AllEightPlaceholders()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "{Host} {Port} {User} {ServerName} {Protocol} {KeyFile} {Project} {Gateway}"
        };

        var result = tool.ResolveArguments(
            "myhost", 3389, "admin",
            serverName: "srv1",
            protocol: "RDP",
            keyFile: "key.pem",
            project: "proj1",
            gateway: "gw1");

        Assert.Equal("myhost 3389 admin srv1 RDP key.pem proj1 gw1", result);
    }

    [Fact]
    public void ResolveArguments_NullOptionalPlaceholders_ReplacedWithEmpty()
    {
        var tool = new ExternalToolDefinition
        {
            Arguments = "{ServerName}|{Protocol}|{KeyFile}|{Project}|{Gateway}"
        };

        var result = tool.ResolveArguments("host", 22, "user");

        Assert.Equal("||||", result);
    }

    // ── Sanitization tests for new placeholders ─────────────────────────

    [Theory]
    [InlineData("srv;whoami", "srvwhoami")]
    [InlineData("srv|cat", "srvcat")]
    [InlineData("srv&id", "srvid")]
    [InlineData("srv`cmd`", "srvcmd")]
    [InlineData("srv$ENV", "srvENV")]
    [InlineData("srv<file", "srvfile")]
    [InlineData("srv>file", "srvfile")]
    public void ResolveArguments_SanitizesServerNameMetacharacters(string malicious, string expected)
    {
        var tool = new ExternalToolDefinition { Arguments = "{ServerName}" };
        var result = tool.ResolveArguments("host", 22, "user", serverName: malicious);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SSH;rm -rf", "SSHrm -rf")]
    [InlineData("RDP|bad", "RDPbad")]
    public void ResolveArguments_SanitizesProtocolMetacharacters(string malicious, string expected)
    {
        var tool = new ExternalToolDefinition { Arguments = "{Protocol}" };
        var result = tool.ResolveArguments("host", 22, "user", protocol: malicious);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("key;whoami", "keywhoami")]
    [InlineData("key|cat", "keycat")]
    public void ResolveArguments_SanitizesKeyFileMetacharacters(string malicious, string expected)
    {
        var tool = new ExternalToolDefinition { Arguments = "{KeyFile}" };
        var result = tool.ResolveArguments("host", 22, "user", keyFile: malicious);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("proj;whoami", "projwhoami")]
    [InlineData("proj`id`", "projid")]
    public void ResolveArguments_SanitizesProjectMetacharacters(string malicious, string expected)
    {
        var tool = new ExternalToolDefinition { Arguments = "{Project}" };
        var result = tool.ResolveArguments("host", 22, "user", project: malicious);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("gw;whoami", "gwwhoami")]
    [InlineData("gw&id", "gwid")]
    [InlineData("gw$PATH", "gwPATH")]
    public void ResolveArguments_SanitizesGatewayMetacharacters(string malicious, string expected)
    {
        var tool = new ExternalToolDefinition { Arguments = "{Gateway}" };
        var result = tool.ResolveArguments("host", 22, "user", gateway: malicious);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveArguments_EmptyServerName_ReplacesWithEmpty()
    {
        var tool = new ExternalToolDefinition { Arguments = "--name {ServerName}" };
        var result = tool.ResolveArguments("host", 22, "user", serverName: "");
        Assert.Equal("--name ", result);
    }

    [Fact]
    public void ResolveArguments_EmptyProtocol_ReplacesWithEmpty()
    {
        var tool = new ExternalToolDefinition { Arguments = "--proto {Protocol}" };
        var result = tool.ResolveArguments("host", 22, "user", protocol: "");
        Assert.Equal("--proto ", result);
    }

    [Fact]
    public void ResolveArguments_EmptyKeyFile_ReplacesWithEmpty()
    {
        var tool = new ExternalToolDefinition { Arguments = "--key {KeyFile}" };
        var result = tool.ResolveArguments("host", 22, "user", keyFile: "");
        Assert.Equal("--key ", result);
    }

    [Fact]
    public void ResolveArguments_EmptyProject_ReplacesWithEmpty()
    {
        var tool = new ExternalToolDefinition { Arguments = "--proj {Project}" };
        var result = tool.ResolveArguments("host", 22, "user", project: "");
        Assert.Equal("--proj ", result);
    }

    [Fact]
    public void ResolveArguments_EmptyGateway_ReplacesWithEmpty()
    {
        var tool = new ExternalToolDefinition { Arguments = "--gw {Gateway}" };
        var result = tool.ResolveArguments("host", 22, "user", gateway: "");
        Assert.Equal("--gw ", result);
    }

    // ── WorkingDirectory property ───────────────────────────────────────

    [Fact]
    public void WorkingDirectory_DefaultsToEmpty()
    {
        var tool = new ExternalToolDefinition();
        Assert.Equal(string.Empty, tool.WorkingDirectory);
    }

    [Fact]
    public void WorkingDirectory_CanBeSet()
    {
        var tool = new ExternalToolDefinition { WorkingDirectory = @"C:\tools" };
        Assert.Equal(@"C:\tools", tool.WorkingDirectory);
    }

    [Fact]
    public void WorkingDirectory_SerializesAndDeserializes()
    {
        var tool = new ExternalToolDefinition
        {
            Name = "Test",
            WorkingDirectory = @"C:\my tools\bin"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(tool);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ExternalToolDefinition>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(@"C:\my tools\bin", deserialized.WorkingDirectory);
    }

    // ── RunAsAdministrator property ─────────────────────────────────────

    [Fact]
    public void RunAsAdministrator_DefaultsToFalse()
    {
        var tool = new ExternalToolDefinition();
        Assert.False(tool.RunAsAdministrator);
    }

    [Fact]
    public void RunAsAdministrator_CanBeSetToTrue()
    {
        var tool = new ExternalToolDefinition { RunAsAdministrator = true };
        Assert.True(tool.RunAsAdministrator);
    }

    [Fact]
    public void RunAsAdministrator_SerializesAndDeserializes()
    {
        var tool = new ExternalToolDefinition
        {
            Name = "Elevated",
            RunAsAdministrator = true
        };

        var json = System.Text.Json.JsonSerializer.Serialize(tool);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ExternalToolDefinition>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.RunAsAdministrator);
    }

    // ── RunHidden property ──────────────────────────────────────────────

    [Fact]
    public void RunHidden_DefaultsToFalse()
    {
        var tool = new ExternalToolDefinition();
        Assert.False(tool.RunHidden);
    }

    [Fact]
    public void RunHidden_CanBeSetToTrue()
    {
        var tool = new ExternalToolDefinition { RunHidden = true };
        Assert.True(tool.RunHidden);
    }

    [Fact]
    public void RunHidden_SerializesAndDeserializes()
    {
        var tool = new ExternalToolDefinition
        {
            Name = "Script",
            RunHidden = true
        };

        var json = System.Text.Json.JsonSerializer.Serialize(tool);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ExternalToolDefinition>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.RunHidden);
    }

    // ── Individual placeholder tests ────────────────────────────────────

    [Fact]
    public void ResolveArguments_HostPlaceholder_Individual()
    {
        var tool = new ExternalToolDefinition { Arguments = "{Host}" };
        var result = tool.ResolveArguments("myserver.local", 22, "admin");
        Assert.Equal("myserver.local", result);
    }

    [Fact]
    public void ResolveArguments_PortPlaceholder_Individual()
    {
        var tool = new ExternalToolDefinition { Arguments = "{Port}" };
        var result = tool.ResolveArguments("host", 5900, "user");
        Assert.Equal("5900", result);
    }

    [Fact]
    public void ResolveArguments_UserPlaceholder_Individual()
    {
        var tool = new ExternalToolDefinition { Arguments = "{User}" };
        var result = tool.ResolveArguments("host", 22, "deploy-user");
        Assert.Equal("deploy-user", result);
    }

    [Fact]
    public void ResolveArguments_ServerNamePlaceholder_Individual()
    {
        var tool = new ExternalToolDefinition { Arguments = "{ServerName}" };
        var result = tool.ResolveArguments("host", 22, "user", serverName: "Web Server 01");
        Assert.Equal("Web Server 01", result);
    }

    [Fact]
    public void ResolveArguments_ProtocolPlaceholder_Individual()
    {
        var tool = new ExternalToolDefinition { Arguments = "{Protocol}" };
        var result = tool.ResolveArguments("host", 22, "user", protocol: "SSH");
        Assert.Equal("SSH", result);
    }

    [Fact]
    public void ResolveArguments_KeyFilePlaceholder_Individual()
    {
        var tool = new ExternalToolDefinition { Arguments = "{KeyFile}" };
        var result = tool.ResolveArguments("host", 22, "user", keyFile: @"C:\keys\id_ed25519");
        Assert.Equal(@"C:\keys\id_ed25519", result);
    }

    [Fact]
    public void ResolveArguments_ProjectPlaceholder_Individual()
    {
        var tool = new ExternalToolDefinition { Arguments = "{Project}" };
        var result = tool.ResolveArguments("host", 22, "user", project: "Production");
        Assert.Equal("Production", result);
    }

    [Fact]
    public void ResolveArguments_GatewayPlaceholder_Individual()
    {
        var tool = new ExternalToolDefinition { Arguments = "{Gateway}" };
        var result = tool.ResolveArguments("host", 22, "user", gateway: "bastion-01.corp.net");
        Assert.Equal("bastion-01.corp.net", result);
    }

    // ── Case insensitivity for all variants ─────────────────────────────

    [Theory]
    [InlineData("{HOST}", "resolved")]
    [InlineData("{host}", "resolved")]
    [InlineData("{Host}", "resolved")]
    [InlineData("{hOsT}", "resolved")]
    public void ResolveArguments_HostPlaceholder_CaseInsensitive(string placeholder, string expected)
    {
        var tool = new ExternalToolDefinition { Arguments = placeholder };
        var result = tool.ResolveArguments("resolved", 22, "user");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("{PORT}")]
    [InlineData("{port}")]
    [InlineData("{Port}")]
    public void ResolveArguments_PortPlaceholder_CaseInsensitive(string placeholder)
    {
        var tool = new ExternalToolDefinition { Arguments = placeholder };
        var result = tool.ResolveArguments("host", 443, "user");
        Assert.Equal("443", result);
    }

    [Theory]
    [InlineData("{SERVERNAME}")]
    [InlineData("{servername}")]
    [InlineData("{ServerName}")]
    public void ResolveArguments_ServerNamePlaceholder_CaseInsensitive(string placeholder)
    {
        var tool = new ExternalToolDefinition { Arguments = placeholder };
        var result = tool.ResolveArguments("host", 22, "user", serverName: "DB01");
        Assert.Equal("DB01", result);
    }

    // ── Shell metacharacter sanitization (comprehensive) ────────────────

    [Theory]
    [InlineData(";")]
    [InlineData("&")]
    [InlineData("|")]
    [InlineData("$")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("!")]
    [InlineData("\"")]
    [InlineData("'")]
    [InlineData("`")]
    [InlineData("%")]
    [InlineData("^")]
    public void ResolveArguments_StripsEachMetacharacter_FromHost(string metachar)
    {
        var tool = new ExternalToolDefinition { Arguments = "{Host}" };
        var result = tool.ResolveArguments($"host{metachar}injected", 22, "user");
        Assert.Equal("hostinjected", result);
    }

    [Fact]
    public void ResolveArguments_StripsCarriageReturn()
    {
        var tool = new ExternalToolDefinition { Arguments = "{Host}" };
        var result = tool.ResolveArguments("host\rinjected", 22, "user");
        Assert.Equal("hostinjected", result);
    }

    [Fact]
    public void ResolveArguments_StripsNewline()
    {
        var tool = new ExternalToolDefinition { Arguments = "{Host}" };
        var result = tool.ResolveArguments("host\ninjected", 22, "user");
        Assert.Equal("hostinjected", result);
    }

    // ── Empty arguments ─────────────────────────────────────────────────

    [Fact]
    public void ResolveArguments_EmptyArguments_ReturnsEmpty()
    {
        var tool = new ExternalToolDefinition { Arguments = "" };
        var result = tool.ResolveArguments("host", 22, "user");
        Assert.Equal("", result);
    }

    // ── SupportedPlaceholders static array ──────────────────────────────

    [Fact]
    public void SupportedPlaceholders_HasEightEntries()
    {
        Assert.Equal(8, ExternalToolDefinition.SupportedPlaceholders.Length);
    }

    [Fact]
    public void SupportedPlaceholders_ContainsAllExpectedVariables()
    {
        var variables = ExternalToolDefinition.SupportedPlaceholders
            .Select(p => p.Variable).ToList();

        Assert.Contains("{Host}", variables);
        Assert.Contains("{Port}", variables);
        Assert.Contains("{User}", variables);
        Assert.Contains("{ServerName}", variables);
        Assert.Contains("{Protocol}", variables);
        Assert.Contains("{KeyFile}", variables);
        Assert.Contains("{Project}", variables);
        Assert.Contains("{Gateway}", variables);
    }

    [Fact]
    public void SupportedPlaceholders_AllHaveDescriptionKeys()
    {
        foreach (var (variable, descriptionKey) in ExternalToolDefinition.SupportedPlaceholders)
        {
            Assert.False(string.IsNullOrWhiteSpace(variable), $"Variable should not be blank");
            Assert.False(string.IsNullOrWhiteSpace(descriptionKey), $"DescriptionKey should not be blank for {variable}");
        }
    }

    // ── Full serialization roundtrip ────────────────────────────────────

    [Fact]
    public void FullSerialization_Roundtrip_PreservesAllProperties()
    {
        var tool = new ExternalToolDefinition
        {
            Name = "PuTTY",
            ExecutablePath = @"C:\tools\putty.exe",
            Arguments = "-ssh {User}@{Host} -P {Port}",
            IconGlyph = "\uE8A7",
            WorkingDirectory = @"C:\tools",
            RunAsAdministrator = true,
            RunHidden = false
        };

        var json = System.Text.Json.JsonSerializer.Serialize(tool);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ExternalToolDefinition>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("PuTTY", deserialized.Name);
        Assert.Equal(@"C:\tools\putty.exe", deserialized.ExecutablePath);
        Assert.Equal("-ssh {User}@{Host} -P {Port}", deserialized.Arguments);
        Assert.Equal("\uE8A7", deserialized.IconGlyph);
        Assert.Equal(@"C:\tools", deserialized.WorkingDirectory);
        Assert.True(deserialized.RunAsAdministrator);
        Assert.False(deserialized.RunHidden);
    }
}
