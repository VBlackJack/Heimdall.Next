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
using Heimdall.App.Services;
using Heimdall.Core.Jwt;

namespace Heimdall.App.Tests;

public sealed class JwtParserToolServiceTests
{
    private readonly JwtParserToolService _service = new();

    [Fact]
    public void TryDecode_DelegatesToCoreParser()
    {
        var jwt = CreateJwt("HS256", """{"sub":"john"}""", "secret");

        var success = _service.TryDecode(jwt, out var decoded, out var error);

        Assert.True(success);
        Assert.Equal(JwtDecodeError.None, error);
        Assert.NotNull(decoded);
    }

    [Fact]
    public void EvaluateExpiration_DelegatesToCoreEvaluator()
    {
        var result = _service.EvaluateExpiration("""{"exp":9999999999}""", DateTimeOffset.UnixEpoch);

        Assert.Equal(JwtExpirationStatus.Valid, result.Status);
    }

    [Fact]
    public void ClassifyAlgorithm_DelegatesToCoreVerifier()
    {
        Assert.Equal(JwtAlgorithmKind.Rsa, _service.ClassifyAlgorithm("RS256"));
    }

    [Fact]
    public void VerifyHmac_DelegatesToCoreVerifier()
    {
        _service.TryDecode(CreateJwt("HS256", """{"sub":"john"}""", "secret"), out var decoded, out _);

        var result = _service.VerifyHmac(decoded!, JwtAlgorithmKind.Hmac256, "secret");

        Assert.Equal(JwtHmacVerificationResult.Valid, result);
    }

    [Fact]
    public void VerifyHmac_NonHmac_ReturnsAlgorithmNotHmac()
    {
        _service.TryDecode(CreateJwt("HS256", """{"sub":"john"}""", "secret"), out var decoded, out _);

        var result = _service.VerifyHmac(decoded!, JwtAlgorithmKind.Rsa, "secret");

        Assert.Equal(JwtHmacVerificationResult.AlgorithmNotHmac, result);
    }

    private static string CreateJwt(string alg, string payloadJson, string secret)
    {
        var headerJson = $$"""{"alg":"{{alg}}","typ":"JWT"}""";
        var headerRaw = Encode(Encoding.UTF8.GetBytes(headerJson));
        var payloadRaw = Encode(Encoding.UTF8.GetBytes(payloadJson));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signatureRaw = Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{headerRaw}.{payloadRaw}")));
        return $"{headerRaw}.{payloadRaw}.{signatureRaw}";
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
