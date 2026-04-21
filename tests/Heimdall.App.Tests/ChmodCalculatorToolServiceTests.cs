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

public sealed class ChmodCalculatorToolServiceTests
{
    private readonly ChmodCalculatorToolService _service = new();

    [Fact]
    public void TryParseOctal_DelegatesToCore()
    {
        var success = _service.TryParseOctal("755", out var mode);

        Assert.True(success);
        Assert.Equal("755", mode.ToOctal());
    }

    [Fact]
    public void TryParseOctal_Invalid_ReturnsFalse()
    {
        var success = _service.TryParseOctal("999", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParseSymbolic_DelegatesToCore()
    {
        var success = _service.TryParseSymbolic("u+rwx,g+rx,o+r", out var mode);

        Assert.True(success);
        Assert.Equal("754", mode.ToOctal());
    }

    [Fact]
    public void TryParseSymbolic_Invalid_ReturnsFalse()
    {
        var success = _service.TryParseSymbolic("bad", out _);

        Assert.False(success);
    }
}
