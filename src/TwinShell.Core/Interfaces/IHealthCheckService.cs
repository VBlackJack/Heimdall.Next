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

namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for performing health checks on application components.
/// Used for smoke tests, startup verification, and monitoring.
/// </summary>
public interface IHealthCheckService
{
    /// <summary>
    /// Performs all health checks and returns aggregated status.
    /// </summary>
    /// <returns>Overall health check result.</returns>
    Task<HealthCheckReport> CheckAllAsync();

    /// <summary>
    /// Checks database connectivity and integrity.
    /// </summary>
    Task<HealthCheckResult> CheckDatabaseAsync();

    /// <summary>
    /// Checks configuration file validity.
    /// </summary>
    Task<HealthCheckResult> CheckConfigurationAsync();

    /// <summary>
    /// Checks Git sync configuration and connectivity.
    /// </summary>
    Task<HealthCheckResult> CheckGitSyncAsync();

    /// <summary>
    /// Checks file system permissions for data directories.
    /// </summary>
    Task<HealthCheckResult> CheckFileSystemAsync();

    /// <summary>
    /// Checks external service connectivity (PowerShell Gallery, etc.).
    /// </summary>
    Task<HealthCheckResult> CheckExternalServicesAsync();

    /// <summary>
    /// Performs a quick startup check (minimal checks for fast app launch).
    /// </summary>
    Task<HealthCheckResult> QuickStartupCheckAsync();
}

/// <summary>
/// Result of a single health check.
/// </summary>
public class HealthCheckResult
{
    public string ComponentName { get; set; } = string.Empty;
    public HealthStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public DateTime CheckedAt { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public Exception? Exception { get; set; }

    public static HealthCheckResult Healthy(string component, string message = "OK", Dictionary<string, object>? data = null)
        => new()
        {
            ComponentName = component,
            Status = HealthStatus.Healthy,
            Message = message,
            CheckedAt = DateTime.UtcNow,
            Data = data ?? new()
        };

    public static HealthCheckResult Degraded(string component, string message, Dictionary<string, object>? data = null)
        => new()
        {
            ComponentName = component,
            Status = HealthStatus.Degraded,
            Message = message,
            CheckedAt = DateTime.UtcNow,
            Data = data ?? new()
        };

    public static HealthCheckResult Unhealthy(string component, string message, Exception? ex = null)
        => new()
        {
            ComponentName = component,
            Status = HealthStatus.Unhealthy,
            Message = message,
            CheckedAt = DateTime.UtcNow,
            Exception = ex
        };
}

/// <summary>
/// Aggregated health check report for all components.
/// </summary>
public class HealthCheckReport
{
    public HealthStatus OverallStatus { get; set; }
    public DateTime GeneratedAt { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public string Version { get; set; } = string.Empty;
    public List<HealthCheckResult> Results { get; set; } = new();

    /// <summary>
    /// Creates a report from individual check results.
    /// </summary>
    public static HealthCheckReport FromResults(IEnumerable<HealthCheckResult> results, string version)
    {
        var resultList = results.ToList();
        var overallStatus = HealthStatus.Healthy;

        if (resultList.Any(r => r.Status == HealthStatus.Unhealthy))
            overallStatus = HealthStatus.Unhealthy;
        else if (resultList.Any(r => r.Status == HealthStatus.Degraded))
            overallStatus = HealthStatus.Degraded;

        return new HealthCheckReport
        {
            OverallStatus = overallStatus,
            GeneratedAt = DateTime.UtcNow,
            TotalDuration = TimeSpan.FromMilliseconds(resultList.Sum(r => r.Duration.TotalMilliseconds)),
            Version = version,
            Results = resultList
        };
    }

    /// <summary>
    /// Returns true if all checks passed (Healthy or Degraded).
    /// </summary>
    public bool IsOperational => OverallStatus != HealthStatus.Unhealthy;
}

/// <summary>
/// Health status enumeration.
/// </summary>
public enum HealthStatus
{
    /// <summary>Component is functioning normally.</summary>
    Healthy = 0,

    /// <summary>Component is functioning but with warnings.</summary>
    Degraded = 1,

    /// <summary>Component is not functioning.</summary>
    Unhealthy = 2
}
