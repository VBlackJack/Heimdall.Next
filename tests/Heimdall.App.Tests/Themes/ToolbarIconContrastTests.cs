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
using System.Xml.Linq;

namespace Heimdall.App.Tests.Themes;

public sealed class ToolbarIconContrastTests
{
    private const double MinimumUiContrast = 3.0;
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("AlucardTheme.xaml")]
    [InlineData("BladeTheme.xaml")]
    [InlineData("BuffyTheme.xaml")]
    [InlineData("DraculaProTheme.xaml")]
    [InlineData("LincolnTheme.xaml")]
    [InlineData("MorbiusTheme.xaml")]
    [InlineData("VanHelsingTheme.xaml")]
    public void ToolbarIconContrast_MeetsWcagUiThreshold(string themeFile)
    {
        var resources = LoadThemeResources(themeFile);

        AssertContrast(themeFile, "idle", resources, "TextPrimaryBrush", "SurfaceBrush");
        AssertContrast(themeFile, "hover", resources, "TextPrimaryBrush", "CardBrush");
    }

    private static void AssertContrast(
        string themeFile,
        string state,
        ThemeResources resources,
        string foregroundBrushKey,
        string backgroundBrushKey)
    {
        var foreground = ResolveBrushColor(resources, foregroundBrushKey);
        var background = ResolveBrushColor(resources, backgroundBrushKey);
        var ratio = ContrastRatio(foreground, background);

        Assert.True(
            ratio >= MinimumUiContrast,
            $"{themeFile} {state} toolbar icon contrast is {ratio:F2}:1, below {MinimumUiContrast:F1}:1.");
    }

    private static ThemeResources LoadThemeResources(string themeFile)
    {
        var document = XDocument.Load(FindThemePath(themeFile));
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        var brushes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var element in document.Descendants())
        {
            var key = element.Attribute(XamlNamespace + "Key")?.Value;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (element.Name.LocalName == "Color")
            {
                colors[key] = element.Value.Trim();
            }
            else if (element.Name.LocalName == "SolidColorBrush")
            {
                var colorValue = element.Attribute("Color")?.Value;
                if (!string.IsNullOrWhiteSpace(colorValue))
                {
                    brushes[key] = colorValue.Trim();
                }
            }
        }

        return new ThemeResources(colors, brushes);
    }

    private static string FindThemePath(string themeFile)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var sourceCandidate = Path.Combine(
                directory.FullName,
                "src",
                "Heimdall.App",
                "Themes",
                themeFile);
            if (File.Exists(sourceCandidate))
            {
                return sourceCandidate;
            }

            var outputCandidate = Path.Combine(directory.FullName, "Themes", themeFile);
            if (File.Exists(outputCandidate))
            {
                return outputCandidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate theme file '{themeFile}'.");
    }

    private static Rgb ResolveBrushColor(ThemeResources resources, string brushKey)
    {
        if (!resources.Brushes.TryGetValue(brushKey, out var colorValue))
        {
            throw new KeyNotFoundException($"Theme brush '{brushKey}' was not found.");
        }

        return ResolveColorValue(resources, colorValue, []);
    }

    private static Rgb ResolveColorValue(
        ThemeResources resources,
        string colorValue,
        HashSet<string> visitedKeys)
    {
        if (TryGetStaticResourceKey(colorValue, out var resourceKey))
        {
            if (!visitedKeys.Add(resourceKey))
            {
                throw new InvalidOperationException($"Circular color resource reference detected at '{resourceKey}'.");
            }

            if (resources.Colors.TryGetValue(resourceKey, out var colorResource))
            {
                return ResolveColorValue(resources, colorResource, visitedKeys);
            }

            if (resources.Brushes.TryGetValue(resourceKey, out var brushResource))
            {
                return ResolveColorValue(resources, brushResource, visitedKeys);
            }

            throw new KeyNotFoundException($"Theme color resource '{resourceKey}' was not found.");
        }

        return ParseColor(colorValue);
    }

    private static bool TryGetStaticResourceKey(string value, out string key)
    {
        const string prefix = "{StaticResource ";
        const string suffix = "}";

        value = value.Trim();
        if (value.StartsWith(prefix, StringComparison.Ordinal)
            && value.EndsWith(suffix, StringComparison.Ordinal))
        {
            key = value[prefix.Length..^suffix.Length].Trim();
            return true;
        }

        key = string.Empty;
        return false;
    }

    private static Rgb ParseColor(string color)
    {
        var hex = color.Trim().TrimStart('#');
        if (hex.Length == 8)
        {
            hex = hex[2..];
        }

        if (hex.Length != 6)
        {
            throw new FormatException($"Unsupported color format '{color}'.");
        }

        return new Rgb(
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static double ContrastRatio(Rgb first, Rgb second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Rgb color)
        => 0.2126 * Linearize(color.Red)
           + 0.7152 * Linearize(color.Green)
           + 0.0722 * Linearize(color.Blue);

    private static double Linearize(byte channel)
    {
        var normalized = channel / 255.0;
        return normalized <= 0.03928
            ? normalized / 12.92
            : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }

    private sealed record ThemeResources(
        IReadOnlyDictionary<string, string> Colors,
        IReadOnlyDictionary<string, string> Brushes);

    private readonly record struct Rgb(byte Red, byte Green, byte Blue);
}
