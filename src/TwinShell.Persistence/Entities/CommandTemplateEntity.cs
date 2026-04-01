using TwinShell.Core.Enums;

namespace TwinShell.Persistence.Entities;

/// <summary>
/// Database entity for CommandTemplate
/// </summary>
public class CommandTemplateEntity
{
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Universal unique identifier for GitOps synchronization.
    /// Used as the stable identifier across different environments.
    /// </summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Platform Platform { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CommandPattern { get; set; } = string.Empty;

    /// <summary>
    /// Parameters stored as JSON
    /// </summary>
    public string ParametersJson { get; set; } = "[]";
}
