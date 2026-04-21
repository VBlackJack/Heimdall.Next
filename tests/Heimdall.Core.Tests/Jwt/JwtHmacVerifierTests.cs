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

using System.Security.Cryptography;
using System.Text;
using Heimdall.Core.Jwt;

namespace Heimdall.Core.Tests.Jwt;

public sealed class JwtHmacVerifierTests
{
    [Theory]
    [InlineData("HS256", JwtAlgorithmKind.Hmac256)]
    [InlineData("HS384", JwtAlgorithmKind.Hmac384)]
    [InlineData("HS512", JwtAlgorithmKind.Hmac512)]
    [InlineData("RS256", JwtAlgorithmKind.Rsa)]
    [InlineData("ES256", JwtAlgorithmKind.Ecdsa)]
    [InlineData("PS256", JwtAlgorithmKind.RsaPss)]
    [InlineData("none", JwtAlgorithmKind.None)]
    [InlineData("weird", JwtAlgorithmKind.Unknown)]
    [InlineData(null, JwtAlgorithmKind.Unknown)]
    public void ClassifyAlgorithm_ReturnsExpectedKind(string? alg, JwtAlgorithmKind expected)
    {
        Assert.Equal(expected, JwtHmacVerifier.ClassifyAlgorithm(alg));
    }

    [Theory]
    [InlineData(JwtAlgorithmKind.Hmac256, "HS256")]
    [InlineData(JwtAlgorithmKind.Hmac384, "HS384")]
    [InlineData(JwtAlgorithmKind.Hmac512, "HS512")]
    public void Verify_ValidSignature_ReturnsValid(JwtAlgorithmKind kind, string alg)
    {
        var decoded = CreateSignedDecoded(alg, "secret");

        var result = JwtHmacVerifier.Verify(decoded, kind, "secret");

        Assert.Equal(JwtHmacVerificationResult.Valid, result);
    }

    [Theory]
    [InlineData(JwtAlgorithmKind.Hmac256, "HS256")]
    [InlineData(JwtAlgorithmKind.Hmac384, "HS384")]
    [InlineData(JwtAlgorithmKind.Hmac512, "HS512")]
    public void Verify_WrongSecret_ReturnsInvalid(JwtAlgorithmKind kind, string alg)
    {
        var decoded = CreateSignedDecoded(alg, "secret");

        var result = JwtHmacVerifier.Verify(decoded, kind, "other");

        Assert.Equal(JwtHmacVerificationResult.Invalid, result);
    }

    [Theory]
    [InlineData(JwtAlgorithmKind.Rsa)]
    [InlineData(JwtAlgorithmKind.Ecdsa)]
    [InlineData(JwtAlgorithmKind.RsaPss)]
    [InlineData(JwtAlgorithmKind.Unknown)]
    [InlineData(JwtAlgorithmKind.None)]
    public void Verify_NonHmacAlgorithm_ReturnsAlgorithmNotHmac(JwtAlgorithmKind kind)
    {
        var decoded = CreateSignedDecoded("HS256", "secret");

        var result = JwtHmacVerifier.Verify(decoded, kind, "secret");

        Assert.Equal(JwtHmacVerificationResult.AlgorithmNotHmac, result);
    }

    [Fact]
    public void Verify_EmptySecret_ReturnsMalformedInput()
    {
        var decoded = CreateSignedDecoded("HS256", "secret");

        var result = JwtHmacVerifier.Verify(decoded, JwtAlgorithmKind.Hmac256, string.Empty);

        Assert.Equal(JwtHmacVerificationResult.MalformedInput, result);
    }

    [Fact]
    public void Verify_NullDecoded_ReturnsMalformedInput()
    {
        var result = JwtHmacVerifier.Verify(null!, JwtAlgorithmKind.Hmac256, "secret");

        Assert.Equal(JwtHmacVerificationResult.MalformedInput, result);
    }

    [Fact]
    public void Verify_ComparesRawBytes_NotEncodedText()
    {
        var decoded = CreateSignedDecoded("HS256", "secret");

        Assert.Equal(JwtHmacVerificationResult.Valid, JwtHmacVerifier.Verify(decoded, JwtAlgorithmKind.Hmac256, "secret"));
    }

    private static JwtDecoded CreateSignedDecoded(string alg, string secret)
    {
        var headerJson = $$"""{"alg":"{{alg}}","typ":"JWT"}""";
        var payloadJson = """{"sub":"john"}""";
        var headerRaw = EncodeSegment(headerJson);
        var payloadRaw = EncodeSegment(payloadJson);
        var input = Encoding.UTF8.GetBytes($"{headerRaw}.{payloadRaw}");
        var signature = alg switch
        {
            "HS256" => new HMACSHA256(Encoding.UTF8.GetBytes(secret)).ComputeHash(input),
            "HS384" => new HMACSHA384(Encoding.UTF8.GetBytes(secret)).ComputeHash(input),
            "HS512" => new HMACSHA512(Encoding.UTF8.GetBytes(secret)).ComputeHash(input),
            _ => []
        };
        return new JwtDecoded(headerJson, payloadJson, signature, headerRaw, payloadRaw, EncodeBytes(signature));
    }

    private static string EncodeSegment(string json) => EncodeBytes(Encoding.UTF8.GetBytes(json));
    private static string EncodeBytes(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
