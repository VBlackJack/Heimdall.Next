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

using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public sealed class WhoisLookupModelsTests
{
    [Fact]
    public void Ok_WithOutput_ProducesSuccessResult()
    {
        var result = WhoisLookupResult.Ok("hello", 42);

        Assert.Equal("hello", result.Output);
        Assert.Equal(42, result.ElapsedMs);
        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.ErrorKey);
        Assert.Null(result.ErrorArg);
    }

    [Fact]
    public void Ok_NullOutput_IsNormalizedToEmptyString()
    {
        var result = WhoisLookupResult.Ok(null, 1);

        Assert.Equal(string.Empty, result.Output);
        Assert.True(result.Success);
    }

    [Fact]
    public void Error_WithErrorArg_ProducesFailureResult()
    {
        var result = WhoisLookupResult.Error("ToolWhoisErrorFailed", 100, "connection refused");

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal("ToolWhoisErrorFailed", result.ErrorKey);
        Assert.Equal("connection refused", result.ErrorArg);
        Assert.Equal(100, result.ElapsedMs);
    }

    [Fact]
    public void Error_WithoutErrorArg_DefaultsToNull()
    {
        var result = WhoisLookupResult.Error("ToolWhoisErrorTimeout", 50);

        Assert.Null(result.ErrorArg);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Error_InvalidErrorKey_ThrowsArgumentException(string? invalidKey)
    {
        var ex = Record.Exception(() => WhoisLookupResult.Error(invalidKey!, 0));

        Assert.NotNull(ex);
        Assert.IsAssignableFrom<ArgumentException>(ex);
    }

    [Fact]
    public void Records_AreValueEqualByContent()
    {
        var requestA = new WhoisLookupRequest("example.com");
        var requestB = new WhoisLookupRequest("example.com");
        var resultA = WhoisLookupResult.Ok("same", 10);
        var resultB = WhoisLookupResult.Ok("same", 10);

        Assert.Equal(requestA, requestB);
        Assert.Equal(resultA, resultB);
    }
}
