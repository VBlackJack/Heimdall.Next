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

public class GroupDefaultsDtoTests
{
    // ── Resolve: exact match ────────────────────────────────────────────

    [Fact]
    public void Resolve_ExactMatch_ReturnsGroupDefaults()
    {
        var defaults = new Dictionary<string, GroupDefaultsDto>
        {
            ["PROD"] = new()
            {
                SshUsername = "deploy",
                SshGatewayId = "gw-prod",
                SshPort = 2222
            }
        };

        var result = GroupDefaultsDto.Resolve("PROD", defaults);

        Assert.Equal("deploy", result.SshUsername);
        Assert.Equal("gw-prod", result.SshGatewayId);
        Assert.Equal(2222, result.SshPort);
    }

    // ── Resolve: hierarchical fallback ──────────────────────────────────

    [Fact]
    public void Resolve_ChildGroup_InheritsFromParent()
    {
        var defaults = new Dictionary<string, GroupDefaultsDto>
        {
            ["PROD"] = new()
            {
                SshUsername = "deploy",
                SshGatewayId = "gw-prod"
            }
        };

        var result = GroupDefaultsDto.Resolve("PROD/Linux", defaults);

        Assert.Equal("deploy", result.SshUsername);
        Assert.Equal("gw-prod", result.SshGatewayId);
    }

    [Fact]
    public void Resolve_RootValueOverridesLeaf_WhenBothProvided()
    {
        // The second pass iterates leaf-first then root, so root writes last and wins.
        var defaults = new Dictionary<string, GroupDefaultsDto>
        {
            ["PROD"] = new()
            {
                SshUsername = "deploy",
                SshGatewayId = "gw-prod"
            },
            ["PROD/Linux"] = new()
            {
                SshUsername = "linux-admin"
            }
        };

        var result = GroupDefaultsDto.Resolve("PROD/Linux", defaults);

        // Root "PROD" overwrites leaf "PROD/Linux" in the second pass
        Assert.Equal("deploy", result.SshUsername);
        Assert.Equal("gw-prod", result.SshGatewayId);
    }

    [Fact]
    public void Resolve_LeafOnlyFields_InheritedWhenRootLacksValue()
    {
        var defaults = new Dictionary<string, GroupDefaultsDto>
        {
            ["PROD"] = new()
            {
                SshUsername = "deploy"
            },
            ["PROD/Linux"] = new()
            {
                SshKeyPath = "/keys/linux.pem"
            }
        };

        var result = GroupDefaultsDto.Resolve("PROD/Linux", defaults);

        // Root provides username, leaf provides key path (no conflict)
        Assert.Equal("deploy", result.SshUsername);
        Assert.Equal("/keys/linux.pem", result.SshKeyPath);
    }

    [Fact]
    public void Resolve_ThreeLevelHierarchy_RootFieldsOverrideLeaf()
    {
        var defaults = new Dictionary<string, GroupDefaultsDto>
        {
            ["PROD"] = new()
            {
                SshUsername = "root",
                SshPort = 22,
                Environment = "Production"
            },
            ["PROD/Linux"] = new()
            {
                SshKeyPath = "/keys/linux.pem"
            },
            ["PROD/Linux/Web"] = new()
            {
                SshPort = 2222
            }
        };

        var result = GroupDefaultsDto.Resolve("PROD/Linux/Web", defaults);

        // Root values win when specified at multiple levels
        Assert.Equal("root", result.SshUsername);
        Assert.Equal(22, result.SshPort);
        Assert.Equal("/keys/linux.pem", result.SshKeyPath);
        Assert.Equal("Production", result.Environment);
    }

    // ── Resolve: no match ───────────────────────────────────────────────

    [Fact]
    public void Resolve_NoMatch_ReturnsEmptyDefaults()
    {
        var defaults = new Dictionary<string, GroupDefaultsDto>
        {
            ["PROD"] = new() { SshUsername = "deploy" }
        };

        var result = GroupDefaultsDto.Resolve("DEV", defaults);

        Assert.Null(result.SshUsername);
        Assert.Null(result.SshGatewayId);
        Assert.Null(result.SshKeyPath);
        Assert.Null(result.SshPort);
    }

    [Fact]
    public void Resolve_NullGroupName_ReturnsEmptyDefaults()
    {
        var defaults = new Dictionary<string, GroupDefaultsDto>
        {
            ["PROD"] = new() { SshUsername = "deploy" }
        };

        var result = GroupDefaultsDto.Resolve(null, defaults);

        Assert.Null(result.SshUsername);
    }

    [Fact]
    public void Resolve_EmptyGroupName_ReturnsEmptyDefaults()
    {
        var defaults = new Dictionary<string, GroupDefaultsDto>
        {
            ["PROD"] = new() { SshUsername = "deploy" }
        };

        var result = GroupDefaultsDto.Resolve("", defaults);

        Assert.Null(result.SshUsername);
    }

    [Fact]
    public void Resolve_EmptyDictionary_ReturnsEmptyDefaults()
    {
        var result = GroupDefaultsDto.Resolve("PROD", new Dictionary<string, GroupDefaultsDto>());

        Assert.Null(result.SshUsername);
    }

    // ── ApplyTo: fills empty server fields ──────────────────────────────

    [Fact]
    public void ApplyTo_SetsGatewayWhenServerFieldEmpty()
    {
        var groupDefaults = new GroupDefaultsDto { SshGatewayId = "gw-default" };
        var server = new ServerProfileDto();

        groupDefaults.ApplyTo(server);

        Assert.Equal("gw-default", server.SshGatewayId);
    }

    [Fact]
    public void ApplyTo_SetsSshUsernameWhenServerFieldEmpty()
    {
        var groupDefaults = new GroupDefaultsDto { SshUsername = "deploy" };
        var server = new ServerProfileDto();

        groupDefaults.ApplyTo(server);

        Assert.Equal("deploy", server.SshUsername);
    }

    [Fact]
    public void ApplyTo_SetsSshKeyPathWhenServerFieldEmpty()
    {
        var groupDefaults = new GroupDefaultsDto { SshKeyPath = "/keys/id_rsa" };
        var server = new ServerProfileDto();

        groupDefaults.ApplyTo(server);

        Assert.Equal("/keys/id_rsa", server.SshKeyPath);
    }

    [Fact]
    public void ApplyTo_SetsSshPortWhenServerHasDefault22()
    {
        var groupDefaults = new GroupDefaultsDto { SshPort = 2222 };
        var server = new ServerProfileDto { SshPort = 22 };

        groupDefaults.ApplyTo(server);

        Assert.Equal(2222, server.SshPort);
    }

    // ── ApplyTo: does NOT override existing values ──────────────────────

    [Fact]
    public void ApplyTo_DoesNotOverrideServerGateway()
    {
        var groupDefaults = new GroupDefaultsDto { SshGatewayId = "gw-default" };
        var server = new ServerProfileDto { SshGatewayId = "gw-custom" };

        groupDefaults.ApplyTo(server);

        Assert.Equal("gw-custom", server.SshGatewayId);
    }

    [Fact]
    public void ApplyTo_DoesNotOverrideServerSshUsername()
    {
        var groupDefaults = new GroupDefaultsDto { SshUsername = "deploy" };
        var server = new ServerProfileDto { SshUsername = "custom-user" };

        groupDefaults.ApplyTo(server);

        Assert.Equal("custom-user", server.SshUsername);
    }

    [Fact]
    public void ApplyTo_DoesNotOverrideServerSshKeyPath()
    {
        var groupDefaults = new GroupDefaultsDto { SshKeyPath = "/keys/default.pem" };
        var server = new ServerProfileDto { SshKeyPath = "/keys/custom.pem" };

        groupDefaults.ApplyTo(server);

        Assert.Equal("/keys/custom.pem", server.SshKeyPath);
    }

    [Fact]
    public void ApplyTo_DoesNotOverrideSshPortWhenNotDefault()
    {
        var groupDefaults = new GroupDefaultsDto { SshPort = 2222 };
        var server = new ServerProfileDto { SshPort = 9999 };

        groupDefaults.ApplyTo(server);

        Assert.Equal(9999, server.SshPort);
    }
}
