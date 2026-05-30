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

using System.Collections.Frozen;
using System.Net;
using System.Text.RegularExpressions;

namespace Heimdall.Core.Security;

/// <summary>
/// Validates user inputs against predefined security patterns to prevent
/// injection attacks (CWE-78 prevention). All patterns are compiled regexes
/// for optimal performance.
/// </summary>
public static class InputValidator
{
    /// <summary>Minimum valid port number.</summary>
    private const int MinPort = 1;

    /// <summary>Maximum valid port number.</summary>
    private const int MaxPort = 65535;

    /// <summary>Maximum FQDN length per DNS RFC.</summary>
    private const int MaxFqdnLength = 255;

    /// <summary>Maximum DNS label length per RFC.</summary>
    private const int MaxDnsLabelLength = 63;

    /// <summary>
    /// Pre-compiled validation patterns indexed by name.
    /// </summary>
    private static readonly FrozenDictionary<string, Regex> ValidationPatterns =
        new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase)
        {
            // FQDN or hostname: alphanumeric, dots, hyphens
            ["SshGateway"] = CompilePattern(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$"),

            // SSH username: alphanumeric, underscore, hyphen, dot, at, backslash
            ["SshUser"] = CompilePattern(@"^[a-zA-Z0-9._@\\-]+$"),

            // RDP username: user, DOMAIN\user, or user@domain.com
            ["Username"] = CompilePattern(@"^[a-zA-Z0-9_\-\.]+([\\@][a-zA-Z0-9_\-\.]+)?$"),

            // TunnelTarget: hostname:port format
            ["TunnelTarget"] = CompilePattern(@"^[a-zA-Z0-9\.\-]+:\d{1,5}$"),

            // IP Address (IPv4)
            ["IPv4"] = CompilePattern(
                @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$"),

            // Hostname: alphanumeric with dots and hyphens
            ["Hostname"] = CompilePattern(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$"),

            // Address: IP or hostname
            ["Address"] = CompilePattern(
                @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$|^[a-zA-Z][a-zA-Z0-9\-]*(\.[a-zA-Z0-9][a-zA-Z0-9\-]*)*$"),

            // Port numbers (used for LocalPort, RemotePort, etc.)
            ["LocalPort"] = CompilePattern(@"^\d{1,5}$"),
            ["RemotePort"] = CompilePattern(@"^\d{1,5}$"),
            ["Port"] = CompilePattern(@"^\d{1,5}$"),
        }.ToFrozenDictionary();

    /// <summary>
    /// Pattern names that require additional DNS validation (consecutive dots/hyphens,
    /// label lengths, total FQDN length).
    /// </summary>
    private static readonly FrozenSet<string> DnsValidatedPatterns =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SshGateway",
            "Hostname",
            "Address"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Compiled regex for detecting invalid DNS sequences.
    /// </summary>
    private static readonly Regex InvalidDnsSequence =
        new(@"(\.\.|\.-|-\.)", RegexOptions.Compiled);

    /// <summary>
    /// Validate a value against a named security pattern.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="patternName">
    /// Name of the pattern: SshGateway, SshUser, Username, Hostname, IPv4, Address,
    /// TunnelTarget, LocalPort, RemotePort, Port.
    /// </param>
    /// <returns>True if the value passes validation; false otherwise.</returns>
    public static bool Validate(string? value, string patternName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();

        if (!ValidationPatterns.TryGetValue(patternName, out Regex? regex))
            return false;

        try
        {
            if (!regex!.IsMatch(trimmed))
                return false;
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail closed: a pathological input that trips the ReDoS guard is rejected, never thrown.
            return false;
        }

        // Additional DNS validation for hostname-type patterns
        if (DnsValidatedPatterns.Contains(patternName))
        {
            if (!ValidateDns(trimmed))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Validate that a port number is in the valid TCP/UDP range (1-65535).
    /// </summary>
    /// <param name="port">The port number to validate.</param>
    /// <returns>True if the port is valid.</returns>
    public static bool ValidatePortRange(int port)
    {
        return port >= MinPort && port <= MaxPort;
    }

    /// <summary>
    /// Get the regex pattern string for a named validation pattern.
    /// </summary>
    /// <param name="patternName">Name of the pattern.</param>
    /// <returns>The regex pattern string, or null if the pattern name is unknown.</returns>
    public static string? GetPattern(string patternName)
    {
        return ValidationPatterns.TryGetValue(patternName, out Regex? regex)
            ? regex!.ToString()
            : null;
    }

    /// <summary>
    /// Get all available pattern names.
    /// </summary>
    /// <returns>An enumerable of pattern names.</returns>
    public static IEnumerable<string> GetPatternNames()
    {
        return ValidationPatterns.Keys;
    }

    /// <summary>
    /// Additional DNS validation that the base regex patterns cannot express cleanly:
    /// consecutive dots/hyphens, label length limits, total FQDN length.
    /// </summary>
    private static bool ValidateDns(string value)
    {
        // Check total length (max 255 for FQDN)
        if (value.Length > MaxFqdnLength)
            return false;

        // Check for consecutive dots or hyphens (invalid DNS)
        if (InvalidDnsSequence.IsMatch(value))
            return false;

        // Check individual label lengths and edge constraints
        string[] labels = value.Split('.');
        foreach (string label in labels)
        {
            if (label.Length > MaxDnsLabelLength)
                return false;

            // Labels cannot start or end with a hyphen
            if (label.StartsWith('-') || label.EndsWith('-'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Validate that a value matches the "Domain" pattern: FQDN, hostname, or IPv4.
    /// Convenience wrapper for <see cref="Validate"/> with the "Address" pattern,
    /// also accepting wildcard-stripped domains (e.g., from TLS certificate SANs).
    /// </summary>
    /// <param name="value">The domain/hostname/IP to validate.</param>
    /// <returns>True if the value is a valid domain or IP address.</returns>
    public static bool ValidateDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim().TrimStart('*', '.');
        return trimmed.Length > 0 && Validate(trimmed, "Address");
    }

    /// <summary>
    /// Validate an SSH target host as either a domain name or an IP address.
    /// </summary>
    /// <param name="host">The host value to validate.</param>
    /// <returns>True if the host can be used as an SSH target.</returns>
    public static bool IsValidSshHost(string host)
    {
        return !string.IsNullOrWhiteSpace(host)
            && (ValidateDomain(host) || IPAddress.TryParse(host, out _));
    }

    /// <summary>
    /// Sanitizes a value for safe inclusion in CSV cells by prefixing formula-triggering
    /// characters with a single quote, preventing spreadsheet formula injection.
    /// </summary>
    /// <param name="value">The cell value to sanitize.</param>
    /// <returns>The sanitized value safe for CSV output.</returns>
    public static string SanitizeCsvCell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
            return "'" + value;
        return value;
    }

    /// <summary>
    /// Escapes a value for safe interpolation inside a double-quoted shell string.
    /// Unlike <see cref="EscapeShellArg"/>, this does NOT wrap in single quotes —
    /// it only neutralizes characters that have special meaning inside
    /// <c>"..."</c>: backslash, double-quote, dollar, and backtick.
    /// Use this for values embedded in <c>echo -e "..."</c> payloads sent over nc.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped string safe for double-quoted shell interpolation.</returns>
    public static string EscapeForDoubleQuotedString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);
    }

    /// <summary>
    /// Escapes a string for safe use as a single-quoted POSIX shell argument.
    /// Prevents shell injection (CWE-78) by wrapping the value in single quotes
    /// and escaping any embedded single quotes.
    /// </summary>
    /// <param name="arg">The argument to escape.</param>
    /// <returns>A shell-safe quoted argument string.</returns>
    public static string EscapeShellArg(string arg)
    {
        ArgumentNullException.ThrowIfNull(arg);
        return "'" + arg.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="executablePath"/> points to a
    /// shell interpreter or script host whose argument parsing would expand
    /// metacharacters (<c>%</c>, <c>^</c>, <c>()</c>, <c>!</c>, etc.).
    /// Covers cmd.exe, PowerShell, WSL, Unix shells (bash/sh/zsh),
    /// Windows Script Host (cscript/wscript/mshta), and their associated
    /// script extensions (.bat, .cmd, .ps1, .vbs, .js, .jse, .vbe, .wsf, .hta).
    /// Returns <c>true</c> for null/empty paths as a safe default.
    /// </summary>
    public static bool IsShellTarget(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return true;

        ReadOnlySpan<char> fileName = Path.GetFileName(executablePath.AsSpan());
        if (fileName.IsEmpty) return true;

        // Windows trims trailing spaces/dots when resolving a path; mirror that so "cmd.exe " / "cmd.exe."
        // are still detected as shell targets.
        ReadOnlySpan<char> normalized = fileName.TrimEnd([' ', '.']);
        if (normalized.IsEmpty) return true;

        // Script files executed by a shell/interpreter via file association
        if (normalized.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".vbs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".jse", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".vbe", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".wsf", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".hta", StringComparison.OrdinalIgnoreCase))
            return true;

        // Shell interpreters and script hosts (with and without .exe)
        ReadOnlySpan<char> stem = normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;

        return stem.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("bash", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("sh", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("zsh", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("wsl", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("cscript", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("wscript", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("mshta", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Valid PowerShell execution policies. Used to whitelist the user-configurable
    /// setting before interpolating it into shell arguments (CWE-78 prevention).
    /// </summary>
    private static readonly FrozenSet<string> ValidExecutionPolicies =
        FrozenSet.ToFrozenSet(
        [
            "AllSigned", "Bypass", "Default", "RemoteSigned",
            "Restricted", "Undefined", "Unrestricted"
        ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when the value is a known PowerShell execution policy.
    /// </summary>
    public static bool IsValidExecutionPolicy(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && ValidExecutionPolicies.Contains(value);
    }

    /// <summary>
    /// Compile a regex pattern with timeout protection against ReDoS.
    /// </summary>
    private static Regex CompilePattern(string pattern)
    {
        return new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));
    }
}
