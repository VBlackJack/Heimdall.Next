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

using Heimdall.App.ViewModels;

namespace Heimdall.App.Tests;

public sealed class SidebarDisplayNameFormatterTests
{
    [Fact]
    public void Format_Null_ReturnsNull()
    {
        Assert.Null(SidebarDisplayNameFormatter.Format(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_EmptyOrWhitespace_ReturnsInput(string displayName)
    {
        Assert.Equal(displayName, SidebarDisplayNameFormatter.Format(displayName));
    }

    [Fact]
    public void Format_NoFinalParenthesizedSuffix_ReturnsInput()
    {
        const string displayName = "VeryLongServerNameWithoutAnySuffixAtAll";

        Assert.Equal(displayName, SidebarDisplayNameFormatter.Format(displayName));
    }

    [Fact]
    public void Format_AtMaxLength_ReturnsInput()
    {
        const string displayName = "TestEnv Linux A (SSH, admin via gateway)";

        Assert.Equal(displayName, SidebarDisplayNameFormatter.Format(displayName));
    }

    [Fact]
    public void Format_OneCharacterPastMaxLength_PreservesHeadAndTrimsSuffix()
    {
        const string displayName = "TestEnv Linux A (SSH, deploy via gateway)";

        Assert.Equal("TestEnv Linux A (SSH, deploy via gatew\u2026)", SidebarDisplayNameFormatter.Format(displayName));
    }

    [Fact]
    public void Format_LongSuffix_PreservesHeadAndTrimsSuffixAtEnd()
    {
        const string displayName = "TestEnv Linux A (SSH, admin via gateway, with extra detail)";

        Assert.Equal("TestEnv Linux A (SSH, admin via gatewa\u2026)", SidebarDisplayNameFormatter.Format(displayName));
    }

    [Fact]
    public void Format_HeadAloneExceedsMaxLength_ReturnsInput()
    {
        const string displayName = "VeryVeryLongHeadNameThatAlreadyExceedsFortyChars (x)";

        Assert.Equal(displayName, SidebarDisplayNameFormatter.Format(displayName));
    }

    [Fact]
    public void Format_HeadAlmostFillsBudget_DropsSuffix()
    {
        const string displayName = "AlmostFullHeadNameThatFillsBudget (whatever)";

        Assert.Equal("AlmostFullHeadNameThatFillsBudget", SidebarDisplayNameFormatter.Format(displayName));
    }

    [Fact]
    public void Format_NestedParenthesesInLongSuffix_PreservesHeadAndUsesOuterSuffix()
    {
        const string displayName = "Foo (bar (baz) with a much longer suffix detail)";

        Assert.Equal("Foo (bar (baz) with a much longer suff\u2026)", SidebarDisplayNameFormatter.Format(displayName));
    }

    [Fact]
    public void Format_OpenParenthesisWithoutClosing_ReturnsInput()
    {
        const string displayName = "Foo (incomplete";

        Assert.Equal(displayName, SidebarDisplayNameFormatter.Format(displayName));
    }

    [Fact]
    public void Format_SuffixOnly_ReturnsInput()
    {
        const string displayName = "(only suffix)";

        Assert.Equal(displayName, SidebarDisplayNameFormatter.Format(displayName));
    }

    [Fact]
    public void Format_CustomMaxLength_UsesRequestedTargetLength()
    {
        const string displayName = "Production Server (SSH via gateway)";

        Assert.Equal("Production Server (SSH v\u2026)", SidebarDisplayNameFormatter.Format(displayName, maxLength: 26));
    }

    [Fact]
    public void ServerItemViewModel_DisplayNameChange_RaisesSidebarDisplayName()
    {
        var vm = new ServerItemViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changed.Add(args.PropertyName);
            }
        };

        vm.DisplayName = "TestEnv Linux A (SSH, deploy via gateway)";

        Assert.Equal("TestEnv Linux A (SSH, deploy via gatew\u2026)", vm.SidebarDisplayName);
        Assert.Contains(nameof(ServerItemViewModel.SidebarDisplayName), changed);
    }
}
