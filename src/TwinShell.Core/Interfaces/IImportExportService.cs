namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for importing and exporting actions to/from JSON files
/// </summary>
public interface IImportExportService
{
    /// <summary>
    /// Export all actions to a JSON file
    /// </summary>
    /// <param name="filePath">Path to the export file</param>
    /// <returns>Export result with status and count</returns>
    Task<ExportResult> ExportActionsAsync(string filePath);

    /// <summary>
    /// Export a single action to a JSON file
    /// </summary>
    /// <param name="filePath">Path to the export file</param>
    /// <param name="action">The action to export</param>
    /// <returns>Export result with status</returns>
    Task<ExportResult> ExportSingleActionAsync(string filePath, Models.Action action);

    /// <summary>
    /// Import a single action from a JSON file
    /// </summary>
    /// <param name="filePath">Path to the import file</param>
    /// <returns>Import result with status</returns>
    Task<ImportResult> ImportSingleActionAsync(string filePath);

    /// <summary>
    /// Import actions from a JSON file
    /// </summary>
    /// <param name="filePath">Path to the import file</param>
    /// <param name="mode">Import mode (Merge or Replace)</param>
    /// <returns>Import result with statistics</returns>
    Task<ImportResult> ImportActionsAsync(string filePath, ImportMode mode);

    /// <summary>
    /// Validate a JSON import file without importing
    /// </summary>
    /// <param name="filePath">Path to the file to validate</param>
    /// <returns>Validation result</returns>
    Task<ValidationResult> ValidateImportFileAsync(string filePath);
}

/// <summary>
/// Result of an export operation
/// </summary>
public class ExportResult
{
    public bool Success { get; set; }
    public int ActionCount { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of an import operation
/// </summary>
public class ImportResult
{
    public bool Success { get; set; }
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of a validation operation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SchemaVersion { get; set; }
    public int ActionCount { get; set; }
}

/// <summary>
/// Import mode for actions
/// </summary>
public enum ImportMode
{
    /// <summary>
    /// Merge: Add new actions and update existing user-created ones, skip system actions
    /// </summary>
    Merge,

    /// <summary>
    /// Replace: Delete all user-created actions before importing
    /// </summary>
    Replace
}
