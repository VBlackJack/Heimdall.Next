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

namespace Heimdall.Core.Hashing;

public enum HmacOutputFormat
{
    Hex,
    Base64,
}

public static class HmacAlgorithmCatalog
{
    private static readonly HashAlgorithmKind[] Supported =
    [
        HashAlgorithmKind.Sha256,
        HashAlgorithmKind.Sha384,
        HashAlgorithmKind.Sha512,
        HashAlgorithmKind.Sha1,
        HashAlgorithmKind.Md5,
    ];

    public static IReadOnlyList<HashAlgorithmKind> SupportedKinds => Supported;

    public static bool IsSupported(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Sha256 or
        HashAlgorithmKind.Sha384 or
        HashAlgorithmKind.Sha512 or
        HashAlgorithmKind.Sha1 or
        HashAlgorithmKind.Md5 => true,
        _ => false,
    };

    public static string DisplayName(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Md5 => "HMAC-MD5",
        HashAlgorithmKind.Sha1 => "HMAC-SHA1",
        HashAlgorithmKind.Sha256 => "HMAC-SHA256",
        HashAlgorithmKind.Sha384 => "HMAC-SHA384",
        HashAlgorithmKind.Sha512 => "HMAC-SHA512",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not supported for HMAC."),
    };
}
