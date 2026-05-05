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

using Heimdall.Sftp;

namespace Heimdall.App.Tests;

public sealed class FtpBrowserParsingTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("/", "/")]
    [InlineData("/var/log", "/var/log")]
    [InlineData("var/log", "/var/log")]
    [InlineData("/var/log/", "/var/log")]
    [InlineData("/var/log///", "/var/log")]
    public void NormalizePath_ProducesCanonicalForm(string? input, string expected)
    {
        Assert.Equal(expected, FtpBrowser.NormalizePath(input!));
    }

    [Theory]
    [InlineData("/etc/passwd", "/var/log", "/etc/passwd")]
    [InlineData("logs", "/var", "/var/logs")]
    [InlineData("logs", "/", "/logs")]
    [InlineData("../etc", "/var/log", "/var/log/../etc")]
    public void ResolvePath_HandlesAbsoluteAndRelative(
        string input,
        string currentDirectory,
        string expected)
    {
        Assert.Equal(expected, FtpBrowser.ResolvePath(input, currentDirectory));
    }

    [Fact]
    public void ParseListLine_UnixFile_ParsedCorrectly()
    {
        const string line = "-rw-r--r-- 1 root root 4096 Jan 15 12:34 hosts";

        var entry = FtpBrowser.ParseListLine(line, "/etc");

        Assert.NotNull(entry);
        Assert.Equal("hosts", entry!.Name);
        Assert.Equal("/etc/hosts", entry.FullPath);
        Assert.False(entry.IsDirectory);
        Assert.Equal(4096, entry.Size);
        Assert.Equal("root", entry.Owner);
        Assert.Equal("root", entry.Group);
        Assert.Equal("rw-r--r--", entry.Permissions);
    }

    [Fact]
    public void ParseListLine_UnixDirectory_ParsedCorrectly()
    {
        const string line = "drwxr-xr-x 2 user staff 4096 Mar 10 09:00 archive";

        var entry = FtpBrowser.ParseListLine(line, "/srv");

        Assert.NotNull(entry);
        Assert.True(entry!.IsDirectory);
        Assert.Equal(0, entry.Size);
        Assert.Equal("archive", entry.Name);
        Assert.Equal("/srv/archive", entry.FullPath);
    }

    [Fact]
    public void ParseListLine_DosFile_ParsedCorrectly()
    {
        const string line = "01-15-26  12:34PM             1024 readme.txt";

        var entry = FtpBrowser.ParseListLine(line, "/");

        Assert.NotNull(entry);
        Assert.False(entry!.IsDirectory);
        Assert.Equal(1024, entry.Size);
        Assert.Equal("readme.txt", entry.Name);
        Assert.Equal("/readme.txt", entry.FullPath);
    }

    [Fact]
    public void ParseListLine_DosDirectory_ParsedCorrectly()
    {
        const string line = "01-15-26  12:34PM       <DIR>          subfolder";

        var entry = FtpBrowser.ParseListLine(line, "/");

        Assert.NotNull(entry);
        Assert.True(entry!.IsDirectory);
        Assert.Equal(0, entry.Size);
        Assert.Equal("subfolder", entry.Name);
        Assert.Equal("/subfolder", entry.FullPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage line")]
    [InlineData("-rw-r--r-- 1 root root")]
    public void ParseListLine_MalformedLine_ReturnsNull(string line)
    {
        Assert.Null(FtpBrowser.ParseListLine(line, "/"));
    }

    [Fact]
    public void ParseListLine_FilenameExceedingMaxLength_ReturnsNull()
    {
        var giantName = new string('a', FtpBrowser.MaxFtpFilenameLength + 1);
        var line = $"-rw-r--r-- 1 user user 1 Jan 01 12:00 {giantName}";

        Assert.Null(FtpBrowser.ParseListLine(line, "/"));
    }

    [Fact]
    public void ParseUnixDate_TimeFormat_PreservesMonthAndDay()
    {
        var result = FtpBrowser.ParseUnixDate("Mar 15 09:00");

        Assert.NotEqual(DateTime.MinValue, result);
        Assert.Equal(3, result.Month);
        Assert.Equal(15, result.Day);
        Assert.Equal(9, result.Hour);
        Assert.Equal(0, result.Minute);
    }

    [Fact]
    public void ParseUnixDate_YearFormat_UsesParsedYear()
    {
        var result = FtpBrowser.ParseUnixDate("Mar 15  2024");

        Assert.Equal(2024, result.Year);
        Assert.Equal(3, result.Month);
        Assert.Equal(15, result.Day);
    }

    [Fact]
    public void ParseUnixDate_FutureDateInTimeFormat_RollsBackOneYear()
    {
        var result = FtpBrowser.ParseUnixDate("Dec 31 23:59");
        var now = DateTime.Now;
        var currentYearCandidate = new DateTime(now.Year, 12, 31, 23, 59, 0);

        Assert.Equal(currentYearCandidate > now ? now.Year - 1 : now.Year, result.Year);
        Assert.Equal(12, result.Month);
        Assert.Equal(31, result.Day);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("")]
    [InlineData("Foo 99 99:99")]
    public void ParseUnixDate_Garbage_ReturnsMinValue(string input)
    {
        Assert.Equal(DateTime.MinValue, FtpBrowser.ParseUnixDate(input));
    }
}
