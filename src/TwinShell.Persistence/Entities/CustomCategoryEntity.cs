namespace TwinShell.Persistence.Entities;

/// <summary>
/// Database entity for custom user-defined categories.
/// </summary>
public class CustomCategoryEntity
{
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Universal unique identifier for GitOps synchronization.
    /// Used as the stable identifier across different environments.
    /// </summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string IconKey { get; set; } = "folder";
    public string ColorHex { get; set; } = "#2196F3";
    public bool IsSystemCategory { get; set; } = false;
    public int DisplayOrder { get; set; } = 0;
    public bool IsHidden { get; set; } = false;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }

    // Navigation property for many-to-many relationship
    public ICollection<ActionCategoryMappingEntity> ActionMappings { get; set; } = new List<ActionCategoryMappingEntity>();
}
