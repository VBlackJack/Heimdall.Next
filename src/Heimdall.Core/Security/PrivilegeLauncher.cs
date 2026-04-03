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

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.Core.Security;

/// <summary>
/// Privilege levels for process launching.
/// </summary>
public enum PrivilegeLevel
{
    /// <summary>Standard UAC elevation (run as administrator).</summary>
    CurrentUserElevated,

    /// <summary>NT AUTHORITY\SYSTEM context (via winlogon.exe token).</summary>
    System,

    /// <summary>NT SERVICE\TrustedInstaller context (via TrustedInstaller service token).</summary>
    TrustedInstaller
}

/// <summary>
/// Result of a privilege-launched process.
/// </summary>
public sealed record PrivilegeLaunchResult(bool Success, int ProcessId, string? ErrorMessage = null)
{
    public static PrivilegeLaunchResult Failed(string message) => new(false, 0, message);
    public static PrivilegeLaunchResult Ok(int pid) => new(true, pid);
}

/// <summary>
/// Launches processes under elevated security contexts (SYSTEM, TrustedInstaller)
/// using Win32 token duplication and <c>CreateProcessWithTokenW</c>.
/// Requires the calling process to be elevated (administrator).
/// </summary>
[SupportedOSPlatform("windows")]
public static class PrivilegeLauncher
{
    // ── Win32 constants ───────────────────────────────────────────────

    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint TOKEN_ADJUST_SESSION_ID = 0x0100;
    private const uint MAXIMUM_ALLOWED = 0x02000000;

    private const int SECURITY_IMPERSONATION = 2; // SecurityImpersonation
    private const int TOKEN_PRIMARY = 1;          // TokenPrimary

    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const uint LOGON_WITH_PROFILE = 0x00000001;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_RUNNING = 0x00000004;

    private const int MAX_SERVICE_WAIT_MS = 10_000;
    private const int SERVICE_POLL_INTERVAL_MS = 250;

    // ── Win32 structures ──────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    // ── Win32 imports ─────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        SafeAccessTokenHandle hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int impersonationLevel,
        int tokenType,
        out SafeAccessTokenHandle phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(
        SafeAccessTokenHandle hToken,
        uint dwLogonFlags,
        string? lpApplicationName,
        string? lpCommandLine,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        SafeAccessTokenHandle tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        int bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr hService, int dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const string PrivLaunchArg = "--privlaunch";

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Launches a process under the specified privilege level.
    /// When the current process is not elevated and SYSTEM/TrustedInstaller
    /// is requested, automatically re-launches itself elevated via UAC
    /// to perform the token work.
    /// </summary>
    public static PrivilegeLaunchResult Launch(string executablePath, string? arguments, PrivilegeLevel level)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return PrivilegeLaunchResult.Failed("Executable path is required.");

        if (!System.IO.File.Exists(executablePath))
            return PrivilegeLaunchResult.Failed($"File not found: {executablePath}");

        // UAC elevation is handled by ShellExecute directly
        if (level == PrivilegeLevel.CurrentUserElevated)
            return LaunchElevated(executablePath, arguments);

        // For SYSTEM/TI: if already elevated, do in-process; otherwise self-elevate
        if (IsCurrentProcessElevated())
        {
            return level switch
            {
                PrivilegeLevel.System => LaunchWithTokenProbe(executablePath, arguments, SystemProcessCandidates),
                PrivilegeLevel.TrustedInstaller => LaunchAsTrustedInstaller(executablePath, arguments),
                _ => PrivilegeLaunchResult.Failed($"Unknown privilege level: {level}")
            };
        }

        return LaunchViaSelfElevation(executablePath, arguments, level);
    }

    /// <summary>
    /// Checks whether the current process is running elevated (administrator).
    /// </summary>
    public static bool IsCurrentProcessElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Handles the <c>--privlaunch</c> command-line argument in the elevated child process.
    /// Call from <c>App.OnStartup</c> before any UI initialization.
    /// Returns null if args don't match; otherwise returns an exit code (0=success).
    /// </summary>
    public static int? HandlePrivilegeLaunchArgs(string[] args)
    {
        // Expected: --privlaunch <level> <exe> [arguments...]
        if (args.Length < 3 || !string.Equals(args[0], PrivLaunchArg, StringComparison.OrdinalIgnoreCase))
            return null;

        var levelStr = args[1];
        var exe = args[2];
        var childArgs = args.Length > 3 ? string.Join(" ", args[3..]) : null;

        if (!Enum.TryParse<PrivilegeLevel>(levelStr, ignoreCase: true, out var level))
            return 1;

        // We are now elevated — do the actual token work
        PrivilegeLaunchResult result;
        try
        {
            result = level switch
            {
                PrivilegeLevel.System => LaunchWithTokenProbe(exe, childArgs, SystemProcessCandidates),
                PrivilegeLevel.TrustedInstaller => LaunchAsTrustedInstaller(exe, childArgs),
                _ => PrivilegeLaunchResult.Failed($"Unsupported level for self-elevation: {level}")
            };
        }
        catch (Exception ex)
        {
            result = PrivilegeLaunchResult.Failed(ex.Message);
        }

        return result.Success ? 0 : 2;
    }

    // ── Elevated (UAC) ────────────────────────────────────────────────

    private static PrivilegeLaunchResult LaunchElevated(string exe, string? args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args ?? string.Empty,
                UseShellExecute = true,
                Verb = "runas"
            };

            var process = Process.Start(psi);
            return process is not null
                ? PrivilegeLaunchResult.Ok(process.Id)
                : PrivilegeLaunchResult.Failed("Process.Start returned null.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            return PrivilegeLaunchResult.Failed("UAC elevation was cancelled by the user.");
        }
        catch (Exception ex)
        {
            return PrivilegeLaunchResult.Failed(ex.Message);
        }
    }

    // ── Self-elevation (re-launch as admin via UAC) ─────────────────

    private static PrivilegeLaunchResult LaunchViaSelfElevation(
        string exe, string? args, PrivilegeLevel level)
    {
        try
        {
            // Resolve the native host exe for self-elevation.
            // Environment.ProcessPath returns dotnet.exe when using "dotnet run",
            // so we derive the .exe path from the entry assembly's DLL location.
            var (selfExe, selfPrefix) = ResolveSelfExecutable();
            if (string.IsNullOrEmpty(selfExe))
                return PrivilegeLaunchResult.Failed("Cannot determine current executable path.");

            // Build argument string: [dotnet-prefix] --privlaunch <level> "<exe>" <args>
            var childArguments = $"{selfPrefix}{PrivLaunchArg} {level} \"{exe}\"";
            if (!string.IsNullOrWhiteSpace(args))
                childArguments += $" {args}";

            var psi = new ProcessStartInfo
            {
                FileName = selfExe,
                Arguments = childArguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(psi);
            if (process is null)
                return PrivilegeLaunchResult.Failed("Failed to start elevated process.");

            // Wait for the elevated child to finish (it just launches the target and exits)
            process.WaitForExit(15_000);
            var exitCode = process.HasExited ? process.ExitCode : -1;
            process.Dispose();

            return exitCode == 0
                ? PrivilegeLaunchResult.Ok(0)
                : PrivilegeLaunchResult.Failed("Elevated launch failed (child exit code: " + exitCode + ").");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            return PrivilegeLaunchResult.Failed("UAC elevation was cancelled by the user.");
        }
        catch (Exception ex)
        {
            return PrivilegeLaunchResult.Failed(ex.Message);
        }
    }

    // ── Self-executable resolution ──────────────────────────────────

    /// <summary>
    /// Resolves the native host executable for self-elevation.
    /// Returns (exePath, argPrefix) where argPrefix is empty for direct exe
    /// or contains "exec &lt;dll&gt; " for dotnet-hosted scenarios.
    /// </summary>
    private static (string? ExePath, string ArgPrefix) ResolveSelfExecutable()
    {
        var processPath = Environment.ProcessPath;

        // Direct exe launch (Release, Run.bat, etc.) — ProcessPath IS the app exe
        if (processPath is not null
            && !processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return (processPath, string.Empty);
        }

        // Running via "dotnet run" or "dotnet exec" — derive the .exe from the loaded DLL
        var dllPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrEmpty(dllPath))
        {
            var exePath = System.IO.Path.ChangeExtension(dllPath, ".exe");
            if (System.IO.File.Exists(exePath))
                return (exePath, string.Empty);

            // No .exe found (single-file or framework-dependent without host) — use dotnet exec
            if (processPath is not null)
                return (processPath, $"exec \"{dllPath}\" ");
        }

        return (null, string.Empty);
    }

    // ── SYSTEM (probe multiple candidates) ──────────────────────────

    /// <summary>
    /// SYSTEM process candidates ordered by likelihood of being non-PPL.
    /// PPL (Protected Process Light) blocks OpenProcessToken even with SeDebugPrivilege.
    /// </summary>
    private static readonly string[] SystemProcessCandidates =
        ["spoolsv", "svchost", "services", "lsass", "winlogon"];

    private static SafeAccessTokenHandle? TryAcquireTokenFromProcesses(string[] processNames)
    {
        foreach (var name in processNames)
        {
            var procs = Process.GetProcessesByName(name);
            try
            {
                foreach (var proc in procs)
                {
                    var token = TryOpenAndDuplicateToken(proc.Id);
                    if (token is not null) return token;
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
        }

        return null;
    }

    private static SafeAccessTokenHandle? TryOpenAndDuplicateToken(int pid)
    {
        using var processHandle = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
        if (processHandle.IsInvalid) return null;

        if (!OpenProcessToken(processHandle, TOKEN_DUPLICATE, out var tokenHandle))
            return null;

        using (tokenHandle)
        {
            if (DuplicateTokenEx(
                    tokenHandle, MAXIMUM_ALLOWED, IntPtr.Zero,
                    SECURITY_IMPERSONATION, TOKEN_PRIMARY,
                    out var duplicated))
                return duplicated;

            return null;
        }
    }

    // ── TrustedInstaller ──────────────────────────────────────────────

    private static PrivilegeLaunchResult LaunchAsTrustedInstaller(string exe, string? args)
    {
        // Step 1: Ensure TrustedInstaller service is running
        var serviceResult = EnsureTrustedInstallerServiceRunning();
        if (!serviceResult.Success)
            return serviceResult;

        // Step 2: Launch with TrustedInstaller's token
        return LaunchWithTokenProbe(exe, args, ["TrustedInstaller"]);
    }

    private static PrivilegeLaunchResult EnsureTrustedInstallerServiceRunning()
    {
        var scManager = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scManager == IntPtr.Zero)
            return PrivilegeLaunchResult.Failed($"OpenSCManager failed: {Marshal.GetLastPInvokeError()}");

        try
        {
            var service = OpenService(scManager, "TrustedInstaller", SERVICE_START | SERVICE_QUERY_STATUS);
            if (service == IntPtr.Zero)
                return PrivilegeLaunchResult.Failed($"OpenService(TrustedInstaller) failed: {Marshal.GetLastPInvokeError()}");

            try
            {
                // Check current status
                if (!QueryServiceStatus(service, out var status))
                    return PrivilegeLaunchResult.Failed($"QueryServiceStatus failed: {Marshal.GetLastPInvokeError()}");

                if (status.dwCurrentState == SERVICE_RUNNING)
                    return PrivilegeLaunchResult.Ok(0); // Already running

                // Start the service
                if (!StartService(service, 0, IntPtr.Zero))
                {
                    var err = Marshal.GetLastPInvokeError();
                    if (err != 1056) // ERROR_SERVICE_ALREADY_RUNNING
                        return PrivilegeLaunchResult.Failed($"StartService(TrustedInstaller) failed: {err}");
                }

                // Wait for it to reach RUNNING state
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < MAX_SERVICE_WAIT_MS)
                {
                    if (QueryServiceStatus(service, out status) && status.dwCurrentState == SERVICE_RUNNING)
                        return PrivilegeLaunchResult.Ok(0);

                    Thread.Sleep(SERVICE_POLL_INTERVAL_MS);
                }

                return PrivilegeLaunchResult.Failed("TrustedInstaller service did not start within the timeout.");
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(scManager);
        }
    }

    // ── Token probe + process creation ──────────────────────────────

    /// <summary>
    /// Enables SeDebugPrivilege, probes candidate processes for an accessible token,
    /// duplicates it, and creates the target process under that token.
    /// </summary>
    private static PrivilegeLaunchResult LaunchWithTokenProbe(
        string exe, string? args, string[] processCandidates)
    {
        try
        {
            EnableDebugPrivilege();

            using var token = TryAcquireTokenFromProcesses(processCandidates);
            if (token is null || token.IsInvalid)
                return PrivilegeLaunchResult.Failed(
                    $"No accessible token found. Probed: {string.Join(", ", processCandidates)}. " +
                    "Ensure Heimdall is running as administrator.");

            var commandLine = string.IsNullOrWhiteSpace(args)
                ? $"\"{exe}\""
                : $"\"{exe}\" {args}";

            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };

            if (!CreateProcessWithTokenW(
                    token, LOGON_WITH_PROFILE, null, commandLine,
                    CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, null,
                    ref si, out var pi))
                return PrivilegeLaunchResult.Failed(
                    $"CreateProcessWithTokenW failed: {Marshal.GetLastPInvokeError()}");

            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);

            return PrivilegeLaunchResult.Ok(pi.dwProcessId);
        }
        catch (Exception ex)
        {
            return PrivilegeLaunchResult.Failed(ex.Message);
        }
    }

    private static void EnableDebugPrivilege()
    {
        const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        using var process = Process.GetCurrentProcess();
        using var processHandle = new SafeProcessHandle(process.Handle, ownsHandle: false);

        if (!OpenProcessToken(processHandle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var tokenHandle))
            throw new Win32Exception(Marshal.GetLastPInvokeError(),
                "Failed to open current process token.");

        using (tokenHandle)
        {
            if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid))
                throw new Win32Exception(Marshal.GetLastPInvokeError(),
                    "SeDebugPrivilege lookup failed.");

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                }
            };

            AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            var lastError = Marshal.GetLastPInvokeError();
            if (lastError == 1300) // ERROR_NOT_ALL_ASSIGNED
                throw new InvalidOperationException(
                    "SeDebugPrivilege is not available. Run as administrator.");
        }
    }
}
