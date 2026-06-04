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
/// Coordinates temp-path uploads and remote replacement without depending on a live SFTP server.
/// </summary>
public static class SftpAtomicUpload
{
    /// <summary>
    /// Creates a unique temporary remote path next to the final remote path.
    /// </summary>
    public static string CreateRemoteTempPath(string finalRemotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalRemotePath);

        return $"{finalRemotePath}.{Guid.NewGuid():N}.part";
    }

    /// <summary>
    /// Replaces the final remote path with the uploaded temp path.
    /// </summary>
    public static void CommitRename(
        string tempRemotePath,
        string finalRemotePath,
        Action<string, string> atomicRename,
        Action<string, string> plainRename,
        Action<string> deleteFinalIfExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRemotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalRemotePath);
        ArgumentNullException.ThrowIfNull(atomicRename);
        ArgumentNullException.ThrowIfNull(plainRename);
        ArgumentNullException.ThrowIfNull(deleteFinalIfExists);

        try
        {
            atomicRename(tempRemotePath, finalRemotePath);
            return;
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"SFTP atomic rename unavailable for '{finalRemotePath}', falling back to replace: {ex.Message}");
        }

        deleteFinalIfExists(finalRemotePath);
        plainRename(tempRemotePath, finalRemotePath);
    }

    /// <summary>
    /// Deletes an abandoned remote temp path without touching the final remote path.
    /// </summary>
    public static void Rollback(string tempRemotePath, Action<string> deleteTemp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRemotePath);
        ArgumentNullException.ThrowIfNull(deleteTemp);

        try
        {
            deleteTemp(tempRemotePath);
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"SFTP temp upload rollback failed for '{tempRemotePath}': {ex.Message}");
        }
    }
}
