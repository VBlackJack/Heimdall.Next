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

using Heimdall.Core.Configuration;

namespace Heimdall.Rdp.Display;

/// <summary>
/// Resolved RDP display settings applied immediately before connection.
/// </summary>
public sealed record EffectiveDisplayContext
{
    public required RdpResolutionMode ConfiguredMode { get; init; }

    public required RdpResolutionMode EffectiveMode { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required uint DesktopScaleFactor { get; init; }

    public required uint DeviceScaleFactor { get; init; }

    public required bool SmartSizingEnabled { get; init; }

    public required bool MultiMonitorEnabled { get; init; }

    public required string Reason { get; init; }
}
