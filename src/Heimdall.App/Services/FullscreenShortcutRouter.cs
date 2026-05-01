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

namespace Heimdall.App.Services;

public enum FullscreenShortcutAction
{
    None,
    EnterFullscreen,
    ExitFullscreen,
    ToggleFullscreen
}

public static class FullscreenShortcutRouter
{
    // TODO: AppSettings currently exposes RdpFullscreenToggleShortcut and
    // RdpReleaseFocusShortcut, but they are not wired into this router. Wire
    // or retire those fields when fullscreen shortcuts become user-configurable.
    public static FullscreenShortcutAction Resolve(
        Key key,
        ModifierKeys modifiers,
        bool isFullscreen)
    {
        if (key == Key.Escape && modifiers == ModifierKeys.None && isFullscreen)
        {
            return FullscreenShortcutAction.ExitFullscreen;
        }

        if (key == Key.F11 && modifiers == ModifierKeys.None)
        {
            return isFullscreen
                ? FullscreenShortcutAction.ExitFullscreen
                : FullscreenShortcutAction.EnterFullscreen;
        }

        if (key == Key.F11 && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            return FullscreenShortcutAction.ToggleFullscreen;
        }

        return FullscreenShortcutAction.None;
    }
}
