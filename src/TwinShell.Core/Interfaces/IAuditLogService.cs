using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for managing audit logs
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Add a new audit log entry
    /// </summary>
    Task AddLogAsync(AuditLog log);

    /// <summary>
    /// Get recent audit logs
    /// </summary>
    /// <param name="count">Number of logs to retrieve</param>
    Task<IEnumerable<AuditLog>> GetRecentAsync(int count = 100);

    /// <summary>
    /// Get audit logs for a specific date range
    /// </summary>
    Task<IEnumerable<AuditLog>> GetByDateRangeAsync(DateTime from, DateTime to);

    /// <summary>
    /// Export audit logs to CSV format
    /// </summary>
    /// <param name="filePath">Path to save the CSV file</param>
    /// <param name="from">Start date (optional)</param>
    /// <param name="to">End date (optional)</param>
    Task ExportToCsvAsync(string filePath, DateTime? from = null, DateTime? to = null);

    /// <summary>
    /// Get total count of audit logs
    /// </summary>
    Task<int> GetCountAsync();

    /// <summary>
    /// Clean up old audit logs (older than retention days)
    /// </summary>
    Task CleanupOldLogsAsync(int retentionDays = 365);
}
