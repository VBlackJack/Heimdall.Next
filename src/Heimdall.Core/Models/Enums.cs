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

namespace Heimdall.Core.Models;

public enum ConnectionType { Rdp, Ssh, Sftp, Ftp, Local, Citrix, Vnc, Telnet }

public enum ConnectionState
{
    Disconnected,
    Initializing,
    ValidatingConfig,
    EstablishingTunnel,
    TunnelEstablished,
    LaunchingRdp,
    LaunchingSsh,
    LaunchingSftp,
    LaunchingLocal,
    LaunchingVnc,
    LaunchingFtp,
    LaunchingTelnet,
    LaunchingCitrix,
    Connected,
    Disconnecting,
    Error
}

public enum ApplicationStatus
{
    Initializing,
    Ready,
    Busy,
    Error,
    Shutdown
}

public enum SplitOrientation { Horizontal, Vertical }

public enum RdpAudioMode
{
    Disabled = 0,
    LocalPlayback = 1,
    RemotePlayback = 2
}

public enum AspectRatio
{
    Stretch,
    Auto,
    Ratio16x9,
    Ratio4x3,
    Ratio21x9
}

public enum SshMode { External, Embedded }

public enum RdpMode { External, Embedded }

public enum EnvironmentType
{
    None,
    Production,
    Staging,
    Lab,
    Personal
}

public enum RecurrenceType
{
    Once,
    Daily,
    Weekly,
    Weekdays,
    Custom
}

/// <summary>
/// Schedule types for automated connection tasks.
/// </summary>
public enum ScheduleType
{
    /// <summary>Connect at a specific time each day.</summary>
    Daily,

    /// <summary>Connect at a recurring interval in minutes.</summary>
    Interval
}

/// <summary>
/// Categories for built-in tools in the command palette and menus.
/// </summary>
public enum ToolCategory
{
    Network,
    Security,
    Encoding,
    System,
    External
}

/// <summary>
/// Elevation strategy for local shell sessions.
/// </summary>
public enum ElevationMode
{
    /// <summary>No elevation requested.</summary>
    None,
    /// <summary>Try gsudo first, fall back to runas on failure (default when elevated).</summary>
    Auto,
    /// <summary>Force gsudo (fails if gsudo not available or blocked by endpoint manager).</summary>
    Gsudo,
    /// <summary>Use ShellExecute runas verb (compatible with AdminByRequest, CyberArk, etc.).</summary>
    Runas
}

/// <summary>
/// Provenance tag for a server profile.
/// Numeric ordering is permanent because the values are persisted in servers.json.
/// </summary>
public enum ProfileOrigin
{
    /// <summary>Created manually, or deserialized from a pre-b63 profile without an origin.</summary>
    Manual = 0,
    /// <summary>Imported from a .rdp file.</summary>
    ImportRdp = 1,
    /// <summary>Imported from an OpenSSH config file.</summary>
    ImportOpenSsh = 2,
    /// <summary>Imported from PuTTY sessions.</summary>
    ImportPutty = 3,
    /// <summary>Imported from mRemoteNG.</summary>
    ImportMRemoteNg = 4,
    /// <summary>Imported from MobaXterm.</summary>
    ImportMobaXterm = 5,
    /// <summary>Imported from RDCMan.</summary>
    ImportRdcMan = 6
}
