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

using System.Buffers.Binary;
using Heimdall.Core.Hashing;

namespace Heimdall.Core.Otp;

public static class TotpParameters
{
    public const int DefaultTimeStepSeconds = 30;
    public const int DefaultDigits = 6;
    public const int MinDigits = 1;
    public const int MaxDigits = 9;
    public const HashAlgorithmKind DefaultAlgorithm = HashAlgorithmKind.Sha1;
}

public static class TotpGenerator
{
    /// <summary>
    /// Generates a time-based one-time password per RFC 6238.
    /// Uses HMAC-SHA1 by default because RFC 6238 keeps SHA-1 as the baseline
    /// profile for the widest authenticator compatibility. CA5350 concerns are
    /// acceptable here because the default profile is specification-driven, and
    /// SHA-256 / SHA-512 are also supported when explicitly requested.
    /// </summary>
    public static string Generate(
        byte[] secret,
        long unixTimeSeconds,
        HashAlgorithmKind algorithm = HashAlgorithmKind.Sha1,
        int digits = TotpParameters.DefaultDigits,
        int timeStepSeconds = TotpParameters.DefaultTimeStepSeconds)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, TotpParameters.MinDigits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, TotpParameters.MaxDigits);
        if (timeStepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeStepSeconds), timeStepSeconds, "Time step must be positive.");
        }

        if (algorithm is not (HashAlgorithmKind.Sha1 or HashAlgorithmKind.Sha256 or HashAlgorithmKind.Sha512))
        {
            throw new NotSupportedException($"TOTP algorithm '{algorithm}' is not supported. Use Sha1, Sha256, or Sha512.");
        }

        var counter = unixTimeSeconds / timeStepSeconds;
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        var hash = HmacComputer.Compute(algorithm, secret, counterBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        var modulus = (int)Math.Pow(10, digits);
        var code = binary % modulus;
        return code.ToString("D" + digits, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static int ElapsedInStep(long unixTimeSeconds, int timeStepSeconds = TotpParameters.DefaultTimeStepSeconds)
    {
        if (timeStepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeStepSeconds), timeStepSeconds, "Time step must be positive.");
        }

        return (int)(unixTimeSeconds % timeStepSeconds);
    }

    public static int RemainingInStep(long unixTimeSeconds, int timeStepSeconds = TotpParameters.DefaultTimeStepSeconds)
    {
        if (timeStepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeStepSeconds), timeStepSeconds, "Time step must be positive.");
        }

        return timeStepSeconds - (int)(unixTimeSeconds % timeStepSeconds);
    }
}
