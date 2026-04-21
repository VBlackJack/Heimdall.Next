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

using Heimdall.Core.CronJob;

namespace Heimdall.Core.Tests;

public sealed class CrontabParserTests
{
    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        Assert.Empty(CrontabParser.Parse(null));
    }

    [Fact]
    public void Parse_Whitespace_ReturnsEmpty()
    {
        Assert.Empty(CrontabParser.Parse(" \r\n\t "));
    }

    [Fact]
    public void Parse_SkipsCommentsAndVariableAssignments()
    {
        const string input = """
# comment
SHELL=/bin/bash
MAILTO=root@example.com
*/5 * * * * /usr/bin/echo hello
""";

        var entries = CrontabParser.Parse(input);

        Assert.Single(entries);
        Assert.Equal("*/5", entries[0].Minute);
        Assert.Equal("/usr/bin/echo hello", entries[0].Command);
    }

    [Fact]
    public void Parse_VariableAssignmentWithSpaces_IsNotSkipped()
    {
        const string input = "0 0 * * * MAILTO = root@example.com";

        var entries = CrontabParser.Parse(input);

        Assert.Single(entries);
        Assert.Equal("MAILTO = root@example.com", entries[0].Command);
    }

    [Fact]
    public void Parse_TabsAndExtraSpaces_AreCollapsed()
    {
        const string input = "0\t12\t*\t*\t1-5\t/usr/bin/python   script.py  --flag";

        var entries = CrontabParser.Parse(input);

        Assert.Single(entries);
        Assert.Equal("0", entries[0].Minute);
        Assert.Equal("12", entries[0].Hour);
        Assert.Equal("/usr/bin/python   script.py  --flag", entries[0].Command);
    }

    [Fact]
    public void Parse_LinesWithTooFewFields_AreSkipped()
    {
        const string input = """
0 0 * *
0 0 * * * /ok
""";

        var entries = CrontabParser.Parse(input);

        Assert.Single(entries);
        Assert.Equal("/ok", entries[0].Command);
    }

    [Fact]
    public void Parse_PreservesTrimmedRawLine()
    {
        const string input = "   15 6 * * 0 /usr/bin/backup   ";

        var entries = CrontabParser.Parse(input);

        Assert.Single(entries);
        Assert.Equal("15 6 * * 0 /usr/bin/backup", entries[0].RawLine);
    }
}
