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

namespace Heimdall.Ssh.Tests;

public class RdpRedirectionOptionsTests
{
    // ── Default values ──────────────────────────────────────────────

    [Fact]
    public void Defaults_ClipboardEnabled()
    {
        var opts = new RdpRedirectionOptions();
        Assert.True(opts.Clipboard);
    }

    [Fact]
    public void Defaults_NlaEnabled()
    {
        var opts = new RdpRedirectionOptions();
        Assert.True(opts.Nla);
    }

    [Fact]
    public void Defaults_BitmapCachingEnabled()
    {
        var opts = new RdpRedirectionOptions();
        Assert.True(opts.BitmapCaching);
    }

    [Fact]
    public void Defaults_CompressionEnabled()
    {
        var opts = new RdpRedirectionOptions();
        Assert.True(opts.Compression);
    }

    [Fact]
    public void Defaults_AutoReconnectEnabled()
    {
        var opts = new RdpRedirectionOptions();
        Assert.True(opts.AutoReconnect);
    }

    [Fact]
    public void Defaults_PerformanceFlagsZero()
    {
        var opts = new RdpRedirectionOptions();
        Assert.Equal(0, opts.PerformanceFlags);
    }

    [Fact]
    public void Defaults_DisableUdpFalse()
    {
        var opts = new RdpRedirectionOptions();
        Assert.False(opts.DisableUdp);
    }

    [Fact]
    public void Defaults_DrivesDisabled()
    {
        var opts = new RdpRedirectionOptions();
        Assert.False(opts.Drives);
    }

    [Fact]
    public void Defaults_PrintersDisabled()
    {
        var opts = new RdpRedirectionOptions();
        Assert.False(opts.Printers);
    }

    // ── Performance flags bitmask ───────────────────────────────────

    [Fact]
    public void PerformanceFlags_DisableWallpaper_Bit0x01()
    {
        var opts = new RdpRedirectionOptions { PerformanceFlags = 0x01 };
        Assert.Equal(0x01, opts.PerformanceFlags & 0x01);
    }

    [Fact]
    public void PerformanceFlags_DisableFullWindowDrag_Bit0x02()
    {
        var opts = new RdpRedirectionOptions { PerformanceFlags = 0x02 };
        Assert.Equal(0x02, opts.PerformanceFlags & 0x02);
    }

    [Fact]
    public void PerformanceFlags_DisableMenuAnimations_Bit0x04()
    {
        var opts = new RdpRedirectionOptions { PerformanceFlags = 0x04 };
        Assert.Equal(0x04, opts.PerformanceFlags & 0x04);
    }

    [Fact]
    public void PerformanceFlags_DisableThemes_Bit0x08()
    {
        var opts = new RdpRedirectionOptions { PerformanceFlags = 0x08 };
        Assert.Equal(0x08, opts.PerformanceFlags & 0x08);
    }

    [Fact]
    public void PerformanceFlags_DisableCursorShadow_Bit0x20()
    {
        var opts = new RdpRedirectionOptions { PerformanceFlags = 0x20 };
        Assert.Equal(0x20, opts.PerformanceFlags & 0x20);
    }

    [Fact]
    public void PerformanceFlags_EnableComposition_Bit0x80()
    {
        var opts = new RdpRedirectionOptions { PerformanceFlags = 0x80 };
        Assert.Equal(0x80, opts.PerformanceFlags & 0x80);
    }

    [Fact]
    public void PerformanceFlags_CombinedBitmask_PreservesAllBits()
    {
        var flags = 0x01 | 0x02 | 0x04 | 0x08 | 0x20 | 0x80;
        var opts = new RdpRedirectionOptions { PerformanceFlags = flags };
        Assert.Equal(flags, opts.PerformanceFlags);
    }

    [Fact]
    public void PerformanceFlags_LanProfile_AllDisabled()
    {
        var opts = new RdpRedirectionOptions { PerformanceFlags = 0 };
        Assert.Equal(0, opts.PerformanceFlags);
    }

    [Fact]
    public void PerformanceFlags_LowBandwidthProfile_WallpaperAndThemes()
    {
        var opts = new RdpRedirectionOptions { PerformanceFlags = 0x01 | 0x08 };
        Assert.Equal(0x09, opts.PerformanceFlags);
        Assert.NotEqual(0, opts.PerformanceFlags & 0x01);
        Assert.NotEqual(0, opts.PerformanceFlags & 0x08);
        Assert.Equal(0, opts.PerformanceFlags & 0x02);
    }

    // ── DisableUdp ──────────────────────────────────────────────────

    [Fact]
    public void DisableUdp_WhenSet_IsTrue()
    {
        var opts = new RdpRedirectionOptions { DisableUdp = true };
        Assert.True(opts.DisableUdp);
    }

    // ── Full configuration ──────────────────────────────────────────

    [Fact]
    public void FullConfig_AllPropertiesSetCorrectly()
    {
        var opts = new RdpRedirectionOptions
        {
            Clipboard = false,
            Drives = true,
            Printers = true,
            ComPorts = true,
            SmartCards = true,
            Usb = true,
            Webcam = true,
            AudioCapture = true,
            AudioMode = 1,
            MultiMonitor = true,
            DynamicResolution = true,
            Nla = false,
            BitmapCaching = false,
            Compression = false,
            AutoReconnect = false,
            PerformanceFlags = 0xFF,
            DisableUdp = true
        };

        Assert.False(opts.Clipboard);
        Assert.True(opts.Drives);
        Assert.True(opts.Printers);
        Assert.True(opts.ComPorts);
        Assert.True(opts.SmartCards);
        Assert.True(opts.Usb);
        Assert.True(opts.Webcam);
        Assert.True(opts.AudioCapture);
        Assert.Equal(1, opts.AudioMode);
        Assert.True(opts.MultiMonitor);
        Assert.True(opts.DynamicResolution);
        Assert.False(opts.Nla);
        Assert.False(opts.BitmapCaching);
        Assert.False(opts.Compression);
        Assert.False(opts.AutoReconnect);
        Assert.Equal(0xFF, opts.PerformanceFlags);
        Assert.True(opts.DisableUdp);
    }
}
