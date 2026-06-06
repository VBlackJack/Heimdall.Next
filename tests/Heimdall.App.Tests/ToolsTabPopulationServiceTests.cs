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

using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

public sealed class ToolsTabPopulationServiceTests
{
    [Fact]
    public void GroupExternalToolsByProvider_IgnoresNativeExternalTools()
    {
        var native = CreateExternalDescriptor("WOL", "PaletteToolWol", null);
        var detected = CreateExternalDescriptor("EXT:SYSINTERNALS:PSEXEC", "PsExec", "Sysinternals");

        var groups = ToolsTabPopulationService.GroupExternalToolsByProvider([native, detected]);

        var group = Assert.Single(groups);
        Assert.Equal("Sysinternals", group.ProviderName);
        var tool = Assert.Single(group.Tools);
        Assert.Equal("EXT:SYSINTERNALS:PSEXEC", tool.Id);
    }

    [Fact]
    public void GroupExternalToolsByProvider_GroupsProviderNamesCaseInsensitively()
    {
        var psExec = CreateExternalDescriptor("EXT:SYSINTERNALS:PSEXEC", "PsExec", "Sysinternals");
        var psInfo = CreateExternalDescriptor("EXT:SYSINTERNALS:PSINFO", "PsInfo", "sysinternals");
        var currPorts = CreateExternalDescriptor("EXT:NIRSOFT:CURRPORTS", "CurrPorts", "NirSoft");

        var groups = ToolsTabPopulationService.GroupExternalToolsByProvider([psExec, psInfo, currPorts]);

        Assert.Collection(
            groups,
            group =>
            {
                Assert.Equal("Sysinternals", group.ProviderName);
                Assert.Equal(
                    new[] { "EXT:SYSINTERNALS:PSEXEC", "EXT:SYSINTERNALS:PSINFO" },
                    group.Tools.Select(tool => tool.Id));
            },
            group =>
            {
                Assert.Equal("NirSoft", group.ProviderName);
                var tool = Assert.Single(group.Tools);
                Assert.Equal("EXT:NIRSOFT:CURRPORTS", tool.Id);
            });
    }

    [Fact]
    public void RegisterExternalTools_CapturesProviderNameOnDynamicDescriptors()
    {
        var registry = new ToolRegistry();

        registry.RegisterExternalTools(
        [
            new ExternalToolInfo
            {
                Id = "PSEXEC",
                Name = "PsExec",
                ExecutablePath = @"C:\Tools\Sysinternals\PsExec.exe",
                ProviderName = "Sysinternals"
            }
        ]);

        var detected = registry.GetById("EXT:SYSINTERNALS:PSEXEC");
        var native = registry.GetById("WOL");

        Assert.NotNull(detected);
        Assert.Equal("Sysinternals", detected!.ExternalProviderName);
        Assert.NotNull(native);
        Assert.Null(native!.ExternalProviderName);
    }

    private static ToolDescriptor CreateExternalDescriptor(
        string id,
        string labelKey,
        string? externalProviderName)
        => new(
            id,
            ToolCategory.External,
            "ToolCategoryExternal",
            labelKey,
            null,
            [id.ToLowerInvariant()],
            false,
            ExternalProviderName: externalProviderName);
}
