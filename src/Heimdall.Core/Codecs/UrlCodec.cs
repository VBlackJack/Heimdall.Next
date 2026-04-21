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

namespace Heimdall.Core.Codecs;

public static class UrlCodec
{
    public static string Encode(string input, bool componentMode)
    {
        ArgumentNullException.ThrowIfNull(input);

        return componentMode
            ? Uri.EscapeDataString(input)
            : EscapeUrlPreservingStructure(input);
    }

    public static string Decode(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Uri.UnescapeDataString(input);
    }

    private static string EscapeUrlPreservingStructure(string url)
    {
        var result = new StringBuilder(url.Length * 2);
        var segment = new StringBuilder();

        foreach (var ch in url)
        {
            if (IsUrlStructuralChar(ch))
            {
                if (segment.Length > 0)
                {
                    result.Append(Uri.EscapeDataString(segment.ToString()));
                    segment.Clear();
                }

                result.Append(ch);
            }
            else
            {
                segment.Append(ch);
            }
        }

        if (segment.Length > 0)
        {
            result.Append(Uri.EscapeDataString(segment.ToString()));
        }

        return result.ToString();
    }

    private static bool IsUrlStructuralChar(char c)
        => c is ':' or '/' or '?' or '#' or '&' or '=' or '@' or '%';
}
