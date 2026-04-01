/*
 * Copyright 2025 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TwinShell.Core.Interfaces;

namespace TwinShell.Infrastructure.Services;

/// <summary>
/// Implementation of correlation service for request tracing.
/// Uses AsyncLocal to maintain correlation context across async calls.
/// </summary>
public class CorrelationService : ICorrelationService
{
    private readonly ILogger<CorrelationService> _logger;
    private readonly AsyncLocal<CorrelationContext?> _currentContext = new();

    /// <summary>
    /// Activity source for OpenTelemetry integration.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("TwinShell", "1.5.1");

    public CorrelationService(ILogger<CorrelationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string CurrentCorrelationId => _currentContext.Value?.CorrelationId ?? GenerateCorrelationId();

    /// <inheritdoc />
    public string? CurrentOperationName => _currentContext.Value?.OperationName;

    /// <inheritdoc />
    public TimeSpan ElapsedTime => _currentContext.Value?.Stopwatch.Elapsed ?? TimeSpan.Zero;

    /// <inheritdoc />
    public IDisposable BeginScope(string operationName)
    {
        return BeginScope(operationName, GenerateCorrelationId());
    }

    /// <inheritdoc />
    public IDisposable BeginScope(string operationName, string correlationId)
    {
        var parentContext = _currentContext.Value;
        var context = new CorrelationContext(correlationId, operationName, parentContext);
        _currentContext.Value = context;

        // Start an Activity for OpenTelemetry integration
        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        activity?.SetTag("correlation.id", correlationId);

        _logger.LogDebug(
            "Started operation {OperationName} with correlation ID {CorrelationId}",
            operationName,
            correlationId);

        return new CorrelationScope(this, parentContext, activity);
    }

    private static string GenerateCorrelationId()
    {
        // Format: timestamp-guid (e.g., "20250124-abc123")
        return $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    private void EndScope(CorrelationContext? parentContext, Activity? activity)
    {
        var currentContext = _currentContext.Value;
        if (currentContext != null)
        {
            var elapsed = currentContext.Stopwatch.Elapsed;
            _logger.LogDebug(
                "Completed operation {OperationName} (correlation ID: {CorrelationId}) in {ElapsedMs}ms",
                currentContext.OperationName,
                currentContext.CorrelationId,
                elapsed.TotalMilliseconds);
        }

        activity?.Dispose();
        _currentContext.Value = parentContext;
    }

    /// <summary>
    /// Internal context for tracking correlation state.
    /// </summary>
    private class CorrelationContext
    {
        public string CorrelationId { get; }
        public string OperationName { get; }
        public CorrelationContext? Parent { get; }
        public Stopwatch Stopwatch { get; }

        public CorrelationContext(string correlationId, string operationName, CorrelationContext? parent)
        {
            CorrelationId = correlationId;
            OperationName = operationName;
            Parent = parent;
            Stopwatch = Stopwatch.StartNew();
        }
    }

    /// <summary>
    /// Disposable scope that restores parent context when disposed.
    /// </summary>
    private class CorrelationScope : IDisposable
    {
        private readonly CorrelationService _service;
        private readonly CorrelationContext? _parentContext;
        private readonly Activity? _activity;
        private bool _disposed;

        public CorrelationScope(CorrelationService service, CorrelationContext? parentContext, Activity? activity)
        {
            _service = service;
            _parentContext = parentContext;
            _activity = activity;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _service.EndScope(_parentContext, _activity);
            _disposed = true;
        }
    }
}
