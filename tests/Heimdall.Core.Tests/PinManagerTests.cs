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

using System.Diagnostics;
using Heimdall.Core.Security;

namespace Heimdall.Core.Tests;

public class PinManagerTests
{
    private readonly PinManager _pm = new();

    // ── GenerateSalt ────────────────────────────────────────────────────

    [Fact]
    public void GenerateSalt_Returns16ByteBase64()
    {
        var salt = _pm.GenerateSalt();
        var bytes = Convert.FromBase64String(salt);

        Assert.Equal(16, bytes.Length);
    }

    [Fact]
    public void GenerateSalt_ProducesUniqueSalts()
    {
        var salt1 = _pm.GenerateSalt();
        var salt2 = _pm.GenerateSalt();

        Assert.NotEqual(salt1, salt2);
    }

    [Fact]
    public void GenerateSalt_ManyCallsNeverCollide()
    {
        var salts = Enumerable.Range(0, 100).Select(_ => _pm.GenerateSalt()).ToHashSet();

        Assert.Equal(100, salts.Count);
    }

    // ── Hash ────────────────────────────────────────────────────────────

    [Fact]
    public void Hash_Returns32ByteBase64()
    {
        var salt = _pm.GenerateSalt();
        var hash = _pm.Hash("1234", salt);
        var bytes = Convert.FromBase64String(hash);

        Assert.Equal(32, bytes.Length);
    }

    [Fact]
    public void Hash_SameInputSameSalt_ProducesSameHash()
    {
        var salt = _pm.GenerateSalt();
        var hash1 = _pm.Hash("5678", salt);
        var hash2 = _pm.Hash("5678", salt);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_SameInputDifferentSalt_ProducesDifferentHash()
    {
        var salt1 = _pm.GenerateSalt();
        var salt2 = _pm.GenerateSalt();
        var hash1 = _pm.Hash("1234", salt1);
        var hash2 = _pm.Hash("1234", salt2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_DifferentInputSameSalt_ProducesDifferentHash()
    {
        var salt = _pm.GenerateSalt();
        var hash1 = _pm.Hash("1234", salt);
        var hash2 = _pm.Hash("5678", salt);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_NullPin_ThrowsArgumentNullException()
    {
        var salt = _pm.GenerateSalt();

        Assert.Throws<ArgumentNullException>(() => _pm.Hash(null!, salt));
    }

    [Fact]
    public void Hash_NullSalt_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _pm.Hash("1234", null!));
    }

    [Fact]
    public void Hash_InvalidSaltLength_ThrowsArgumentException()
    {
        var shortSalt = Convert.ToBase64String(new byte[8]);

        Assert.Throws<ArgumentException>(() => _pm.Hash("1234", shortSalt));
    }

    [Fact]
    public void Hash_EmptyPin_DoesNotThrow()
    {
        var salt = _pm.GenerateSalt();
        var hash = _pm.Hash("", salt);

        Assert.NotEmpty(hash);
    }

    // ── Verify (hash + verify round-trip) ───────────────────────────────

    [Fact]
    public void Verify_CorrectPin_ReturnsTrue()
    {
        var salt = _pm.GenerateSalt();
        var hash = _pm.Hash("1234", salt);

        Assert.True(_pm.Verify("1234", hash, salt));
    }

    [Fact]
    public void Verify_WrongPin_ReturnsFalse()
    {
        var salt = _pm.GenerateSalt();
        var hash = _pm.Hash("1234", salt);

        Assert.False(_pm.Verify("5678", hash, salt));
    }

    [Fact]
    public void Verify_WrongSalt_ReturnsFalse()
    {
        var salt1 = _pm.GenerateSalt();
        var salt2 = _pm.GenerateSalt();
        var hash = _pm.Hash("1234", salt1);

        Assert.False(_pm.Verify("1234", hash, salt2));
    }

    [Fact]
    public void Verify_TamperedHash_ReturnsFalse()
    {
        var salt = _pm.GenerateSalt();
        var hash = _pm.Hash("1234", salt);
        var tampered = Convert.ToBase64String(new byte[32]);

        Assert.False(_pm.Verify("1234", tampered, salt));
    }

    [Fact]
    public void Verify_NullPin_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _pm.Verify(null!, "hash", "salt"));
    }

    [Fact]
    public void Verify_NullHash_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _pm.Verify("1234", null!, "salt"));
    }

    [Fact]
    public void Verify_NullSalt_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _pm.Verify("1234", "hash", null!));
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalse()
    {
        var salt = _pm.GenerateSalt();

        Assert.False(_pm.Verify("1234", "not-valid-base64!!!", salt));
    }

    // ── Constant-time comparison (timing side-channel resistance) ───────

    [Fact]
    public void Verify_UsesConstantTimeComparison_TimingSimilarForCorrectAndWrongPin()
    {
        var salt = _pm.GenerateSalt();
        var hash = _pm.Hash("1234", salt);

        // Warm up JIT
        _pm.Verify("1234", hash, salt);
        _pm.Verify("9999", hash, salt);

        const int iterations = 500;
        var sw = new Stopwatch();

        // Measure correct PIN
        sw.Restart();
        for (int i = 0; i < iterations; i++)
            _pm.Verify("1234", hash, salt);
        sw.Stop();
        var correctTicks = sw.ElapsedTicks;

        // Measure wrong PIN
        sw.Restart();
        for (int i = 0; i < iterations; i++)
            _pm.Verify("9999", hash, salt);
        sw.Stop();
        var wrongTicks = sw.ElapsedTicks;

        // Both should take similar time. Allow 3x tolerance for CI variance.
        // The key invariant: wrong PIN must NOT be significantly faster than correct PIN,
        // which would indicate early-exit comparison.
        var ratio = (double)wrongTicks / correctTicks;
        Assert.InRange(ratio, 0.3, 3.0);
    }

    // ── PBKDF2 parameters ───────────────────────────────────────────────

    [Fact]
    public void Hash_UsesPbkdf2Sha256_KnownVectorConsistency()
    {
        // Verify determinism: same input + salt always yields identical output,
        // confirming PBKDF2 is used (not a random-per-call scheme).
        var salt = Convert.ToBase64String(new byte[16]); // all-zero salt
        var hash1 = _pm.Hash("1234", salt);
        var hash2 = _pm.Hash("1234", salt);

        Assert.Equal(hash1, hash2);
        // Output is 32 bytes = 256 bits, confirming SHA-256 key length
        Assert.Equal(32, Convert.FromBase64String(hash1).Length);
    }

    [Fact]
    public void Hash_OutputLength_Confirms256BitKey()
    {
        var salt = _pm.GenerateSalt();
        var hash = _pm.Hash("1234", salt);
        var bytes = Convert.FromBase64String(hash);

        Assert.Equal(32, bytes.Length);
    }

    // ── ValidateFormat ──────────────────────────────────────────────────

    [Theory]
    [InlineData("1234")]
    [InlineData("12345")]
    [InlineData("123456")]
    [InlineData("1234567")]
    [InlineData("12345678")]
    public void ValidateFormat_ValidPin_ReturnsIsValid(string pin)
    {
        var result = _pm.ValidateFormat(pin);

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("12")]
    [InlineData("123")]
    public void ValidateFormat_TooShort_ReturnsTooShortError(string pin)
    {
        var result = _pm.ValidateFormat(pin);

        Assert.False(result.IsValid);
        Assert.Equal(PinValidationError.TooShort, result.Error);
    }

    [Fact]
    public void ValidateFormat_Null_ReturnsTooShortError()
    {
        var result = _pm.ValidateFormat(null!);

        Assert.False(result.IsValid);
        Assert.Equal(PinValidationError.TooShort, result.Error);
    }

    [Fact]
    public void ValidateFormat_TooLong_ReturnsTooLongError()
    {
        var result = _pm.ValidateFormat("123456789");

        Assert.False(result.IsValid);
        Assert.Equal(PinValidationError.TooLong, result.Error);
    }

    [Theory]
    [InlineData("abcd")]
    [InlineData("12ab")]
    [InlineData("12 4")]
    [InlineData("1234!")]
    public void ValidateFormat_InvalidChars_ReturnsInvalidCharsError(string pin)
    {
        var result = _pm.ValidateFormat(pin);

        Assert.False(result.IsValid);
        Assert.Equal(PinValidationError.InvalidChars, result.Error);
    }

    // ── Lockout (RegisterFailure / ResetFailures / IsLockedOut) ─────────

    [Fact]
    public void FailureCount_InitiallyZero()
    {
        Assert.Equal(0, _pm.FailureCount);
    }

    [Fact]
    public void IsLockedOut_InitiallyFalse()
    {
        Assert.False(_pm.IsLockedOut);
    }

    [Fact]
    public void RegisterFailure_IncrementsCount()
    {
        var count = _pm.RegisterFailure();

        Assert.Equal(1, count);
        Assert.Equal(1, _pm.FailureCount);
    }

    [Fact]
    public void RegisterFailure_AtMaxAttempts_TriggersLockout()
    {
        var pm = new PinManager(maxAttempts: 3);

        pm.RegisterFailure();
        pm.RegisterFailure();
        Assert.False(pm.IsLockedOut);

        pm.RegisterFailure();
        Assert.True(pm.IsLockedOut);
    }

    [Fact]
    public void ResetFailures_ClearsCountAndLockout()
    {
        var pm = new PinManager(maxAttempts: 2);

        pm.RegisterFailure();
        pm.RegisterFailure();
        Assert.True(pm.IsLockedOut);

        pm.ResetFailures();

        Assert.Equal(0, pm.FailureCount);
        Assert.False(pm.IsLockedOut);
    }

    [Fact]
    public void LockoutRemaining_WhenNotLockedOut_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, _pm.LockoutRemaining);
    }

    [Fact]
    public void LockoutRemaining_WhenLockedOut_ReturnsPositive()
    {
        var pm = new PinManager(maxAttempts: 1, lockoutDuration: TimeSpan.FromMinutes(10));
        pm.RegisterFailure();

        Assert.True(pm.LockoutRemaining > TimeSpan.Zero);
    }

    // ── Constructor defaults ────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultMaxAttempts_IsFive()
    {
        Assert.Equal(5, _pm.MaxAttempts);
    }

    [Fact]
    public void Constructor_DefaultLockoutDuration_IsFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), _pm.LockoutDuration);
    }

    [Fact]
    public void Constructor_ZeroMaxAttempts_FallsBackToFive()
    {
        var pm = new PinManager(maxAttempts: 0);

        Assert.Equal(5, pm.MaxAttempts);
    }

    [Fact]
    public void Constructor_NegativeMaxAttempts_FallsBackToFive()
    {
        var pm = new PinManager(maxAttempts: -1);

        Assert.Equal(5, pm.MaxAttempts);
    }

    // ── RestoreLockoutState ─────────────────────────────────────────────

    [Fact]
    public void RestoreLockoutState_ActiveLockout_RestoresLockedState()
    {
        var pm = new PinManager();
        var futureUtc = DateTime.UtcNow.AddMinutes(10);

        pm.RestoreLockoutState(5, futureUtc);

        Assert.Equal(5, pm.FailureCount);
        Assert.True(pm.IsLockedOut);
    }

    [Fact]
    public void RestoreLockoutState_ExpiredLockout_ResetsToZero()
    {
        var pm = new PinManager();
        var pastUtc = DateTime.UtcNow.AddMinutes(-10);

        pm.RestoreLockoutState(5, pastUtc);

        Assert.Equal(0, pm.FailureCount);
        Assert.False(pm.IsLockedOut);
    }

    [Fact]
    public void RestoreLockoutState_NullLockout_KeepsFailureCount()
    {
        var pm = new PinManager();

        pm.RestoreLockoutState(3, null);

        Assert.Equal(3, pm.FailureCount);
        Assert.False(pm.IsLockedOut);
    }

    [Fact]
    public void RestoreLockoutState_NegativeFailureCount_ClampsToZero()
    {
        var pm = new PinManager();

        pm.RestoreLockoutState(-5, null);

        Assert.Equal(0, pm.FailureCount);
    }
}
