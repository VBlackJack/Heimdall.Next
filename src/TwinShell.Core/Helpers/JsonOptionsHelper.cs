/*
 * Copyright 2025 Julien Bombled
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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwinShell.Core.Helpers;

/// <summary>
/// Provides centralized, reusable JsonSerializerOptions instances.
/// Using static instances avoids repeated allocation and improves performance.
/// </summary>
public static class JsonOptionsHelper
{
    /// <summary>
    /// Default options for general JSON reading/writing with human-readable formatting.
    /// WriteIndented = true, PropertyNameCaseInsensitive = true
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Default options with enum converter for reading/writing enums as strings.
    /// WriteIndented = true, PropertyNameCaseInsensitive = true, JsonStringEnumConverter
    /// </summary>
    public static JsonSerializerOptions DefaultWithEnumConverter { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Options for exporting JSON with camelCase property names.
    /// WriteIndented = true, PropertyNamingPolicy = CamelCase
    /// </summary>
    public static JsonSerializerOptions CamelCaseForExport { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Options for importing JSON with camelCase property names.
    /// PropertyNamingPolicy = CamelCase
    /// </summary>
    public static JsonSerializerOptions CamelCaseForImport { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Options for compact storage (database, etc.) without indentation.
    /// WriteIndented = false, PropertyNameCaseInsensitive = true
    /// </summary>
    public static JsonSerializerOptions CompactStorage { get; } = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Options for reading JSON with case-insensitive property matching only.
    /// PropertyNameCaseInsensitive = true
    /// </summary>
    public static JsonSerializerOptions CaseInsensitive { get; } = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Options for reading/writing JSON with camelCase and case-insensitive matching.
    /// PropertyNamingPolicy = CamelCase, PropertyNameCaseInsensitive = true
    /// </summary>
    public static JsonSerializerOptions CamelCaseCaseInsensitive { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Options for exporting JSON with human-readable formatting, omitting null values.
    /// WriteIndented = true, DefaultIgnoreCondition = WhenWritingNull
    /// </summary>
    public static JsonSerializerOptions IndentedIgnoreNull { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Options for secure import with depth limit to prevent DoS via deeply nested objects.
    /// PropertyNameCaseInsensitive = true, MaxDepth = 32
    /// </summary>
    public static JsonSerializerOptions SecureImport { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32
    };

    /// <summary>
    /// Options for sync services with camelCase, null ignoring, and case-insensitive reading.
    /// WriteIndented = true, PropertyNamingPolicy = CamelCase,
    /// DefaultIgnoreCondition = WhenWritingNull, PropertyNameCaseInsensitive = true
    /// </summary>
    public static JsonSerializerOptions SyncService { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}
