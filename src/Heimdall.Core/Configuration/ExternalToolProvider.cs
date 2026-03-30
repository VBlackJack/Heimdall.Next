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
/// Describes a third-party CLI tool detected on the local machine
/// (e.g. NirSoft CurrPorts, Sysinternals PsExec).
/// </summary>
public sealed class ExternalToolInfo
{
    /// <summary>Unique identifier within its provider (e.g. "PSEXEC", "CURRPORTS").</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name (e.g. "PsExec").</summary>
    public required string Name { get; init; }

    /// <summary>Absolute path to the detected executable.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Provider that discovered this tool (e.g. "Sysinternals", "NirSoft").</summary>
    public required string ProviderName { get; init; }

    /// <summary>Short description for the UI card.</summary>
    public string? DescriptionKey { get; init; }

    /// <summary>CLI arguments template. Supports {Host}, {Port} placeholders.</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Output parsing mode for structured data capture.</summary>
    public OutputFormat OutputFormat { get; init; } = OutputFormat.Text;

    /// <summary>True if the tool requires administrator privileges.</summary>
    public bool RequiresElevation { get; init; }

    /// <summary>XAML geometry resource key for the icon (e.g. "Geo.Tool.WakeOnLan").</summary>
    public string? IconResourceKey { get; init; }
}

/// <summary>
/// Output format supported by external CLI tools.
/// </summary>
public enum OutputFormat
{
    /// <summary>Plain text (human-readable, not parsed).</summary>
    Text,

    /// <summary>Comma-separated values (NirSoft /scomma, Sysinternals -c).</summary>
    Csv,

    /// <summary>XML output (NirSoft /sxml).</summary>
    Xml,

    /// <summary>JSON output (NirSoft /sjson, newer tools only).</summary>
    Json,

    /// <summary>Tab-delimited (NirSoft /stab).</summary>
    TabDelimited
}

/// <summary>
/// Detects and catalogs third-party CLI tools from a specific vendor
/// (e.g. NirSoft, Sysinternals) on the local machine.
/// </summary>
public interface IExternalToolProvider
{
    /// <summary>Provider display name (e.g. "Sysinternals", "NirSoft").</summary>
    string Name { get; }

    /// <summary>
    /// Scans known paths and user-configured directories for available tools.
    /// </summary>
    /// <param name="customSearchPaths">Additional directories supplied by user settings.</param>
    /// <returns>List of detected tools with their executable paths and metadata.</returns>
    IReadOnlyList<ExternalToolInfo> Scan(IEnumerable<string>? customSearchPaths = null);
}
