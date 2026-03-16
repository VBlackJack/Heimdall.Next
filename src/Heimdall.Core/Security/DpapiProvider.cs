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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Heimdall.Core.Security;

/// <summary>
/// Provides Windows DPAPI encryption and decryption (CurrentUser scope).
/// All intermediate byte arrays are zeroed in finally blocks to minimize
/// the window where plaintext exists in memory (CWE-316).
/// </summary>
[SupportedOSPlatform("windows")]
public static class DpapiProvider
{
    /// <summary>
    /// Encrypt a plaintext string using DPAPI (CurrentUser scope).
    /// </summary>
    /// <param name="plainText">The plaintext string to encrypt.</param>
    /// <returns>Base64-encoded DPAPI-encrypted ciphertext.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plainText"/> is null.</exception>
    /// <exception cref="CryptographicException">Thrown when DPAPI encryption fails.</exception>
    public static string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        byte[]? bytes = null;
        byte[]? encrypted = null;

        try
        {
            bytes = Encoding.UTF8.GetBytes(plainText);
            encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        finally
        {
            if (encrypted is not null) Array.Clear(encrypted);
            if (bytes is not null) Array.Clear(bytes);
        }
    }

    /// <summary>
    /// Decrypt a DPAPI-encrypted Base64 string to plaintext.
    /// </summary>
    /// <param name="encryptedBase64">Base64-encoded DPAPI ciphertext.</param>
    /// <returns>Decrypted plaintext string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="encryptedBase64"/> is null.</exception>
    /// <exception cref="CryptographicException">Thrown when DPAPI decryption fails.</exception>
    /// <exception cref="FormatException">Thrown when the input is not valid Base64.</exception>
    public static string Unprotect(string encryptedBase64)
    {
        ArgumentNullException.ThrowIfNull(encryptedBase64);

        byte[]? encrypted = null;
        byte[]? decrypted = null;

        try
        {
            encrypted = Convert.FromBase64String(encryptedBase64);
            decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        finally
        {
            if (encrypted is not null) Array.Clear(encrypted);
            if (decrypted is not null) Array.Clear(decrypted);
        }
    }

    /// <summary>
    /// Decrypt a DPAPI-encrypted Base64 string directly to a SecureString
    /// without creating an intermediate managed string (CWE-316 prevention).
    /// </summary>
    /// <param name="encryptedBase64">Base64-encoded DPAPI ciphertext.</param>
    /// <returns>A read-only SecureString containing the decrypted value. Caller must dispose.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="encryptedBase64"/> is null.</exception>
    /// <exception cref="CryptographicException">Thrown when DPAPI decryption fails.</exception>
    public static SecureString UnprotectToSecureString(string encryptedBase64)
    {
        ArgumentNullException.ThrowIfNull(encryptedBase64);

        byte[]? encrypted = null;
        byte[]? decrypted = null;
        char[]? chars = null;

        try
        {
            encrypted = Convert.FromBase64String(encryptedBase64);
            decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            chars = Encoding.UTF8.GetChars(decrypted);

            var secureString = new SecureString();
            foreach (var c in chars)
            {
                secureString.AppendChar(c);
            }
            secureString.MakeReadOnly();
            return secureString;
        }
        finally
        {
            if (encrypted is not null) Array.Clear(encrypted);
            if (decrypted is not null) Array.Clear(decrypted);
            if (chars is not null) Array.Clear(chars);
        }
    }

    /// <summary>
    /// Verify that DPAPI encryption and decryption work correctly in the
    /// current user context by performing a roundtrip test.
    /// </summary>
    /// <returns>True if DPAPI is functioning correctly; false otherwise.</returns>
    public static bool TestHealth()
    {
        string? probe = null;
        string? encrypted = null;
        string? decrypted = null;

        try
        {
            probe = $"DPAPI-Health-{Guid.NewGuid():N}";
            encrypted = Protect(probe);

            if (string.IsNullOrWhiteSpace(encrypted))
                return false;

            decrypted = Unprotect(encrypted);
            return string.Equals(probe, decrypted, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
        finally
        {
            probe = null;
            encrypted = null;
            decrypted = null;
        }
    }

    /// <summary>
    /// Convert a SecureString to a plaintext string for temporary use.
    /// The caller is responsible for minimizing the lifetime of the returned string.
    /// </summary>
    /// <param name="secureString">The SecureString to convert.</param>
    /// <returns>The plaintext content of the SecureString.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secureString"/> is null.</exception>
    public static string ConvertFromSecureString(SecureString secureString)
    {
        ArgumentNullException.ThrowIfNull(secureString);

        var bstr = IntPtr.Zero;
        try
        {
            bstr = Marshal.SecureStringToBSTR(secureString);
            return Marshal.PtrToStringBSTR(bstr);
        }
        finally
        {
            if (bstr != IntPtr.Zero)
            {
                Marshal.ZeroFreeBSTR(bstr);
            }
        }
    }
}
