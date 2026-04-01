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

namespace TwinShell.Core.Constants;

/// <summary>
/// Performance Service Level Agreement (SLA) constants.
/// Defines latency targets and monitoring thresholds for application operations.
/// </summary>
public static class PerformanceConstants
{
    /// <summary>
    /// Target latency for search operations (milliseconds).
    /// SLA: Search results should return within 200ms for up to 1000 actions.
    /// </summary>
    public const int SearchLatencyTargetMs = 200;

    /// <summary>
    /// Target latency for UI load operations (milliseconds).
    /// SLA: Main window should be interactive within 500ms.
    /// </summary>
    public const int UiLoadLatencyTargetMs = 500;

    /// <summary>
    /// Warning threshold for database queries (milliseconds).
    /// Queries exceeding this threshold will be logged as warnings.
    /// </summary>
    public const int DatabaseQueryWarningMs = 5000;

    /// <summary>
    /// Target latency for action list rendering (milliseconds).
    /// SLA: Action list should render within 300ms for up to 500 visible items.
    /// </summary>
    public const int ActionListRenderTargetMs = 300;

    /// <summary>
    /// Target latency for favorites toggle operation (milliseconds).
    /// SLA: Favorite toggle should complete within 100ms.
    /// </summary>
    public const int FavoritesToggleTargetMs = 100;

    /// <summary>
    /// Target latency for clipboard copy operation (milliseconds).
    /// SLA: Clipboard operations should complete within 50ms.
    /// </summary>
    public const int ClipboardCopyTargetMs = 50;

    /// <summary>
    /// Target latency for settings save operation (milliseconds).
    /// SLA: Settings should be persisted within 200ms.
    /// </summary>
    public const int SettingsSaveTargetMs = 200;

    /// <summary>
    /// Target latency for backup creation (seconds).
    /// SLA: Full backup should complete within 60 seconds.
    /// </summary>
    public const int BackupCreationTargetSeconds = 60;

    /// <summary>
    /// Target latency for health check (milliseconds).
    /// SLA: Full health check should complete within 5000ms.
    /// </summary>
    public const int HealthCheckTargetMs = 5000;

    /// <summary>
    /// Maximum recommended number of actions for optimal performance.
    /// Beyond this count, consider enabling pagination.
    /// </summary>
    public const int MaxActionsForOptimalPerformance = 1000;

    /// <summary>
    /// Recommended batch size for large operations.
    /// Used for import/export and bulk database operations.
    /// </summary>
    public const int RecommendedBatchSize = 100;
}
