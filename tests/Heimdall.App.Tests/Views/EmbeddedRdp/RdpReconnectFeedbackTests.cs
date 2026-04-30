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

using System.Globalization;
using System.IO;
using Heimdall.App.Views;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

public sealed class RdpReconnectFeedbackTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(10, 20, 10)]
    [InlineData(20, 20, 20)]
    [InlineData(25, 20, 20)]
    [InlineData(-1, 20, 0)]
    [InlineData(5, 0, 0)]
    public void ResolveReconnectProgressValue_ClampsAttemptToValidRange(
        int currentAttempt,
        int maxAttempts,
        int expected)
    {
        Assert.Equal(expected, EmbeddedRdpView.ResolveReconnectProgressValue(currentAttempt, maxAttempts));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task RdpReconnectElapsedFormat_PreservesElapsedPlaceholder(string locale)
    {
        var localizer = await CreateLocalizerAsync(locale);
        var template = localizer["RdpReconnectElapsedFormat"];

        Assert.Equal(1, CountOccurrences(template, "{0}"));

        var formatted = string.Format(CultureInfo.InvariantCulture, template, 12);
        Assert.Contains("12", formatted);
        Assert.False(string.IsNullOrWhiteSpace(formatted));
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
