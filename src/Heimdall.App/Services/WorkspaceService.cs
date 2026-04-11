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

using System.IO;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Services;

/// <summary>
/// Persists and restores the list of open sessions across application restarts.
/// </summary>
public static class WorkspaceService
{
    private static readonly string WorkspacePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "workspace.json");

    private static readonly SemaphoreSlim _fileLock = new(1, 1);

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public static async Task SaveAsync(IEnumerable<WorkspaceSessionDto> sessions)
    {
        var dto = new WorkspaceDto
        {
            Sessions = sessions.ToList(),
            SavedAt = DateTime.UtcNow
        };
        var json = System.Text.Json.JsonSerializer.Serialize(dto, JsonOptions);

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(WorkspacePath, json).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }

        Core.Logging.FileLogger.Info($"Workspace saved: {dto.Sessions.Count} session(s)");
    }

    public static async Task<WorkspaceDto?> LoadAsync()
    {
        if (!File.Exists(WorkspacePath)) return null;

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(WorkspacePath).ConfigureAwait(false);
            return System.Text.Json.JsonSerializer.Deserialize<WorkspaceDto>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"Workspace load failed: {ex.Message}");
            return null;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public static void Delete()
    {
        try
        {
            File.Delete(WorkspacePath);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Workspace delete failed: {ex.Message}");
        }
    }
}
