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

public class OuiDatabaseTests
{
    [Theory]
    [InlineData("00:0C:29:AA:BB:CC", "VMware")]
    [InlineData("00-15-5D-11-22-33", "Microsoft Hyper-V")]
    [InlineData("B4FBE4112233", "Ubiquiti")]
    [InlineData("001132AABBCC", "Synology")]
    [InlineData("D4CA6D112233", "MikroTik")]
    [InlineData("5CCF7F112233", "Espressif (ESP8266/ESP32)")]
    [InlineData("B827EB112233", "Raspberry Pi")]
    public void LookupManufacturer_OriginalEntries_Recognized(string mac, string expected)
    {
        var result = OuiDatabase.LookupManufacturer(mac);
        Assert.NotNull(result);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("00:26:5A:11:22:33", "Cisco")]
    [InlineData("001C73112233", "Arista Networks")]
    [InlineData("E8D0FC112233", "Sagemcom (ISP Router)")]
    [InlineData("C8D015112233", "Konica Minolta")]
    [InlineData("000E8C112233", "Siemens")]
    [InlineData("AC1F6B112233", "Supermicro")]
    [InlineData("E09806112233", "Shelly")]
    [InlineData("7CF666112233", "Ring (Amazon)")]
    [InlineData("3C8D20112233", "LG Electronics")]
    [InlineData("FA163E112233", "Amazon AWS (ENI)")]
    public void LookupManufacturer_NewEntries_Recognized(string mac, string expected)
    {
        var result = OuiDatabase.LookupManufacturer(mac);
        Assert.NotNull(result);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("112233445566")]
    [InlineData("99-99-99-99-99-99")]
    public void LookupManufacturer_UnknownPrefix_ReturnsNull(string mac)
    {
        Assert.Null(OuiDatabase.LookupManufacturer(mac));
    }

    [Fact]
    public void LookupManufacturer_LocallyAdministeredMac_ReturnsRandomized()
    {
        // 0xAA has bit 1 set = locally administered (randomized MAC)
        var result = OuiDatabase.LookupManufacturer("AA:BB:CC:DD:EE:FF");
        Assert.Equal("Private (Randomized MAC)", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC")]
    public void LookupManufacturer_InvalidInput_ReturnsNull(string mac)
    {
        Assert.Null(OuiDatabase.LookupManufacturer(mac));
    }

    [Fact]
    public void LookupManufacturer_Null_ReturnsNull()
    {
        Assert.Null(OuiDatabase.LookupManufacturer(null!));
    }

    [Fact]
    public void LookupManufacturer_AcceptsAllFormats()
    {
        // Same OUI (VMware 000C29) in all three formats
        var colon = OuiDatabase.LookupManufacturer("00:0C:29:AA:BB:CC");
        var dash = OuiDatabase.LookupManufacturer("00-0C-29-AA-BB-CC");
        var raw = OuiDatabase.LookupManufacturer("000C29AABBCC");

        Assert.Equal("VMware", colon);
        Assert.Equal("VMware", dash);
        Assert.Equal("VMware", raw);
    }

    [Fact]
    public void LookupManufacturer_CaseInsensitive()
    {
        var upper = OuiDatabase.LookupManufacturer("00:0C:29:AA:BB:CC");
        var lower = OuiDatabase.LookupManufacturer("00:0c:29:aa:bb:cc");
        Assert.Equal(upper, lower);
    }
}
