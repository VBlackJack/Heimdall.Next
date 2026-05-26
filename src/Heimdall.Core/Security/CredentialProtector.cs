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

using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Heimdall.Core.Security;

/// <summary>
/// Unified credential protection: DPAPI encryption with HMAC-SHA256 integrity.
/// Provides backward-compatible decryption for legacy DPAPI-only blobs (auto-upgrade).
/// </summary>
/// <remarks>
/// The HMAC key must be initialized via <see cref="Initialize"/> at startup before
/// any Protect/Unprotect calls. Legacy blobs (without HMAC) are accepted on
/// Unprotect and should be re-encrypted with HMAC on next save.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class CredentialProtector
{
    private static string? _hmacKeyRaw;

    /// <summary>
    /// Initialize the protector with the raw HMAC key (Base64).
    /// Must be called once at startup after loading settings.
    /// </summary>
    /// <param name="hmacKeyBase64">
    /// The raw (unprotected) HMAC key in Base64. If null, HMAC is disabled
    /// and the protector falls back to plain DPAPI.
    /// </param>
    public static void Initialize(string? hmacKeyBase64)
    {
        _hmacKeyRaw = hmacKeyBase64;
    }

    /// <summary>
    /// Encrypt a plaintext credential with DPAPI + HMAC integrity.
    /// Falls back to plain DPAPI if no HMAC key is configured.
    /// </summary>
    /// <param name="plainText">The plaintext credential to protect.</param>
    /// <returns>Protected string (HMAC-wrapped if key available, plain DPAPI otherwise).</returns>
    public static string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        if (_hmacKeyRaw is not null)
        {
            return HmacIntegrity.ProtectWithHmac(plainText, _hmacKeyRaw);
        }

        return DpapiProvider.Protect(plainText);
    }

    /// <summary>
    /// Decrypt a protected credential. Supports both HMAC-protected and legacy
    /// DPAPI-only formats for backward compatibility.
    /// </summary>
    /// <param name="protectedValue">The protected string to decrypt.</param>
    /// <returns>Decrypted plaintext, or null on failure.</returns>
    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            return null;

        try
        {
            if (HmacIntegrity.IsHmacProtected(protectedValue) && _hmacKeyRaw is not null)
            {
                return HmacIntegrity.UnprotectWithHmac(protectedValue, _hmacKeyRaw);
            }

            // Legacy DPAPI-only blob — decrypt without HMAC verification
            return DpapiProvider.Unprotect(protectedValue);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decrypt a protected credential directly to UTF-8 bytes, without ever
    /// materialising the plaintext as a managed string. Mirrors
    /// <see cref="Unprotect(string?)"/> but returns bytes; supports both HMAC-protected
    /// and legacy DPAPI-only formats.
    /// </summary>
    /// <param name="protectedValue">The protected string to decrypt.</param>
    /// <returns>
    /// UTF-8 plaintext bytes, or <c>null</c> on failure or empty input. The caller
    /// is responsible for zeroing the buffer with
    /// <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/> once it is no
    /// longer needed.
    /// </returns>
    public static byte[]? UnprotectToBytes(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            if (HmacIntegrity.IsHmacProtected(protectedValue) && _hmacKeyRaw is not null)
            {
                return HmacIntegrity.UnprotectToBytesWithHmac(protectedValue, _hmacKeyRaw);
            }

            return DpapiProvider.UnprotectToBytes(protectedValue);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check whether a protected value uses the legacy DPAPI-only format
    /// (no HMAC integrity). Legacy blobs should be re-encrypted on next save.
    /// </summary>
    public static bool IsLegacyFormat(string? protectedValue)
    {
        return !string.IsNullOrWhiteSpace(protectedValue)
            && !HmacIntegrity.IsHmacProtected(protectedValue);
    }
}
