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
using System.Windows.Input;

namespace Heimdall.App.Services;

/// <summary>
/// Thin WH_KEYBOARD_LL wrapper used so shell fullscreen accelerators still
/// work when the ActiveX child owns keyboard focus.
/// </summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;
    private const int KeyDownStateMask = 0x8000;

    private readonly Func<Key, ModifierKeys, bool> _onKeyDown;
    private readonly HookProc _hookProc;
    private readonly uint _ownProcessId;
    private IntPtr _hookHandle;

    private LowLevelKeyboardHook(Func<Key, ModifierKeys, bool> onKeyDown)
    {
        _onKeyDown = onKeyDown;
        _hookProc = HookCallback;
        _ownProcessId = (uint)Environment.ProcessId;

        var moduleHandle = GetModuleHandle(null);
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, moduleHandle, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install low-level keyboard hook.");
        }
    }

    ~LowLevelKeyboardHook()
    {
        Dispose(disposing: false);
    }

    public static IDisposable Install(Func<Key, ModifierKeys, bool> onKeyDown)
    {
        ArgumentNullException.ThrowIfNull(onKeyDown);
        return new LowLevelKeyboardHook(onKeyDown);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        var hookHandle = _hookHandle;
        if (hookHandle == IntPtr.Zero)
        {
            return;
        }

        _hookHandle = IntPtr.Zero;
        _ = UnhookWindowsHookEx(hookHandle);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (!IsOwnProcessForeground())
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        if (nCode < 0 || (message != WmKeyDown && message != WmSysKeyDown))
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var keyInfo = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var key = KeyInterop.KeyFromVirtualKey((int)keyInfo.VkCode);
        var modifiers = ReadCurrentModifiers();

        if (_onKeyDown(key, modifiers))
        {
            return 1;
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static ModifierKeys ReadCurrentModifiers()
    {
        var modifiers = ModifierKeys.None;

        if (IsKeyDown(VkControl))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (IsKeyDown(VkShift))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (IsKeyDown(VkMenu))
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (IsKeyDown(VkLwin) || IsKeyDown(VkRwin))
        {
            modifiers |= ModifierKeys.Windows;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey)
        => (GetAsyncKeyState(virtualKey) & KeyDownStateMask) != 0;

    private bool IsOwnProcessForeground()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId);
        return foregroundProcessId == _ownProcessId;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KbdLlHookStruct
    {
        public readonly uint VkCode;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        HookProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
