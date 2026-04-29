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

public sealed class RdpConnectionStateStatusPolicyTests
{
    [Fact]
    public async Task RdpStabilizingStatus_PreservesCountdownPlaceholder()
    {
        var localizer = await CreateLocalizerAsync("en");
        var template = localizer["RdpStabilizingStatus"];

        Assert.Equal(1, CountOccurrences(template, "{0}"));

        var formatted = string.Format(CultureInfo.InvariantCulture, template, 7);
        Assert.Contains("7", formatted);
        Assert.True(
            formatted.Contains("session", StringComparison.OrdinalIgnoreCase),
            $"Expected formatted stabilizing status to mention the session, but got: {formatted}");
    }

    [Theory]
    [InlineData("server-1", "server-1", false, false, true)]
    [InlineData("server-1", "server-2", false, false, false)]
    [InlineData("server-1", "", false, false, false)]
    [InlineData("server-1", null, false, false, false)]
    [InlineData("server-1", "server-1", true, false, false)]
    [InlineData("server-1", "server-1", false, true, false)]
    [InlineData("server-1", "SERVER-1", false, false, false)]
    [InlineData("server-1", "server-1 ", false, false, false)]
    public void ShouldHandleStateChange_ReturnsExpected(
        string serverId,
        string? targetServerId,
        bool comDrivenStatusActive,
        bool disposed,
        bool expected)
    {
        var actual = EmbeddedRdpView.ShouldHandleStateChange(
            serverId,
            targetServerId,
            comDrivenStatusActive,
            disposed);

        Assert.Equal(expected, actual);
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
