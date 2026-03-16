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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels;

/// <summary>
/// ViewModel for the application settings tab.
/// Tracks dirty state and delegates persistence to <see cref="ConfigManager"/>.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;
    private readonly LocalizationManager _localizer;

    [ObservableProperty]
    private string _plinkPath = "";

    [ObservableProperty]
    private string _defaultLocale = "en";

    [ObservableProperty]
    private string _defaultTheme = "Dark";

    [ObservableProperty]
    private bool _enableLogging = true;

    [ObservableProperty]
    private int _antiIdleInterval = 60;

    [ObservableProperty]
    private int _maxEmbeddedSessions = 10;

    [ObservableProperty]
    private string _externalEditorPath = "";

    [ObservableProperty]
    private bool _sftpAutoOpenOnSsh = true;

    [ObservableProperty]
    private bool _isDirty;

    public SettingsViewModel(
        ConfigManager configManager,
        LocalizationManager localizer)
    {
        _configManager = configManager;
        _localizer = localizer;
    }

    /// <summary>
    /// Populates ViewModel properties from the loaded <see cref="AppSettings"/>.
    /// Does not mark the ViewModel as dirty.
    /// </summary>
    public void LoadFromSettings(AppSettings settings)
    {
        PlinkPath = settings.PlinkPath;
        DefaultLocale = settings.DefaultLocale;
        DefaultTheme = settings.DefaultTheme;
        EnableLogging = settings.EnableLogging;
        AntiIdleInterval = settings.AntiIdleIntervalSeconds;
        MaxEmbeddedSessions = settings.MaxEmbeddedSessions;
        ExternalEditorPath = settings.ExternalEditorPath;
        SftpAutoOpenOnSsh = settings.SftpAutoOpenOnSsh;

        IsDirty = false;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var settings = await _configManager.LoadSettingsAsync();

        settings.PlinkPath = PlinkPath;
        settings.DefaultLocale = DefaultLocale;
        settings.DefaultTheme = DefaultTheme;
        settings.EnableLogging = EnableLogging;
        settings.AntiIdleIntervalSeconds = AntiIdleInterval;
        settings.MaxEmbeddedSessions = MaxEmbeddedSessions;
        settings.ExternalEditorPath = ExternalEditorPath;
        settings.SftpAutoOpenOnSsh = SftpAutoOpenOnSsh;

        await _configManager.SaveSettingsAsync(settings);

        if (!string.Equals(_localizer.CurrentLocale, DefaultLocale, StringComparison.OrdinalIgnoreCase))
        {
            await _localizer.SwitchLocaleAsync(DefaultLocale);
        }

        IsDirty = false;
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync(CancellationToken cancellationToken)
    {
        var defaults = new AppSettings();
        LoadFromSettings(defaults);
        IsDirty = true;
    }

    [RelayCommand]
    private async Task ExportConfigAsync(CancellationToken cancellationToken)
    {
        // Export dialog will be implemented in Phase 4B
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ImportConfigAsync(CancellationToken cancellationToken)
    {
        // Import dialog will be implemented in Phase 4B
        await Task.CompletedTask;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Mark dirty when any settings property changes (except IsDirty itself)
        if (e.PropertyName is not (nameof(IsDirty)))
        {
            IsDirty = true;
        }
    }
}
