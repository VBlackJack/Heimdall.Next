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

public class RdpFileGeneratorTests
{
    // ── Basic generation ──────────────────────────────────────────────

    [Fact]
    public void Generate_MinimalOptions_ProducesValidRdpContent()
    {
        var options = new RdpFileOptions { Host = "server.example.com" };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("full address:s:", content);
        Assert.Contains("desktopwidth:i:", content);
        Assert.Contains("desktopheight:i:", content);
        Assert.Contains("screen mode id:i:", content);
        Assert.Contains("redirectclipboard:i:", content);
        Assert.Contains("authentication level:i:", content);
    }

    // ── Host and port ─────────────────────────────────────────────────

    [Fact]
    public void Generate_HostAndDefaultPort_AppearsInFullAddress()
    {
        var options = new RdpFileOptions { Host = "10.0.0.1" };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("full address:s:10.0.0.1:3389", content);
    }

    [Fact]
    public void Generate_CustomPort_AppearsInFullAddress()
    {
        var options = new RdpFileOptions { Host = "10.0.0.1", Port = 3390 };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("full address:s:10.0.0.1:3390", content);
    }

    // ── Username ──────────────────────────────────────────────────────

    [Fact]
    public void Generate_WithUsername_IncludesUsernameField()
    {
        var options = new RdpFileOptions { Host = "srv", Username = "admin" };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("username:s:admin", content);
    }

    [Fact]
    public void Generate_WithDomain_IncludesDomainField()
    {
        var options = new RdpFileOptions { Host = "srv", Domain = "CORP" };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("domain:s:CORP", content);
    }

    [Fact]
    public void Generate_NullUsername_OmitsUsernameField()
    {
        var options = new RdpFileOptions { Host = "srv" };

        var content = RdpFileGenerator.Generate(options);

        Assert.DoesNotContain("username:s:", content);
    }

    // ── CRLF injection sanitization ───────────────────────────────────

    [Fact]
    public void Generate_UsernameCrlfInjection_StrippedFromOutput()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Username = "admin\r\nevil:s:payload"
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("username:s:adminevil:s:payload", content);
        Assert.DoesNotContain("\r\nevil", content);
    }

    [Fact]
    public void Generate_HostWithNullBytes_StrippedFromOutput()
    {
        var options = new RdpFileOptions { Host = "srv\0.local" };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("full address:s:srv.local:", content);
    }

    // ── Gateway settings ──────────────────────────────────────────────

    [Fact]
    public void Generate_WithGateway_IncludesGatewaySettings()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            GatewayHostname = "gw.example.com"
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("gatewayusagemethod:i:1", content);
        Assert.Contains("gatewayprofileusagemethod:i:1", content);
        Assert.Contains("gatewayhostname:s:gw.example.com", content);
        Assert.Contains("gatewaycredentialssource:i:0", content);
    }

    [Fact]
    public void Generate_WithoutGateway_DisablesGateway()
    {
        var options = new RdpFileOptions { Host = "srv" };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("gatewayusagemethod:i:0", content);
        Assert.DoesNotContain("gatewayhostname:s:", content);
    }

    // ── Redirection options ───────────────────────────────────────────

    [Fact]
    public void Generate_ClipboardEnabled_RedirectClipboard1()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { Clipboard = true }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("redirectclipboard:i:1", content);
    }

    [Fact]
    public void Generate_DrivesEnabled_RedirectDrives1()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { Drives = true }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("redirectdrives:i:1", content);
    }

    [Fact]
    public void Generate_PrintersEnabled_RedirectPrinters1()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { Printers = true }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("redirectprinters:i:1", content);
    }

    [Fact]
    public void Generate_AllRedirectionsDisabled_AllZero()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions
            {
                Clipboard = false,
                Drives = false,
                Printers = false,
                ComPorts = false,
                SmartCards = false
            }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("redirectclipboard:i:0", content);
        Assert.Contains("redirectdrives:i:0", content);
        Assert.Contains("redirectprinters:i:0", content);
        Assert.Contains("redirectcomports:i:0", content);
        Assert.Contains("redirectsmartcards:i:0", content);
    }

    // ── Color depth and resolution ────────────────────────────────────

    [Fact]
    public void Generate_DefaultColorDepth_SessionBpp32()
    {
        var options = new RdpFileOptions { Host = "srv" };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("session bpp:i:32", content);
    }

    [Fact]
    public void Generate_CustomColorDepth_SessionBpp16()
    {
        var options = new RdpFileOptions { Host = "srv", ColorDepth = 16 };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("session bpp:i:16", content);
    }

    [Fact]
    public void Generate_CustomResolution_IncludedInOutput()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Width = 2560,
            Height = 1440
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("desktopwidth:i:2560", content);
        Assert.Contains("desktopheight:i:1440", content);
    }

    [Fact]
    public void Generate_FullScreen_ScreenModeId2()
    {
        var options = new RdpFileOptions { Host = "srv", FullScreen = true };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("screen mode id:i:2", content);
    }

    [Fact]
    public void Generate_Windowed_ScreenModeId1()
    {
        var options = new RdpFileOptions { Host = "srv", FullScreen = false };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("screen mode id:i:1", content);
    }

    // ── NLA setting ───────────────────────────────────────────────────

    [Fact]
    public void Generate_NlaEnabled_AuthLevel2()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { Nla = true }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("authentication level:i:2", content);
        Assert.Contains("enablecredsspsupport:i:1", content);
    }

    [Fact]
    public void Generate_NlaDisabled_AuthLevel0()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { Nla = false }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("authentication level:i:0", content);
        Assert.Contains("enablecredsspsupport:i:0", content);
    }

    // ── Admin mode ────────────────────────────────────────────────────

    [Fact]
    public void Generate_AdminMode_IncludesAdministrativeSession()
    {
        var options = new RdpFileOptions { Host = "srv", AdminMode = true };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("administrative session:i:1", content);
    }

    [Fact]
    public void Generate_NoAdminMode_OmitsAdministrativeSession()
    {
        var options = new RdpFileOptions { Host = "srv", AdminMode = false };

        var content = RdpFileGenerator.Generate(options);

        Assert.DoesNotContain("administrative session:i:", content);
    }

    // ── Null / empty optional fields ──────────────────────────────────

    [Fact]
    public void Generate_NullDomain_OmitsDomainField()
    {
        var options = new RdpFileOptions { Host = "srv", Domain = null };

        var content = RdpFileGenerator.Generate(options);

        Assert.DoesNotContain("domain:s:", content);
    }

    [Fact]
    public void Generate_EmptyUsername_OmitsUsernameField()
    {
        var options = new RdpFileOptions { Host = "srv", Username = "" };

        var content = RdpFileGenerator.Generate(options);

        Assert.DoesNotContain("username:s:", content);
    }

    [Fact]
    public void Generate_WhitespaceGateway_DisablesGateway()
    {
        var options = new RdpFileOptions { Host = "srv", GatewayHostname = "   " };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("gatewayusagemethod:i:0", content);
        Assert.DoesNotContain("gatewayhostname:s:", content);
    }

    [Fact]
    public void Generate_NullHost_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RdpFileGenerator.Generate(null!));
    }

    [Fact]
    public void Generate_EmptyHost_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            RdpFileGenerator.Generate(new RdpFileOptions { Host = "" }));
    }

    // ── Multi-monitor and dynamic resolution ──────────────────────────

    [Fact]
    public void Generate_MultiMonitor_IncludesUseMultimon()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { MultiMonitor = true }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("use multimon:i:1", content);
    }

    [Fact]
    public void Generate_DynamicResolution_IncludesSmartSizing()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { DynamicResolution = true }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("smart sizing:i:1", content);
        Assert.Contains("dynamic resolution:i:1", content);
    }

    // ── Performance flags ────────────────────────────────────────────

    [Fact]
    public void Generate_PerformanceFlags_DecomposesToRdpSettings()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions
            {
                PerformanceFlags = 0x01 | 0x04 | 0x80 | 0x100
            }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("disable wallpaper:i:1", content);
        Assert.Contains("disable full window drag:i:0", content);
        Assert.Contains("disable menu anims:i:1", content);
        Assert.Contains("disable themes:i:0", content);
        Assert.Contains("allow font smoothing:i:1", content);
        Assert.Contains("allow desktop composition:i:1", content);
    }

    [Fact]
    public void Generate_PerformanceFlagsZero_OmitsPerformanceSettings()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { PerformanceFlags = 0 }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.DoesNotContain("disable wallpaper:i:", content);
        Assert.DoesNotContain("allow font smoothing:i:", content);
    }

    // ── DisableUdp / network auto-detect ─────────────────────────────

    [Fact]
    public void Generate_DisableUdp_ForcesTcpOnly()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { DisableUdp = true }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("networkautodetect:i:0", content);
        Assert.Contains("bandwidthautodetect:i:0", content);
        Assert.Contains("connection type:i:6", content);
    }

    [Fact]
    public void Generate_DefaultUdp_EnablesAutoDetect()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { DisableUdp = false }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("networkautodetect:i:1", content);
        Assert.Contains("bandwidthautodetect:i:1", content);
        Assert.Contains("connection type:i:7", content);
    }

    // ── USB / Webcam ─────────────────────────────────────────────────

    [Fact]
    public void Generate_UsbRedirect_IncludesUsbDevices()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { Usb = true }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("usbdevicestoredirect:s:*", content);
    }

    [Fact]
    public void Generate_WebcamRedirect_IncludesCameraRedirect()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { Webcam = true }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("camerastoredirect:s:*", content);
    }

    [Fact]
    public void Generate_NoUsbWebcam_OmitsDeviceStrings()
    {
        var options = new RdpFileOptions { Host = "srv" };

        var content = RdpFileGenerator.Generate(options);

        Assert.DoesNotContain("usbdevicestoredirect", content);
        Assert.DoesNotContain("camerastoredirect", content);
    }

    // ── Audio mode ───────────────────────────────────────────────────

    [Fact]
    public void Generate_AudioModeLocal_MapsToRdpMode0()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { AudioMode = 1 }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("audiomode:i:0", content);
    }

    [Fact]
    public void Generate_AudioModeRemote_MapsToRdpMode1()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { AudioMode = 2 }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("audiomode:i:1", content);
    }

    [Fact]
    public void Generate_AudioModeDisabled_MapsToRdpMode2()
    {
        var options = new RdpFileOptions
        {
            Host = "srv",
            Redirections = new RdpRedirectionOptions { AudioMode = 0 }
        };

        var content = RdpFileGenerator.Generate(options);

        Assert.Contains("audiomode:i:2", content);
    }
}
