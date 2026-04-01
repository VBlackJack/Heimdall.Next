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
using System.IO;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Logging;
using TwinShell.Core.Interfaces;

namespace TwinShell.Infrastructure.Services;

/// <summary>
/// Service for performing health checks on application components.
/// Used for smoke tests, startup verification, and monitoring.
/// </summary>
public class HealthCheckService : IHealthCheckService
{
    private readonly IActionRepository _actionRepository;
    private readonly ISettingsService _settingsService;
    private readonly IGitSyncService? _gitSyncService;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly string _appDataPath;

    public HealthCheckService(
        IActionRepository actionRepository,
        ISettingsService settingsService,
        ILogger<HealthCheckService> logger,
        IGitSyncService? gitSyncService = null)
    {
        _actionRepository = actionRepository;
        _settingsService = settingsService;
        _gitSyncService = gitSyncService;
        _logger = logger;

        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TwinShell");
    }

    public async Task<HealthCheckReport> CheckAllAsync()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var results = new List<HealthCheckResult>();

        results.Add(await CheckDatabaseAsync());
        results.Add(await CheckConfigurationAsync());
        results.Add(await CheckFileSystemAsync());
        results.Add(await CheckGitSyncAsync());
        results.Add(await CheckExternalServicesAsync());

        var report = HealthCheckReport.FromResults(results, version);

        _logger.LogInformation("Health check completed: {Status} ({Count} checks)",
            report.OverallStatus, results.Count);

        return report;
    }

    public async Task<HealthCheckResult> CheckDatabaseAsync()
    {
        const string component = "Database";
        var sw = Stopwatch.StartNew();

        try
        {
            // Try to query the database
            var actions = await _actionRepository.GetAllAsync();
            sw.Stop();

            var actionCount = actions.Count();
            var data = new Dictionary<string, object>
            {
                ["actionCount"] = actionCount,
                ["responseTimeMs"] = sw.ElapsedMilliseconds
            };

            if (sw.ElapsedMilliseconds > 5000)
            {
                return HealthCheckResult.Degraded(component,
                    $"Database responding slowly ({sw.ElapsedMilliseconds}ms)", data);
            }

            return HealthCheckResult.Healthy(component,
                $"Database operational with {actionCount} actions", data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Database health check failed");
            return HealthCheckResult.Unhealthy(component, $"Database error: {ex.Message}", ex);
        }
        finally
        {
            sw.Stop();
        }
    }

    public async Task<HealthCheckResult> CheckConfigurationAsync()
    {
        const string component = "Configuration";
        var sw = Stopwatch.StartNew();

        try
        {
            var settings = await Task.Run(() => _settingsService.CurrentSettings);
            sw.Stop();

            var warnings = new List<string>();
            var data = new Dictionary<string, object>();

            if (settings != null)
            {
                data["theme"] = settings.Theme.ToString();
                data["language"] = settings.CultureCode ?? "en";

                // Check for potential issues
                if (string.IsNullOrEmpty(settings.CultureCode))
                    warnings.Add("Language not configured");

                if (warnings.Count > 0)
                {
                    data["warnings"] = warnings;
                    return HealthCheckResult.Degraded(component,
                        $"Configuration loaded with {warnings.Count} warning(s)", data);
                }

                return HealthCheckResult.Healthy(component, "Configuration valid", data);
            }

            return HealthCheckResult.Degraded(component, "Using default configuration", data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Configuration health check failed");
            return HealthCheckResult.Unhealthy(component, $"Configuration error: {ex.Message}", ex);
        }
    }

    public async Task<HealthCheckResult> CheckGitSyncAsync()
    {
        const string component = "GitSync";
        var sw = Stopwatch.StartNew();

        try
        {
            if (_gitSyncService == null)
            {
                return HealthCheckResult.Healthy(component, "Git sync service not configured");
            }

            if (!_gitSyncService.IsConfigured)
            {
                return HealthCheckResult.Healthy(component, "Git sync not enabled");
            }

            // Check if repository is valid
            var status = await _gitSyncService.GetRepositoryStatusAsync();
            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["isConfigured"] = _gitSyncService.IsConfigured,
                ["responseTimeMs"] = sw.ElapsedMilliseconds
            };

            if (status != null)
            {
                data["branch"] = status.CurrentBranch ?? "unknown";
                data["hasRemote"] = !string.IsNullOrEmpty(status.RemoteUrl);
            }

            return HealthCheckResult.Healthy(component, "Git repository accessible", data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Git sync health check failed");

            // Git sync failures are degraded, not unhealthy (app can work without it)
            return HealthCheckResult.Degraded(component, $"Git sync unavailable: {ex.Message}");
        }
    }

    public async Task<HealthCheckResult> CheckFileSystemAsync()
    {
        const string component = "FileSystem";
        var sw = Stopwatch.StartNew();

        try
        {
            var issues = new List<string>();
            var data = new Dictionary<string, object>();

            // Check app data directory
            if (!Directory.Exists(_appDataPath))
            {
                Directory.CreateDirectory(_appDataPath);
            }

            // Test write permissions
            var testFile = Path.Combine(_appDataPath, $"healthcheck-{Guid.NewGuid()}.tmp");
            try
            {
                await File.WriteAllTextAsync(testFile, "test");
                File.Delete(testFile);
                data["writePermission"] = true;
            }
            catch
            {
                issues.Add("No write permission to app data directory");
                data["writePermission"] = false;
            }

            // Check available disk space
            var drive = new DriveInfo(Path.GetPathRoot(_appDataPath) ?? "C:");
            var freeSpaceGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
            data["freeSpaceGb"] = Math.Round(freeSpaceGb, 2);

            if (freeSpaceGb < 1)
            {
                issues.Add("Less than 1 GB of free disk space");
            }

            sw.Stop();

            if (issues.Count > 0)
            {
                data["issues"] = issues;
                return HealthCheckResult.Degraded(component,
                    $"File system accessible with {issues.Count} issue(s)", data);
            }

            return HealthCheckResult.Healthy(component,
                $"File system OK ({freeSpaceGb:F1} GB free)", data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "File system health check failed");
            return HealthCheckResult.Unhealthy(component, $"File system error: {ex.Message}", ex);
        }
    }

    public async Task<HealthCheckResult> CheckExternalServicesAsync()
    {
        const string component = "ExternalServices";
        var sw = Stopwatch.StartNew();

        try
        {
            var data = new Dictionary<string, object>();
            var warnings = new List<string>();

            // Check PowerShell Gallery connectivity (optional)
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            try
            {
                var response = await httpClient.GetAsync("https://www.powershellgallery.com/api/v2/");
                data["powershellGallery"] = response.IsSuccessStatusCode ? "available" : "unavailable";

                if (!response.IsSuccessStatusCode)
                {
                    warnings.Add("PowerShell Gallery not reachable");
                }
            }
            catch
            {
                data["powershellGallery"] = "unreachable";
                warnings.Add("PowerShell Gallery connection failed");
            }

            sw.Stop();
            data["responseTimeMs"] = sw.ElapsedMilliseconds;

            if (warnings.Count > 0)
            {
                data["warnings"] = warnings;
                return HealthCheckResult.Degraded(component,
                    "Some external services unavailable", data);
            }

            return HealthCheckResult.Healthy(component, "External services available", data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "External services health check failed");
            return HealthCheckResult.Degraded(component, $"Check failed: {ex.Message}");
        }
    }

    public async Task<HealthCheckResult> QuickStartupCheckAsync()
    {
        const string component = "StartupCheck";
        var sw = Stopwatch.StartNew();

        try
        {
            // Quick checks only - database and file system
            var dbCheck = await CheckDatabaseAsync();
            var fsCheck = await CheckFileSystemAsync();

            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["database"] = dbCheck.Status.ToString(),
                ["fileSystem"] = fsCheck.Status.ToString(),
                ["totalTimeMs"] = sw.ElapsedMilliseconds
            };

            if (dbCheck.Status == HealthStatus.Unhealthy || fsCheck.Status == HealthStatus.Unhealthy)
            {
                return HealthCheckResult.Unhealthy(component, "Critical components unavailable",
                    dbCheck.Exception ?? fsCheck.Exception);
            }

            if (dbCheck.Status == HealthStatus.Degraded || fsCheck.Status == HealthStatus.Degraded)
            {
                return HealthCheckResult.Degraded(component, "Some components degraded", data);
            }

            return HealthCheckResult.Healthy(component, $"Startup check passed ({sw.ElapsedMilliseconds}ms)", data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Quick startup check failed");
            return HealthCheckResult.Unhealthy(component, $"Startup check failed: {ex.Message}", ex);
        }
    }
}
