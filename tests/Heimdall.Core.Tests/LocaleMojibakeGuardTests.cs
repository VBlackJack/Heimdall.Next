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
using System.Text.Json;

namespace Heimdall.Core.Tests;

/// <summary>
/// Guards the locale source files against double-encoded (mojibake) characters.
/// Failure means a string was written to en.json or fr.json with UTF-8 bytes that
/// had been previously misread as Windows-1252 (or another single-byte codepage)
/// and then re-encoded as UTF-8, producing visually-corrupted text in the UI.
/// </summary>
public sealed class LocaleMojibakeGuardTests
{
    private const string EnLocaleFileName = "en.json";
    private const string FrLocaleFileName = "fr.json";

    /// <summary>
    /// Mojibake markers: short character sequences that almost never occur in
    /// legitimate French or English UI strings but are typical of Windows-1252 →
    /// UTF-8 double-encoding artifacts. Each entry is checked against locale values
    /// after JSON unescape (so <c>Ã‰</c> in source is caught as <c>Ã‰</c> here).
    /// </summary>
    private static readonly IReadOnlyList<string> MojibakeMarkers = new[]
    {
        "Ã‰",  // Ã‰  – mojibake for É
        "Ã©",  // Ã©  – mojibake for é
        "Ã¨",  // Ã¨  – mojibake for è
        "Ãª",  // Ãª  – mojibake for ê
        "Ã ",  // Ã   – mojibake for à
        "Ã´",  // Ã´  – mojibake for ô
        "Ã®",  // Ã®  – mojibake for î
        "Ã¢",  // Ã¢  – mojibake for â
        "Ã»",  // Ã»  – mojibake for û
        "Ã§",  // Ã§  – mojibake for ç
        "Â«",  // Â«  – mojibake for «
        "Â»",  // Â»  – mojibake for »
        "Â ",  // Â<NBSP> – mojibake for NBSP
        "â€”", // â€"  – mojibake for —
        "â€“", // â€"  – mojibake for –
        "â€¦", // â€¦  – mojibake for …
        "â€™", // â€™  – mojibake for '
        "â€˜", // â€˜  – mojibake for '
        "â€œ", // â€œ  – mojibake for "
        "â†’", // â†'  – mojibake for →
        "â†‘", // â†'  – mojibake for ←
        "�",        // U+FFFD replacement character (lost-encoding marker)
    };

    [Theory]
    [InlineData(EnLocaleFileName)]
    [InlineData(FrLocaleFileName)]
    public void LocaleValues_DoNotContainMojibakeMarkers(string fileName)
    {
        string repoRoot = FindRepoRoot();
        string localePath = Path.Combine(repoRoot, "locales", fileName);

        Assert.True(
            File.Exists(localePath),
            $"Locale file not found: {localePath}");

        string raw = File.ReadAllText(localePath, System.Text.Encoding.UTF8);
        using var document = JsonDocument.Parse(raw);

        List<string> violations = new();
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;

            string? value = property.Value.GetString();
            if (string.IsNullOrEmpty(value))
                continue;

            foreach (string marker in MojibakeMarkers)
            {
                int index = value.IndexOf(marker, StringComparison.Ordinal);
                if (index >= 0)
                {
                    violations.Add(
                        $"  {fileName}::{property.Name} contains marker "
                        + $"U+{(int)marker[0]:X4}{(marker.Length > 1 ? "+" : string.Empty)}"
                        + $"{(marker.Length > 1 ? $"U+{(int)marker[1]:X4}" : string.Empty)}"
                        + $"{(marker.Length > 2 ? $"+U+{(int)marker[2]:X4}" : string.Empty)}"
                        + $" at position {index}");
                    break;
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} mojibake violation(s) in {fileName}:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Heimdall.slnx")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException(
            $"Cannot find repository root containing Heimdall.slnx from test binary directory: {AppContext.BaseDirectory}");
    }
}
