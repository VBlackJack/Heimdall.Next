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

using System.Text;

namespace Heimdall.Core.Discovery;

/// <summary>
/// Generates Draw.io (diagrams.net) XML files from network scan snapshots
/// for visual network topology diagrams.
/// </summary>
public static class DrawIoExporter
{
    /// <summary>
    /// Generates a Draw.io XML string from a <see cref="NetworkScanSnapshot"/>.
    /// </summary>
    public static string Generate(NetworkScanSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<mxfile host=\"Heimdall.Next\">");
        sb.AppendLine($"  <diagram name=\"{EscapeXml(snapshot.Profile.Subnet)} - {snapshot.Timestamp:yyyy-MM-dd HH:mm}\">");
        sb.AppendLine("    <mxGraphModel dx=\"1200\" dy=\"800\" grid=\"1\" gridSize=\"10\">");
        sb.AppendLine("      <root>");
        sb.AppendLine("        <mxCell id=\"0\"/>");
        sb.AppendLine("        <mxCell id=\"1\" parent=\"0\"/>");

        var groups = snapshot.Hosts
            .GroupBy(h => h.PrimaryRole?.Role ?? "Unknown")
            .OrderBy(g => g.Key)
            .ToList();

        var cellId = 2;
        var groupX = 40;

        foreach (var group in groups)
        {
            var groupId = cellId++;
            var groupWidth = 200;
            var groupHeight = Math.Max(120, group.Count() * 90 + 40);

            sb.AppendLine($"        <mxCell id=\"{groupId}\" value=\"{EscapeXml(group.Key)}\" " +
                $"style=\"swimlane;startSize=30;fillColor={GetRoleColor(group.Key)};fontColor=#ffffff;rounded=1;\" " +
                $"vertex=\"1\" parent=\"1\">");
            sb.AppendLine($"          <mxGeometry x=\"{groupX}\" y=\"40\" width=\"{groupWidth}\" height=\"{groupHeight}\" as=\"geometry\"/>");
            sb.AppendLine("        </mxCell>");

            var groupY = 40;
            foreach (var host in group)
            {
                var nodeId = cellId++;
                var label = BuildNodeLabel(host);
                var style = GetNodeStyle(group.Key);

                sb.AppendLine($"        <mxCell id=\"{nodeId}\" value=\"{EscapeXml(label)}\" " +
                    $"style=\"{style}\" vertex=\"1\" parent=\"{groupId}\">");
                sb.AppendLine($"          <mxGeometry x=\"20\" y=\"{groupY}\" width=\"160\" height=\"70\" as=\"geometry\"/>");
                sb.AppendLine("        </mxCell>");

                groupY += 80;
            }

            groupX += 240;
        }

        sb.AppendLine("      </root>");
        sb.AppendLine("    </mxGraphModel>");
        sb.AppendLine("  </diagram>");
        sb.AppendLine("</mxfile>");

        return sb.ToString();
    }

    private static string BuildNodeLabel(HostScanResult host)
    {
        var sb = new StringBuilder();
        sb.Append(host.IpAddress);
        if (!string.IsNullOrEmpty(host.Hostname))
            sb.Append($"\n{host.Hostname}");

        var ports = string.Join(", ", host.Services.Where(s => s.IsOpen).Select(s => s.Port));
        if (!string.IsNullOrEmpty(ports))
            sb.Append($"\nPorts: {ports}");

        var tlsCert = host.Services.FirstOrDefault(s => s.Certificate is not null)?.Certificate;
        if (tlsCert is not null)
        {
            sb.Append(tlsCert.IsExpired ? "\nCert EXPIRED" : $"\nCert: {tlsCert.TlsVersion}");
        }

        return sb.ToString();
    }

    private static string GetRoleColor(string role) => role switch
    {
        "Active Directory" => "#1E40AF",
        "Web Server" => "#16A34A",
        var r when r.StartsWith("Database", StringComparison.Ordinal) => "#D97706",
        "Mail Server" => "#7C3AED",
        "Windows RDP" => "#2563EB",
        "SSH Server" => "#059669",
        "Network Equipment (SNMP)" => "#6B7280",
        _ => "#44475A"
    };

    private static string GetNodeStyle(string role) => role switch
    {
        "Active Directory" => "rounded=1;whiteSpace=wrap;fillColor=#1E40AF;fontColor=#ffffff;strokeColor=#1E3A8A;fontSize=10;align=left;spacingLeft=8;",
        "Web Server" => "rounded=1;whiteSpace=wrap;fillColor=#16A34A;fontColor=#ffffff;strokeColor=#15803D;fontSize=10;align=left;spacingLeft=8;",
        var r when r.StartsWith("Database", StringComparison.Ordinal) => "shape=cylinder3;whiteSpace=wrap;fillColor=#D97706;fontColor=#ffffff;strokeColor=#B45309;fontSize=10;size=8;",
        "Mail Server" => "rounded=1;whiteSpace=wrap;fillColor=#7C3AED;fontColor=#ffffff;strokeColor=#6D28D9;fontSize=10;align=left;spacingLeft=8;",
        _ => "rounded=1;whiteSpace=wrap;fillColor=#44475A;fontColor=#ffffff;strokeColor=#6272A4;fontSize=10;align=left;spacingLeft=8;"
    };

    private static string EscapeXml(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("\n", "&#xa;");
}
