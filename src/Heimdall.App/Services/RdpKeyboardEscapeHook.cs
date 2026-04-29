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
using System.Windows.Input;
using System.Windows.Threading;
using Heimdall.App.Views;
using Heimdall.Core.Logging;

namespace Heimdall.App.Services;

/// <summary>
/// Thread-local keyboard hook that lets users release focus from the embedded
/// RDP ActiveX surface with the mstsc-compatible Ctrl+Alt+Home shortcut.
/// </summary>
internal static class RdpKeyboardEscapeHook
{
    private const int WhKeyboard = 2;
    private const int HcAction = 0;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<object, RegisteredView> RegisteredViews = new();
    private static readonly KeyboardHookProc HookProc = OnKeyboardHook;

    private static IntPtr _hookHandle;
    private static uint _hookThreadId;
    private static bool _probeInstalled;
    private static RdpShortcut _shortcut = RdpShortcutParser.DefaultShortcut;

    internal static Action<bool>? InstallProbe { get; set; }

    internal static int RegisteredViewCount
    {
        get
        {
            lock (SyncRoot)
            {
                return RegisteredViews.Count;
            }
        }
    }

    public static bool Register(EmbeddedRdpView view, string? shortcut = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        return RegisterCore(view, view, shortcut);
    }

    public static void Unregister(EmbeddedRdpView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        UnregisterCore(view);
    }

    internal static bool RegisterForTests(object viewKey, string? shortcut = null)
    {
        ArgumentNullException.ThrowIfNull(viewKey);
        return RegisterCore(viewKey, null, shortcut);
    }

    internal static void UnregisterForTests(object viewKey)
    {
        ArgumentNullException.ThrowIfNull(viewKey);
        UnregisterCore(viewKey);
    }

    internal static void ResetForTests()
    {
        lock (SyncRoot)
        {
            RegisteredViews.Clear();
            _hookHandle = IntPtr.Zero;
            _hookThreadId = 0;
            _probeInstalled = false;
            _shortcut = RdpShortcutParser.DefaultShortcut;
            InstallProbe = null;
        }
    }

    private static bool RegisterCore(object viewKey, EmbeddedRdpView? view, string? shortcut)
    {
        lock (SyncRoot)
        {
            if (RegisteredViews.ContainsKey(viewKey))
            {
                return true;
            }

            _shortcut = RdpShortcutParser.ParseOrDefault(shortcut);

            if (RegisteredViews.Count == 0 && !InstallHook())
            {
                return false;
            }

            RegisteredViews.Add(viewKey, new RegisteredView(view));
            return true;
        }
    }

    private static void UnregisterCore(object viewKey)
    {
        lock (SyncRoot)
        {
            if (!RegisteredViews.Remove(viewKey) || RegisteredViews.Count > 0)
            {
                return;
            }

            UninstallHook();
        }
    }

    private static bool InstallHook()
    {
        if (InstallProbe is not null)
        {
            InstallProbe(true);
            _probeInstalled = true;
            return true;
        }

        _hookThreadId = GetCurrentThreadId();
        _hookHandle = SetWindowsHookEx(WhKeyboard, HookProc, IntPtr.Zero, _hookThreadId);
        if (_hookHandle == IntPtr.Zero)
        {
            FileLogger.Error(
                $"RDP keyboard escape hook could not be installed. Win32 error={Marshal.GetLastWin32Error()}");
            _hookThreadId = 0;
            return false;
        }

        FileLogger.Info($"RDP keyboard escape hook installed on thread {_hookThreadId}.");
        return true;
    }

    private static void UninstallHook()
    {
        if (InstallProbe is not null)
        {
            if (_probeInstalled)
            {
                InstallProbe(false);
            }

            _probeInstalled = false;
            return;
        }

        if (_hookHandle == IntPtr.Zero)
        {
            _hookThreadId = 0;
            return;
        }

        var hook = _hookHandle;
        _hookHandle = IntPtr.Zero;
        _hookThreadId = 0;

        if (!UnhookWindowsHookEx(hook))
        {
            FileLogger.Error(
                $"RDP keyboard escape hook could not be uninstalled. Win32 error={Marshal.GetLastWin32Error()}");
            return;
        }

        FileLogger.Info("RDP keyboard escape hook uninstalled.");
    }

    private static IntPtr OnKeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HcAction && IsKeyDown(lParam) && MatchesShortcut(wParam))
        {
            var view = FindFocusedRdpView();
            if (view is not null)
            {
                _ = view.Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(view.FocusRdpToolbarFromEscapeHook));
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private static bool IsKeyDown(IntPtr lParam)
    {
        return ((lParam.ToInt64() >> 31) & 1) == 0;
    }

    private static bool MatchesShortcut(IntPtr wParam)
    {
        return wParam.ToInt32() == _shortcut.VirtualKey
            && IsCurrentModifierState(_shortcut.Modifiers);
    }

    private static bool IsCurrentModifierState(ModifierKeys expected)
    {
        var current = ModifierKeys.None;
        if (IsVirtualKeyPressed(VkControl))
        {
            current |= ModifierKeys.Control;
        }

        if (IsVirtualKeyPressed(VkMenu))
        {
            current |= ModifierKeys.Alt;
        }

        if (IsVirtualKeyPressed(VkShift))
        {
            current |= ModifierKeys.Shift;
        }

        if (IsVirtualKeyPressed(VkLwin) || IsVirtualKeyPressed(VkRwin))
        {
            current |= ModifierKeys.Windows;
        }

        return current == expected;
    }

    private static bool IsVirtualKeyPressed(int virtualKey)
    {
        return (GetKeyState(virtualKey) & 0x8000) != 0;
    }

    private static EmbeddedRdpView? FindFocusedRdpView()
    {
        var focusedHwnd = GetFocus();
        if (focusedHwnd == IntPtr.Zero)
        {
            return null;
        }

        List<EmbeddedRdpView> views;
        lock (SyncRoot)
        {
            views = RegisteredViews.Values
                .Select(static registration => registration.View)
                .Where(static view => view is not null)
                .Cast<EmbeddedRdpView>()
                .ToList();
        }

        foreach (var view in views)
        {
            var hostHandle = view.GetRdpKeyboardInputHandle();
            if (hostHandle == IntPtr.Zero)
            {
                continue;
            }

            if (focusedHwnd == hostHandle || IsChild(hostHandle, focusedHwnd))
            {
                return view;
            }
        }

        return null;
    }

    private sealed record RegisteredView(EmbeddedRdpView? View);

    private delegate IntPtr KeyboardHookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        KeyboardHookProc lpfn,
        IntPtr hmod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
}

internal readonly record struct RdpShortcut(ModifierKeys Modifiers, Key Key)
{
    public int VirtualKey => KeyInterop.VirtualKeyFromKey(Key);
}

internal static class RdpShortcutParser
{
    private static readonly KeyConverter KeyConverter = new();
    private static readonly ModifierKeysConverter ModifierKeysConverter = new();

    public static RdpShortcut DefaultShortcut { get; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Home);

    public static RdpShortcut ParseOrDefault(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return DefaultShortcut;
        }

        try
        {
            var parts = shortcut.Split(
                    '+',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            if (parts.Length < 2)
            {
                return Fallback(shortcut);
            }

            var modifiers = ModifierKeys.None;
            for (var index = 0; index < parts.Length - 1; index++)
            {
                modifiers |= ParseModifier(parts[index]);
            }

            if (modifiers == ModifierKeys.None)
            {
                return Fallback(shortcut);
            }

            if (KeyConverter.ConvertFromInvariantString(parts[^1]) is not Key key || key == Key.None)
            {
                return Fallback(shortcut);
            }

            return new RdpShortcut(modifiers, key);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            return Fallback(shortcut);
        }
    }

    private static ModifierKeys ParseModifier(string value)
    {
        var normalized = value.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
            ? "Control"
            : value;

        return ModifierKeysConverter.ConvertFromInvariantString(normalized) is ModifierKeys modifier
            ? modifier
            : ModifierKeys.None;
    }

    private static RdpShortcut Fallback(string shortcut)
    {
        FileLogger.Warn(
            $"Invalid RDP release focus shortcut '{shortcut}'. Falling back to Ctrl+Alt+Home.");
        return DefaultShortcut;
    }
}
