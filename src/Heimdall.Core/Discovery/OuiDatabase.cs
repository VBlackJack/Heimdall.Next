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
/// Embedded OUI (Organizationally Unique Identifier) database for MAC address
/// manufacturer lookup. Contains common prefixes covering enterprise, network,
/// IoT, and consumer devices.
/// </summary>
public static class OuiDatabase
{
    /// <summary>
    /// Looks up the manufacturer name from a MAC address.
    /// Accepts formats: "AA:BB:CC:DD:EE:FF", "AA-BB-CC-DD-EE-FF", "AABBCCDDEEFF".
    /// </summary>
    public static string? LookupManufacturer(string macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress)) return null;

        // Normalize to uppercase, remove separators, take first 6 chars (OUI)
        var normalized = macAddress.ToUpperInvariant()
            .Replace(":", "").Replace("-", "").Replace(".", "");
        if (normalized.Length < 6) return null;
        var oui = normalized[..6];

        return OuiTable.TryGetValue(oui, out var manufacturer) ? manufacturer : null;
    }

    private static readonly Dictionary<string, string> OuiTable = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── VMware / Hypervisors ─────────────────────────────────────────
        ["000C29"] = "VMware",
        ["005056"] = "VMware",
        ["000569"] = "VMware",
        ["00155D"] = "Microsoft Hyper-V",
        ["080027"] = "Oracle VirtualBox",
        ["525400"] = "KVM/QEMU",

        // ── Network Equipment ────────────────────────────────────────────
        ["B4FBE4"] = "Ubiquiti",
        ["245A4C"] = "Ubiquiti",
        ["F09FC2"] = "Ubiquiti",
        ["788A20"] = "Ubiquiti",
        ["000F66"] = "Cisco",
        ["0050BA"] = "D-Link",
        ["001E58"] = "D-Link",
        ["C0A0BB"] = "D-Link",
        ["AC84C6"] = "TP-Link",
        ["50C7BF"] = "TP-Link",
        ["E894F6"] = "TP-Link",
        ["001F33"] = "Netgear",
        ["A42B8C"] = "Netgear",
        ["C43DC7"] = "Netgear",
        ["0024B2"] = "Netgear",
        ["207918"] = "Juniper",
        ["001BED"] = "Juniper",
        ["406C8F"] = "Apple",
        ["A4B197"] = "Apple",
        ["3C15C2"] = "Apple",
        ["D8CF9C"] = "Apple",
        ["000D93"] = "Apple",
        ["00A040"] = "Apple",
        ["000A27"] = "Apple",
        ["009333"] = "Apple",

        // ── NAS ──────────────────────────────────────────────────────────
        ["001132"] = "Synology",
        ["00089B"] = "ICP Electronics (QNAP)",
        ["246511"] = "QNAP",
        ["0011D8"] = "Asustor",
        ["00D0B7"] = "Intel (NAS/Compute)",

        // ── Cameras ──────────────────────────────────────────────────────
        ["70B3D5"] = "Hikvision",
        ["C0568D"] = "Hikvision",
        ["3CEF8C"] = "Dahua",
        ["A0BD1D"] = "Dahua",
        ["ACCC8E"] = "AXIS Communications",
        ["001A07"] = "AXIS Communications",
        ["0002D1"] = "Vivotek",
        ["000E8F"] = "Foscam",
        ["B0C554"] = "Reolink",

        // ── Printers ─────────────────────────────────────────────────────
        ["002590"] = "HP (Printer)",
        ["3C2AF4"] = "HP (Printer)",
        ["00215A"] = "HP Enterprise",
        ["001BA9"] = "Brother",
        ["0001E6"] = "Hewlett-Packard",
        ["000085"] = "Canon",
        ["001599"] = "Samsung Electronics",
        ["000400"] = "Lexmark",
        ["00137F"] = "Cisco-Linksys",
        ["001E0B"] = "Hewlett-Packard",

        // ── IoT / Smart Home ─────────────────────────────────────────────
        ["001788"] = "Philips Hue",
        ["ECB5FA"] = "Philips Hue",
        ["B8D7AF"] = "Murata (IoT modules)",
        ["5CCF7F"] = "Espressif (ESP8266/ESP32)",
        ["240AC4"] = "Espressif",
        ["A020A6"] = "Espressif",
        ["40F520"] = "ESPHome",
        ["B827EB"] = "Raspberry Pi",
        ["DCA632"] = "Raspberry Pi",
        ["E45F01"] = "Raspberry Pi",
        ["D83ADD"] = "Raspberry Pi",

        // ── Smartphones / Tablets ────────────────────────────────────────
        ["286C07"] = "Xiaomi",
        ["7811DC"] = "Xiaomi",
        ["643415"] = "Xiaomi",
        ["9C2EA1"] = "Samsung",
        ["B47443"] = "Samsung",
        ["D0176A"] = "Samsung",
        ["3C5AB4"] = "Google",
        ["F4F5D8"] = "Google",
        ["54608B"] = "Google Nest",
        ["A4771A"] = "Google Nest",
        ["7C2EBD"] = "Google",

        // ── Server Hardware ──────────────────────────────────────────────
        ["001E67"] = "Intel AMT (iDRAC/iLO)",
        ["D4AE52"] = "Dell",
        ["B083FE"] = "Dell",
        ["246E96"] = "Dell",
        ["002655"] = "Hewlett-Packard",
        ["3C4A92"] = "Hewlett-Packard",
        ["94B866"] = "Hewlett-Packard Enterprise",
        ["D89EF3"] = "Lenovo",

        // ── Firewalls / Security ─────────────────────────────────────────
        ["001A8C"] = "Sophos",
        ["002615"] = "Fortinet",
        ["00163E"] = "Xensource (Citrix)",

        // ── Audio / Media ────────────────────────────────────────────────
        ["B8E937"] = "Sonos",
        ["5CDAD4"] = "Sonos",
        ["949359"] = "Sonos",
        ["7071BC"] = "Sonos",
        ["48D6D5"] = "Google Chromecast",

        // ── AVM / ISP boxes ──────────────────────────────────────────────
        ["3810D5"] = "AVM (Fritz!Box)",
        ["C80E14"] = "AVM (Fritz!Box)",
        ["B0487A"] = "AVM (Fritz!Box)",
        ["2C3AE8"] = "Cisco Meraki",
        ["0018FE"] = "Hewlett-Packard",

        // ── MikroTik ─────────────────────────────────────────────────────
        ["D4CA6D"] = "MikroTik",
        ["6C3B6B"] = "MikroTik",
        ["E48D8C"] = "MikroTik",
        ["48A98A"] = "MikroTik",
        ["C4AD34"] = "MikroTik",
    };
}
