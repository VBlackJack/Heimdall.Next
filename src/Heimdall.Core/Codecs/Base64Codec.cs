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

namespace Heimdall.Core.Codecs;

public static class Base64Codec
{
    public static string Encode(byte[] data, bool urlSafe)
    {
        ArgumentNullException.ThrowIfNull(data);

        var encoded = Convert.ToBase64String(data, Base64FormattingOptions.InsertLineBreaks);
        if (!urlSafe)
        {
            return encoded;
        }

        return encoded
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static byte[] Decode(string base64, bool urlSafe)
    {
        ArgumentNullException.ThrowIfNull(base64);

        if (urlSafe)
        {
            base64 = base64.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }
        }

        return Convert.FromBase64String(base64);
    }
}
