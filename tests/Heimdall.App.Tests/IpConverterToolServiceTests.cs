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

public sealed class IpConverterToolServiceTests
{
    private readonly IpConverterToolService _service = new();

    [Fact]
    public void TryConvert_ValidInput_DelegatesToCodec()
    {
        Assert.True(_service.TryConvert("192.168.1.1", out var result));
        Assert.Equal("0xC0A80101", result.Hex);
    }

    [Fact]
    public void TryConvert_InvalidInput_ReturnsFalse()
    {
        Assert.False(_service.TryConvert("invalid", out _));
    }

    [Fact]
    public void TryConvert_TrimmedInput_StillSucceeds()
    {
        Assert.True(_service.TryConvert(" 192.168.1.1 ", out var result));
        Assert.Equal("192.168.1.1", result.Dotted);
    }
}
