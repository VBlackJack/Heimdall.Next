namespace TwinShell.Core.Constants;

/// <summary>
/// Database-related constants for TwinShell application.
/// </summary>
public static class DatabaseConstants
{
    /// <summary>
    /// Default database connection string for SQLite.
    /// </summary>
    public const string DefaultConnectionString = "Data Source=twinshell.db";

    /// <summary>
    /// Default database file name.
    /// </summary>
    public const string DefaultDatabaseFileName = "twinshell.db";

    /// <summary>
    /// Configuration file name for JSON export/import.
    /// </summary>
    public const string ConfigurationFileName = "TwinShell-Config";

    /// <summary>
    /// JSON file extension.
    /// </summary>
    public const string JsonFileExtension = ".json";

    /// <summary>
    /// JSON file filter for dialogs.
    /// </summary>
    public const string JsonFileFilter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
}
