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

using System.Runtime.Versioning;
using System.Security.Cryptography;
using Heimdall.Core.Security;

namespace Heimdall.Core.Tests;

[SupportedOSPlatform("windows")]
public class DpapiProviderTests
{
    // ── Protect + Unprotect round-trip ──────────────────────────────────

    [Theory]
    [InlineData("hello world")]
    [InlineData("my-secret-password-123!")]
    [InlineData("unicode: \u00e9\u00e0\u00fc\u00f1 \u4e16\u754c")]
    public void ProtectAndUnprotect_RoundTrip_ReturnsOriginal(string plaintext)
    {
        var encrypted = DpapiProvider.Protect(plaintext);
        var decrypted = DpapiProvider.Unprotect(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    // ── Different inputs produce different outputs ─────────────────────

    [Fact]
    public void Protect_DifferentInputs_ProducesDifferentOutputs()
    {
        var a = DpapiProvider.Protect("alpha");
        var b = DpapiProvider.Protect("bravo");

        Assert.NotEqual(a, b);
    }

    // ── Same input produces different outputs (DPAPI nonce) ────────────

    [Fact]
    public void Protect_SameInputTwice_ProducesDifferentOutputs()
    {
        var a = DpapiProvider.Protect("same-input");
        var b = DpapiProvider.Protect("same-input");

        Assert.NotEqual(a, b);
    }

    // ── Unprotect invalid base64 throws FormatException ────────────────

    [Fact]
    public void Unprotect_InvalidBase64_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() =>
            DpapiProvider.Unprotect("not-valid-base64!!!"));
    }

    // ── Unprotect tampered data throws CryptographicException ──────────

    [Fact]
    public void Unprotect_TamperedData_ThrowsCryptographicException()
    {
        var encrypted = DpapiProvider.Protect("secret");

        // Tamper with the ciphertext by flipping bytes
        var bytes = Convert.FromBase64String(encrypted);
        bytes[0] ^= 0xFF;
        bytes[1] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes);

        Assert.Throws<CryptographicException>(() =>
            DpapiProvider.Unprotect(tampered));
    }

    // ── Protect null throws ArgumentNullException ──────────────────────

    [Fact]
    public void Protect_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DpapiProvider.Protect(null!));
    }

    // ── Unprotect null throws ArgumentNullException ────────────────────

    [Fact]
    public void Unprotect_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DpapiProvider.Unprotect(null!));
    }

    // ── Protect empty string succeeds ──────────────────────────────────

    [Fact]
    public void Protect_EmptyString_Succeeds()
    {
        var encrypted = DpapiProvider.Protect(string.Empty);
        var decrypted = DpapiProvider.Unprotect(encrypted);

        Assert.Equal(string.Empty, decrypted);
    }

    // ── Protected output is valid base64 ───────────────────────────────

    [Fact]
    public void Protect_Output_IsValidBase64()
    {
        var encrypted = DpapiProvider.Protect("test-data");

        var bytes = Convert.FromBase64String(encrypted);
        Assert.True(bytes.Length > 0);
    }

    // ── UnprotectToSecureString round-trip ──────────────────────────────

    [Fact]
    public void UnprotectToSecureString_RoundTrip_PreservesPlaintext()
    {
        var plaintext = "secure-password-456";
        var encrypted = DpapiProvider.Protect(plaintext);

        using var secure = DpapiProvider.UnprotectToSecureString(encrypted);
        var result = DpapiProvider.ConvertFromSecureString(secure);

        Assert.Equal(plaintext, result);
    }

    [Fact]
    public void UnprotectToSecureString_ReturnsReadOnlySecureString()
    {
        var encrypted = DpapiProvider.Protect("test");

        using var secure = DpapiProvider.UnprotectToSecureString(encrypted);

        Assert.True(secure.IsReadOnly());
    }

    [Fact]
    public void UnprotectToSecureString_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DpapiProvider.UnprotectToSecureString(null!));
    }

    // ── ConvertFromSecureString null throws ─────────────────────────────

    [Fact]
    public void ConvertFromSecureString_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DpapiProvider.ConvertFromSecureString(null!));
    }

    // ── TestHealth ──────────────────────────────────────────────────────

    [Fact]
    public void TestHealth_ReturnsTrue()
    {
        Assert.True(DpapiProvider.TestHealth());
    }
}
