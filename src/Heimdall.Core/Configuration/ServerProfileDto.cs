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
/// Flat DTO for server JSON deserialization.
/// Compatible with legacy servers.json format.
/// The ViewModel layer converts these to ObservableObject models.
/// </summary>
public class ServerProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RemoteServer { get; set; } = string.Empty;
    public int RemotePort { get; set; } = 3389;
    public int LocalPort { get; set; } = 33890;
    public string? Group { get; set; }
    public string? SshGatewayId { get; set; }
    public string? RdpUsername { get; set; }
    public string? RdpPasswordEncrypted { get; set; }
    public bool UseDirectConnection { get; set; }
    public string? ProjectId { get; set; }
    public string ConnectionType { get; set; } = "RDP";

    // SSH settings
    public string? SshUsername { get; set; }
    public int SshPort { get; set; } = 22;
    public string SshMode { get; set; } = "Embedded";
    public bool SshAgentForwarding { get; set; }
    public string? SshKeyPath { get; set; }
    public string? SshPasswordEncrypted { get; set; }
    public bool SshCompression { get; set; }
    public bool SshX11Forwarding { get; set; }
    public int SocksProxyPort { get; set; }
    public int RemoteBindPort { get; set; }
    public int RemoteLocalPort { get; set; }
    public string PostConnectCommand { get; set; } = "";
    public int PostConnectDelayMs { get; set; } = 800;

    // RDP display settings
    public bool RdpAntiIdle { get; set; }
    public string RdpAspectRatio { get; set; } = "Stretch";
    public bool IsFavorite { get; set; }
    public int SortOrder { get; set; }
    public string? Tags { get; set; }

    // RDP mode and device redirection
    public string RdpMode { get; set; } = "Embedded";
    public bool RdpUseGlobalDefaults { get; set; } = true;
    public bool RdpRedirectClipboard { get; set; } = true;
    public bool RdpRedirectDrives { get; set; }
    public bool RdpRedirectPrinters { get; set; }
    public bool RdpRedirectComPorts { get; set; }
    public bool RdpRedirectSmartCards { get; set; }
    public bool RdpRedirectWebcam { get; set; }
    public bool RdpRedirectUsb { get; set; }
    public int RdpAudioMode { get; set; }
    public bool RdpAudioCapture { get; set; }
    public bool RdpMultiMonitor { get; set; }
    public bool RdpDynamicResolution { get; set; } = true;
    public bool RdpNla { get; set; } = true;
    public int RdpColorDepth { get; set; } = 32;
    public bool RdpBitmapCaching { get; set; } = true;
    public bool RdpCompression { get; set; } = true;
    public bool RdpAutoReconnect { get; set; } = true;
    public int RdpPerformanceFlags { get; set; }
    public bool RdpDisableUdp { get; set; }
    public string? RdpGateway { get; set; }
    public string? Environment { get; set; }

    /// <summary>MAC address for Wake-on-LAN (format: AA:BB:CC:DD:EE:FF).</summary>
    public string? MacAddress { get; set; }

    // Local shell settings
    public string? LocalShellExecutable { get; set; }
    public string? LocalShellArguments { get; set; }
    public string? LocalShellWorkingDirectory { get; set; }
    public bool LocalShellElevated { get; set; }
    public Models.ElevationMode ElevationMode { get; set; } = Models.ElevationMode.None;

    /// <summary>
    /// Returns the effective elevation mode: if <see cref="ElevationMode"/> is
    /// <see cref="Models.ElevationMode.None"/> but legacy <see cref="LocalShellElevated"/>
    /// is true, returns <see cref="Models.ElevationMode.Auto"/> for backward compatibility.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Models.ElevationMode EffectiveElevationMode =>
        ElevationMode != Models.ElevationMode.None ? ElevationMode
        : LocalShellElevated ? Models.ElevationMode.Auto
        : Models.ElevationMode.None;

    // Citrix settings
    public string? CitrixStoreFrontUrl { get; set; }
    public string? CitrixAppName { get; set; }
    public string? CitrixIcaFilePath { get; set; }
    public bool CitrixSeamlessMode { get; set; } = true;
    public bool CitrixUseSso { get; set; } = true;

    /// <summary>Pre-authenticated SelfService.exe launch arguments from cache XML.</summary>
    public string? CitrixLaunchCommandLine { get; set; }

    // FTP settings
    public int FtpPort { get; set; } = 21;
    public string? FtpUsername { get; set; }
    public string? FtpPasswordEncrypted { get; set; }

    // VNC settings
    public int VncPort { get; set; } = 5900;
    public string? VncPassword { get; set; }

    // FTP options
    public bool FtpPassiveMode { get; set; } = true;
    public bool FtpUseSsl { get; set; }

    // VNC options
    public bool VncViewOnly { get; set; }

    // Telnet settings
    public int TelnetPort { get; set; } = 23;
    public string? TelnetUsername { get; set; }
    public string? TelnetPasswordEncrypted { get; set; }
}
