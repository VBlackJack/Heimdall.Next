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

using Heimdall.Core.Codecs;

namespace Heimdall.Core.Tests;

public sealed class JsonCodecTests
{
    [Fact]
    public void Format_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => JsonCodec.Format(null!, true));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void Format_Whitespace_ReturnsEmpty(string input)
    {
        var result = JsonCodec.Format(input, true);

        Assert.Equal(JsonFormatStatus.Empty, result.Status);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void Format_Prettify_Object_ReturnsIndentedJson()
    {
        var result = JsonCodec.Format("{\"a\":1,\"b\":2}", true);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.Contains(Environment.NewLine, result.Output, StringComparison.Ordinal);
        Assert.Contains("\"a\": 1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_Minify_Object_ReturnsCompactJson()
    {
        var result = JsonCodec.Format("{\n  \"a\": 1,\n  \"b\": 2\n}", false);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.Equal("{\"a\":1,\"b\":2}", result.Output);
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("123", "123")]
    [InlineData("\"text\"", "\"text\"")]
    public void Format_Primitive_RoundTrips(string input, string expected)
    {
        var result = JsonCodec.Format(input, false);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.Equal(expected, result.Output);
    }

    [Fact]
    public void Format_Array_Prettifies()
    {
        var result = JsonCodec.Format("[1,2,3]", true);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.Contains("[", result.Output, StringComparison.Ordinal);
        Assert.Contains("  2", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_UnsafeRelaxedJsonEscaping_PreservesCharacters()
    {
        var result = JsonCodec.Format("{\"text\":\"é<&/'\"}", false);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.Contains("é", result.Output, StringComparison.Ordinal);
        Assert.Contains("<", result.Output, StringComparison.Ordinal);
        Assert.Contains("&", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00e9", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Format_EscapedCharacters_Preserved()
    {
        var result = JsonCodec.Format("{\"text\":\"line\\t\\\"quote\\\"\\\\slash\\u1234\"}", false);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.Contains("\\t", result.Output, StringComparison.Ordinal);
        Assert.Contains("\\\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("\\\\", result.Output, StringComparison.Ordinal);
        Assert.Contains("ሴ", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ScientificNotation_Preserved()
    {
        var result = JsonCodec.Format("{\"n\":1.23e+10}", false);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.Equal("{\"n\":1.23e+10}", result.Output);
    }

    [Fact]
    public void Format_DeepObject_Prettifies()
    {
        const string input = "{\"a\":{\"b\":{\"c\":[1,2,{\"d\":4}]}}}";

        var result = JsonCodec.Format(input, true);

        Assert.Equal(JsonFormatStatus.Success, result.Status);
        Assert.Contains("\"d\": 4", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_InvalidJson_ReturnsParseError()
    {
        var result = JsonCodec.Format("{\"a\":", true);

        Assert.Equal(JsonFormatStatus.ParseError, result.Status);
        Assert.NotEmpty(result.ErrorMessage);
    }

    [Fact]
    public void Format_InvalidJson_WithMultilineInput_ReturnsPosition()
    {
        var result = JsonCodec.Format("{\n  \"a\": 1,\n}", true);

        Assert.Equal(JsonFormatStatus.ParseError, result.Status);
        Assert.True(result.LineNumber.HasValue);
        Assert.True(result.ColumnNumber.HasValue);
    }

    [Fact]
    public void Format_PrettifyThenMinify_RemainsSemanticallyEquivalent()
    {
        var prettified = JsonCodec.Format("{\"a\":1,\"b\":[1,2,3]}", true);
        var minified = JsonCodec.Format(prettified.Output, false);

        Assert.Equal(JsonFormatStatus.Success, prettified.Status);
        Assert.Equal(JsonFormatStatus.Success, minified.Status);
        Assert.Equal("{\"a\":1,\"b\":[1,2,3]}", minified.Output);
    }
}
