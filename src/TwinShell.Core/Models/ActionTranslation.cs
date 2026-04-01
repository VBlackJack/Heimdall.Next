namespace TwinShell.Core.Models;

/// <summary>
/// Represents a translation for an action
/// </summary>
public class ActionTranslation
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Associated action ID
    /// </summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>
    /// Culture code (e.g., "en", "es", "fr")
    /// </summary>
    public string CultureCode { get; set; } = string.Empty;

    /// <summary>
    /// Translated title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Translated description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Translated notes (optional)
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Navigation property to action
    /// </summary>
    public Action? Action { get; set; }
}
