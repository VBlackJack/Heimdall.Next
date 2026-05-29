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
using TwinShell.Core.Enums;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;
using ActionModel = TwinShell.Core.Models.Action;

namespace TwinShell.Infrastructure.Tests;

public sealed class JsonSyncServiceOrphanCleanupTests
{
    [Fact]
    public async Task Export_AfterEntityDeleted_RemovesStaleFile()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            CustomCategory first = CreateCategory(
                "category-delete-survivor",
                Guid.Parse("99999999-9999-4999-8999-999999999999"),
                "Survivor",
                displayOrder: 1);
            CustomCategory second = CreateCategory(
                "category-delete-stale",
                Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
                "Deleted",
                displayOrder: 2);
            await fixture.Categories.CreateAsync(first);
            await fixture.Categories.CreateAsync(second);

            SyncExportResult firstExport = await fixture.SyncService.ExportDataToYamlAsync(exportPath);
            firstExport.Success.Should().BeTrue();

            string survivorFile = Path.Combine(
                exportPath,
                "categories",
                BuildExpectedEntityFileName(first.Name, first.PublicId));
            string staleFile = Path.Combine(
                exportPath,
                "categories",
                BuildExpectedEntityFileName(second.Name, second.PublicId));
            File.Exists(survivorFile).Should().BeTrue();
            File.Exists(staleFile).Should().BeTrue();

            await fixture.Categories.DeleteAsync(second.Id);
            SyncExportResult secondExport = await fixture.SyncService.ExportDataToYamlAsync(exportPath);

            secondExport.Success.Should().BeTrue();
            File.Exists(staleFile).Should().BeFalse();
            File.Exists(survivorFile).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Export_AfterRename_LeavesSingleFile()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            ActionModel action = CreateAction(
                "action-rename",
                Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
                "Original Action",
                "Actions");
            await fixture.Actions.AddAsync(action);

            SyncExportResult firstExport = await fixture.SyncService.ExportDataToYamlAsync(exportPath);
            firstExport.Success.Should().BeTrue();

            string actionsRoot = Path.Combine(exportPath, "actions");
            string oldFile = Path.Combine(
                actionsRoot,
                "Actions",
                BuildExpectedEntityFileName(action.Title, action.PublicId));
            File.Exists(oldFile).Should().BeTrue();

            ActionModel? storedAction = await fixture.Actions.GetByPublicIdAsync(action.PublicId);
            storedAction.Should().NotBeNull();
            storedAction!.Title = "Renamed Action";
            storedAction.UpdatedAt = storedAction.UpdatedAt.AddMinutes(1);
            await fixture.Actions.UpdateAsync(storedAction);

            SyncExportResult secondExport = await fixture.SyncService.ExportDataToYamlAsync(exportPath);

            secondExport.Success.Should().BeTrue();
            string[] actionFiles = Directory.GetFiles(actionsRoot, "*.json", SearchOption.AllDirectories);
            actionFiles.Should().ContainSingle();
            File.Exists(oldFile).Should().BeFalse();
            ReadPublicIdFromJsonFile(actionFiles.Single()).Should().Be(action.PublicId);
            Path.GetFileName(actionFiles.Single()).Should().Be(BuildExpectedEntityFileName("Renamed Action", action.PublicId));
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Export_PreservesUnderscorePrefixedFiles()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            string categoriesPath = Path.Combine(exportPath, "categories");
            Directory.CreateDirectory(categoriesPath);
            string metadataPath = Path.Combine(categoriesPath, "_meta.json");
            await File.WriteAllTextAsync(metadataPath, "{}");

            SyncExportResult result = await fixture.SyncService.ExportDataToYamlAsync(exportPath);

            result.Success.Should().BeTrue();
            File.Exists(metadataPath).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Export_PreservesNonJsonFiles()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            string templatesPath = Path.Combine(exportPath, "templates");
            Directory.CreateDirectory(templatesPath);
            string readmePath = Path.Combine(templatesPath, "README.md");
            await File.WriteAllTextAsync(readmePath, "Local notes");

            SyncExportResult result = await fixture.SyncService.ExportDataToYamlAsync(exportPath);

            result.Success.Should().BeTrue();
            File.Exists(readmePath).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Export_AfterCategoryChange_RemovesEmptyCategoryFolder()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            ActionModel action = CreateAction(
                "action-category-change",
                Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
                "Move Category Action",
                "A");
            await fixture.Actions.AddAsync(action);

            SyncExportResult firstExport = await fixture.SyncService.ExportDataToYamlAsync(exportPath);
            firstExport.Success.Should().BeTrue();

            string actionsRoot = Path.Combine(exportPath, "actions");
            string oldCategoryPath = Path.Combine(actionsRoot, "A");
            string newCategoryPath = Path.Combine(actionsRoot, "B");
            Directory.Exists(oldCategoryPath).Should().BeTrue();

            ActionModel? storedAction = await fixture.Actions.GetByPublicIdAsync(action.PublicId);
            storedAction.Should().NotBeNull();
            storedAction!.Category = "B";
            storedAction.UpdatedAt = storedAction.UpdatedAt.AddMinutes(1);
            await fixture.Actions.UpdateAsync(storedAction);

            SyncExportResult secondExport = await fixture.SyncService.ExportDataToYamlAsync(exportPath);

            secondExport.Success.Should().BeTrue();
            Directory.Exists(oldCategoryPath).Should().BeFalse();
            Directory.Exists(newCategoryPath).Should().BeTrue();
            Directory.GetFiles(newCategoryPath, "*.json").Should().ContainSingle(file =>
                ReadPublicIdFromJsonFile(file) == action.PublicId);
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Export_WithPreCancelledToken_DeletesNothing()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            string categoriesPath = Path.Combine(exportPath, "categories");
            Directory.CreateDirectory(categoriesPath);
            string stalePath = Path.Combine(categoriesPath, "stale.json");
            await File.WriteAllTextAsync(stalePath, "{}");
            CancellationToken cancellationToken = new(canceled: true);
            Func<Task> act = () => fixture.SyncService.ExportDataToYamlAsync(exportPath, cancellationToken);

            await act.Should().ThrowAsync<OperationCanceledException>();
            File.Exists(stalePath).Should().BeTrue();
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
            Description = "Orphan cleanup test category",
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
    }

    private static ActionModel CreateAction(
        string id,
        Guid publicId,
        string title,
        string category)
    {
        DateTime createdAt = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        return new ActionModel
        {
            Id = id,
            PublicId = publicId,
            Title = title,
            Description = "Orphan cleanup test action",
            Category = category,
            Platform = Platform.Windows,
            Level = CriticalityLevel.Info,
            Tags = ["orphan-cleanup"],
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddHours(1),
            IsUserCreated = true
        };
    }

    private static Guid ReadPublicIdFromJsonFile(string path)
    {
        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static string BuildExpectedEntityFileName(string name, Guid publicId)
        => $"{name}-{publicId:N}.json";

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "heimdall_twinshell_orphan_cleanup_" + Guid.NewGuid().ToString("N"));
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
