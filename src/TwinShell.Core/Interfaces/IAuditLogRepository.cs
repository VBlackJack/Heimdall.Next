using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Repository interface for AuditLog persistence
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Add a new audit log entry
    /// </summary>
    Task AddAsync(AuditLog log);

    /// <summary>
    /// Get recent audit logs
    /// </summary>
    Task<IEnumerable<AuditLog>> GetRecentAsync(int count = 100);

    /// <summary>
    /// Get audit logs by date range
    /// </summary>
    Task<IEnumerable<AuditLog>> GetByDateRangeAsync(DateTime from, DateTime to);

    /// <summary>
    /// Get total count of audit logs
    /// </summary>
    Task<int> GetCountAsync();

    /// <summary>
    /// Delete audit logs older than specified date
    /// </summary>
    Task DeleteOlderThanAsync(DateTime date);
}
