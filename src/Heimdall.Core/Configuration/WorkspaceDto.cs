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

namespace Heimdall.Core.Configuration;

/// <summary>
/// Persisted workspace state: the list of open sessions at the time the application closed.
/// </summary>
public sealed class WorkspaceDto
{
    public List<WorkspaceSessionDto> Sessions { get; set; } = new();
    public DateTime SavedAt { get; set; }
}

/// <summary>
/// A single session entry within a persisted workspace.
/// </summary>
public sealed class WorkspaceSessionDto
{
    public string ServerId { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string Protocol { get; set; } = "";
}
