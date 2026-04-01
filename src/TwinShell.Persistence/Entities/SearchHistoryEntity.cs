namespace TwinShell.Persistence.Entities;

/// <summary>
/// Database entity for SearchHistory
/// </summary>
public class SearchHistoryEntity
{
    public string Id { get; set; } = string.Empty;
    public string SearchTerm { get; set; } = string.Empty;
    public string NormalizedSearchTerm { get; set; } = string.Empty;
    public int SearchCount { get; set; }
    public int ResultCount { get; set; }
    public DateTime LastSearchedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool WasSuccessful { get; set; }
    public string? UserId { get; set; }
}
