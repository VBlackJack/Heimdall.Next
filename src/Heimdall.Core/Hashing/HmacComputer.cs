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

using System.Security.Cryptography;

namespace Heimdall.Core.Hashing;

public static class HmacComputer
{
    public static byte[] Compute(HashAlgorithmKind kind, byte[] key, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);

        if (!HmacAlgorithmCatalog.IsSupported(kind))
        {
            throw new NotSupportedException($"Hash algorithm '{kind}' is not supported for HMAC.");
        }

        using HMAC hmac = kind switch
        {
            HashAlgorithmKind.Md5 => new HMACMD5(key),
            HashAlgorithmKind.Sha1 => new HMACSHA1(key),
            HashAlgorithmKind.Sha256 => new HMACSHA256(key),
            HashAlgorithmKind.Sha384 => new HMACSHA384(key),
            HashAlgorithmKind.Sha512 => new HMACSHA512(key),
            _ => throw new NotSupportedException($"Hash algorithm '{kind}' is not supported for HMAC."),
        };

        return hmac.ComputeHash(data);
    }

    public static string Format(byte[] hmacBytes, HmacOutputFormat format)
    {
        ArgumentNullException.ThrowIfNull(hmacBytes);

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown HMAC output format.");
        }

        return format switch
        {
            HmacOutputFormat.Hex => Convert.ToHexStringLower(hmacBytes),
            HmacOutputFormat.Base64 => Convert.ToBase64String(hmacBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown HMAC output format."),
        };
    }
}
