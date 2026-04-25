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
using System.Security.AccessControl;
using System.Security.Principal;

namespace Heimdall.Ssh.Pageant;

/// <summary>
/// Builds a self-only Win32 SECURITY_ATTRIBUTES descriptor and owns the
/// native allocations until disposed. Used by Pageant IPC to restrict the
/// shared file mapping DACL to the current user, blocking same-session
/// userland snooping. Windows-only by construction (uses WindowsIdentity).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SecurityAttributesScope : IDisposable
{
    private IntPtr _securityDescriptorPtr;
    private IntPtr _securityAttributesPtr;
    private bool _disposed;

    private SecurityAttributesScope(IntPtr saPtr, IntPtr sdPtr)
    {
        _securityAttributesPtr = saPtr;
        _securityDescriptorPtr = sdPtr;
    }

    /// <summary>
    /// Pointer to the SECURITY_ATTRIBUTES structure usable as the
    /// <c>lpAttributes</c> argument of Win32 handle-creating APIs.
    /// </summary>
    public IntPtr Pointer => _securityAttributesPtr;

    /// <summary>
    /// Allocate a SECURITY_ATTRIBUTES with a self-only DACL
    /// (<c>D:P(A;;FA;;;&lt;currentUserSid&gt;)</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the current user SID cannot be resolved.
    /// </exception>
    public static SecurityAttributesScope CreateSelfOnly()
    {
        var currentUser = WindowsIdentity.GetCurrent();
        var userSid = currentUser.User
            ?? throw new InvalidOperationException("Cannot resolve current user SID for restrictive ACL.");

        var sddl = BuildSelfOnlySddl(userSid);
        var rawSd = new RawSecurityDescriptor(sddl);
        var sdBinary = new byte[rawSd.BinaryLength];
        rawSd.GetBinaryForm(sdBinary, 0);

        var sdPtr = IntPtr.Zero;
        var saPtr = IntPtr.Zero;
        try
        {
            sdPtr = Marshal.AllocHGlobal(sdBinary.Length);
            Marshal.Copy(sdBinary, 0, sdPtr, sdBinary.Length);

            var sa = new NativeMethods.SECURITY_ATTRIBUTES
            {
                nLength = (uint)Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = sdPtr,
                bInheritHandle = false
            };

            saPtr = Marshal.AllocHGlobal((int)sa.nLength);
            Marshal.StructureToPtr(sa, saPtr, fDeleteOld: false);

            // Ownership transferred to the scope; clear locals so the catch
            // does not free buffers the scope is now responsible for.
            var scope = new SecurityAttributesScope(saPtr, sdPtr);
            saPtr = IntPtr.Zero;
            sdPtr = IntPtr.Zero;
            return scope;
        }
        catch
        {
            // Free in reverse allocation order. Both pointers may be IntPtr.Zero
            // if their allocation step did not run or if ownership was already
            // transferred above; FreeHGlobal(IntPtr.Zero) is a documented no-op.
            if (saPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(saPtr);
            }
            if (sdPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(sdPtr);
            }
            throw;
        }
    }

    /// <summary>
    /// Build the SDDL string granting full access to the supplied SID only,
    /// with inheritance disabled (<c>D:P</c>).
    /// </summary>
    internal static string BuildSelfOnlySddl(SecurityIdentifier userSid)
    {
        ArgumentNullException.ThrowIfNull(userSid);
        return $"D:P(A;;FA;;;{userSid.Value})";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_securityAttributesPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_securityAttributesPtr);
            _securityAttributesPtr = IntPtr.Zero;
        }

        if (_securityDescriptorPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_securityDescriptorPtr);
            _securityDescriptorPtr = IntPtr.Zero;
        }
    }
}
