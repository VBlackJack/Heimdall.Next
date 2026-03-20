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

using System.Runtime.Versioning;
using Heimdall.Core.Models;
using Heimdall.Rdp;

namespace Heimdall.Ssh.Tests;

[SupportedOSPlatform("windows")]
public class AspectRatioManagerTests
{
    // ── Stretch mode ───────────────────────────────────────────────────

    [Fact]
    public void Calculate_Stretch_ReturnsContainerDimensionsUnchanged()
    {
        var (width, height) = AspectRatioManager.Calculate(1920, 1080, AspectRatio.Stretch);

        Assert.Equal(1920, width);
        Assert.Equal(1080, height);
    }

    [Fact]
    public void Calculate_Auto_ReturnsContainerDimensionsUnchanged()
    {
        var (width, height) = AspectRatioManager.Calculate(1920, 1080, AspectRatio.Auto);

        Assert.Equal(1920, width);
        Assert.Equal(1080, height);
    }

    // ── Preserve: letterboxing (16:9 source on 4:3-ish container) ──────

    [Fact]
    public void Calculate_16x9_OnTallerContainer_Letterboxes()
    {
        // Container is 1024x768 (4:3), target is 16:9
        // 16:9 is wider than 4:3, so constrain by width: height = 1024 / (16/9) = 576
        var (width, height) = AspectRatioManager.Calculate(1024, 768, AspectRatio.Ratio16x9);

        Assert.Equal(1024, width);
        Assert.Equal(576, height);
        Assert.True(height < 768, "Height should be less than container (letterboxed)");
    }

    // ── Preserve: pillarboxing (4:3 source on 16:9 container) ──────────

    [Fact]
    public void Calculate_4x3_OnWiderContainer_Pillarboxes()
    {
        // Container is 1920x1080 (16:9), target is 4:3
        // 4:3 is narrower than 16:9, so constrain by height: width = 1080 * (4/3) = 1440
        var (width, height) = AspectRatioManager.Calculate(1920, 1080, AspectRatio.Ratio4x3);

        Assert.Equal(1440, width);
        Assert.Equal(1080, height);
        Assert.True(width < 1920, "Width should be less than container (pillarboxed)");
    }

    // ── Zero dimensions ────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(0, 0)]
    [InlineData(-1, 1080)]
    [InlineData(1920, -1)]
    public void Calculate_ZeroOrNegativeDimensions_ReturnsZero(int containerWidth, int containerHeight)
    {
        var (width, height) = AspectRatioManager.Calculate(containerWidth, containerHeight, AspectRatio.Ratio16x9);

        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    // ── Matching aspect ratio ──────────────────────────────────────────

    [Fact]
    public void Calculate_16x9_OnMatchingContainer_ReturnsExactDimensions()
    {
        // Container is already 16:9
        var (width, height) = AspectRatioManager.Calculate(1920, 1080, AspectRatio.Ratio16x9);

        Assert.Equal(1920, width);
        Assert.Equal(1080, height);
    }

    [Fact]
    public void Calculate_4x3_OnMatchingContainer_ReturnsExactDimensions()
    {
        // Container is already 4:3
        var (width, height) = AspectRatioManager.Calculate(1024, 768, AspectRatio.Ratio4x3);

        Assert.Equal(1024, width);
        Assert.Equal(768, height);
    }

    // ── Even dimension enforcement ─────────────────────────────────────

    [Fact]
    public void Calculate_ResultDimensionsAreAlwaysEven()
    {
        // Use an odd container size to verify even-rounding
        var (width, height) = AspectRatioManager.Calculate(1001, 751, AspectRatio.Ratio16x9);

        Assert.Equal(0, width % 2);
        Assert.Equal(0, height % 2);
    }

    // ── 21:9 ultrawide ─────────────────────────────────────────────────

    [Fact]
    public void Calculate_21x9_OnStandardContainer_Letterboxes()
    {
        // Container is 1920x1080 (16:9), target is 21:9
        // 21:9 is wider than 16:9, so constrain by width: height = 1920 / (21/9) = ~823
        var (width, height) = AspectRatioManager.Calculate(1920, 1080, AspectRatio.Ratio21x9);

        Assert.True(height < 1080, "21:9 on 16:9 container should letterbox");
        Assert.Equal(0, width % 2);
        Assert.Equal(0, height % 2);
    }
}
