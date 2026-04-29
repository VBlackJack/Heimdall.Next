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
using Heimdall.App.Views.EmbeddedRdp;

namespace Heimdall.App.Tests;

public sealed class RdpSessionStatusKeysTests
{
    [Theory]
    [InlineData((int)RdpSessionStatus.Preparing, "RdpStatusPreparing")]
    [InlineData((int)RdpSessionStatus.Connecting, "RdpStatusConnecting")]
    [InlineData((int)RdpSessionStatus.Connected, "RdpStatusConnected")]
    [InlineData((int)RdpSessionStatus.Disconnecting, "RdpStatusDisconnecting")]
    [InlineData((int)RdpSessionStatus.Disconnected, "RdpStatusDisconnected")]
    [InlineData((int)RdpSessionStatus.Reconnecting, "RdpStatusReconnecting")]
    [InlineData((int)RdpSessionStatus.Error, "RdpStatusError")]
    public void GetKey_ReturnsStableKey(int statusValue, string expected)
    {
        var status = (RdpSessionStatus)statusValue;

        Assert.Equal(expected, RdpSessionStatusKeys.GetKey(status));
    }

    [Fact]
    public void EveryEnumValue_HasAnInvariantCode()
    {
        foreach (var status in Enum.GetValues<RdpSessionStatus>())
        {
            var code = RdpSessionStatusKeys.GetInvariantCode(status);

            Assert.False(string.IsNullOrWhiteSpace(code));
            Assert.DoesNotContain(' ', code);
        }
    }

    [Fact]
    public void EveryEnumValue_ResolvesToLocaleKeyPresentInEnglish()
    {
        using var document = LoadLocaleDocument("en");

        foreach (var status in Enum.GetValues<RdpSessionStatus>())
        {
            var key = RdpSessionStatusKeys.GetKey(status);

            Assert.True(
                document.RootElement.TryGetProperty(key, out _),
                $"Locale key '{key}' for {status} is missing from en.json");
        }
    }

    [Theory]
    [InlineData("BtnCancelReconnect")]
    [InlineData("TooltipCancelReconnect")]
    [InlineData("A11yCancelReconnect")]
    public void CancelReconnectKeys_ArePresentInEnglish(string key)
    {
        using var document = LoadLocaleDocument("en");

        Assert.True(
            document.RootElement.TryGetProperty(key, out _),
            $"Locale key '{key}' is missing from en.json");
    }

    [Theory]
    [InlineData("BtnCancelReconnect")]
    [InlineData("TooltipCancelReconnect")]
    [InlineData("A11yCancelReconnect")]
    public void CancelReconnectKeys_ArePresentInFrench(string key)
    {
        using var document = LoadLocaleDocument("fr");

        Assert.True(
            document.RootElement.TryGetProperty(key, out _),
            $"Locale key '{key}' is missing from fr.json");
    }

    [Theory]
    [InlineData("RdpAutofillSearching")]
    [InlineData("RdpAutofillFilled")]
    [InlineData("RdpAutofillTimedOut")]
    [InlineData("RdpAutofillFailed")]
    public void AutofillKeys_ArePresentInEnglish(string key)
    {
        using var document = LoadLocaleDocument("en");

        Assert.True(
            document.RootElement.TryGetProperty(key, out _),
            $"Locale key '{key}' is missing from en.json");
    }

    [Theory]
    [InlineData("RdpAutofillSearching")]
    [InlineData("RdpAutofillFilled")]
    [InlineData("RdpAutofillTimedOut")]
    [InlineData("RdpAutofillFailed")]
    public void AutofillKeys_ArePresentInFrench(string key)
    {
        using var document = LoadLocaleDocument("fr");

        Assert.True(
            document.RootElement.TryGetProperty(key, out _),
            $"Locale key '{key}' is missing from fr.json");
    }

    [Theory]
    [InlineData("RdpAspectPreserve")]
    [InlineData("RdpAspect16x9")]
    [InlineData("RdpAspect4x3")]
    [InlineData("RdpAspect21x9")]
    public void AspectRatioKeys_ArePresentInEnglish(string key)
    {
        using var document = LoadLocaleDocument("en");

        Assert.True(
            document.RootElement.TryGetProperty(key, out _),
            $"Locale key '{key}' is missing from en.json");
    }

    [Theory]
    [InlineData("RdpAspectPreserve")]
    [InlineData("RdpAspect16x9")]
    [InlineData("RdpAspect4x3")]
    [InlineData("RdpAspect21x9")]
    public void AspectRatioKeys_ArePresentInFrench(string key)
    {
        using var document = LoadLocaleDocument("fr");

        Assert.True(
            document.RootElement.TryGetProperty(key, out _),
            $"Locale key '{key}' is missing from fr.json");
    }

    [Fact]
    public void ExternalClientStatusKey_IsPresentInEnglish()
    {
        using var document = LoadLocaleDocument("en");

        Assert.True(
            document.RootElement.TryGetProperty("StatusLaunchedExternalClient", out _),
            "Locale key 'StatusLaunchedExternalClient' is missing from en.json");
    }

    [Fact]
    public void ExternalClientStatusKey_IsPresentInFrench()
    {
        using var document = LoadLocaleDocument("fr");

        Assert.True(
            document.RootElement.TryGetProperty("StatusLaunchedExternalClient", out _),
            "Locale key 'StatusLaunchedExternalClient' is missing from fr.json");
    }

    [Fact]
    public void RdpStatusReconnecting_HasAttemptAndCapPlaceholdersInEnglish()
    {
        using var document = LoadLocaleDocument("en");

        Assert.True(document.RootElement.TryGetProperty("RdpStatusReconnecting", out var value));
        var reconnecting = value.GetString() ?? string.Empty;

        Assert.Contains("{0}", reconnecting);
        Assert.Contains("{1}", reconnecting);
    }

    private static JsonDocument LoadLocaleDocument(string locale)
    {
        var localesPath = FindLocalesPath();
        var jsonPath = Path.Combine(localesPath, $"{locale}.json");

        Assert.True(File.Exists(jsonPath), $"{locale}.json not found at {jsonPath}");

        return JsonDocument.Parse(File.ReadAllText(jsonPath));
    }

    private static string FindLocalesPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "locales");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "en.json")))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        var fallback = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "locales"));

        if (Directory.Exists(fallback))
        {
            return fallback;
        }

        throw new DirectoryNotFoundException(
            "Cannot find locales/ directory. Ensure the test runs from the repository.");
    }
}
