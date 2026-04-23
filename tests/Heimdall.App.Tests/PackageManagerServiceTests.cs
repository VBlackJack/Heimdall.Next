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

using System.Diagnostics;
using System.Reflection;
using TwinShell.Infrastructure.Services;

namespace Heimdall.App.Tests;

public sealed class PackageManagerServiceTests
{
    [Theory]
    [InlineData("-help")]
    [InlineData("   /?")]
    public async Task SearchWingetPackagesAsync_OptionShapedSearchTerm_ThrowsArgumentException(string searchTerm)
    {
        var service = new PackageManagerService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SearchWingetPackagesAsync(searchTerm));

        Assert.Equal("searchTerm", ex.ParamName);
        Assert.Contains("Option-shaped values are not allowed", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-force")]
    [InlineData("   /install")]
    public async Task GetChocolateyPackageInfoAsync_OptionShapedPackageId_ThrowsArgumentException(string packageId)
    {
        var service = new PackageManagerService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GetChocolateyPackageInfoAsync(packageId));

        Assert.Equal("packageId", ex.ParamName);
        Assert.Contains("Option-shaped values are not allowed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSearchArguments_Winget_PreservesSpacesAsSingleArgumentToken()
    {
        var arguments = InvokePrivateStatic<IReadOnlyList<string>>(
            nameof(BuildSearchArguments_Winget_PreservesSpacesAsSingleArgumentToken),
            "BuildSearchArguments",
            "winget",
            "Visual Studio Code");
        var processStartInfo = InvokePrivateStatic<ProcessStartInfo>(
            nameof(BuildSearchArguments_Winget_PreservesSpacesAsSingleArgumentToken),
            "CreateProcessStartInfo",
            "winget",
            arguments);

        Assert.Equal(["search", "--", "Visual Studio Code"], arguments);
        Assert.Equal(3, processStartInfo.ArgumentList.Count);
        Assert.Equal("Visual Studio Code", processStartInfo.ArgumentList[2]);
        Assert.Equal(string.Empty, processStartInfo.Arguments);
    }

    [Theory]
    [InlineData("winget", "Git.Git", "show")]
    [InlineData("choco", "git", "info")]
    public void BuildInfoArguments_AppendsEndOfOptionsDelimiter(string command, string packageId, string expectedVerb)
    {
        var arguments = InvokePrivateStatic<IReadOnlyList<string>>(
            nameof(BuildInfoArguments_AppendsEndOfOptionsDelimiter),
            "BuildInfoArguments",
            command,
            packageId);
        var processStartInfo = InvokePrivateStatic<ProcessStartInfo>(
            nameof(BuildInfoArguments_AppendsEndOfOptionsDelimiter),
            "CreateProcessStartInfo",
            command,
            arguments);

        Assert.Equal(expectedVerb, arguments[0]);
        Assert.Equal("--", arguments[^2]);
        Assert.Equal(packageId, arguments[^1]);
        Assert.Equal(expectedVerb, processStartInfo.ArgumentList[0]);
        Assert.Equal("--", processStartInfo.ArgumentList[^2]);
        Assert.Equal(packageId, processStartInfo.ArgumentList[^1]);
    }

    private static T InvokePrivateStatic<T>(string testName, string methodName, params object[] arguments)
    {
        var bindingFlags = BindingFlags.NonPublic | BindingFlags.Static;
        var method = typeof(PackageManagerService).GetMethod(methodName, bindingFlags);
        Assert.True(method is not null, $"{testName}: method '{methodName}' was not found.");

        return (T)method!.Invoke(null, arguments)!;
    }
}
