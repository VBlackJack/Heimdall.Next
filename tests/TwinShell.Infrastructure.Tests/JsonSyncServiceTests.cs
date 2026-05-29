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

using FluentAssertions;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;
using ActionModel = TwinShell.Core.Models.Action;

namespace TwinShell.Infrastructure.Tests;

public sealed class JsonSyncServiceTests
{
    [Fact]
    public async Task Export_WritesExpectedFolderStructureAndFiles()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            SeededSyncData seed = await fixture.SeedAsync();

            SyncExportResult result = await fixture.SyncService.ExportDataToYamlAsync(exportPath);

            result.Success.Should().BeTrue();
            result.Errors.Should().BeEmpty();
            result.CategoriesExported.Should().Be(1);
            result.TemplatesExported.Should().Be(1);
            result.ActionsExported.Should().Be(2);
            result.BatchesExported.Should().Be(1);
            result.TotalExported.Should().Be(5);

            Directory.Exists(Path.Combine(exportPath, "actions")).Should().BeTrue();
            Directory.Exists(Path.Combine(exportPath, "batches")).Should().BeTrue();
            Directory.Exists(Path.Combine(exportPath, "templates")).Should().BeTrue();
            Directory.Exists(Path.Combine(exportPath, "categories")).Should().BeTrue();

            File.Exists(Path.Combine(
                exportPath,
                "categories",
                BuildExpectedEntityFileName(seed.Category.Name, seed.Category.PublicId))).Should().BeTrue();
            File.Exists(Path.Combine(
                exportPath,
                "templates",
                BuildExpectedEntityFileName(seed.Template.Name, seed.Template.PublicId))).Should().BeTrue();
            File.Exists(Path.Combine(
                exportPath,
                "batches",
                BuildExpectedEntityFileName(seed.Batch.Name, seed.Batch.PublicId))).Should().BeTrue();

            string actionCategoryPath = Path.Combine(exportPath, "actions", seed.Category.Name);
            File.Exists(Path.Combine(
                actionCategoryPath,
                BuildExpectedEntityFileName(seed.Actions[0].Title, seed.Actions[0].PublicId))).Should().BeTrue();
            File.Exists(Path.Combine(
                actionCategoryPath,
                BuildExpectedEntityFileName(seed.Actions[1].Title, seed.Actions[1].PublicId))).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Import_CreatesEntities_FromExportedFolder()
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
            importResult.Errors.Should().BeEmpty();
            importResult.CategoriesCreated.Should().Be(1);
            importResult.TemplatesCreated.Should().Be(1);
            importResult.ActionsCreated.Should().Be(seed.Actions.Count);
            importResult.BatchesCreated.Should().Be(1);

            IEnumerable<CustomCategory> categories = await importFixture.Categories.GetAllAsync();
            IEnumerable<CommandTemplate> templates = await importFixture.Templates.GetAllAsync();
            IEnumerable<ActionModel> actions = await importFixture.Actions.GetAllAsync();
            IEnumerable<CommandBatch> batches = await importFixture.Batches.GetAllAsync();

            categories.Should().ContainSingle(category => category.PublicId == seed.Category.PublicId);
            templates.Should().ContainSingle(template => template.PublicId == seed.Template.PublicId);
            actions.Should().HaveCount(seed.Actions.Count);
            batches.Should().ContainSingle(batch => batch.PublicId == seed.Batch.PublicId);
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Import_Roundtrip_PreservesPublicIds()
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
            IEnumerable<Guid> expectedActionPublicIds = seed.Actions.Select(action => action.PublicId);

            categories.Select(category => category.PublicId).Should().Contain(seed.Category.PublicId);
            templates.Select(template => template.PublicId).Should().Contain(seed.Template.PublicId);
            actions.Select(action => action.PublicId).Should().BeEquivalentTo(expectedActionPublicIds);
            batches.Select(batch => batch.PublicId).Should().Contain(seed.Batch.PublicId);
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Import_SkipsLocalNewer_AsConflict()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture exportFixture = await TwinShellSyncFixture.CreateAsync();
            SeededSyncData seed = await exportFixture.SeedAsync();
            SyncExportResult exportResult = await exportFixture.SyncService.ExportDataToYamlAsync(exportPath);
            exportResult.Success.Should().BeTrue();

            await using TwinShellSyncFixture importFixture = await TwinShellSyncFixture.CreateAsync();
            SyncImportResult firstImportResult = await importFixture.SyncService.ImportDataFromYamlAsync(exportPath);
            firstImportResult.Success.Should().BeTrue();

            ActionModel? localAction = await importFixture.Actions.GetByPublicIdAsync(seed.Actions[0].PublicId);
            localAction.Should().NotBeNull();
            localAction!.UpdatedAt = seed.Actions[0].UpdatedAt.AddDays(1);
            await importFixture.Actions.UpdateAsync(localAction);

            SyncImportResult secondImportResult = await importFixture.SyncService.ImportDataFromYamlAsync(exportPath);

            secondImportResult.Success.Should().BeTrue();
            secondImportResult.ActionsSkipped.Should().Be(1);
            secondImportResult.Conflicts.Should().ContainSingle(conflict =>
                conflict.EntityType == "Action"
                && conflict.EntityId == seed.Actions[0].PublicId
                && conflict.Resolution == SyncConflictResolution.KeepLocal);
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task ValidateFolder_ReportsFileCounts()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            await fixture.SeedAsync();
            SyncExportResult exportResult = await fixture.SyncService.ExportDataToYamlAsync(exportPath);
            exportResult.Success.Should().BeTrue();

            SyncValidationResult result = await fixture.SyncService.ValidateFolderAsync(exportPath);

            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
            result.CategoryFilesFound.Should().Be(1);
            result.TemplateFilesFound.Should().Be(1);
            result.ActionFilesFound.Should().Be(2);
            result.BatchFilesFound.Should().Be(1);
            result.TotalFilesFound.Should().Be(5);
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "heimdall_twinshell_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string BuildExpectedEntityFileName(string name, Guid publicId)
        => $"{name}-{publicId:N}.json";

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
