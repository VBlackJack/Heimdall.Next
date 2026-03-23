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

using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Heimdall.Rdp;

/// <summary>
/// Generates .rdp files for launching external mstsc.exe connections.
/// All parameter values are sanitized against CRLF injection (CWE-93).
/// </summary>
public static class RdpFileGenerator
{
    /// <summary>
    /// Generate .rdp file content as a string.
    /// </summary>
    public static string Generate(RdpFileOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Host);

        var sb = new StringBuilder();

        // Connection
        AppendSanitized(sb, "full address:s:", $"{options.Host}:{options.Port}");

        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            AppendSanitized(sb, "username:s:", options.Username);
        }
        if (!string.IsNullOrWhiteSpace(options.Domain))
        {
            AppendSanitized(sb, "domain:s:", options.Domain);
        }

        // Display
        sb.AppendLine($"desktopwidth:i:{options.Width}");
        sb.AppendLine($"desktopheight:i:{options.Height}");
        sb.AppendLine($"screen mode id:i:{(options.FullScreen ? 2 : 1)}");
        sb.AppendLine($"session bpp:i:{options.ColorDepth}");

        // Admin mode
        if (options.AdminMode)
        {
            sb.AppendLine("administrative session:i:1");
        }

        // Redirections
        var r = options.Redirections;
        sb.AppendLine($"redirectclipboard:i:{BoolToInt(r.Clipboard)}");
        sb.AppendLine($"redirectdrives:i:{BoolToInt(r.Drives)}");
        sb.AppendLine($"redirectprinters:i:{BoolToInt(r.Printers)}");
        sb.AppendLine($"redirectcomports:i:{BoolToInt(r.ComPorts)}");
        sb.AppendLine($"redirectsmartcards:i:{BoolToInt(r.SmartCards)}");

        // Audio
        sb.AppendLine($"audiomode:i:{r.AudioMode switch
        {
            1 => 0, // Local playback
            2 => 1, // Remote playback
            _ => 2  // Disabled
        }}");
        sb.AppendLine($"audiocapturemode:i:{BoolToInt(r.AudioCapture)}");

        // NLA
        sb.AppendLine($"authentication level:i:{(r.Nla ? 2 : 0)}");
        sb.AppendLine($"enablecredsspsupport:i:{BoolToInt(r.Nla)}");

        // Performance
        sb.AppendLine($"bitmapcachepersistenable:i:{BoolToInt(r.BitmapCaching)}");
        sb.AppendLine($"compression:i:{BoolToInt(r.Compression)}");
        sb.AppendLine($"autoreconnection enabled:i:{BoolToInt(r.AutoReconnect)}");

        // Multi-monitor
        if (r.MultiMonitor)
        {
            sb.AppendLine("use multimon:i:1");
        }

        // Dynamic resolution
        if (r.DynamicResolution)
        {
            sb.AppendLine("smart sizing:i:1");
            sb.AppendLine("dynamic resolution:i:1");
        }

        // USB / Webcam
        if (r.Usb) sb.AppendLine("usbdevicestoredirect:s:*");
        if (r.Webcam) sb.AppendLine("camerastoredirect:s:*");

        // Performance experience flags
        var pf = r.PerformanceFlags;
        if (pf > 0)
        {
            sb.AppendLine($"disable wallpaper:i:{((pf & 0x01) != 0 ? 1 : 0)}");
            sb.AppendLine($"disable full window drag:i:{((pf & 0x02) != 0 ? 1 : 0)}");
            sb.AppendLine($"disable menu anims:i:{((pf & 0x04) != 0 ? 1 : 0)}");
            sb.AppendLine($"disable themes:i:{((pf & 0x08) != 0 ? 1 : 0)}");
            sb.AppendLine($"disable cursor setting:i:{((pf & 0x20) != 0 ? 1 : 0)}");
            sb.AppendLine($"allow font smoothing:i:{((pf & 0x80) != 0 ? 1 : 0)}");
            sb.AppendLine($"allow desktop composition:i:{((pf & 0x100) != 0 ? 1 : 0)}");
        }

        // Force TCP-only: disable network auto-detection to prevent UDP probing
        if (r.DisableUdp)
        {
            sb.AppendLine("networkautodetect:i:0");
            sb.AppendLine("bandwidthautodetect:i:0");
            sb.AppendLine("connection type:i:6");
        }
        else
        {
            sb.AppendLine("networkautodetect:i:1");
            sb.AppendLine("bandwidthautodetect:i:1");
            sb.AppendLine("connection type:i:7");
        }

        // RD Gateway — validate hostname before writing to .rdp file
        if (!string.IsNullOrWhiteSpace(options.GatewayHostname)
            && Core.Security.InputValidator.Validate(options.GatewayHostname, "Address"))
        {
            sb.AppendLine("gatewayusagemethod:i:1");
            sb.AppendLine("gatewayprofileusagemethod:i:1");
            AppendSanitized(sb, "gatewayhostname:s:", options.GatewayHostname);
            sb.AppendLine("gatewaycredentialssource:i:0");
        }
        else
        {
            sb.AppendLine("gatewayusagemethod:i:0");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Write an .rdp file with ACL restricted to the current user + Administrators + SYSTEM.
    /// </summary>
    public static async Task WriteToFileAsync(string filePath, RdpFileOptions options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var content = Generate(options);

        // Write with UTF-8 no BOM (standard for .rdp files)
        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

        // Restrict file ACL to current user + Administrators + SYSTEM
        ApplyRestrictedAcl(filePath);
    }

    /// <summary>
    /// Sanitize a string value to prevent CRLF injection in .rdp files (CWE-93).
    /// Strips CR, LF, and null bytes from the value.
    /// </summary>
    private static string SanitizeValue(string value)
    {
        return value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\0", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Appends a sanitized key:value line to the StringBuilder.
    /// </summary>
    private static void AppendSanitized(StringBuilder sb, string prefix, string value)
    {
        sb.AppendLine($"{prefix}{SanitizeValue(value)}");
    }

    private static int BoolToInt(bool value) => value ? 1 : 0;

    /// <summary>
    /// Applies a restricted ACL to the .rdp file: current user (Full Control),
    /// Administrators (Full Control), SYSTEM (Full Control). All inherited ACEs removed.
    /// </summary>
    private static void ApplyRestrictedAcl(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var security = fileInfo.GetAccessControl();

            // Remove all inherited rules
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var existingRules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in existingRules)
            {
                security.RemoveAccessRule(rule);
            }

            // Current user
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is not null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));
            }

            // Administrators
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                admins,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            // SYSTEM
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                system,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            // Best effort — ACL may fail on network paths or restricted environments
            Core.Logging.FileLogger.Warn($"[RdpFileGenerator] ACL restriction failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Options for generating an .rdp file.
/// </summary>
public class RdpFileOptions
{
    /// <summary>Target host address.</summary>
    public required string Host { get; init; }

    /// <summary>RDP port (default 3389).</summary>
    public int Port { get; init; } = 3389;

    /// <summary>Pre-filled username.</summary>
    public string? Username { get; init; }

    /// <summary>Domain for domain-joined authentication.</summary>
    public string? Domain { get; init; }

    /// <summary>Desktop width in pixels.</summary>
    public int Width { get; init; } = 1920;

    /// <summary>Desktop height in pixels.</summary>
    public int Height { get; init; } = 1080;

    /// <summary>Color depth in bits per pixel (16, 24, or 32).</summary>
    public int ColorDepth { get; init; } = 32;

    /// <summary>Whether to launch in full-screen mode.</summary>
    public bool FullScreen { get; init; }

    /// <summary>Whether to connect in admin/console mode (/admin).</summary>
    public bool AdminMode { get; init; }

    /// <summary>Device and feature redirection options.</summary>
    public RdpRedirectionOptions Redirections { get; init; } = new();

    /// <summary>RD Gateway hostname (null to disable gateway).</summary>
    public string? GatewayHostname { get; init; }
}
