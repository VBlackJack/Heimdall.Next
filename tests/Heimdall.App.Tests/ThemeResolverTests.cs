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
using ThemeForge.Theme;

namespace Heimdall.App.Tests;

public sealed class ThemeResolverTests
{
    public static IEnumerable<object[]> ThemeForgeIds()
    {
        return ThemeNames.All.Select(themeName => new object[] { themeName });
    }

    [Theory]
    [MemberData(nameof(ThemeForgeIds))]
    public void ResolveThemeId_WithCanonicalThemeForgeId_ReturnsSameId(string themeName)
    {
        ThemeResolution result = HeimdallThemeService.ResolveThemeId(themeName);

        Assert.Equal(themeName, result.ThemeId);
        Assert.False(result.ShouldPersist);
    }

    [Theory]
    [InlineData("drakul", ThemeNames.Drakul)]
    [InlineData("PARCHMENT", ThemeNames.Parchment)]
    [InlineData("wHiTbY", ThemeNames.Whitby)]
    [InlineData(" Drakul ", ThemeNames.Drakul)]
    public void ResolveThemeId_WithNonCanonicalThemeForgeId_ReturnsCanonicalIdAndPersists(
        string persisted,
        string expectedTheme)
    {
        ThemeResolution result = HeimdallThemeService.ResolveThemeId(persisted);

        Assert.Equal(expectedTheme, result.ThemeId);
        Assert.True(result.ShouldPersist);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("DraculaPro")]
    [InlineData("Blade")]
    [InlineData("Buffy")]
    [InlineData("NotATheme")]
    public void ResolveThemeId_WithInvalidOrLegacyName_DefaultsToDrakulAndPersists(
        string? persisted)
    {
        ThemeResolution result = HeimdallThemeService.ResolveThemeId(persisted);

        Assert.Equal(ThemeNames.Drakul, result.ThemeId);
        Assert.True(result.ShouldPersist);
    }

    [Theory]
    [InlineData(ThemeNames.Striga)]
    [InlineData(ThemeNames.Carmilla)]
    public void ResolveThemeId_WithCollidingName_TreatsItAsThemeForgeId(string themeName)
    {
        ThemeResolution result = HeimdallThemeService.ResolveThemeId(themeName);

        Assert.Equal(themeName, result.ThemeId);
        Assert.False(result.ShouldPersist);
    }
}
