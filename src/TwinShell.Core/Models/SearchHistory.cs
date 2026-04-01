namespace TwinShell.Core.Models;

/// <summary>
/// Represents a search history entry for autocomplete and suggestions.
/// Stores recent searches to improve user experience.
/// </summary>
public class SearchHistory
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The search term that was used
    /// </summary>
    public required string SearchTerm { get; set; }

    /// <summary>
    /// Normalized version of the search term (for deduplication)
    /// </summary>
    public string NormalizedSearchTerm { get; set; } = string.Empty;

    /// <summary>
    /// Number of times this search was performed
    /// </summary>
    public int SearchCount { get; set; } = 1;

    /// <summary>
    /// Number of results found for this search
    /// </summary>
    public int ResultCount { get; set; }

    /// <summary>
    /// Last time this search was performed
    /// </summary>
    public DateTime LastSearchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// First time this search was performed
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this search was successful (found at least one result)
    /// </summary>
    public bool WasSuccessful { get; set; }

    /// <summary>
    /// Optional user ID (for multi-user scenarios)
    /// </summary>
    public string? UserId { get; set; }
}
