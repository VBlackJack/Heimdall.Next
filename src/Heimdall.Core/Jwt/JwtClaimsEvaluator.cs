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

using System.Text.Json;

namespace Heimdall.Core.Jwt;

public enum JwtExpirationStatus
{
    Expired,
    Valid,
    NoExpiry,
    InvalidClaim,
}

public sealed record JwtClaimsEvaluation(JwtExpirationStatus Status, DateTimeOffset? ExpiresAt);

public static class JwtClaimsEvaluator
{
    public static JwtClaimsEvaluation EvaluateExpiration(string payloadJson, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty("exp", out var expElement))
            {
                return new JwtClaimsEvaluation(JwtExpirationStatus.NoExpiry, null);
            }

            if (!expElement.TryGetInt64(out var expUnix))
            {
                return new JwtClaimsEvaluation(JwtExpirationStatus.InvalidClaim, null);
            }

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix);
            return new JwtClaimsEvaluation(
                expiresAt < now ? JwtExpirationStatus.Expired : JwtExpirationStatus.Valid,
                expiresAt);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new JwtClaimsEvaluation(JwtExpirationStatus.InvalidClaim, null);
        }
        catch (InvalidOperationException)
        {
            return new JwtClaimsEvaluation(JwtExpirationStatus.InvalidClaim, null);
        }
        catch (JsonException)
        {
            return new JwtClaimsEvaluation(JwtExpirationStatus.InvalidClaim, null);
        }
    }
}
