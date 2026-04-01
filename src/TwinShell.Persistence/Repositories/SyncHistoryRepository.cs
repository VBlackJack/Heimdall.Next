/*
 * Copyright 2025 Julien Bombled
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

using Microsoft.EntityFrameworkCore;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Repositories;

/// <summary>
/// Repository implementation for Git sync history
/// </summary>
public class SyncHistoryRepository : ISyncHistoryRepository
{
    private readonly TwinShellDbContext _dbContext;

    public SyncHistoryRepository(TwinShellDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(SyncHistoryEntry entry)
    {
        var entity = ToEntity(entry);
        _dbContext.SyncHistories.Add(entity);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<IEnumerable<SyncHistoryEntry>> GetRecentAsync(int count = 50)
    {
        var entities = await _dbContext.SyncHistories
            .OrderByDescending(e => e.StartedAt)
            .Take(count)
            .ToListAsync();

        return entities.Select(ToModel);
    }

    public async Task<IEnumerable<SyncHistoryEntry>> GetByOperationTypeAsync(string operationType, int count = 50)
    {
        var entities = await _dbContext.SyncHistories
            .Where(e => e.OperationType == operationType)
            .OrderByDescending(e => e.StartedAt)
            .Take(count)
            .ToListAsync();

        return entities.Select(ToModel);
    }

    public async Task<IEnumerable<SyncHistoryEntry>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        var entities = await _dbContext.SyncHistories
            .Where(e => e.StartedAt >= startDate && e.StartedAt <= endDate)
            .OrderByDescending(e => e.StartedAt)
            .ToListAsync();

        return entities.Select(ToModel);
    }

    public async Task<SyncHistoryEntry?> GetLastSuccessfulAsync()
    {
        var entity = await _dbContext.SyncHistories
            .Where(e => e.Success)
            .OrderByDescending(e => e.StartedAt)
            .FirstOrDefaultAsync();

        return entity != null ? ToModel(entity) : null;
    }

    public async Task<int> DeleteOldEntriesAsync(DateTime olderThan)
    {
        // PERFORMANCE FIX (TD-001): Use ExecuteDeleteAsync to delete in database without loading entities
        // This prevents OOM issues with large sync history tables and is 10-100x faster
        return await _dbContext.SyncHistories
            .Where(e => e.StartedAt < olderThan)
            .ExecuteDeleteAsync();
    }

    public async Task<int> GetCountAsync()
    {
        return await _dbContext.SyncHistories.CountAsync();
    }

    private static SyncHistoryEntity ToEntity(SyncHistoryEntry model)
    {
        return new SyncHistoryEntity
        {
            Id = model.Id,
            OperationType = model.OperationType,
            Success = model.Success,
            ErrorCode = model.ErrorCode,
            Message = model.Message,
            ErrorDetails = model.ErrorDetails,
            ItemsCreated = model.ItemsCreated,
            ItemsUpdated = model.ItemsUpdated,
            ItemsExported = model.ItemsExported,
            ItemsSkipped = model.ItemsSkipped,
            ConflictsDetected = model.ConflictsDetected,
            CommitsMerged = model.CommitsMerged,
            DurationMs = (long)model.Duration.TotalMilliseconds,
            RemoteUrl = model.RemoteUrl,
            Branch = model.Branch,
            StartedAt = model.StartedAt,
            CompletedAt = model.CompletedAt
        };
    }

    private static SyncHistoryEntry ToModel(SyncHistoryEntity entity)
    {
        return new SyncHistoryEntry
        {
            Id = entity.Id,
            OperationType = entity.OperationType,
            Success = entity.Success,
            ErrorCode = entity.ErrorCode,
            Message = entity.Message,
            ErrorDetails = entity.ErrorDetails,
            ItemsCreated = entity.ItemsCreated,
            ItemsUpdated = entity.ItemsUpdated,
            ItemsExported = entity.ItemsExported,
            ItemsSkipped = entity.ItemsSkipped,
            ConflictsDetected = entity.ConflictsDetected,
            CommitsMerged = entity.CommitsMerged,
            Duration = TimeSpan.FromMilliseconds(entity.DurationMs),
            RemoteUrl = entity.RemoteUrl,
            Branch = entity.Branch,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt
        };
    }
}
