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

using System.Text;
using Heimdall.Core.Localization;

namespace Heimdall.Core.Tests;

public class LocalizationManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _localesPath;
    private readonly LocalizationManager _manager;

    public LocalizationManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Heimdall.Tests." + Guid.NewGuid().ToString("N"));
        _localesPath = Path.Combine(_tempDir, "locales");
        Directory.CreateDirectory(_localesPath);
        _manager = new LocalizationManager();

        // Create test locale files
        var enJson = """
        {
            "StatusReady": "Ready",
            "StatusConnecting": "Connecting to {0}...",
            "StatusProgress": "Step {0} of {1}",
            "BtnConnect": "Connect",
            "ErrorNotFound": "Resource not found",
            "MalformedFormat": "Value is {invalid}"
        }
        """;
        File.WriteAllText(Path.Combine(_localesPath, "en.json"), enJson, new UTF8Encoding(false));

        var frJson = """
        {
            "StatusReady": "Pr\u00eat",
            "StatusConnecting": "Connexion \u00e0 {0}...",
            "StatusProgress": "\u00c9tape {0} sur {1}",
            "BtnConnect": "Connecter",
            "ErrorNotFound": "Ressource introuvable"
        }
        """;
        File.WriteAllText(Path.Combine(_localesPath, "fr.json"), frJson, new UTF8Encoding(false));

        var emptyJson = "{}";
        File.WriteAllText(Path.Combine(_localesPath, "empty.json"), emptyJson, new UTF8Encoding(false));

        var specialJson = """
        {
            "MultiLine": "Line one\nLine two",
            "UnicodeEmoji": "\u2714 Done",
            "TabChar": "Col1\tCol2",
            "QuotedValue": "She said \"hello\""
        }
        """;
        File.WriteAllText(Path.Combine(_localesPath, "special.json"), specialJson, new UTF8Encoding(false));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    // -- LoadAsync -----------------------------------------------------------

    [Fact]
    public async Task LoadAsync_LoadsKeysFromJsonLocaleFile()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.True(_manager.KeyCount > 0);
        Assert.Equal(6, _manager.KeyCount);
    }

    [Fact]
    public async Task LoadAsync_ThrowsOnNullLocalesPath()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _manager.LoadAsync(null!, "en"));
    }

    [Fact]
    public async Task LoadAsync_ThrowsOnEmptyLocale()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _manager.LoadAsync(_localesPath, ""));
    }

    [Fact]
    public async Task LoadAsync_ThrowsOnWhitespaceLocale()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _manager.LoadAsync(_localesPath, "   "));
    }

    [Fact]
    public async Task LoadAsync_ThrowsFileNotFoundException_WhenLocaleFileMissing()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _manager.LoadAsync(_localesPath, "de"));
    }

    [Fact]
    public async Task LoadAsync_EmptyJsonFile_ReturnsEmptyDictionary()
    {
        await _manager.LoadAsync(_localesPath, "empty");

        Assert.Equal(0, _manager.KeyCount);
        Assert.Equal("empty", _manager.CurrentLocale);
    }

    // -- Indexer -------------------------------------------------------------

    [Fact]
    public async Task Indexer_ReturnsCorrectValue_ForExistingKey()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.Equal("Ready", _manager["StatusReady"]);
        Assert.Equal("Connect", _manager["BtnConnect"]);
    }

    [Fact]
    public async Task Indexer_ReturnsKeyName_ForMissingKey()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.Equal("NonExistentKey", _manager["NonExistentKey"]);
    }

    // -- GetString -----------------------------------------------------------

    [Fact]
    public async Task GetString_ReturnsValue_ForExistingKey()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.Equal("Resource not found", _manager.GetString("ErrorNotFound"));
    }

    [Fact]
    public async Task GetString_ReturnsKeyAsFallback_ForMissingKey()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.Equal("SomeUnknownKey", _manager.GetString("SomeUnknownKey"));
    }

    [Fact]
    public async Task GetString_ThrowsOnNullKey()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.Throws<ArgumentNullException>(() => _manager.GetString(null!));
    }

    [Fact]
    public async Task GetString_ThrowsOnEmptyKey()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.Throws<ArgumentException>(() => _manager.GetString(""));
    }

    [Fact]
    public async Task GetString_ThrowsOnWhitespaceKey()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.Throws<ArgumentException>(() => _manager.GetString("   "));
    }

    // -- Format --------------------------------------------------------------

    [Fact]
    public async Task Format_SubstitutesPlaceholders()
    {
        await _manager.LoadAsync(_localesPath, "en");

        var result = _manager.Format("StatusConnecting", "Server01");

        Assert.Equal("Connecting to Server01...", result);
    }

    [Fact]
    public async Task Format_SubstitutesMultiplePlaceholders()
    {
        await _manager.LoadAsync(_localesPath, "en");

        var result = _manager.Format("StatusProgress", 3, 10);

        Assert.Equal("Step 3 of 10", result);
    }

    [Fact]
    public async Task Format_WithMissingKey_ReturnsKeyName()
    {
        await _manager.LoadAsync(_localesPath, "en");

        var result = _manager.Format("MissingFormatKey", "arg1");

        Assert.Equal("MissingFormatKey", result);
    }

    [Fact]
    public async Task Format_WithNoArgs_ReturnsTemplate()
    {
        await _manager.LoadAsync(_localesPath, "en");

        var result = _manager.Format("StatusReady");

        Assert.Equal("Ready", result);
    }

    [Fact]
    public async Task Format_WithMalformedFormatString_ReturnsTemplateAsIs()
    {
        await _manager.LoadAsync(_localesPath, "en");

        var result = _manager.Format("MalformedFormat", "test");

        Assert.Equal("Value is {invalid}", result);
    }

    // -- SwitchLocaleAsync ---------------------------------------------------

    [Fact]
    public async Task SwitchLocaleAsync_ChangesActiveLocale()
    {
        await _manager.LoadAsync(_localesPath, "en");
        Assert.Equal("en", _manager.CurrentLocale);
        Assert.Equal("Ready", _manager["StatusReady"]);

        await _manager.SwitchLocaleAsync("fr");

        Assert.Equal("fr", _manager.CurrentLocale);
        Assert.Equal("Pr\u00eat", _manager["StatusReady"]);
    }

    [Fact]
    public async Task SwitchLocaleAsync_ThrowsInvalidOperationException_BeforeInitialLoad()
    {
        var freshManager = new LocalizationManager();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => freshManager.SwitchLocaleAsync("fr"));
    }

    [Fact]
    public async Task SwitchLocaleAsync_ThrowsFileNotFoundException_ForMissingLocale()
    {
        await _manager.LoadAsync(_localesPath, "en");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _manager.SwitchLocaleAsync("ja"));
    }

    // -- CurrentLocale -------------------------------------------------------

    [Fact]
    public void CurrentLocale_DefaultsToEn()
    {
        var freshManager = new LocalizationManager();

        Assert.Equal("en", freshManager.CurrentLocale);
    }

    [Fact]
    public async Task CurrentLocale_ReflectsLoadedLocale()
    {
        await _manager.LoadAsync(_localesPath, "fr");

        Assert.Equal("fr", _manager.CurrentLocale);
    }

    // -- Special characters --------------------------------------------------

    [Fact]
    public async Task Keys_WithNewlines_ArePreserved()
    {
        await _manager.LoadAsync(_localesPath, "special");

        Assert.Equal("Line one\nLine two", _manager["MultiLine"]);
    }

    [Fact]
    public async Task Keys_WithUnicode_ArePreserved()
    {
        await _manager.LoadAsync(_localesPath, "special");

        Assert.Equal("\u2714 Done", _manager["UnicodeEmoji"]);
    }

    [Fact]
    public async Task Keys_WithTabs_ArePreserved()
    {
        await _manager.LoadAsync(_localesPath, "special");

        Assert.Equal("Col1\tCol2", _manager["TabChar"]);
    }

    [Fact]
    public async Task Keys_WithEscapedQuotes_ArePreserved()
    {
        await _manager.LoadAsync(_localesPath, "special");

        Assert.Equal("She said \"hello\"", _manager["QuotedValue"]);
    }

    // -- HasKey --------------------------------------------------------------

    [Fact]
    public async Task HasKey_ReturnsTrue_ForExistingKey()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.True(_manager.HasKey("StatusReady"));
    }

    [Fact]
    public async Task HasKey_ReturnsFalse_ForMissingKey()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.False(_manager.HasKey("NonExistent"));
    }

    [Fact]
    public void HasKey_ReturnsFalse_ForNullOrWhitespace()
    {
        Assert.False(_manager.HasKey(null!));
        Assert.False(_manager.HasKey(""));
        Assert.False(_manager.HasKey("   "));
    }

    // -- GetAvailableLocales -------------------------------------------------

    [Fact]
    public async Task GetAvailableLocales_ReturnsAllLocaleFiles()
    {
        await _manager.LoadAsync(_localesPath, "en");

        var locales = _manager.GetAvailableLocales();

        Assert.Contains("en", locales);
        Assert.Contains("fr", locales);
        Assert.Contains("empty", locales);
        Assert.Contains("special", locales);
        Assert.Equal(4, locales.Count);
    }

    [Fact]
    public void GetAvailableLocales_ReturnsEmpty_BeforeLoad()
    {
        var freshManager = new LocalizationManager();

        var locales = freshManager.GetAvailableLocales();

        Assert.Empty(locales);
    }

    // -- KeyCount ------------------------------------------------------------

    [Fact]
    public void KeyCount_IsZero_BeforeLoad()
    {
        var freshManager = new LocalizationManager();

        Assert.Equal(0, freshManager.KeyCount);
    }

    [Fact]
    public async Task KeyCount_ReflectsLoadedKeys()
    {
        await _manager.LoadAsync(_localesPath, "en");

        Assert.Equal(6, _manager.KeyCount);
    }

    [Fact]
    public async Task KeyCount_UpdatesAfterLocaleSwitch()
    {
        await _manager.LoadAsync(_localesPath, "en");
        Assert.Equal(6, _manager.KeyCount);

        await _manager.SwitchLocaleAsync("fr");
        Assert.Equal(5, _manager.KeyCount);
    }
}
