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
using FluentAssertions;
using Heimdall.App.Services.Handlers;

namespace Heimdall.App.Tests.Services.Handlers;

public sealed class CitrixHandlerLauncherTests
{
    [Fact]
    public void BuildCitrixLauncherCandidates_ReturnsEightCandidates()
    {
        IReadOnlyList<string> candidates = CitrixHandler.BuildCitrixLauncherCandidates(
            @"C:\PFX86",
            @"C:\PF");

        candidates.Should().HaveCount(8);
    }

    [Fact]
    public void BuildCitrixLauncherCandidates_OrdersModernLayoutsBeforeLegacyLayouts()
    {
        IReadOnlyList<string> candidates = CitrixHandler.BuildCitrixLauncherCandidates(
            @"C:\PFX86",
            @"C:\PF");
        List<string> candidateList = candidates.ToList();

        int firstAuthManagerIndex = candidateList.FindIndex(path =>
            path.Contains(Path.Combine("AuthManager", "storebrowse.exe"), StringComparison.Ordinal));
        int firstLegacyStorebrowseIndex = candidateList.FindIndex(path =>
            path.EndsWith(Path.Combine("ICA Client", "storebrowse.exe"), StringComparison.Ordinal));
        int lastSelfServicePluginIndex = candidateList.FindLastIndex(path =>
            path.Contains(Path.Combine("SelfServicePlugin", "SelfService.exe"), StringComparison.Ordinal));
        int firstLegacySelfServiceIndex = candidateList.FindIndex(path =>
            path.EndsWith(Path.Combine("ICA Client", "SelfService.exe"), StringComparison.Ordinal));

        firstAuthManagerIndex.Should().BeLessThan(firstLegacyStorebrowseIndex);
        lastSelfServicePluginIndex.Should().BeLessThan(firstLegacySelfServiceIndex);
    }

    [Fact]
    public void BuildCitrixLauncherCandidates_ReturnsExpectedDistinctPathsForDistinctRoots()
    {
        string programFilesX86 = @"C:\PFX86";
        string programFiles = @"C:\PF";
        string[] expected =
        [
            Path.Combine(programFilesX86, "Citrix", "ICA Client", "AuthManager", "storebrowse.exe"),
            Path.Combine(programFiles, "Citrix", "ICA Client", "AuthManager", "storebrowse.exe"),
            Path.Combine(programFilesX86, "Citrix", "ICA Client", "storebrowse.exe"),
            Path.Combine(programFiles, "Citrix", "ICA Client", "storebrowse.exe"),
            Path.Combine(programFilesX86, "Citrix", "ICA Client", "SelfServicePlugin", "SelfService.exe"),
            Path.Combine(programFiles, "Citrix", "ICA Client", "SelfServicePlugin", "SelfService.exe"),
            Path.Combine(programFilesX86, "Citrix", "ICA Client", "SelfService.exe"),
            Path.Combine(programFiles, "Citrix", "ICA Client", "SelfService.exe"),
        ];

        IReadOnlyList<string> candidates = CitrixHandler.BuildCitrixLauncherCandidates(
            programFilesX86,
            programFiles);

        candidates.Should().Equal(expected);
        candidates.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildCitrixLauncherCandidates_AllowsEmptyRoots()
    {
        IReadOnlyList<string> candidates = CitrixHandler.BuildCitrixLauncherCandidates(
            string.Empty,
            string.Empty);

        candidates.Should().HaveCount(8);
        candidates.Should().OnlyContain(path => !Path.IsPathRooted(path));
    }
}
