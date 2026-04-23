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
using Heimdall.App.Services;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class ToolContextProviderTests
{
    [Fact]
    public async Task OnLocaleChanged_OnUiThread_RunsInline()
    {
        var localizer = await CreateLocalizerAsync("en");
        var dispatcher = new FakeUiDispatcher(checkAccess: true);
        using var provider = new ToolContextProvider(localizer, dispatcher);
        var originalLabel = provider.ContextLabel;

        await localizer.SwitchLocaleAsync("fr");

        Assert.Equal(0, dispatcher.InvokeAsyncCalls);
        Assert.NotEqual(originalLabel, provider.ContextLabel);
    }

    [Fact]
    public async Task OnLocaleChanged_OffUiThread_PostsToDispatcher()
    {
        var localizer = await CreateLocalizerAsync("en");
        var dispatcher = new FakeUiDispatcher(checkAccess: false);
        using var provider = new ToolContextProvider(localizer, dispatcher);
        var originalLabel = provider.ContextLabel;

        await localizer.SwitchLocaleAsync("fr");

        Assert.Equal(1, dispatcher.InvokeAsyncCalls);
        Assert.NotEqual(originalLabel, provider.ContextLabel);
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }
}
