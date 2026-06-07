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
using Microsoft.Win32.SafeHandles;

namespace Heimdall.Terminal.ConPty;

/// <summary>
/// P/Invoke declarations for Windows Pseudo Console (ConPTY) APIs.
/// Uses source-generated marshalling via <see cref="LibraryImportAttribute"/>.
/// </summary>
internal static partial class NativeMethods
{
    // ========================================================================
    // Pseudo Console
    // ========================================================================

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int CreatePseudoConsole(
        COORD size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int ResizePseudoConsole(IntPtr hPC, COORD size);

    [LibraryImport("kernel32.dll")]
    internal static partial void ClosePseudoConsole(IntPtr hPC);

    // ========================================================================
    // Pipes
    // ========================================================================

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        uint nSize);

    // ========================================================================
    // Process creation
    // ========================================================================

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcessW(
        string? lpApplicationName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    // ========================================================================
    // Process lifecycle
    // ========================================================================

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle,
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwOptions);

    // ========================================================================
    // Thread attribute list (pseudo console assignment)
    // ========================================================================

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        uint dwAttributeCount,
        uint dwFlags,
        ref nint lpSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        nuint Attribute,
        IntPtr lpValue,
        nuint cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [LibraryImport("kernel32.dll")]
    internal static partial void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    // ========================================================================
    // ConPTY availability check
    // ========================================================================

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr GetModuleHandleW(string lpModuleName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    // ========================================================================
    // Constants
    // ========================================================================

    internal const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    internal const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    internal const uint STILL_ACTIVE = 259;
    internal const uint INFINITE = 0xFFFFFFFF;
    internal const uint WAIT_FAILED = 0xFFFFFFFF;
    internal const uint DUPLICATE_SAME_ACCESS = 0x00000002;

    internal static readonly nuint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    // ========================================================================
    // Structures
    // ========================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct COORD
    {
        public short X;
        public short Y;

        public COORD(short x, short y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SECURITY_ATTRIBUTES
    {
        public uint nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOW
    {
        public uint cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOEX
    {
        public STARTUPINFOW StartupInfo;
        public IntPtr lpAttributeList;
    }
}
