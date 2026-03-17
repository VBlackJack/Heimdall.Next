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
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.Terminal.ConPty;

/// <summary>
/// Terminal session backed by the Windows Pseudo Console (ConPTY) API.
/// Manages the full lifecycle: pipe creation, pseudo console allocation,
/// process launch, async output reading, resize, and cleanup.
/// </summary>
public sealed class ConPtySession : ITerminalSession
{
    private SafePseudoConsoleHandle? _pseudoConsole;
    private IntPtr _processHandle;
    private IntPtr _threadHandle;
    private IntPtr _attrList;
    private int _processId;

    // Pipe endpoints owned by this session:
    // - _pipeInputRead / _pipeOutputWrite are the child-side ends passed to ConPTY,
    //   kept alive for the console's lifetime.
    // - _outputReader / _inputWriter are the parent-side FileStreams.
    private SafeFileHandle? _pipeInputRead;
    private SafeFileHandle? _pipeOutputWrite;
    private FileStream? _outputReader;
    private FileStream? _inputWriter;

    private Task? _readLoop;
    private CancellationTokenSource? _cts;

    private volatile bool _disposed;
    private readonly object _disposeLock = new();

    /// <inheritdoc />
    public event Action<ReadOnlyMemory<byte>>? DataReceived;

    /// <inheritdoc />
    public event Action<int>? ProcessExited;

    /// <inheritdoc />
    public bool IsRunning
    {
        get
        {
            if (_disposed || _processHandle == IntPtr.Zero)
                return false;

            NativeMethods.GetExitCodeProcess(_processHandle, out uint exitCode);
            return exitCode == NativeMethods.STILL_ACTIVE;
        }
    }

    /// <inheritdoc />
    public int? ProcessId => _disposed ? null : _processId == 0 ? null : _processId;

    /// <summary>
    /// Returns true if the ConPTY API is available on this Windows version
    /// (Windows 10 1809+ / Windows Server 2019+).
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                IntPtr module = NativeMethods.GetModuleHandleW("kernel32.dll");
                if (module == IntPtr.Zero)
                    return false;
                return NativeMethods.GetProcAddress(module, "CreatePseudoConsole") != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(string executable, string arguments, int columns = 80, int rows = 24, string? workingDirectory = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        if (_processHandle != IntPtr.Zero)
            throw new InvalidOperationException("Session already started. Dispose and create a new instance.");

        CreatePipes(out SafeFileHandle inputRead, out SafeFileHandle inputWrite,
                    out SafeFileHandle outputRead, out SafeFileHandle outputWrite);

        // Keep child-side handles alive — ConPTY needs them for the console's lifetime.
        _pipeInputRead = inputRead;
        _pipeOutputWrite = outputWrite;

        try
        {
            CreatePseudoConsole(columns, rows, inputRead, outputWrite);
            SetupProcessAttributeList();

            // Wrap parent-side pipe ends in FileStreams for managed I/O.
            const int bufferSize = 4096;
            _inputWriter = new FileStream(inputWrite, FileAccess.Write, bufferSize);
            _outputReader = new FileStream(outputRead, FileAccess.Read, bufferSize);

            LaunchProcess(executable, arguments, workingDirectory);
            StartReadLoop();
        }
        catch
        {
            Dispose();
            throw;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Write(ReadOnlySpan<byte> data)
    {
        if (_disposed || _inputWriter is null)
            return;

        try
        {
            _inputWriter.Write(data);
            _inputWriter.Flush();
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        if (_disposed || _inputWriter is null || string.IsNullOrEmpty(text))
            return;

        int maxBytes = Encoding.UTF8.GetMaxByteCount(text.Length);
        Span<byte> buffer = maxBytes <= 1024
            ? stackalloc byte[maxBytes]
            : new byte[maxBytes];

        int written = Encoding.UTF8.GetBytes(text, buffer);
        Write(buffer[..written]);
    }

    /// <inheritdoc />
    public void Resize(int columns, int rows)
    {
        if (_disposed || _pseudoConsole is null || _pseudoConsole.IsInvalid)
            return;

        if (columns <= 0 || rows <= 0)
            throw new ArgumentOutOfRangeException(
                columns <= 0 ? nameof(columns) : nameof(rows),
                "Terminal dimensions must be positive.");

        var size = new NativeMethods.COORD((short)columns, (short)rows);
        int hr = NativeMethods.ResizePseudoConsole(_pseudoConsole.DangerousGetHandle(), size);
        if (hr != 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    /// <inheritdoc />
    public void Kill()
    {
        if (_disposed || _processHandle == IntPtr.Zero)
            return;

        NativeMethods.GetExitCodeProcess(_processHandle, out uint exitCode);
        if (exitCode == NativeMethods.STILL_ACTIVE)
        {
            NativeMethods.TerminateProcess(_processHandle, 1);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        // Signal the read loop to stop.
        _cts?.Cancel();

        // Close pseudo console first — this signals the child process to exit
        // and unblocks any pending read on the output pipe.
        _pseudoConsole?.Dispose();
        _pseudoConsole = null;

        // Close managed streams (parent-side pipe ends).
        DisposeStream(ref _inputWriter);
        DisposeStream(ref _outputReader);

        // Close child-side pipe ends kept alive for the console.
        _pipeInputRead?.Dispose();
        _pipeInputRead = null;
        _pipeOutputWrite?.Dispose();
        _pipeOutputWrite = null;

        // Terminate the child process if still running.
        TerminateAndCloseProcess();

        // Free the attribute list.
        FreeAttributeList();

        // Wait briefly for the read loop task to complete.
        if (_readLoop is not null)
        {
            try { _readLoop.Wait(TimeSpan.FromSeconds(2)); }
            catch { /* Best-effort wait for clean shutdown. */ }
        }

        _cts?.Dispose();
        _cts = null;
    }

    // ========================================================================
    // Private helpers
    // ========================================================================

    private static void CreatePipes(
        out SafeFileHandle inputRead, out SafeFileHandle inputWrite,
        out SafeFileHandle outputRead, out SafeFileHandle outputWrite)
    {
        var sa = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = (uint)Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            bInheritHandle = 1 // TRUE
        };

        if (!NativeMethods.CreatePipe(out inputRead, out inputWrite, ref sa, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create input pipe.");

        if (!NativeMethods.CreatePipe(out outputRead, out outputWrite, ref sa, 0))
        {
            inputRead.Dispose();
            inputWrite.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create output pipe.");
        }
    }

    private void CreatePseudoConsole(
        int columns, int rows, SafeFileHandle inputRead, SafeFileHandle outputWrite)
    {
        var size = new NativeMethods.COORD((short)columns, (short)rows);
        int hr = NativeMethods.CreatePseudoConsole(size, inputRead, outputWrite, 0, out IntPtr hPC);
        if (hr != 0)
            Marshal.ThrowExceptionForHR(hr);

        _pseudoConsole = new SafePseudoConsoleHandle(hPC);
    }

    private void SetupProcessAttributeList()
    {
        nint attrSize = 0;
        // First call: query required buffer size (expected to fail).
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);

        _attrList = Marshal.AllocHGlobal((int)attrSize);
        if (!NativeMethods.InitializeProcThreadAttributeList(_attrList, 1, 0, ref attrSize))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed.");

        if (!NativeMethods.UpdateProcThreadAttribute(
            _attrList,
            0,
            NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            _pseudoConsole!.DangerousGetHandle(),
            (nuint)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed.");
        }
    }

    private void LaunchProcess(string executable, string arguments, string? workingDirectory = null)
    {
        string cmdLine = string.IsNullOrEmpty(arguments)
            ? $"\"{executable}\""
            : $"\"{executable}\" {arguments}";

        var si = new NativeMethods.STARTUPINFOEX
        {
            lpAttributeList = _attrList
        };
        si.StartupInfo.cb = (uint)Marshal.SizeOf<NativeMethods.STARTUPINFOEX>();

        uint flags = NativeMethods.EXTENDED_STARTUPINFO_PRESENT
                   | NativeMethods.CREATE_UNICODE_ENVIRONMENT;

        string? cwd = !string.IsNullOrWhiteSpace(workingDirectory) ? workingDirectory : null;

        if (!NativeMethods.CreateProcessW(
            null, cmdLine, IntPtr.Zero, IntPtr.Zero,
            false, flags, IntPtr.Zero, cwd,
            ref si, out NativeMethods.PROCESS_INFORMATION pi))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW failed.");
        }

        _processHandle = pi.hProcess;
        _threadHandle = pi.hThread;
        _processId = (int)pi.dwProcessId;
    }

    private void StartReadLoop()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var reader = _outputReader!;

        _readLoop = Task.Run(async () =>
        {
            byte[] buffer = new byte[4096];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int bytesRead = await reader.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (bytesRead <= 0)
                        break;

                    // Deliver a copy to subscribers so the buffer can be reused.
                    var copy = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, copy, 0, bytesRead);
                    DataReceived?.Invoke(copy.AsMemory());
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (IOException) { }

            // Raise ProcessExited with the child's exit code.
            if (!_disposed)
            {
                int exitCode = -1;
                if (_processHandle != IntPtr.Zero)
                {
                    // Give the process a moment to finalize.
                    NativeMethods.WaitForSingleObject(_processHandle, 1000);
                    if (NativeMethods.GetExitCodeProcess(_processHandle, out uint ec))
                        exitCode = (int)ec;
                }
                ProcessExited?.Invoke(exitCode);
            }
        }, token);
    }

    private void TerminateAndCloseProcess()
    {
        if (_processHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.GetExitCodeProcess(_processHandle, out uint exitCode);
                if (exitCode == NativeMethods.STILL_ACTIVE)
                    NativeMethods.TerminateProcess(_processHandle, 1);
            }
            catch { /* Best-effort termination during cleanup. */ }

            NativeMethods.CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }

        if (_threadHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_threadHandle);
            _threadHandle = IntPtr.Zero;
        }
    }

    private void FreeAttributeList()
    {
        if (_attrList != IntPtr.Zero)
        {
            NativeMethods.DeleteProcThreadAttributeList(_attrList);
            Marshal.FreeHGlobal(_attrList);
            _attrList = IntPtr.Zero;
        }
    }

    private static void DisposeStream<T>(ref T? stream) where T : Stream
    {
        if (stream is null)
            return;

        try { stream.Dispose(); }
        catch { /* Best-effort stream cleanup. */ }
        stream = null;
    }
}
