namespace TwinShell.Core.Constants;

/// <summary>
/// Timeout constants for command execution and network operations.
/// </summary>
public static class TimeoutConstants
{
    /// <summary>
    /// Default command execution timeout in seconds (30 seconds).
    /// </summary>
    public const int CommandTimeoutSeconds = 30;

    /// <summary>
    /// Maximum command execution timeout in seconds (300 seconds = 5 minutes).
    /// </summary>
    public const int MaxCommandTimeoutSeconds = 300;

    /// <summary>
    /// Minimum command execution timeout in seconds (1 second).
    /// </summary>
    public const int MinCommandTimeoutSeconds = 1;

    /// <summary>
    /// PowerShell Gallery search timeout in seconds (60 seconds).
    /// </summary>
    public const int PowerShellGallerySearchTimeoutSeconds = 60;

    /// <summary>
    /// PowerShell Gallery install timeout in seconds (300 seconds = 5 minutes).
    /// </summary>
    public const int PowerShellGalleryInstallTimeoutSeconds = 300;

    /// <summary>
    /// HTTP request timeout in seconds for general API calls (30 seconds).
    /// </summary>
    public const int HttpTimeoutSeconds = 30;
}
