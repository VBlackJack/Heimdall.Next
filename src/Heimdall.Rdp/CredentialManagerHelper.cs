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
using System.Text;

namespace Heimdall.Rdp;

/// <summary>
/// Manages RDP credentials in Windows Credential Manager for mstsc.exe.
/// Writes DOMAIN_PASSWORD credentials (CRED_TYPE=2) in the TERMSRV/host format
/// recognized by the Windows RDP client for automatic login.
/// </summary>
public static class CredentialManagerHelper
{
    #region Constants

    private const uint CredTypeGeneric = 1;
    private const uint CredTypeDomainPassword = 2;
    private const uint CredPersistSession = 1;
    private const int CredMaxCredentialBlobSize = 512;
    private const int ErrorNotFound = 1168;

    #endregion

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    #endregion

    /// <summary>
    /// Store a GENERIC credential (CRED_TYPE=1) with session persistence.
    /// </summary>
    public static bool WriteGenericCredential(string targetName, string username, string password, out string? error)
    {
        return WriteCredential(targetName, username, password, CredTypeGeneric, CredPersistSession, out error);
    }

    /// <summary>
    /// Store a DOMAIN_PASSWORD credential (CRED_TYPE=2) recognized by mstsc.exe / RDP.
    /// Target must follow the TERMSRV/host format for RDP auto-login.
    /// Persist is set to Session — credential lives only until logoff and is cleaned
    /// up by the caller after the RDP session launches (defense-in-depth).
    /// </summary>
    public static bool WriteDomainCredential(string targetName, string username, string password, out string? error)
    {
        return WriteCredential(targetName, username, password, CredTypeDomainPassword, CredPersistSession, out error);
    }

    /// <summary>
    /// Delete credentials for the specified target.
    /// Attempts to delete both DOMAIN_PASSWORD and GENERIC types for backward compatibility.
    /// </summary>
    public static bool DeleteCredential(string targetName, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(targetName))
        {
            error = "Credential target cannot be empty.";
            return false;
        }

        // Try deleting as DOMAIN_PASSWORD first (used for TERMSRV/ RDP targets)
        bool deletedDomain = CredDelete(targetName, CredTypeDomainPassword, 0);
        if (!deletedDomain)
        {
            int win32Domain = Marshal.GetLastWin32Error();
            if (win32Domain != ErrorNotFound)
            {
                error = $"WIN32_ERROR_{win32Domain}";
                return false;
            }
        }

        // Also attempt GENERIC cleanup in case legacy credentials exist
        bool deletedGeneric = CredDelete(targetName, CredTypeGeneric, 0);
        if (!deletedGeneric)
        {
            int win32Generic = Marshal.GetLastWin32Error();
            if (win32Generic != ErrorNotFound && !deletedDomain)
            {
                error = $"WIN32_ERROR_{win32Generic}";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Shared implementation for writing a Windows credential via CredWriteW.
    /// Validates inputs, encodes the secret, marshals to native memory, and
    /// zeroes all sensitive byte arrays in the finally block.
    /// </summary>
    private static bool WriteCredential(
        string target,
        string userName,
        string secret,
        uint credType,
        uint credPersist,
        out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(target))
        {
            error = "Credential target cannot be empty.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(userName))
        {
            error = "Credential username cannot be empty.";
            return false;
        }
        if (secret is null)
        {
            error = "Credential secret cannot be null.";
            return false;
        }

        byte[]? secretBytes = null;
        IntPtr secretPtr = IntPtr.Zero;

        try
        {
            secretBytes = Encoding.Unicode.GetBytes(secret);
            if (secretBytes.Length > CredMaxCredentialBlobSize)
            {
                error = $"Credential secret exceeds {CredMaxCredentialBlobSize} bytes.";
                return false;
            }

            secretPtr = Marshal.StringToCoTaskMemUni(secret);

            var credential = new NativeCredential
            {
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = null,
                TargetAlias = null,
                Type = credType,
                Persist = credPersist,
                CredentialBlobSize = (uint)secretBytes.Length,
                TargetName = target,
                CredentialBlob = secretPtr,
                UserName = userName
            };

            bool written = CredWrite(ref credential, 0);
            if (!written)
            {
                error = $"WIN32_ERROR_{Marshal.GetLastWin32Error()}";
            }

            return written;
        }
        finally
        {
            // Zero and release sensitive memory
            if (secretPtr != IntPtr.Zero)
            {
                Marshal.ZeroFreeCoTaskMemUnicode(secretPtr);
            }
            if (secretBytes is not null)
            {
                Array.Clear(secretBytes, 0, secretBytes.Length);
            }
        }
    }
}
