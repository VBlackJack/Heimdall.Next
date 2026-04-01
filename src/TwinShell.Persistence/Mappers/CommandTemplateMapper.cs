using System.Text.Json;
using TwinShell.Core.Helpers;
using TwinShell.Core.Models;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Mappers;

/// <summary>
/// Maps between CommandTemplate domain model and CommandTemplateEntity
/// </summary>
public static class CommandTemplateMapper
{
    private static JsonSerializerOptions JsonOptions => JsonOptionsHelper.CompactStorage;

    public static CommandTemplateEntity ToEntity(CommandTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return new CommandTemplateEntity
        {
            Id = template.Id,
            PublicId = template.PublicId,
            Platform = template.Platform,
            Name = template.Name,
            CommandPattern = template.CommandPattern,
            ParametersJson = JsonSerializer.Serialize(template.Parameters, JsonOptions)
        };
    }

    public static CommandTemplate ToModel(CommandTemplateEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new CommandTemplate
        {
            Id = entity.Id,
            PublicId = entity.PublicId,
            Platform = entity.Platform,
            Name = entity.Name,
            CommandPattern = entity.CommandPattern,
            Parameters = JsonSerializer.Deserialize<List<TemplateParameter>>(entity.ParametersJson, JsonOptions) ?? new List<TemplateParameter>()
        };
    }
}
