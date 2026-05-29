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
using FluentAssertions;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;
using ActionModel = TwinShell.Core.Models.Action;

namespace TwinShell.Infrastructure.Tests;

public sealed class JsonSyncServiceExportHygieneTests
{
    [Fact]
    public async Task Export_TwoEntitiesWithSameName_ProduceDistinctFiles()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            CustomCategory first = CreateCategory(
                "duplicate-category-1",
                Guid.Parse("66666666-6666-4666-8666-666666666666"),
                "Duplicate",
                displayOrder: 1);
            CustomCategory second = CreateCategory(
                "duplicate-category-2",
                Guid.Parse("77777777-7777-4777-8777-777777777777"),
                "Duplicate",
                displayOrder: 2);
            await fixture.Categories.CreateAsync(first);
            await fixture.Categories.CreateAsync(second);

            SyncExportResult result = await fixture.SyncService.ExportDataToYamlAsync(exportPath);

            result.Success.Should().BeTrue();
            result.CategoriesExported.Should().Be(2);

            string[] files = Directory.GetFiles(Path.Combine(exportPath, "categories"), "*.json");
            files.Should().HaveCount(2);
            files.Select(Path.GetFileName).Should().OnlyHaveUniqueItems();
            files.Select(ReadPublicIdFromJsonFile).Should().BeEquivalentTo([first.PublicId, second.PublicId]);
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Export_Filename_IsDeterministicAcrossExports()
    {
        string firstExportPath = CreateTempDirectory();
        string secondExportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            SeededSyncData seed = await fixture.SeedAsync();

            SyncExportResult firstResult = await fixture.SyncService.ExportDataToYamlAsync(firstExportPath);
            SyncExportResult secondResult = await fixture.SyncService.ExportDataToYamlAsync(secondExportPath);

            firstResult.Success.Should().BeTrue();
            secondResult.Success.Should().BeTrue();

            string firstFileName = FindFileNameContainingPublicId(
                Path.Combine(firstExportPath, "categories"),
                seed.Category.PublicId);
            string secondFileName = FindFileNameContainingPublicId(
                Path.Combine(secondExportPath, "categories"),
                seed.Category.PublicId);

            firstFileName.Should().Be(secondFileName);
        }
        finally
        {
            DeleteDirectory(firstExportPath);
            DeleteDirectory(secondExportPath);
        }
    }

    [Fact]
    public async Task Export_LeavesNoTempFiles()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            await fixture.SeedAsync();

            SyncExportResult result = await fixture.SyncService.ExportDataToYamlAsync(exportPath);

            result.Success.Should().BeTrue();
            Directory.GetFiles(exportPath, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Export_EntityWithUnsanitizableName_UsesPublicIdFilename()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            CustomCategory category = CreateCategory(
                "category-unsanitizable",
                Guid.Parse("88888888-8888-4888-8888-888888888888"),
                "///",
                displayOrder: 1);
            await fixture.Categories.CreateAsync(category);

            SyncExportResult result = await fixture.SyncService.ExportDataToYamlAsync(exportPath);

            result.Success.Should().BeTrue();
            File.Exists(Path.Combine(
                exportPath,
                "categories",
                category.PublicId.ToString("N") + ".json")).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Roundtrip_WithNewNamingScheme_StillImportsByPublicId()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture exportFixture = await TwinShellSyncFixture.CreateAsync();
            SeededSyncData seed = await exportFixture.SeedAsync();
            SyncExportResult exportResult = await exportFixture.SyncService.ExportDataToYamlAsync(exportPath);
            exportResult.Success.Should().BeTrue();

            await using TwinShellSyncFixture importFixture = await TwinShellSyncFixture.CreateAsync();
            SyncImportResult importResult = await importFixture.SyncService.ImportDataFromYamlAsync(exportPath);

            importResult.Success.Should().BeTrue();
            IEnumerable<CustomCategory> categories = await importFixture.Categories.GetAllAsync();
            IEnumerable<CommandTemplate> templates = await importFixture.Templates.GetAllAsync();
            IEnumerable<ActionModel> actions = await importFixture.Actions.GetAllAsync();
            IEnumerable<CommandBatch> batches = await importFixture.Batches.GetAllAsync();

            categories.Select(category => category.PublicId).Should().Contain(seed.Category.PublicId);
            templates.Select(template => template.PublicId).Should().Contain(seed.Template.PublicId);
            actions.Select(action => action.PublicId).Should().BeEquivalentTo(seed.Actions.Select(action => action.PublicId));
            batches.Select(batch => batch.PublicId).Should().Contain(seed.Batch.PublicId);
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    private static CustomCategory CreateCategory(
        string id,
        Guid publicId,
        string name,
        int displayOrder)
    {
        return new CustomCategory
        {
            Id = id,
            PublicId = publicId,
            Name = name,
            IconKey = "folder",
            ColorHex = "#336699",
            IsSystemCategory = false,
            DisplayOrder = displayOrder,
            IsHidden = false,
            Description = "Export hygiene test category",
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
    }

    private static Guid ReadPublicIdFromJsonFile(string path)
    {
        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static string FindFileNameContainingPublicId(string folderPath, Guid publicId)
    {
        string publicIdText = publicId.ToString("N");
        string[] files = Directory.GetFiles(folderPath, "*.json");
        string? match = files
            .Select(Path.GetFileName)
            .SingleOrDefault(fileName => fileName != null && fileName.Contains(publicIdText, StringComparison.Ordinal));

        match.Should().NotBeNull();
        return match!;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "heimdall_twinshell_export_hygiene_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
