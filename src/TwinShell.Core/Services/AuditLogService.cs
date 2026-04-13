using System.Globalization;
using System.IO;
using System.Text;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;

namespace TwinShell.Core.Services;

/// <summary>
/// Service for managing audit logs
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repository;

    public AuditLogService(IAuditLogRepository repository)
    {
        _repository = repository;
    }

    public async Task AddLogAsync(AuditLog log)
    {
        await _repository.AddAsync(log).ConfigureAwait(false);
    }

    public async Task<IEnumerable<AuditLog>> GetRecentAsync(int count = 100)
    {
        return await _repository.GetRecentAsync(count).ConfigureAwait(false);
    }

    public async Task<IEnumerable<AuditLog>> GetByDateRangeAsync(DateTime from, DateTime to)
    {
        return await _repository.GetByDateRangeAsync(from, to).ConfigureAwait(false);
    }

    public async Task ExportToCsvAsync(string filePath, DateTime? from = null, DateTime? to = null)
    {
        // SECURITY: Validate file path to prevent path traversal attacks
        if (!IsPathSecure(filePath))
        {
            throw new ArgumentException("Invalid or insecure file path", nameof(filePath));
        }

        var logs = await _repository.GetByDateRangeAsync(
            from ?? DateTime.UtcNow.AddYears(-1),
            to ?? DateTime.UtcNow).ConfigureAwait(false);

        var csv = new StringBuilder();

        // Header
        csv.AppendLine("Timestamp,ActionTitle,Category,Command,Platform,ExitCode,Success,Duration,WasDangerous");

        // Data
        foreach (var log in logs)
        {
            csv.AppendLine($"{log.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                          $"\"{EscapeCsv(log.ActionTitle)}\"," +
                          $"\"{EscapeCsv(log.Category)}\"," +
                          $"\"{EscapeCsv(log.Command)}\"," +
                          $"{log.Platform}," +
                          $"{log.ExitCode}," +
                          $"{log.Success}," +
                          $"{log.Duration.TotalSeconds:F2}," +
                          $"{log.WasDangerous}");
        }

        await File.WriteAllTextAsync(filePath, csv.ToString()).ConfigureAwait(false);
    }

    public async Task<int> GetCountAsync()
    {
        return await _repository.GetCountAsync().ConfigureAwait(false);
    }

    public async Task CleanupOldLogsAsync(int retentionDays = 365)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        await _repository.DeleteOlderThanAsync(cutoffDate).ConfigureAwait(false);
    }

    private string EscapeCsv(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    /// <summary>
    /// Validates that the file path is secure and doesn't allow path traversal
    /// </summary>
    private static bool IsPathSecure(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            // Get the full canonical path
            var fullPath = Path.GetFullPath(filePath);

            // Get allowed base directories (user directories only)
            var allowedBases = new[]
            {
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            };

            // Check if the path starts with one of the allowed bases
            if (!allowedBases.Any(baseDir => fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // Additional checks for suspicious patterns
            if (filePath.Contains("..") || filePath.Contains("~"))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Path validation failed
            return false;
        }
    }
}
