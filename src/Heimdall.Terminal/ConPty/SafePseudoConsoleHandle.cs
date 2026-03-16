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

namespace Heimdall.Terminal.ConPty;

/// <summary>
/// SafeHandle wrapper for a Windows Pseudo Console (HPCON) handle.
/// Ensures <see cref="NativeMethods.ClosePseudoConsole"/> is called on release,
/// even if the owning code throws or forgets to dispose.
/// </summary>
internal sealed class SafePseudoConsoleHandle : SafeHandle
{
    public SafePseudoConsoleHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public SafePseudoConsoleHandle(IntPtr handle) : base(handle, ownsHandle: true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        NativeMethods.ClosePseudoConsole(handle);
        return true;
    }
}
