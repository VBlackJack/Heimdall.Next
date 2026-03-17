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
using Heimdall.Core.Security;

namespace Heimdall.Core.Tests;

public class HmacIntegrityTests
{
    // ── GenerateRawKey ──────────────────────────────────────────────────

    [Fact]
    public void GenerateRawKey_Returns32ByteBase64()
    {
        var key = HmacIntegrity.GenerateRawKey();
        var bytes = Convert.FromBase64String(key);

        Assert.Equal(32, bytes.Length);
    }

    [Fact]
    public void GenerateRawKey_ProducesUniqueKeys()
    {
        var key1 = HmacIntegrity.GenerateRawKey();
        var key2 = HmacIntegrity.GenerateRawKey();

        Assert.NotEqual(key1, key2);
    }

    // ── ProtectWithHmac + UnprotectWithHmac round-trip ──────────────────

    [Fact]
    public void ProtectAndUnprotect_RoundTrip_PreservesPlaintext()
    {
        var key = HmacIntegrity.GenerateRawKey();
        var plaintext = "my-secret-password-123!";

        var protected_ = HmacIntegrity.ProtectWithHmac(plaintext, key);
        var decrypted = HmacIntegrity.UnprotectWithHmac(protected_, key);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void ProtectWithHmac_ContainsHmacSeparator()
    {
        var key = HmacIntegrity.GenerateRawKey();
        var protected_ = HmacIntegrity.ProtectWithHmac("test", key);

        Assert.Contains("|HMAC|", protected_);
    }

    [Fact]
    public void ProtectWithHmac_SameInput_ProducesDifferentOutput()
    {
        // DPAPI uses different entropy each time
        var key = HmacIntegrity.GenerateRawKey();
        var a = HmacIntegrity.ProtectWithHmac("same-input", key);
        var b = HmacIntegrity.ProtectWithHmac("same-input", key);

        Assert.NotEqual(a, b);
    }

    // ── HasIntegrity ────────────────────────────────────────────────────

    [Fact]
    public void HasIntegrity_ValidData_ReturnsTrue()
    {
        var key = HmacIntegrity.GenerateRawKey();
        var protected_ = HmacIntegrity.ProtectWithHmac("secret", key);

        Assert.True(HmacIntegrity.HasIntegrity(protected_, key));
    }

    [Fact]
    public void HasIntegrity_TamperedData_ReturnsFalse()
    {
        var key = HmacIntegrity.GenerateRawKey();
        var protected_ = HmacIntegrity.ProtectWithHmac("secret", key);

        // Tamper with the encrypted portion
        var parts = protected_.Split("|HMAC|");
        var tampered = "AAAA" + parts[0][4..] + "|HMAC|" + parts[1];

        Assert.False(HmacIntegrity.HasIntegrity(tampered, key));
    }

    [Fact]
    public void HasIntegrity_WrongKey_ReturnsFalse()
    {
        var key1 = HmacIntegrity.GenerateRawKey();
        var key2 = HmacIntegrity.GenerateRawKey();
        var protected_ = HmacIntegrity.ProtectWithHmac("secret", key1);

        Assert.False(HmacIntegrity.HasIntegrity(protected_, key2));
    }

    [Fact]
    public void HasIntegrity_NullInput_ReturnsFalse()
    {
        Assert.False(HmacIntegrity.HasIntegrity(null!, "key"));
        Assert.False(HmacIntegrity.HasIntegrity("data", null!));
    }

    [Fact]
    public void HasIntegrity_MalformedInput_ReturnsFalse()
    {
        var key = HmacIntegrity.GenerateRawKey();
        Assert.False(HmacIntegrity.HasIntegrity("not-valid-format", key));
    }

    // ── IsHmacProtected ─────────────────────────────────────────────────

    [Fact]
    public void IsHmacProtected_WithSeparator_ReturnsTrue()
    {
        Assert.True(HmacIntegrity.IsHmacProtected("abc|HMAC|def"));
    }

    [Fact]
    public void IsHmacProtected_WithoutSeparator_ReturnsFalse()
    {
        Assert.False(HmacIntegrity.IsHmacProtected("abc-def-ghi"));
    }

    [Fact]
    public void IsHmacProtected_Null_ReturnsFalse()
    {
        Assert.False(HmacIntegrity.IsHmacProtected(null));
    }

    // ── UnprotectWithHmac error cases ───────────────────────────────────

    [Fact]
    public void UnprotectWithHmac_TamperedHmac_ThrowsCryptographicException()
    {
        var key = HmacIntegrity.GenerateRawKey();
        var protected_ = HmacIntegrity.ProtectWithHmac("secret", key);

        // Tamper with the HMAC portion
        var parts = protected_.Split("|HMAC|");
        var tampered = parts[0] + "|HMAC|" + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        Assert.Throws<CryptographicException>(() =>
            HmacIntegrity.UnprotectWithHmac(tampered, key));
    }

    [Fact]
    public void UnprotectWithHmac_InvalidFormat_ThrowsFormatException()
    {
        var key = HmacIntegrity.GenerateRawKey();

        Assert.Throws<FormatException>(() =>
            HmacIntegrity.UnprotectWithHmac("no-separator-here", key));
    }

    [Fact]
    public void UnprotectWithHmac_NullArgs_ThrowsArgumentNull()
    {
        var key = HmacIntegrity.GenerateRawKey();

        Assert.Throws<ArgumentNullException>(() =>
            HmacIntegrity.UnprotectWithHmac(null!, key));
        Assert.Throws<ArgumentNullException>(() =>
            HmacIntegrity.UnprotectWithHmac("data|HMAC|hmac", null!));
    }

    [Fact]
    public void ProtectWithHmac_InvalidKeyLength_ThrowsArgumentException()
    {
        // 16-byte key instead of 32
        var shortKey = Convert.ToBase64String(new byte[16]);

        Assert.Throws<ArgumentException>(() =>
            HmacIntegrity.ProtectWithHmac("test", shortKey));
    }

    // ── NeedsRotation ───────────────────────────────────────────────────

    [Fact]
    public void NeedsRotation_FreshKey_ReturnsFalse()
    {
        Assert.False(HmacIntegrity.NeedsRotation(DateTime.UtcNow));
    }

    [Fact]
    public void NeedsRotation_ExpiredKey_ReturnsTrue()
    {
        var oldDate = DateTime.UtcNow.AddDays(-91);
        Assert.True(HmacIntegrity.NeedsRotation(oldDate));
    }

    [Fact]
    public void NeedsRotation_ExactBoundary_ReturnsTrue()
    {
        var exactBoundary = DateTime.UtcNow.AddDays(-90);
        Assert.True(HmacIntegrity.NeedsRotation(exactBoundary));
    }

    // ── GetRotationStatus ───────────────────────────────────────────────

    [Fact]
    public void GetRotationStatus_FreshKey_HasPositiveDaysRemaining()
    {
        var status = HmacIntegrity.GetRotationStatus(DateTime.UtcNow);

        Assert.False(status.NeedsRotation);
        Assert.True(status.DaysUntilRotation > 0);
        Assert.Equal(0, status.KeyAgeDays);
    }

    [Fact]
    public void GetRotationStatus_OverdueKey_HasNegativeDaysRemaining()
    {
        var status = HmacIntegrity.GetRotationStatus(DateTime.UtcNow.AddDays(-100));

        Assert.True(status.NeedsRotation);
        Assert.True(status.DaysUntilRotation < 0);
        Assert.Equal(100, status.KeyAgeDays);
    }
}
