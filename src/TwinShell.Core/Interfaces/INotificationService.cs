namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for displaying toast notifications to the user.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows an informational notification.
    /// </summary>
    void ShowInfo(string message, string? title = null, int durationSeconds = 3);

    /// <summary>
    /// Shows a success notification.
    /// </summary>
    void ShowSuccess(string message, string? title = null, int durationSeconds = 3);

    /// <summary>
    /// Shows a warning notification.
    /// </summary>
    void ShowWarning(string message, string? title = null, int durationSeconds = 4);

    /// <summary>
    /// Shows an error notification.
    /// </summary>
    void ShowError(string message, string? title = null, int durationSeconds = 5);
}
