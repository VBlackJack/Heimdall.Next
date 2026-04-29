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

using Heimdall.App.Services;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Tests;

public sealed class RdpProfileResolverTests
{
    [Fact]
    public void BuildRedirections_UsesSettingsWhenFlagIsTrue()
    {
        var server = new ServerProfileDto
        {
            RdpUseGlobalDefaults = true,
            RdpRedirectClipboard = false,
            RdpRedirectDrives = true,
            RdpRedirectPrinters = true,
            RdpRedirectComPorts = true,
            RdpRedirectSmartCards = true,
            RdpRedirectWebcam = true,
            RdpRedirectUsb = true,
            RdpAudioCapture = true,
            RdpAudioMode = 2,
            RdpMultiMonitor = true,
            RdpDynamicResolution = false,
            RdpNla = false,
            RdpBitmapCaching = false,
            RdpCompression = false,
            RdpAutoReconnect = false
        };
        var settings = new AppSettings
        {
            RdpDefaultRedirectClipboard = true,
            RdpDefaultRedirectDrives = false,
            RdpDefaultRedirectPrinters = false,
            RdpDefaultRedirectComPorts = false,
            RdpDefaultRedirectSmartCards = false,
            RdpDefaultRedirectWebcam = false,
            RdpDefaultRedirectUsb = false,
            RdpDefaultAudioCapture = false,
            RdpDefaultAudioMode = 0,
            RdpDefaultMultiMonitor = false,
            RdpDefaultDynamicResolution = true,
            RdpDefaultNla = true,
            RdpDefaultBitmapCaching = true,
            RdpDefaultCompression = true,
            RdpDefaultAutoReconnect = true
        };

        var redirections = RdpProfileResolver.BuildRedirections(server, settings);

        Assert.True(redirections.Clipboard);
        Assert.False(redirections.Drives);
        Assert.False(redirections.Printers);
        Assert.False(redirections.ComPorts);
        Assert.False(redirections.SmartCards);
        Assert.False(redirections.Webcam);
        Assert.False(redirections.Usb);
        Assert.False(redirections.AudioCapture);
        Assert.Equal(0, redirections.AudioMode);
        Assert.False(redirections.MultiMonitor);
        Assert.True(redirections.DynamicResolution);
        Assert.True(redirections.Nla);
        Assert.True(redirections.BitmapCaching);
        Assert.True(redirections.Compression);
        Assert.True(redirections.AutoReconnect);
    }

    [Fact]
    public void BuildRedirections_UsesServerWhenFlagIsFalse()
    {
        var server = new ServerProfileDto
        {
            RdpUseGlobalDefaults = false,
            RdpRedirectClipboard = false,
            RdpRedirectDrives = true,
            RdpRedirectPrinters = true,
            RdpRedirectComPorts = true,
            RdpRedirectSmartCards = true,
            RdpRedirectWebcam = true,
            RdpRedirectUsb = true,
            RdpAudioCapture = true,
            RdpAudioMode = 2,
            RdpMultiMonitor = true,
            RdpDynamicResolution = false,
            RdpNla = false,
            RdpBitmapCaching = false,
            RdpCompression = false,
            RdpAutoReconnect = false
        };
        var settings = new AppSettings
        {
            RdpDefaultRedirectClipboard = true,
            RdpDefaultRedirectDrives = false,
            RdpDefaultRedirectPrinters = false,
            RdpDefaultRedirectComPorts = false,
            RdpDefaultRedirectSmartCards = false,
            RdpDefaultRedirectWebcam = false,
            RdpDefaultRedirectUsb = false,
            RdpDefaultAudioCapture = false,
            RdpDefaultAudioMode = 0,
            RdpDefaultMultiMonitor = false,
            RdpDefaultDynamicResolution = true,
            RdpDefaultNla = true,
            RdpDefaultBitmapCaching = true,
            RdpDefaultCompression = true,
            RdpDefaultAutoReconnect = true
        };

        var redirections = RdpProfileResolver.BuildRedirections(server, settings);

        Assert.False(redirections.Clipboard);
        Assert.True(redirections.Drives);
        Assert.True(redirections.Printers);
        Assert.True(redirections.ComPorts);
        Assert.True(redirections.SmartCards);
        Assert.True(redirections.Webcam);
        Assert.True(redirections.Usb);
        Assert.True(redirections.AudioCapture);
        Assert.Equal(2, redirections.AudioMode);
        Assert.True(redirections.MultiMonitor);
        Assert.False(redirections.DynamicResolution);
        Assert.False(redirections.Nla);
        Assert.False(redirections.BitmapCaching);
        Assert.False(redirections.Compression);
        Assert.False(redirections.AutoReconnect);
    }

    [Fact]
    public void BuildRedirections_PerformanceFlagsAndDisableUdpAreAlwaysPerServer()
    {
        var server = new ServerProfileDto
        {
            RdpUseGlobalDefaults = true,
            RdpPerformanceFlags = 0xFF,
            RdpDisableUdp = true
        };

        var redirections = RdpProfileResolver.BuildRedirections(server, new AppSettings());

        Assert.Equal(0xFF, redirections.PerformanceFlags);
        Assert.True(redirections.DisableUdp);
    }

    [Theory]
    [InlineData(true, 16, 32, 16)]
    [InlineData(true, 32, 16, 32)]
    [InlineData(false, 16, 32, 32)]
    [InlineData(false, 32, 16, 16)]
    public void ResolveColorDepth_GovernsByFlag(
        bool useGlobalDefaults,
        int settingsValue,
        int serverValue,
        int expected)
    {
        var server = new ServerProfileDto
        {
            RdpUseGlobalDefaults = useGlobalDefaults,
            RdpColorDepth = serverValue
        };
        var settings = new AppSettings { RdpDefaultColorDepth = settingsValue };

        Assert.Equal(expected, RdpProfileResolver.ResolveColorDepth(server, settings));
    }

    [Theory]
    [InlineData(8, 16)]
    [InlineData(20, 24)]
    [InlineData(40, 32)]
    public void ResolveColorDepth_NormalizesToSupportedValues(int raw, int expected)
    {
        var server = new ServerProfileDto
        {
            RdpUseGlobalDefaults = false,
            RdpColorDepth = raw
        };

        Assert.Equal(expected, RdpProfileResolver.ResolveColorDepth(server, new AppSettings()));
    }

    [Fact]
    public void BuildRedirections_WithDefaultDtoAndDefaultSettingsIsConsistent()
    {
        var settings = new AppSettings();
        var globalServer = new ServerProfileDto { RdpUseGlobalDefaults = true };
        var localServer = new ServerProfileDto { RdpUseGlobalDefaults = false };

        var asGlobal = RdpProfileResolver.BuildRedirections(globalServer, settings);
        var asServer = RdpProfileResolver.BuildRedirections(localServer, settings);

        Assert.Equal(asGlobal.Clipboard, asServer.Clipboard);
        Assert.Equal(asGlobal.Drives, asServer.Drives);
        Assert.Equal(asGlobal.Printers, asServer.Printers);
        Assert.Equal(asGlobal.ComPorts, asServer.ComPorts);
        Assert.Equal(asGlobal.SmartCards, asServer.SmartCards);
        Assert.Equal(asGlobal.Webcam, asServer.Webcam);
        Assert.Equal(asGlobal.Usb, asServer.Usb);
        Assert.Equal(asGlobal.AudioCapture, asServer.AudioCapture);
        Assert.Equal(asGlobal.AudioMode, asServer.AudioMode);
        Assert.Equal(asGlobal.MultiMonitor, asServer.MultiMonitor);
        Assert.Equal(asGlobal.DynamicResolution, asServer.DynamicResolution);
        Assert.Equal(asGlobal.Nla, asServer.Nla);
        Assert.Equal(asGlobal.BitmapCaching, asServer.BitmapCaching);
        Assert.Equal(asGlobal.Compression, asServer.Compression);
        Assert.Equal(asGlobal.AutoReconnect, asServer.AutoReconnect);
    }
}
