namespace TwinShell.Core.Enums;

/// <summary>
/// Represents the application theme options.
/// </summary>
public enum Theme
{
    /// <summary>
    /// Light theme with bright colors and dark text.
    /// </summary>
    Light = 0,

    /// <summary>
    /// Dark theme with dark colors and light text (WCAG AA compliant).
    /// </summary>
    Dark = 1,

    /// <summary>
    /// Follows the system theme preference (Windows theme).
    /// </summary>
    System = 2,

    /// <summary>
    /// High contrast theme for accessibility (WCAG AAA+ compliant, 10:1+ contrast).
    /// </summary>
    HighContrast = 3
}
