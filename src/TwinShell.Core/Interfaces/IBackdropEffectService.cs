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

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Glass UI fallback level based on Windows version and accessibility settings.
/// Used to determine which visual effects are available.
/// </summary>
public enum GlassFallbackLevel
{
    /// <summary>
    /// Full glass effects: Mica, Acrylic, Reveal, animations.
    /// Windows 11 22H2+ with transparency enabled.
    /// </summary>
    Full,

    /// <summary>
    /// Partial effects: Mica only, no Acrylic blur.
    /// Windows 11 21H2 or transparency disabled.
    /// </summary>
    Partial,

    /// <summary>
    /// Minimal effects: Solid colors with subtle gradients.
    /// Windows 10 or reduced motion enabled.
    /// </summary>
    Minimal,

    /// <summary>
    /// No effects: Pure solid colors, no animations.
    /// High contrast mode or unsupported system.
    /// </summary>
    None
}

/// <summary>
/// Service for applying Windows 11 Mica/Acrylic backdrop effects to windows.
/// Provides graceful fallback for older Windows versions and accessibility settings.
/// </summary>
public interface IBackdropEffectService
{
    /// <summary>
    /// Gets whether Mica/Acrylic effects are supported on the current system.
    /// Returns false on Windows 10 or when transparency effects are disabled.
    /// </summary>
    bool IsBackdropEffectSupported { get; }

    /// <summary>
    /// Gets or sets whether backdrop effects are currently enabled.
    /// When disabled, windows use solid fallback colors.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Applies Mica backdrop effect to a window.
    /// Mica samples the desktop wallpaper once for excellent performance.
    /// Best suited for main application windows.
    /// </summary>
    /// <param name="windowHandle">The window handle (HWND).</param>
    /// <param name="isDarkMode">Whether to use dark mode appearance.</param>
    /// <returns>True if the effect was applied successfully.</returns>
    bool ApplyMica(IntPtr windowHandle, bool isDarkMode = false);

    /// <summary>
    /// Applies Acrylic backdrop effect to a window.
    /// Acrylic provides continuous blur effect with higher GPU usage.
    /// Best suited for transient windows like dialogs and popups.
    /// </summary>
    /// <param name="windowHandle">The window handle (HWND).</param>
    /// <param name="isDarkMode">Whether to use dark mode appearance.</param>
    /// <returns>True if the effect was applied successfully.</returns>
    bool ApplyAcrylic(IntPtr windowHandle, bool isDarkMode = false);

    /// <summary>
    /// Applies Mica Alt (tabbed) backdrop effect to a window.
    /// Optimized for tabbed interfaces.
    /// </summary>
    /// <param name="windowHandle">The window handle (HWND).</param>
    /// <param name="isDarkMode">Whether to use dark mode appearance.</param>
    /// <returns>True if the effect was applied successfully.</returns>
    bool ApplyMicaAlt(IntPtr windowHandle, bool isDarkMode = false);

    /// <summary>
    /// Disables backdrop effects on a window, returning to solid colors.
    /// </summary>
    /// <param name="windowHandle">The window handle (HWND).</param>
    /// <returns>True if the effect was disabled successfully.</returns>
    bool DisableBackdropEffect(IntPtr windowHandle);

    /// <summary>
    /// Checks if Windows transparency effects are enabled in accessibility settings.
    /// When disabled by the user, backdrop effects should be skipped.
    /// </summary>
    /// <returns>True if transparency effects are allowed.</returns>
    bool IsTransparencyEffectsEnabled();

    /// <summary>
    /// Gets the current Glass UI fallback level based on system capabilities
    /// and user accessibility settings.
    /// </summary>
    GlassFallbackLevel FallbackLevel { get; }

    /// <summary>
    /// Determines the appropriate fallback level for the current system.
    /// Considers Windows version, transparency settings, and reduced motion preference.
    /// </summary>
    /// <returns>The calculated fallback level.</returns>
    GlassFallbackLevel GetFallbackLevel();
}
