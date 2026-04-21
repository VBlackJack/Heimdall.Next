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

public sealed class UrlEncoderToolServiceTests
{
    private readonly UrlEncoderToolService _service = new();

    [Fact]
    public void Encode_ComponentMode_UsesCodecBehavior()
    {
        var actual = _service.Encode("a/b?c=d", true);

        Assert.Equal("a%2Fb%3Fc%3Dd", actual);
    }

    [Fact]
    public void Encode_PreserveStructure_UsesCodecBehavior()
    {
        var actual = _service.Encode("https://example.com/a b?x=1", false);

        Assert.Equal("https://example.com/a%20b?x=1", actual);
    }

    [Fact]
    public void Decode_ReturnsPlainText()
    {
        var actual = _service.Decode("caf%C3%A9");

        Assert.Equal("café", actual);
    }

    [Fact]
    public void Decode_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _service.Decode(string.Empty));
    }

    [Fact]
    public void Service_IsReusableAcrossCalls()
    {
        var first = _service.Encode("a b", false);
        var second = _service.Decode(first);

        Assert.Equal("a%20b", first);
        Assert.Equal("a b", second);
    }
}
