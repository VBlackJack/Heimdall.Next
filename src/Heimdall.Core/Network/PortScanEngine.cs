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

using System.Text;
using Heimdall.Core.Security;

namespace Heimdall.Core.Network;

/// <summary>
/// Pure port-scan engine: constants, data records, and export formatting.
/// Port parsing is delegated to <see cref="BannerGrabEngine.ParsePorts(string)"/>.
/// </summary>
public static class PortScanEngine
{
    public const int ConnectTimeoutMs = 2000;
    public const int BannerGrabTimeoutMs = 1000;
    public const int BannerMaxBytes = 256;
    public const int MaxConcurrent = 50;
    public const int LargePortCountWarningThreshold = 10000;

    /// <summary>
    /// Builds CSV content from port scan results.
    /// </summary>
    public static string BuildCsvExport(IReadOnlyList<PortScanResult> results, Func<string, string>? localize = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(L(localize, "ToolPortScanCsvHeader"));

        foreach (var result in results.OrderBy(result => result.Port))
        {
            var status = InputValidator.SanitizeCsvCell(result.Status);
            var service = InputValidator.SanitizeCsvCell(result.Service);
            var responseTime = InputValidator.SanitizeCsvCell(result.ResponseTime);
            var banner = InputValidator.SanitizeCsvCell(result.Banner).Replace("\"", "\"\"", StringComparison.Ordinal);
            builder.AppendLine($"{result.Port},{status},{service},{responseTime},\"{banner}\"");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds formatted text for clipboard copy.
    /// </summary>
    public static string BuildClipboardText(IReadOnlyList<PortScanResult> results, Func<string, string>? localize = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{L(localize, "ToolPortScanColPort"),-8}{L(localize, "ToolPortScanColStatus"),-12}{L(localize, "ToolPortScanColService"),-20}{L(localize, "ToolPortScanColResponseTime")}");
        builder.AppendLine(new string('-', 52));

        foreach (var result in results)
        {
            builder.AppendLine($"{result.Port,-8}{result.Status,-12}{result.Service,-20}{result.ResponseTime}");
        }

        return builder.ToString();
    }

    private static string L(Func<string, string>? localize, string key)
        => localize?.Invoke(key) ?? key;
}

/// <summary>
/// Raw result from probing a single port (before localization/UI projection).
/// </summary>
public sealed record PortProbeResult(int Port, bool IsOpen, string Service, string ResponseTime, string? Banner);

/// <summary>
/// Display-ready port scan result for DataGrid binding.
/// Status is localized by the view layer.
/// </summary>
public sealed record PortScanResult(int Port, bool IsOpen, string Service, string ResponseTime, string Status, string Banner);
