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

namespace Heimdall.Core.Otp;

public static class Base32Codec
{
    public static byte[] Decode(string base32)
    {
        if (string.IsNullOrEmpty(base32))
        {
            return [];
        }

        base32 = base32.TrimEnd('=');
        var output = new byte[base32.Length * 5 / 8];
        var bitBuffer = 0;
        var bitsRemaining = 0;
        var outputIndex = 0;

        foreach (var c in base32)
        {
            var value = CharToValue(c);
            if (value < 0)
            {
                throw new FormatException($"Invalid Base32 character: {c}");
            }

            bitBuffer = (bitBuffer << 5) | value;
            bitsRemaining += 5;

            if (bitsRemaining >= 8)
            {
                bitsRemaining -= 8;
                output[outputIndex++] = (byte)(bitBuffer >> bitsRemaining);
            }
        }

        return output;
    }

    private static int CharToValue(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a',
        >= '2' and <= '7' => c - '2' + 26,
        _ => -1,
    };
}
