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
/// Strongly-typed application settings mapped to settings.json.
/// Default values match the legacy settings.default.json for backward compatibility.
/// </summary>
public class AppSettings
{
    // Display
    public int DefaultResolutionWidth { get; set; } = 1920;
    public int DefaultResolutionHeight { get; set; } = 1080;
    public bool FullScreen { get; set; } = true;
    public bool AdminMode { get; set; } = true;
    public string DefaultLocale { get; set; } = "en";
    public string DefaultTheme { get; set; } = "Dark";

    // Tools (legacy Plink paths, kept for import compatibility)
    public string PlinkPath { get; set; } = @"C:\Program Files\PuTTY\plink.exe";
    public string? PuttyPath { get; set; }
    public string? PsftpPath { get; set; }

    // Tunnels
    public int TunnelEstablishmentDelayMs { get; set; } = 2500;
    public int TunnelRetryDelayMs { get; set; } = 1500;
    public int ProcessKillTimeoutMs { get; set; } = 2000;

    // Logging
    public bool EnableLogging { get; set; } = true;
    public string LogFilePath { get; set; } = @"logs\heimdall.log";

    // Security
    public string? PinHash { get; set; }
    public string? PinSalt { get; set; }
    public string? HmacKey { get; set; }
    public DateTime? HmacKeyCreatedAt { get; set; }
    public string? LastDpapiUser { get; set; }
    public bool RequireCredentialGuard { get; set; }
    public bool EnableEventLog { get; set; }

    // SSH defaults
    public string SshDefaultMode { get; set; } = "External";
    public int AntiIdleIntervalSeconds { get; set; } = 60;
    public int SshTmoutResetIntervalSeconds { get; set; } = 240;

    // RDP defaults
    public string RdpDefaultMode { get; set; } = "External";
    public bool RdpDefaultRedirectClipboard { get; set; } = true;
    public bool RdpDefaultRedirectDrives { get; set; }
    public bool RdpDefaultRedirectPrinters { get; set; }
    public bool RdpDefaultRedirectComPorts { get; set; }
    public bool RdpDefaultRedirectSmartCards { get; set; }
    public bool RdpDefaultRedirectWebcam { get; set; }
    public bool RdpDefaultRedirectUsb { get; set; }
    public int RdpDefaultAudioMode { get; set; }
    public bool RdpDefaultAudioCapture { get; set; }
    public bool RdpDefaultMultiMonitor { get; set; }
    public bool RdpDefaultDynamicResolution { get; set; } = true;
    public bool RdpDefaultNla { get; set; } = true;
    public int RdpDefaultColorDepth { get; set; } = 32;
    public bool RdpDefaultBitmapCaching { get; set; } = true;
    public bool RdpDefaultCompression { get; set; } = true;
    public bool RdpDefaultAutoReconnect { get; set; } = true;

    // Session
    public bool EnableSessionPersistence { get; set; }
    public int MaxEmbeddedSessions { get; set; } = 10;
    public int EmbeddedRdpTimeoutMs { get; set; } = 30000;
    public int EmbeddedIdleTimeoutMs { get; set; }
    public bool SftpBrowserEnabled { get; set; } = true;
    public bool SftpAutoOpenOnSsh { get; set; } = true;
    public string ExternalEditorPath { get; set; } = @"%windir%\system32\notepad.exe";
    public bool PreventSleepDuringSession { get; set; } = true;
    public bool SessionLoggingEnabled { get; set; }
    public string SessionLogDirectory { get; set; } = @"logs\sessions";

    // UI state
    public bool SidebarCollapsed { get; set; }
    public int SidebarWidth { get; set; } = 220;
    public List<string> TreeExpandedNodes { get; set; } = new();
    public string? TunnelGridColumnWidths { get; set; }

    // Collections
    public List<SshGatewayDto> SshGateways { get; set; } = new();
    public List<ProjectDto> Projects { get; set; } = new();

    /// <summary>
    /// Empty groups persisted so they remain visible in the TreeView even without servers.
    /// Each entry is "projectId|groupPath" (e.g., "abc123|Infrastructure/Linux").
    /// </summary>
    public List<string> EmptyGroups { get; set; } = new();

    // Scheduled connections
    public List<ScheduledTaskDto> ScheduledTasks { get; set; } = new();
}
