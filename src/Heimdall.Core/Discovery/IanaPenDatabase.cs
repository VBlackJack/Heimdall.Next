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

namespace Heimdall.Core.Discovery;

/// <summary>
/// Maps SNMP sysObjectID enterprise numbers to vendor names using
/// IANA Private Enterprise Numbers (PEN) registry.
/// OID format: 1.3.6.1.4.1.{PEN}.{product-specific}
/// </summary>
public static class IanaPenDatabase
{
    private static readonly Regex SysObjectIdRegex = new(
        @"^1\.3\.6\.1\.4\.1\.(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the vendor name from a sysObjectID OID string.
    /// Returns null if the OID is not in the enterprise subtree or PEN is unknown.
    /// </summary>
    public static string? LookupVendor(string? sysObjectId)
    {
        if (string.IsNullOrEmpty(sysObjectId)) return null;
        var match = SysObjectIdRegex.Match(sysObjectId);
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].Value, out var pen)) return null;
        return PenTable.TryGetValue(pen, out var vendor) ? vendor : null;
    }

    /// <summary>
    /// Extracts the raw PEN number from a sysObjectID string.
    /// </summary>
    public static int? ExtractPen(string? sysObjectId)
    {
        if (string.IsNullOrEmpty(sysObjectId)) return null;
        var match = SysObjectIdRegex.Match(sysObjectId);
        if (!match.Success) return null;
        return int.TryParse(match.Groups[1].Value, out var pen) ? pen : null;
    }

    private static readonly Dictionary<int, string> PenTable = new()
    {
        // Network equipment
        [9] = "Cisco",
        [11] = "Hewlett-Packard",
        [43] = "3Com",
        [45] = "Sytek",
        [171] = "D-Link",
        [207] = "Allied Telesis",
        [232] = "Compaq",
        [318] = "APC (Schneider Electric)",
        [343] = "Intel",
        [674] = "Dell",
        [872] = "AVM (Fritz!Box)",
        [1038] = "Sagemcom",
        [1368] = "Orange",
        [1588] = "Brocade",
        [1916] = "Extreme Networks",
        [2011] = "Huawei",
        [2272] = "Nortel",
        [2385] = "Sharp",
        [2435] = "Brother",
        [2636] = "Juniper Networks",
        [3076] = "Cisco (VPN)",
        [3224] = "Juniper (NetScreen)",
        [3375] = "F5 Networks",
        [3417] = "Blue Coat / Symantec",
        [4526] = "Netgear",
        [5624] = "Enterasys",
        [5771] = "Cisco Meraki",
        [6141] = "Calix",
        [6486] = "Alcatel-Lucent",
        [6574] = "Synology",
        [6876] = "VMware",
        [7779] = "Ricoh",
        [8072] = "Net-SNMP (Linux/BSD)",

        // Security appliances
        [9694] = "Trend Micro",
        [12356] = "Fortinet",
        [14988] = "MikroTik",
        [25461] = "Palo Alto Networks",
        [30803] = "SonicWall",

        // Storage / NAS
        [24681] = "QNAP",
        [55062] = "QNAP Systems",

        // Surveillance
        [37496] = "Zhejiang Dahua",
        [39165] = "Hangzhou Hikvision",

        // IoT / Consumer
        [41112] = "Ubiquiti Networks",

        // Printers
        [367] = "Ricoh",
        [11] = "HP (Printer)",
        [253] = "Xerox",
        [641] = "Lexmark",
        [1347] = "Epson",
        [2435] = "Brother",
        [2543] = "Canon",
        [18334] = "Kyocera",
        [27724] = "Konica Minolta",

        // UPS / Power
        [318] = "APC (Schneider Electric)",
        [534] = "Eaton",
        [476] = "Liebert (Vertiv)",

        // Microsoft / OS
        [311] = "Microsoft",

        // Virtualization
        [6876] = "VMware",

        // Monitoring
        [8072] = "Net-SNMP",
    };
}
