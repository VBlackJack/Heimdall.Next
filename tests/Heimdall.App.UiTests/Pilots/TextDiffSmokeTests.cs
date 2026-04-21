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
public sealed class TextDiffSmokeTests : UiTestBase<TextDiffView>
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_DisplaysTwoInputFields()
    {
        using var session = OpenTool();

        Assert.NotNull(session.FindByAutomationId("TextDiff.OriginalText"));
        Assert.NotNull(session.FindByAutomationId("TextDiff.ModifiedText"));
        Assert.NotNull(session.FindByAutomationId("TextDiff.CompareButton"));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Compare_WithSimpleDiff_AllowsCopyingDiff()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TextDiff.OriginalText").AsTextBox().Text = "hello\r\nworld";
        session.FindByAutomationId("TextDiff.ModifiedText").AsTextBox().Text = "hello\r\nmonde";
        WaitHelpers.WaitUntil(
            () => session.FindByAutomationId("TextDiff.CompareButton").AsButton().IsEnabled,
            "text diff compare button enabled");
        session.FindByAutomationId("TextDiff.CompareButton").AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.Button)control.FindName("BtnCopyDiff")).IsEnabled),
            "text diff copy button enabled");

        WaitHelpers.WaitUntil(
            () =>
            {
                session.InvokeOnUi(control =>
                    ((System.Windows.Controls.Button)control.FindName("BtnCopyDiff"))
                        .RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)));
                var clipboard = ReadClipboardText();
                return clipboard.Contains("world", StringComparison.Ordinal)
                    && clipboard.Contains("monde", StringComparison.Ordinal);
            },
            "text diff copied to clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void SwapButton_ExchangesContent()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TextDiff.OriginalText").AsTextBox().Text = "A";
        session.FindByAutomationId("TextDiff.ModifiedText").AsTextBox().Text = "B";

        session.FindByAutomationId("TextDiff.SwapButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("TextDiff.OriginalText").AsTextBox().Text,
            "B",
            "original text swapped");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("TextDiff.ModifiedText").AsTextBox().Text,
            "A",
            "modified text swapped");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void ClearButton_EmptiesBothFields()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TextDiff.OriginalText").AsTextBox().Text = "left";
        session.FindByAutomationId("TextDiff.ModifiedText").AsTextBox().Text = "right";

        session.FindByAutomationId("TextDiff.ClearButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("TextDiff.OriginalText").AsTextBox().Text,
            string.Empty,
            "original text cleared");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("TextDiff.ModifiedText").AsTextBox().Text,
            string.Empty,
            "modified text cleared");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void AutoCompare_OnTyping_AllowsCopyingDiff()
    {
        using var session = OpenTool();

        var autoCompare = session.FindByAutomationId("TextDiff.AutoCompareCheck").AsCheckBox();
        if (autoCompare.IsChecked != true)
        {
            autoCompare.Toggle();
        }

        session.FindByAutomationId("TextDiff.OriginalText").AsTextBox().Text = "foo";
        session.FindByAutomationId("TextDiff.ModifiedText").AsTextBox().Text = "bar";
        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.Button)control.FindName("BtnCopyDiff")).IsEnabled),
            "text diff auto-compare copy button enabled");

        WaitHelpers.WaitUntil(
            () =>
            {
                session.InvokeOnUi(control =>
                    ((System.Windows.Controls.Button)control.FindName("BtnCopyDiff"))
                        .RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)));
                var clipboard = ReadClipboardText();
                return clipboard.Contains("foo", StringComparison.Ordinal)
                    && clipboard.Contains("bar", StringComparison.Ordinal);
            },
            "text diff auto-compare");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TextDiff.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("TextDiff.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "text diff help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("TextDiff.CloseHelpButton")),
            "text diff help panel collapsed");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void LocaleSwitch_UpdatesCompareButtonLabel()
    {
        using var session = OpenTool();

        WpfTestHost.SwitchLocale("fr");
        WaitHelpers.WaitUntilTextEquals(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.Button)control.FindName("BtnCompare")).Content?.ToString() ?? string.Empty),
            WpfTestHost.Translate("ToolDiffBtnCompare"),
            "text diff compare button in fr");
    }
}
