/*
 * Copyright 2026 Julien Bombled
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

using Heimdall.Core.Models;

namespace Heimdall.Core.StateMachine;

/// <summary>
/// Manages the global application lifecycle status with validated transitions.
/// Thread-safe: all state mutations are protected by a lock.
/// </summary>
public sealed class ApplicationStatusMachine
{
    private ApplicationStatus _currentStatus = ApplicationStatus.Initializing;
    private ApplicationStatus _previousStatus = ApplicationStatus.Initializing;
    private string? _busyReason;
    private string? _errorMessage;
    private int _activeOperationCount;
    private readonly object _lock = new();

    private static readonly Dictionary<ApplicationStatus, HashSet<ApplicationStatus>> ValidTransitions = new()
    {
        [ApplicationStatus.Initializing] = [ApplicationStatus.Ready, ApplicationStatus.Error],
        [ApplicationStatus.Ready] = [ApplicationStatus.Busy, ApplicationStatus.Shutdown, ApplicationStatus.Error],
        [ApplicationStatus.Busy] = [ApplicationStatus.Ready, ApplicationStatus.Error],
        [ApplicationStatus.Error] = [ApplicationStatus.Ready, ApplicationStatus.Shutdown],
        [ApplicationStatus.Shutdown] = [],
    };

    private static readonly Dictionary<ApplicationStatus, ApplicationStatusMetadata> Metadata = new()
    {
        [ApplicationStatus.Initializing] = new("AppStatusInitializing", AllowsUserAction: false, IsTerminal: false),
        [ApplicationStatus.Ready] = new("StatusReady", AllowsUserAction: true, IsTerminal: false),
        [ApplicationStatus.Busy] = new("AppStatusBusy", AllowsUserAction: false, IsTerminal: false),
        [ApplicationStatus.Error] = new("AppStatusError", AllowsUserAction: true, IsTerminal: false),
        [ApplicationStatus.Shutdown] = new("AppStatusShutdown", AllowsUserAction: false, IsTerminal: true),
    };

    /// <summary>
    /// Raised after a successful status transition.
    /// Parameters: previousStatus, newStatus.
    /// </summary>
    public event Action<ApplicationStatus, ApplicationStatus>? StatusChanged;

    /// <summary>
    /// Gets the current application status.
    /// </summary>
    public ApplicationStatus CurrentStatus
    {
        get
        {
            lock (_lock) { return _currentStatus; }
        }
    }

    /// <summary>
    /// Gets the previous application status.
    /// </summary>
    public ApplicationStatus PreviousStatus
    {
        get
        {
            lock (_lock) { return _previousStatus; }
        }
    }

    /// <summary>
    /// Gets the reason for the current Busy status, or null if not busy.
    /// </summary>
    public string? BusyReason
    {
        get
        {
            lock (_lock) { return _busyReason; }
        }
    }

    /// <summary>
    /// Gets the error message for the current Error status, or null if not in error.
    /// </summary>
    public string? ErrorMessage
    {
        get
        {
            lock (_lock) { return _errorMessage; }
        }
    }

    /// <summary>
    /// Whether the application is in a state that allows user interaction.
    /// </summary>
    public bool AllowsUserAction
    {
        get
        {
            lock (_lock) { return Metadata[_currentStatus].AllowsUserAction; }
        }
    }

    /// <summary>
    /// Attempts to transition to a new application status.
    /// Same-state transitions are treated as successful no-ops.
    /// </summary>
    /// <param name="newStatus">The target status.</param>
    /// <param name="reason">Optional reason (used for Busy and Error states).</param>
    /// <returns>True if the transition was valid and applied, or was a same-state no-op.</returns>
    public bool TryTransition(ApplicationStatus newStatus, string? reason = null)
    {
        ApplicationStatus previousStatus;

        lock (_lock)
        {
            if (_currentStatus == newStatus)
            {
                return true;
            }

            if (!IsValidTransition(_currentStatus, newStatus))
            {
                return false;
            }

            previousStatus = _currentStatus;
            _previousStatus = previousStatus;
            _currentStatus = newStatus;

            switch (newStatus)
            {
                case ApplicationStatus.Busy:
                    _busyReason = reason;
                    break;
                case ApplicationStatus.Error:
                    _errorMessage = reason;
                    break;
                case ApplicationStatus.Ready:
                    _busyReason = null;
                    _errorMessage = null;
                    break;
            }
        }

        StatusChanged?.Invoke(previousStatus, newStatus);
        return true;
    }

    /// <summary>
    /// Begins a tracked operation, transitioning from Ready to Busy if needed.
    /// Returns a disposable that transitions back to Ready when disposed
    /// (if no other operations are still active).
    /// </summary>
    /// <param name="reason">Description of the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the application is not in Ready or Busy state.
    /// </exception>
    public IDisposable BeginOperation(string? reason = null)
    {
        lock (_lock)
        {
            if (_currentStatus != ApplicationStatus.Ready
                && _currentStatus != ApplicationStatus.Busy
                && _currentStatus != ApplicationStatus.Initializing)
            {
                throw new InvalidOperationException(
                    $"Cannot begin operation in {_currentStatus} state.");
            }

            _activeOperationCount++;
        }

        if (_currentStatus == ApplicationStatus.Ready)
        {
            TryTransition(ApplicationStatus.Busy, reason);
        }

        return new OperationScope(this);
    }

    /// <summary>
    /// Checks whether a transition from one status to another is valid.
    /// </summary>
    public static bool IsValidTransition(ApplicationStatus from, ApplicationStatus to)
    {
        return ValidTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>
    /// Returns the metadata for a given application status.
    /// </summary>
    public static ApplicationStatusMetadata GetMetadata(ApplicationStatus status)
    {
        return Metadata[status];
    }

    private void EndOperation()
    {
        bool shouldTransitionToReady;

        lock (_lock)
        {
            _activeOperationCount = Math.Max(0, _activeOperationCount - 1);
            shouldTransitionToReady = _activeOperationCount == 0
                                     && (_currentStatus == ApplicationStatus.Busy
                                         || _currentStatus == ApplicationStatus.Initializing);
        }

        if (shouldTransitionToReady)
        {
            TryTransition(ApplicationStatus.Ready);
        }
    }

    private sealed class OperationScope(ApplicationStatusMachine machine) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                machine.EndOperation();
            }
        }
    }
}

/// <summary>
/// Immutable metadata describing an application status.
/// </summary>
/// <param name="DisplayKey">i18n key for user-facing display text.</param>
/// <param name="AllowsUserAction">Whether user interactions are permitted in this status.</param>
/// <param name="IsTerminal">Whether this status represents a final endpoint.</param>
public sealed record ApplicationStatusMetadata(
    string DisplayKey,
    bool AllowsUserAction,
    bool IsTerminal
);
