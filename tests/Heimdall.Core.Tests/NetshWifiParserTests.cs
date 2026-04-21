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

public sealed class NetshWifiParserTests
{
    private const string EnglishFixture = """
SSID 1 : CorpWifi
    Network type            : Infrastructure
    Authentication          : WPA2-Enterprise
    Encryption              : CCMP
    BSSID 1                 : aa:bb:cc:dd:ee:ff
         Signal             : 82%
         Radio type         : 802.11ax
         Channel            : 36
    BSSID 2                 : aa:bb:cc:dd:ee:00
         Signal             : 61%
         Radio type         : 802.11ac
         Channel            : 44

SSID 2 : GuestWifi
    Network type            : Infrastructure
    Authentication          : Open
    Encryption              : None
    BSSID 1                 : 11:22:33:44:55:66
         Signal             : 47%
         Radio type         : 802.11n
         Channel            : 11
""";

    private const string FrenchFixture = """
SSID 1 : Bureau
    Type de réseau          : Infrastructure
    Authentification        : WPA2-Personnel
    Chiffrement             : CCMP
    BSSID 1                 : aa:bb:cc:dd:ee:ff
         Signal             : 74%
         Type de radio      : 802.11ac
         Canal              : 44
""";

    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        Assert.Empty(NetshWifiParser.Parse(null));
    }

    [Fact]
    public void Parse_Whitespace_ReturnsEmpty()
    {
        Assert.Empty(NetshWifiParser.Parse(" \r\n\t "));
    }

    [Fact]
    public void Parse_EnglishFixture_ReturnsEntries()
    {
        var results = NetshWifiParser.Parse(EnglishFixture);

        Assert.Equal(3, results.Count);
        Assert.Equal("CorpWifi", results[0].Ssid);
        Assert.Equal("aa:bb:cc:dd:ee:ff", results[0].Bssid);
        Assert.Equal("82%", results[0].Signal);
        Assert.Equal(82, results[0].SignalValue);
        Assert.Equal("WPA2-Enterprise", results[0].Auth);
        Assert.Equal("CCMP", results[0].Encryption);
    }

    [Fact]
    public void Parse_FrenchFixture_ReturnsEntry()
    {
        var results = NetshWifiParser.Parse(FrenchFixture);

        Assert.Single(results);
        Assert.Equal("Bureau", results[0].Ssid);
        Assert.Equal("74%", results[0].Signal);
        Assert.Equal(74, results[0].SignalValue);
        Assert.Equal("44", results[0].Channel);
        Assert.Equal(string.Empty, results[0].RadioType);
    }

    [Fact]
    public void Parse_FlushesPreviousBssidOnNewBssid()
    {
        var input = """
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 : aa:aa:aa:aa:aa:aa
        Signal : 50%
        Channel : 1
    BSSID 2 : bb:bb:bb:bb:bb:bb
        Signal : 40%
        Channel : 6
""";

        var results = NetshWifiParser.Parse(input);

        Assert.Equal(2, results.Count);
        Assert.Equal("aa:aa:aa:aa:aa:aa", results[0].Bssid);
        Assert.Equal("bb:bb:bb:bb:bb:bb", results[1].Bssid);
    }

    [Fact]
    public void Parse_FlushesPreviousBssidOnNewSsid()
    {
        var input = """
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 : aa:aa:aa:aa:aa:aa
        Signal : 50%
        Channel : 1
SSID 2 : Guest
    Authentication : Open
    Encryption : None
    BSSID 1 : bb:bb:bb:bb:bb:bb
        Signal : 40%
        Channel : 6
""";

        var results = NetshWifiParser.Parse(input);

        Assert.Equal(2, results.Count);
        Assert.Equal("CorpWifi", results[0].Ssid);
        Assert.Equal("Guest", results[1].Ssid);
    }

    [Fact]
    public void Parse_IgnoresEmptyLinesAndCrLf()
    {
        var input = "SSID 1 : CorpWifi\r\n\r\n    Authentication : WPA2\r\n    Encryption : CCMP\r\n    BSSID 1 : aa:bb:cc:dd:ee:ff\r\n         Signal : 82%\r\n         Channel : 36\r\n";

        var results = NetshWifiParser.Parse(input);

        Assert.Single(results);
        Assert.Equal("82%", results[0].Signal);
    }

    [Fact]
    public void Parse_MissingSignalValue_UsesZero()
    {
        var input = """
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 : aa:bb:cc:dd:ee:ff
        Signal : ??
        Channel : 36
""";

        var results = NetshWifiParser.Parse(input);

        Assert.Single(results);
        Assert.Equal(0, results[0].SignalValue);
    }

    [Fact]
    public void Parse_BssidWithoutSignal_StillReturnsEntry()
    {
        var input = """
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 : aa:bb:cc:dd:ee:ff
""";

        var results = NetshWifiParser.Parse(input);

        Assert.Single(results);
        Assert.Equal(string.Empty, results[0].Signal);
        Assert.Equal(0, results[0].SignalValue);
    }

    [Fact]
    public void Parse_SsidWithoutBssid_ReturnsEmpty()
    {
        var input = """
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
""";

        Assert.Empty(NetshWifiParser.Parse(input));
    }

    [Fact]
    public void Parse_IgnoresNetworkTypeButPreservesTypeDeRHeuristic()
    {
        var input = """
SSID 1 : Bureau
    Type de réseau étendu : Infrastructure
    Authentification : WPA2
    Chiffrement : CCMP
    BSSID 1 : aa:bb:cc:dd:ee:ff
        Signal : 70%
        Canal : 6
""";

        var results = NetshWifiParser.Parse(input);

        Assert.Single(results);
        Assert.Equal("Bureau", results[0].Ssid);
    }

    [Fact]
    public void Parse_CaseInsensitiveFieldNames()
    {
        var input = """
ssid 1 : CorpWifi
    authentication : WPA2
    encryption : CCMP
    bssid 1 : aa:bb:cc:dd:ee:ff
        signal : 82%
        radio type : 802.11ax
        channel : 36
""";

        var results = NetshWifiParser.Parse(input);

        Assert.Single(results);
        Assert.Equal("802.11ax", results[0].RadioType);
    }

    [Fact]
    public void Parse_LinesWithoutColon_ReturnEmptyFieldValues()
    {
        var input = """
SSID 1 : CorpWifi
    Authentication
    Encryption : CCMP
    BSSID 1 : aa:bb:cc:dd:ee:ff
        Signal : 82%
        Channel : 36
""";

        var results = NetshWifiParser.Parse(input);

        Assert.Single(results);
        Assert.Equal(string.Empty, results[0].Auth);
    }

    [Fact]
    public void Parse_IgnoresNoiseBeforeFirstSsid()
    {
        var input = """
Interface name : Wi-Fi
There are 2 networks currently visible.
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 : aa:bb:cc:dd:ee:ff
        Signal : 82%
        Channel : 36
""";

        var results = NetshWifiParser.Parse(input);

        Assert.Single(results);
        Assert.Equal("CorpWifi", results[0].Ssid);
    }

    [Fact]
    public void Parse_SkipsEmptyBssidValues()
    {
        var input = """
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 :
        Signal : 82%
        Channel : 36
""";

        Assert.Empty(NetshWifiParser.Parse(input));
    }

    [Fact]
    public void Parse_PreservesAuthAndEncryptionAcrossMultipleBssids()
    {
        var input = """
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 : aa:bb:cc:dd:ee:ff
        Signal : 82%
        Channel : 36
    BSSID 2 : aa:bb:cc:dd:ee:00
        Signal : 61%
        Channel : 44
""";

        var results = NetshWifiParser.Parse(input);

        Assert.Equal(2, results.Count);
        Assert.All(results, entry =>
        {
            Assert.Equal("WPA2", entry.Auth);
            Assert.Equal("CCMP", entry.Encryption);
        });
    }

    [Fact]
    public void Parse_BssidWithoutColonValue_IsSkipped()
    {
        var input = """
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1
        Signal : 82%
        Channel : 36
""";

        Assert.Empty(NetshWifiParser.Parse(input));
    }

    [Fact]
    public void Parse_SignalWithSurroundingSpaces_IsTrimmedForNumericValue()
    {
        var input = """
SSID 1 : CorpWifi
    Authentication : WPA2
    Encryption : CCMP
    BSSID 1 : aa:bb:cc:dd:ee:ff
        Signal :   82%  
        Channel : 36
""";

        var results = NetshWifiParser.Parse(input);

        Assert.Single(results);
        Assert.Equal("82%", results[0].Signal.Trim());
        Assert.Equal(82, results[0].SignalValue);
    }
}
