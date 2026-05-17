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

using Heimdall.App.Services;
using Heimdall.Core.SessionHealth;

namespace Heimdall.App.Tests;

/// <summary>
/// Unit tests for <see cref="HealthReasonLocalizer"/>. The helper is pure
/// (no IO, no UI), so the LocalizationManager is passed as <c>null</c> in
/// every case — the implementation falls back to returning the i18n key
/// when the manager is unavailable, which makes the expected strings stable
/// across runs without needing a real locale bundle.
/// </summary>
public class HealthReasonLocalizerTests
{
    [Theory]
    [InlineData(HealthStatus.Up, "HealthStatusUp")]
    [InlineData(HealthStatus.Down, "HealthStatusDown")]
    [InlineData(HealthStatus.Probing, "HealthStatusProbing")]
    [InlineData(HealthStatus.Unknown, "HealthStatusUnknown")]
    public void StatusKey_MapsEnumToI18nKey(HealthStatus status, string expectedKey)
    {
        Assert.Equal(expectedKey, HealthReasonLocalizer.StatusKey(status));
    }

    [Theory]
    [InlineData("timeout", "HealthReasonTimeout")]
    [InlineData("refused", "HealthReasonRefused")]
    [InlineData("unreachable", "HealthReasonUnreachable")]
    [InlineData("dns", "HealthReasonDns")]
    [InlineData("behind-gateway", "HealthReasonBehindGateway")]
    [InlineData("no-port", "HealthReasonNoPort")]
    [InlineData("no-host", "HealthReasonNoHost")]
    public void ReasonKey_MapsKnownTagsToI18nKey(string tag, string expectedKey)
    {
        Assert.Equal(expectedKey, HealthReasonLocalizer.ReasonKey(tag));
    }

    [Theory]
    [InlineData("AddressNotAvailable")]
    [InlineData("anything-new")]
    [InlineData("")]
    public void ReasonKey_ReturnsNull_ForUnknownOrEmptyTag(string tag)
    {
        Assert.Null(HealthReasonLocalizer.ReasonKey(tag));
    }

    [Fact]
    public void FormatTooltip_UpWithLatency_RendersStatusLatencyAndTime()
    {
        var state = new HealthState(HealthStatus.Up, new DateTime(2026, 5, 17, 14, 32, 55, DateTimeKind.Utc), 42, null);

        var tooltip = HealthReasonLocalizer.FormatTooltip(state, localizer: null);

        Assert.Contains("HealthStatusUp (42 ms)", tooltip);
        Assert.Contains(" · ", tooltip);
    }

    [Fact]
    public void FormatTooltip_DownWithReason_RendersReasonSegment()
    {
        var state = new HealthState(HealthStatus.Down, DateTime.UtcNow, null, "timeout");

        var tooltip = HealthReasonLocalizer.FormatTooltip(state, localizer: null);

        Assert.Contains("HealthStatusDown", tooltip);
        Assert.Contains("HealthReasonTimeout", tooltip);
    }

    [Fact]
    public void FormatTooltip_UnknownReason_WrapsRawTagInParentheses()
    {
        var state = new HealthState(HealthStatus.Down, DateTime.UtcNow, null, "MysterySocketCode");

        var tooltip = HealthReasonLocalizer.FormatTooltip(state, localizer: null);

        Assert.Contains("(MysterySocketCode)", tooltip);
    }

    [Fact]
    public void FormatTooltip_InitialState_UsesNeverInsteadOfTimestamp()
    {
        var tooltip = HealthReasonLocalizer.FormatTooltip(HealthState.Initial, localizer: null);

        Assert.Contains("HealthLastCheckedNever", tooltip);
    }

    [Fact]
    public void FormatTooltip_NullReason_OmitsReasonSegment()
    {
        var state = new HealthState(HealthStatus.Up, DateTime.UtcNow, 5, null);

        var tooltip = HealthReasonLocalizer.FormatTooltip(state, localizer: null);

        // Should have exactly two ' · ' separators: status, then time.
        Assert.Equal(1, CountOccurrences(tooltip, " · "));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
