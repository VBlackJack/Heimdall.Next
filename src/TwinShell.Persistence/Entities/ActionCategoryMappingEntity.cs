namespace TwinShell.Persistence.Entities;

/// <summary>
/// Join table entity for many-to-many relationship between Actions and CustomCategories.
/// </summary>
public class ActionCategoryMappingEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Foreign key to ActionEntity.
    /// </summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to CustomCategoryEntity.
    /// </summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>
    /// Date and time when the action was added to the category.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ActionEntity Action { get; set; } = null!;
    public CustomCategoryEntity Category { get; set; } = null!;
}
