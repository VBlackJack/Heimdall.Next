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
using System.Text.Json;

namespace Heimdall.Core.Discovery;

/// <summary>
/// Persists and loads network scan snapshots to enable historical comparison
/// and diff analysis between scan runs.
/// </summary>
public static class ScanHistoryManager
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string GetScanDir() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "network-scans");

    /// <summary>
    /// Persists a scan snapshot to the history directory as JSON.
    /// </summary>
    public static async Task SaveSnapshotAsync(NetworkScanSnapshot snapshot)
    {
        var dir = GetScanDir();
        Directory.CreateDirectory(dir);
        var fileName = $"scan_{snapshot.Timestamp:yyyyMMdd_HHmmss}_{snapshot.Profile.Subnet.Replace('/', '-')}.json";
        var json = JsonSerializer.Serialize(snapshot, SerializeOptions);
        await File.WriteAllTextAsync(Path.Combine(dir, fileName), json).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists all saved scan snapshots ordered by most recent first.
    /// </summary>
    public static List<(string FileName, DateTime Timestamp, string Subnet)> ListSnapshots()
    {
        var dir = GetScanDir();
        if (!Directory.Exists(dir)) return [];

        return Directory.GetFiles(dir, "scan_*.json")
            .Select(f =>
            {
                try
                {
                    var json = File.ReadAllText(f);
                    var snap = JsonSerializer.Deserialize<NetworkScanSnapshot>(json, DeserializeOptions);
                    return snap is not null ? (Path.GetFileName(f), snap.Timestamp, snap.Profile.Subnet) : default;
                }
                catch { return default; }
            })
            .Where(x => x != default)
            .OrderByDescending(x => x.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Loads a specific scan snapshot from a history file.
    /// </summary>
    public static NetworkScanSnapshot? LoadSnapshot(string fileName)
    {
        var path = Path.Combine(GetScanDir(), fileName);
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<NetworkScanSnapshot>(json, DeserializeOptions);
    }

    /// <summary>
    /// Computes the differences between two scan snapshots: new hosts, removed hosts,
    /// and per-host changes (ports, hostname, role).
    /// </summary>
    public static ScanDiff ComputeDiff(NetworkScanSnapshot older, NetworkScanSnapshot newer)
    {
        var oldIps = older.Hosts.ToDictionary(h => h.IpAddress);
        var newIps = newer.Hosts.ToDictionary(h => h.IpAddress);

        var newHosts = newer.Hosts.Where(h => !oldIps.ContainsKey(h.IpAddress)).ToList();
        var removedHosts = older.Hosts.Where(h => !newIps.ContainsKey(h.IpAddress)).ToList();

        var modified = new List<(HostScanResult Old, HostScanResult New, List<string> Changes)>();
        foreach (var newHost in newer.Hosts)
        {
            if (!oldIps.TryGetValue(newHost.IpAddress, out var oldHost)) continue;

            var changes = new List<string>();

            var oldPorts = new HashSet<int>(oldHost.Services.Where(s => s.IsOpen).Select(s => s.Port));
            var newPorts = new HashSet<int>(newHost.Services.Where(s => s.IsOpen).Select(s => s.Port));

            foreach (var p in newPorts.Except(oldPorts))
                changes.Add($"+port:{p}");
            foreach (var p in oldPorts.Except(newPorts))
                changes.Add($"-port:{p}");

            if (oldHost.Hostname != newHost.Hostname)
                changes.Add($"hostname: {oldHost.Hostname ?? "(none)"} -> {newHost.Hostname ?? "(none)"}");

            if (oldHost.PrimaryRole?.Role != newHost.PrimaryRole?.Role)
                changes.Add($"role: {oldHost.PrimaryRole?.Role ?? "Unknown"} -> {newHost.PrimaryRole?.Role ?? "Unknown"}");

            if (changes.Count > 0)
                modified.Add((oldHost, newHost, changes));
        }

        return new ScanDiff(newHosts, removedHosts, modified);
    }
}
