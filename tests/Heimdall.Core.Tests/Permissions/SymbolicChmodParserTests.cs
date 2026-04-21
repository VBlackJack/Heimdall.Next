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

using Heimdall.Core.Permissions;

namespace Heimdall.Core.Tests;

public sealed class SymbolicChmodParserTests
{
    [Theory]
    [InlineData("u+x", "100")]
    [InlineData("g+w", "020")]
    [InlineData("o+r", "004")]
    [InlineData("a+r", "444")]
    [InlineData("u=rw", "600")]
    [InlineData("g=rx", "050")]
    [InlineData("o=", "000")]
    [InlineData("u+rwx,g+rx,o+r", "754")]
    [InlineData("u=rw,g=r,o=", "640")]
    [InlineData("u+x,,g+w", "120")]
    public void TryParse_ValidClauses_ReturnExpectedMode(string input, string expectedOctal)
    {
        var success = SymbolicChmodParser.TryParse(input, out var mode);

        Assert.True(success);
        Assert.Equal(expectedOctal, mode.ToOctal());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("z+r")]
    [InlineData("u?x")]
    [InlineData("u+r,garbage")]
    [InlineData(",")]
    [InlineData("u+s")]
    public void TryParse_InvalidClauses_ReturnFalse(string? input)
    {
        var success = SymbolicChmodParser.TryParse(input, out var mode);

        Assert.False(success);
        Assert.Equal("000", PosixMode.Empty.ToOctal());
        Assert.Equal(default, mode);
    }

    [Fact]
    public void TryParse_BaseAlwaysStartsAtEmpty()
    {
        var success = SymbolicChmodParser.TryParse("g+w", out var mode);

        Assert.True(success);
        Assert.Equal("020", mode.ToOctal());
    }

    [Fact]
    public void TryParse_EqualsOperation_ClearsRoleBeforeApplying()
    {
        var success = SymbolicChmodParser.TryParse("a+rwx,g=", out var mode);

        Assert.True(success);
        Assert.Equal("707", mode.ToOctal());
    }
}
