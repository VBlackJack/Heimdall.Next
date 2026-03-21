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

namespace Heimdall.Core.Discovery;

/// <summary>
/// Heuristic role classification based on open port patterns.
/// Maps common port combinations to server roles (e.g., Active Directory,
/// Web Server, Database) with confidence scoring.
/// </summary>
public static class RoleClassifier
{
    private static readonly RoleDefinition[] Definitions =
    [
        new("Active Directory", [53, 88, 389], [636, 445, 135, 464, 3268, 3269], 95),
        new("DNS Server", [53], [], 60),
        new("Web Server", [80, 443], [8080, 8443, 9090], 80),
        new("Database (MySQL)", [3306], [33060], 85),
        new("Database (PostgreSQL)", [5432], [], 85),
        new("Database (MSSQL)", [1433], [1434], 85),
        new("Database (Oracle)", [1521], [], 85),
        new("Database (MongoDB)", [27017], [27018, 27019], 85),
        new("Mail Server", [25], [587, 465, 993, 995, 143, 110], 80),
        new("SSH Server", [22], [], 50),
        new("Windows RDP", [3389], [445, 135, 139], 70),
        new("VNC Server", [5900], [5901, 5902], 65),
        new("Proxy/Load Balancer", [8080], [8443, 3128, 80, 443], 55),
        new("Network Equipment (SNMP)", [161], [162], 60),
        new("Syslog Server", [514], [6514], 55),
        new("DHCP Server", [67], [68], 55),
        new("FTP Server", [21], [990], 60),
        new("Redis", [6379], [], 75),
        new("Elasticsearch", [9200], [9300], 75),
        new("Docker/Container Host", [2375], [2376], 60),
        new("Kubernetes API", [6443], [10250], 70),
    ];

    /// <summary>
    /// Classifies a host based on its open ports, returning all matching roles
    /// sorted by descending confidence.
    /// </summary>
    public static List<RoleMatch> Classify(IReadOnlyList<int> openPorts)
    {
        var matches = new List<RoleMatch>();
        var portSet = new HashSet<int>(openPorts);

        foreach (var def in Definitions)
        {
            var requiredHits = def.RequiredPorts.Count(p => portSet.Contains(p));
            if (requiredHits == 0) continue;

            var optionalHits = def.OptionalPorts.Count(p => portSet.Contains(p));

            var requiredCoverage = (double)requiredHits / def.RequiredPorts.Length;
            var confidence = (int)(def.BaseConfidence * requiredCoverage);
            if (optionalHits > 0) confidence += optionalHits * 5;
            confidence = Math.Min(confidence, 99);

            if (requiredCoverage < 1.0 && confidence < 40) continue;

            var evidence = new List<string>();
            foreach (var p in def.RequiredPorts.Where(p => portSet.Contains(p)))
                evidence.Add($"port:{p} ({GetPortServiceName(p)})");
            foreach (var p in def.OptionalPorts.Where(p => portSet.Contains(p)))
                evidence.Add($"port:{p} ({GetPortServiceName(p)}) [optional]");

            matches.Add(new RoleMatch(def.RoleName, confidence, [.. evidence]));
        }

        return [.. matches.OrderByDescending(m => m.Confidence)];
    }

    /// <summary>
    /// Returns a human-readable service name for a well-known port number.
    /// </summary>
    internal static string GetPortServiceName(int port) => port switch
    {
        21 => "FTP", 22 => "SSH", 25 => "SMTP", 53 => "DNS",
        67 => "DHCP", 68 => "DHCP", 80 => "HTTP", 88 => "Kerberos",
        110 => "POP3", 135 => "RPC", 139 => "NetBIOS", 143 => "IMAP",
        161 => "SNMP", 162 => "SNMP-Trap", 389 => "LDAP", 443 => "HTTPS",
        445 => "SMB", 464 => "Kerberos-Change", 465 => "SMTPS",
        514 => "Syslog", 587 => "SMTP-Submission", 636 => "LDAPS",
        993 => "IMAPS", 995 => "POP3S", 990 => "FTPS",
        1433 => "MSSQL", 1434 => "MSSQL-Browser", 1521 => "Oracle",
        2375 => "Docker", 2376 => "Docker-TLS",
        3128 => "Squid", 3268 => "Global-Catalog", 3269 => "GC-SSL",
        3306 => "MySQL", 3389 => "RDP", 5432 => "PostgreSQL",
        5900 => "VNC", 6379 => "Redis", 6443 => "K8s-API",
        6514 => "Syslog-TLS", 8080 => "HTTP-Alt", 8443 => "HTTPS-Alt",
        9090 => "Web-Console", 9200 => "Elasticsearch", 9300 => "ES-Transport",
        10250 => "Kubelet", 27017 => "MongoDB",
        27018 => "MongoDB-Shard", 27019 => "MongoDB-Config",
        33060 => "MySQL-X",
        _ => $"Port-{port}"
    };

    private record RoleDefinition(
        string RoleName,
        int[] RequiredPorts,
        int[] OptionalPorts,
        int BaseConfidence);
}
