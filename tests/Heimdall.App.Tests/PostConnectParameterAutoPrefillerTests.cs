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

using System.Globalization;
using Heimdall.App.Services.PostConnect;
using TwinShell.Core.Models;

namespace Heimdall.App.Tests;

public sealed class PostConnectParameterAutoPrefillerTests
{
    [Theory]
    [InlineData("host")]
    [InlineData("hostname")]
    [InlineData("targetHost")]
    [InlineData("server")]
    [InlineData("remoteHost")]
    [InlineData("HOST")]
    public void Prefill_HostAlias_ReturnsContextHost_AllVariants(string name)
    {
        var result = PostConnectParameterAutoPrefiller.Prefill(
        [
            new TemplateParameter { Name = name, Label = "Host" }
        ],
        new AutoPrefillContext("server.example.com", 22, "alice", "SSH"));

        Assert.Equal("server.example.com", result[name]);
    }

    [Theory]
    [InlineData("port")]
    [InlineData("sshPort")]
    [InlineData("targetPort")]
    public void Prefill_PortAlias_ReturnsContextPortString(string name)
    {
        var result = PostConnectParameterAutoPrefiller.Prefill(
        [
            new TemplateParameter { Name = name, Label = "Port" }
        ],
        new AutoPrefillContext("server.example.com", 2222, "alice", "SSH"));

        Assert.Equal(2222.ToString(CultureInfo.InvariantCulture), result[name]);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("username")]
    [InlineData("sshUser")]
    [InlineData("targetUser")]
    public void Prefill_UserAlias_ReturnsContextUsername(string name)
    {
        var result = PostConnectParameterAutoPrefiller.Prefill(
        [
            new TemplateParameter { Name = name, Label = "User" }
        ],
        new AutoPrefillContext("server.example.com", 22, "alice", "SSH"));

        Assert.Equal("alice", result[name]);
    }

    [Fact]
    public void Prefill_UnknownParameterName_DoesNotAppear()
    {
        var result = PostConnectParameterAutoPrefiller.Prefill(
        [
            new TemplateParameter { Name = "path", Label = "Path" }
        ],
        new AutoPrefillContext("server.example.com", 22, "alice", "SSH"));

        Assert.Empty(result);
    }

    [Fact]
    public void Prefill_ExistingValueWins_EvenOverMappableAlias()
    {
        var result = PostConnectParameterAutoPrefiller.Prefill(
        [
            new TemplateParameter { Name = "host", Label = "Host" }
        ],
        new AutoPrefillContext("server.example.com", 22, "alice", "SSH"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = "legacy.example.com"
        });

        Assert.Equal("legacy.example.com", result["host"]);
    }

    [Fact]
    public void Prefill_ExistingEmptyValue_Preserved()
    {
        var result = PostConnectParameterAutoPrefiller.Prefill(
        [
            new TemplateParameter { Name = "host", Label = "Host" }
        ],
        new AutoPrefillContext("server.example.com", 22, "alice", "SSH"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = string.Empty
        });

        Assert.Equal(string.Empty, result["host"]);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("token")]
    [InlineData("apiKey")]
    [InlineData("privateKey")]
    [InlineData("passphrase")]
    public void Prefill_SecretParameterNeverPrefilled(string name)
    {
        var result = PostConnectParameterAutoPrefiller.Prefill(
        [
            new TemplateParameter { Name = name, Label = name }
        ],
        new AutoPrefillContext("server.example.com", 22, "alice", "SSH"));

        Assert.Empty(result);
    }

    [Fact]
    public void Prefill_NullContextField_SkipsMappableParam()
    {
        var result = PostConnectParameterAutoPrefiller.Prefill(
        [
            new TemplateParameter { Name = "port", Label = "Port" }
        ],
        new AutoPrefillContext("server.example.com", null, "alice", "SSH"));

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("keyName")]
    [InlineData("keyPath")]
    public void IsSecretParameter_DoesNotTreatBenignKeyNamesAsSecret(string name)
    {
        Assert.False(PostConnectParameterAutoPrefiller.IsSecretParameter(name));
    }
}
