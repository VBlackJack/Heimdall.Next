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
/// Detects CLI-capable tools distributed by the NanaRun project.
/// Only stable command-line components are included for now.
/// </summary>
public sealed class NanaRunToolProvider : IExternalToolProvider
{
    public string Name => "NanaRun";

    /// <summary>
    /// Known NanaRun project tools that expose a usable CLI.
    /// The upstream README currently documents MinSudo and SynthRdp.
    /// </summary>
    private static readonly ToolTemplate[] s_templates =
    [
        new("MINSUDO", "MinSudo", "MinSudo.exe", "--Help", OutputFormat.Text, false, "ExtToolDescMinSudo", "Geo.Tool.CommandLibrary"),
        new("SYNTHRDP", "SynthRdp", "SynthRdp.exe", "Help", OutputFormat.Text, false, "ExtToolDescSynthRdp", "Geo.Tool.ServiceStatusDashboard"),
    ];

    /// <summary>
    /// Standard directories where NanaRun binaries may be found.
    /// </summary>
    private static readonly string[] s_defaultSearchPaths =
    [
        @"C:\NanaRun",
        @"C:\Tools\NanaRun",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NanaRun"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NanaRun"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
    ];

    public IReadOnlyList<ExternalToolInfo> Scan(IEnumerable<string>? customSearchPaths = null)
    {
        var searchDirs = BuildSearchDirectories(customSearchPaths);
        var results = new List<ExternalToolInfo>();

        foreach (var template in s_templates)
        {
            var exePath = FindExecutable(template.FileName, searchDirs);
            if (exePath is null) continue;

            results.Add(new ExternalToolInfo
            {
                Id = template.Id,
                Name = template.Name,
                ExecutablePath = exePath,
                ProviderName = Name,
                DescriptionKey = template.DescriptionKey,
                Arguments = template.Arguments,
                OutputFormat = template.Format,
                RequiresElevation = template.RequiresElevation,
                IconResourceKey = template.IconResourceKey,
            });
        }

        return results;
    }

    private static List<SearchDirectory> BuildSearchDirectories(IEnumerable<string>? customPaths)
    {
        var dirs = new List<SearchDirectory>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDirectories(dirs, seen, s_defaultSearchPaths, searchDepth: 2);
        AddDirectories(dirs, seen, customPaths, searchDepth: 2);

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            AddDirectories(
                dirs,
                seen,
                pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries),
                searchDepth: 0);
        }

        return dirs;
    }

    private static void AddDirectories(
        List<SearchDirectory> destination,
        HashSet<string> seen,
        IEnumerable<string>? paths,
        int searchDepth)
    {
        if (paths is null) return;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!seen.Add(path)) continue;
            destination.Add(new SearchDirectory(path, searchDepth));
        }
    }

    private static string? FindExecutable(string fileName, IReadOnlyList<SearchDirectory> searchDirs)
    {
        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir.Path)) continue;

            foreach (var candidateDir in EnumerateCandidateDirectories(dir.Path, dir.SearchDepth))
            {
                var candidate = Path.Combine(candidateDir, fileName);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string root, int depth)
    {
        yield return root;
        if (depth <= 0) yield break;

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(root);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var nested in EnumerateCandidateDirectories(child, depth - 1))
                yield return nested;
        }
    }

    private readonly record struct SearchDirectory(string Path, int SearchDepth);

    private readonly record struct ToolTemplate(
        string Id,
        string Name,
        string FileName,
        string Arguments,
        OutputFormat Format,
        bool RequiresElevation,
        string DescriptionKey,
        string IconResourceKey);
}
