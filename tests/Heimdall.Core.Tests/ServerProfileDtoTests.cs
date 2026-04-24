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

using System.Text.Json;
using Heimdall.Core.Configuration;

namespace Heimdall.Core.Tests;

public class ServerProfileDtoTests
{
    // ── Default values ──────────────────────────────────────────────────

    [Fact]
    public void DefaultValues_ConnectionType_IsRdp()
    {
        var dto = new ServerProfileDto();

        Assert.Equal("RDP", dto.ConnectionType);
    }

    [Fact]
    public void DefaultValues_RemotePort_Is3389()
    {
        var dto = new ServerProfileDto();

        Assert.Equal(3389, dto.RemotePort);
    }

    [Fact]
    public void DefaultValues_LocalPort_Is33890()
    {
        var dto = new ServerProfileDto();

        Assert.Equal(33890, dto.LocalPort);
    }

    [Fact]
    public void DefaultValues_SshPort_Is22()
    {
        var dto = new ServerProfileDto();

        Assert.Equal(22, dto.SshPort);
    }

    [Fact]
    public void DefaultValues_LocalShellElevated_IsFalse()
    {
        var dto = new ServerProfileDto();

        Assert.False(dto.LocalShellElevated);
    }

    [Fact]
    public void DefaultValues_RdpMode_IsEmbedded()
    {
        var dto = new ServerProfileDto();

        Assert.Equal("Embedded", dto.RdpMode);
    }

    [Fact]
    public void DefaultValues_RdpDefaults_MatchExpected()
    {
        var dto = new ServerProfileDto();

        Assert.True(dto.RdpUseGlobalDefaults);
        Assert.True(dto.RdpRedirectClipboard);
        Assert.True(dto.RdpDynamicResolution);
        Assert.True(dto.RdpNla);
        Assert.Equal(32, dto.RdpColorDepth);
        Assert.True(dto.RdpBitmapCaching);
        Assert.True(dto.RdpCompression);
        Assert.True(dto.RdpAutoReconnect);
    }

    [Fact]
    public void DefaultValues_OptionalFields_AreNull()
    {
        var dto = new ServerProfileDto();

        Assert.Null(dto.Group);
        Assert.Null(dto.SshGatewayId);
        Assert.Null(dto.RdpUsername);
        Assert.Null(dto.RdpPasswordEncrypted);
        Assert.Null(dto.ProjectId);
        Assert.Null(dto.SshUsername);
        Assert.Null(dto.SshKeyPath);
        Assert.Null(dto.SshPasswordEncrypted);
        Assert.Null(dto.SshKeyPassphraseEncrypted);
        Assert.False(dto.HasSshKeyPassphraseEncryptedField);
        Assert.Null(dto.Tags);
        Assert.Null(dto.Environment);
        Assert.Null(dto.LocalShellExecutable);
        Assert.Null(dto.LocalShellArguments);
        Assert.Null(dto.LocalShellWorkingDirectory);
        Assert.Null(dto.RdpGateway);
    }

    // ── JSON round-trip ─────────────────────────────────────────────────

    [Fact]
    public void JsonRoundTrip_PreservesAllFields()
    {
        var original = new ServerProfileDto
        {
            Id = "srv-001",
            DisplayName = "Production DB",
            RemoteServer = "db.example.com",
            RemotePort = 3389,
            LocalPort = 13389,
            Group = "Databases",
            SshGatewayId = "gw-01",
            RdpUsername = @"CORP\admin",
            ConnectionType = "RDP",
            SshUsername = "deploy",
            SshPort = 2222,
            SshMode = "Embedded",
            SshAgentForwarding = true,
            SshKeyPath = @"C:\keys\id_rsa",
            SshPasswordEncrypted = "encrypted-password",
            SshKeyPassphraseEncrypted = "encrypted-key-passphrase",
            IsFavorite = true,
            SortOrder = 5,
            Tags = "production,critical",
            RdpMode = "Embedded",
            RdpMultiMonitor = true,
            Environment = "Production",
            LocalShellExecutable = "pwsh.exe",
            LocalShellElevated = true,
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ServerProfileDto>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.DisplayName, deserialized.DisplayName);
        Assert.Equal(original.RemoteServer, deserialized.RemoteServer);
        Assert.Equal(original.RemotePort, deserialized.RemotePort);
        Assert.Equal(original.LocalPort, deserialized.LocalPort);
        Assert.Equal(original.Group, deserialized.Group);
        Assert.Equal(original.SshGatewayId, deserialized.SshGatewayId);
        Assert.Equal(original.RdpUsername, deserialized.RdpUsername);
        Assert.Equal(original.ConnectionType, deserialized.ConnectionType);
        Assert.Equal(original.SshUsername, deserialized.SshUsername);
        Assert.Equal(original.SshPort, deserialized.SshPort);
        Assert.Equal(original.SshMode, deserialized.SshMode);
        Assert.Equal(original.SshAgentForwarding, deserialized.SshAgentForwarding);
        Assert.Equal(original.SshKeyPath, deserialized.SshKeyPath);
        Assert.Equal(original.SshPasswordEncrypted, deserialized.SshPasswordEncrypted);
        Assert.Equal(original.SshKeyPassphraseEncrypted, deserialized.SshKeyPassphraseEncrypted);
        Assert.True(deserialized.HasSshKeyPassphraseEncryptedField);
        Assert.Equal(original.IsFavorite, deserialized.IsFavorite);
        Assert.Equal(original.SortOrder, deserialized.SortOrder);
        Assert.Equal(original.Tags, deserialized.Tags);
        Assert.Equal(original.RdpMode, deserialized.RdpMode);
        Assert.Equal(original.RdpMultiMonitor, deserialized.RdpMultiMonitor);
        Assert.Equal(original.Environment, deserialized.Environment);
        Assert.Equal(original.LocalShellExecutable, deserialized.LocalShellExecutable);
        Assert.Equal(original.LocalShellElevated, deserialized.LocalShellElevated);
    }

    [Fact]
    public void JsonDeserialization_MissingFields_UsesDefaults()
    {
        var json = """{"Id":"minimal","DisplayName":"Minimal Server","RemoteServer":"10.0.0.1"}""";

        var dto = JsonSerializer.Deserialize<ServerProfileDto>(json);

        Assert.NotNull(dto);
        Assert.Equal("minimal", dto.Id);
        Assert.Equal("RDP", dto.ConnectionType);
        Assert.Equal(3389, dto.RemotePort);
        Assert.Equal(33890, dto.LocalPort);
        Assert.Equal(22, dto.SshPort);
        Assert.Equal("Embedded", dto.RdpMode);
        Assert.Equal("Embedded", dto.SshMode);
        Assert.False(dto.LocalShellElevated);
    }
}
