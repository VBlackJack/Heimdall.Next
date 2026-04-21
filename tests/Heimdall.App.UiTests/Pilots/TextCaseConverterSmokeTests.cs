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
public sealed class TextCaseConverterSmokeTests : UiTestBase<TextCaseConverterView>
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_AndLocaleSwitch_UpdatesCamelLabel()
    {
        using var session = OpenTool();

        Assert.NotNull(session.FindByAutomationId("TextCase.InputField"));
        Assert.NotNull(session.FindByAutomationId("TextCase.CamelButton"));
        Assert.NotNull(session.FindByAutomationId("TextCase.UpperButton"));
        Assert.NotNull(session.FindByAutomationId("TextCase.KebabButton"));

        WpfTestHost.SwitchLocale("fr");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("TextCase.CamelButton").Name,
            WpfTestHost.Translate("ToolTextCaseCamel"),
            "text case camel button in fr");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CamelButton_ConvertsInput()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TextCase.InputField").AsTextBox().Text = "hello world";
        session.FindByAutomationId("TextCase.CamelButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("TextCase.OutputField").AsTextBox().Text,
            "helloWorld",
            "text case camel output");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void UpperButton_ConvertsInput()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TextCase.InputField").AsTextBox().Text = "hello world";
        session.FindByAutomationId("TextCase.UpperButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("TextCase.OutputField").AsTextBox().Text,
            "HELLO WORLD",
            "text case upper output");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void KebabButton_ConvertsInput()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TextCase.InputField").AsTextBox().Text = "hello world";
        session.FindByAutomationId("TextCase.KebabButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("TextCase.OutputField").AsTextBox().Text,
            "hello-world",
            "text case kebab output");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyButton_CopiesOutput()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TextCase.InputField").AsTextBox().Text = "hello world";
        session.FindByAutomationId("TextCase.KebabButton").AsButton().Invoke();
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("TextCase.OutputField").AsTextBox().Text,
            "hello-world",
            "text case output before copy");

        session.FindByAutomationId("TextCase.CopyButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            ReadClipboardText,
            "hello-world",
            "text case clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TextCase.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("TextCase.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "text case help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("TextCase.CloseHelpButton")),
            "text case help panel collapsed");
    }
}
