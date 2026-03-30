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

using Heimdall.App.Services;

namespace Heimdall.App.Tests;

/// <summary>
/// Verifies that <see cref="WebSocketVncProxy.IsAllowedOrigin"/> performs exact
/// host matching and rejects subdomain-based bypass attempts (CSWSH prevention).
/// </summary>
public class WebSocketVncProxyOriginTests
{
    [Theory]
    [InlineData("https://heimdall-vnc.local")]
    [InlineData("http://heimdall-vnc.local")]
    [InlineData("http://127.0.0.1")]
    [InlineData("https://127.0.0.1")]
    [InlineData("http://localhost")]
    [InlineData("https://localhost")]
    public void IsAllowedOrigin_ValidOrigin_ReturnsTrue(string origin)
    {
        Assert.True(WebSocketVncProxy.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("HTTPS://HEIMDALL-VNC.LOCAL")]
    [InlineData("HTTP://LOCALHOST")]
    [InlineData("Http://127.0.0.1")]
    public void IsAllowedOrigin_CaseInsensitive_ReturnsTrue(string origin)
    {
        Assert.True(WebSocketVncProxy.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://127.0.0.1:9090")]
    public void IsAllowedOrigin_WithPort_ReturnsTrue(string origin)
    {
        Assert.True(WebSocketVncProxy.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("https://heimdall-vnc.local.attacker.tld")]
    [InlineData("http://127.0.0.1.attacker.tld")]
    [InlineData("http://localhost.evil.com")]
    public void IsAllowedOrigin_SubdomainBypass_ReturnsFalse(string origin)
    {
        Assert.False(WebSocketVncProxy.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsAllowedOrigin_NullOrEmpty_ReturnsFalse(string? origin)
    {
        Assert.False(WebSocketVncProxy.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("://missing-scheme")]
    [InlineData("just some text")]
    public void IsAllowedOrigin_MalformedUri_ReturnsFalse(string origin)
    {
        Assert.False(WebSocketVncProxy.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("ftp://localhost")]
    [InlineData("ws://127.0.0.1")]
    [InlineData("wss://heimdall-vnc.local")]
    public void IsAllowedOrigin_InvalidScheme_ReturnsFalse(string origin)
    {
        Assert.False(WebSocketVncProxy.IsAllowedOrigin(origin));
    }

    [Fact]
    public void IsAllowedOrigin_DifferentHost_ReturnsFalse()
    {
        Assert.False(WebSocketVncProxy.IsAllowedOrigin("https://evil.com"));
    }
}
