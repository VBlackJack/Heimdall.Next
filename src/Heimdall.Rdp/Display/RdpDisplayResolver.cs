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
using Heimdall.Rdp;

namespace Heimdall.Rdp.Display;

public static class RdpDisplayResolver
{
    private static readonly uint[] DesktopScaleFactors = [100, 125, 150, 175, 200];
    private static readonly Size DefaultSize = new(1024, 768);

    public static EffectiveDisplayContext Resolve(
        RdpResolutionMode configuredMode,
        HostDisplayContext hostContext,
        IReadOnlyList<(int Width, int Height)> presets,
        int? configuredWidthPx = null,
        int? configuredHeightPx = null)
    {
        ArgumentNullException.ThrowIfNull(hostContext);
        ArgumentNullException.ThrowIfNull(presets);

        var desktopScaleFactor = ResolveDesktopScaleFactor(hostContext.DesktopDpiScale);
        var deviceScaleFactor = desktopScaleFactor <= 140 ? 100u : 140u;

        return configuredMode switch
        {
            RdpResolutionMode.Auto => ResolveAuto(
                hostContext,
                presets,
                desktopScaleFactor,
                deviceScaleFactor),
            RdpResolutionMode.Fixed => Create(
                configuredMode,
                RdpResolutionMode.Fixed,
                new Size(
                    configuredWidthPx.GetValueOrDefault(DefaultSize.Width),
                    configuredHeightPx.GetValueOrDefault(DefaultSize.Height)),
                desktopScaleFactor,
                deviceScaleFactor,
                smartSizing: false,
                multiMonitor: false,
                reason: "explicit-fixed",
                floorWidthToDesktopMinimum: true),
            RdpResolutionMode.SmartSizing => Create(
                configuredMode,
                RdpResolutionMode.SmartSizing,
                ResolveViewportOrFallback(hostContext, out _),
                desktopScaleFactor,
                deviceScaleFactor,
                smartSizing: true,
                multiMonitor: false,
                reason: "explicit-smart-sizing",
                floorWidthToDesktopMinimum: false),
            RdpResolutionMode.Multimon => Create(
                configuredMode,
                RdpResolutionMode.Multimon,
                CoalesceSize(hostContext.MonitorBoundsPhysicalPx, DefaultSize),
                desktopScaleFactor,
                deviceScaleFactor,
                smartSizing: false,
                multiMonitor: true,
                reason: "explicit-multimon",
                floorWidthToDesktopMinimum: true),
            _ => Create(
                configuredMode,
                RdpResolutionMode.FitWindow,
                ResolveViewportOrFallback(hostContext, out _),
                desktopScaleFactor,
                deviceScaleFactor,
                smartSizing: true,
                multiMonitor: false,
                reason: "explicit-fit-window-scaled",
                floorWidthToDesktopMinimum: false)
        };
    }

    public static RdpMultimonValidation ValidateMultimon(
        RdpDisplayCapabilities host,
        RdpDisplaySettings requested)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(requested);

        if (!requested.UseMultimon)
        {
            return new RdpMultimonValidation(false, MultimonFallbackReason.None, requested);
        }

        if (host.MonitorCount == 1)
        {
            return CreateMultimonFallback(requested, MultimonFallbackReason.SingleMonitorHost);
        }

        if (requested.SelectedMonitorIndices.Any(index => index < 0 || index >= host.MonitorCount))
        {
            return CreateMultimonFallback(requested, MultimonFallbackReason.InvalidMonitorIndex);
        }

        return new RdpMultimonValidation(false, MultimonFallbackReason.None, requested);
    }

    public static Size ResolveExternalAutoWindowedSize(Size primaryWorkingArea, Size fallback)
    {
        var source = IsValidSize(primaryWorkingArea)
            ? primaryWorkingArea
            : fallback;

        return new Size(
            SnapDimension(source.Width),
            SnapDimension(source.Height));
    }

    private static EffectiveDisplayContext ResolveAuto(
        HostDisplayContext hostContext,
        IReadOnlyList<(int Width, int Height)> presets,
        uint desktopScaleFactor,
        uint deviceScaleFactor)
    {
        if (hostContext.IsFullscreen)
        {
            var monitorSize = CoalesceSize(hostContext.MonitorBoundsPhysicalPx, DefaultSize);
            return Create(
                RdpResolutionMode.Auto,
                RdpResolutionMode.Fixed,
                SelectClosestPreset(monitorSize, presets),
                desktopScaleFactor,
                deviceScaleFactor,
                smartSizing: false,
                multiMonitor: false,
                reason: "auto-fullscreen-single-monitor",
                floorWidthToDesktopMinimum: true);
        }

        return Create(
            RdpResolutionMode.Auto,
            RdpResolutionMode.SmartSizing,
            ResolveViewportOrFallback(hostContext, out var usedFallback),
            desktopScaleFactor,
            deviceScaleFactor,
            smartSizing: true,
            multiMonitor: false,
            reason: usedFallback ? "auto-windowed-fallback" : "auto-windowed",
            floorWidthToDesktopMinimum: false);
    }

    private static RdpMultimonValidation CreateMultimonFallback(
        RdpDisplaySettings requested,
        MultimonFallbackReason reason)
    {
        var coerced = requested with
        {
            ResolutionMode = RdpResolutionMode.FitWindow,
            UseMultimon = false,
            SelectedMonitorIndices = []
        };

        return new RdpMultimonValidation(true, reason, coerced);
    }

    private static EffectiveDisplayContext Create(
        RdpResolutionMode configuredMode,
        RdpResolutionMode effectiveMode,
        Size size,
        uint desktopScaleFactor,
        uint deviceScaleFactor,
        bool smartSizing,
        bool multiMonitor,
        string reason,
        bool floorWidthToDesktopMinimum)
    {
        return new EffectiveDisplayContext
        {
            ConfiguredMode = configuredMode,
            EffectiveMode = effectiveMode,
            Width = SnapWidth(size.Width, floorWidthToDesktopMinimum),
            Height = size.Height > 0 ? size.Height : DefaultSize.Height,
            DesktopScaleFactor = desktopScaleFactor,
            DeviceScaleFactor = deviceScaleFactor,
            SmartSizingEnabled = smartSizing,
            MultiMonitorEnabled = multiMonitor,
            Reason = reason
        };
    }

    private static Size ResolveViewportOrFallback(HostDisplayContext hostContext, out bool usedFallback)
    {
        if (IsValidSize(hostContext.ViewportPhysicalPx))
        {
            usedFallback = false;
            return hostContext.ViewportPhysicalPx;
        }

        usedFallback = true;
        return CoalesceSize(hostContext.WorkingAreaPhysicalPx, hostContext.MonitorBoundsPhysicalPx, DefaultSize);
    }

    private static Size SelectClosestPreset(Size target, IReadOnlyList<(int Width, int Height)> presets)
    {
        var hasCandidate = false;
        var selected = target;
        var selectedDistance = long.MaxValue;
        var selectedAreaDelta = long.MaxValue;
        var selectedArea = long.MinValue;

        foreach (var preset in presets)
        {
            if (preset.Width <= 0 || preset.Height <= 0)
            {
                continue;
            }

            var widthDelta = (long)preset.Width - target.Width;
            var heightDelta = (long)preset.Height - target.Height;
            var distance = widthDelta * widthDelta + heightDelta * heightDelta;
            var area = (long)preset.Width * preset.Height;
            var areaDelta = Math.Abs(area - ((long)target.Width * target.Height));

            if (!hasCandidate
                || distance < selectedDistance
                || (distance == selectedDistance && areaDelta < selectedAreaDelta)
                || (distance == selectedDistance && areaDelta == selectedAreaDelta && area > selectedArea))
            {
                hasCandidate = true;
                selected = new Size(preset.Width, preset.Height);
                selectedDistance = distance;
                selectedAreaDelta = areaDelta;
                selectedArea = area;
            }
        }

        return hasCandidate ? selected : target;
    }

    private static Size CoalesceSize(params Size[] sizes)
    {
        foreach (var size in sizes)
        {
            if (IsValidSize(size))
            {
                return size;
            }
        }

        return DefaultSize;
    }

    private static bool IsValidSize(Size size) => size.Width > 0 && size.Height > 0;

    private static int SnapDimension(int value)
    {
        var snapped = RdpDisplayHelper.SnapToMultipleOf(value, 4);
        return snapped > 0 ? snapped : 4;
    }

    private static int SnapWidth(int width, bool floorToDesktopMinimum)
    {
        var snapped = RdpDisplayHelper.SnapToMultipleOf(width, 4);
        if (snapped <= 0)
        {
            snapped = 4;
        }

        return floorToDesktopMinimum && snapped < 640
            ? 640
            : snapped;
    }

    private static uint ResolveDesktopScaleFactor(double dpiScale)
    {
        var percent = double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0
            ? 100
            : dpiScale * 100;
        var nearest = DesktopScaleFactors[0];
        var nearestDistance = Math.Abs(percent - nearest);

        for (var i = 1; i < DesktopScaleFactors.Length; i++)
        {
            var candidate = DesktopScaleFactors[i];
            var distance = Math.Abs(percent - candidate);
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        return nearest;
    }
}
