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
using Heimdall.Core.Models;

namespace Heimdall.Core.Tests;

public sealed class ServerProfileDtoOriginTests
{
    [Fact]
    public void NewDto_HasManualOriginByDefault()
    {
        var dto = new ServerProfileDto();

        Assert.Equal(ProfileOrigin.Manual, dto.Origin);
    }

    [Fact]
    public void RoundTrip_ManualOrigin_PreservesValue()
    {
        var json = JsonSerializer.Serialize(new ServerProfileDto { Origin = ProfileOrigin.Manual });
        var dto = JsonSerializer.Deserialize<ServerProfileDto>(json);

        Assert.NotNull(dto);
        Assert.Equal(ProfileOrigin.Manual, dto!.Origin);
    }

    [Theory]
    [InlineData(ProfileOrigin.ImportRdp)]
    [InlineData(ProfileOrigin.ImportOpenSsh)]
    [InlineData(ProfileOrigin.ImportPutty)]
    [InlineData(ProfileOrigin.ImportMRemoteNg)]
    [InlineData(ProfileOrigin.ImportMobaXterm)]
    [InlineData(ProfileOrigin.ImportRdcMan)]
    public void RoundTrip_EachImportOrigin_PreservesValue(ProfileOrigin origin)
    {
        var json = JsonSerializer.Serialize(new ServerProfileDto { Origin = origin });
        var dto = JsonSerializer.Deserialize<ServerProfileDto>(json);

        Assert.NotNull(dto);
        Assert.Equal(origin, dto!.Origin);
    }

    [Fact]
    public void Deserialize_LegacyJsonWithoutOriginField_DefaultsToManual()
    {
        const string json = """
            {
              "Id": "legacy-1",
              "DisplayName": "Legacy profile",
              "RemoteServer": "legacy.example.com"
            }
            """;

        var dto = JsonSerializer.Deserialize<ServerProfileDto>(json);

        Assert.NotNull(dto);
        Assert.Equal(ProfileOrigin.Manual, dto!.Origin);
    }
}
