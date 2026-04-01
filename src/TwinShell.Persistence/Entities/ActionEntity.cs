using TwinShell.Core.Enums;

namespace TwinShell.Persistence.Entities;

/// <summary>
/// Database entity for Action
/// </summary>
public class ActionEntity
{
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Universal unique identifier for GitOps synchronization.
    /// Used as the stable identifier across different environments.
    /// </summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Platform Platform { get; set; }
    public CriticalityLevel Level { get; set; }

    /// <summary>
    /// Tags stored as JSON array
    /// </summary>
    public string TagsJson { get; set; } = "[]";

    public string? WindowsCommandTemplateId { get; set; }
    public CommandTemplateEntity? WindowsCommandTemplate { get; set; }

    public string? LinuxCommandTemplateId { get; set; }
    public CommandTemplateEntity? LinuxCommandTemplate { get; set; }

    /// <summary>
    /// Examples stored as JSON (legacy - for single-platform actions)
    /// </summary>
    public string ExamplesJson { get; set; } = "[]";

    /// <summary>
    /// Windows-specific examples stored as JSON (for cross-platform actions)
    /// </summary>
    public string WindowsExamplesJson { get; set; } = "[]";

    /// <summary>
    /// Linux-specific examples stored as JSON (for cross-platform actions)
    /// </summary>
    public string LinuxExamplesJson { get; set; } = "[]";

    public string? Notes { get; set; }

    /// <summary>
    /// Links stored as JSON
    /// </summary>
    public string LinksJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsUserCreated { get; set; }
}
