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

using System.IO;
using System.Text.RegularExpressions;

namespace Heimdall.App.Tests;

/// <summary>
/// Prevents user-visible hardcoded defaults/placeholders from creeping back
/// into tool XAML input controls.
/// </summary>
public class ToolXamlInputHardcodingTests
{
    private static readonly Regex s_textBoxWithHardcodedText = new(
        @"<TextBox\b[^>]*\bText=""(?!\s*"")[^""]+""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex s_textBoxWithHardcodedTag = new(
        @"<TextBox\b[^>]*\bTag=""(?!\s*"")[^""]+""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void ToolXaml_TextBoxes_DoNotUseHardcodedTextOrTagValues()
    {
        var toolsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Heimdall.App", "Views", "Tools"));

        var violations = new List<string>();

        foreach (var xamlPath in Directory.EnumerateFiles(toolsDir, "*.xaml", SearchOption.TopDirectoryOnly))
        {
            var content = File.ReadAllText(xamlPath);

            foreach (Match match in s_textBoxWithHardcodedText.Matches(content))
            {
                violations.Add($"{Path.GetFileName(xamlPath)}:{GetLineNumber(content, match.Index)} -> {match.Value}");
            }

            foreach (Match match in s_textBoxWithHardcodedTag.Matches(content))
            {
                violations.Add($"{Path.GetFileName(xamlPath)}:{GetLineNumber(content, match.Index)} -> {match.Value}");
            }
        }

        Assert.True(violations.Count == 0,
            "Hardcoded TextBox Text/Tag values found in tool XAML:\n" + string.Join("\n", violations));
    }

    private static int GetLineNumber(string content, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (content[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
