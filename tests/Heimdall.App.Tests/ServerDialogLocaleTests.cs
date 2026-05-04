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

using System.IO;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class ServerDialogLocaleTests
{
    [Theory]
    [InlineData("en", "Choose the protocol", "host", "port")]
    [InlineData("fr", "Choisissez le protocole", "h\u00f4te", "port")]
    public async Task ServerDialogConnectionBasicsDesc_DoesNotMentionChooseProtocol(
        string locale,
        string obsoletePhrase,
        string hostTerm,
        string portTerm)
    {
        var localizer = await CreateLocalizerAsync(locale);

        var description = localizer["ServerDialogConnectionBasicsDesc"];

        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.DoesNotContain(obsoletePhrase, description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(hostTerm, description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(portTerm, description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task ServerDialogResolutionModeAutoTitle_ExistsInBothLocales(string locale)
    {
        var localizer = await CreateLocalizerAsync(locale);

        var text = localizer["ServerDialogResolutionModeAutoTitle"];

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Theory]
    [InlineData("en", "host", "fullscreen", "windowed")]
    [InlineData("fr", "h\u00f4te", "plein \u00e9cran", "fen\u00eatr\u00e9")]
    public async Task ServerDialogResolutionModeAutoDescription_MentionsHostAndFullscreen(
        string locale,
        string hostTerm,
        string fullscreenTerm,
        string windowedTerm)
    {
        var localizer = await CreateLocalizerAsync(locale);

        var text = localizer["ServerDialogResolutionModeAutoDescription"];

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains(hostTerm, text, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            text.Contains(fullscreenTerm, StringComparison.OrdinalIgnoreCase)
            || text.Contains(windowedTerm, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task ServerDialogResolutionMoreOptions_ExistsInBothLocales(string locale)
    {
        var localizer = await CreateLocalizerAsync(locale);

        var text = localizer["ServerDialogResolutionMoreOptions"];

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task ServerDialogResolutionModeSwitchToAuto_ExistsInBothLocales(string locale)
    {
        var localizer = await CreateLocalizerAsync(locale);

        var text = localizer["ServerDialogResolutionModeSwitchToAuto"];

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Theory]
    [InlineData("en", "monitor")]
    [InlineData("fr", "moniteur")]
    public async Task ServerDialogResolutionModeMultimonUnavailableTooltip_ExistsInBothLocales(
        string locale,
        string expectedTerm)
    {
        var localizer = await CreateLocalizerAsync(locale);

        var text = localizer["ServerDialogResolutionModeMultimonUnavailableTooltip"];

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains(expectedTerm, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "RdpAutofillRetryAction")]
    [InlineData("en", "RdpAutofillRetryTooltip")]
    [InlineData("en", "RdpAutofillDismissAction")]
    [InlineData("en", "RdpAutofillDismissTooltip")]
    [InlineData("fr", "RdpAutofillRetryAction")]
    [InlineData("fr", "RdpAutofillRetryTooltip")]
    [InlineData("fr", "RdpAutofillDismissAction")]
    [InlineData("fr", "RdpAutofillDismissTooltip")]
    public async Task RdpAutofillActionKeys_ExistInBothLocales(string locale, string key)
    {
        var localizer = await CreateLocalizerAsync(locale);

        var text = localizer[key];

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task RdpSplitDisplayResizeWarning_ExistsInBothLocales(string locale)
    {
        var localizer = await CreateLocalizerAsync(locale);

        var text = localizer["RdpSplitDisplayResizeWarning"];

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }
}
