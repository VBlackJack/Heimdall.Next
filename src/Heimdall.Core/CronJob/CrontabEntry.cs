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

namespace Heimdall.Core.CronJob;

/// <summary>
/// A single parsed crontab schedule line with its original raw line.
/// </summary>
public sealed record CrontabEntry(
    string Minute,
    string Hour,
    string DayOfMonth,
    string Month,
    string DayOfWeek,
    string Command,
    string RawLine)
{
    public string[] ScheduleFields() => [Minute, Hour, DayOfMonth, Month, DayOfWeek];
}
