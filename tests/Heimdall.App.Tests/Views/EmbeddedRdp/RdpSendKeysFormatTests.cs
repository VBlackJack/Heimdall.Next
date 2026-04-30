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

public sealed class RdpSendKeysFormatTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task RdpSendKeysLabels_AreLocalizedAndNonEmpty(string locale)
    {
        var localizer = await CreateLocalizerAsync(locale);

        foreach (var key in SendKeysKeys)
        {
            var value = localizer[key];

            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.NotEqual(key, value);
        }
    }

    private static readonly string[] SendKeysKeys =
    [
        "RdpSendKeysCtrlAltDel",
        "RdpSendKeysWindows",
        "RdpSendKeysAltTab",
        "RdpSendKeysCtrlEsc",
        "RdpSendKeysPrintScreen",
        "RdpSendKeysEscape"
    ];

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }
}
