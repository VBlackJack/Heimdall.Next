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
using Heimdall.App.Services.Import;
using Heimdall.Core.Configuration;
using Heimdall.Core.Import;
using Heimdall.Core.Ssh;

namespace Heimdall.App.Tests;

public sealed class OpenSshConfigImporterTests
{
    [Fact]
    public async Task ComputeStatuses_AllNew_WhenNoExistingMatch()
    {
        using var fixture = new OpenSshImportFixture();
        var assessments = await fixture.Importer.ComputeStatusesAsync(
        [
            new OpenSshImportCandidate { Alias = "prod", HostName = "prod.example.com", SourceLineNumber = 1 },
            new OpenSshImportCandidate { Alias = "logs", HostName = "logs.example.com", SourceLineNumber = 2 }
        ]);

        Assert.All(assessments, assessment => Assert.Equal(ImportCandidateStatus.New, assessment.Status));
    }

    [Fact]
    public async Task ComputeStatuses_DuplicateDetectedByAliasCaseInsensitive()
    {
        using var fixture = new OpenSshImportFixture();
        await fixture.ConfigManager.SaveServersAsync(
        [
            new ServerProfileDto
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = "PROD1",
                ConnectionType = "SSH",
                RemoteServer = "existing.example.com"
            }
        ]);

        var assessments = await fixture.Importer.ComputeStatusesAsync(
        [
            new OpenSshImportCandidate { Alias = "prod1", HostName = "new.example.com", SourceLineNumber = 1 }
        ]);

        var assessment = Assert.Single(assessments);
        Assert.Equal(ImportCandidateStatus.Duplicate, assessment.Status);
    }

    [Fact]
    public async Task ImportSelectedAsync_AddsNewProfiles_AndSkipsDuplicates_ReturnsOutcome()
    {
        using var fixture = new OpenSshImportFixture();
        await fixture.ConfigManager.SaveServersAsync(
        [
            new ServerProfileDto
            {
                Id = "existing",
                DisplayName = "prod1",
                ConnectionType = "SSH",
                RemoteServer = "existing.example.com"
            }
        ]);

        var missingKeyPath = Path.Combine(fixture.RootPath, "keys", "missing");
        var outcome = await fixture.Importer.ImportSelectedAsync(
        [
            new OpenSshImportCandidate
            {
                Alias = "prod1",
                HostName = "duplicate.example.com",
                SourceLineNumber = 1
            },
            new OpenSshImportCandidate
            {
                Alias = "logs",
                HostName = "logs.example.com",
                Port = 2222,
                User = "alice",
                IdentityFile = missingKeyPath,
                SourceLineNumber = 2
            }
        ]);

        var servers = await fixture.ConfigManager.LoadServersAsync();
        Assert.Equal(2, servers.Count);
        var imported = Assert.Single(servers, server => server.DisplayName == "logs");
        Assert.Equal("SSH", imported.ConnectionType);
        Assert.Equal("Embedded", imported.SshMode);
        Assert.Equal(2222, imported.SshPort);
        Assert.Equal("alice", imported.SshUsername);
        Assert.Equal(missingKeyPath, imported.SshKeyPath);
        Assert.Equal(1, outcome.ImportedCount);
        Assert.Equal(1, outcome.SkippedDuplicates);
        Assert.Equal(1, outcome.WarningCount);
    }

    private sealed class OpenSshImportFixture : IDisposable
    {
        public OpenSshImportFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "heimdall-b60-tests", Guid.NewGuid().ToString("N"));
            ConfigManager = new ConfigManager(RootPath);
            ConfigManager.InitializeAsync().GetAwaiter().GetResult();
            Importer = new OpenSshConfigImporter(ConfigManager);
        }

        public string RootPath { get; }

        public ConfigManager ConfigManager { get; }

        public OpenSshConfigImporter Importer { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
