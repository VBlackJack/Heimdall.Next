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

namespace Heimdall.Core.Configuration;

/// <summary>
/// Normalizes legacy and Phase 1 RDP resolution profile fields.
/// </summary>
public static class RdpResolutionProfileMigration
{
    public static void Migrate(ServerProfileDto server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (!server.HasRdpResolutionModeField)
        {
            // Preserve legacy semantics before deriving from dimensions:
            // explicit mode wins, then legacy multimon, then legacy fixed size.
            server.RdpResolutionMode = server.RdpMultiMonitor
                ? RdpResolutionMode.Multimon
                : server.RdpFixedWidth > 0 && server.RdpFixedHeight > 0
                    ? RdpResolutionMode.Fixed
                    : RdpResolutionMode.FitWindow;
        }

        if (server.RdpResolutionMode == RdpResolutionMode.Multimon)
        {
            server.RdpMultiMonitor = true;
        }
    }

    public static void PrepareForSave(ServerProfileDto server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (!server.HasRdpResolutionModeField)
        {
            Migrate(server);
        }

        server.RdpMultiMonitor = server.RdpResolutionMode == RdpResolutionMode.Multimon;
    }
}
