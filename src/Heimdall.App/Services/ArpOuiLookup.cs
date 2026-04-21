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

namespace Heimdall.App.Services;

/// <summary>
/// Resolves common MAC OUI prefixes to vendor names for ARP monitor entries.
/// </summary>
internal static class ArpOuiLookup
{
    public static string Lookup(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac) || mac.Length < 8)
        {
            return "";
        }

        var prefix = mac.Replace("-", ":")[..8].ToUpperInvariant();
        return OuiVendors.GetValueOrDefault(prefix, "");
    }

    /// <summary>
    /// Top OUI vendor prefixes for quick identification.
    /// </summary>
    private static readonly Dictionary<string, string> OuiVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        // Virtual / Hypervisors
        ["00:50:56"] = "VMware",
        ["00:0C:29"] = "VMware",
        ["00:05:69"] = "VMware",
        ["08:00:27"] = "VirtualBox",
        ["0A:00:27"] = "VirtualBox",
        ["52:54:00"] = "QEMU/KVM",
        ["00:15:5D"] = "Hyper-V",
        ["00:16:3E"] = "Xen",
        // Apple
        ["3C:22:FB"] = "Apple",
        ["A4:83:E7"] = "Apple",
        ["F0:18:98"] = "Apple",
        ["AC:DE:48"] = "Apple",
        ["14:7D:DA"] = "Apple",
        ["F8:FF:C2"] = "Apple",
        ["78:7B:8A"] = "Apple",
        // Raspberry Pi
        ["DC:A6:32"] = "Raspberry Pi",
        ["B8:27:EB"] = "Raspberry Pi",
        ["E4:5F:01"] = "Raspberry Pi",
        ["28:CD:C1"] = "Raspberry Pi",
        ["D8:3A:DD"] = "Raspberry Pi",
        // Intel
        ["00:1B:21"] = "Intel",
        ["3C:97:0E"] = "Intel",
        ["A4:C3:F0"] = "Intel",
        ["48:51:B7"] = "Intel",
        ["F8:F2:1E"] = "Intel",
        // Cisco
        ["00:1A:A1"] = "Cisco",
        ["00:26:0B"] = "Cisco",
        ["00:50:0F"] = "Cisco",
        ["58:AC:78"] = "Cisco",
        ["F4:CF:E2"] = "Cisco",
        // HP / HPE
        ["00:1E:0B"] = "HP",
        ["3C:D9:2B"] = "HP",
        ["94:57:A5"] = "HP",
        ["B4:B5:2F"] = "HP",
        // Dell
        ["00:14:22"] = "Dell",
        ["18:03:73"] = "Dell",
        ["F8:DB:88"] = "Dell",
        ["B0:83:FE"] = "Dell",
        // Lenovo
        ["00:06:1B"] = "Lenovo",
        ["28:D2:44"] = "Lenovo",
        ["E8:2A:44"] = "Lenovo",
        // Realtek
        ["00:E0:4C"] = "Realtek",
        ["52:54:AB"] = "Realtek",
        // TP-Link
        ["50:C7:BF"] = "TP-Link",
        ["EC:08:6B"] = "TP-Link",
        ["60:32:B1"] = "TP-Link",
        ["14:EB:B6"] = "TP-Link",
        // Ubiquiti
        ["04:18:D6"] = "Ubiquiti",
        ["24:A4:3C"] = "Ubiquiti",
        ["68:D7:9A"] = "Ubiquiti",
        ["FC:EC:DA"] = "Ubiquiti",
        // Netgear
        ["00:26:F2"] = "Netgear",
        ["28:C6:8E"] = "Netgear",
        ["A4:2B:8C"] = "Netgear",
        // ASUS
        ["00:1A:92"] = "ASUS",
        ["04:D4:C4"] = "ASUS",
        ["2C:FD:A1"] = "ASUS",
        // Synology
        ["00:11:32"] = "Synology",
        // QNAP
        ["00:08:9B"] = "QNAP",
        // Aruba / HPE Aruba
        ["00:0B:86"] = "Aruba",
        ["24:DE:C6"] = "Aruba",
        // Juniper
        ["00:05:85"] = "Juniper",
        ["88:E0:F3"] = "Juniper",
        // MikroTik
        ["00:0C:42"] = "MikroTik",
        ["48:8F:5A"] = "MikroTik",
        // Fortinet
        ["00:09:0F"] = "Fortinet",
        ["70:4C:A5"] = "Fortinet",
        // Samsung
        ["00:16:32"] = "Samsung",
        ["8C:F5:A3"] = "Samsung",
        ["50:A4:C8"] = "Samsung",
        // Huawei
        ["00:E0:FC"] = "Huawei",
        ["48:46:FB"] = "Huawei",
        // Amazon (Echo, Ring, etc.)
        ["FC:65:DE"] = "Amazon",
        ["A4:08:01"] = "Amazon",
        // Google
        ["F4:F5:D8"] = "Google",
        ["3C:5A:B4"] = "Google",
        // Microsoft
        ["00:50:F2"] = "Microsoft",
        ["28:18:78"] = "Microsoft",
        // Sonos
        ["00:0E:58"] = "Sonos",
        ["B8:E9:37"] = "Sonos",
        // Broadcom
        ["00:10:18"] = "Broadcom",
        // D-Link
        ["00:1C:F0"] = "D-Link",
        ["28:10:7B"] = "D-Link",
    };
}
