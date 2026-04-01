using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TwinShell.Core.Enums;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;

namespace TwinShell.Infrastructure.Services;

/// <summary>
/// Service for executing batches of commands sequentially
/// </summary>
public class BatchExecutionService : IBatchExecutionService
{
    private readonly ICommandExecutionService _commandExecutionService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<BatchExecutionService>? _logger;

    public BatchExecutionService(
        ICommandExecutionService commandExecutionService,
        IAuditLogService auditLogService,
        ILogger<BatchExecutionService>? logger = null)
    {
        _commandExecutionService = commandExecutionService ?? throw new ArgumentNullException(nameof(commandExecutionService));
        _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
        _logger = logger;
    }

    /// <summary>
    /// Executes a batch of commands sequentially
    /// </summary>
    public async Task<BatchExecutionResult> ExecuteBatchAsync(
        CommandBatch batch,
        CancellationToken cancellationToken,
        int timeoutSeconds = 30,
        Action<BatchExecutionProgress>? onProgressChanged = null,
        Action<OutputLine>? onOutputReceived = null)
    {
        if (!ValidateBatch(batch))
        {
            return new BatchExecutionResult
            {
                Batch = batch,
                Success = false,
                ErrorMessage = "Invalid batch: must contain at least one command"
            };
        }

        var result = new BatchExecutionResult
        {
            Batch = batch,
            StartedAt = DateTime.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();
        var executedCount = 0;
        var successCount = 0;
        var failureCount = 0;
        var skippedCount = 0;

        try
        {
            for (int i = 0; i < batch.Commands.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    skippedCount = batch.Commands.Count - i;
                    break;
                }

                var command = batch.Commands[i];

                // Report progress
                var progress = new BatchExecutionProgress
                {
                    CurrentCommandIndex = i,
                    TotalCommands = batch.Commands.Count,
                    CurrentCommand = command,
                    CompletedCount = executedCount,
                    SuccessCount = successCount,
                    FailureCount = failureCount,
                    IsRunning = true
                };
                onProgressChanged?.Invoke(progress);

                // Execute the command
                var executionResult = await _commandExecutionService.ExecuteAsync(
                    command.Command,
                    command.Platform,
                    cancellationToken,
                    timeoutSeconds,
                    onOutputReceived);

                // Update command with result
                command.IsExecuted = true;
                command.ExecutionResult = executionResult;

                executedCount++;

                if (executionResult.Success)
                {
                    successCount++;

                    // Log successful execution with user tracking
                    await _auditLogService.AddLogAsync(new AuditLog
                    {
                        Timestamp = DateTime.UtcNow,
                        UserId = Environment.UserName,
                        ActionId = command.ActionId ?? batch.Id,
                        Command = command.Command,
                        Platform = command.Platform,
                        ExitCode = executionResult.ExitCode,
                        Success = true,
                        Duration = executionResult.Duration,
                        ActionTitle = command.ActionTitle,
                        Category = "Batch Execution",
                        WasDangerous = false
                    });
                }
                else
                {
                    failureCount++;

                    // Log to audit trail with user tracking
                    await _auditLogService.AddLogAsync(new AuditLog
                    {
                        Timestamp = DateTime.UtcNow,
                        UserId = Environment.UserName,
                        ActionId = command.ActionId ?? batch.Id,
                        Command = command.Command,
                        Platform = command.Platform,
                        ExitCode = executionResult.ExitCode,
                        Success = false,
                        Duration = executionResult.Duration,
                        ActionTitle = command.ActionTitle,
                        Category = "Batch Execution",
                        WasDangerous = false
                    });

                    // If StopOnError mode, break the loop
                    if (batch.ExecutionMode == BatchExecutionMode.StopOnError)
                    {
                        skippedCount = batch.Commands.Count - (i + 1);
                        progress.ErrorMessage = $"Command failed: {command.ActionTitle}. Stopping execution.";
                        onProgressChanged?.Invoke(progress);
                        break;
                    }
                }

                // Report progress after execution
                progress.CompletedCount = executedCount;
                progress.SuccessCount = successCount;
                progress.FailureCount = failureCount;
                progress.IsRunning = i < batch.Commands.Count - 1;
                onProgressChanged?.Invoke(progress);
            }

            stopwatch.Stop();

            // Final progress update
            var finalProgress = new BatchExecutionProgress
            {
                CurrentCommandIndex = batch.Commands.Count,
                TotalCommands = batch.Commands.Count,
                CompletedCount = executedCount,
                SuccessCount = successCount,
                FailureCount = failureCount,
                IsRunning = false
            };
            onProgressChanged?.Invoke(finalProgress);

            // Update batch last executed timestamp
            batch.LastExecutedAt = DateTime.UtcNow;

            // Build result
            result.Success = failureCount == 0 && !result.WasCancelled;
            result.ExecutedCount = executedCount;
            result.SuccessCount = successCount;
            result.FailureCount = failureCount;
            result.SkippedCount = skippedCount;
            result.TotalDuration = stopwatch.Elapsed;
            result.CompletedAt = DateTime.UtcNow;

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            // SECURITY: Don't expose exception details to users
            _logger?.LogError(ex, "Batch execution failed for batch {BatchId}", batch.Id);
            result.ErrorMessage = "Batch execution failed";
            result.TotalDuration = stopwatch.Elapsed;
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }
    }

    /// <summary>
    /// Validates a batch before execution
    /// </summary>
    public bool ValidateBatch(CommandBatch batch)
    {
        if (batch == null)
            return false;

        if (batch.Commands == null || batch.Commands.Count == 0)
            return false;

        // All commands must have a non-empty command string
        if (batch.Commands.Any(c => string.IsNullOrWhiteSpace(c.Command)))
            return false;

        return true;
    }
}
