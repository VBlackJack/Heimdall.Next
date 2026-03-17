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

using System.Text.RegularExpressions;

namespace Heimdall.Core.Configuration;

/// <summary>
/// Validates configuration objects against expected schemas and constraints.
/// </summary>
public static partial class SchemaValidator
{
    private const int MinPort = 1;
    private const int MaxPort = 65535;
    private const int MinResolution = 640;
    private const int MaxResolution = 7680;
    private const int MinColorDepth = 8;
    private const int MaxColorDepth = 32;
    private const int MaxEmbeddedSessionsLimit = 20;
    private const int MinAntiIdleInterval = 10;
    private const int MaxAntiIdleInterval = 600;

    private static readonly HashSet<string> ValidLocales = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "fr"
    };

    private static readonly HashSet<string> ValidThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Light", "Dark"
    };

    private static readonly HashSet<string> ValidConnectionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "RDP", "SSH", "SFTP"
    };

    private static readonly HashSet<string> ValidModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "External", "Embedded"
    };

    private static readonly HashSet<string> ValidAspectRatios = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stretch", "Auto", "16:9", "4:3", "21:9"
    };

    /// <summary>
    /// Validates an <see cref="AppSettings"/> instance against expected constraints.
    /// </summary>
    public static ValidationResult ValidateSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<string>();

        ValidateRange(errors, settings.DefaultResolutionWidth,
            MinResolution, MaxResolution, nameof(settings.DefaultResolutionWidth));
        ValidateRange(errors, settings.DefaultResolutionHeight,
            MinResolution, MaxResolution, nameof(settings.DefaultResolutionHeight));

        if (!ValidLocales.Contains(settings.DefaultLocale))
        {
            errors.Add($"{nameof(settings.DefaultLocale)}: unsupported locale '{settings.DefaultLocale}'.");
        }

        if (!ValidThemes.Contains(settings.DefaultTheme))
        {
            errors.Add($"{nameof(settings.DefaultTheme)}: unsupported theme '{settings.DefaultTheme}'.");
        }

        ValidateRange(errors, settings.TunnelEstablishmentDelayMs, 0, 60000,
            nameof(settings.TunnelEstablishmentDelayMs));
        ValidateRange(errors, settings.TunnelRetryDelayMs, 0, 60000,
            nameof(settings.TunnelRetryDelayMs));
        ValidateRange(errors, settings.ProcessKillTimeoutMs, 0, 60000,
            nameof(settings.ProcessKillTimeoutMs));

        if (!ValidModes.Contains(settings.SshDefaultMode))
        {
            errors.Add($"{nameof(settings.SshDefaultMode)}: must be 'External' or 'Embedded'.");
        }

        if (!ValidModes.Contains(settings.RdpDefaultMode))
        {
            errors.Add($"{nameof(settings.RdpDefaultMode)}: must be 'External' or 'Embedded'.");
        }

        ValidateRange(errors, settings.AntiIdleIntervalSeconds,
            MinAntiIdleInterval, MaxAntiIdleInterval, nameof(settings.AntiIdleIntervalSeconds));
        ValidateRange(errors, settings.RdpDefaultColorDepth,
            MinColorDepth, MaxColorDepth, nameof(settings.RdpDefaultColorDepth));
        ValidateRange(errors, settings.MaxEmbeddedSessions,
            1, MaxEmbeddedSessionsLimit, nameof(settings.MaxEmbeddedSessions));
        ValidateRange(errors, settings.SidebarWidth, 0, 1000, nameof(settings.SidebarWidth));

        return new ValidationResult(errors.Count == 0, errors);
    }

    /// <summary>
    /// Validates an <see cref="ServerProfileDto"/> instance against expected constraints.
    /// </summary>
    public static ValidationResult ValidateServer(ServerProfileDto server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(server.Id))
        {
            errors.Add($"{nameof(server.Id)}: required.");
        }

        if (string.IsNullOrWhiteSpace(server.DisplayName))
        {
            errors.Add($"{nameof(server.DisplayName)}: required.");
        }

        if (string.IsNullOrWhiteSpace(server.RemoteServer))
        {
            errors.Add($"{nameof(server.RemoteServer)}: required.");
        }
        else if (!HostnameRegex().IsMatch(server.RemoteServer))
        {
            errors.Add($"{nameof(server.RemoteServer)}: invalid hostname or IP address.");
        }

        ValidatePort(errors, server.RemotePort, nameof(server.RemotePort));
        ValidatePort(errors, server.LocalPort, nameof(server.LocalPort));
        ValidatePort(errors, server.SshPort, nameof(server.SshPort));

        if (!ValidConnectionTypes.Contains(server.ConnectionType))
        {
            errors.Add($"{nameof(server.ConnectionType)}: must be 'RDP', 'SSH', or 'SFTP'.");
        }

        if (!ValidModes.Contains(server.SshMode))
        {
            errors.Add($"{nameof(server.SshMode)}: must be 'External' or 'Embedded'.");
        }

        if (!ValidModes.Contains(server.RdpMode))
        {
            errors.Add($"{nameof(server.RdpMode)}: must be 'External' or 'Embedded'.");
        }

        if (!ValidAspectRatios.Contains(server.RdpAspectRatio))
        {
            errors.Add($"{nameof(server.RdpAspectRatio)}: unsupported aspect ratio '{server.RdpAspectRatio}'.");
        }

        ValidateRange(errors, server.RdpColorDepth,
            MinColorDepth, MaxColorDepth, nameof(server.RdpColorDepth));
        ValidateRange(errors, server.RdpAudioMode, 0, 2, nameof(server.RdpAudioMode));

        return new ValidationResult(errors.Count == 0, errors);
    }

    /// <summary>
    /// Validates an <see cref="SshGatewayDto"/> instance against expected constraints.
    /// </summary>
    public static ValidationResult ValidateGateway(SshGatewayDto gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(gateway.Id))
        {
            errors.Add($"{nameof(gateway.Id)}: required.");
        }

        if (string.IsNullOrWhiteSpace(gateway.Name))
        {
            errors.Add($"{nameof(gateway.Name)}: required.");
        }

        if (string.IsNullOrWhiteSpace(gateway.Host))
        {
            errors.Add($"{nameof(gateway.Host)}: required.");
        }
        else if (!HostnameRegex().IsMatch(gateway.Host))
        {
            errors.Add($"{nameof(gateway.Host)}: invalid hostname or IP address.");
        }

        ValidatePort(errors, gateway.Port, nameof(gateway.Port));

        if (string.IsNullOrWhiteSpace(gateway.User))
        {
            errors.Add($"{nameof(gateway.User)}: required.");
        }

        if (!string.IsNullOrEmpty(gateway.KeyPath) && !File.Exists(gateway.KeyPath))
        {
            errors.Add($"{nameof(gateway.KeyPath)}: file not found '{gateway.KeyPath}'.");
        }

        if (gateway.ParentGatewayId == gateway.Id && !string.IsNullOrEmpty(gateway.Id))
        {
            errors.Add($"{nameof(gateway.ParentGatewayId)}: gateway cannot be its own parent.");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateRange(List<string> errors, int value, int min, int max, string name)
    {
        if (value < min || value > max)
        {
            errors.Add($"{name}: value {value} is outside the valid range [{min}..{max}].");
        }
    }

    private static void ValidatePort(List<string> errors, int port, string name)
    {
        ValidateRange(errors, port, MinPort, MaxPort, name);
    }

    /// <summary>
    /// Matches valid hostnames, FQDNs, IPv4, and IPv6 addresses.
    /// </summary>
    [GeneratedRegex(@"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)*[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?$|^\d{1,3}(?:\.\d{1,3}){3}$|^\[?[0-9a-fA-F:]+\]?$")]
    private static partial Regex HostnameRegex();
}

/// <summary>
/// Result of a schema validation operation.
/// </summary>
/// <param name="IsValid">Whether the validation passed without errors.</param>
/// <param name="Errors">List of validation error messages (empty if valid).</param>
public record ValidationResult(bool IsValid, List<string> Errors);
