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

using Heimdall.App.Views.EmbeddedRdp;

namespace Heimdall.App.Tests;

public sealed class LetterboxLayoutCalculatorTests
{
    [Fact]
    public void Compute_WhenPaneIsWiderThanContent_CentersFullHeightHost()
    {
        var rect = LetterboxLayoutCalculator.Compute(1600, 900, 1024, 768);

        AssertClose(200, rect.HostX);
        AssertClose(0, rect.HostY);
        AssertClose(1200, rect.HostWidth);
        AssertClose(900, rect.HostHeight);
    }

    [Fact]
    public void Compute_WhenPaneIsTallerThanContent_CentersFullWidthHost()
    {
        var rect = LetterboxLayoutCalculator.Compute(1000, 1000, 1920, 1080);

        AssertClose(0, rect.HostX);
        AssertClose(218.75, rect.HostY);
        AssertClose(1000, rect.HostWidth);
        AssertClose(562.5, rect.HostHeight);
    }

    [Fact]
    public void Compute_WhenAspectRatioMatches_FillsPane()
    {
        var rect = LetterboxLayoutCalculator.Compute(1600, 900, 1920, 1080);

        AssertClose(0, rect.HostX);
        AssertClose(0, rect.HostY);
        AssertClose(1600, rect.HostWidth);
        AssertClose(900, rect.HostHeight);
    }

    [Fact]
    public void Compute_WhenContentIsLargerThanPane_ScalesDownToFit()
    {
        var rect = LetterboxLayoutCalculator.Compute(800, 600, 1920, 1080);

        AssertClose(0, rect.HostX);
        AssertClose(75, rect.HostY);
        AssertClose(800, rect.HostWidth);
        AssertClose(450, rect.HostHeight);
    }

    [Theory]
    [InlineData(0, 600, 1920, 1080)]
    [InlineData(800, 0, 1920, 1080)]
    [InlineData(800, 600, 0, 1080)]
    [InlineData(800, 600, 1920, 0)]
    [InlineData(-800, 600, 1920, 1080)]
    [InlineData(800, -600, 1920, 1080)]
    [InlineData(800, 600, -1920, 1080)]
    [InlineData(800, 600, 1920, -1080)]
    public void Compute_WithZeroOrNegativeInputs_ReturnsSafeEmptyRect(
        double paneWidth,
        double paneHeight,
        double contentWidth,
        double contentHeight)
    {
        var rect = LetterboxLayoutCalculator.Compute(
            paneWidth,
            paneHeight,
            contentWidth,
            contentHeight);

        Assert.Equal(0, rect.HostX);
        Assert.Equal(0, rect.HostY);
        Assert.Equal(0, rect.HostWidth);
        Assert.Equal(0, rect.HostHeight);
    }

    [Fact]
    public void Compute_WithWideContentAspectRatio_AddsTopAndBottomBars()
    {
        var rect = LetterboxLayoutCalculator.Compute(1000, 1000, 4000, 1000);

        AssertClose(0, rect.HostX);
        AssertClose(375, rect.HostY);
        AssertClose(1000, rect.HostWidth);
        AssertClose(250, rect.HostHeight);
    }

    [Fact]
    public void Compute_WithTallContentAspectRatio_AddsSideBars()
    {
        var rect = LetterboxLayoutCalculator.Compute(1000, 1000, 1000, 4000);

        AssertClose(375, rect.HostX);
        AssertClose(0, rect.HostY);
        AssertClose(250, rect.HostWidth);
        AssertClose(1000, rect.HostHeight);
    }

    private static void AssertClose(double expected, double actual)
        => Assert.InRange(actual, expected - 0.0001, expected + 0.0001);
}
