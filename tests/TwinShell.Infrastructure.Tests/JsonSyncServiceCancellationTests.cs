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
using TwinShell.Infrastructure.Services;
using TwinShell.Persistence;
using ActionModel = TwinShell.Core.Models.Action;

namespace TwinShell.Infrastructure.Tests;

public sealed class JsonSyncServiceCancellationTests
{
    [Fact]
    public async Task Import_WithDefaultToken_StillImportsEverything()
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
            importResult.CategoriesCreated.Should().Be(1);
            importResult.TemplatesCreated.Should().Be(1);
            importResult.ActionsCreated.Should().Be(seed.Actions.Count);
            importResult.BatchesCreated.Should().Be(1);
            importResult.TotalCreated.Should().Be(5);
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Import_WithPreCancelledToken_ThrowsOperationCanceled_AndPersistsNothing()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture exportFixture = await TwinShellSyncFixture.CreateAsync();
            await exportFixture.SeedAsync();
            SyncExportResult exportResult = await exportFixture.SyncService.ExportDataToYamlAsync(exportPath);
            exportResult.Success.Should().BeTrue();

            await using TwinShellSyncFixture importFixture = await TwinShellSyncFixture.CreateAsync();
            CancellationToken cancellationToken = new(canceled: true);
            Func<Task> act = () => importFixture.SyncService.ImportDataFromYamlAsync(exportPath, cancellationToken);

            await act.Should().ThrowAsync<OperationCanceledException>();
            await AssertDatabaseEmptyAsync(importFixture);
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Import_CancelledAfterFirstEntity_RollsBackEverything()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture exportFixture = await TwinShellSyncFixture.CreateAsync();
            await exportFixture.SeedAsync();
            SyncExportResult exportResult = await exportFixture.SyncService.ExportDataToYamlAsync(exportPath);
            exportResult.Success.Should().BeTrue();

            await using TwinShellSyncFixture importFixture = await TwinShellSyncFixture.CreateAsync();
            using CancellationTokenSource cancellationTokenSource = new();
            IActionRepository cancellingActions = new CancelAfterFirstAddActionRepository(
                importFixture.Actions,
                cancellationTokenSource);
            using UnitOfWork unitOfWork = new(importFixture.DbContext);
            JsonSyncService syncService = new(
                cancellingActions,
                importFixture.Batches,
                importFixture.Categories,
                importFixture.Templates,
                unitOfWork);
            Func<Task> act = () => syncService.ImportDataFromYamlAsync(exportPath, cancellationTokenSource.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            await AssertDatabaseEmptyAsync(importFixture);
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    [Fact]
    public async Task Export_WithPreCancelledToken_ThrowsOperationCanceled()
    {
        string exportPath = CreateTempDirectory();

        try
        {
            await using TwinShellSyncFixture fixture = await TwinShellSyncFixture.CreateAsync();
            await fixture.SeedAsync();
            CancellationToken cancellationToken = new(canceled: true);
            Func<Task> act = () => fixture.SyncService.ExportDataToYamlAsync(exportPath, cancellationToken);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            DeleteDirectory(exportPath);
        }
    }

    private static async Task AssertDatabaseEmptyAsync(TwinShellSyncFixture fixture)
    {
        IEnumerable<ActionModel> actions = await fixture.Actions.GetAllAsync();
        IEnumerable<CustomCategory> categories = await fixture.Categories.GetAllAsync();
        IEnumerable<CommandTemplate> templates = await fixture.Templates.GetAllAsync();
        IEnumerable<CommandBatch> batches = await fixture.Batches.GetAllAsync();

        actions.Should().BeEmpty();
        categories.Should().BeEmpty();
        templates.Should().BeEmpty();
        batches.Should().BeEmpty();
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "heimdall_twinshell_cancel_" + Guid.NewGuid().ToString("N"));
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

    private sealed class CancelAfterFirstAddActionRepository : IActionRepository
    {
        private readonly IActionRepository _inner;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private int _addCount;

        internal CancelAfterFirstAddActionRepository(
            IActionRepository inner,
            CancellationTokenSource cancellationTokenSource)
        {
            _inner = inner;
            _cancellationTokenSource = cancellationTokenSource;
        }

        public Task<IEnumerable<ActionModel>> GetAllAsync()
            => _inner.GetAllAsync();

        public Task<ActionModel?> GetByIdAsync(string id)
            => _inner.GetByIdAsync(id);

        public Task<IEnumerable<ActionModel>> GetByCategoryAsync(string category)
            => _inner.GetByCategoryAsync(category);

        public Task<IEnumerable<string>> GetAllCategoriesAsync()
            => _inner.GetAllCategoriesAsync();

        public async Task AddAsync(ActionModel action)
        {
            await _inner.AddAsync(action);
            int addCount = Interlocked.Increment(ref _addCount);
            if (addCount == 1)
            {
                _cancellationTokenSource.Cancel();
            }
        }

        public Task UpdateAsync(ActionModel action)
            => _inner.UpdateAsync(action);

        public Task DeleteAsync(string id)
            => _inner.DeleteAsync(id);

        public Task<bool> ExistsAsync(string id)
            => _inner.ExistsAsync(id);

        public Task<int> CountAsync()
            => _inner.CountAsync();

        public Task<int> CountByCategoryAsync(string category)
            => _inner.CountByCategoryAsync(category);

        public Task<int> UpdateCategoryForActionsAsync(string oldCategory, string? newCategory)
            => _inner.UpdateCategoryForActionsAsync(oldCategory, newCategory);

        public Task<ActionModel?> GetByPublicIdAsync(Guid publicId)
            => _inner.GetByPublicIdAsync(publicId);

        public Task<IEnumerable<ActionModel>> GetAllWithTemplatesAsync()
            => _inner.GetAllWithTemplatesAsync();

        public Task AddRangeAsync(IEnumerable<ActionModel> actions)
            => _inner.AddRangeAsync(actions);

        public Task UpdateRangeAsync(IEnumerable<ActionModel> actions)
            => _inner.UpdateRangeAsync(actions);
    }
}
