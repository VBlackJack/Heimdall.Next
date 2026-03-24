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

using System.Net;
using System.Security;
using System.Text.RegularExpressions;

namespace Heimdall.App.Services;

public static partial class ConfluenceStorageConverter
{
    [GeneratedRegex(@"<pre><code(?: class=""language-(?<lang>[^""]+)"")?>(?<code>.*?)</code></pre>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FencedCodeBlockRegex();

    [GeneratedRegex(@"<img[^>]*src=""(?<src>[^""]+)""[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"<input[^>]*type=""checkbox""[^>]*checked=""checked""[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CheckedTaskRegex();

    [GeneratedRegex(@"<input[^>]*type=""checkbox""[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex UncheckedTaskRegex();

    [GeneratedRegex(@"<del>(?<text>.*?)</del>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StrikethroughRegex();

    public static string Convert(string markdown)
    {
        var html = SimpleMarkdownConverter.ToHtmlFragment(markdown);
        html = ConvertCodeBlocks(html);
        html = ConvertImages(html);
        html = CheckedTaskRegex().Replace(html, "&#9745; ");
        html = UncheckedTaskRegex().Replace(html, "&#9744; ");
        html = StrikethroughRegex().Replace(html, """<span style="text-decoration: line-through;">${text}</span>""");

        return $"""
<div xmlns:ac="http://atlassian.com/content" xmlns:ri="http://atlassian.com/resource/identifier">
{html}
</div>
""";
    }

    private static string ConvertCodeBlocks(string html)
    {
        return FencedCodeBlockRegex().Replace(html, match =>
        {
            var language = match.Groups["lang"].Success
                ? SecurityElement.Escape(match.Groups["lang"].Value)
                : null;
            var code = WebUtility.HtmlDecode(match.Groups["code"].Value)
                .Replace("]]>", "]]]]><![CDATA[>");

            return $$"""
<ac:structured-macro ac:name="code">
{{(string.IsNullOrWhiteSpace(language) ? string.Empty : $"  <ac:parameter ac:name=\"language\">{language}</ac:parameter>\n")}}  <ac:plain-text-body><![CDATA[{{code}}]]></ac:plain-text-body>
</ac:structured-macro>
""";
        });
    }

    private static string ConvertImages(string html)
    {
        return ImageRegex().Replace(html, match =>
        {
            var source = SecurityElement.Escape(match.Groups["src"].Value) ?? string.Empty;
            return $"""<ac:image><ri:url ri:value="{source}" /></ac:image>""";
        });
    }
}
