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

public class DnsLookupModelsTests
{
    [Theory]
    [InlineData(DnsRecordType.A, "A")]
    [InlineData(DnsRecordType.AAAA, "AAAA")]
    [InlineData(DnsRecordType.MX, "MX")]
    [InlineData(DnsRecordType.CNAME, "CNAME")]
    [InlineData(DnsRecordType.TXT, "TXT")]
    [InlineData(DnsRecordType.NS, "NS")]
    [InlineData(DnsRecordType.PTR, "PTR")]
    [InlineData(DnsRecordType.SOA, "SOA")]
    [InlineData(DnsRecordType.ANY, "ANY")]
    public void ToWireFormat_ReturnsCanonicalToken(DnsRecordType type, string expected)
    {
        Assert.Equal(expected, type.ToWireFormat());
    }

    [Fact]
    public void ToWireFormat_CoversEveryEnumValue()
    {
        // Guard against silent breakage when a new enum member is added without
        // updating the switch expression.
        foreach (DnsRecordType type in Enum.GetValues<DnsRecordType>())
        {
            var token = type.ToWireFormat();
            Assert.False(string.IsNullOrEmpty(token));
        }
    }

    [Theory]
    [InlineData("A", DnsRecordType.A)]
    [InlineData("AAAA", DnsRecordType.AAAA)]
    [InlineData("MX", DnsRecordType.MX)]
    [InlineData("TXT", DnsRecordType.TXT)]
    public void TryParseWireFormat_UppercaseToken_Succeeds(string input, DnsRecordType expected)
    {
        var ok = DnsRecordTypeExtensions.TryParseWireFormat(input, out var parsed);

        Assert.True(ok);
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("a", DnsRecordType.A)]
    [InlineData("aaaa", DnsRecordType.AAAA)]
    [InlineData("mx", DnsRecordType.MX)]
    [InlineData("Txt", DnsRecordType.TXT)]
    [InlineData("Soa", DnsRecordType.SOA)]
    public void TryParseWireFormat_MixedCaseToken_Succeeds(string input, DnsRecordType expected)
    {
        var ok = DnsRecordTypeExtensions.TryParseWireFormat(input, out var parsed);

        Assert.True(ok);
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("  A  ")]
    [InlineData("\tAAAA\t")]
    [InlineData(" MX\n")]
    public void TryParseWireFormat_TrimsWhitespace(string input)
    {
        var ok = DnsRecordTypeExtensions.TryParseWireFormat(input, out _);

        Assert.True(ok);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void TryParseWireFormat_NullEmptyOrWhitespace_ReturnsFalse(string? input)
    {
        var ok = DnsRecordTypeExtensions.TryParseWireFormat(input, out var parsed);

        Assert.False(ok);
        Assert.Equal(DnsRecordType.A, parsed); // default value
    }

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("Hello")]
    [InlineData("A-Record")]
    public void TryParseWireFormat_UnknownToken_ReturnsFalse(string input)
    {
        var ok = DnsRecordTypeExtensions.TryParseWireFormat(input, out _);
        Assert.False(ok);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("99")]
    [InlineData("1234567890")]
    public void TryParseWireFormat_NumericToken_ReturnsFalse(string input)
    {
        // Enum.TryParse would otherwise coerce "5" into TXT; the helper must reject it.
        var ok = DnsRecordTypeExtensions.TryParseWireFormat(input, out _);
        Assert.False(ok);
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("A!")]
    [InlineData("A,")]
    [InlineData("A.B")]
    public void TryParseWireFormat_MixedLetterAndSymbol_ReturnsFalse(string input)
    {
        var ok = DnsRecordTypeExtensions.TryParseWireFormat(input, out _);
        Assert.False(ok);
    }

    [Fact]
    public void DnsLookupRequest_ExposesAllFields()
    {
        var req = new DnsLookupRequest("example.com", DnsRecordType.MX, "1.1.1.1");

        Assert.Equal("example.com", req.Hostname);
        Assert.Equal(DnsRecordType.MX, req.RecordType);
        Assert.Equal("1.1.1.1", req.DnsServer);
    }

    [Fact]
    public void DnsLookupRequest_NullDnsServer_IsAllowed()
    {
        var req = new DnsLookupRequest("example.org", DnsRecordType.A, null);

        Assert.Null(req.DnsServer);
    }

    [Fact]
    public void DnsLookupResult_Ok_SetsSuccessTrue()
    {
        var result = DnsLookupResult.Ok("example.com. 300 IN A 1.2.3.4", 42);

        Assert.True(result.Success);
        Assert.Equal("example.com. 300 IN A 1.2.3.4", result.Output);
        Assert.Equal(42, result.ElapsedMs);
        Assert.Equal(string.Empty, result.ErrorKey);
        Assert.Null(result.ErrorArg);
    }

    [Fact]
    public void DnsLookupResult_Ok_NullOutput_IsEmptyString()
    {
        var result = DnsLookupResult.Ok(null!, 10);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void DnsLookupResult_Error_SetsSuccessFalse()
    {
        var result = DnsLookupResult.Error("ToolDnsErrorTimeout", 5000);

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal("ToolDnsErrorTimeout", result.ErrorKey);
        Assert.Null(result.ErrorArg);
        Assert.Equal(5000, result.ElapsedMs);
    }

    [Fact]
    public void DnsLookupResult_Error_WithArg_PopulatesErrorArg()
    {
        var result = DnsLookupResult.Error("ToolDnsErrorLookupFailed", 123, "connection refused");

        Assert.False(result.Success);
        Assert.Equal("connection refused", result.ErrorArg);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DnsLookupResult_Error_NullOrEmptyKey_Throws(string? invalidKey)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException
        // (subclass of ArgumentException) for null and ArgumentException for empty
        // or whitespace. Both variants must be rejected.
        var ex = Record.Exception(() => DnsLookupResult.Error(invalidKey!, 0));

        Assert.NotNull(ex);
        Assert.IsAssignableFrom<ArgumentException>(ex);
    }
}
