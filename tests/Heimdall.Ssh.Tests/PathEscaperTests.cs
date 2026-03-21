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

namespace Heimdall.Ssh.Tests;

public class PathEscaperTests
{
    // ── Basic escaping ──────────────────────────────────────────────────

    [Fact]
    public void EscapeForShell_SimplePath_WrapsInSingleQuotes()
    {
        var result = PathEscaper.EscapeForShell("/home/user/file.txt");

        Assert.Equal("'/home/user/file.txt'", result);
    }

    [Fact]
    public void EscapeForShell_PathWithSpaces_WrapsCorrectly()
    {
        var result = PathEscaper.EscapeForShell("/home/user/my documents/file.txt");

        Assert.Equal("'/home/user/my documents/file.txt'", result);
    }

    // ── Single-quote escaping (CWE-78 prevention) ───────────────────────

    [Fact]
    public void EscapeForShell_PathWithSingleQuote_EscapesCorrectly()
    {
        var result = PathEscaper.EscapeForShell("/home/user/it's a file.txt");

        // Expected: '/home/user/it'\''s a file.txt'
        Assert.Equal(@"'/home/user/it'\''s a file.txt'", result);
    }

    [Fact]
    public void EscapeForShell_PathWithMultipleSingleQuotes_EscapesAll()
    {
        var result = PathEscaper.EscapeForShell("it's Bob's file");

        Assert.Equal(@"'it'\''s Bob'\''s file'", result);
    }

    // ── Shell injection prevention ──────────────────────────────────────

    [Theory]
    [InlineData("/tmp/$(whoami)")]
    [InlineData("/tmp/`id`")]
    [InlineData("/tmp/; rm -rf /")]
    [InlineData("/tmp/$HOME")]
    [InlineData("/tmp/file && echo pwned")]
    [InlineData("/tmp/file | cat /etc/passwd")]
    public void EscapeForShell_InjectionPayloads_AreSafelyQuoted(string maliciousPath)
    {
        var result = PathEscaper.EscapeForShell(maliciousPath);

        // The result should be wrapped in single quotes, making shell
        // metacharacters literal. No unescaped single quotes should exist.
        Assert.StartsWith("'", result);
        Assert.EndsWith("'", result);
    }

    // ── Control character rejection ─────────────────────────────────────

    [Fact]
    public void EscapeForShell_ControlCharacter_ThrowsArgumentException()
    {
        var pathWithNull = "/home/user/\0evil";

        var ex = Assert.Throws<ArgumentException>(
            () => PathEscaper.EscapeForShell(pathWithNull));
        Assert.Contains("control character", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EscapeForShell_NewlineInPath_ThrowsArgumentException()
    {
        var pathWithNewline = "/home/user/line1\nline2";

        Assert.Throws<ArgumentException>(
            () => PathEscaper.EscapeForShell(pathWithNewline));
    }

    [Fact]
    public void EscapeForShell_TabInPath_ThrowsArgumentException()
    {
        var pathWithTab = "/home/user/\tfile";

        Assert.Throws<ArgumentException>(
            () => PathEscaper.EscapeForShell(pathWithTab));
    }

    // ── Null argument ───────────────────────────────────────────────────

    [Fact]
    public void EscapeForShell_Null_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => PathEscaper.EscapeForShell(null!));
    }

    // ── Edge cases ──────────────────────────────────────────────────────

    [Fact]
    public void EscapeForShell_EmptyString_WrapsInQuotes()
    {
        var result = PathEscaper.EscapeForShell("");

        Assert.Equal("''", result);
    }

    [Fact]
    public void EscapeForShell_DoubleQuotes_ArePreservedLiterally()
    {
        var result = PathEscaper.EscapeForShell("/tmp/\"quoted\"");

        // Double quotes inside single quotes are literal in shell
        Assert.Equal("'/tmp/\"quoted\"'", result);
    }

    [Fact]
    public void EscapeForShell_UnicodeCharacters_ArePreserved()
    {
        var result = PathEscaper.EscapeForShell("/home/user/fichier_accentue.txt");

        Assert.Equal("'/home/user/fichier_accentue.txt'", result);
    }

    // ── Extended edge cases ─────────────────────────────────────────────

    [Fact]
    public void EscapeForShell_PathWithSpaces_EscapedInSingleQuotes()
    {
        var result = PathEscaper.EscapeForShell("/path/with spaces/file.txt");

        Assert.Equal("'/path/with spaces/file.txt'", result);
    }

    [Fact]
    public void EscapeForShell_PathWithJapaneseCharacters_Preserved()
    {
        var result = PathEscaper.EscapeForShell("/path/\u65E5\u672C\u8A9E/file.txt");

        Assert.Equal("'/path/\u65E5\u672C\u8A9E/file.txt'", result);
    }

    [Fact]
    public void EscapeForShell_PathWithCJKCharacters_Preserved()
    {
        var result = PathEscaper.EscapeForShell("/home/\u4E2D\u6587/\u6587\u4EF6.txt");

        Assert.Equal("'/home/\u4E2D\u6587/\u6587\u4EF6.txt'", result);
    }

    [Fact]
    public void EscapeForShell_PathWithDollarSign_QuotedLiterally()
    {
        var result = PathEscaper.EscapeForShell("/path/$HOME/file");

        Assert.StartsWith("'", result);
        Assert.EndsWith("'", result);
        Assert.Contains("$HOME", result);
    }

    [Fact]
    public void EscapeForShell_PathWithBackticks_QuotedLiterally()
    {
        var result = PathEscaper.EscapeForShell("/path/`command`/file");

        Assert.StartsWith("'", result);
        Assert.EndsWith("'", result);
        Assert.Contains("`command`", result);
    }

    [Fact]
    public void EscapeForShell_PathWithOnlySpecialChars()
    {
        // Characters that are not control characters but are shell-special
        var result = PathEscaper.EscapeForShell("$HOME;rm -rf /");

        Assert.StartsWith("'", result);
        Assert.EndsWith("'", result);
    }

    [Fact]
    public void EscapeForShell_VeryLongPath()
    {
        var longPath = "/" + string.Join("/", Enumerable.Repeat("segment", 100));
        var result = PathEscaper.EscapeForShell(longPath);

        Assert.StartsWith("'", result);
        Assert.EndsWith("'", result);
        Assert.Contains("segment", result);
    }

    [Fact]
    public void EscapeForShell_PathWithBackslash()
    {
        var result = PathEscaper.EscapeForShell("/path/with\\backslash");

        Assert.StartsWith("'", result);
        Assert.EndsWith("'", result);
    }

    [Fact]
    public void EscapeForShell_PathWithHash_QuotedLiterally()
    {
        var result = PathEscaper.EscapeForShell("/path/file#backup");

        Assert.Equal("'/path/file#backup'", result);
    }

    [Fact]
    public void EscapeForShell_PathWithTilde_QuotedLiterally()
    {
        var result = PathEscaper.EscapeForShell("~/documents/file.txt");

        Assert.Equal("'~/documents/file.txt'", result);
    }

    [Fact]
    public void EscapeForShell_PathWithAsterisk_QuotedLiterally()
    {
        var result = PathEscaper.EscapeForShell("/tmp/*.log");

        Assert.Equal("'/tmp/*.log'", result);
    }

    [Fact]
    public void EscapeForShell_PathWithQuestionMark_QuotedLiterally()
    {
        var result = PathEscaper.EscapeForShell("/tmp/file?.txt");

        Assert.Equal("'/tmp/file?.txt'", result);
    }
}
