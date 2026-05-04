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

namespace Heimdall.Rdp.Display;

/// <summary>
/// Host-side display facts used to resolve the initial RDP desktop shape.
/// All sizes are physical pixels.
/// </summary>
public sealed record HostDisplayContext
{
    /// <summary>Bounds of the monitor or monitor group targeted by the host window, in physical pixels.</summary>
    public required Size MonitorBoundsPhysicalPx { get; init; }

    /// <summary>Working area of the targeted monitor or monitor group excluding taskbars and docked shells, in physical pixels.</summary>
    public required Size WorkingAreaPhysicalPx { get; init; }

    /// <summary>Desktop DPI scale for the targeted monitor where 1.0 is 100%.</summary>
    public required double DesktopDpiScale { get; init; }

    /// <summary>Current embedded RDP viewport size, in physical pixels.</summary>
    public required Size ViewportPhysicalPx { get; init; }

    /// <summary>Whether the owning RDP pane is currently fullscreen.</summary>
    public required bool IsFullscreen { get; init; }

    /// <summary>Number of screens visible to the local host.</summary>
    public required int ScreenCount { get; init; }

    /// <summary>Whether the profile explicitly requested a multi-monitor RDP session.</summary>
    public required bool IsMultiMonitorRequested { get; init; }
}
