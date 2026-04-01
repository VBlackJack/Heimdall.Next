using TwinShell.Core.Models;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Mappers;

/// <summary>
/// Maps between UserFavorite domain model and UserFavoriteEntity
/// </summary>
public static class UserFavoriteMapper
{
    public static UserFavoriteEntity ToEntity(UserFavorite favorite)
    {
        ArgumentNullException.ThrowIfNull(favorite);

        return new UserFavoriteEntity
        {
            Id = favorite.Id,
            UserId = favorite.UserId,
            ActionId = favorite.ActionId,
            CreatedAt = favorite.CreatedAt,
            DisplayOrder = favorite.DisplayOrder
        };
    }

    public static UserFavorite ToModel(UserFavoriteEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var favorite = new UserFavorite
        {
            Id = entity.Id,
            UserId = entity.UserId,
            ActionId = entity.ActionId,
            CreatedAt = entity.CreatedAt,
            DisplayOrder = entity.DisplayOrder
        };

        if (entity.Action != null)
        {
            favorite.Action = ActionMapper.ToModel(entity.Action);
        }

        return favorite;
    }
}
