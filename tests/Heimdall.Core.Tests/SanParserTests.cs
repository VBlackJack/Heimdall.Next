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

public sealed class SanParserTests
{
    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(SanParser.Parse(string.Empty));
    }

    [Fact]
    public void Parse_WhitespaceInput_ReturnsEmpty()
    {
        Assert.Empty(SanParser.Parse("  ,   "));
    }

    [Fact]
    public void Parse_SingleEntry_ReturnsOne()
    {
        var values = SanParser.Parse("server.local");

        Assert.Equal(["server.local"], values);
    }

    [Fact]
    public void Parse_CommaSeparated_ReturnsMultiple()
    {
        var values = SanParser.Parse("server.local,api.local,10.0.0.1");

        Assert.Equal(["server.local", "api.local", "10.0.0.1"], values);
    }

    [Fact]
    public void Parse_TrimsWhitespace()
    {
        var values = SanParser.Parse(" server.local , api.local ");

        Assert.Equal(["server.local", "api.local"], values);
    }

    [Fact]
    public void Parse_RemovesEmptyEntries()
    {
        var values = SanParser.Parse("server.local, , ,,10.0.0.1");

        Assert.Equal(["server.local", "10.0.0.1"], values);
    }
}
