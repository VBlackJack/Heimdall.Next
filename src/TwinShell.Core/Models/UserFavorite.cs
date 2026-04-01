namespace TwinShell.Core.Models;

/// <summary>
/// Represents a user's favorite action
/// </summary>
public class UserFavorite
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// User identifier (nullable for MVP - single user mode)
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Associated action ID
    /// </summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>
    /// Associated action (navigation property)
    /// </summary>
    public Action? Action { get; set; }

    /// <summary>
    /// Timestamp when the favorite was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Display order for sorting favorites
    /// </summary>
    public int DisplayOrder { get; set; }
}
