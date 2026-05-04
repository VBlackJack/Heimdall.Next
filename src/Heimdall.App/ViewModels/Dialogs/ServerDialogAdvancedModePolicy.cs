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

namespace Heimdall.App.ViewModels.Dialogs;

internal static class ServerDialogAdvancedModePolicy
{
    public static bool ShouldApplyRdpDefault(string? connectionType, bool isEditMode, bool isProtocolSelected)
    {
        return IsRdp(connectionType) && (isEditMode || isProtocolSelected);
    }

    public static bool ShouldPersistRdpDefault(
        string? connectionType,
        bool isEditMode,
        bool isProtocolSelected,
        bool isApplyingDefault)
    {
        return !isApplyingDefault
            && IsRdp(connectionType)
            && (isEditMode || isProtocolSelected);
    }

    /// <summary>
    /// Snapshot of the advanced RDP fields the dialog tracks. <c>true</c>
    /// means a value diverges from the conservative defaults baked into the
    /// app, so the dialog should keep the Advanced expander open. When all
    /// fields are at their defaults, the Advanced toggle can be reset to
    /// <c>false</c> on the next open of a saved profile.
    /// </summary>
    public readonly record struct AdvancedRdpSnapshot(
        bool UseGlobalDefaults,
        bool AntiIdle,
        bool BitmapCaching,
        bool Compression,
        bool AutoReconnect,
        bool AdminMode,
        bool FullScreen);

    /// <summary>
    /// Returns true when the user has tweaked at least one advanced RDP field
    /// away from the conservative defaults (UseGlobalDefaults off, AntiIdle
    /// off, BitmapCaching on, Compression on, AutoReconnect on, AdminMode off,
    /// FullScreen off).
    /// </summary>
    public static bool IsAdvancedRdpCustomized(AdvancedRdpSnapshot snapshot)
        => snapshot.UseGlobalDefaults
        || snapshot.AntiIdle
        || !snapshot.BitmapCaching
        || !snapshot.Compression
        || !snapshot.AutoReconnect
        || snapshot.AdminMode
        || snapshot.FullScreen;

    /// <summary>
    /// Decides whether to honor the persisted "open dialog in advanced mode"
    /// preference for an edit-mode visit of an existing RDP profile. The
    /// preference is suppressed when the profile has no advanced
    /// customizations, so the dialog auto-collapses Advanced for cleanly
    /// configured profiles even when the global preference is true.
    /// </summary>
    public static bool ResolveAdvancedDefault(
        bool persistedDefault,
        bool isEditMode,
        AdvancedRdpSnapshot snapshot)
    {
        if (!persistedDefault)
        {
            return false;
        }

        if (isEditMode && !IsAdvancedRdpCustomized(snapshot))
        {
            return false;
        }

        return true;
    }

    private static bool IsRdp(string? connectionType)
    {
        return string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase);
    }
}
