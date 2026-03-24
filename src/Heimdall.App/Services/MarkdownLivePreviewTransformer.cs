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

using System.Text.RegularExpressions;
using System.Windows;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Heimdall.App.Services;

/// <summary>
/// AvalonEdit line transformer that applies Obsidian-style live preview formatting.
/// Works alongside the XSHD syntax highlighting: XSHD handles colors, this transformer
/// handles font size scaling (headers) and text decorations (strikethrough).
/// </summary>
public sealed partial class MarkdownLivePreviewTransformer : DocumentColorizingTransformer
{
    private const double FontSizeH1 = 24;
    private const double FontSizeH2 = 20;
    private const double FontSizeH3 = 17;
    private const double FontSizeH4 = 15;
    private const double SyntaxCharOpacity = 0.35;

    [GeneratedRegex(@"~~(.+?)~~")]
    private static partial Regex StrikethroughPattern();

    [GeneratedRegex(@"\[\[(.+?)\]\]")]
    private static partial Regex NoteLinkPattern();

    private readonly System.Windows.Media.Brush _dimBrush;

    public MarkdownLivePreviewTransformer()
    {
        var dimColor = System.Windows.Media.Color.FromArgb(
            (byte)(255 * SyntaxCharOpacity), 180, 180, 180);
        _dimBrush = new System.Windows.Media.SolidColorBrush(dimColor);
        _dimBrush.Freeze();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0)
        {
            return;
        }

        var lineText = CurrentContext.Document.GetText(line);
        var offset = line.Offset;

        if (TryColorizeHeading(lineText, offset, line.EndOffset))
        {
            return;
        }

        ColorizeStrikethrough(lineText, offset);
        ColorizeNoteLinkUnderline(lineText, offset);
    }

    private bool TryColorizeHeading(string lineText, int lineOffset, int lineEndOffset)
    {
        var idx = 0;
        while (idx < lineText.Length && lineText[idx] == ' ') idx++;
        if (idx >= lineText.Length || lineText[idx] != '#') return false;

        var level = 0;
        while (idx < lineText.Length && lineText[idx] == '#') { level++; idx++; }
        if (level == 0 || level > 6 || idx >= lineText.Length || lineText[idx] != ' ') return false;

        var fontSize = level switch
        {
            1 => FontSizeH1,
            2 => FontSizeH2,
            3 => FontSizeH3,
            _ => FontSizeH4
        };

        // Dim the # markers
        var markerEnd = lineOffset + idx;
        ChangeLinePart(lineOffset, markerEnd, element =>
        {
            element.TextRunProperties.SetForegroundBrush(_dimBrush);
            element.TextRunProperties.SetFontRenderingEmSize(fontSize);
        });

        // Scale the heading text
        if (markerEnd < lineEndOffset)
        {
            ChangeLinePart(markerEnd, lineEndOffset, element =>
            {
                element.TextRunProperties.SetFontRenderingEmSize(fontSize);
            });
        }

        return true;
    }

    private void ColorizeStrikethrough(string lineText, int lineOffset)
    {
        foreach (var match in StrikethroughPattern().Matches(lineText).Cast<Match>())
        {
            var matchStart = lineOffset + match.Index;
            var matchEnd = matchStart + match.Length;

            // Dim the ~~ markers
            ChangeLinePart(matchStart, matchStart + 2, element =>
                element.TextRunProperties.SetForegroundBrush(_dimBrush));
            ChangeLinePart(matchEnd - 2, matchEnd, element =>
                element.TextRunProperties.SetForegroundBrush(_dimBrush));

            // Apply strikethrough on the content
            if (matchStart + 2 < matchEnd - 2)
            {
                ChangeLinePart(matchStart + 2, matchEnd - 2, element =>
                    element.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough));
            }
        }
    }

    private void ColorizeNoteLinkUnderline(string lineText, int lineOffset)
    {
        foreach (var match in NoteLinkPattern().Matches(lineText).Cast<Match>())
        {
            var matchStart = lineOffset + match.Index;
            var matchEnd = matchStart + match.Length;

            // Dim the [[ and ]] markers
            ChangeLinePart(matchStart, matchStart + 2, element =>
                element.TextRunProperties.SetForegroundBrush(_dimBrush));
            ChangeLinePart(matchEnd - 2, matchEnd, element =>
                element.TextRunProperties.SetForegroundBrush(_dimBrush));

            // Underline the content
            if (matchStart + 2 < matchEnd - 2)
            {
                ChangeLinePart(matchStart + 2, matchEnd - 2, element =>
                    element.TextRunProperties.SetTextDecorations(TextDecorations.Underline));
            }
        }
    }
}
