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
        var localesPath = FindLocalesPath();
        var enJsonPath = Path.Combine(localesPath, "en.json");

        Assert.True(File.Exists(enJsonPath), $"en.json not found at {enJsonPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(enJsonPath));
        var root = document.RootElement;

        foreach (var status in Enum.GetValues<RdpSessionStatus>())
        {
            var key = RdpSessionStatusKeys.GetKey(status);

            Assert.True(
                root.TryGetProperty(key, out _),
                $"Locale key '{key}' for {status} is missing from en.json");
        }
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
