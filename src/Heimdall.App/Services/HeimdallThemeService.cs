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
using Heimdall.Core.Logging;
using ThemeForgeChangedEventArgs = ThemeForge.Theme.ThemeChangedEventArgs;
using ThemeForgeIThemeService = ThemeForge.Theme.IThemeService;
using ThemeForgeNames = ThemeForge.Theme.ThemeNames;
using ThemeForgeThemeService = ThemeForge.Theme.ThemeService;

namespace Heimdall.App.Services;

internal readonly record struct ThemeResolution(string ThemeId, bool ShouldPersist);

/// <summary>
/// Heimdall compatibility wrapper around the ThemeForge theme engine.
/// </summary>
public sealed class HeimdallThemeService
{
    private const string DefaultTheme = ThemeForgeNames.Drakul;

    private static readonly Dictionary<string, string> ThemeForgeIds =
        ThemeForgeNames.All.ToDictionary(
            themeName => themeName,
            themeName => themeName,
            StringComparer.OrdinalIgnoreCase);

    private readonly IConfigManager _configManager;
    private readonly object _themeServiceLock = new();
    private ThemeForgeIThemeService? _themeService;
    private string _currentTheme = DefaultTheme;

    public HeimdallThemeService(IConfigManager configManager)
    {
        _configManager = configManager;
    }

    /// <summary>Canonical ThemeForge id currently applied to the application.</summary>
    public string CurrentTheme
    {
        get
        {
            string current = _themeService?.CurrentTheme ?? string.Empty;
            return string.IsNullOrWhiteSpace(current) ? _currentTheme : current;
        }
    }

    /// <summary>
    /// Monotonically increasing ThemeForge revision counter after successful swaps.
    /// </summary>
    public int ThemeRevision => _themeService?.ThemeRevision ?? 0;

    /// <summary>
    /// Raised after ThemeForge applies a theme, translated to the legacy event shape.
    /// </summary>
    public event Action<string>? ThemeChanged;

    /// <summary>
    /// Enumerates the ThemeForge theme ids recognized by Heimdall.
    /// </summary>
    public static IReadOnlyCollection<string> AvailableThemes => ThemeForgeNames.All;

    /// <summary>
    /// Resolves a persisted setting to a ThemeForge id and delegates the swap.
    /// </summary>
    public void ApplyTheme(string? themeName)
    {
        ThemeForgeIThemeService? themeService = GetThemeService();
        if (themeService is null)
        {
            return;
        }

        ThemeResolution resolution = ResolveThemeId(themeName);
        if (resolution.ShouldPersist)
        {
            PersistTheme(resolution.ThemeId);
        }

        themeService.ApplyTheme(resolution.ThemeId);

        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        foreach (Window window in app.Windows)
        {
            WindowThemeHelper.ApplyCurrentTheme(window);
        }
    }

    internal static ThemeResolution ResolveThemeId(string? persisted)
    {
        if (string.IsNullOrWhiteSpace(persisted))
        {
            return new ThemeResolution(DefaultTheme, ShouldPersist: true);
        }

        string trimmed = persisted.Trim();
        if (!ThemeForgeIds.TryGetValue(trimmed, out string? canonical))
        {
            return new ThemeResolution(DefaultTheme, ShouldPersist: true);
        }

        bool shouldPersist = !string.Equals(persisted, canonical, StringComparison.Ordinal);
        return new ThemeResolution(canonical, shouldPersist);
    }

    private ThemeForgeIThemeService? GetThemeService()
    {
        if (_themeService is not null)
        {
            return _themeService;
        }

        Application? app = Application.Current;
        if (app is null)
        {
            return null;
        }

        lock (_themeServiceLock)
        {
            if (_themeService is not null)
            {
                return _themeService;
            }

            ThemeForgeIThemeService service = new ThemeForgeThemeService(app, ThemeForgeNames.All);
            service.ThemeChanged += OnThemeForgeThemeChanged;
            _themeService = service;
            return _themeService;
        }
    }

    private void PersistTheme(string themeId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _configManager.MergeSettingAsync(settings => settings.DefaultTheme = themeId);
                FileLogger.Info(
                    $"[HeimdallThemeService] Persisted ThemeForge theme '{themeId}'");
            }
            catch (Exception ex)
            {
                FileLogger.Warn(
                    $"[HeimdallThemeService] Failed to persist theme: {ex.Message}");
            }
        });
    }

    private void OnThemeForgeThemeChanged(object? sender, ThemeForgeChangedEventArgs args)
    {
        _currentTheme = args.CurrentTheme;
        ThemeChanged?.Invoke(args.CurrentTheme);
    }
}
