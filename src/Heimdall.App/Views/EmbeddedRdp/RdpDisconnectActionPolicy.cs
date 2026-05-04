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

internal enum RdpOverlayPrimaryAction
{
    Reconnect,
    EditProfile
}

/// <summary>
/// Determines which reconnect-overlay actions are useful for a disconnect code.
/// </summary>
internal static class RdpDisconnectActionPolicy
{
    // Profile-remediation disconnects include security/NLA issues; 2308 is included
    // so users can disable NLA from the overlay's Edit profile button.
    public static bool ShouldOfferEditProfile(int? disconnectCode) => disconnectCode switch
    {
        2055 or 2308 or 2311 or 2825 or 3080 or 3848 or 4360 => true,
        _ => false
    };

    public static RdpOverlayPrimaryAction ResolvePrimaryAction(int? disconnectCode)
        => ShouldOfferEditProfile(disconnectCode)
            ? RdpOverlayPrimaryAction.EditProfile
            : RdpOverlayPrimaryAction.Reconnect;
}
