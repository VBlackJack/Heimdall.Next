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

using Heimdall.Rdp;

namespace Heimdall.App.Tests;

public sealed class RdpDisplayHelperTests
{
    [Theory]
    [InlineData(1920, 4, 1920)]
    [InlineData(1919, 4, 1916)]
    [InlineData(1366, 4, 1364)]
    [InlineData(3, 4, 0)]
    [InlineData(0, 4, 0)]
    [InlineData(-5, 4, 0)]
    public void SnapToMultipleOf_SnapsDown(int value, int multiple, int expected)
    {
        Assert.Equal(expected, RdpDisplayHelper.SnapToMultipleOf(value, multiple));
    }

    [Fact]
    public void SnapToMultipleOf_RejectsInvalidMultiple()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RdpDisplayHelper.SnapToMultipleOf(100, 0));
    }

    [Theory]
    [InlineData(1.00, 100)]
    [InlineData(1.10, 100)]
    [InlineData(1.33, 125)]
    [InlineData(1.50, 150)]
    [InlineData(1.74, 175)]
    [InlineData(2.40, 250)]
    [InlineData(3.50, 300)]
    [InlineData(4.50, 400)]
    [InlineData(5.20, 500)]
    [InlineData(144.0, 150)]
    public void MapDpiToDesktopScaleFactor_MapsToNearestSupportedValue(double dpi, uint expected)
    {
        Assert.Equal(expected, RdpDisplayHelper.MapDpiToDesktopScaleFactor(dpi));
    }

    [Theory]
    [InlineData(1.00, 100)]
    [InlineData(1.19, 100)]
    [InlineData(1.25, 140)]
    [InlineData(1.50, 140)]
    [InlineData(1.65, 180)]
    [InlineData(2.00, 180)]
    [InlineData(144.0, 140)]
    public void MapDpiToDeviceScaleFactor_MapsToNearestSupportedValue(double dpi, uint expected)
    {
        Assert.Equal(expected, RdpDisplayHelper.MapDpiToDeviceScaleFactor(dpi));
    }

    [Theory]
    [InlineData(1920, 96.0, 508)]
    [InlineData(1920, 144.0, 339)]
    [InlineData(0, 96.0, 0)]
    [InlineData(1920, 0.0, 0)]
    public void ComputePhysicalSizeMm_ConvertsPixelsToMillimeters(int pixels, double dpi, uint expected)
    {
        Assert.Equal(expected, RdpDisplayHelper.ComputePhysicalSizeMm(pixels, dpi));
    }
}
