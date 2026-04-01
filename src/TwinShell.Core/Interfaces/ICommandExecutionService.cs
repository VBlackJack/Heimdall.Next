using TwinShell.Core.Enums;
using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for executing PowerShell and Bash commands
/// </summary>
public interface ICommandExecutionService
{
    /// <summary>
    /// Executes a command on the specified platform
    /// </summary>
    /// <param name="command">Command to execute</param>
    /// <param name="platform">Platform (Windows = PowerShell, Linux = Bash)</param>
    /// <param name="cancellationToken">Cancellation token to stop execution</param>
    /// <param name="timeoutSeconds">Timeout in seconds (default: 30)</param>
    /// <param name="onOutputReceived">Callback for real-time output (optional)</param>
    /// <returns>Execution result</returns>
    Task<ExecutionResult> ExecuteAsync(
        string command,
        Platform platform,
        CancellationToken cancellationToken,
        int timeoutSeconds = 30,
        Action<OutputLine>? onOutputReceived = null);
}
