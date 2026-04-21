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
public sealed class JsonFormatterSmokeTests : UiTestBase<JsonFormatterView>
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_AndLocaleSwitch_UpdatesPrettifyLabel()
    {
        using var session = OpenTool();

        Assert.NotNull(session.FindByAutomationId("JsonFormatter.InputField"));
        Assert.NotNull(session.FindByAutomationId("JsonFormatter.PrettifyButton"));
        Assert.NotNull(session.FindByAutomationId("JsonFormatter.MinifyButton"));

        WpfTestHost.SwitchLocale("fr");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("JsonFormatter.PrettifyButton").Name,
            WpfTestHost.Translate("ToolJsonBtnPrettify"),
            "json formatter prettify button in fr");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void PrettifyButton_FormatsJson()
    {
        using var session = OpenTool();

        session.FindByAutomationId("JsonFormatter.InputField").AsTextBox().Text = "{\"a\":1}";
        session.FindByAutomationId("JsonFormatter.PrettifyButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("JsonFormatter.OutputField").AsTextBox().Text,
            value => value.Contains(Environment.NewLine, StringComparison.Ordinal) && value.Contains("\"a\": 1", StringComparison.Ordinal),
            "json formatter prettified output");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void MinifyButton_MinifiesJson()
    {
        using var session = OpenTool();

        session.FindByAutomationId("JsonFormatter.InputField").AsTextBox().Text = "{\r\n  \"a\": 1\r\n}";
        session.FindByAutomationId("JsonFormatter.MinifyButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("JsonFormatter.OutputField").AsTextBox().Text,
            "{\"a\":1}",
            "json formatter minified output");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void InvalidJson_ShowsErrorStatus()
    {
        using var session = OpenTool();

        session.FindByAutomationId("JsonFormatter.InputField").AsTextBox().Text = "{\"a\":";
        session.FindByAutomationId("JsonFormatter.PrettifyButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextMatches(
            () => ReadText(session.FindByAutomationId("JsonFormatter.StatusText")),
            value => value.Contains("Invalid JSON", StringComparison.OrdinalIgnoreCase)
                || value.Contains("line", StringComparison.OrdinalIgnoreCase),
            "json formatter error status");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyOutputButton_CopiesFormattedJson()
    {
        using var session = OpenTool();

        session.FindByAutomationId("JsonFormatter.InputField").AsTextBox().Text = "{\"a\":1}";
        session.FindByAutomationId("JsonFormatter.PrettifyButton").AsButton().Invoke();
        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("JsonFormatter.OutputField").AsTextBox().Text,
            value => value.Contains("\"a\": 1", StringComparison.Ordinal),
            "json formatter output before copy");

        var expected = session.FindByAutomationId("JsonFormatter.OutputField").AsTextBox().Text;
        session.FindByAutomationId("JsonFormatter.CopyOutputButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            ReadClipboardText,
            expected,
            "json formatter clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("JsonFormatter.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("JsonFormatter.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "json formatter help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("JsonFormatter.CloseHelpButton")),
            "json formatter help panel collapsed");
    }

    private static bool IsCollapsed(AutomationElement? element)
        => element is null || element.Properties.IsOffscreen.ValueOrDefault;
}
