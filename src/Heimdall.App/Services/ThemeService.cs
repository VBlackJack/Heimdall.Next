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

using System.Windows;
using Heimdall.App.Theming;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Services;

/// <summary>
/// Centralized theme swap service. Owns the single code path that replaces
/// the active theme <see cref="ResourceDictionary"/> on the application, and
/// raises <see cref="ThemeChanged"/> after a successful swap so downstream
/// consumers (views that cache brushes, DWM title bar, etc.) can refresh.
/// </summary>
public sealed class ThemeService
{
    private const string DefaultTheme = "DraculaPro";
    private const string ThemePathMarker = "Theme.xaml";

    private static readonly Dictionary<string, Uri> ThemeUris = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DraculaPro"] = new Uri("Themes/DraculaProTheme.xaml", UriKind.Relative),
        ["Alucard"] = new Uri("Themes/AlucardTheme.xaml", UriKind.Relative),
        ["Blade"] = new Uri("Themes/BladeTheme.xaml", UriKind.Relative),
        ["Buffy"] = new Uri("Themes/BuffyTheme.xaml", UriKind.Relative),
        ["Lincoln"] = new Uri("Themes/LincolnTheme.xaml", UriKind.Relative),
        ["Morbius"] = new Uri("Themes/MorbiusTheme.xaml", UriKind.Relative),
        ["VanHelsing"] = new Uri("Themes/VanHelsingTheme.xaml", UriKind.Relative),
        ["Nosferatu"] = new Uri("Themes/NosferatuTheme.xaml", UriKind.Relative),
        ["Renfield"] = new Uri("Themes/RenfieldTheme.xaml", UriKind.Relative),
        ["Carmilla"] = new Uri("Themes/CarmillaTheme.xaml", UriKind.Relative),
        ["Drakula"] = new Uri("Themes/DrakulaTheme.xaml", UriKind.Relative),
        ["Bathory"] = new Uri("Themes/BathoryTheme.xaml", UriKind.Relative),
        ["Striga"] = new Uri("Themes/StrigaTheme.xaml", UriKind.Relative),
        ["Akasha"] = new Uri("Themes/AkashaTheme.xaml", UriKind.Relative),
        ["Helsing"] = new Uri("Themes/HelsingTheme.xaml", UriKind.Relative),
    };

    private readonly IConfigManager _configManager;
    private int _themeRevision;

    public ThemeService(IConfigManager configManager)
    {
        _configManager = configManager;
        CurrentTheme = DefaultTheme;
    }

    /// <summary>Canonical name of the theme currently applied to the application.</summary>
    public string CurrentTheme { get; private set; }

    /// <summary>
    /// Monotonically increasing counter bumped on every successful theme swap.
    /// Bindings that need to re-evaluate on theme change (e.g. <c>MultiBinding</c>s
    /// over brush-resolving converters) should observe this as a trigger value.
    /// </summary>
    public int ThemeRevision => _themeRevision;

    /// <summary>
    /// Raised on the UI thread after the active theme dictionary has been
    /// replaced. Consumers that snapshot brushes (converters, code-built views)
    /// should rebuild their visual state when this fires.
    /// </summary>
    public event Action<string>? ThemeChanged;

    /// <summary>
    /// Enumerates the canonical names of all themes recognized by the service.
    /// </summary>
    public static IReadOnlyCollection<string> AvailableThemes => ThemeUris.Keys;

    /// <summary>
    /// Swaps the active theme dictionary to the theme identified by
    /// <paramref name="themeName"/>. Legacy names "Dark" and "Light" are
    /// silently migrated to <see cref="DefaultTheme"/> and persisted back
    /// to settings. Unknown names fall back to <see cref="DefaultTheme"/>.
    /// Idempotent: calls for the already-active theme are a no-op.
    /// </summary>
    public void ApplyTheme(string? themeName)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var canonical = Resolve(themeName, out var migrated);

        if (migrated)
        {
            PersistMigratedTheme(canonical);
        }

        if (string.Equals(CurrentTheme, canonical, StringComparison.OrdinalIgnoreCase)
            && HasThemeDictionary(app))
        {
            return;
        }

        var uri = ThemeUris[canonical];
        var newTheme = new ResourceDictionary { Source = uri };

        var existing = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains(
                ThemePathMarker, StringComparison.OrdinalIgnoreCase) == true);

        if (existing is not null)
        {
            app.Resources.MergedDictionaries.Remove(existing);
        }

        app.Resources.MergedDictionaries.Add(newTheme);
        CurrentTheme = canonical;
        _themeRevision++;

        foreach (Window window in app.Windows)
        {
            WindowThemeHelper.ApplyCurrentTheme(window);
        }

        ThemeChanged?.Invoke(canonical);
    }

    private static string Resolve(string? themeName, out bool migrated)
    {
        migrated = false;

        if (string.IsNullOrWhiteSpace(themeName))
        {
            return DefaultTheme;
        }

        // Legacy settings written by pre-Dracula builds still contain
        // "Dark" / "Light" — migrate silently to the new default.
        if (themeName.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            || themeName.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            migrated = true;
            return DefaultTheme;
        }

        if (ThemeUris.ContainsKey(themeName))
        {
            // Normalize to the canonical casing of the dictionary key.
            return ThemeUris.Keys.First(k =>
                k.Equals(themeName, StringComparison.OrdinalIgnoreCase));
        }

        return DefaultTheme;
    }

    private static bool HasThemeDictionary(Application app)
    {
        return app.Resources.MergedDictionaries.Any(d =>
            d.Source?.OriginalString.Contains(
                ThemePathMarker, StringComparison.OrdinalIgnoreCase) == true);
    }

    private void PersistMigratedTheme(string canonical)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _configManager.MergeSettingAsync(s => s.DefaultTheme = canonical);
                Core.Logging.FileLogger.Info(
                    $"[ThemeService] Migrated legacy theme to '{canonical}'");
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn(
                    $"[ThemeService] Failed to persist migrated theme: {ex.Message}");
            }
        });
    }
}
