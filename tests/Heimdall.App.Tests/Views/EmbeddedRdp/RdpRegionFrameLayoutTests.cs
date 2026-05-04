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

using System.Windows;
using Heimdall.App.Views.EmbeddedRdp;

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

public sealed class RdpRegionFrameLayoutTests
{
    [Fact]
    public void FromPaneAndContent_WhenPaneIsWiderThanContent_SizesFrameAndMarksLetterboxActive()
    {
        var layout = RdpRegionFrameLayout.FromPaneAndContent(1600, 900, 1024, 768);

        AssertClose(200, layout.FrameMargin.Left);
        AssertClose(0, layout.FrameMargin.Top);
        AssertClose(1200, layout.FrameWidth);
        AssertClose(900, layout.FrameHeight);
        Assert.True(layout.IsLetterboxActive);
        Assert.Equal(System.Windows.HorizontalAlignment.Left, layout.FrameHorizontalAlignment);
        Assert.Equal(System.Windows.VerticalAlignment.Top, layout.FrameVerticalAlignment);
        // RDP-LIVE-24: when letterbox is active, the WindowsFormsHost is pinned
        // to the frame size so the Win32 HWND does not bleed past it.
        Assert.Equal(System.Windows.HorizontalAlignment.Left, layout.HostHorizontalAlignment);
        Assert.Equal(System.Windows.VerticalAlignment.Top, layout.HostVerticalAlignment);
        Assert.Equal(new Thickness(0), layout.HostMargin);
        AssertClose(1200, layout.HostWidth);
        AssertClose(900, layout.HostHeight);
    }

    [Fact]
    public void FromPaneAndContent_WhenLetterboxInactive_HostStaysStretched()
    {
        var layout = RdpRegionFrameLayout.FromPaneAndContent(1600, 900, 1920, 1080);

        Assert.False(layout.IsLetterboxActive);
        Assert.Equal(System.Windows.HorizontalAlignment.Left, layout.HostHorizontalAlignment);
        Assert.True(double.IsNaN(layout.HostWidth));
        Assert.True(double.IsNaN(layout.HostHeight));
    }

    [Fact]
    public void FromPaneAndContent_WhenAspectRatioMatches_FillsFrameAndMarksLetterboxInactive()
    {
        var layout = RdpRegionFrameLayout.FromPaneAndContent(1600, 900, 1920, 1080);

        Assert.Equal(new Thickness(0), layout.FrameMargin);
        AssertClose(1600, layout.FrameWidth);
        AssertClose(900, layout.FrameHeight);
        Assert.False(layout.IsLetterboxActive);
    }

    [Fact]
    public void HasLetterboxBands_WhenFrameIsSmallerThanPane_ReturnsTrue()
    {
        var active = RdpRegionFrameLayout.HasLetterboxBands(
            (0, 75, 800, 450),
            800,
            600);

        Assert.True(active);
    }

    private static void AssertClose(double expected, double actual)
        => Assert.InRange(actual, expected - 0.0001, expected + 0.0001);
}
