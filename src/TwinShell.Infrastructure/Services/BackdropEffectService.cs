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

using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TwinShell.Core.Interfaces;
using TwinShell.Infrastructure.Interop;

namespace TwinShell.Infrastructure.Services;

/// <summary>
/// Service for applying Windows 11 Mica/Acrylic backdrop effects.
/// Automatically falls back to solid colors on unsupported systems or when
/// accessibility settings disable transparency effects.
/// </summary>
public class BackdropEffectService : IBackdropEffectService
{
    private readonly ILogger<BackdropEffectService>? _logger;
    private readonly ISettingsService? _settingsService;
    private bool _isEnabled = true;
    private bool? _isSupported;
    private GlassFallbackLevel? _fallbackLevel;

    /// <summary>
    /// Windows 11 minimum build number for Mica support (21H2).
    /// </summary>
    private const int Windows11MinBuild = 22000;

    /// <summary>
    /// Windows 11 22H2 build number for improved Mica API.
    /// </summary>
    private const int Windows11_22H2Build = 22621;

    public BackdropEffectService(
        ILogger<BackdropEffectService>? logger = null,
        ISettingsService? settingsService = null)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    /// <inheritdoc/>
    public bool IsBackdropEffectSupported
    {
        get
        {
            if (_isSupported.HasValue)
                return _isSupported.Value;

            _isSupported = CheckBackdropSupport();
            return _isSupported.Value;
        }
    }

    /// <inheritdoc/>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <inheritdoc/>
    public bool ApplyMica(IntPtr windowHandle, bool isDarkMode = false)
    {
        return ApplyBackdropEffect(windowHandle, DwmApi.DWMSBT_MAINWINDOW, isDarkMode, "Mica");
    }

    /// <inheritdoc/>
    public bool ApplyAcrylic(IntPtr windowHandle, bool isDarkMode = false)
    {
        return ApplyBackdropEffect(windowHandle, DwmApi.DWMSBT_TRANSIENTWINDOW, isDarkMode, "Acrylic");
    }

    /// <inheritdoc/>
    public bool ApplyMicaAlt(IntPtr windowHandle, bool isDarkMode = false)
    {
        return ApplyBackdropEffect(windowHandle, DwmApi.DWMSBT_TABBEDWINDOW, isDarkMode, "Mica Alt");
    }

    /// <inheritdoc/>
    public bool DisableBackdropEffect(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            int disableValue = DwmApi.DWMSBT_DISABLE;
            int result = DwmApi.DwmSetWindowAttribute(
                windowHandle,
                DwmApi.DWMWA_SYSTEMBACKDROP_TYPE,
                ref disableValue,
                sizeof(int));

            bool success = result == 0;
            _logger?.LogDebug("Backdrop effect disabled: {Success}", success);
            return success;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to disable backdrop effect");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool IsTransparencyEffectsEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            // Check Windows Settings: Personalization > Colors > Transparency effects
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            if (key != null)
            {
                var value = key.GetValue("EnableTransparency");
                if (value is int intValue)
                {
                    return intValue == 1;
                }
            }

            // Default to enabled if registry key not found
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to check transparency effects setting");
            return true; // Default to enabled
        }
    }

    /// <inheritdoc/>
    public GlassFallbackLevel FallbackLevel
    {
        get
        {
            if (_fallbackLevel.HasValue)
                return _fallbackLevel.Value;

            _fallbackLevel = GetFallbackLevel();
            return _fallbackLevel.Value;
        }
    }

    /// <inheritdoc/>
    public GlassFallbackLevel GetFallbackLevel()
    {
        // Check if high contrast mode is enabled
        if (IsHighContrastEnabled())
        {
            _logger?.LogInformation("High contrast mode detected, using None fallback level");
            return GlassFallbackLevel.None;
        }

        // Check if reduced motion is enabled
        if (_settingsService?.CurrentSettings?.ReducedMotion == true)
        {
            _logger?.LogInformation("Reduced motion enabled, using Minimal fallback level");
            return GlassFallbackLevel.Minimal;
        }

        // Check Windows version
        if (!IsBackdropEffectSupported)
        {
            _logger?.LogInformation("Windows 10 or earlier detected, using Minimal fallback level");
            return GlassFallbackLevel.Minimal;
        }

        // Check if transparency effects are enabled
        if (!IsTransparencyEffectsEnabled())
        {
            _logger?.LogInformation("Transparency effects disabled, using Partial fallback level");
            return GlassFallbackLevel.Partial;
        }

        // Check for Windows 11 22H2+ (full support)
        if (IsWindows11_22H2OrLater())
        {
            _logger?.LogInformation("Windows 11 22H2+ detected, using Full fallback level");
            return GlassFallbackLevel.Full;
        }

        // Windows 11 21H2 - partial support (no Acrylic blur)
        _logger?.LogInformation("Windows 11 21H2 detected, using Partial fallback level");
        return GlassFallbackLevel.Partial;
    }

    /// <summary>
    /// Checks if High Contrast mode is enabled in Windows.
    /// </summary>
    private bool IsHighContrastEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Control Panel\Accessibility\HighContrast");

            if (key != null)
            {
                var flags = key.GetValue("Flags");
                if (flags is string flagsStr && int.TryParse(flagsStr, out int flagValue))
                {
                    // HCF_HIGHCONTRASTON = 0x00000001
                    return (flagValue & 0x00000001) != 0;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to check high contrast mode");
            return false;
        }
    }

    /// <summary>
    /// Checks if the current Windows version is 11 22H2 or later.
    /// </summary>
    private bool IsWindows11_22H2OrLater()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var version = Environment.OSVersion;
            return version.Version.Major >= 10 && version.Version.Build >= Windows11_22H2Build;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applies a backdrop effect to the specified window.
    /// </summary>
    private bool ApplyBackdropEffect(IntPtr windowHandle, int backdropType, bool isDarkMode, string effectName)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger?.LogDebug("Backdrop effects not supported on non-Windows platforms");
            return false;
        }

        if (windowHandle == IntPtr.Zero)
        {
            _logger?.LogWarning("Invalid window handle provided for {EffectName} effect", effectName);
            return false;
        }

        // Check if effects are enabled
        if (!_isEnabled)
        {
            _logger?.LogDebug("{EffectName} effect skipped: effects are disabled", effectName);
            return false;
        }

        // Check Windows version support
        if (!IsBackdropEffectSupported)
        {
            _logger?.LogDebug("{EffectName} effect not supported on this Windows version", effectName);
            return false;
        }

        // Check accessibility settings
        if (!IsTransparencyEffectsEnabled())
        {
            _logger?.LogInformation("{EffectName} effect skipped: transparency disabled by user", effectName);
            return false;
        }

        // Check reduced motion setting
        if (_settingsService?.CurrentSettings?.ReducedMotion == true)
        {
            _logger?.LogDebug("{EffectName} effect skipped: reduced motion enabled", effectName);
            return false;
        }

        try
        {
            // Set dark mode first to avoid visual artifacts
            int darkModeValue = isDarkMode ? 1 : 0;
            DwmApi.DwmSetWindowAttribute(
                windowHandle,
                DwmApi.DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref darkModeValue,
                sizeof(int));

            // Extend frame into client area for proper Mica rendering
            var margins = DwmApi.MARGINS.ExtendAll;
            DwmApi.DwmExtendFrameIntoClientArea(windowHandle, ref margins);

            // Apply the backdrop effect
            int result = DwmApi.DwmSetWindowAttribute(
                windowHandle,
                DwmApi.DWMWA_SYSTEMBACKDROP_TYPE,
                ref backdropType,
                sizeof(int));

            bool success = result == 0;
            if (success)
            {
                _logger?.LogInformation("{EffectName} effect applied successfully (dark mode: {IsDarkMode})",
                    effectName, isDarkMode);
            }
            else
            {
                _logger?.LogWarning("{EffectName} effect failed with HRESULT: 0x{Result:X8}",
                    effectName, result);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply {EffectName} effect", effectName);
            return false;
        }
    }

    /// <summary>
    /// Checks if the current Windows version supports backdrop effects.
    /// </summary>
    private bool CheckBackdropSupport()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var version = Environment.OSVersion;

            // Windows 11 = NT 10.0 with build >= 22000
            bool isWindows11 = version.Version.Major >= 10 && version.Version.Build >= Windows11MinBuild;

            if (isWindows11)
            {
                _logger?.LogInformation("Windows 11 detected (Build {Build}), backdrop effects supported",
                    version.Version.Build);
            }
            else
            {
                _logger?.LogDebug("Windows 10 or earlier detected, backdrop effects not supported");
            }

            return isWindows11;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to detect Windows version");
            return false;
        }
    }
}
