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

using Heimdall.App.Services;

namespace Heimdall.App.Tests;

public sealed class UlidGeneratorToolServiceTests
{
    private readonly UlidGeneratorToolService _service = new();

    [Fact]
    public void Generate_ReturnsTwentySixCharacters()
    {
        Assert.Equal(26, _service.Generate().Length);
    }

    [Fact]
    public void Generate_ReturnsDistinctValuesAcrossTenCalls()
    {
        var values = Enumerable.Range(0, 10).Select(_ => _service.Generate()).ToArray();
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generate_UsesCrockfordAlphabet()
    {
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{26}$", _service.Generate());
    }
}
