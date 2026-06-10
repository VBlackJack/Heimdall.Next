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
/// Verifies that <see cref="WebSocketVncProxy.SelectSubProtocol"/> echoes the
/// "binary" subprotocol noVNC requests (RFC 6455 requires the server to echo a
/// requested subprotocol or Chromium fails the connection) and returns
/// <c>null</c> for anything else. Producer: <c>AcceptLoopAsync</c> calls it
/// with the raw <c>Sec-WebSocket-Protocol</c> header before
/// <c>AcceptWebSocketAsync</c>.
/// </summary>
public class WebSocketVncProxySubProtocolTests
{
    [Theory]
    [InlineData("binary")]
    [InlineData("foo, binary")]
    public void SelectSubProtocol_BinaryOffered_ReturnsBinary(string offered)
    {
        Assert.Equal("binary", WebSocketVncProxy.SelectSubProtocol(offered));
    }

    [Theory]
    [InlineData("Binary")]
    [InlineData("foo")]
    public void SelectSubProtocol_BinaryNotOffered_ReturnsNull(string offered)
    {
        Assert.Null(WebSocketVncProxy.SelectSubProtocol(offered));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void SelectSubProtocol_NullOrEmpty_ReturnsNull(string? offered)
    {
        Assert.Null(WebSocketVncProxy.SelectSubProtocol(offered));
    }
}
