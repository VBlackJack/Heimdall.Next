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

using Heimdall.App.ViewModels.Tools;

namespace Heimdall.App.Tests;

public sealed class DateTimeConverterTimezoneSearchableNameTests
{
    [Fact]
    public void BuildSearchableName_RomanceDisplayName_BiasesParis()
    {
        string displayName = "(UTC+01:00) Bruxelles, Copenhague, Madrid, Paris";

        string searchableName = DateTimeConverterViewModel.BuildSearchableName(displayName);

        Assert.Equal("Paris - (UTC+01:00) Bruxelles, Copenhague, Madrid, Paris", searchableName);
    }

    [Fact]
    public void BuildSearchableName_UtcDisplayName_BiasesSingleSegment()
    {
        string displayName = "(UTC) Coordinated Universal Time";

        string searchableName = DateTimeConverterViewModel.BuildSearchableName(displayName);

        Assert.Equal("Coordinated Universal Time - (UTC) Coordinated Universal Time", searchableName);
    }

    [Fact]
    public void BuildSearchableName_TokyoDisplayName_BiasesTokyo()
    {
        string displayName = "(UTC+09:00) Osaka, Sapporo, Tokyo";

        string searchableName = DateTimeConverterViewModel.BuildSearchableName(displayName);

        Assert.Equal("Tokyo - (UTC+09:00) Osaka, Sapporo, Tokyo", searchableName);
    }

    [Fact]
    public void BuildSearchableName_NoParenthesis_ReturnsDisplayName()
    {
        string displayName = "Custom Timezone Without Parens";

        string searchableName = DateTimeConverterViewModel.BuildSearchableName(displayName);

        Assert.Equal("Custom Timezone Without Parens", searchableName);
    }

    [Fact]
    public void BuildSearchableName_EmptyRemainder_ReturnsDisplayName()
    {
        string displayName = "(UTC+00:00)";

        string searchableName = DateTimeConverterViewModel.BuildSearchableName(displayName);

        Assert.Equal("(UTC+00:00)", searchableName);
    }

    [Fact]
    public void BuildSearchableName_OnlyEmptySegments_ReturnsDisplayName()
    {
        string displayName = "(UTC+00:00) , , ,";

        string searchableName = DateTimeConverterViewModel.BuildSearchableName(displayName);

        Assert.Equal("(UTC+00:00) , , ,", searchableName);
    }
}
