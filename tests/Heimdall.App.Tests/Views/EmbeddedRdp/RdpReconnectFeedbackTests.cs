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
using Heimdall.App.Views.EmbeddedRdp;
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

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task RdpReconnectNextRetryFormat_PreservesEtaPlaceholder(string locale)
    {
        var localizer = await CreateLocalizerAsync(locale);
        var template = localizer["RdpReconnectNextRetryFormat"];

        Assert.Equal(1, CountOccurrences(template, "{0}"));

        var formatted = string.Format(CultureInfo.InvariantCulture, template, 8);
        Assert.Contains("~8s", formatted);
        Assert.False(string.IsNullOrWhiteSpace(formatted));
    }

    [Fact]
    public void EstimateSeconds_ReturnsNull_WhenNoAttemptsObserved()
    {
        var now = CreateUtc(2026, 5, 4, 8, 0, 0);

        var etaSeconds = ReconnectEtaCalculator.EstimateSeconds(Array.Empty<DateTime>(), now);

        Assert.Null(etaSeconds);
    }

    [Fact]
    public void EstimateSeconds_ReturnsNull_WhenOnlyOneAttemptObserved()
    {
        var now = CreateUtc(2026, 5, 4, 8, 0, 0);
        var attempts = new[] { now.AddSeconds(-10) };

        var etaSeconds = ReconnectEtaCalculator.EstimateSeconds(attempts, now);

        Assert.Null(etaSeconds);
    }

    [Fact]
    public void EstimateSeconds_ReturnsRemainingSeconds_WhenNextAttemptIsInFuture()
    {
        var now = CreateUtc(2026, 5, 4, 8, 0, 0);
        var attempts = new[]
        {
            now.AddSeconds(-12),
            now.AddSeconds(-4),
        };

        var etaSeconds = ReconnectEtaCalculator.EstimateSeconds(attempts, now);

        Assert.Equal(4, etaSeconds);
    }

    [Fact]
    public void EstimateSeconds_ReturnsZero_WhenExpectedAttemptTimeHasPassed()
    {
        var now = CreateUtc(2026, 5, 4, 8, 0, 0);
        var attempts = new[]
        {
            now.AddSeconds(-20),
            now.AddSeconds(-10),
        };

        var etaSeconds = ReconnectEtaCalculator.EstimateSeconds(attempts, now);

        Assert.Equal(0, etaSeconds);
    }

    [Fact]
    public void EstimateSeconds_UsesLatestAttemptPair_WhenThreeAttemptsObserved()
    {
        var now = CreateUtc(2026, 5, 4, 8, 0, 0);
        var attempts = new[]
        {
            now.AddSeconds(-30),
            now.AddSeconds(-11),
            now.AddSeconds(-5),
        };

        var etaSeconds = ReconnectEtaCalculator.EstimateSeconds(attempts, now);

        Assert.Equal(1, etaSeconds);
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

    private static DateTime CreateUtc(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second)
        => new(year, month, day, hour, minute, second, DateTimeKind.Utc);
}
