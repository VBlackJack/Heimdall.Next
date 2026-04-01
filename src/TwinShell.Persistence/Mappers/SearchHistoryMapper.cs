using TwinShell.Core.Models;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Mappers;

/// <summary>
/// Maps between SearchHistory domain model and SearchHistoryEntity
/// </summary>
public static class SearchHistoryMapper
{
    public static SearchHistoryEntity ToEntity(SearchHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        return new SearchHistoryEntity
        {
            Id = history.Id,
            SearchTerm = history.SearchTerm,
            NormalizedSearchTerm = history.NormalizedSearchTerm,
            SearchCount = history.SearchCount,
            ResultCount = history.ResultCount,
            LastSearchedAt = history.LastSearchedAt,
            CreatedAt = history.CreatedAt,
            WasSuccessful = history.WasSuccessful,
            UserId = history.UserId
        };
    }

    public static SearchHistory ToModel(SearchHistoryEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new SearchHistory
        {
            Id = entity.Id,
            SearchTerm = entity.SearchTerm,
            NormalizedSearchTerm = entity.NormalizedSearchTerm,
            SearchCount = entity.SearchCount,
            ResultCount = entity.ResultCount,
            LastSearchedAt = entity.LastSearchedAt,
            CreatedAt = entity.CreatedAt,
            WasSuccessful = entity.WasSuccessful,
            UserId = entity.UserId
        };
    }
}
