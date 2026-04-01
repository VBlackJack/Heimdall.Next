using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;
using TwinShell.Persistence.Mappers;

namespace TwinShell.Persistence.Repositories;

/// <summary>
/// Repository implementation for AuditLog persistence
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly TwinShellDbContext _context;
    private readonly ILogger<AuditLogRepository> _logger;

    public AuditLogRepository(TwinShellDbContext context, ILogger<AuditLogRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task AddAsync(AuditLog log)
    {
        try
        {
            var entity = AuditLogMapper.ToEntity(log);
            _context.AuditLogs.Add(entity);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error while adding audit log: {LogId}", log.Id);
            throw;
        }
    }

    public async Task<IEnumerable<AuditLog>> GetRecentAsync(int count = 100)
    {
        // PERFORMANCE: AsNoTracking for read-only queries
        var entities = await _context.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToListAsync();

        return entities.Select(AuditLogMapper.ToModel);
    }

    public async Task<IEnumerable<AuditLog>> GetByDateRangeAsync(DateTime from, DateTime to)
    {
        // PERFORMANCE: AsNoTracking for read-only queries
        var entities = await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.Timestamp >= from && a.Timestamp <= to)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();

        return entities.Select(AuditLogMapper.ToModel);
    }

    public async Task<int> GetCountAsync()
    {
        return await _context.AuditLogs.CountAsync();
    }

    public async Task DeleteOlderThanAsync(DateTime date)
    {
        // PERFORMANCE: Use ExecuteDeleteAsync for direct SQL DELETE
        // This avoids loading all entities into memory (prevents OOM with large datasets)
        await _context.AuditLogs
            .Where(a => a.Timestamp < date)
            .ExecuteDeleteAsync();
    }
}
