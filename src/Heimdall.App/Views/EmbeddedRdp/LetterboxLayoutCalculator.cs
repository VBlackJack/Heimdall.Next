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

namespace Heimdall.App.Views.EmbeddedRdp;

/// <summary>
/// Calculates the centered child rectangle needed to preserve a fixed remote aspect ratio.
/// </summary>
public static class LetterboxLayoutCalculator
{
    public static (
        double HostX,
        double HostY,
        double HostWidth,
        double HostHeight) Compute(
            double paneWidth,
            double paneHeight,
            double contentWidth,
            double contentHeight)
    {
        if (!IsPositiveFinite(paneWidth)
            || !IsPositiveFinite(paneHeight)
            || !IsPositiveFinite(contentWidth)
            || !IsPositiveFinite(contentHeight))
        {
            Heimdall.Core.Logging.FileLogger.Debug(
                $"LetterboxLayoutCalculator invalid input: pane={paneWidth:0.##}x{paneHeight:0.##} content={contentWidth:0.##}x{contentHeight:0.##}");
            return (0, 0, 0, 0);
        }

        var paneAspect = paneWidth / paneHeight;
        var contentAspect = contentWidth / contentHeight;

        double hostWidth;
        double hostHeight;

        if (paneAspect > contentAspect)
        {
            hostHeight = paneHeight;
            hostWidth = paneHeight * contentAspect;
        }
        else
        {
            hostWidth = paneWidth;
            hostHeight = paneWidth / contentAspect;
        }

        return (
            Math.Max(0, (paneWidth - hostWidth) / 2.0),
            Math.Max(0, (paneHeight - hostHeight) / 2.0),
            Math.Max(0, hostWidth),
            Math.Max(0, hostHeight));
    }

    private static bool IsPositiveFinite(double value)
        => value > 0 && double.IsFinite(value);
}
