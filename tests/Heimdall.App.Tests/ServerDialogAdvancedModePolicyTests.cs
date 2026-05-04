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

using Heimdall.App.ViewModels.Dialogs;

namespace Heimdall.App.Tests;

public sealed class ServerDialogAdvancedModePolicyTests
{
    /// <summary>
    /// Conservative defaults — every advanced field at its repo-wide
    /// default value (UseGlobalDefaults off, AntiIdle off, BitmapCaching
    /// on, Compression on, AutoReconnect on, AdminMode off, FullScreen
    /// off). Used as the baseline by the "smart Advanced reset" path.
    /// </summary>
    private static ServerDialogAdvancedModePolicy.AdvancedRdpSnapshot DefaultSnapshot() =>
        new(
            UseGlobalDefaults: false,
            AntiIdle: false,
            BitmapCaching: true,
            Compression: true,
            AutoReconnect: true,
            AdminMode: false,
            FullScreen: false);

    [Fact]
    public void IsAdvancedRdpCustomized_DefaultSnapshot_ReturnsFalse()
    {
        var snapshot = DefaultSnapshot();

        Assert.False(ServerDialogAdvancedModePolicy.IsAdvancedRdpCustomized(snapshot));
    }

    [Fact]
    public void IsAdvancedRdpCustomized_UseGlobalDefaultsOn_ReturnsTrue()
    {
        var snapshot = DefaultSnapshot() with { UseGlobalDefaults = true };

        Assert.True(ServerDialogAdvancedModePolicy.IsAdvancedRdpCustomized(snapshot));
    }

    [Fact]
    public void IsAdvancedRdpCustomized_AntiIdleOn_ReturnsTrue()
    {
        var snapshot = DefaultSnapshot() with { AntiIdle = true };

        Assert.True(ServerDialogAdvancedModePolicy.IsAdvancedRdpCustomized(snapshot));
    }

    [Fact]
    public void IsAdvancedRdpCustomized_BitmapCachingOff_ReturnsTrue()
    {
        var snapshot = DefaultSnapshot() with { BitmapCaching = false };

        Assert.True(ServerDialogAdvancedModePolicy.IsAdvancedRdpCustomized(snapshot));
    }

    [Fact]
    public void IsAdvancedRdpCustomized_CompressionOff_ReturnsTrue()
    {
        var snapshot = DefaultSnapshot() with { Compression = false };

        Assert.True(ServerDialogAdvancedModePolicy.IsAdvancedRdpCustomized(snapshot));
    }

    [Fact]
    public void IsAdvancedRdpCustomized_AutoReconnectOff_ReturnsTrue()
    {
        var snapshot = DefaultSnapshot() with { AutoReconnect = false };

        Assert.True(ServerDialogAdvancedModePolicy.IsAdvancedRdpCustomized(snapshot));
    }

    [Fact]
    public void IsAdvancedRdpCustomized_AdminModeOn_ReturnsTrue()
    {
        var snapshot = DefaultSnapshot() with { AdminMode = true };

        Assert.True(ServerDialogAdvancedModePolicy.IsAdvancedRdpCustomized(snapshot));
    }

    [Fact]
    public void IsAdvancedRdpCustomized_FullScreenOn_ReturnsTrue()
    {
        var snapshot = DefaultSnapshot() with { FullScreen = true };

        Assert.True(ServerDialogAdvancedModePolicy.IsAdvancedRdpCustomized(snapshot));
    }

    [Fact]
    public void ResolveAdvancedDefault_PersistedFalse_AlwaysReturnsFalse()
    {
        Assert.False(ServerDialogAdvancedModePolicy.ResolveAdvancedDefault(
            persistedDefault: false,
            isEditMode: false,
            DefaultSnapshot()));

        Assert.False(ServerDialogAdvancedModePolicy.ResolveAdvancedDefault(
            persistedDefault: false,
            isEditMode: true,
            DefaultSnapshot() with { AdminMode = true }));
    }

    [Fact]
    public void ResolveAdvancedDefault_AddMode_HonoursPersistedDefaultRegardlessOfSnapshot()
    {
        // In add mode (new profile) the snapshot is the default factory state;
        // persisted=true should always open in advanced mode for a fresh profile.
        Assert.True(ServerDialogAdvancedModePolicy.ResolveAdvancedDefault(
            persistedDefault: true,
            isEditMode: false,
            DefaultSnapshot()));

        Assert.True(ServerDialogAdvancedModePolicy.ResolveAdvancedDefault(
            persistedDefault: true,
            isEditMode: false,
            DefaultSnapshot() with { AdminMode = true }));
    }

    [Fact]
    public void ResolveAdvancedDefault_EditMode_AutoCollapsesWhenSnapshotIsDefault()
    {
        // The whole point of RDP-PROF-09: an existing profile with no
        // advanced customizations should re-open with Advanced collapsed
        // even when the global preference is "open in advanced mode".
        var resolved = ServerDialogAdvancedModePolicy.ResolveAdvancedDefault(
            persistedDefault: true,
            isEditMode: true,
            DefaultSnapshot());

        Assert.False(resolved);
    }

    [Fact]
    public void ResolveAdvancedDefault_EditMode_HonoursPersistedDefaultWhenSnapshotIsCustomized()
    {
        var resolved = ServerDialogAdvancedModePolicy.ResolveAdvancedDefault(
            persistedDefault: true,
            isEditMode: true,
            DefaultSnapshot() with { AdminMode = true });

        Assert.True(resolved);
    }
}
