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

namespace Heimdall.App.ViewModels;

internal static class LocalDownloadPath
{
    public static bool TryResolveContained(
        string targetFolder,
        string fileName,
        out string localPath)
    {
        localPath = string.Empty;

        if (string.IsNullOrWhiteSpace(targetFolder)
            || string.IsNullOrWhiteSpace(fileName)
            || fileName is "." or ".."
            || fileName.Contains('/', StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            string root = Path.GetFullPath(targetFolder);
            string candidate = Path.GetFullPath(Path.Combine(root, fileName));
            string rootWithSeparator = EnsureTrailingSeparator(root);
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!candidate.StartsWith(rootWithSeparator, comparison))
            {
                return false;
            }

            localPath = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or IOException)
        {
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
