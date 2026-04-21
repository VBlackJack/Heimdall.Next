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

using Heimdall.Core.Jwt;

namespace Heimdall.Core.Tests.Jwt;

public sealed class JwtClaimsEvaluatorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EvaluateExpiration_NoExp_ReturnsNoExpiry()
    {
        var result = JwtClaimsEvaluator.EvaluateExpiration("""{"sub":"john"}""", FixedNow);

        Assert.Equal(JwtExpirationStatus.NoExpiry, result.Status);
        Assert.Null(result.ExpiresAt);
    }

    [Fact]
    public void EvaluateExpiration_FutureExp_ReturnsValid()
    {
        var result = JwtClaimsEvaluator.EvaluateExpiration("""{"exp":1776600000}""", FixedNow);

        Assert.Equal(JwtExpirationStatus.Valid, result.Status);
        Assert.NotNull(result.ExpiresAt);
    }

    [Fact]
    public void EvaluateExpiration_PastExp_ReturnsExpired()
    {
        var result = JwtClaimsEvaluator.EvaluateExpiration("""{"exp":1600000000}""", FixedNow);

        Assert.Equal(JwtExpirationStatus.Expired, result.Status);
        Assert.NotNull(result.ExpiresAt);
    }

    [Fact]
    public void EvaluateExpiration_ExpEqualToNow_IsValid()
    {
        var result = JwtClaimsEvaluator.EvaluateExpiration($$"""{"exp":{{FixedNow.ToUnixTimeSeconds()}}}""", FixedNow);

        Assert.Equal(JwtExpirationStatus.Valid, result.Status);
    }

    [Theory]
    [InlineData("""{"exp":"tomorrow"}""")]
    [InlineData("""{"exp":null}""")]
    [InlineData("""{"exp":true}""")]
    public void EvaluateExpiration_InvalidClaim_ReturnsInvalidClaim(string payloadJson)
    {
        var result = JwtClaimsEvaluator.EvaluateExpiration(payloadJson, FixedNow);

        Assert.Equal(JwtExpirationStatus.InvalidClaim, result.Status);
        Assert.Null(result.ExpiresAt);
    }

    [Fact]
    public void EvaluateExpiration_InvalidJson_ReturnsInvalidClaim()
    {
        var result = JwtClaimsEvaluator.EvaluateExpiration("{", FixedNow);

        Assert.Equal(JwtExpirationStatus.InvalidClaim, result.Status);
    }

    [Fact]
    public void EvaluateExpiration_OverflowUnixSeconds_ReturnsInvalidClaim()
    {
        var result = JwtClaimsEvaluator.EvaluateExpiration("""{"exp":999999999999999999}""", FixedNow);

        Assert.Equal(JwtExpirationStatus.InvalidClaim, result.Status);
    }

    [Theory]
    [InlineData(1, JwtExpirationStatus.Expired)]
    [InlineData(31_536_000_000, JwtExpirationStatus.Valid)]
    public void EvaluateExpiration_UsesUnixSecondsSemantics(long exp, JwtExpirationStatus expected)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000_000_000);
        var result = JwtClaimsEvaluator.EvaluateExpiration($$"""{"exp":{{exp}}}""", now);

        Assert.Equal(expected, result.Status);
    }
}
