using TwinShell.Core.Models;

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for executing batches of commands sequentially
/// </summary>
public interface IBatchExecutionService
{
    /// <summary>
    /// Executes a batch of commands sequentially
    /// </summary>
    /// <param name="batch">The batch to execute</param>
    /// <param name="cancellationToken">Cancellation token to stop execution</param>
    /// <param name="timeoutSeconds">Timeout in seconds per command (default: 30)</param>
    /// <param name="onProgressChanged">Callback for progress updates (optional)</param>
    /// <param name="onOutputReceived">Callback for real-time command output (optional)</param>
    /// <returns>Batch execution result</returns>
    Task<BatchExecutionResult> ExecuteBatchAsync(
        CommandBatch batch,
        CancellationToken cancellationToken,
        int timeoutSeconds = 30,
        Action<BatchExecutionProgress>? onProgressChanged = null,
        Action<OutputLine>? onOutputReceived = null);

    /// <summary>
    /// Validates a batch before execution
    /// </summary>
    /// <param name="batch">The batch to validate</param>
    /// <returns>True if batch is valid, false otherwise</returns>
    bool ValidateBatch(CommandBatch batch);
}
