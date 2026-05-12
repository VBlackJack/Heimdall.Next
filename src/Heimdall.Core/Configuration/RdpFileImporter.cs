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

using Heimdall.Core.Models;

namespace Heimdall.Core.Configuration;

/// <summary>
/// Parses standard Microsoft .rdp files (key:type:value format) into
/// <see cref="ServerProfileDto"/> instances.
/// </summary>
public static class RdpFileImporter
{
    /// <summary>
    /// Parse a single .rdp file content into a server profile.
    /// </summary>
    public static ServerProfileDto? Parse(string content, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Format: key:type:value (e.g., "full address:s:server.example.com")
            var parts = trimmed.Split(':', 3);
            if (parts.Length >= 3)
            {
                props[parts[0].Trim()] = parts[2].Trim();
            }
            else if (parts.Length == 2)
            {
                props[parts[0].Trim()] = parts[1].Trim();
            }
        }

        if (!props.TryGetValue("full address", out var fullAddress) || string.IsNullOrWhiteSpace(fullAddress))
            return null;

        // Parse host:port from full address
        var host = fullAddress;
        var port = DefaultPorts.Rdp;
        var colonIdx = fullAddress.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(fullAddress[(colonIdx + 1)..], out var parsedPort))
        {
            host = fullAddress[..colonIdx];
            port = parsedPort;
        }

        var displayName = fileName is not null
            ? System.IO.Path.GetFileNameWithoutExtension(fileName)
            : host;

        var dto = new ServerProfileDto
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = displayName,
            Origin = ProfileOrigin.ImportRdp,
            RemoteServer = host,
            RemotePort = port,
            ConnectionType = "RDP",
            RdpMode = "Embedded",
            UseDirectConnection = true,
        };

        if (props.TryGetValue("username", out var user) && !string.IsNullOrWhiteSpace(user))
        {
            // Handle domain\user or user@domain
            if (user.Contains('\\'))
            {
                var split = user.Split('\\', 2);
                dto.RdpUsername = $"{split[1]}@{split[0]}";
            }
            else
            {
                dto.RdpUsername = user;
            }
        }

        if (props.TryGetValue("domain", out var domain) && !string.IsNullOrWhiteSpace(domain)
            && dto.RdpUsername is not null && !dto.RdpUsername.Contains('@'))
        {
            dto.RdpUsername = $"{dto.RdpUsername}@{domain}";
        }

        if (props.TryGetValue("session bpp", out var bpp) && int.TryParse(bpp, out var colorDepth))
            dto.RdpColorDepth = colorDepth;

        if (props.TryGetValue("smart sizing", out var smart))
            dto.RdpDynamicResolution = smart == "1";

        if (props.TryGetValue("redirectclipboard", out var clip))
            dto.RdpRedirectClipboard = clip == "1";

        if (props.TryGetValue("redirectdrives", out var drives))
            dto.RdpRedirectDrives = drives == "1";

        if (props.TryGetValue("redirectprinters", out var printers))
            dto.RdpRedirectPrinters = printers == "1";

        if (props.TryGetValue("audiocapturemode", out var audioCapture))
            dto.RdpAudioCapture = audioCapture == "1";

        if (props.TryGetValue("use multimon", out var multimon))
            dto.RdpMultiMonitor = multimon == "1";

        if (props.TryGetValue("gatewayhostname", out var gw) && !string.IsNullOrWhiteSpace(gw))
        {
            dto.RdpGateway = gw;
            dto.RdpMode = "External";
        }

        return dto;
    }
}
