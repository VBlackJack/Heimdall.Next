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

namespace Heimdall.Rdp;

/// <summary>
/// Pure helpers for Remote Desktop display sizing and DPI scale mapping.
/// </summary>
public static class RdpDisplayHelper
{
    public static readonly uint[] DesktopScaleFactors = [100, 125, 150, 175, 200, 250, 300, 400, 500];

    public static readonly uint[] DeviceScaleFactors = [100, 140, 180];

    /// <summary>
    /// Snaps a positive dimension down to the nearest multiple.
    /// </summary>
    public static int SnapToMultipleOf(int value, int multiple)
    {
        if (multiple <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiple), "Multiple must be greater than zero.");
        }

        if (value <= 0)
        {
            return 0;
        }

        return value - (value % multiple);
    }

    /// <summary>
    /// Maps WPF DPI scale (1.0 = 100%) or raw DPI (96 = 100%) to an RDP desktop scale factor.
    /// </summary>
    public static uint MapDpiToDesktopScaleFactor(double wpfDpi)
        => FindNearest(ToScalePercent(wpfDpi), DesktopScaleFactors);

    /// <summary>
    /// Maps WPF DPI scale (1.0 = 100%) or raw DPI (96 = 100%) to an RDP device scale factor.
    /// </summary>
    public static uint MapDpiToDeviceScaleFactor(double wpfDpi)
        => FindNearest(ToScalePercent(wpfDpi), DeviceScaleFactors);

    /// <summary>
    /// Converts a pixel span at the supplied raw DPI into physical millimeters.
    /// </summary>
    public static uint ComputePhysicalSizeMm(int pixels, double dpi)
    {
        if (pixels <= 0 || dpi <= 0 || double.IsNaN(dpi) || double.IsInfinity(dpi))
        {
            return 0;
        }

        var millimeters = pixels / dpi * 25.4;
        return (uint)Math.Round(millimeters, MidpointRounding.AwayFromZero);
    }

    private static double ToScalePercent(double wpfDpi)
    {
        if (double.IsNaN(wpfDpi) || double.IsInfinity(wpfDpi) || wpfDpi <= 0)
        {
            return 100;
        }

        return wpfDpi <= 10
            ? wpfDpi * 100
            : wpfDpi / 96 * 100;
    }

    private static uint FindNearest(double value, IReadOnlyList<uint> supportedValues)
    {
        var nearest = supportedValues[0];
        var nearestDistance = Math.Abs(value - nearest);

        for (var i = 1; i < supportedValues.Count; i++)
        {
            var candidate = supportedValues[i];
            var distance = Math.Abs(value - candidate);
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        return nearest;
    }
}
