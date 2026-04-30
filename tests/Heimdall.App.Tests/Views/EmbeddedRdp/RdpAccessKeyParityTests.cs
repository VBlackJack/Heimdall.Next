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

namespace Heimdall.App.Tests.Views.EmbeddedRdp;

public sealed class RdpAccessKeyParityTests
{
    [Theory]
    [InlineData("en", "BtnDisconnectSession", "_D")]
    [InlineData("en", "BtnCancelConnect", "_C")]
    [InlineData("en", "BtnCancelReconnect", "_C")]
    [InlineData("en", "BtnReconnectSession", "_R")]
    [InlineData("en", "BtnCloseOverlay", "_C")]
    [InlineData("fr", "BtnDisconnectSession", "_D")]
    [InlineData("fr", "BtnCancelConnect", "_A")]
    [InlineData("fr", "BtnCancelReconnect", "_A")]
    [InlineData("fr", "BtnReconnectSession", "_R")]
    [InlineData("fr", "BtnCloseOverlay", "_F")]
    public async Task ToolbarTextButtonAccessKeys_ArePresentInBothLocales(
        string locale,
        string key,
        string expectedPrefix)
    {
        var localizer = await CreateLocalizerAsync(locale);
        var value = localizer[key];

        Assert.Contains("_", value, StringComparison.Ordinal);
        Assert.StartsWith(expectedPrefix, value, StringComparison.Ordinal);
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }
}
