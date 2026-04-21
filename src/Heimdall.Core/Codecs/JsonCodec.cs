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
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Heimdall.Core.Codecs;

public enum JsonFormatStatus
{
    Success,
    Empty,
    ParseError,
    InputTooLarge,
}

public readonly record struct JsonFormatResult(
    JsonFormatStatus Status,
    string Output,
    string ErrorMessage,
    long? LineNumber,
    long? ColumnNumber);

public static class JsonCodec
{
    public static JsonFormatResult Format(string input, bool indented)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input))
        {
            return new JsonFormatResult(JsonFormatStatus.Empty, string.Empty, string.Empty, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(input);
            var options = new JsonWriterOptions
            {
                Indented = indented,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                document.RootElement.WriteTo(writer);
            }

            return new JsonFormatResult(
                JsonFormatStatus.Success,
                Encoding.UTF8.GetString(stream.ToArray()),
                string.Empty,
                null,
                null);
        }
        catch (JsonException ex)
        {
            return new JsonFormatResult(
                JsonFormatStatus.ParseError,
                string.Empty,
                ex.InnerException?.Message ?? ex.Message,
                ex.LineNumber,
                ex.BytePositionInLine);
        }
    }
}
