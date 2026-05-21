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

public sealed class AccentTintResolverTests
{
    public static IEnumerable<object[]> AccentTintIds()
    {
        return AccentTints.All.Select(accentTint => new object[] { accentTint.ToString() });
    }

    [Theory]
    [MemberData(nameof(AccentTintIds))]
    public void ResolveAccentTint_WithCanonicalAccentTint_ReturnsSameId(string accentTintId)
    {
        AccentTintResolution result = HeimdallThemeService.ResolveAccentTint(accentTintId);

        Assert.Equal(accentTintId, result.AccentTintId);
        Assert.False(result.ShouldPersist);
    }

    [Theory]
    [InlineData("blue", nameof(AccentTint.Blue))]
    [InlineData("CYAN", nameof(AccentTint.Cyan))]
    [InlineData("gReEn", nameof(AccentTint.Green))]
    [InlineData(" Orange ", nameof(AccentTint.Orange))]
    public void ResolveAccentTint_WithNonCanonicalAccentTint_ReturnsCanonicalIdAndPersists(
        string persisted,
        string expectedAccentTint)
    {
        AccentTintResolution result = HeimdallThemeService.ResolveAccentTint(persisted);

        Assert.Equal(expectedAccentTint, result.AccentTintId);
        Assert.True(result.ShouldPersist);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Magenta")]
    [InlineData("Drakul")]
    [InlineData("NotAnAccent")]
    public void ResolveAccentTint_WithInvalidName_DefaultsToDefaultAndPersists(
        string? persisted)
    {
        AccentTintResolution result = HeimdallThemeService.ResolveAccentTint(persisted);

        Assert.Equal(nameof(AccentTint.Default), result.AccentTintId);
        Assert.True(result.ShouldPersist);
    }
}
