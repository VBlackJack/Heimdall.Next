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
using System.Text;

namespace Heimdall.Core.Ssh;

public static class KnownHostsHash
{
    public static bool TryMatches(string hashedHost, string plainHost)
    {
        ArgumentNullException.ThrowIfNull(hashedHost);
        ArgumentNullException.ThrowIfNull(plainHost);

        return TryParse(hashedHost, out var salt, out var expectedHash)
            && CryptographicOperations.FixedTimeEquals(
                ComputeHash(salt, plainHost),
                expectedHash);
    }

    internal static bool TryParse(string hashedHost, out byte[] salt, out byte[] hash)
    {
        salt = [];
        hash = [];

        var parts = hashedHost.Split('|');
        if (parts.Length != 4 || parts[0].Length != 0 || parts[1] != "1")
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
            return salt.Length > 0 && hash.Length == 20;
        }
        catch (FormatException)
        {
            salt = [];
            hash = [];
            return false;
        }
    }

    private static byte[] ComputeHash(byte[] salt, string plainHost)
    {
        using var hmac = new HMACSHA1(salt);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(plainHost));
    }
}
