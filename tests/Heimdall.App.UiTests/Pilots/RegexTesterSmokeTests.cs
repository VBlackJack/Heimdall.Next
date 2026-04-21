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

using FlaUI.Core.AutomationElements;
using Heimdall.App.UiTests.Infrastructure;
using Heimdall.App.Views.Tools;

namespace Heimdall.App.UiTests.Pilots;

[Collection(DesktopUiCollection.Name)]
public sealed class RegexTesterSmokeTests : UiTestBase<RegexTesterView>
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_DisplaysInteractiveControls()
    {
        using var session = OpenTool();

        Assert.NotNull(session.FindByAutomationId("Regex.PatternText"));
        Assert.NotNull(session.FindByAutomationId("Regex.TestText"));
        Assert.NotNull(session.FindByAutomationId("Regex.IgnoreCaseCheck"));
        Assert.NotNull(session.FindByAutomationId("Regex.MultilineCheck"));
        Assert.NotNull(session.FindByAutomationId("Regex.SinglelineCheck"));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void TypingPattern_AndTestText_ShowsHighlightDisplay()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Regex.PatternText").AsTextBox().Text = "\\d+";
        session.FindByAutomationId("Regex.TestText").AsTextBox().Text = "a1b22c333";

        var highlight = session.FindByAutomationId("Regex.HighlightDisplay");
        WaitHelpers.WaitUntilVisible(highlight, "regex highlight display");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void IgnoreCaseCheck_Toggles()
    {
        using var session = OpenTool();

        var checkbox = session.FindByAutomationId("Regex.IgnoreCaseCheck").AsCheckBox();
        var initial = checkbox.IsChecked;

        checkbox.Toggle();

        WaitHelpers.WaitUntil(
            () => session.FindByAutomationId("Regex.IgnoreCaseCheck").AsCheckBox().IsChecked != initial,
            "regex ignore-case toggle");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void MultilineCheck_Toggles()
    {
        using var session = OpenTool();

        var checkbox = session.FindByAutomationId("Regex.MultilineCheck").AsCheckBox();
        var initial = checkbox.IsChecked;

        checkbox.Toggle();

        WaitHelpers.WaitUntil(
            () => session.FindByAutomationId("Regex.MultilineCheck").AsCheckBox().IsChecked != initial,
            "regex multiline toggle");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Regex.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("Regex.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "regex help panel visible");

        closeButton.AsButton().Invoke();

        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("Regex.CloseHelpButton")),
            "regex help panel collapsed");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void LocaleSwitch_UpdatesIgnoreCaseLabel()
    {
        using var session = OpenTool();

        WpfTestHost.SwitchLocale("fr");
        WaitHelpers.WaitUntilTextEquals(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.CheckBox)control.FindName("ChkIgnoreCase")).Content?.ToString() ?? string.Empty),
            WpfTestHost.Translate("ToolRegexIgnoreCase"),
            "regex ignore-case name in fr");
    }

    private static bool IsCollapsed(AutomationElement? element)
        => element is null || element.Properties.IsOffscreen.ValueOrDefault;
}
