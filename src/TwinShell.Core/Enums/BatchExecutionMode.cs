namespace TwinShell.Core.Enums;

/// <summary>
/// Defines how batch execution should handle errors
/// </summary>
public enum BatchExecutionMode
{
    /// <summary>
    /// Stop execution when any command fails (ExitCode != 0)
    /// </summary>
    StopOnError,

    /// <summary>
    /// Continue executing remaining commands even if some fail
    /// </summary>
    ContinueOnError
}
