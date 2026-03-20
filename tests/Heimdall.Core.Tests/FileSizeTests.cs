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

using System.Globalization;
using Heimdall.Core.Utilities;

namespace Heimdall.Core.Tests;

public class FileSizeTests : IDisposable
{
    private readonly CultureInfo _originalCulture;

    public FileSizeTests()
    {
        _originalCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        Thread.CurrentThread.CurrentCulture = _originalCulture;
    }

    // ── Zero bytes ──────────────────────────────────────────────────────

    [Fact]
    public void Format_ZeroBytes_ReturnsZeroB()
    {
        Assert.Equal("0 B", FileSize.Format(0));
    }

    // ── Bytes (< 1024) ─────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "1 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    public void Format_Bytes_ReturnsWholeNumberWithB(long bytes, string expected)
    {
        Assert.Equal(expected, FileSize.Format(bytes));
    }

    // ── Kilobytes ───────────────────────────────────────────────────────

    [Fact]
    public void Format_ExactlyOneKB_Returns1Point0KB()
    {
        Assert.Equal("1.0 KB", FileSize.Format(1024));
    }

    [Fact]
    public void Format_1536Bytes_Returns1Point5KB()
    {
        Assert.Equal("1.5 KB", FileSize.Format(1536));
    }

    // ── Megabytes ───────────────────────────────────────────────────────

    [Fact]
    public void Format_ExactlyOneMB_Returns1Point0MB()
    {
        Assert.Equal("1.0 MB", FileSize.Format(1024L * 1024));
    }

    [Fact]
    public void Format_FractionalMB()
    {
        // 2.5 MB = 2.5 * 1024 * 1024 = 2621440
        Assert.Equal("2.5 MB", FileSize.Format(2621440));
    }

    // ── Gigabytes ───────────────────────────────────────────────────────

    [Fact]
    public void Format_ExactlyOneGB_Returns1Point0GB()
    {
        Assert.Equal("1.0 GB", FileSize.Format(1024L * 1024 * 1024));
    }

    // ── Terabytes ───────────────────────────────────────────────────────

    [Fact]
    public void Format_ExactlyOneTB_Returns1Point0TB()
    {
        Assert.Equal("1.0 TB", FileSize.Format(1024L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Format_LargeValueAboveTB_StaysInTB()
    {
        long fiveTb = 5L * 1024 * 1024 * 1024 * 1024;
        Assert.Equal("5.0 TB", FileSize.Format(fiveTb));
    }

    // ── Negative values ─────────────────────────────────────────────────

    [Fact]
    public void Format_NegativeBytes_FormatsWithMinus()
    {
        var result = FileSize.Format(-512);
        // Negative value stays in bytes since -512 < 1024
        Assert.Equal("-512 B", result);
    }

    [Fact]
    public void Format_NegativeKB_StaysInBytes()
    {
        // Negative values don't trigger the >= 1024 loop, so they stay in bytes
        var result = FileSize.Format(-1536);
        Assert.Equal("-1536 B", result);
    }

    // ── Exact powers of 1024 ────────────────────────────────────────────

    [Theory]
    [InlineData(1L, "1 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1024L * 1024, "1.0 MB")]
    [InlineData(1024L * 1024 * 1024, "1.0 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1.0 TB")]
    public void Format_ExactPowersOf1024_ReturnsCleanUnit(long bytes, string expected)
    {
        Assert.Equal(expected, FileSize.Format(bytes));
    }
}
