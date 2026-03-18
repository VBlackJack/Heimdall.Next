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
/// Default connection settings for a server group/folder.
/// Servers in this group inherit these values when their own fields are null/empty.
/// Supports hierarchical resolution: "PROD/Linux" inherits from "PROD" if not overridden.
/// </summary>
public class GroupDefaultsDto
{
    /// <summary>Default SSH gateway for servers in this group.</summary>
    public string? SshGatewayId { get; set; }

    /// <summary>Default SSH username for servers in this group.</summary>
    public string? SshUsername { get; set; }

    /// <summary>Default SSH key path for servers in this group.</summary>
    public string? SshKeyPath { get; set; }

    /// <summary>Default SSH port for servers in this group.</summary>
    public int? SshPort { get; set; }

    /// <summary>Default connection type (RDP, SSH, SFTP) for servers in this group.</summary>
    public string? ConnectionType { get; set; }

    /// <summary>Default environment tag (Production, Staging, Lab) for servers in this group.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Resolves inherited defaults for a server by walking up the group hierarchy.
    /// For group "PROD/Linux/Web", checks "PROD/Linux/Web" → "PROD/Linux" → "PROD".
    /// </summary>
    /// <param name="groupName">Full group path of the server.</param>
    /// <param name="allDefaults">All configured group defaults.</param>
    /// <returns>Merged defaults with the most specific values taking priority.</returns>
    public static GroupDefaultsDto Resolve(string? groupName, Dictionary<string, GroupDefaultsDto> allDefaults)
    {
        if (string.IsNullOrWhiteSpace(groupName) || allDefaults.Count == 0)
            return new GroupDefaultsDto();

        var result = new GroupDefaultsDto();
        var path = groupName;

        // Walk from root to leaf so more specific values override
        var ancestors = new List<string>();
        while (!string.IsNullOrEmpty(path))
        {
            ancestors.Add(path);
            var lastSep = path.LastIndexOf('/');
            path = lastSep > 0 ? path[..lastSep] : null;
        }

        ancestors.Reverse(); // root first

        foreach (var ancestor in ancestors)
        {
            if (allDefaults.TryGetValue(ancestor, out var defaults))
            {
                result.SshGatewayId ??= defaults.SshGatewayId;
                result.SshUsername ??= defaults.SshUsername;
                result.SshKeyPath ??= defaults.SshKeyPath;
                result.SshPort ??= defaults.SshPort;
                result.ConnectionType ??= defaults.ConnectionType;
                result.Environment ??= defaults.Environment;
            }
        }

        // Reverse priority: leaf overrides root
        foreach (var ancestor in Enumerable.Reverse(ancestors))
        {
            if (allDefaults.TryGetValue(ancestor, out var defaults))
            {
                if (defaults.SshGatewayId is not null) result.SshGatewayId = defaults.SshGatewayId;
                if (defaults.SshUsername is not null) result.SshUsername = defaults.SshUsername;
                if (defaults.SshKeyPath is not null) result.SshKeyPath = defaults.SshKeyPath;
                if (defaults.SshPort is not null) result.SshPort = defaults.SshPort;
                if (defaults.ConnectionType is not null) result.ConnectionType = defaults.ConnectionType;
                if (defaults.Environment is not null) result.Environment = defaults.Environment;
            }
        }

        return result;
    }

    /// <summary>
    /// Applies group defaults to a server DTO, filling in null/empty fields.
    /// The server's own values always take priority over inherited defaults.
    /// </summary>
    public void ApplyTo(ServerProfileDto server)
    {
        if (string.IsNullOrEmpty(server.SshGatewayId))
            server.SshGatewayId = SshGatewayId;

        if (string.IsNullOrEmpty(server.SshUsername))
            server.SshUsername = SshUsername;

        if (string.IsNullOrEmpty(server.SshKeyPath))
            server.SshKeyPath = SshKeyPath;

        if (server.SshPort == 22 && SshPort.HasValue)
            server.SshPort = SshPort.Value;
    }
}
