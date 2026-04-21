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

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Heimdall.Core.Codecs;

public enum TextCaseStyle
{
    Camel,
    Pascal,
    Snake,
    Kebab,
    Upper,
    Lower,
    Title,
    Constant,
}

public static class TextCaseCodec
{
    public static string Convert(string input, TextCaseStyle style)
    {
        ArgumentNullException.ThrowIfNull(input);

        return style switch
        {
            TextCaseStyle.Camel => ToCamelCase(input),
            TextCaseStyle.Pascal => ToPascalCase(input),
            TextCaseStyle.Snake => ToSnakeCase(input),
            TextCaseStyle.Kebab => ToKebabCase(input),
            TextCaseStyle.Upper => input.ToUpperInvariant(),
            TextCaseStyle.Lower => input.ToLowerInvariant(),
            TextCaseStyle.Title => ToTitleCase(input),
            TextCaseStyle.Constant => ToConstantCase(input),
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };
    }

    private static string[] SplitWords(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return [];
        }

        var withBoundaries = Regex.Replace(input, @"(?<=[a-z])(?=[A-Z])", " ");
        withBoundaries = Regex.Replace(withBoundaries, @"(?<=[A-Z])(?=[A-Z][a-z])", " ");
        var words = Regex.Split(withBoundaries, @"[\s_\-]+");

        return words.Where(w => w.Length > 0).ToArray();
    }

    private static string Capitalize(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return word;
        }

        return char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..].ToLowerInvariant();
    }

    private static string ToCamelCase(string input)
    {
        var words = SplitWords(input);
        if (words.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append(words[0].ToLowerInvariant());
        for (var i = 1; i < words.Length; i++)
        {
            sb.Append(Capitalize(words[i]));
        }

        return sb.ToString();
    }

    private static string ToPascalCase(string input)
    {
        var words = SplitWords(input);
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            sb.Append(Capitalize(word));
        }

        return sb.ToString();
    }

    private static string ToSnakeCase(string input)
    {
        var words = SplitWords(input);
        return string.Join("_", words.Select(w => w.ToLowerInvariant()));
    }

    private static string ToKebabCase(string input)
    {
        var words = SplitWords(input);
        return string.Join("-", words.Select(w => w.ToLowerInvariant()));
    }

    private static string ToTitleCase(string input)
    {
        var words = SplitWords(input);
        return string.Join(" ", words.Select(Capitalize));
    }

    private static string ToConstantCase(string input)
    {
        var words = SplitWords(input);
        return string.Join("_", words.Select(w => w.ToUpperInvariant()));
    }
}
