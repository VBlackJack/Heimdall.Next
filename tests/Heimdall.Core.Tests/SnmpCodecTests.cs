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

public class SnmpCodecTests
{
    [Fact]
    public void Asn1EncodeLength_Short_ReturnsSingleByte()
    {
        var result = SnmpCodec.Asn1EncodeLength(42);
        Assert.Equal([42], result);
    }

    [Fact]
    public void Asn1EncodeLength_Medium_ReturnsTwoBytes()
    {
        var result = SnmpCodec.Asn1EncodeLength(200);
        Assert.Equal([0x81, 200], result);
    }

    [Fact]
    public void Asn1EncodeLength_Long_ReturnsThreeBytes()
    {
        var result = SnmpCodec.Asn1EncodeLength(300);
        Assert.Equal([0x82, 0x01, 0x2C], result);
    }

    [Fact]
    public void Asn1Wrap_ProducesValidTlv()
    {
        var result = SnmpCodec.Asn1Wrap(0x04, [0x41, 0x42]);
        Assert.Equal([0x04, 0x02, 0x41, 0x42], result);
    }

    [Fact]
    public void Asn1ReadTlv_ParsesSimpleTlv()
    {
        var data = new byte[] { 0x02, 0x01, 0x05 };
        var (totalLength, value) = SnmpCodec.Asn1ReadTlv(data, 0);
        Assert.Equal(3, totalLength);
        Assert.NotNull(value);
        Assert.Equal([0x05], value);
    }

    [Fact]
    public void Asn1ReadTlv_EmptyData_ReturnsNull()
    {
        var (totalLength, value) = SnmpCodec.Asn1ReadTlv([], 0);
        Assert.Equal(0, totalLength);
        Assert.Null(value);
    }

    [Fact]
    public void Asn1EncodeOid_SystemOid_RoundTrips()
    {
        var encoded = SnmpCodec.Asn1EncodeOid([1, 3, 6, 1, 2, 1, 1]);
        var (_, content) = SnmpCodec.Asn1ReadTlv(encoded, 0);
        Assert.NotNull(content);
        Assert.Equal("1.3.6.1.2.1.1", SnmpCodec.Asn1DecodeOid(content));
    }

    [Fact]
    public void Asn1EncodeOid_LargeComponent_RoundTrips()
    {
        var encoded = SnmpCodec.Asn1EncodeOid([1, 3, 6, 1, 4, 1, 9, 1, 1745]);
        var (_, content) = SnmpCodec.Asn1ReadTlv(encoded, 0);
        Assert.NotNull(content);
        Assert.Equal("1.3.6.1.4.1.9.1.1745", SnmpCodec.Asn1DecodeOid(content));
    }

    [Fact]
    public void Asn1EncodeInteger_Zero_EncodesSingleByte()
    {
        var result = SnmpCodec.Asn1EncodeInteger(0);
        Assert.Equal([0x02, 0x01, 0x00], result);
    }

    [Fact]
    public void Asn1EncodeInteger_PositiveValue_Encodes()
    {
        var result = SnmpCodec.Asn1EncodeInteger(42);
        Assert.Equal([0x02, 0x01, 0x2A], result);
    }

    [Fact]
    public void Asn1EncodeInteger_HighBitSet_AddsLeadingZero()
    {
        var result = SnmpCodec.Asn1EncodeInteger(128);
        Assert.Equal([0x02, 0x02, 0x00, 0x80], result);
    }

    [Fact]
    public void Asn1DecodeSignedInt_Positive_Decodes()
    {
        Assert.Equal(42, SnmpCodec.Asn1DecodeSignedInt([0x2A]));
    }

    [Fact]
    public void Asn1DecodeSignedInt_Negative_Decodes()
    {
        Assert.Equal(-1, SnmpCodec.Asn1DecodeSignedInt([0xFF]));
    }

    [Fact]
    public void Asn1DecodeSignedInt_Empty_ReturnsZero()
    {
        Assert.Equal(0, SnmpCodec.Asn1DecodeSignedInt([]));
    }

    [Fact]
    public void Asn1DecodeUnsignedInt_SingleByte_Decodes()
    {
        Assert.Equal(200L, SnmpCodec.Asn1DecodeUnsignedInt([200]));
    }

    [Fact]
    public void Asn1DecodeUnsignedInt_MultipleBytes_Decodes()
    {
        Assert.Equal(256L, SnmpCodec.Asn1DecodeUnsignedInt([1, 0]));
    }

    [Theory]
    [InlineData(0x02, "INTEGER")]
    [InlineData(0x04, "STRING")]
    [InlineData(0x05, "NULL")]
    [InlineData(0x06, "OID")]
    [InlineData(0x40, "IpAddress")]
    [InlineData(0x41, "Counter32")]
    [InlineData(0x42, "Gauge32")]
    [InlineData(0x43, "TimeTicks")]
    [InlineData(0x80, "noSuchObject")]
    [InlineData(0x81, "noSuchInstance")]
    [InlineData(0x82, "endOfMibView")]
    public void GetTagName_ReturnsExpected(byte tag, string expected)
    {
        Assert.Equal(expected, SnmpCodec.GetTagName(tag));
    }

    [Fact]
    public void GetTagName_UnknownTag_ReturnsHex()
    {
        Assert.Equal("0xFF", SnmpCodec.GetTagName(0xFF));
    }

    [Fact]
    public void DecodeSnmpValue_Integer_Decodes()
    {
        var (type, value) = SnmpCodec.DecodeSnmpValue(0x02, [0x2A]);
        Assert.Equal("INTEGER", type);
        Assert.Equal("42", value);
    }

    [Fact]
    public void DecodeSnmpValue_String_DecodesPrintable()
    {
        var (type, value) = SnmpCodec.DecodeSnmpValue(0x04, "Hello"u8.ToArray());
        Assert.Equal("STRING", type);
        Assert.Equal("Hello", value);
    }

    [Fact]
    public void DecodeSnmpValue_IpAddress_Formats()
    {
        var (type, value) = SnmpCodec.DecodeSnmpValue(0x40, [10, 0, 0, 1]);
        Assert.Equal("IpAddress", type);
        Assert.Equal("10.0.0.1", value);
    }

    [Fact]
    public void DecodeSnmpValue_NullContent_ReturnsEmptyValue()
    {
        var (type, value) = SnmpCodec.DecodeSnmpValue(0x02, null);
        Assert.Equal("INTEGER", type);
        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public void DecodeIpAddress_NonStandard_ReturnsHex()
    {
        Assert.Equal("0A0001", SnmpCodec.DecodeIpAddress([10, 0, 1]));
    }

    [Fact]
    public void DecodeOctetString_NonPrintable_ReturnsHex()
    {
        Assert.Equal("01-02-03", SnmpCodec.DecodeOctetString([0x01, 0x02, 0x03]));
    }

    [Fact]
    public void FormatTimeTicks_FormatsCorrectly()
    {
        var result = SnmpCodec.FormatTimeTicks(100);
        Assert.StartsWith("0d 00:00:01", result);
        Assert.Contains("1s", result);
    }

    [Theory]
    [InlineData("1.3.6.1.2.1.1.1.0", "sysDescr.0")]
    [InlineData("1.3.6.1.2.1.1.5.0", "sysName.0")]
    [InlineData("1.3.6.1.2.1.2.2.1.2", "ifDescr")]
    public void ResolveOidName_KnownOids_ResolvesCorrectly(string oid, string expected)
    {
        Assert.Equal(expected, SnmpCodec.ResolveOidName(oid));
    }

    [Fact]
    public void ResolveOidName_TableIndex_ResolvesPrefix()
    {
        Assert.Equal("ifDescr.1", SnmpCodec.ResolveOidName("1.3.6.1.2.1.2.2.1.2.1"));
    }

    [Fact]
    public void ResolveOidName_UnknownOid_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SnmpCodec.ResolveOidName("1.3.6.1.99.99.99"));
    }

    [Fact]
    public void ParseSnmpWalkLine_StandardFormat_Parses()
    {
        var result = SnmpCodec.ParseSnmpWalkLine("iso.3.6.1.2.1.1.1.0 = STRING: Linux server 5.4.0");
        Assert.NotNull(result);
        Assert.Equal("1.3.6.1.2.1.1.1.0", result.Oid);
        Assert.Equal("sysDescr.0", result.Name);
        Assert.Equal("STRING", result.Type);
        Assert.Equal("Linux server 5.4.0", result.Value);
    }

    [Fact]
    public void ParseSnmpWalkLine_MibPrefix_ParsesName()
    {
        var result = SnmpCodec.ParseSnmpWalkLine("SNMPv2-MIB::sysDescr.0 = STRING: Linux");
        Assert.NotNull(result);
        Assert.Equal("sysDescr.0", result.Name);
    }

    [Fact]
    public void ParseSnmpWalkLine_EmptyLine_ReturnsNull()
    {
        Assert.Null(SnmpCodec.ParseSnmpWalkLine(""));
        Assert.Null(SnmpCodec.ParseSnmpWalkLine("   "));
    }

    [Fact]
    public void ParseSnmpWalkLine_NoEquals_ReturnsNull()
    {
        Assert.Null(SnmpCodec.ParseSnmpWalkLine("some random text"));
    }

    [Fact]
    public void ParseOidString_Valid_ReturnsComponents()
    {
        var result = SnmpCodec.ParseOidString("1.3.6.1");
        Assert.NotNull(result);
        Assert.Equal([1, 3, 6, 1], result);
    }

    [Fact]
    public void ParseOidString_Invalid_ReturnsNull()
    {
        Assert.Null(SnmpCodec.ParseOidString("not.an.oid"));
    }

    [Fact]
    public void BuildSnmpGetNextRequest_ProducesValidPacket()
    {
        var packet = SnmpCodec.BuildSnmpGetNextRequest("public", [1, 3, 6, 1, 2, 1, 1]);
        Assert.Equal(0x30, packet[0]);
        Assert.True(packet.Length > 20);
    }

    [Fact]
    public void ParseSnmpGetNextResponse_InvalidData_ReturnsNullOid()
    {
        var (oid, type, value) = SnmpCodec.ParseSnmpGetNextResponse([0x00, 0x01]);
        Assert.Null(oid);
        Assert.Equal(string.Empty, type);
        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public void BuildCsvExport_EmptyList_ReturnsHeaderOnly()
    {
        var csv = SnmpCodec.BuildCsvExport([]);
        Assert.Single(csv.Trim().Split('\n'));
    }

    [Fact]
    public void BuildCsvExport_WithEntries_IncludesData()
    {
        var csv = SnmpCodec.BuildCsvExport(
        [
            new SnmpEntry { Oid = "1.3.6.1.2.1.1.1.0", Name = "sysDescr.0", Type = "STRING", Value = "Test" },
        ]);

        var lines = csv.Trim().Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("sysDescr.0", lines[1]);
    }

    [Fact]
    public void BuildCsvExport_SanitizesCsvInjection()
    {
        var csv = SnmpCodec.BuildCsvExport(
        [
            new SnmpEntry { Oid = "1.3.6.1", Name = "test", Type = "STRING", Value = "=cmd()" },
        ]);

        Assert.Contains("\"'=cmd()\"", csv);
        Assert.DoesNotContain(",\"=cmd()\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCsvExport_WithLocalize_UsesLocalizedHeaders()
    {
        string Localize(string key) => key switch
        {
            "ToolSnmpColOid" => "OID",
            "ToolSnmpColName" => "Nom",
            "ToolSnmpColType" => "Type",
            "ToolSnmpColValue" => "Valeur",
            _ => key,
        };

        var csv = SnmpCodec.BuildCsvExport([], Localize);
        Assert.StartsWith("OID,Nom,Type,Valeur", csv.Trim(), StringComparison.Ordinal);
    }
}
