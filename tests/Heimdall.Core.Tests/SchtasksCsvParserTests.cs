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

public sealed class SchtasksCsvParserTests
{
    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        Assert.Empty(SchtasksCsvParser.Parse(null));
    }

    [Fact]
    public void Parse_HeaderOnly_ReturnsEmpty()
    {
        Assert.Empty(SchtasksCsvParser.Parse("\"TaskName\",\"Status\""));
    }

    [Fact]
    public void Parse_ParsesSingleRow()
    {
        const string csv = """
"TaskName","Status","Next Run Time","Last Run Time","Last Result"
"\My Task","Ready","4/18/2026 12:00:00","4/17/2026 12:00:00","0"
""";

        var tasks = SchtasksCsvParser.Parse(csv);

        Assert.Single(tasks);
        Assert.Equal(@"\My Task", tasks[0].Name);
        Assert.Equal("Ready", tasks[0].Status);
    }

    [Fact]
    public void Parse_SkipsRowsWithoutTaskName()
    {
        const string csv = """
"TaskName","Status"
"","Ready"
"\Valid","Running"
""";

        var tasks = SchtasksCsvParser.Parse(csv);

        Assert.Single(tasks);
        Assert.Equal(@"\Valid", tasks[0].Name);
    }

    [Fact]
    public void ParseCsvLine_HandlesEscapedQuotesAndCommas()
    {
        var fields = SchtasksCsvParser.ParseCsvLine("\"task\",\"Ready\",\"He said \"\"Hello\"\", world\"");

        Assert.Equal(3, fields.Count);
        Assert.Equal("He said \"Hello\", world", fields[2]);
    }

    [Fact]
    public void FindColumnIndex_UsesPartialMatch()
    {
        var header = new List<string> { "HostName", "TaskName", "Next Run Time  " };

        Assert.Equal(2, SchtasksCsvParser.FindColumnIndex(header, "Next Run Time"));
    }

    [Fact]
    public void GetField_OutOfRange_ReturnsEmpty()
    {
        var fields = new List<string> { "a", "b" };

        Assert.Equal(string.Empty, SchtasksCsvParser.GetField(fields, 10));
    }
}
