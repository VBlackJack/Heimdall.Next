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
using Heimdall.Core.Logging;

namespace Heimdall.App.Services;

/// <summary>
/// Watches foreground-window changes that leave the current process.
/// </summary>
public interface IForegroundWatchService : IDisposable
{
    /// <summary>
    /// Raised when the foreground window changes to a window owned by another
    /// process. Consumers must marshal to their UI thread before touching UI state.
    /// </summary>
    event EventHandler<IntPtr>? ForegroundChanged;

    /// <summary>Arms the foreground-window hook. No-op when already armed.</summary>
    void Start();

    /// <summary>Disarms the foreground-window hook. No-op when not armed.</summary>
    void Stop();
}

/// <summary>
/// Win32-backed implementation of <see cref="IForegroundWatchService"/>.
/// </summary>
public sealed class ForegroundWatchService : IForegroundWatchService
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    private readonly object _syncRoot = new();
    private readonly WinEventDelegate _winEventDelegate;
    private readonly uint _currentProcessId = (uint)Environment.ProcessId;
    private IntPtr _hook;
    private bool _disposed;

    /// <summary>
    /// Initializes a new foreground watcher and pins the WinEvent delegate for
    /// the lifetime of the service.
    /// </summary>
    public ForegroundWatchService()
    {
        _winEventDelegate = OnWinEvent;
    }

    /// <inheritdoc />
    public event EventHandler<IntPtr>? ForegroundChanged;

    /// <inheritdoc />
    public void Start()
    {
        lock (_syncRoot)
        {
            if (_disposed || _hook != IntPtr.Zero)
            {
                return;
            }

            _hook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WinEventOutOfContext);

            if (_hook == IntPtr.Zero)
            {
                FileLogger.Warn(
                    $"Foreground hook could not be armed. Win32 error={Marshal.GetLastWin32Error()}");
                return;
            }
        }

        FileLogger.Info("Foreground hook armed.");
    }

    /// <inheritdoc />
    public void Stop()
    {
        IntPtr hook;

        lock (_syncRoot)
        {
            if (_hook == IntPtr.Zero)
            {
                return;
            }

            hook = _hook;
            _hook = IntPtr.Zero;
        }

        if (!UnhookWinEvent(hook))
        {
            FileLogger.Warn(
                $"Foreground hook could not be disarmed. Win32 error={Marshal.GetLastWin32Error()}");
            return;
        }

        FileLogger.Info("Foreground hook disarmed.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType != EventSystemForeground || hwnd == IntPtr.Zero)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_hook == IntPtr.Zero || hWinEventHook != _hook)
            {
                return;
            }
        }

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || processId == _currentProcessId)
        {
            return;
        }

        FileLogger.Info(
            $"Cross-process foreground change detected. hwnd=0x{hwnd.ToInt64():X}, processId={processId}");
        ForegroundChanged?.Invoke(this, hwnd);
    }

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
