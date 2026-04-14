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

using System.Collections.ObjectModel;
using Heimdall.Core.Models;

namespace Heimdall.App.ViewModels.CommandPalette;

/// <summary>
/// Search partial of <see cref="CommandPaletteViewModel"/>: the
/// <see cref="OnSearchTextChanged"/> generated handler plus the fuzzy
/// scoring, tool-command parsing and ad-hoc SSH parsing helpers.
/// </summary>
public sealed partial class CommandPaletteViewModel
{
    /// <summary>
    /// Generated partial: recomputes the palette result set every time the
    /// user types in the search TextBox (the XAML binding uses
    /// <c>UpdateSourceTrigger=PropertyChanged</c>). When the query is
    /// empty, seeds the results from active sessions (split mode), recent
    /// tools and the top N servers.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        var query = value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            var initialResults = new List<ServerItemViewModel>();

            // In split mode, show active sessions first (merge candidates)
            if (_splitPaletteSession is not null)
            {
                foreach (var s in _main.Connection.ActiveSessions)
                {
                    // Skip the session being split
                    if (s == _splitPaletteSession) continue;
                    if (s.HostControl is null) continue;

                    initialResults.Add(new ServerItemViewModel
                    {
                        Id = $"session-{s.ServerId}",
                        DisplayName = $"\u2194 {s.Title}",
                        RemoteServer = _localizer["SplitMergeActiveSession"],
                        ConnectionType = s.ConnectionType ?? "",
                        Group = _localizer["SplitActiveSessionsHeader"]
                    });
                }
            }

            // In split mode, boost previously paired servers to the top
            var boostedIds = new HashSet<string>(StringComparer.Ordinal);
            if (_splitPaletteSession is not null)
            {
                var sessionServerId = _splitPaletteSession.OriginalServerId;
                var partners = _main.Split.LayoutMemory.FindAllPartners(sessionServerId);
                foreach (var entry in partners)
                {
                    var partnerId = string.Equals(entry.PrimaryServerId, sessionServerId, StringComparison.Ordinal)
                        ? entry.SecondaryServerId
                        : entry.PrimaryServerId;

                    var serverVm = _main.ServerList.Servers.FirstOrDefault(
                        s => string.Equals(s.Id, partnerId, StringComparison.Ordinal));
                    if (serverVm is not null && boostedIds.Add(partnerId))
                    {
                        initialResults.Add(serverVm);
                    }
                }
            }

            // Show servers (skip already-boosted ones).
            // In split mode, show ALL servers so every treeview entry is reachable.
            // In normal palette mode, limit to 10 recent servers.
            var availableServers = _main.ServerList.Servers
                .Where(s => !boostedIds.Contains(s.Id));
            if (_splitPaletteSession is null)
                availableServers = availableServers.Take(10);
            initialResults.AddRange(availableServers);

            // Then recent tools at the bottom (if any)
            foreach (var toolId in _main.RecentToolIds)
            {
                var desc = _toolRegistry.GetById(toolId);
                if (desc is not null)
                {
                    initialResults.Add(new ServerItemViewModel
                    {
                        Id = $"tool-{desc.Id.ToLowerInvariant()}",
                        DisplayName = _localizer[desc.LabelKey],
                        ConnectionType = desc.ToolType,
                        Group = _localizer["PaletteRecentToolsHeader"]
                    });
                }
            }

            Results = new ObservableCollection<ServerItemViewModel>(initialResults);
            SelectedItem = Results.FirstOrDefault();
            return;
        }

        // Check for tool commands first (e.g., "subnet 192.168.1.0/24", "hash", "tools")
        var toolItems = TryParseToolCommand(query);
        if (toolItems.Count > 0)
        {
            Results = new ObservableCollection<ServerItemViewModel>(toolItems);
            SelectedItem = Results.FirstOrDefault();
            return;
        }

        var matches = _main.ServerList.Servers
            .Select(s => (Server: s, Score: FuzzyScore(s, query)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Server)
            .Take(15)
            .ToList();

        // Ad-hoc SSH URL parsing: "ssh user@host" or "user@host:port"
        var adHoc = TryParseAdHocSsh(query);
        if (adHoc is not null && matches.Count == 0)
        {
            matches.Insert(0, adHoc);
        }

        // Bare IP / hostname: propose SSH and RDP ad-hoc connections
        if (matches.Count == 0 && LooksLikeHostOrIp(query))
        {
            matches.Add(new ServerItemViewModel
            {
                Id = $"adhoc-ssh-{query}",
                DisplayName = _localizer.Format("QuickConnectSshTo", query),
                RemoteServer = query,
                Endpoint = query,
                ConnectionType = "SSH",
                Group = ""
            });
            matches.Add(new ServerItemViewModel
            {
                Id = $"adhoc-rdp-{query}",
                DisplayName = _localizer.Format("QuickConnectRdpTo", query),
                RemoteServer = query,
                Endpoint = query,
                ConnectionType = "RDP",
                Group = ""
            });
        }

        Results = new ObservableCollection<ServerItemViewModel>(matches);
        SelectedItem = Results.FirstOrDefault();
    }

    /// <summary>
    /// Scores how well a server matches a query using fuzzy matching.
    /// Returns 0 for no match. Higher = better match.
    /// </summary>
    private static int FuzzyScore(ServerItemViewModel server, string query)
    {
        int best = 0;
        best = Math.Max(best, FuzzyScoreString(server.DisplayName, query));
        best = Math.Max(best, FuzzyScoreString(server.RemoteServer ?? "", query));
        best = Math.Max(best, FuzzyScoreString(server.Group ?? "", query) / 2);
        best = Math.Max(best, FuzzyScoreString(server.Username ?? "", query) / 2);
        best = Math.Max(best, FuzzyScoreString(server.ConnectionType ?? "", query) / 2);
        best = Math.Max(best, FuzzyScoreString(server.Environment ?? "", query) / 2);
        best = Math.Max(best, FuzzyScoreString(server.Tags ?? "", query) / 2);
        best = Math.Max(best, FuzzyScoreString(server.ProjectName ?? "", query) / 2);
        return best;
    }

    private static int FuzzyScoreString(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return 0;

        // Exact prefix match = highest score
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 100 + (query.Length * 10);

        // Contains = good score
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 50 + (query.Length * 5);

        // Fuzzy: all query chars appear in order (non-contiguous)
        int qi = 0;
        int consecutive = 0;
        int score = 0;

        for (int ti = 0; ti < text.Length && qi < query.Length; ti++)
        {
            if (char.ToLowerInvariant(text[ti]) == char.ToLowerInvariant(query[qi]))
            {
                qi++;
                consecutive++;
                score += consecutive * 2; // Consecutive chars score more
            }
            else
            {
                consecutive = 0;
            }
        }

        return qi == query.Length ? score : 0;
    }

    /// <summary>
    /// Checks whether the palette query matches a tool command prefix.
    /// Returns matching tool palette items, or all tools when the query is
    /// <c>"tool"</c> / <c>"tools"</c>. Uses the centralized
    /// <see cref="ToolRegistry"/> instead of a static array.
    /// </summary>
    private List<ServerItemViewModel> TryParseToolCommand(string query)
    {
        var results = new List<ServerItemViewModel>();
        var lower = query.ToLowerInvariant();

        // Show all tools when user types "tool" or "tools", grouped by category
        if (lower is "tool" or "tools")
        {
            foreach (var descriptor in _toolRegistry.All)
            {
                results.Add(new ServerItemViewModel
                {
                    Id = $"tool-{descriptor.Id.ToLowerInvariant()}",
                    DisplayName = _localizer[descriptor.LabelKey],
                    ConnectionType = descriptor.ToolType,
                    Group = _localizer[descriptor.CategoryLabelKey]
                });
            }
            return results;
        }

        // Check if query starts with a known tool prefix
        foreach (var descriptor in _toolRegistry.All)
        {
            foreach (var prefix in descriptor.CommandPrefixes)
            {
                if (!lower.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var rest = query[prefix.Length..].Trim();
                string displayName;

                if (!string.IsNullOrEmpty(rest) && descriptor.LabelWithArgKey is not null)
                {
                    displayName = _localizer.Format(descriptor.LabelWithArgKey, rest);
                }
                else
                {
                    displayName = _localizer[descriptor.LabelKey];
                }

                results.Add(new ServerItemViewModel
                {
                    Id = $"tool-{descriptor.Id.ToLowerInvariant()}|{rest}",
                    DisplayName = displayName,
                    ConnectionType = descriptor.ToolType,
                    Group = _localizer["PaletteToolsSectionHeader"]
                });
                break;
            }

            if (results.Count > 0) break;
        }

        // Always search external tools (even when a built-in tool matched,
        // so user-defined tools with overlapping names remain discoverable).
        var extTools = _main.CurrentSettings?.ExternalTools ?? [];
        foreach (var ext in extTools)
        {
            if (string.IsNullOrWhiteSpace(ext.Name)) continue;

            if (FuzzyScoreString(ext.Name, query) > 0
                || ext.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new ServerItemViewModel
                {
                    Id = $"ext-tool-{ext.Name}",
                    DisplayName = ext.Name,
                    ConnectionType = "EXTERNAL",
                    Group = _localizer["PaletteExternalToolsHeader"]
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Returns true when the input looks like a bare IP address or hostname
    /// (no spaces, no protocol prefix, alphanumeric with dots and hyphens).
    /// </summary>
    private static bool LooksLikeHostOrIp(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Contains(' '))
            return false;
        return System.Net.IPAddress.TryParse(input, out _)
            || System.Text.RegularExpressions.Regex.IsMatch(
                input, @"^[a-zA-Z0-9][a-zA-Z0-9.\-]*$");
    }

    /// <summary>
    /// Attempts to parse an ad-hoc SSH URL from the palette search text.
    /// Supports: <c>ssh user@host</c>, <c>ssh user@host:port</c>,
    /// <c>user@host</c>. Returns a temporary
    /// <see cref="ServerItemViewModel"/> for connection, or <c>null</c>.
    /// </summary>
    private ServerItemViewModel? TryParseAdHocSsh(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var text = input.Trim();
        if (text.StartsWith("ssh ", StringComparison.OrdinalIgnoreCase))
        {
            text = text[4..].Trim();
        }

        // Match user@host or user@host:port
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"^([^@]+)@([^:]+)(?::(\d+))?$");

        if (!match.Success) return null;

        string user = match.Groups[1].Value;
        string host = match.Groups[2].Value;
        int port = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 22;

        var vm = new ServerItemViewModel
        {
            Id = $"adhoc-{Guid.NewGuid():N}",
            DisplayName = $"{user}@{host}:{port}",
            RemoteServer = host,
            ConnectionType = "SSH",
            Group = ""
        };
        return vm;
    }
}
