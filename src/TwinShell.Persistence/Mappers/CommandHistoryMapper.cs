using System.Text.Json;
using TwinShell.Core.Helpers;
using TwinShell.Core.Models;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Mappers;

/// <summary>
/// Maps between CommandHistory domain model and CommandHistoryEntity
/// </summary>
public static class CommandHistoryMapper
{
    private static JsonSerializerOptions JsonOptions => JsonOptionsHelper.CompactStorage;

    public static CommandHistoryEntity ToEntity(CommandHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        return new CommandHistoryEntity
        {
            Id = history.Id,
            UserId = history.UserId,
            ActionId = history.ActionId,
            GeneratedCommand = history.GeneratedCommand,
            ParametersJson = JsonSerializer.Serialize(history.Parameters, JsonOptions),
            Platform = history.Platform,
            CreatedAt = history.CreatedAt,
            Category = history.Category,
            ActionTitle = history.ActionTitle,
            IsExecuted = history.IsExecuted,
            ExitCode = history.ExitCode,
            ExecutionDurationTicks = history.ExecutionDuration?.Ticks,
            ExecutionSuccess = history.ExecutionSuccess
        };
    }

    public static CommandHistory ToModel(CommandHistoryEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var history = new CommandHistory
        {
            Id = entity.Id,
            UserId = entity.UserId,
            ActionId = entity.ActionId,
            GeneratedCommand = entity.GeneratedCommand,
            Parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ParametersJson, JsonOptions)
                ?? new Dictionary<string, string>(),
            Platform = entity.Platform,
            CreatedAt = entity.CreatedAt,
            Category = entity.Category,
            ActionTitle = entity.ActionTitle,
            IsExecuted = entity.IsExecuted,
            ExitCode = entity.ExitCode,
            ExecutionDuration = entity.ExecutionDurationTicks.HasValue
                ? TimeSpan.FromTicks(entity.ExecutionDurationTicks.Value)
                : null,
            ExecutionSuccess = entity.ExecutionSuccess
        };

        if (entity.Action != null)
        {
            history.Action = ActionMapper.ToModel(entity.Action);
        }

        return history;
    }
}
