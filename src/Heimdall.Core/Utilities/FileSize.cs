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

namespace Heimdall.Core.Utilities;

/// <summary>
/// Formats byte counts into human-readable size strings.
/// </summary>
public static class FileSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// Formats a byte count into a human-readable size string (e.g., "1.5 MB").
    /// </summary>
    public static string Format(long bytes)
    {
        int index = 0;
        double size = bytes;

        while (size >= 1024 && index < Units.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return index == 0 ? $"{size:F0} {Units[index]}" : $"{size:F1} {Units[index]}";
    }
}
