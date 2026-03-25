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

using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Heimdall.App.Services;

public static class MarkdownHighlighting
{
    private const string Xshd = """
        <SyntaxDefinition name="Markdown"
            xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">

          <Color name="Heading"     foreground="#bd93f9" fontWeight="bold" />
          <Color name="Bold"        foreground="#ffb86c" fontWeight="bold" />
          <Color name="Italic"      foreground="#f1fa8c" fontStyle="italic" />
          <Color name="Strikethrough" foreground="#6272a4" />
          <Color name="InlineCode"  foreground="#ff79c6" background="#44475a" />
          <Color name="CodeBlock"   foreground="#f8f8f2" background="#282a36" />
          <Color name="Link"        foreground="#8be9fd" />
          <Color name="NoteLink"    foreground="#50fa7b" fontWeight="bold" />
          <Color name="Image"       foreground="#8be9fd" />
          <Color name="Blockquote"  foreground="#6272a4" fontStyle="italic" />
          <Color name="ListMarker"  foreground="#f1fa8c" />
          <Color name="HRule"       foreground="#44475a" />
          <Color name="MetaTag"     foreground="#8be9fd" />

          <RuleSet>
            <Span color="CodeBlock" multiline="true">
              <Begin>```</Begin>
              <End>```</End>
            </Span>

            <Span color="InlineCode">
              <Begin>`</Begin>
              <End>`</End>
            </Span>

            <Span color="Heading">
              <Begin>^\#{1,6}\s</Begin>
            </Span>

            <Span color="Blockquote">
              <Begin>^&gt;\s</Begin>
            </Span>

            <Rule color="NoteLink">\[\[.+?\]\]</Rule>
            <Rule color="Image">!\[.*?\]\(.+?\)</Rule>
            <Rule color="Link">\[.*?\]\(.+?\)</Rule>
            <Rule color="Bold">\*\*.+?\*\*</Rule>
            <Rule color="Bold">__.+?__</Rule>
            <Rule color="Strikethrough">~~.+?~~</Rule>
            <Rule color="ListMarker">[-*+]\s\[[ xX]\]\s</Rule>
            <Rule color="ListMarker">[-*+]\s</Rule>
            <Rule color="ListMarker">\d+\.\s</Rule>
            <Rule color="HRule">-{3,}</Rule>
            <Rule color="HRule">\*{3,}</Rule>
            <Rule color="HRule">_{3,}</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    public static IHighlightingDefinition Create()
    {
        using var reader = new XmlTextReader(new System.IO.StringReader(Xshd));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
