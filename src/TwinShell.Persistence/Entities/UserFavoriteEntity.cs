namespace TwinShell.Persistence.Entities;

/// <summary>
/// Database entity for UserFavorite
/// </summary>
public class UserFavoriteEntity
{
    public string Id { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public ActionEntity? Action { get; set; }
    public DateTime CreatedAt { get; set; }
    public int DisplayOrder { get; set; }
}
