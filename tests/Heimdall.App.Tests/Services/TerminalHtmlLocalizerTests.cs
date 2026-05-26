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
using System.Text.Json;
using FluentAssertions;
using Heimdall.App.Services;

namespace Heimdall.App.Tests.Services;

public sealed class TerminalHtmlLocalizerTests
{
    private const string SampleHtml =
        "<div id=\"loading\">Initializing terminal&#x2026;</div>" +
        "<input id=\"search-input\" type=\"text\" placeholder=\"Search...\" />" +
        "term.write('\\r\\n\\x1b[90m' + /*{{TERMINAL_SESSION_ENDED_LITERAL}}*/ + '\\x1b[0m\\r\\n');";

    [Fact]
    public void Localize_WithAllKeysResolved_ReplacesAllMarkers()
    {
        string result = TerminalHtmlLocalizer.Localize(SampleHtml, LocalizeFrench);

        result.Should().Contain("Initialisation du terminal\u2026");
        result.Should().Contain("Rechercher...");
        result.Should().Contain(JsonSerializer.Serialize("--- Session termin\u00e9e ---"));
        result.Should().NotContain(TerminalHtmlLocalizer.MarkerLoadingLabel);
        result.Should().NotContain(TerminalHtmlLocalizer.MarkerSearchPlaceholder);
        result.Should().NotContain(TerminalHtmlLocalizer.MarkerSessionEndedLiteral);
    }

    [Fact]
    public void Localize_WhenLoadingLabelIsNull_UsesEncodedFallback()
    {
        string result = TerminalHtmlLocalizer.Localize(SampleHtml, key =>
            string.Equals(key, TerminalHtmlLocalizer.KeyLoadingLabel, StringComparison.Ordinal)
                ? null
                : LocalizeFrench(key));

        result.Should().Contain(WebUtility.HtmlEncode(TerminalHtmlLocalizer.FallbackLoadingLabel));
        result.Should().Contain("Rechercher...");
        result.Should().Contain(JsonSerializer.Serialize("--- Session termin\u00e9e ---"));
    }

    [Fact]
    public void Localize_WhenSearchPlaceholderIsRawKey_UsesEncodedFallback()
    {
        string result = TerminalHtmlLocalizer.Localize(SampleHtml, key =>
            string.Equals(key, TerminalHtmlLocalizer.KeySearchPlaceholder, StringComparison.Ordinal)
                ? TerminalHtmlLocalizer.KeySearchPlaceholder
                : LocalizeFrench(key));

        result.Should().Contain(WebUtility.HtmlEncode(TerminalHtmlLocalizer.FallbackSearchPlaceholder));
        result.Should().Contain("Initialisation du terminal\u2026");
        result.Should().Contain(JsonSerializer.Serialize("--- Session termin\u00e9e ---"));
    }

    [Fact]
    public void Localize_WhenSessionEndedIsWhitespace_UsesJsonFallback()
    {
        string result = TerminalHtmlLocalizer.Localize(SampleHtml, key =>
            string.Equals(key, TerminalHtmlLocalizer.KeySessionEnded, StringComparison.Ordinal)
                ? "   "
                : LocalizeFrench(key));

        result.Should().Contain(JsonSerializer.Serialize(TerminalHtmlLocalizer.FallbackSessionEnded));
        result.Should().Contain("Initialisation du terminal\u2026");
        result.Should().Contain("Rechercher...");
    }

    [Fact]
    public void Localize_WithHtmlInjectionAttempts_EncodesBodyAndAttributeValues()
    {
        string injection = "<script>alert(1)</script>";

        string result = TerminalHtmlLocalizer.Localize(SampleHtml, key =>
            string.Equals(key, TerminalHtmlLocalizer.KeySessionEnded, StringComparison.Ordinal)
                ? LocalizeFrench(key)
                : injection);

        result.Should().NotContain("<script>");
        result.Should().Contain("&lt;script&gt;");
        CountOccurrences(result, "&lt;script&gt;").Should().Be(2);
    }

    [Fact]
    public void Localize_WithJsInjectionAttempt_SerializesCompleteJsLiteral()
    {
        string injection = "'; alert(1); //";
        string prefix = "'\\r\\n\\x1b[90m' + ";
        string suffix = " + '\\x1b[0m\\r\\n'";

        string result = TerminalHtmlLocalizer.Localize(SampleHtml, key =>
            string.Equals(key, TerminalHtmlLocalizer.KeySessionEnded, StringComparison.Ordinal)
                ? injection
                : LocalizeFrench(key));

        result.Should().Contain(JsonSerializer.Serialize(injection));
        CountOccurrences(result, prefix).Should().Be(1);
        CountOccurrences(result, suffix).Should().Be(1);
    }

    [Fact]
    public void Localize_WhenMarkersAreAbsent_ReturnsInputUnchanged()
    {
        string html = "<html><body>no markers here</body></html>";

        string result = TerminalHtmlLocalizer.Localize(html, LocalizeFrench);

        result.Should().Be(html);
    }

    [Fact]
    public void Localize_WithNullArguments_ThrowsArgumentNullException()
    {
        Action nullHtml = () => TerminalHtmlLocalizer.Localize(null!, _ => "x");
        Action nullLocalize = () => TerminalHtmlLocalizer.Localize("hi", null!);

        nullHtml.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("html");
        nullLocalize.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("localize");
    }

    [Fact]
    public void Localize_WithUnicodeLoadingLabel_UsesHtmlEncodedValue()
    {
        string unicodeLoadingLabel = "D\u00e9marrage du terminal\u2026";

        string result = TerminalHtmlLocalizer.Localize(SampleHtml, key =>
            string.Equals(key, TerminalHtmlLocalizer.KeyLoadingLabel, StringComparison.Ordinal)
                ? unicodeLoadingLabel
                : LocalizeFrench(key));

        result.Should().Contain(WebUtility.HtmlEncode(unicodeLoadingLabel));
    }

    private static string LocalizeFrench(string key)
    {
        return key switch
        {
            TerminalHtmlLocalizer.KeyLoadingLabel => "Initialisation du terminal\u2026",
            TerminalHtmlLocalizer.KeySearchPlaceholder => "Rechercher...",
            TerminalHtmlLocalizer.KeySessionEnded => "--- Session termin\u00e9e ---",
            _ => key
        };
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;
        int foundIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal);

        while (foundIndex >= 0)
        {
            count++;
            startIndex = foundIndex + value.Length;
            foundIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal);
        }

        return count;
    }
}
