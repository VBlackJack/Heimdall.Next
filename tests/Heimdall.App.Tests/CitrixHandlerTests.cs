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

using Heimdall.App.Services.Handlers;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.StateMachine;

namespace Heimdall.App.Tests;

public sealed class CitrixHandlerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/")]
    [InlineData("/path")]
    [InlineData("http:/")]
    public void TryValidateStoreFrontUrl_RejectsInvalidUrls(string? rawUrl)
    {
        var isValid = CitrixHandler.TryValidateStoreFrontUrl(rawUrl, out var validatedUrl);

        Assert.False(isValid);
        Assert.Equal(string.Empty, validatedUrl);
    }

    [Theory]
    [InlineData("https://citrix.example.com/Citrix/StoreWeb/")]
    [InlineData("http://internal-citrix.local:8080/StoreWeb/")]
    public void TryValidateStoreFrontUrl_AcceptsAbsoluteHttpAndHttpsUrls(string rawUrl)
    {
        var isValid = CitrixHandler.TryValidateStoreFrontUrl(rawUrl, out var validatedUrl);

        Assert.True(isValid);
        Assert.Equal(rawUrl, validatedUrl);
    }

    [Fact]
    public void CreateStoreFrontStartInfo_UsesArgumentListAndPreservesQuotedAppNameToken()
    {
        var appName = "Calculator \"Admin\" Tool";
        var storeFrontUrl = "https://citrix.example.com/Citrix/StoreWeb/";

        var startInfo = CitrixHandler.CreateStoreFrontStartInfo(
            @"C:\Program Files (x86)\Citrix\ICA Client\storebrowse.exe",
            appName,
            storeFrontUrl,
            useSso: true);

        Assert.Equal(@"C:\Program Files (x86)\Citrix\ICA Client\storebrowse.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(4, startInfo.ArgumentList.Count);
        Assert.Equal("-L", startInfo.ArgumentList[0]);
        Assert.Equal("-S", startInfo.ArgumentList[1]);
        Assert.Equal(appName, startInfo.ArgumentList[2]);
        Assert.Equal(storeFrontUrl, startInfo.ArgumentList[3]);
        Assert.Equal(string.Empty, startInfo.Arguments);
    }

    [Fact]
    public void CreateStoreFrontStartInfo_WithoutSso_OmitsSessionLaunchFlag()
    {
        var startInfo = CitrixHandler.CreateStoreFrontStartInfo(
            "storebrowse.exe",
            "Calculator",
            "https://citrix.example.com/Citrix/StoreWeb/",
            useSso: false);

        Assert.Equal(3, startInfo.ArgumentList.Count);
        Assert.Equal("-L", startInfo.ArgumentList[0]);
        Assert.Equal("Calculator", startInfo.ArgumentList[1]);
        Assert.Equal("https://citrix.example.com/Citrix/StoreWeb/", startInfo.ArgumentList[2]);
    }

    [Fact]
    public async Task ConnectAsync_InvalidStoreFrontUrl_ReturnsLocalizedError()
    {
        var handler = new CitrixHandler(new ConnectionStateMachine(), new LocalizationManager());
        var server = new ServerProfileDto
        {
            Id = "srv-citrix-invalid-url",
            DisplayName = "Citrix test",
            CitrixAppName = "Calculator",
            CitrixStoreFrontUrl = "javascript:alert(1)"
        };

        var result = await handler.ConnectAsync(server, new AppSettings(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CitrixInvalidStoreFrontUrl", result.ErrorMessage);
        Assert.Null(result.Session);
    }
}
