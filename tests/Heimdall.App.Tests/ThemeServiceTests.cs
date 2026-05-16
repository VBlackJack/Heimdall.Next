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
using System.Linq;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Tests;

/// <summary>
/// Tests for <see cref="ThemeService"/>.
///
/// The service depends on <c>Application.Current</c> (live WPF application) for
/// every meaningful state mutation: <see cref="ThemeService.ApplyTheme"/> early-returns
/// when <c>Application.Current</c> is null. xUnit runs tests outside a WPF Application
/// context, so the migration / idempotence / event / revision-counter scenarios are
/// scaffolded with <c>[Fact(Skip = ...)]</c> rather than left as hard failures. The
/// pure-logic surface (available themes, constructor defaults, no-throw under no-app)
/// is fully covered.
/// </summary>
public class ThemeServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly ConfigManager _configManager;
    private readonly ThemeService _service;

    public ThemeServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"heimdall-theme-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_testDir, "config"));
        _configManager = new ConfigManager(_testDir);
        _service = new ThemeService(_configManager);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); }
        catch { /* test cleanup */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AvailableThemes_Contains_All_Dracula_Variants()
    {
        var themes = ThemeService.AvailableThemes.ToList();

        Assert.Equal(11, themes.Count);
        Assert.Contains("DraculaPro", themes);
        Assert.Contains("Alucard", themes);
        Assert.Contains("Blade", themes);
        Assert.Contains("Buffy", themes);
        Assert.Contains("Lincoln", themes);
        Assert.Contains("Morbius", themes);
        Assert.Contains("VanHelsing", themes);
        Assert.Contains("Nosferatu", themes);
        Assert.Contains("Renfield", themes);
        Assert.Contains("Carmilla", themes);
        Assert.Contains("Drakula", themes);
    }

    [Fact]
    public void Constructor_DefaultsTo_DraculaPro()
    {
        Assert.Equal("DraculaPro", _service.CurrentTheme);
    }

    [Fact]
    public void Constructor_ThemeRevision_Starts_At_Zero()
    {
        Assert.Equal(0, _service.ThemeRevision);
    }

    [Fact]
    public void ApplyTheme_Without_Application_Does_Not_Throw()
    {
        // In a pure xUnit runner Application.Current is null. ApplyTheme must
        // early-return instead of NRE'ing — verify it stays at defaults.
        var before = _service.ThemeRevision;
        _service.ApplyTheme("Alucard");
        _service.ApplyTheme(null);
        _service.ApplyTheme("");
        _service.ApplyTheme("UnknownThemeName");

        Assert.Equal(before, _service.ThemeRevision);
        Assert.Equal("DraculaPro", _service.CurrentTheme);
    }

    [Theory(Skip = "Requires WPF Application context (Application.Current must be live)")]
    [InlineData("Dark", "DraculaPro")]
    [InlineData("Light", "DraculaPro")]
    [InlineData("dark", "DraculaPro")]
    [InlineData("LIGHT", "DraculaPro")]
    [InlineData("", "DraculaPro")]
    [InlineData(null, "DraculaPro")]
    public void ApplyTheme_Migrates_Legacy_Names_To_DraculaPro(string? input, string expected)
    {
        _service.ApplyTheme(input);
        Assert.Equal(expected, _service.CurrentTheme);
    }

    [Fact(Skip = "Requires WPF Application context (Application.Current must be live)")]
    public void ApplyTheme_Same_Theme_Twice_Does_Not_Increment_Revision()
    {
        _service.ApplyTheme("Alucard");
        var revision = _service.ThemeRevision;
        _service.ApplyTheme("Alucard");

        Assert.Equal(revision, _service.ThemeRevision);
    }

    [Fact(Skip = "Requires WPF Application context (Application.Current must be live)")]
    public void ApplyTheme_Different_Themes_Increments_Revision()
    {
        var before = _service.ThemeRevision;
        _service.ApplyTheme("Alucard");
        _service.ApplyTheme("Blade");
        _service.ApplyTheme("Buffy");

        Assert.Equal(before + 3, _service.ThemeRevision);
    }

    [Fact(Skip = "Requires WPF Application context (Application.Current must be live)")]
    public void ApplyTheme_Fires_ThemeChanged_Event_With_Theme_Name()
    {
        string? received = null;
        _service.ThemeChanged += name => received = name;

        _service.ApplyTheme("Lincoln");

        Assert.Equal("Lincoln", received);
    }

    [Fact(Skip = "Requires WPF Application context (Application.Current must be live)")]
    public void ApplyTheme_Unknown_Name_Falls_Back_To_DraculaPro()
    {
        _service.ApplyTheme("NotARealTheme");
        Assert.Equal("DraculaPro", _service.CurrentTheme);
    }

    [Fact(Skip = "Requires WPF Application context (Application.Current must be live)")]
    public void ApplyTheme_Normalizes_Casing_To_Canonical_Key()
    {
        _service.ApplyTheme("alucard");
        Assert.Equal("Alucard", _service.CurrentTheme);
    }
}
