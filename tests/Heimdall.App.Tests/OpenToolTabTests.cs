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
using Heimdall.App.ViewModels;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

public sealed partial class SessionCoordinatorPreMountTests
{
    [Fact]
    public async Task OpenToolTabAsync_NonNetworkToolWithoutContext_ReusesExistingTab()
    {
        using TestHarness harness = TestHarness.Create();

        await harness.Main.OpenToolTabAsync("HASH", "Hash", null);
        SessionTabViewModel firstTab = Assert.Single(harness.Main.Connection.ActiveSessions);

        await harness.Main.OpenToolTabAsync("HASH", "Hash", null);

        Assert.Single(harness.Main.Connection.ActiveSessions);
        Assert.Same(firstTab, harness.Main.Connection.ActiveSession);
        Assert.Equal(1, harness.EmbeddedSessionManager.CreateToolControlCalls);
    }

    [Fact]
    public async Task OpenToolTabAsync_NetworkTool_CreatesSeparateTabs()
    {
        using TestHarness harness = TestHarness.Create();

        await harness.Main.OpenToolTabAsync("PING", "Ping", null);
        await harness.Main.OpenToolTabAsync("PING", "Ping", null);

        Assert.Equal(2, harness.Main.Connection.ActiveSessions.Count);
        Assert.Equal(2, harness.EmbeddedSessionManager.CreateToolControlCalls);
    }

    [Fact]
    public async Task OpenToolTabAsync_NonNetworkToolWithContext_CreatesSeparateTabs()
    {
        using TestHarness harness = TestHarness.Create();
        ToolContext context = new ToolContext(TargetHost: "demo.example.com");

        await harness.Main.OpenToolTabAsync("HASH", "Hash", context);
        await harness.Main.OpenToolTabAsync("HASH", "Hash", context);

        Assert.Equal(2, harness.Main.Connection.ActiveSessions.Count);
    }

    [Fact]
    public async Task OpenToolTabAsync_Success_SetsHostControlAndReadyStatus()
    {
        using TestHarness harness = TestHarness.Create();
        object sentinel = new object();
        harness.EmbeddedSessionManager.CreateToolControlBehavior = (
            SessionTabViewModel sessionTab,
            string toolId,
            ToolContext? context,
            AppSettings? settings) => sentinel;
        LocalizationManager localizer = new LocalizationManager();
        await localizer.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en");

        await harness.Main.OpenToolTabAsync("HASH", "Hash", null);

        SessionTabViewModel tab = Assert.Single(harness.Main.Connection.ActiveSessions);
        Assert.Same(sentinel, tab.HostControl);
        Assert.Equal(localizer["StatusReady"], tab.Status);
        Assert.True(harness.Main.Connection.HasActiveSessions);
    }

    [Fact]
    public async Task OpenToolTabAsync_FactoryThrows_NoPriorTabs_RemovesOrphanAndRethrows()
    {
        using TestHarness harness = TestHarness.Create();
        harness.EmbeddedSessionManager.CreateToolControlBehavior = (
            SessionTabViewModel sessionTab,
            string toolId,
            ToolContext? context,
            AppSettings? settings) => throw new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Main.OpenToolTabAsync("HASH", "Hash", null));

        Assert.Empty(harness.Main.Connection.ActiveSessions);
        Assert.False(harness.Main.Connection.HasActiveSessions);
    }

    [Fact]
    public async Task OpenToolTabAsync_FactoryThrows_PreservesExistingSessions()
    {
        using TestHarness harness = TestHarness.Create();

        await harness.Main.OpenToolTabAsync("HASH", "Hash", null);
        SessionTabViewModel healthyTab = Assert.Single(harness.Main.Connection.ActiveSessions);
        harness.EmbeddedSessionManager.CreateToolControlBehavior = (
            SessionTabViewModel sessionTab,
            string toolId,
            ToolContext? context,
            AppSettings? settings) => throw new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Main.OpenToolTabAsync("JWT", "Jwt", null));

        SessionTabViewModel remainingTab = Assert.Single(harness.Main.Connection.ActiveSessions);
        Assert.Same(healthyTab, remainingTab);
        Assert.True(harness.Main.Connection.HasActiveSessions);
    }

    [Fact]
    public async Task OpenToolTabAsync_FactoryThrows_PropagatesOriginalExceptionUnwrapped()
    {
        using TestHarness harness = TestHarness.Create();
        InvalidOperationException expected = new InvalidOperationException("specific-boom");
        harness.EmbeddedSessionManager.CreateToolControlBehavior = (
            SessionTabViewModel sessionTab,
            string toolId,
            ToolContext? context,
            AppSettings? settings) => throw expected;

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Main.OpenToolTabAsync("HASH", "Hash", null));

        Assert.Same(expected, actual);
    }
}
