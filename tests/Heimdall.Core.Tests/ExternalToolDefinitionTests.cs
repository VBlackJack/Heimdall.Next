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
}
