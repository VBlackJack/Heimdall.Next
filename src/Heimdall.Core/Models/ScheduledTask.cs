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

using CommunityToolkit.Mvvm.ComponentModel;

namespace Heimdall.Core.Models;

/// <summary>
/// Scheduled connection task that triggers server connections at configured times.
/// </summary>
public partial class ScheduledTask : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _serverId = string.Empty;

    [ObservableProperty]
    private string _serverName = string.Empty;

    /// <summary>
    /// Scheduled time in HH:mm format.
    /// </summary>
    [ObservableProperty]
    private string _scheduledTime = string.Empty;

    [ObservableProperty]
    private RecurrenceType _recurrence = RecurrenceType.Once;

    /// <summary>
    /// Days of week for weekly/custom recurrence (0 = Sunday, 6 = Saturday).
    /// </summary>
    [ObservableProperty]
    private DayOfWeek[] _daysOfWeek = [];

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private DateTime _createdAt = DateTime.UtcNow;

    [ObservableProperty]
    private DateTime? _lastTriggeredAt;

    [ObservableProperty]
    private DateTime? _nextTriggerAt;
}
