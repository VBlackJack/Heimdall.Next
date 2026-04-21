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
using System.Text.Json;

namespace Heimdall.Core.Jwt;

public enum JwtDecodeError
{
    None,
    InvalidFormat,
    DecodeFailed,
}

public sealed record JwtDecoded(
    string HeaderJson,
    string PayloadJson,
    byte[] SignatureBytes,
    string HeaderRaw,
    string PayloadRaw,
    string SignatureRaw)
{
    public string PrettyHeaderJson => JwtParser.PrettyPrintJson(HeaderJson);
    public string PrettyPayloadJson => JwtParser.PrettyPrintJson(PayloadJson);
    public string SignatureHex => Convert.ToHexStringLower(SignatureBytes);
}

public static class JwtParser
{
    private static readonly JsonWriterOptions PrettyPrintOptions = new() { Indented = true };

    public static bool TryDecode(string? input, out JwtDecoded? decoded, out JwtDecodeError error)
    {
        decoded = null;
        var trimmed = input?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = JwtDecodeError.InvalidFormat;
            return false;
        }

        var parts = trimmed.Split('.');
        if (parts.Length != 3)
        {
            error = JwtDecodeError.InvalidFormat;
            return false;
        }

        var headerJson = DecodeBase64UrlString(parts[0]);
        var payloadJson = DecodeBase64UrlString(parts[1]);
        var signatureBytes = DecodeBase64UrlBytes(parts[2]);
        if (headerJson is null || payloadJson is null || signatureBytes is null)
        {
            error = JwtDecodeError.DecodeFailed;
            return false;
        }

        if (!IsValidJson(headerJson) || !IsValidJson(payloadJson))
        {
            error = JwtDecodeError.DecodeFailed;
            return false;
        }

        decoded = new JwtDecoded(headerJson, payloadJson, signatureBytes, parts[0], parts[1], parts[2]);
        error = JwtDecodeError.None;
        return true;
    }

    internal static byte[]? DecodeBase64UrlBytes(string base64Url)
    {
        try
        {
            var padded = base64Url
                .Replace('-', '+')
                .Replace('_', '/');

            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal static string? DecodeBase64UrlString(string base64Url)
    {
        var bytes = DecodeBase64UrlBytes(base64Url);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    internal static string PrettyPrintJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, PrettyPrintOptions);
        document.RootElement.WriteTo(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool IsValidJson(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
