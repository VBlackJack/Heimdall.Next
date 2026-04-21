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

using System.Text;
using System.Text.RegularExpressions;

namespace Heimdall.Core.Matching;

public enum DiffStatus
{
    Success,
    InputTooLarge,
}

public enum DiffLineKind
{
    Unchanged,
    Added,
    Removed,
}

public readonly record struct DiffOptions(
    bool IgnoreWhitespace = false,
    bool IgnoreCase = false,
    int? MaxLineCount = null);

public readonly record struct DiffLine(DiffLineKind Kind, string Text);

public readonly record struct TextDiffResult(
    DiffStatus Status,
    IReadOnlyList<DiffLine> Lines,
    int AddedCount,
    int RemovedCount,
    int UnchangedCount);

public readonly record struct WordSegment(string Text, bool IsChanged);

public readonly record struct WordDiffResult(
    IReadOnlyList<WordSegment> OldSegments,
    IReadOnlyList<WordSegment> NewSegments);

public static class DiffEngine
{
    private static readonly Regex CollapseWhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static int DefaultMaxLineCount { get; } = 10000;

    public static TextDiffResult Diff(string? original, string? modified, DiffOptions options)
    {
        var originalLines = SplitLines(original ?? string.Empty);
        var modifiedLines = SplitLines(modified ?? string.Empty);
        var effectiveMaxLineCount = options.MaxLineCount ?? DefaultMaxLineCount;
        if (originalLines.Length > effectiveMaxLineCount || modifiedLines.Length > effectiveMaxLineCount)
        {
            return new TextDiffResult(DiffStatus.InputTooLarge, [], 0, 0, 0);
        }

        var normalizedOriginal = NormalizeLines(originalLines, options.IgnoreWhitespace, options.IgnoreCase);
        var normalizedModified = NormalizeLines(modifiedLines, options.IgnoreWhitespace, options.IgnoreCase);
        var lcs = BuildLcsMatrix(normalizedOriginal, normalizedModified);
        var lines = BacktrackDiff(originalLines, modifiedLines, normalizedOriginal, normalizedModified, lcs);

        var addedCount = 0;
        var removedCount = 0;
        var unchangedCount = 0;
        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case DiffLineKind.Unchanged:
                    unchangedCount++;
                    break;
                case DiffLineKind.Added:
                    addedCount++;
                    break;
                case DiffLineKind.Removed:
                    removedCount++;
                    break;
            }
        }

        return new TextDiffResult(DiffStatus.Success, lines, addedCount, removedCount, unchangedCount);
    }

    public static WordDiffResult WordDiff(string? oldLine, string? newLine)
    {
        var oldTokens = TokenizeLine(oldLine ?? string.Empty);
        var newTokens = TokenizeLine(newLine ?? string.Empty);
        var (oldInLcs, newInLcs) = ComputeWordInLcs(oldTokens, newTokens);

        return new WordDiffResult(
            MergeSegments(oldTokens, oldInLcs),
            MergeSegments(newTokens, newInLcs));
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static string NormalizeLine(string line, bool ignoreWhitespace, bool ignoreCase)
    {
        var result = line;
        if (ignoreWhitespace)
        {
            result = CollapseWhitespaceRegex.Replace(result.Trim(), " ");
        }

        if (ignoreCase)
        {
            result = result.ToLowerInvariant();
        }

        return result;
    }

    private static string[] NormalizeLines(IReadOnlyList<string> lines, bool ignoreWhitespace, bool ignoreCase)
    {
        var normalized = new string[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            normalized[i] = NormalizeLine(lines[i], ignoreWhitespace, ignoreCase);
        }

        return normalized;
    }

    private static int[,] BuildLcsMatrix(IReadOnlyList<string> original, IReadOnlyList<string> modified)
    {
        var lcs = new int[original.Count + 1, modified.Count + 1];
        for (var i = 1; i <= original.Count; i++)
        {
            for (var j = 1; j <= modified.Count; j++)
            {
                if (string.Equals(original[i - 1], modified[j - 1], StringComparison.Ordinal))
                {
                    lcs[i, j] = lcs[i - 1, j - 1] + 1;
                }
                else
                {
                    lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
                }
            }
        }

        return lcs;
    }

    private static IReadOnlyList<DiffLine> BacktrackDiff(
        IReadOnlyList<string> original,
        IReadOnlyList<string> modified,
        IReadOnlyList<string> normalizedOriginal,
        IReadOnlyList<string> normalizedModified,
        int[,] lcs)
    {
        var result = new List<DiffLine>();
        var x = original.Count;
        var y = modified.Count;

        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 &&
                string.Equals(normalizedOriginal[x - 1], normalizedModified[y - 1], StringComparison.Ordinal))
            {
                result.Add(new DiffLine(DiffLineKind.Unchanged, original[x - 1]));
                x--;
                y--;
            }
            else if (y > 0 && (x == 0 || lcs[x, y - 1] >= lcs[x - 1, y]))
            {
                result.Add(new DiffLine(DiffLineKind.Added, modified[y - 1]));
                y--;
            }
            else
            {
                result.Add(new DiffLine(DiffLineKind.Removed, original[x - 1]));
                x--;
            }
        }

        result.Reverse();
        return result;
    }

    private static List<string> TokenizeLine(string line)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < line.Length)
        {
            var start = index;
            var isWhitespace = char.IsWhiteSpace(line[index]);
            while (index < line.Length && char.IsWhiteSpace(line[index]) == isWhitespace)
            {
                index++;
            }

            tokens.Add(line[start..index]);
        }

        return tokens;
    }

    private static (bool[] inLcsOld, bool[] inLcsNew) ComputeWordInLcs(List<string> oldTokens, List<string> newTokens)
    {
        var dp = new int[oldTokens.Count + 1, newTokens.Count + 1];
        for (var i = 1; i <= oldTokens.Count; i++)
        {
            for (var j = 1; j <= newTokens.Count; j++)
            {
                if (string.Equals(oldTokens[i - 1], newTokens[j - 1], StringComparison.Ordinal))
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }
        }

        var oldInLcs = new bool[oldTokens.Count];
        var newInLcs = new bool[newTokens.Count];
        var x = oldTokens.Count;
        var y = newTokens.Count;
        while (x > 0 && y > 0)
        {
            if (string.Equals(oldTokens[x - 1], newTokens[y - 1], StringComparison.Ordinal))
            {
                oldInLcs[x - 1] = true;
                newInLcs[y - 1] = true;
                x--;
                y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        return (oldInLcs, newInLcs);
    }

    private static IReadOnlyList<WordSegment> MergeSegments(List<string> tokens, bool[] inLcs)
    {
        if (tokens.Count == 0)
        {
            return [];
        }

        var segments = new List<WordSegment>();
        var builder = new StringBuilder(tokens[0]);
        var currentChanged = !inLcs[0];

        for (var i = 1; i < tokens.Count; i++)
        {
            var isChanged = !inLcs[i];
            if (isChanged != currentChanged)
            {
                segments.Add(new WordSegment(builder.ToString(), currentChanged));
                builder.Clear();
                currentChanged = isChanged;
            }

            builder.Append(tokens[i]);
        }

        segments.Add(new WordSegment(builder.ToString(), currentChanged));
        return segments;
    }
}
