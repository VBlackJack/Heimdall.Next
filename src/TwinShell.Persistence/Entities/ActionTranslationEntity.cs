namespace TwinShell.Persistence.Entities;

/// <summary>
/// Database entity for ActionTranslation
/// </summary>
public class ActionTranslationEntity
{
    public string Id { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public ActionEntity? Action { get; set; }
    public string CultureCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
