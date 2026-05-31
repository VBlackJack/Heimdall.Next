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

        FileSecurity security = BuildRestrictedSecurity();
        FileInfo fileInfo = new(filePath);

        // CREATE_ALWAYS keeps a pre-existing file's ACL, so the restrictive security descriptor would not be
        // applied to a file that already exists (e.g. pre-created by another process). Remove any existing
        // file first, then CreateNew so the SD is always applied - and if a racing process re-creates the
        // path between the delete and the create, CreateNew fails closed instead of writing the secret into
        // a foreign file.
        fileInfo.Delete();
        using FileStream stream = fileInfo.Create(FileMode.CreateNew, FileSystemRights.WriteData,
            FileShare.None, 4096, FileOptions.None, security);

        byte[] bytes = Utf8NoBom.GetBytes(text ?? string.Empty);
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
    /// Async variant of <see cref="WriteAndProtect"/>. Same TOCTOU-free guarantee:
    /// the restrictive ACL is applied atomically when the file is created, before
    /// any data is written, so an observer never sees the file with a permissive
    /// inherited ACL.
    /// </summary>
    /// <param name="filePath">The target file path.</param>
    /// <param name="text">The text content to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static async Task WriteAndProtectAsync(
        string filePath,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        FileSecurity security = BuildRestrictedSecurity();
        FileInfo fileInfo = new(filePath);

        // See WriteAndProtect: CreateNew (after deleting any stale file) guarantees the restrictive ACL is
        // applied to a freshly created file and fails closed on a creation race.
        fileInfo.Delete();
        await using FileStream stream = fileInfo.Create(FileMode.CreateNew, FileSystemRights.WriteData,
            FileShare.None, 4096, FileOptions.Asynchronous, security);

        byte[] bytes = Utf8NoBom.GetBytes(text ?? string.Empty);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static FileSecurity BuildRestrictedSecurity()
    {
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot determine current user SID.");

        FileSecurity security = new FileSecurity();
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
        return security;
    }

}
