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

public static class HashAlgorithmCatalog
{
    private static readonly HashAlgorithmKind[] AllKindsInternal =
    [
        HashAlgorithmKind.Md5,
        HashAlgorithmKind.Sha1,
        HashAlgorithmKind.Sha256,
        HashAlgorithmKind.Sha384,
        HashAlgorithmKind.Sha512,
        HashAlgorithmKind.Sha3_256,
    ];

    private static readonly HashAlgorithmKind[] SupportedKindsInternal =
        AllKindsInternal.Where(IsSupported).ToArray();

    public static IReadOnlyList<HashAlgorithmKind> AllKinds => AllKindsInternal;

    public static IReadOnlyList<HashAlgorithmKind> SupportedKinds => SupportedKindsInternal;

    public static bool IsSupported(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Md5 => true,
        HashAlgorithmKind.Sha1 => true,
        HashAlgorithmKind.Sha256 => true,
        HashAlgorithmKind.Sha384 => true,
        HashAlgorithmKind.Sha512 => true,
        HashAlgorithmKind.Sha3_256 => SHA3_256.IsSupported,
        _ => false,
    };

    public static string DisplayName(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Md5 => "MD5",
        HashAlgorithmKind.Sha1 => "SHA1",
        HashAlgorithmKind.Sha256 => "SHA256",
        HashAlgorithmKind.Sha384 => "SHA384",
        HashAlgorithmKind.Sha512 => "SHA512",
        HashAlgorithmKind.Sha3_256 => "SHA3-256",
        _ => kind.ToString(),
    };

    public static int HexLength(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Md5 => 32,
        HashAlgorithmKind.Sha1 => 40,
        HashAlgorithmKind.Sha256 => 64,
        HashAlgorithmKind.Sha384 => 96,
        HashAlgorithmKind.Sha512 => 128,
        HashAlgorithmKind.Sha3_256 => 64,
        _ => 0,
    };
}
