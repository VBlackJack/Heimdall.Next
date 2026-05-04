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

namespace Heimdall.App.Views.EmbeddedRdp;

internal readonly record struct RdpRegionFrameLayout(
    Thickness FrameMargin,
    double FrameWidth,
    double FrameHeight,
    bool IsLetterboxActive)
{
    private const double LayoutTolerance = 0.5;

    public System.Windows.HorizontalAlignment FrameHorizontalAlignment => System.Windows.HorizontalAlignment.Left;

    public System.Windows.VerticalAlignment FrameVerticalAlignment => System.Windows.VerticalAlignment.Top;

    public System.Windows.HorizontalAlignment HostHorizontalAlignment => System.Windows.HorizontalAlignment.Left;

    public System.Windows.VerticalAlignment HostVerticalAlignment => System.Windows.VerticalAlignment.Top;

    public Thickness HostMargin => new(0);

    /// <summary>
    /// Pin the WindowsFormsHost width/height to the frame size so the
    /// hosted Win32 HWND is allocated exactly the RDP region rectangle.
    /// Without this, the HostVisual can extend past the frame and the Win32
    /// gray system background bleeds through the letterbox bands instead of
    /// the SurfaceBrush from the parent SurfaceContainer (RDP-LIVE-24).
    /// </summary>
    public double HostWidth => IsLetterboxActive ? FrameWidth : double.NaN;

    public double HostHeight => IsLetterboxActive ? FrameHeight : double.NaN;

    public static RdpRegionFrameLayout FromPaneAndContent(
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

        return new RdpRegionFrameLayout(
            new Thickness(rect.HostX, rect.HostY, 0, 0),
            rect.HostWidth,
            rect.HostHeight,
            HasLetterboxBands(rect, paneWidth, paneHeight));
    }

    public static bool HasLetterboxBands(
        (double HostX, double HostY, double HostWidth, double HostHeight) rect,
        double paneWidth,
        double paneHeight)
    {
        return rect.HostX > LayoutTolerance
            || rect.HostY > LayoutTolerance
            || rect.HostWidth < paneWidth - LayoutTolerance
            || rect.HostHeight < paneHeight - LayoutTolerance;
    }
}
