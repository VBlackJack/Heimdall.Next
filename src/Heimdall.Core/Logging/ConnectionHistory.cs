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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Heimdall.Core.Logging;

/// <summary>
/// Records connection open/close events to a JSONL file for audit purposes.
/// Thread-safe via a write lock. Auto-rotates when the file exceeds 1 MB.
/// </summary>
public static class ConnectionHistory
{
    private static readonly object WriteLock = new();
    private static string? _logDirectory;

    private const string FileName = "connection-history.jsonl";
    private const string RotatedFileName = "connection-history.1.jsonl";
    private const long MaxFileSizeBytes = 1_048_576; // 1 MB

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Sets the log directory. Must be called before recording events.
    /// Uses the same directory as <see cref="FileLogger"/>.
    /// </summary>
    public static void Initialize(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
    }

    /// <summary>
    /// Records a connection open event.
    /// </summary>
    public static void RecordConnect(string serverId, string displayName, string connectionType)
    {
        WriteEntry("Connected", serverId, displayName, connectionType);
    }

    /// <summary>
    /// Records a connection close event.
    /// </summary>
    public static void RecordDisconnect(string serverId, string displayName, string connectionType)
    {
        WriteEntry("Disconnected", serverId, displayName, connectionType);
    }

    private static void WriteEntry(string action, string serverId, string displayName, string connectionType)
    {
        if (string.IsNullOrEmpty(_logDirectory))
        {
            return;
        }

        var entry = new ConnectionHistoryEntry
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Action = action,
            ServerId = serverId,
            DisplayName = displayName,
            ConnectionType = connectionType
        };

        var json = JsonSerializer.Serialize(entry, SerializerOptions);

        lock (WriteLock)
        {
            try
            {
                var filePath = Path.Combine(_logDirectory, FileName);

                // Auto-rotate if file exceeds size limit
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length >= MaxFileSizeBytes)
                    {
                        var rotatedPath = Path.Combine(_logDirectory, RotatedFileName);
                        if (File.Exists(rotatedPath))
                        {
                            File.Delete(rotatedPath);
                        }
                        File.Move(filePath, rotatedPath);
                    }
                }

                File.AppendAllText(filePath, json + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch
            {
                // Logging must never crash the application
            }
        }
    }

    private sealed class ConnectionHistoryEntry
    {
        public string Timestamp { get; set; } = "";
        public string Action { get; set; } = "";
        public string ServerId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ConnectionType { get; set; } = "";
    }
}
