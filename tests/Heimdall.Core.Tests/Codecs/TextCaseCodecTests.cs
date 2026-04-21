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

public sealed class TextCaseCodecTests
{
    [Fact]
    public void Convert_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TextCaseCodec.Convert(null!, TextCaseStyle.Camel));
    }

    [Fact]
    public void Convert_InvalidEnum_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextCaseCodec.Convert("value", (TextCaseStyle)999));
    }

    [Theory]
    [InlineData("", TextCaseStyle.Camel, "")]
    [InlineData("hello world", TextCaseStyle.Camel, "helloWorld")]
    [InlineData("XMLParser", TextCaseStyle.Camel, "xmlParser")]
    [InlineData("xml_parser", TextCaseStyle.Camel, "xmlParser")]
    public void Convert_Camel_ReturnsExpectedValue(string input, TextCaseStyle style, string expected)
    {
        Assert.Equal(expected, TextCaseCodec.Convert(input, style));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello world", "HelloWorld")]
    [InlineData("XMLParser", "XmlParser")]
    [InlineData("xml_parser", "XmlParser")]
    public void Convert_Pascal_ReturnsExpectedValue(string input, string expected)
    {
        Assert.Equal(expected, TextCaseCodec.Convert(input, TextCaseStyle.Pascal));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello world", "hello_world")]
    [InlineData("XMLParser", "xml_parser")]
    [InlineData("xml-parser", "xml_parser")]
    public void Convert_Snake_ReturnsExpectedValue(string input, string expected)
    {
        Assert.Equal(expected, TextCaseCodec.Convert(input, TextCaseStyle.Snake));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello world", "hello-world")]
    [InlineData("XMLParser", "xml-parser")]
    [InlineData("xml_parser", "xml-parser")]
    public void Convert_Kebab_ReturnsExpectedValue(string input, string expected)
    {
        Assert.Equal(expected, TextCaseCodec.Convert(input, TextCaseStyle.Kebab));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello world", "HELLO WORLD")]
    [InlineData("XMLParser", "XMLPARSER")]
    [InlineData("snake_case", "SNAKE_CASE")]
    public void Convert_Upper_BypassesSplitWords(string input, string expected)
    {
        Assert.Equal(expected, TextCaseCodec.Convert(input, TextCaseStyle.Upper));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Hello World", "hello world")]
    [InlineData("XMLParser", "xmlparser")]
    [InlineData("SNAKE_CASE", "snake_case")]
    public void Convert_Lower_BypassesSplitWords(string input, string expected)
    {
        Assert.Equal(expected, TextCaseCodec.Convert(input, TextCaseStyle.Lower));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello world", "Hello World")]
    [InlineData("XMLParser", "Xml Parser")]
    [InlineData("xml_parser", "Xml Parser")]
    public void Convert_Title_ReturnsExpectedValue(string input, string expected)
    {
        Assert.Equal(expected, TextCaseCodec.Convert(input, TextCaseStyle.Title));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello world", "HELLO_WORLD")]
    [InlineData("XMLParser", "XML_PARSER")]
    [InlineData("xml-parser", "XML_PARSER")]
    public void Convert_Constant_ReturnsExpectedValue(string input, string expected)
    {
        Assert.Equal(expected, TextCaseCodec.Convert(input, TextCaseStyle.Constant));
    }

    [Fact]
    public void Convert_SplitsMixedSeparatorsAndCamelBoundaries()
    {
        var actual = TextCaseCodec.Convert("helloWorld_test-case", TextCaseStyle.Title);

        Assert.Equal("Hello World Test Case", actual);
    }

    [Fact]
    public void Convert_FiltersLeadingAndTrailingSeparators()
    {
        var actual = TextCaseCodec.Convert("__hello-world__", TextCaseStyle.Pascal);

        Assert.Equal("HelloWorld", actual);
    }

    [Fact]
    public void Convert_PreservesUnicodeLowercaseBehavior()
    {
        var actual = TextCaseCodec.Convert("héllo wörld", TextCaseStyle.Camel);

        Assert.Equal("hélloWörld", actual);
    }
}
