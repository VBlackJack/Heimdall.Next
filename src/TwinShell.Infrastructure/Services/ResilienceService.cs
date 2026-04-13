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

using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace TwinShell.Infrastructure.Services;

/// <summary>
/// Provides resilience patterns (circuit breaker, retry, timeout) for external service calls.
/// Implements fault-tolerance patterns for PowerShell Gallery, Git operations, etc.
/// </summary>
public sealed class ResilienceService
{
    private readonly ILogger<ResilienceService> _logger;

    // Circuit breaker states
    private readonly Dictionary<string, ResiliencePipeline> _pipelines = new();
    private readonly Dictionary<string, CircuitBreakerStateProvider> _circuitStates = new();

    public ResilienceService(ILogger<ResilienceService> logger)
    {
        _logger = logger;
        InitializePipelines();
    }

    private void InitializePipelines()
    {
        // HTTP operations (PowerShell Gallery, etc.)
        _pipelines["http"] = CreateHttpPipeline();

        // Git operations (network-intensive)
        _pipelines["git"] = CreateGitPipeline();

        // Database operations (local, fast)
        _pipelines["database"] = CreateDatabasePipeline();
    }

    private ResiliencePipeline CreateHttpPipeline()
    {
        var stateProvider = new CircuitBreakerStateProvider();
        _circuitStates["http"] = stateProvider;

        return new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(30))
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning("HTTP retry attempt {Attempt} after {Delay}ms. Reason: {Reason}",
                        args.AttemptNumber, args.RetryDelay.TotalMilliseconds, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                OnOpened = args =>
                {
                    _logger.LogWarning("HTTP circuit breaker OPENED. Breaking for {Duration}s",
                        args.BreakDuration.TotalSeconds);
                    stateProvider.IsOpen = true;
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("HTTP circuit breaker CLOSED. Normal operation resumed.");
                    stateProvider.IsOpen = false;
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = _ =>
                {
                    _logger.LogInformation("HTTP circuit breaker HALF-OPEN. Testing connection...");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private ResiliencePipeline CreateGitPipeline()
    {
        var stateProvider = new CircuitBreakerStateProvider();
        _circuitStates["git"] = stateProvider;

        return new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromMinutes(5))
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(3),
                BackoffType = DelayBackoffType.Linear,
                OnRetry = args =>
                {
                    _logger.LogWarning("Git retry attempt {Attempt}. Reason: {Reason}",
                        args.AttemptNumber, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.7,
                SamplingDuration = TimeSpan.FromMinutes(2),
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromMinutes(1),
                OnOpened = args =>
                {
                    _logger.LogWarning("Git circuit breaker OPENED for {Duration}s",
                        args.BreakDuration.TotalSeconds);
                    stateProvider.IsOpen = true;
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    stateProvider.IsOpen = false;
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private ResiliencePipeline CreateDatabasePipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(10))
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(100),
                BackoffType = DelayBackoffType.Constant
            })
            .Build();
    }

    /// <summary>
    /// Executes an HTTP operation with circuit breaker and retry.
    /// </summary>
    public async Task<T> ExecuteHttpAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        return await _pipelines["http"].ExecuteAsync(async ct => await operation(ct), cancellationToken);
    }

    /// <summary>
    /// Executes a Git operation with circuit breaker and retry.
    /// </summary>
    public async Task<T> ExecuteGitAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        return await _pipelines["git"].ExecuteAsync(async ct => await operation(ct), cancellationToken);
    }

    /// <summary>
    /// Executes a database operation with timeout and retry.
    /// </summary>
    public async Task<T> ExecuteDatabaseAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        return await _pipelines["database"].ExecuteAsync(async ct => await operation(ct), cancellationToken);
    }

    /// <summary>
    /// Checks if a circuit breaker is currently open.
    /// </summary>
    public bool IsCircuitOpen(string pipelineName)
    {
        return _circuitStates.TryGetValue(pipelineName, out var state) && state.IsOpen;
    }

    /// <summary>
    /// Gets the status of all circuit breakers.
    /// </summary>
    public Dictionary<string, CircuitBreakerStatus> GetCircuitBreakerStatuses()
    {
        return _circuitStates.ToDictionary(
            kvp => kvp.Key,
            kvp => new CircuitBreakerStatus
            {
                Name = kvp.Key,
                IsOpen = kvp.Value.IsOpen,
                LastStateChange = kvp.Value.LastStateChange
            });
    }

    /// <summary>
    /// Provider for tracking circuit breaker state.
    /// </summary>
    private sealed class CircuitBreakerStateProvider
    {
        private bool _isOpen;

        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                _isOpen = value;
                LastStateChange = DateTime.UtcNow;
            }
        }

        public DateTime LastStateChange { get; private set; } = DateTime.UtcNow;
    }
}

/// <summary>
/// Status of a circuit breaker.
/// </summary>
public sealed class CircuitBreakerStatus
{
    public string Name { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public DateTime LastStateChange { get; set; }
}
