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
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Ssh;

namespace Heimdall.App.Tests;

public sealed class AppImporterOriginTests
{
    [Fact]
    public async Task OpenSshConfigImporter_ImportSelectedAsync_SetsOriginToImportOpenSsh()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var importer = new OpenSshConfigImporter(fixture.ConfigManager);

        await importer.ImportSelectedAsync(
        [
            new OpenSshImportCandidate
            {
                Alias = "prod",
                HostName = "prod.example.com",
                Port = 2222,
                User = "alice"
            }
        ]);

        var server = Assert.Single(await fixture.ConfigManager.LoadServersAsync());
        Assert.Equal(ProfileOrigin.ImportOpenSsh, server.Origin);
    }

    [Fact]
    public async Task PuttySessionImporter_ImportSelectedAsync_SetsOriginToImportPutty()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var importer = new PuttySessionImporter(new FakePuttySessionRegistrySource([]), fixture.ConfigManager);

        await importer.ImportSelectedAsync(
        [
            new PuttySessionCandidate
            {
                DisplayName = "Prod SSH",
                HostName = "prod.example.com",
                Port = 2022,
                UserName = "alice",
                PublicKeyFile = @"C:\keys\admin.ppk"
            }
        ]);

        var server = Assert.Single(await fixture.ConfigManager.LoadServersAsync());
        Assert.Equal(ProfileOrigin.ImportPutty, server.Origin);
    }

    [Fact]
    public async Task RdpImportService_ApplyAsync_SetsOriginToImportRdp()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var service = new RdpImportService(fixture.ConfigManager, fixture.Localizer);
        var path = Path.Combine(fixture.RootPath, "ImportMe.rdp");
        await File.WriteAllTextAsync(path, "full address:s:rdp.example.com");

        var preview = await service.PreviewAsync([path], CancellationToken.None);
        await service.ApplyAsync(
            preview,
            new RdpImportSelection
            {
                Entries =
                [
                    new RdpImportSelectionEntry
                    {
                        SourceFilePath = path,
                        IsSelected = true,
                        ConflictResolution = RdpConflictResolution.AutoRename
                    }
                ]
            },
            CancellationToken.None);

        var server = Assert.Single(await fixture.ConfigManager.LoadServersAsync());
        Assert.Equal(ProfileOrigin.ImportRdp, server.Origin);
    }

    private sealed class ImportFixture : IAsyncDisposable
    {
        private ImportFixture(string rootPath, ConfigManager configManager, LocalizationManager localizer)
        {
            RootPath = rootPath;
            ConfigManager = configManager;
            Localizer = localizer;
        }

        public string RootPath { get; }

        public ConfigManager ConfigManager { get; }

        public LocalizationManager Localizer { get; }

        public static async Task<ImportFixture> CreateAsync()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "heimdall-b63-importers", Guid.NewGuid().ToString("N"));
            var configManager = new ConfigManager(rootPath);
            await configManager.InitializeAsync();
            var localizer = new LocalizationManager();
            await localizer.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en");
            return new ImportFixture(rootPath, configManager, localizer);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch (DirectoryNotFoundException)
            {
            }

            return ValueTask.CompletedTask;
        }
    }
}
