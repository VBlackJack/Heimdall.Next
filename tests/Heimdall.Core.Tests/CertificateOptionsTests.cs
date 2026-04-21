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

using Heimdall.Core.Certificates;

namespace Heimdall.Core.Tests;

public sealed class CertificateOptionsTests
{
    [Fact]
    public void Validate_ReturnsOk_ForValidOptions()
    {
        var options = CreateValidOptions();

        var result = options.Validate();

        Assert.Equal(CertificateValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Validate_ReturnsCnRequired_ForEmptyCn()
    {
        var options = CreateValidOptions() with { Cn = string.Empty };

        var result = options.Validate();

        Assert.Equal(CertificateValidationCode.CnRequired, result.Code);
    }

    [Fact]
    public void Validate_ReturnsCnRequired_ForWhitespaceCn()
    {
        var options = CreateValidOptions() with { Cn = "   " };

        var result = options.Validate();

        Assert.Equal(CertificateValidationCode.CnRequired, result.Code);
    }

    [Fact]
    public void Validate_ReturnsInvalidValidity_ForZeroDays()
    {
        var options = CreateValidOptions() with { ValidityDays = 0 };

        var result = options.Validate();

        Assert.Equal(CertificateValidationCode.InvalidValidity, result.Code);
    }

    [Fact]
    public void Validate_ReturnsInvalidValidity_ForNegativeDays()
    {
        var options = CreateValidOptions() with { ValidityDays = -1 };

        var result = options.Validate();

        Assert.Equal(CertificateValidationCode.InvalidValidity, result.Code);
    }

    [Fact]
    public void Validate_AllowsEmptyOrgAndCountry()
    {
        var options = CreateValidOptions() with { Org = string.Empty, Country = string.Empty };

        var result = options.Validate();

        Assert.Equal(CertificateValidationCode.Ok, result.Code);
    }

    private static CertificateOptions CreateValidOptions() =>
        new("server.local", "Heimdall", "FR", CertificateGenerator.Rsa2048KeySize, 365, []);
}
