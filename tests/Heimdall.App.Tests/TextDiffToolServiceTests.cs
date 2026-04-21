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
using Heimdall.Core.Matching;

namespace Heimdall.App.Tests;

public sealed class TextDiffToolServiceTests
{
    private readonly TextDiffToolService _service = new();

    [Fact]
    public void Diff_DelegatesToEngine_ReturnsSameResult()
    {
        var options = new DiffOptions(IgnoreWhitespace: true, IgnoreCase: true);

        var actual = _service.Diff("a", "A", options);
        var expected = DiffEngine.Diff("a", "A", options);

        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.AddedCount, actual.AddedCount);
        Assert.Equal(expected.RemovedCount, actual.RemovedCount);
        Assert.Equal(expected.UnchangedCount, actual.UnchangedCount);
        Assert.Equal(expected.Lines.ToArray(), actual.Lines.ToArray());
    }

    [Fact]
    public void WordDiff_DelegatesToEngine_ReturnsSameResult()
    {
        var actual = _service.WordDiff("alpha beta", "alpha gamma");
        var expected = DiffEngine.WordDiff("alpha beta", "alpha gamma");

        Assert.Equal(expected.OldSegments.ToArray(), actual.OldSegments.ToArray());
        Assert.Equal(expected.NewSegments.ToArray(), actual.NewSegments.ToArray());
    }

    [Fact]
    public void Diff_RespectsCustomMaxLineCount_ViaOptions()
    {
        var result = _service.Diff("a\nb", "a\nb", new DiffOptions(MaxLineCount: 1));

        Assert.Equal(DiffStatus.InputTooLarge, result.Status);
    }

    [Fact]
    public void Diff_DefaultOptionsStruct_UsesEngineDefaults()
    {
        var actual = _service.Diff("a", "a", default);
        var expected = DiffEngine.Diff("a", "a", default);

        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.AddedCount, actual.AddedCount);
        Assert.Equal(expected.RemovedCount, actual.RemovedCount);
        Assert.Equal(expected.UnchangedCount, actual.UnchangedCount);
        Assert.Equal(expected.Lines.ToArray(), actual.Lines.ToArray());
    }
}
