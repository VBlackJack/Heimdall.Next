namespace TwinShell.Core.Constants;

/// <summary>
/// UI-related constants for TwinShell application.
/// </summary>
public static class UIConstants
{
    /// <summary>
    /// The internal name for the favorites category.
    /// </summary>
    public const string FavoritesCategoryName = "Favorites";

    /// <summary>
    /// The display name for the favorites category (with emoji).
    /// </summary>
    public const string FavoritesCategoryDisplay = "⭐ Favorites";

    /// <summary>
    /// The display name for the "All" category (shows all commands).
    /// </summary>
    public const string AllCategoryDisplay = "📋 All Commands";

    /// <summary>
    /// Maximum number of favorites a user can have.
    /// </summary>
    public const int MaxFavoritesCount = 50;

    /// <summary>
    /// Maximum number of recent commands to display.
    /// </summary>
    public const int MaxRecentCommandsCount = 10;

    /// <summary>
    /// Default status message shown when the app is ready.
    /// </summary>
    public const string DefaultStatusMessage = "Ready";

    /// <summary>
    /// Status message shown when loading actions.
    /// </summary>
    public const string LoadingActionsMessage = "Loading actions...";

    /// <summary>
    /// Status message shown when refreshing.
    /// </summary>
    public const string RefreshingMessage = "Refreshing...";
}
