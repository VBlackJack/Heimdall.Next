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
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;

namespace Heimdall.App.Services;

/// <summary>
/// Computes transient Settings UI state related to the command library Git sync section.
/// </summary>
public sealed class CommandLibrarySettingsService
{
    private readonly ConfigManager _configManager;
    private readonly LocalizationManager _localizer;

    public CommandLibrarySettingsService(
        ConfigManager configManager,
        LocalizationManager localizer)
    {
        _configManager = configManager;
        _localizer = localizer;
    }

    /// <summary>
    /// Checks whether a Git sync token is currently stored in settings.
    /// </summary>
    public async Task<bool> HasSavedTokenAsync()
    {
        var settings = await _configManager.LoadSettingsAsync().ConfigureAwait(false);
        return !string.IsNullOrEmpty(settings.CmdLibGitSyncToken);
    }

    /// <summary>
    /// Builds the transient token status displayed in the Settings UI.
    /// </summary>
    public CommandLibraryTokenStatus GetTokenStatus(bool hasToken)
    {
        return new CommandLibraryTokenStatus(
            hasToken ? _localizer["SettingsCmdLibSyncTokenSaved"] : string.Empty,
            hasToken ? Visibility.Visible : Visibility.Collapsed);
    }
}

/// <summary>
/// Represents the transient display state of the command library token controls.
/// </summary>
public readonly record struct CommandLibraryTokenStatus(
    string StatusText,
    Visibility ClearButtonVisibility);
