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

namespace Heimdall.Terminal.Logging;

/// <summary>
/// Incrementally decodes UTF-8 byte chunks while preserving multi-byte character boundaries.
/// </summary>
/// <remarks>
/// This type is not thread-safe. Callers must serialize access when a shared instance is used.
/// </remarks>
public sealed class StreamingUtf8Decoder
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    public string DecodeChunk(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty)
        {
            return string.Empty;
        }

        char[] buffer = new char[Encoding.UTF8.GetMaxCharCount(chunk.Length)];
        _decoder.Convert(
            chunk,
            buffer,
            flush: false,
            out _,
            out int charsUsed,
            out _);

        return new string(buffer, 0, charsUsed);
    }

    public string Flush()
    {
        char[] buffer = new char[Encoding.UTF8.GetMaxCharCount(0)];
        _decoder.Convert(
            ReadOnlySpan<byte>.Empty,
            buffer,
            flush: true,
            out _,
            out int charsUsed,
            out _);

        if (charsUsed == 0)
        {
            return string.Empty;
        }

        return new string(buffer, 0, charsUsed);
    }

    public void Reset()
    {
        _decoder.Reset();
    }
}
