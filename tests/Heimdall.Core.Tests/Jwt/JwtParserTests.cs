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

using System.Text;
using Heimdall.Core.Jwt;

namespace Heimdall.Core.Tests.Jwt;

public sealed class JwtParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    public void TryDecode_InvalidFormat_ReturnsInvalidFormat(string? input)
    {
        var success = JwtParser.TryDecode(input, out var decoded, out var error);

        Assert.False(success);
        Assert.Null(decoded);
        Assert.Equal(JwtDecodeError.InvalidFormat, error);
    }

    [Theory]
    [InlineData("%%%%", "eyJzdWIiOiJqb2huIn0", "")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9", "%%%%", "")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9", "eyJzdWIiOiJqb2huIn0", "%%%%")]
    [InlineData("bm90LWpzb24", "eyJzdWIiOiJqb2huIn0", "")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9", "bm90LWpzb24", "")]
    public void TryDecode_DecodeFailed_ReturnsDecodeFailed(string header, string payload, string signature)
    {
        var success = JwtParser.TryDecode($"{header}.{payload}.{signature}", out var decoded, out var error);

        Assert.False(success);
        Assert.Null(decoded);
        Assert.Equal(JwtDecodeError.DecodeFailed, error);
    }

    [Fact]
    public void TryDecode_ValidJwt_ReturnsDecodedSegments()
    {
        var jwt = CreateToken("""{"alg":"HS256","typ":"JWT"}""", """{"sub":"john","role":"admin"}""", new byte[] { 0xAB, 0xCD });

        var success = JwtParser.TryDecode(jwt, out var decoded, out var error);

        Assert.True(success);
        Assert.Equal(JwtDecodeError.None, error);
        Assert.NotNull(decoded);
        Assert.Equal("""{"alg":"HS256","typ":"JWT"}""", decoded!.HeaderJson);
        Assert.Equal("""{"sub":"john","role":"admin"}""", decoded.PayloadJson);
        Assert.Equal("abcd", decoded.SignatureHex);
        Assert.Contains('\n', decoded.PrettyHeaderJson);
        Assert.Contains('\n', decoded.PrettyPayloadJson);
    }

    [Fact]
    public void TryDecode_PreservesRawSegments()
    {
        var header = EncodeSegment("""{"alg":"HS512"}""");
        var payload = EncodeSegment("""{"exp":123}""");
        var jwt = $"{header}.{payload}.";

        var success = JwtParser.TryDecode(jwt, out var decoded, out _);

        Assert.True(success);
        Assert.Equal(header, decoded!.HeaderRaw);
        Assert.Equal(payload, decoded.PayloadRaw);
        Assert.Equal(string.Empty, decoded.SignatureRaw);
    }

    [Fact]
    public void TryDecode_EmptySignatureSegment_IsAllowed()
    {
        var jwt = $"{EncodeSegment("""{"alg":"none"}""")}.{EncodeSegment("""{"sub":"john"}""")}.";

        var success = JwtParser.TryDecode(jwt, out var decoded, out var error);

        Assert.True(success);
        Assert.Equal(JwtDecodeError.None, error);
        Assert.NotNull(decoded);
        Assert.Empty(decoded!.SignatureBytes);
    }

    [Theory]
    [InlineData("SGVsbG8", "Hello")]
    [InlineData("eyJmb28iOiJiYXIifQ", """{"foo":"bar"}""")]
    [InlineData("", "")]
    public void DecodeBase64UrlString_ReturnsExpectedValue(string segment, string expected)
    {
        Assert.Equal(expected, JwtParser.DecodeBase64UrlString(segment));
    }

    [Theory]
    [InlineData("QQ", "A")]
    [InlineData("SGVsbG8", "Hello")]
    [InlineData("YWJjZA", "abcd")]
    public void DecodeBase64UrlBytes_HandlesMissingPadding(string segment, string expected)
    {
        var bytes = JwtParser.DecodeBase64UrlBytes(segment);

        Assert.NotNull(bytes);
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes!));
    }

    [Theory]
    [InlineData("eyJmb28iOiJiYXIifQ", true)]
    [InlineData("%%%%", false)]
    [InlineData("SGVsbG8", true)]
    public void DecodeBase64UrlBytes_ReturnsNullOnlyForMalformedInput(string segment, bool hasValue)
    {
        Assert.Equal(hasValue, JwtParser.DecodeBase64UrlBytes(segment) is not null);
    }

    [Fact]
    public void PrettyHeaderJson_IsIndented()
    {
        var jwt = CreateToken("""{"alg":"HS256","typ":"JWT"}""", """{"sub":"john"}""", []);
        JwtParser.TryDecode(jwt, out var decoded, out _);

        Assert.StartsWith("{", decoded!.PrettyHeaderJson, StringComparison.Ordinal);
        Assert.Contains("  \"alg\"", decoded.PrettyHeaderJson, StringComparison.Ordinal);
    }

    [Fact]
    public void PrettyPayloadJson_IsIndented()
    {
        var jwt = CreateToken("""{"alg":"HS256"}""", """{"sub":"john","scope":["a","b"]}""", []);
        JwtParser.TryDecode(jwt, out var decoded, out _);

        Assert.Contains("  \"scope\"", decoded!.PrettyPayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void SignatureHex_IsLowercase()
    {
        var jwt = CreateToken("""{"alg":"HS256"}""", """{"sub":"john"}""", [0xAA, 0xBB, 0xCC]);
        JwtParser.TryDecode(jwt, out var decoded, out _);

        Assert.Equal("aabbcc", decoded!.SignatureHex);
    }

    [Fact]
    public void TryDecode_TrimmedInput_IsAccepted()
    {
        var jwt = CreateToken("""{"alg":"HS256"}""", """{"sub":"john"}""", []);

        var success = JwtParser.TryDecode($"  {jwt}  ", out var decoded, out _);

        Assert.True(success);
        Assert.NotNull(decoded);
    }

    [Fact]
    public void TryDecode_JsonArrays_AreAccepted()
    {
        var jwt = CreateToken("""["alg","HS256"]""", """["a","b"]""", []);

        var success = JwtParser.TryDecode(jwt, out var decoded, out _);

        Assert.True(success);
        Assert.Equal("""["alg","HS256"]""", decoded!.HeaderJson);
    }

    [Fact]
    public void TryDecode_SupportsUtf8Payload()
    {
        var jwt = CreateToken("""{"alg":"HS256"}""", """{"name":"éclair"}""", []);

        var success = JwtParser.TryDecode(jwt, out var decoded, out _);

        Assert.True(success);
        Assert.Contains("éclair", decoded!.PayloadJson, StringComparison.Ordinal);
    }

    private static string CreateToken(string headerJson, string payloadJson, byte[] signatureBytes)
        => $"{EncodeSegment(headerJson)}.{EncodeSegment(payloadJson)}.{EncodeBytes(signatureBytes)}";

    private static string EncodeSegment(string json) => EncodeBytes(Encoding.UTF8.GetBytes(json));

    private static string EncodeBytes(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
