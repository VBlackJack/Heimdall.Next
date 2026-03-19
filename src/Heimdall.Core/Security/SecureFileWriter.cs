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
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Heimdall.Core.Security;

/// <summary>
/// Writes sensitive data to files while minimizing the window where plaintext
/// exists in memory. All intermediate char[] and byte[] buffers are zeroed
/// in finally blocks (CWE-316 prevention).
/// </summary>
public static class SecureFileWriter
{
    /// <summary>
    /// UTF-8 encoding without BOM (matches legacy PS 5.1 behavior that avoids BOM corruption).
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Write a SecureString directly to a file as UTF-8 without creating an
    /// intermediate managed System.String, minimizing the plaintext memory exposure window.
    /// BSTR, char[], and byte[] intermediates are all zeroed in finally blocks.
    /// </summary>
    /// <param name="secureString">The SecureString to write.</param>
    /// <param name="filePath">The target file path.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null or empty.</exception>
    public static void WriteSecureStringToFile(SecureString secureString, string filePath)
    {
        ArgumentNullException.ThrowIfNull(secureString);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var bstr = IntPtr.Zero;
        char[]? chars = null;
        byte[]? utf8Bytes = null;

        try
        {
            bstr = Marshal.SecureStringToBSTR(secureString);

            // BSTR stores its byte length as a 4-byte integer at offset -4
            var bstrLenBytes = Marshal.ReadInt32(bstr, -4);
            var charCount = bstrLenBytes / 2;

            // Copy UTF-16 chars from unmanaged BSTR into a clearable managed array
            chars = new char[charCount];
            for (var i = 0; i < charCount; i++)
            {
                chars[i] = (char)Marshal.ReadInt16(bstr, i * 2);
            }

            // Convert to UTF-8 for file output
            utf8Bytes = Utf8NoBom.GetBytes(chars);

            // Zero the char array immediately after encoding
            Array.Clear(chars);
            chars = null;

            // Write UTF-8 bytes to file
            File.WriteAllBytes(filePath, utf8Bytes);
        }
        finally
        {
            // Zero the BSTR in unmanaged memory
            if (bstr != IntPtr.Zero)
            {
                Marshal.ZeroFreeBSTR(bstr);
            }

            // Zero the char array if not already cleared
            if (chars is not null)
            {
                Array.Clear(chars);
            }

            // Zero the UTF-8 byte array
            if (utf8Bytes is not null)
            {
                Array.Clear(utf8Bytes);
            }
        }
    }

    /// <summary>
    /// Writes text to a file that is created with restrictive ACL from the start,
    /// eliminating the TOCTOU window between file creation and permission enforcement.
    /// Only the current user, Administrators, and SYSTEM can access the file.
    /// </summary>
    /// <param name="filePath">The target file path.</param>
    /// <param name="text">The text content to write.</param>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void WriteAndProtect(string filePath, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot determine current user SID.");

        // Build a restrictive ACL before creating the file
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        // Create the file with the restrictive ACL applied atomically
        var fileInfo = new FileInfo(filePath);
        using var stream = fileInfo.Create(FileMode.Create, FileSystemRights.WriteData,
            FileShare.None, 4096, FileOptions.None, security);

        var bytes = Utf8NoBom.GetBytes(text ?? string.Empty);
        try
        {
            stream.Write(bytes, 0, bytes.Length);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    /// <summary>
    /// Write text to a file using UTF-8 without BOM.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <param name="filePath">The target file path.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null or empty.</exception>
    public static void WriteText(string text, string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        File.WriteAllText(filePath, text ?? string.Empty, Utf8NoBom);
    }

    /// <summary>
    /// Write text to a file using UTF-8 without BOM (async version).
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <param name="filePath">The target file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null or empty.</exception>
    public static async Task WriteTextAsync(
        string text,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        await File.WriteAllTextAsync(filePath, text ?? string.Empty, Utf8NoBom, cancellationToken)
            .ConfigureAwait(false);
    }
}
