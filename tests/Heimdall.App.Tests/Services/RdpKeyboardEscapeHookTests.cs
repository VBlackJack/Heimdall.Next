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

using System.Windows.Input;
using Heimdall.App.Services;

namespace Heimdall.App.Tests.Services;

public sealed class RdpKeyboardEscapeHookTests : IDisposable
{
    public RdpKeyboardEscapeHookTests()
    {
        RdpKeyboardEscapeHook.ResetForTests();
    }

    public void Dispose()
    {
        RdpKeyboardEscapeHook.ResetForTests();
    }

    [Fact]
    public void IsRegisteredRdpViewFocused_NoRegisteredViews_ReturnsFalse()
    {
        Assert.False(RdpKeyboardEscapeHook.IsRegisteredRdpViewFocused());
    }

    [Fact]
    public void RegisterAndUnregister_InstallOnlyFirstAndUninstallOnlyLast()
    {
        var installEvents = new List<bool>();
        RdpKeyboardEscapeHook.InstallProbe = installEvents.Add;
        var firstView = new object();
        var secondView = new object();

        Assert.True(RdpKeyboardEscapeHook.RegisterForTests(firstView));
        Assert.True(RdpKeyboardEscapeHook.RegisterForTests(secondView));

        Assert.Equal(2, RdpKeyboardEscapeHook.RegisteredViewCount);
        Assert.Equal([true], installEvents);

        RdpKeyboardEscapeHook.UnregisterForTests(firstView);

        Assert.Equal(1, RdpKeyboardEscapeHook.RegisteredViewCount);
        Assert.Equal([true], installEvents);

        RdpKeyboardEscapeHook.UnregisterForTests(secondView);

        Assert.Equal(0, RdpKeyboardEscapeHook.RegisteredViewCount);
        Assert.Equal([true, false], installEvents);
    }

    [Fact]
    public void Register_SameViewTwice_DoesNotDoubleCount()
    {
        var installEvents = new List<bool>();
        RdpKeyboardEscapeHook.InstallProbe = installEvents.Add;
        var view = new object();

        Assert.True(RdpKeyboardEscapeHook.RegisterForTests(view));
        Assert.True(RdpKeyboardEscapeHook.RegisterForTests(view));

        Assert.Equal(1, RdpKeyboardEscapeHook.RegisteredViewCount);
        Assert.Equal([true], installEvents);

        RdpKeyboardEscapeHook.UnregisterForTests(view);

        Assert.Equal(0, RdpKeyboardEscapeHook.RegisteredViewCount);
        Assert.Equal([true, false], installEvents);
    }

    [Fact]
    public void Register_DuplicateShortcuts_LogsSingleWarningAndKeepsRegistration()
    {
        var installEvents = new List<bool>();
        var warnings = new List<string>();
        RdpKeyboardEscapeHook.InstallProbe = installEvents.Add;
        RdpKeyboardEscapeHook.WarningProbe = warnings.Add;

        var shortcuts = new RdpHookShortcuts("Ctrl+Alt+Home", "Ctrl+Alt+Home");

        Assert.True(RdpKeyboardEscapeHook.RegisterForTests(new object(), shortcuts));
        Assert.True(RdpKeyboardEscapeHook.RegisterForTests(new object(), shortcuts));

        Assert.Equal(2, RdpKeyboardEscapeHook.RegisteredViewCount);
        Assert.Equal([true], installEvents);
        Assert.Single(warnings);
        Assert.Contains("release-focus", warnings[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("precedence", warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShortcutRouter_CtrlK_PassesThroughToLowLevelHook()
    {
        var action = RdpKeyboardHookShortcutRouter.Resolve(
            Key.K,
            ModifierKeys.Control,
            RdpShortcutParser.DefaultShortcut,
            RdpShortcutParser.DefaultFullscreenShortcut);

        Assert.Equal(RdpKeyboardHookAction.None, action);
    }

    [Fact]
    public void ShortcutRouter_CtrlK_WhenReleaseFocusUsesCtrlK_ReleasesFocus()
    {
        var action = RdpKeyboardHookShortcutRouter.Resolve(
            Key.K,
            ModifierKeys.Control,
            new RdpShortcut(ModifierKeys.Control, Key.K),
            RdpShortcutParser.DefaultFullscreenShortcut);

        Assert.Equal(RdpKeyboardHookAction.ReleaseFocus, action);
    }

    [Fact]
    public void ShortcutRouter_CtrlShiftK_PassesThrough()
    {
        var action = RdpKeyboardHookShortcutRouter.Resolve(
            Key.K,
            ModifierKeys.Control | ModifierKeys.Shift,
            RdpShortcutParser.DefaultShortcut,
            RdpShortcutParser.DefaultFullscreenShortcut);

        Assert.Equal(RdpKeyboardHookAction.None, action);
    }

    [Fact]
    public void ShortcutRouter_F11_PreservesFullscreenToggle()
    {
        var action = RdpKeyboardHookShortcutRouter.Resolve(
            Key.F11,
            ModifierKeys.None,
            RdpShortcutParser.DefaultShortcut,
            RdpShortcutParser.DefaultFullscreenShortcut);

        Assert.Equal(RdpKeyboardHookAction.ToggleFullscreen, action);
    }
}
