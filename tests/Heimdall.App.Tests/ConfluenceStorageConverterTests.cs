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

public class ConfluenceStorageConverterTests
{
    [Fact]
    public void EmptyMarkdown_ReturnsWrappedDiv()
    {
        var result = ConfluenceStorageConverter.Convert("");
        Assert.Contains("<div xmlns:ac=", result);
        Assert.Contains("</div>", result);
    }

    [Fact]
    public void CodeBlock_ConvertedToStructuredMacro()
    {
        var markdown = "```bash\necho hello\n```";
        var result = ConfluenceStorageConverter.Convert(markdown);
        Assert.Contains("ac:structured-macro", result);
        Assert.Contains("ac:name=\"code\"", result);
        Assert.Contains("<![CDATA[", result);
        Assert.Contains("echo hello", result);
    }

    [Fact]
    public void CodeBlock_WithLanguage_HasLanguageParam()
    {
        var markdown = "```python\nprint('hi')\n```";
        var result = ConfluenceStorageConverter.Convert(markdown);
        Assert.Contains("ac:name=\"language\"", result);
        Assert.Contains("python", result);
    }

    [Fact]
    public void CodeBlock_WithoutLanguage_NoLanguageParam()
    {
        var markdown = "```\nplain code\n```";
        var result = ConfluenceStorageConverter.Convert(markdown);
        Assert.Contains("ac:structured-macro", result);
        Assert.DoesNotContain("ac:name=\"language\"", result);
    }

    [Fact]
    public void Image_ConvertedToAcImage()
    {
        var markdown = "![alt text](http://example.com/img.png)";
        var result = ConfluenceStorageConverter.Convert(markdown);
        Assert.Contains("ac:image", result);
        Assert.Contains("ri:url", result);
        Assert.Contains("http://example.com/img.png", result);
    }

    [Fact]
    public void CheckedTask_ConvertedToCheckmark()
    {
        var markdown = "- [x] done task";
        var result = ConfluenceStorageConverter.Convert(markdown);
        Assert.Contains("&#9745;", result);
        Assert.Contains("done task", result);
    }

    [Fact]
    public void UncheckedTask_ConvertedToEmptyBox()
    {
        var markdown = "- [ ] todo task";
        var result = ConfluenceStorageConverter.Convert(markdown);
        Assert.Contains("&#9744;", result);
        Assert.Contains("todo task", result);
    }

    [Fact]
    public void Strikethrough_ConvertedToLineThrough()
    {
        var markdown = "~~deleted text~~";
        var result = ConfluenceStorageConverter.Convert(markdown);
        Assert.Contains("text-decoration: line-through", result);
        Assert.Contains("deleted text", result);
    }

    [Fact]
    public void Heading_PreservedAsHtml()
    {
        var markdown = "# Title";
        var result = ConfluenceStorageConverter.Convert(markdown);
        Assert.Contains("<h1>Title</h1>", result);
    }

    [Fact]
    public void CodeBlock_CdataEscaping()
    {
        var markdown = "```\ndata with ]]> inside\n```";
        var result = ConfluenceStorageConverter.Convert(markdown);
        Assert.Contains("]]]]><![CDATA[>", result);
        Assert.DoesNotContain("]]>]]>", result);
    }
}
