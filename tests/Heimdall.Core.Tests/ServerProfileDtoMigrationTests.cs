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

using System.Text.Json;
using Heimdall.Core.Configuration;

namespace Heimdall.Core.Tests;

public class ServerProfileDtoMigrationTests
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Deserialize_LegacyDefaultResolutionFields_PopulatesFixedResolution()
    {
        var json = """
        {
            "id": "srv-legacy",
            "displayName": "Legacy",
            "remoteServer": "10.0.0.1",
            "rdpDefaultResolutionWidth": 1920,
            "rdpDefaultResolutionHeight": 1080
        }
        """;

        var dto = JsonSerializer.Deserialize<ServerProfileDto>(json, ReadOptions);

        Assert.NotNull(dto);
        Assert.Equal(1920, dto.RdpFixedWidth);
        Assert.Equal(1080, dto.RdpFixedHeight);
        Assert.Null(typeof(ServerProfileDto).GetProperty("RdpDefaultResolutionWidth")?.GetMethod);
        Assert.Null(typeof(ServerProfileDto).GetProperty("RdpDefaultResolutionHeight")?.GetMethod);
    }

    [Fact]
    public void Deserialize_HybridLegacyAndFixedResolutionFields_FixedResolutionWins()
    {
        AssertFixedResolutionWins("""
        {
            "id": "srv-hybrid",
            "displayName": "Hybrid",
            "remoteServer": "10.0.0.1",
            "rdpDefaultResolutionWidth": 1920,
            "rdpDefaultResolutionHeight": 1080,
            "rdpFixedResolutionWidth": 2560,
            "rdpFixedResolutionHeight": 1440
        }
        """);

        AssertFixedResolutionWins("""
        {
            "id": "srv-hybrid",
            "displayName": "Hybrid",
            "remoteServer": "10.0.0.1",
            "rdpFixedResolutionWidth": 2560,
            "rdpFixedResolutionHeight": 1440,
            "rdpDefaultResolutionWidth": 1920,
            "rdpDefaultResolutionHeight": 1080
        }
        """);
    }

    [Fact]
    public void Serialize_FixedResolution_DoesNotWriteLegacyDefaultResolutionFields()
    {
        var dto = new ServerProfileDto
        {
            RdpResolutionMode = RdpResolutionMode.Fixed,
            RdpFixedWidth = 2560,
            RdpFixedHeight = 1440
        };

        var json = JsonSerializer.Serialize(dto);

        Assert.Contains("rdpFixedResolutionWidth", json);
        Assert.Contains("rdpFixedResolutionHeight", json);
        Assert.DoesNotContain("rdpDefaultResolutionWidth", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rdpDefaultResolutionHeight", json, StringComparison.Ordinal);
    }

    private static void AssertFixedResolutionWins(string json)
    {
        var dto = JsonSerializer.Deserialize<ServerProfileDto>(json, ReadOptions);

        Assert.NotNull(dto);
        Assert.Equal(2560, dto.RdpFixedWidth);
        Assert.Equal(1440, dto.RdpFixedHeight);
    }
}
