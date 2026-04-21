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

namespace Heimdall.Core.Jwt;

public enum JwtAlgorithmKind
{
    Unknown,
    None,
    Hmac256,
    Hmac384,
    Hmac512,
    Rsa,
    Ecdsa,
    RsaPss,
}

public enum JwtHmacVerificationResult
{
    Valid,
    Invalid,
    AlgorithmNotHmac,
    MalformedInput,
}

public static class JwtHmacVerifier
{
    public static JwtAlgorithmKind ClassifyAlgorithm(string? alg)
        => alg switch
        {
            "none" => JwtAlgorithmKind.None,
            "HS256" => JwtAlgorithmKind.Hmac256,
            "HS384" => JwtAlgorithmKind.Hmac384,
            "HS512" => JwtAlgorithmKind.Hmac512,
            _ when alg?.StartsWith("RS", StringComparison.Ordinal) == true => JwtAlgorithmKind.Rsa,
            _ when alg?.StartsWith("ES", StringComparison.Ordinal) == true => JwtAlgorithmKind.Ecdsa,
            _ when alg?.StartsWith("PS", StringComparison.Ordinal) == true => JwtAlgorithmKind.RsaPss,
            _ => JwtAlgorithmKind.Unknown,
        };

    public static JwtHmacVerificationResult Verify(JwtDecoded decoded, JwtAlgorithmKind alg, string secret)
    {
        if (decoded is null || string.IsNullOrEmpty(secret))
        {
            return JwtHmacVerificationResult.MalformedInput;
        }

        using var hmac = CreateHmac(alg, Encoding.UTF8.GetBytes(secret));
        if (hmac is null)
        {
            return JwtHmacVerificationResult.AlgorithmNotHmac;
        }

        var signingInput = Encoding.UTF8.GetBytes($"{decoded.HeaderRaw}.{decoded.PayloadRaw}");
        var computed = hmac.ComputeHash(signingInput);
        return CryptographicOperations.FixedTimeEquals(computed, decoded.SignatureBytes)
            ? JwtHmacVerificationResult.Valid
            : JwtHmacVerificationResult.Invalid;
    }

    private static HMAC? CreateHmac(JwtAlgorithmKind alg, byte[] key)
        => alg switch
        {
            JwtAlgorithmKind.Hmac256 => new HMACSHA256(key),
            JwtAlgorithmKind.Hmac384 => new HMACSHA384(key),
            JwtAlgorithmKind.Hmac512 => new HMACSHA512(key),
            _ => null,
        };
}
