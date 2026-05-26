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
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
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
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            if (decrypted is not null) CryptographicOperations.ZeroMemory(decrypted);
        }
    }

    /// <summary>
    /// Decrypt a DPAPI-encrypted Base64 string directly to UTF-8 bytes,
    /// without ever materialising the plaintext as a managed string.
    /// </summary>
    /// <param name="encryptedBase64">Base64-encoded DPAPI ciphertext.</param>
    /// <returns>
    /// A new byte array containing UTF-8 plaintext. The caller is responsible
    /// for zeroing the buffer with <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/>
    /// once it is no longer needed.
    /// </returns>
    public static byte[] UnprotectToBytes(string encryptedBase64)
    {
        ArgumentNullException.ThrowIfNull(encryptedBase64);

        byte[]? encrypted = null;

        try
        {
            encrypted = Convert.FromBase64String(encryptedBase64);
            return ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            if (encrypted is not null)
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
        }
    }

    /// <summary>
    /// Encrypt UTF-8 plaintext bytes with DPAPI (CurrentUser scope).
    /// Does not mutate the source buffer; callers should zero it themselves
    /// after the call.
    /// </summary>
    /// <param name="plainBytes">UTF-8 plaintext bytes.</param>
    /// <returns>Base64-encoded DPAPI-encrypted ciphertext.</returns>
    public static string ProtectBytes(byte[] plainBytes)
    {
        ArgumentNullException.ThrowIfNull(plainBytes);

        byte[]? encrypted = null;

        try
        {
            encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        finally
        {
            if (encrypted is not null)
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
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

            SecureString secureString = new SecureString();
            foreach (char c in chars)
            {
                secureString.AppendChar(c);
            }
            secureString.MakeReadOnly();
            return secureString;
        }
        finally
        {
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            if (decrypted is not null) CryptographicOperations.ZeroMemory(decrypted);
            if (chars is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars.AsSpan()));
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
