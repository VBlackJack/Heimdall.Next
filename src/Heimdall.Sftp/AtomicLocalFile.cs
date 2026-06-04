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

namespace Heimdall.Sftp;

/// <summary>
/// Provides same-directory temp-file writes for atomic local file replacement.
/// </summary>
public static class AtomicLocalFile
{
    /// <summary>
    /// Creates a unique temporary path next to the final destination path.
    /// </summary>
    /// <param name="finalPath">The final file path that will be replaced after a successful write.</param>
    /// <returns>A unique <c>.part</c> path in the same directory as <paramref name="finalPath"/>.</returns>
    public static string CreateTempPath(string finalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);

        string fileName = Path.GetFileName(finalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Final path must include a file name.", nameof(finalPath));
        }

        string? directory = Path.GetDirectoryName(finalPath);
        string tempFileName = $"{fileName}.{Guid.NewGuid():N}.part";
        return string.IsNullOrEmpty(directory)
            ? tempFileName
            : Path.Combine(directory, tempFileName);
    }

    /// <summary>
    /// Atomically replaces the final file with the completed temporary file.
    /// </summary>
    public static void Commit(string tempPath, string finalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);

        File.Move(tempPath, finalPath, overwrite: true);
    }

    /// <summary>
    /// Removes an abandoned temporary file without touching the final path.
    /// </summary>
    public static void Rollback(string tempPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);

        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"AtomicLocalFile rollback failed for '{tempPath}': {ex.Message}");
        }
    }
}
