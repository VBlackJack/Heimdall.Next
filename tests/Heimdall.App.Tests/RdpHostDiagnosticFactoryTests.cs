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
using Heimdall.Core.SessionDiagnostics;

namespace Heimdall.App.Tests;

public sealed class RdpHostDiagnosticFactoryTests
{
    [Theory]
    [InlineData(0, "RdpDisconnectNoInfo")]
    [InlineData(516, "RdpDisconnectSocketConnectFailed")]
    [InlineData(2055, "RdpDisconnectBadCredentials")]
    [InlineData(99999, "RdpDisconnectUnknownCode")]
    public void FromDisconnect_MapsReasonToMessageKey(int code, string expectedKey)
    {
        var diagnostic = RdpHostDiagnosticFactory.FromDisconnect(code);

        Assert.Equal(SessionFailureStage.RdpActiveXDisconnect, diagnostic.Stage);
        Assert.Equal(expectedKey, diagnostic.MessageKey);
        Assert.Equal(code, diagnostic.Code);
        Assert.Null(diagnostic.Detail);
    }

    [Fact]
    public void FromFatalError_UsesFatalErrorDetailMessageKey()
    {
        var diagnostic = RdpHostDiagnosticFactory.FromFatalError(260);

        Assert.Equal(SessionFailureStage.RdpActiveXDisconnect, diagnostic.Stage);
        Assert.Equal("RdpStatusFatalErrorDetail", diagnostic.MessageKey);
        Assert.Equal(260, diagnostic.Code);
        Assert.Null(diagnostic.Detail);
    }

    [Fact]
    public void FatalErrorDetailTemplate_HasCodePlaceholderInBothLocales()
    {
        using var en = LoadLocaleDocument("en");
        Assert.True(en.RootElement.TryGetProperty("RdpStatusFatalErrorDetail", out var enValue));
        Assert.Contains("{0}", enValue.GetString() ?? string.Empty, StringComparison.Ordinal);

        using var fr = LoadLocaleDocument("fr");
        Assert.True(fr.RootElement.TryGetProperty("RdpStatusFatalErrorDetail", out var frValue));
        Assert.Contains("{0}", frValue.GetString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void FromDisconnect_WithDifferentReasons_UsesDistinctMessageKeys()
    {
        var socketFailure = RdpHostDiagnosticFactory.FromDisconnect(516);
        var resolutionFailure = RdpHostDiagnosticFactory.FromDisconnect(4360);

        Assert.NotEqual(socketFailure.MessageKey, resolutionFailure.MessageKey);
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
