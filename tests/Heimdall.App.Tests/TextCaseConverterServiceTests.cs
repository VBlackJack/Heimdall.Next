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
using Heimdall.Core.Codecs;

namespace Heimdall.App.Tests;

public sealed class TextCaseConverterServiceTests
{
    private readonly TextCaseConverterService _service = new();

    [Theory]
    [InlineData("hello world", TextCaseStyle.Camel, "helloWorld")]
    [InlineData("hello world", TextCaseStyle.Title, "Hello World")]
    [InlineData("hello world", TextCaseStyle.Constant, "HELLO_WORLD")]
    public void Convert_DelegatesToCodec(string input, TextCaseStyle style, string expected)
    {
        Assert.Equal(expected, _service.Convert(input, style));
    }
}
