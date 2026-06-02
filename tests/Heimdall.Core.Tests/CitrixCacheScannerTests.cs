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

using Heimdall.Core.Configuration;

namespace Heimdall.Core.Tests;

public sealed class CitrixCacheScannerTests
{
    [Fact]
    public void ToServerProfiles_EmptyInput_ReturnsEmptyList()
    {
        CitrixResource[] resources = [];

        List<ServerProfileDto> profiles = CitrixCacheScanner.ToServerProfiles(resources);

        Assert.Empty(profiles);
    }

    [Fact]
    public void ToServerProfiles_SingleResource_MapsConstantAndPassthroughFields()
    {
        CitrixResource resource = BuildResource(
            friendlyName: "Published Excel",
            launchCommandLine: @"SelfService.exe -qlaunch ""Published Excel""",
            storeFrontUrl: "https://store.example.com");
        CitrixResource[] resources = [resource];

        List<ServerProfileDto> profiles = CitrixCacheScanner.ToServerProfiles(resources);

        ServerProfileDto profile = Assert.Single(profiles);
        Assert.Equal("Published Excel", profile.DisplayName);
        Assert.Equal("Published Excel", profile.CitrixAppName);
        Assert.Equal("Citrix", profile.ConnectionType);
        Assert.Equal(@"SelfService.exe -qlaunch ""Published Excel""", profile.CitrixLaunchCommandLine);
        Assert.Equal("https://store.example.com", profile.CitrixStoreFrontUrl);
        Assert.True(profile.CitrixUseSso);
        Assert.True(profile.UseDirectConnection);
    }

    [Theory]
    [InlineData(null, "Citrix Local")]
    [InlineData("", "Citrix Local")]
    [InlineData("   ", "Citrix Local")]
    [InlineData("https://sf.example.com", "https://sf.example.com")]
    public void ToServerProfiles_StoreFrontUrl_MapsRemoteServerFallback(
        string? storeFrontUrl,
        string expectedRemoteServer)
    {
        CitrixResource[] resources = [BuildResource(storeFrontUrl: storeFrontUrl)];

        List<ServerProfileDto> profiles = CitrixCacheScanner.ToServerProfiles(resources);

        ServerProfileDto profile = Assert.Single(profiles);
        Assert.Equal(expectedRemoteServer, profile.RemoteServer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToServerProfiles_BlankCategory_MapsNullGroup(string? category)
    {
        CitrixResource[] resources = [BuildResource(category: category)];

        List<ServerProfileDto> profiles = CitrixCacheScanner.ToServerProfiles(resources);

        ServerProfileDto profile = Assert.Single(profiles);
        Assert.Null(profile.Group);
    }

    [Fact]
    public void ToServerProfiles_PlainCategory_PrefixesGroup()
    {
        CitrixResource[] resources = [BuildResource(category: "Productivity")];

        List<ServerProfileDto> profiles = CitrixCacheScanner.ToServerProfiles(resources);

        ServerProfileDto profile = Assert.Single(profiles);
        Assert.Equal("Citrix/Productivity", profile.Group);
    }

    [Fact]
    public void ToServerProfiles_CategoryBackslashes_NormalizesGroupSeparators()
    {
        CitrixResource[] resources = [BuildResource(category: @"Dept\Team\Sub")];

        List<ServerProfileDto> profiles = CitrixCacheScanner.ToServerProfiles(resources);

        ServerProfileDto profile = Assert.Single(profiles);
        Assert.Equal("Citrix/Dept/Team/Sub", profile.Group);
    }

    [Fact]
    public void ToServerProfiles_MultipleResources_AssignsDistinctParseableIds()
    {
        CitrixResource[] resources =
        [
            BuildResource(friendlyName: "First App"),
            BuildResource(friendlyName: "Second App")
        ];

        List<ServerProfileDto> profiles = CitrixCacheScanner.ToServerProfiles(resources);

        Assert.Equal(2, profiles.Count);
        Assert.True(Guid.TryParse(profiles[0].Id, out Guid firstId));
        Assert.True(Guid.TryParse(profiles[1].Id, out Guid secondId));
        Assert.NotEqual(firstId, secondId);
        Assert.NotEqual(profiles[0].Id, profiles[1].Id);
    }

    [Fact]
    public void ToServerProfiles_MultipleResources_PreservesCountAndOrder()
    {
        CitrixResource[] resources =
        [
            BuildResource(friendlyName: "First App"),
            BuildResource(friendlyName: "Second App"),
            BuildResource(friendlyName: "Third App")
        ];

        List<ServerProfileDto> profiles = CitrixCacheScanner.ToServerProfiles(resources);

        Assert.Equal(3, profiles.Count);
        Assert.Equal("First App", profiles[0].DisplayName);
        Assert.Equal("Second App", profiles[1].DisplayName);
        Assert.Equal("Third App", profiles[2].DisplayName);
    }

    private static CitrixResource BuildResource(
        string friendlyName = "Published App",
        string? category = "Applications",
        string launchCommandLine = @"SelfService.exe -qlaunch ""Published App""",
        string? storeFrontUrl = "https://store.example.com")
    {
        return new CitrixResource
        {
            FriendlyName = friendlyName,
            Category = category,
            LaunchCommandLine = launchCommandLine,
            StoreFrontUrl = storeFrontUrl
        };
    }
}
