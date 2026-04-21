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

using Heimdall.Core.SystemInfo;

namespace Heimdall.Core.Tests;

public sealed class PowershellServiceCsvParserTests
{
    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        Assert.Empty(PowershellServiceCsvParser.Parse(null));
    }

    [Fact]
    public void Parse_Whitespace_ReturnsEmpty()
    {
        Assert.Empty(PowershellServiceCsvParser.Parse(" \r\n\t "));
    }

    [Fact]
    public void Parse_ParsesEntriesAndSortsByDisplayName()
    {
        const string csv = """
"Name","DisplayName","Status","StartType"
"w32time","Windows Time","Running","Automatic"
"bits","Background Intelligent Transfer Service","Stopped","Manual"
""";

        var results = PowershellServiceCsvParser.Parse(csv);

        Assert.Equal(2, results.Count);
        Assert.Equal("Background Intelligent Transfer Service", results[0].DisplayName);
        Assert.Equal("Windows Time", results[1].DisplayName);
    }

    [Fact]
    public void Parse_SkipsMalformedLines()
    {
        const string csv = """
"Name","DisplayName","Status","StartType"
"w32time","Windows Time","Running"
"bits","Background Intelligent Transfer Service","Stopped","Manual"
""";

        var results = PowershellServiceCsvParser.Parse(csv);

        Assert.Single(results);
        Assert.Equal("bits", results[0].Name);
    }

    [Fact]
    public void Parse_HandlesCrLf()
    {
        const string csv = "\"Name\",\"DisplayName\",\"Status\",\"StartType\"\r\n\"w32time\",\"Windows Time\",\"Running\",\"Automatic\"\r\n";

        var results = PowershellServiceCsvParser.Parse(csv);

        Assert.Single(results);
        Assert.Equal("Automatic", results[0].StartType);
    }

    [Fact]
    public void ParseCsvLine_HandlesQuotedComma()
    {
        var fields = PowershellServiceCsvParser.ParseCsvLine("\"bits\",\"Background, Intelligent Transfer Service\",\"Stopped\",\"Manual\"");

        Assert.Equal(4, fields.Count);
        Assert.Equal("Background, Intelligent Transfer Service", fields[1]);
    }

    [Fact]
    public void ParseCsvLine_HandlesEscapedQuotes()
    {
        var fields = PowershellServiceCsvParser.ParseCsvLine("\"svc\",\"Display \"\"Quoted\"\" Name\",\"Running\",\"Automatic\"");

        Assert.Equal("Display \"Quoted\" Name", fields[1]);
    }

    [Fact]
    public void ParseCsvLine_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => PowershellServiceCsvParser.ParseCsvLine(null!));
    }
}
