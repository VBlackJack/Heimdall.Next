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

using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public sealed class ArpTableParserTests
{
    private const string WindowsFixture = """
Interface: 192.168.1.10 --- 0x7
  Internet Address      Physical Address      Type
  192.168.1.1           aa-bb-cc-dd-ee-ff     dynamic
  192.168.1.20          11-22-33-44-55-66     dynamic
  192.168.1.30          77-88-99-aa-bb-cc     static
""";

    private const string LinuxFixture = """
IP address       HW type     Flags       HW address            Mask     Device
192.168.1.1      0x1         0x2         aa:bb:cc:dd:ee:ff     *        eth0
192.168.1.20     0x1         0x2         11:22:33:44:55:66     *        eth0
192.168.1.30     0x1         0x2         77:88:99:aa:bb:cc     *        wlan0
""";

    private const string MacOsFixture = """
? (192.168.1.1) at aa:bb:cc:dd:ee:ff on en0 ifscope [ethernet]
? (192.168.1.20) at 11:22:33:44:55:66 on en0 ifscope [ethernet]
? (192.168.1.30) at 77:88:99:aa:bb:cc on en1 ifscope [ethernet]
""";

    [Fact]
    public void ParseWindows_TypicalOutput_ReturnsExpectedRows()
    {
        var results = ArpTableParser.ParseWindows(WindowsFixture);

        AssertDictionary(
            results,
            ("192.168.1.1", "aa-bb-cc-dd-ee-ff"),
            ("192.168.1.20", "11-22-33-44-55-66"),
            ("192.168.1.30", "77-88-99-aa-bb-cc"));
    }

    [Fact]
    public void ParseLinuxProcNet_TypicalContent_ReturnsExpectedRows()
    {
        var results = ArpTableParser.ParseLinuxProcNet(LinuxFixture);

        AssertDictionary(
            results,
            ("192.168.1.1", "aa-bb-cc-dd-ee-ff"),
            ("192.168.1.20", "11-22-33-44-55-66"),
            ("192.168.1.30", "77-88-99-aa-bb-cc"));
    }

    [Fact]
    public void ParseMacOs_TypicalOutput_ReturnsExpectedRows()
    {
        var results = ArpTableParser.ParseMacOs(MacOsFixture);

        AssertDictionary(
            results,
            ("192.168.1.1", "aa-bb-cc-dd-ee-ff"),
            ("192.168.1.20", "11-22-33-44-55-66"),
            ("192.168.1.30", "77-88-99-aa-bb-cc"));
    }

    [Fact]
    public void ParseWindows_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ArpTableParser.ParseWindows(""));
    }

    [Fact]
    public void ParseLinuxProcNet_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ArpTableParser.ParseLinuxProcNet(""));
    }

    [Fact]
    public void ParseMacOs_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ArpTableParser.ParseMacOs(""));
    }

    [Fact]
    public void ParseWindows_MalformedLine_IsSkippedSilently()
    {
        var input = """
Interface: 192.168.1.10 --- 0x7
  Internet Address      Physical Address      Type
  not-an-ip             aa-bb-cc-dd-ee-ff     dynamic
  192.168.1.20          invalid-mac           dynamic
  192.168.1.30          77-88-99-aa-bb-cc     dynamic
""";

        var results = ArpTableParser.ParseWindows(input);

        AssertDictionary(results, ("192.168.1.30", "77-88-99-aa-bb-cc"));
    }

    [Fact]
    public void ParseLinuxProcNet_ZeroMac_IsSkippedSilently()
    {
        var input = """
IP address       HW type     Flags       HW address            Mask     Device
192.168.1.1      0x1         0x2         00:00:00:00:00:00     *        eth0
192.168.1.20     0x1         0x2         11:22:33:44:55:66     *        eth0
""";

        var results = ArpTableParser.ParseLinuxProcNet(input);

        AssertDictionary(results, ("192.168.1.20", "11-22-33-44-55-66"));
    }

    private static void AssertDictionary(
        IReadOnlyDictionary<string, string> actual,
        params (string Ip, string Mac)[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        Assert.Equal(
            expected.Select(x => x.Ip).OrderBy(x => x, StringComparer.Ordinal),
            actual.Keys.OrderBy(x => x, StringComparer.Ordinal));

        foreach (var (ip, mac) in expected)
        {
            Assert.True(actual.TryGetValue(ip, out var actualMac), $"Missing ARP entry for IP '{ip}'.");
            Assert.Equal(mac, actualMac);
        }
    }
}
