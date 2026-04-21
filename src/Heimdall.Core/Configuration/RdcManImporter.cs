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

using System.Xml.Linq;
using Heimdall.Core.Models;

namespace Heimdall.Core.Configuration;

/// <summary>
/// Parses Microsoft Remote Desktop Connection Manager (.rdg) files into
/// <see cref="ServerProfileDto"/> instances. Supports schema v1 (RDCMan 2.2)
/// and v3 (RDCMan 2.7+).
/// </summary>
public static class RdcManImporter
{
    public static ImportResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var result = new ImportResult();

        try
        {
            var xmlSettings = new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(content), xmlSettings);
            var doc = XDocument.Load(reader);
            var root = doc.Root;
            if (root is null) return result;

            // RDCMan files have <file> or <RDCMan> as root
            var fileNodes = root.Name.LocalName == "file"
                ? [root]
                : root.Descendants().Where(e => e.Name.LocalName == "file").ToArray();

            foreach (var fileNode in fileNodes)
            {
                ParseGroup(fileNode, "", result);
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"XML parse error: {ex.Message}");
        }

        return result;
    }

    private static void ParseGroup(XElement element, string groupPath, ImportResult result)
    {
        var groupName = El(element, "name");
        var currentPath = string.IsNullOrWhiteSpace(groupPath)
            ? (groupName ?? "")
            : (string.IsNullOrWhiteSpace(groupName) ? groupPath : $"{groupPath}/{groupName}");

        // Parse servers in this group
        foreach (var server in element.Elements().Where(e => e.Name.LocalName == "server"))
        {
            try
            {
                var dto = ParseServer(server, currentPath, element);
                if (dto is not null)
                    result.Servers.Add(dto);
            }
            catch (Exception ex)
            {
                var name = El(server, "name") ?? El(server, "displayName") ?? "unknown";
                result.Warnings.Add($"{name}: {ex.Message}");
            }
        }

        // Recurse into sub-groups
        foreach (var group in element.Elements().Where(e => e.Name.LocalName == "group"))
        {
            ParseGroup(group, currentPath, result);
        }
    }

    private static ServerProfileDto? ParseServer(XElement server, string groupPath, XElement parentGroup)
    {
        var name = El(server, "displayName") ?? El(server, "name");
        if (string.IsNullOrWhiteSpace(name)) return null;

        var host = El(server, "name") ?? name;

        var dto = new ServerProfileDto
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = name,
            Origin = ProfileOrigin.ImportRdcMan,
            RemoteServer = host,
            RemotePort = DefaultPorts.Rdp,
            ConnectionType = "RDP",
            RdpMode = "Embedded",
            UseDirectConnection = true,
            Group = string.IsNullOrWhiteSpace(groupPath) ? null : groupPath,
        };

        // Server-level properties
        var serverProps = server.Elements().FirstOrDefault(e => e.Name.LocalName == "properties");

        // Try to get credentials (server > group > file level)
        var creds = FindElement(server, "logonCredentials")
                    ?? FindElement(parentGroup, "logonCredentials");

        if (creds is not null)
        {
            var user = El(creds, "userName");
            var domain = El(creds, "domain");

            if (!string.IsNullOrWhiteSpace(user))
            {
                dto.RdpUsername = !string.IsNullOrWhiteSpace(domain)
                    ? $"{user}@{domain}"
                    : user;
            }
        }

        // Connection settings
        var connSettings = FindElement(server, "connectionSettings")
                           ?? FindElement(parentGroup, "connectionSettings");

        if (connSettings is not null)
        {
            if (int.TryParse(El(connSettings, "port"), out var port) && port > 0)
                dto.RemotePort = port;
        }

        // Gateway settings
        var gwSettings = FindElement(server, "gatewaySettings");
        if (gwSettings is not null)
        {
            var gwName = El(gwSettings, "hostName");
            if (!string.IsNullOrWhiteSpace(gwName))
                dto.RdpGateway = gwName;
        }

        return dto;
    }

    private static XElement? FindElement(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string? El(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value?.Trim();

    public sealed class ImportResult
    {
        public List<ServerProfileDto> Servers { get; } = [];
        public List<string> Warnings { get; } = [];
    }
}
