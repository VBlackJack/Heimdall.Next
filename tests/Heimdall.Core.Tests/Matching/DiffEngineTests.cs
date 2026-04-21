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

using Heimdall.Core.Matching;

namespace Heimdall.Core.Tests;

public sealed class DiffEngineTests
{
    [Fact]
    public void Diff_BothEmpty_ReturnsSuccessAllZeroCounts()
    {
        var result = DiffEngine.Diff(string.Empty, string.Empty, new DiffOptions());

        Assert.Equal(DiffStatus.Success, result.Status);
        Assert.Empty(result.Lines);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(0, result.UnchangedCount);
    }

    [Fact]
    public void Diff_Identical_ReturnsAllUnchanged()
    {
        var result = DiffEngine.Diff("a\nb", "a\nb", new DiffOptions());

        Assert.Equal(2, result.UnchangedCount);
        Assert.All(result.Lines, line => Assert.Equal(DiffLineKind.Unchanged, line.Kind));
    }

    [Fact]
    public void Diff_OnlyAdditions_ReturnsAllAdded()
    {
        var result = DiffEngine.Diff(string.Empty, "a\nb", new DiffOptions());

        Assert.Equal(2, result.AddedCount);
        Assert.All(result.Lines, line => Assert.Equal(DiffLineKind.Added, line.Kind));
    }

    [Fact]
    public void Diff_OnlyRemovals_ReturnsAllRemoved()
    {
        var result = DiffEngine.Diff("a\nb", string.Empty, new DiffOptions());

        Assert.Equal(2, result.RemovedCount);
        Assert.All(result.Lines, line => Assert.Equal(DiffLineKind.Removed, line.Kind));
    }

    [Fact]
    public void Diff_ModifiedMiddle_ReturnsCorrectMix()
    {
        var result = DiffEngine.Diff("a\nb\nc", "a\nx\nc", new DiffOptions());

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(2, result.UnchangedCount);
        Assert.Equal(
            [DiffLineKind.Unchanged, DiffLineKind.Removed, DiffLineKind.Added, DiffLineKind.Unchanged],
            result.Lines.Select(line => line.Kind).ToArray());
    }

    [Theory]
    [InlineData("  hello\tworld  ", "hello world", true, false)]
    [InlineData("HELLO", "hello", false, true)]
    [InlineData("  HELLO\tWORLD ", "hello world", true, true)]
    public void Diff_NormalizationOptions_TreatLinesAsUnchanged(string original, string modified, bool ignoreWhitespace, bool ignoreCase)
    {
        var result = DiffEngine.Diff(original, modified, new DiffOptions(ignoreWhitespace, ignoreCase));

        Assert.Single(result.Lines);
        Assert.Equal(DiffLineKind.Unchanged, result.Lines[0].Kind);
    }

    [Fact]
    public void Diff_CrlfLineEndings_NormalizedToLf()
    {
        var result = DiffEngine.Diff("a\r\nb\r\n", "a\nb\n", new DiffOptions());

        Assert.Equal(3, result.UnchangedCount);
    }

    [Fact]
    public void Diff_TrailingNewline_ProducesEmptyLastLine()
    {
        var result = DiffEngine.Diff("a\n", "a", new DiffOptions());

        Assert.Equal(string.Empty, result.Lines.Last().Text);
        Assert.Equal(DiffLineKind.Removed, result.Lines.Last().Kind);
    }

    [Fact]
    public void Diff_EqualCostBacktrack_PrefersAdded()
    {
        var result = DiffEngine.Diff("a", "b", new DiffOptions());

        Assert.Equal([DiffLineKind.Removed, DiffLineKind.Added], result.Lines.Select(line => line.Kind).ToArray());
    }

    [Fact]
    public void Diff_OriginalExceedsDefaultMax_ReturnsInputTooLarge()
    {
        var lines = string.Join('\n', Enumerable.Repeat("x", DiffEngine.DefaultMaxLineCount + 1));

        var result = DiffEngine.Diff(lines, string.Empty, new DiffOptions());

        Assert.Equal(DiffStatus.InputTooLarge, result.Status);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Diff_ModifiedExceedsDefaultMax_ReturnsInputTooLarge()
    {
        var lines = string.Join('\n', Enumerable.Repeat("x", DiffEngine.DefaultMaxLineCount + 1));

        var result = DiffEngine.Diff(string.Empty, lines, new DiffOptions());

        Assert.Equal(DiffStatus.InputTooLarge, result.Status);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Diff_ExactlyAtDefaultMax_ReturnsSuccess()
    {
        var lines = string.Join('\n', Enumerable.Repeat("x", DiffEngine.DefaultMaxLineCount));

        var result = DiffEngine.Diff(lines, lines, new DiffOptions());

        Assert.Equal(DiffStatus.Success, result.Status);
        Assert.Equal(DiffEngine.DefaultMaxLineCount, result.UnchangedCount);
    }

    [Fact]
    public void Diff_CustomMaxOverride_Honored()
    {
        var result = DiffEngine.Diff("a\nb", "a\nb", new DiffOptions(MaxLineCount: 1));

        Assert.Equal(DiffStatus.InputTooLarge, result.Status);
    }

    [Theory]
    [InlineData(null, "a", DiffLineKind.Added)]
    [InlineData("a", null, DiffLineKind.Removed)]
    public void Diff_NullInputs_TreatedAsEmpty(string? original, string? modified, DiffLineKind expectedKind)
    {
        var result = DiffEngine.Diff(original, modified, new DiffOptions());

        Assert.Single(result.Lines);
        Assert.Equal(expectedKind, result.Lines[0].Kind);
    }

    [Theory]
    [InlineData("", "a\nb", 2, 0)]
    [InlineData("a\nb", "", 0, 2)]
    public void Diff_EmptyOneSide_ReturnsExpectedCounts(string original, string modified, int added, int removed)
    {
        var result = DiffEngine.Diff(original, modified, new DiffOptions());

        Assert.Equal(added, result.AddedCount);
        Assert.Equal(removed, result.RemovedCount);
    }

    [Fact]
    public void Diff_CountsMatchKinds()
    {
        var result = DiffEngine.Diff("a\nb\nc", "a\nc\nd", new DiffOptions());

        Assert.Equal(result.AddedCount, result.Lines.Count(line => line.Kind == DiffLineKind.Added));
        Assert.Equal(result.RemovedCount, result.Lines.Count(line => line.Kind == DiffLineKind.Removed));
        Assert.Equal(result.UnchangedCount, result.Lines.Count(line => line.Kind == DiffLineKind.Unchanged));
    }

    [Fact]
    public void WordDiff_IdenticalLines_AllSegmentsUnchanged()
    {
        var result = DiffEngine.WordDiff("alpha beta", "alpha beta");

        Assert.All(result.OldSegments, segment => Assert.False(segment.IsChanged));
        Assert.All(result.NewSegments, segment => Assert.False(segment.IsChanged));
    }

    [Fact]
    public void WordDiff_CompletelyDifferent_AllSegmentsChanged()
    {
        var result = DiffEngine.WordDiff("alpha beta", "gamma delta");

        Assert.Collection(
            result.OldSegments,
            segment => Assert.Equal(new WordSegment("alpha", true), segment),
            segment => Assert.Equal(new WordSegment(" ", false), segment),
            segment => Assert.Equal(new WordSegment("beta", true), segment));
        Assert.Collection(
            result.NewSegments,
            segment => Assert.Equal(new WordSegment("gamma", true), segment),
            segment => Assert.Equal(new WordSegment(" ", false), segment),
            segment => Assert.Equal(new WordSegment("delta", true), segment));
    }

    [Fact]
    public void WordDiff_MiddleWordChanged_OuterSegmentsRemainUnchanged()
    {
        var result = DiffEngine.WordDiff("alpha beta gamma", "alpha delta gamma");

        Assert.Collection(
            result.OldSegments,
            segment => Assert.Equal(new WordSegment("alpha ", false), segment),
            segment => Assert.Equal(new WordSegment("beta", true), segment),
            segment => Assert.Equal(new WordSegment(" gamma", false), segment));
        Assert.Collection(
            result.NewSegments,
            segment => Assert.Equal(new WordSegment("alpha ", false), segment),
            segment => Assert.Equal(new WordSegment("delta", true), segment),
            segment => Assert.Equal(new WordSegment(" gamma", false), segment));
    }

    [Fact]
    public void WordDiff_WhitespacePreservedAsTokens()
    {
        var result = DiffEngine.WordDiff("foo  bar", "foo baz");

        Assert.Contains(result.OldSegments, segment => segment.Text.Contains("  ", StringComparison.Ordinal));
    }

    [Fact]
    public void WordDiff_BothEmpty_ReturnsEmptySegments()
    {
        var result = DiffEngine.WordDiff(string.Empty, string.Empty);

        Assert.Empty(result.OldSegments);
        Assert.Empty(result.NewSegments);
    }

    [Fact]
    public void WordDiff_EmptyOldNonEmptyNew_AllNewChanged()
    {
        var result = DiffEngine.WordDiff(string.Empty, "alpha beta");

        Assert.Empty(result.OldSegments);
        Assert.Single(result.NewSegments);
        Assert.True(result.NewSegments[0].IsChanged);
    }

    [Fact]
    public void WordDiff_EmptyNewNonEmptyOld_AllOldChanged()
    {
        var result = DiffEngine.WordDiff("alpha beta", string.Empty);

        Assert.Single(result.OldSegments);
        Assert.True(result.OldSegments[0].IsChanged);
        Assert.Empty(result.NewSegments);
    }

    [Fact]
    public void WordDiff_ConsecutiveChangedTokens_AreMerged()
    {
        var result = DiffEngine.WordDiff("alpha beta gamma", "omega theta gamma");

        Assert.Collection(
            result.OldSegments,
            segment => Assert.Equal(new WordSegment("alpha", true), segment),
            segment => Assert.Equal(new WordSegment(" ", false), segment),
            segment => Assert.Equal(new WordSegment("beta", true), segment),
            segment => Assert.Equal(new WordSegment(" gamma", false), segment));
    }

    [Fact]
    public void WordDiff_MixedTokens_GroupedByFlag()
    {
        var result = DiffEngine.WordDiff("one two three four", "one three four five");

        Assert.Collection(
            result.OldSegments,
            segment => Assert.Equal(new WordSegment("one", false), segment),
            segment => Assert.Equal(new WordSegment(" two", true), segment),
            segment => Assert.Equal(new WordSegment(" three four", false), segment));
        Assert.Collection(
            result.NewSegments,
            segment => Assert.Equal(new WordSegment("one three four", false), segment),
            segment => Assert.Equal(new WordSegment(" five", true), segment));
    }
}
