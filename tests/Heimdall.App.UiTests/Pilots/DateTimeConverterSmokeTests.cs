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
public sealed class DateTimeConverterSmokeTests : UiTestBase<DateTimeConverterView>
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_DisplaysInputAndNowButton()
    {
        using var session = OpenTool();

        Assert.NotNull(session.FindByAutomationId("DateTime.InputField"));
        Assert.NotNull(session.FindByAutomationId("DateTime.NowButton"));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void NowButton_FillsInputAndShowsResults()
    {
        using var session = OpenTool();

        session.FindByAutomationId("DateTime.NowButton").AsButton().Invoke();

        WaitHelpers.WaitUntil(
            () => !string.IsNullOrWhiteSpace(session.FindByAutomationId("DateTime.InputField").AsTextBox().Text),
            "date-time input filled");
        WaitHelpers.WaitUntil(
            () => !string.IsNullOrWhiteSpace(session.FindByAutomationId("DateTime.UnixOutput").AsTextBox().Text),
            "date-time results visible");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void InputUnixTimestamp_ProducesIsoOutputs()
    {
        using var session = OpenTool();

        session.FindByAutomationId("DateTime.InputField").AsTextBox().Text = "1700000000";

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("DateTime.UnixOutput").AsTextBox().Text,
            "1700000000",
            "unix output preserved");
        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("DateTime.IsoUtcOutput").AsTextBox().Text,
            value => value.StartsWith("2023-", StringComparison.Ordinal),
            "ISO UTC output");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyUnixButton_CopiesOutput()
    {
        using var session = OpenTool();

        session.FindByAutomationId("DateTime.NowButton").AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => !string.IsNullOrWhiteSpace(session.FindByAutomationId("DateTime.UnixOutput").AsTextBox().Text),
            "date-time unix output");

        session.FindByAutomationId("DateTime.CopyUnixButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            ReadClipboardText,
            session.FindByAutomationId("DateTime.UnixOutput").AsTextBox().Text,
            "date-time unix clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("DateTime.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("DateTime.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "date-time help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("DateTime.CloseHelpButton")),
            "date-time help panel collapsed");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void LocaleSwitch_UpdatesNowButtonLabel()
    {
        using var session = OpenTool();

        WpfTestHost.SwitchLocale("fr");
        WaitHelpers.WaitUntilTextEquals(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.Button)control.FindName("BtnNow")).Content?.ToString() ?? string.Empty),
            WpfTestHost.Translate("ToolDateTimeBtnNow"),
            "date-time now button in fr");
    }

    private static bool IsCollapsed(AutomationElement? element)
        => element is null || element.Properties.IsOffscreen.ValueOrDefault;
}
