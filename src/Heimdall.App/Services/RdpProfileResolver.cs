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
using Heimdall.Rdp;

namespace Heimdall.App.Services;

/// <summary>
/// Resolves connect-time RDP options from a server profile and the global settings.
/// </summary>
internal static class RdpProfileResolver
{
    /// <summary>
    /// Builds the RDP redirection options using strict global-default semantics.
    /// </summary>
    public static RdpRedirectionOptions BuildRedirections(
        ServerProfileDto server,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        if (server.RdpUseGlobalDefaults)
        {
            return new RdpRedirectionOptions
            {
                Clipboard = settings.RdpDefaultRedirectClipboard,
                Drives = settings.RdpDefaultRedirectDrives,
                Printers = settings.RdpDefaultRedirectPrinters,
                ComPorts = settings.RdpDefaultRedirectComPorts,
                SmartCards = settings.RdpDefaultRedirectSmartCards,
                Webcam = settings.RdpDefaultRedirectWebcam,
                Usb = settings.RdpDefaultRedirectUsb,
                AudioCapture = settings.RdpDefaultAudioCapture,
                AudioMode = settings.RdpDefaultAudioMode,
                MultiMonitor = settings.RdpDefaultMultiMonitor,
                DynamicResolution = settings.RdpDefaultDynamicResolution,
                Nla = settings.RdpDefaultNla,
                BitmapCaching = settings.RdpDefaultBitmapCaching,
                Compression = settings.RdpDefaultCompression,
                AutoReconnect = settings.RdpDefaultAutoReconnect,
                PerformanceFlags = server.RdpPerformanceFlags,
                DisableUdp = server.RdpDisableUdp
            };
        }

        return new RdpRedirectionOptions
        {
            Clipboard = server.RdpRedirectClipboard,
            Drives = server.RdpRedirectDrives,
            Printers = server.RdpRedirectPrinters,
            ComPorts = server.RdpRedirectComPorts,
            SmartCards = server.RdpRedirectSmartCards,
            Webcam = server.RdpRedirectWebcam,
            Usb = server.RdpRedirectUsb,
            AudioCapture = server.RdpAudioCapture,
            AudioMode = server.RdpAudioMode,
            MultiMonitor = server.RdpMultiMonitor,
            DynamicResolution = server.RdpDynamicResolution,
            Nla = server.RdpNla,
            BitmapCaching = server.RdpBitmapCaching,
            Compression = server.RdpCompression,
            AutoReconnect = server.RdpAutoReconnect,
            PerformanceFlags = server.RdpPerformanceFlags,
            DisableUdp = server.RdpDisableUdp
        };
    }

    /// <summary>
    /// Resolves and normalizes the RDP color depth using the same governance rule.
    /// </summary>
    public static int ResolveColorDepth(ServerProfileDto server, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        var raw = server.RdpUseGlobalDefaults
            ? settings.RdpDefaultColorDepth
            : server.RdpColorDepth;

        return raw switch
        {
            <= 16 => 16,
            <= 24 => 24,
            _ => 32
        };
    }
}
