using TwinShell.Core.Models;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Mappers;

/// <summary>
/// Maps between AuditLog domain model and AuditLogEntity
/// </summary>
public static class AuditLogMapper
{
    public static AuditLogEntity ToEntity(AuditLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        return new AuditLogEntity
        {
            Id = log.Id,
            Timestamp = log.Timestamp,
            UserId = log.UserId,
            ActionId = log.ActionId,
            Command = log.Command,
            Platform = log.Platform,
            ExitCode = log.ExitCode,
            Success = log.Success,
            DurationTicks = log.Duration.Ticks,
            ActionTitle = log.ActionTitle,
            Category = log.Category,
            WasDangerous = log.WasDangerous
        };
    }

    public static AuditLog ToModel(AuditLogEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new AuditLog
        {
            Id = entity.Id,
            Timestamp = entity.Timestamp,
            UserId = entity.UserId,
            ActionId = entity.ActionId,
            Command = entity.Command,
            Platform = entity.Platform,
            ExitCode = entity.ExitCode,
            Success = entity.Success,
            Duration = TimeSpan.FromTicks(entity.DurationTicks),
            ActionTitle = entity.ActionTitle,
            Category = entity.Category,
            WasDangerous = entity.WasDangerous
        };
    }
}
