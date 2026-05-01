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

namespace Heimdall.App.Tests;

public sealed class FullscreenShortcutRouterTests
{
    [Fact]
    public void Resolve_Escape_WhenFullscreen_ExitsFullscreen()
    {
        var action = FullscreenShortcutRouter.Resolve(
            Key.Escape,
            ModifierKeys.None,
            isFullscreen: true);

        Assert.Equal(FullscreenShortcutAction.ExitFullscreen, action);
    }

    [Fact]
    public void Resolve_Escape_WhenWindowed_DoesNotIntercept()
    {
        var action = FullscreenShortcutRouter.Resolve(
            Key.Escape,
            ModifierKeys.None,
            isFullscreen: false);

        Assert.Equal(FullscreenShortcutAction.None, action);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_CtrlShiftF11_TogglesInBothDirections(bool isFullscreen)
    {
        var action = FullscreenShortcutRouter.Resolve(
            Key.F11,
            ModifierKeys.Control | ModifierKeys.Shift,
            isFullscreen);

        Assert.Equal(FullscreenShortcutAction.ToggleFullscreen, action);
    }

    [Fact]
    public void Resolve_PlainF11_WhenWindowed_EntersFullscreen()
    {
        var action = FullscreenShortcutRouter.Resolve(
            Key.F11,
            ModifierKeys.None,
            isFullscreen: false);

        Assert.Equal(FullscreenShortcutAction.EnterFullscreen, action);
    }

    [Fact]
    public void Resolve_PlainF11_WhenFullscreen_ExitsFullscreen()
    {
        var action = FullscreenShortcutRouter.Resolve(
            Key.F11,
            ModifierKeys.None,
            isFullscreen: true);

        Assert.Equal(FullscreenShortcutAction.ExitFullscreen, action);
    }

    [Theory]
    [InlineData(ModifierKeys.Alt)]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Shift)]
    [InlineData(ModifierKeys.Windows)]
    public void Resolve_ModifiedPlainF11_PassesThrough(ModifierKeys modifiers)
    {
        var action = FullscreenShortcutRouter.Resolve(
            Key.F11,
            modifiers,
            isFullscreen: true);

        Assert.Equal(FullscreenShortcutAction.None, action);
    }
}
