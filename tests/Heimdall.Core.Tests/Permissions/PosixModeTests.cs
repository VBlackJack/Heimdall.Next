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

using Heimdall.Core.Permissions;

namespace Heimdall.Core.Tests;

public sealed class PosixModeTests
{
    [Theory]
    [InlineData(-1, 0, 0, "owner")]
    [InlineData(0, 8, 0, "group")]
    [InlineData(0, 0, 9, "others")]
    public void Ctor_InvalidDigit_Throws(int owner, int group, int others, string paramName)
    {
        Action act = () => _ = new PosixMode(owner, group, others);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(act);
        Assert.Equal(paramName, ex.ParamName);
    }

    [Theory]
    [InlineData("755", 7, 5, 5)]
    [InlineData("644", 6, 4, 4)]
    [InlineData("000", 0, 0, 0)]
    [InlineData("777", 7, 7, 7)]
    public void TryParseOctal_Valid_ReturnsMode(string octal, int owner, int group, int others)
    {
        var success = PosixMode.TryParseOctal(octal, out var mode);

        Assert.True(success);
        Assert.Equal(owner, mode.Owner);
        Assert.Equal(group, mode.Group);
        Assert.Equal(others, mode.Others);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("75")]
    [InlineData("7555")]
    [InlineData("78a")]
    [InlineData("abc")]
    public void TryParseOctal_Invalid_ReturnsFalse(string? octal)
    {
        var success = PosixMode.TryParseOctal(octal, out var mode);

        Assert.False(success);
        Assert.Equal(default, mode);
    }

    [Fact]
    public void FromOctal_Invalid_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => PosixMode.FromOctal("999"));
    }

    [Theory]
    [InlineData("755", "rwxr-xr-x")]
    [InlineData("644", "rw-r--r--")]
    [InlineData("600", "rw-------")]
    [InlineData("700", "rwx------")]
    public void ToSymbolic_ReturnsExpectedString(string octal, string symbolic)
    {
        var mode = PosixMode.FromOctal(octal);

        Assert.Equal(symbolic, mode.ToSymbolic());
    }

    [Fact]
    public void WithBit_EnableAndDisable_ReturnsNewMode()
    {
        var mode = PosixMode.Empty
            .WithBit(PosixRole.Owner, PosixPermission.Read, true)
            .WithBit(PosixRole.Group, PosixPermission.Write, true)
            .WithBit(PosixRole.Others, PosixPermission.Execute, true)
            .WithBit(PosixRole.Group, PosixPermission.Write, false);

        Assert.Equal("401", mode.ToOctal());
    }

    [Fact]
    public void Accessors_ReflectBits()
    {
        var mode = PosixMode.FromOctal("751");

        Assert.True(mode.OwnerRead);
        Assert.True(mode.OwnerWrite);
        Assert.True(mode.OwnerExecute);
        Assert.True(mode.GroupRead);
        Assert.False(mode.GroupWrite);
        Assert.True(mode.GroupExecute);
        Assert.False(mode.OthersRead);
        Assert.False(mode.OthersWrite);
        Assert.True(mode.OthersExecute);
    }

    [Theory]
    [InlineData("644")]
    [InlineData("755")]
    [InlineData("600")]
    [InlineData("700")]
    [InlineData("777")]
    [InlineData("000")]
    public void ToOctal_RoundTrips(string octal)
    {
        var mode = PosixMode.FromOctal(octal);

        Assert.Equal(octal, mode.ToOctal());
    }

    [Fact]
    public void Presets_ExposeExpectedValues()
    {
        Assert.Equal("000", PosixMode.Empty.ToOctal());
        Assert.Equal("644", PosixMode.Preset644.ToOctal());
        Assert.Equal("755", PosixMode.Preset755.ToOctal());
        Assert.Equal("600", PosixMode.Preset600.ToOctal());
        Assert.Equal("700", PosixMode.Preset700.ToOctal());
        Assert.Equal("777", PosixMode.Preset777.ToOctal());
    }
}
