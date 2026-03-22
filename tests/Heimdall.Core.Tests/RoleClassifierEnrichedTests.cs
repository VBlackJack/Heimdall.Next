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

using Heimdall.Core.Discovery;

namespace Heimdall.Core.Tests;

public class RoleClassifierEnrichedTests
{
    [Fact]
    public void ClassifyEnriched_SnmpCisco_BoostsRouterConfidence()
    {
        // A host with SNMP port open + Cisco sysDescr
        var ports = new List<int> { 22, 80, 161 };
        var banners = new List<string?> { null, null, null };
        var snmp = new SnmpInfo("Cisco IOS Software, C2960 Version 15.2(2)", "CORE-SW-01", "Server Room");

        var roles = RoleClassifier.ClassifyEnriched(ports, banners, null, null, null,
            snmp, null, null);

        Assert.NotEmpty(roles);
        var networkRole = roles.FirstOrDefault(r =>
            r.Role.Contains("Network", StringComparison.OrdinalIgnoreCase) ||
            r.Role.Contains("Router", StringComparison.OrdinalIgnoreCase) ||
            r.Role.Contains("Switch", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(networkRole);
        Assert.Contains(networkRole.Evidence, e => e.Contains("snmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClassifyEnriched_MdnsAirplay_CreatesAppleRole()
    {
        var ports = new List<int> { 80 };
        var banners = new List<string?> { null };
        var mdns = new List<string> { "AirPlay", "Apple Companion" };

        var roles = RoleClassifier.ClassifyEnriched(ports, banners, null, null, null,
            null, mdns, null);

        Assert.Contains(roles, r =>
            r.Role.Contains("Apple", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClassifyEnriched_NetBiosDomain_BoostsWindowsRoles()
    {
        // A Windows RDP host with NetBIOS domain
        var ports = new List<int> { 3389, 445, 135 };
        var banners = new List<string?> { null, null, null };

        var rolesWithout = RoleClassifier.ClassifyEnriched(ports, banners, null, "PC-01", null,
            null, null, null);
        var rolesWith = RoleClassifier.ClassifyEnriched(ports, banners, null, "PC-01", "CONTOSO",
            null, null, null);

        var rdpWithout = rolesWithout.FirstOrDefault(r => r.Role.Contains("RDP", StringComparison.OrdinalIgnoreCase));
        var rdpWith = rolesWith.FirstOrDefault(r => r.Role.Contains("RDP", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(rdpWithout);
        Assert.NotNull(rdpWith);
        Assert.True(rdpWith.Confidence >= rdpWithout.Confidence);
    }

    [Fact]
    public void ClassifyEnriched_HttpNtlm_BoostsWindowsConfidence()
    {
        var ports = new List<int> { 80, 443, 3389 };
        var banners = new List<string?> { "HTTP/1.1 200 OK\r\nServer: Microsoft-IIS/10.0", null, null };
        var headers = new Dictionary<string, string>
        {
            ["WWW-Authenticate"] = "NTLM"
        };

        var roles = RoleClassifier.ClassifyEnriched(ports, banners, null, null, null,
            null, null, headers);

        var windowsRole = roles.FirstOrDefault(r =>
            r.Role.Contains("Windows", StringComparison.OrdinalIgnoreCase) ||
            r.Role.Contains("IIS", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(windowsRole);
    }

    [Fact]
    public void ClassifyEnriched_SnmpPrinter_BoostsPrinterRole()
    {
        var ports = new List<int> { 80, 9100, 631 };
        var banners = new List<string?> { null, null, null };
        var snmp = new SnmpInfo("HP ETHERNET MULTI-ENVIRONMENT", "HP-LJ-4250", "3rd Floor");

        var roles = RoleClassifier.ClassifyEnriched(ports, banners, null, null, null,
            snmp, null, null);

        var printerRole = roles.FirstOrDefault(r =>
            r.Role.Contains("Printer", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(printerRole);
        Assert.True(printerRole.Confidence >= 80);
    }

    [Fact]
    public void ClassifyEnriched_MdnsPrinter_CreatesOrBoostsPrinterRole()
    {
        var ports = new List<int> { 80, 631, 9100 };
        var banners = new List<string?> { null, null, null };
        var mdns = new List<string> { "IPP Printer", "Scanner" };

        var roles = RoleClassifier.ClassifyEnriched(ports, banners, null, null, null,
            null, mdns, null);

        var printerRole = roles.FirstOrDefault(r =>
            r.Role.Contains("Printer", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(printerRole);
        Assert.Contains(printerRole.Evidence, e => e.Contains("mdns", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClassifyEnriched_SnmpUps_CreatesUpsRole()
    {
        var ports = new List<int> { 161, 80 };
        var banners = new List<string?> { null, null };
        var snmp = new SnmpInfo("APC Web/SNMP Management Card", "UPS-01", "Server Room");

        var roles = RoleClassifier.ClassifyEnriched(ports, banners, null, null, null,
            snmp, null, null);

        var upsRole = roles.FirstOrDefault(r =>
            r.Role.Contains("UPS", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(upsRole);
    }
}
