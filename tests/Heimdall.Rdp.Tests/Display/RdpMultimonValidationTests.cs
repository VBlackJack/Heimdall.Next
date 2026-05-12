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

using System.Drawing;
using Heimdall.Core.Configuration;
using Heimdall.Rdp.Display;

namespace Heimdall.Rdp.Tests.Display;

public sealed class RdpMultimonValidationTests
{
    [Fact]
    public void ValidateMultimon_SingleMonitorRequestedOnSingleMonitorHost_FallsBack()
    {
        var result = Validate(
            monitorCount: 1,
            Settings(RdpResolutionMode.Multimon, useMultimon: true, selectedMonitors: []));

        Assert.True(result.ShouldFallback);
        Assert.Equal(MultimonFallbackReason.SingleMonitorHost, result.Reason);
        Assert.Equal(RdpResolutionMode.FitWindow, result.CoercedSettings.ResolutionMode);
        Assert.False(result.CoercedSettings.UseMultimon);
        Assert.Empty(result.CoercedSettings.SelectedMonitorIndices);
    }

    [Fact]
    public void ValidateMultimon_InvalidSelectedMonitorIndex_FallsBack()
    {
        var result = Validate(
            monitorCount: 2,
            Settings(RdpResolutionMode.Multimon, useMultimon: true, selectedMonitors: [0, 2]));

        Assert.True(result.ShouldFallback);
        Assert.Equal(MultimonFallbackReason.InvalidMonitorIndex, result.Reason);
        Assert.Equal(RdpResolutionMode.FitWindow, result.CoercedSettings.ResolutionMode);
        Assert.False(result.CoercedSettings.UseMultimon);
        Assert.Empty(result.CoercedSettings.SelectedMonitorIndices);
    }

    [Fact]
    public void ValidateMultimon_EmptySelectionWithMultimonRequested_UsesAllMonitors()
    {
        var requested = Settings(RdpResolutionMode.Multimon, useMultimon: true, selectedMonitors: []);

        var result = Validate(monitorCount: 2, requested);

        Assert.False(result.ShouldFallback);
        Assert.Equal(MultimonFallbackReason.None, result.Reason);
        Assert.Same(requested, result.CoercedSettings);
    }

    [Fact]
    public void ValidateMultimon_SingleMonitorRequestedOnSingleMonitorHost_DoesNotFallback()
    {
        var requested = Settings(RdpResolutionMode.FitWindow, useMultimon: false, selectedMonitors: []);

        var result = Validate(monitorCount: 1, requested);

        Assert.False(result.ShouldFallback);
        Assert.Equal(MultimonFallbackReason.None, result.Reason);
        Assert.Same(requested, result.CoercedSettings);
    }

    [Fact]
    public void ValidateMultimon_ValidSelectedMonitorIndices_DoesNotFallback()
    {
        var requested = Settings(RdpResolutionMode.Multimon, useMultimon: true, selectedMonitors: [0, 1]);

        var result = Validate(monitorCount: 2, requested);

        Assert.False(result.ShouldFallback);
        Assert.Equal(MultimonFallbackReason.None, result.Reason);
        Assert.Same(requested, result.CoercedSettings);
    }

    [Fact]
    public void ValidateMultimon_CoercedSettingsResolveToStableSingleMonitorMode()
    {
        var result = Validate(
            monitorCount: 1,
            Settings(RdpResolutionMode.Multimon, useMultimon: true, selectedMonitors: []));

        Assert.True(result.ShouldFallback);

        var effective = RdpDisplayResolver.Resolve(
            result.CoercedSettings.ResolutionMode,
            Host(screenCount: 1),
            []);
        var secondPass = Validate(monitorCount: 1, result.CoercedSettings);

        Assert.Equal(RdpResolutionMode.FitWindow, effective.EffectiveMode);
        Assert.False(effective.MultiMonitorEnabled);
        Assert.False(secondPass.ShouldFallback);
    }

    private static RdpMultimonValidation Validate(int monitorCount, RdpDisplaySettings requested)
        => RdpDisplayResolver.ValidateMultimon(new RdpDisplayCapabilities(monitorCount), requested);

    private static RdpDisplaySettings Settings(
        RdpResolutionMode mode,
        bool useMultimon,
        IReadOnlyList<int> selectedMonitors)
        => new(mode, useMultimon, selectedMonitors);

    private static HostDisplayContext Host(int screenCount)
        => new()
        {
            MonitorBoundsPhysicalPx = new Size(1920, 1080),
            WorkingAreaPhysicalPx = new Size(1920, 1040),
            DesktopDpiScale = 1.0,
            ViewportPhysicalPx = new Size(1280, 720),
            IsFullscreen = false,
            ScreenCount = screenCount,
            IsMultiMonitorRequested = false
        };
}
