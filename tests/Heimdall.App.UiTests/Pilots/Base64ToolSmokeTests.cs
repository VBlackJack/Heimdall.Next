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
public sealed class Base64ToolSmokeTests : UiTestBase<Base64ToolView>
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_AndLocaleSwitch_UpdatesEncodeLabel()
    {
        using var session = OpenTool();

        Assert.NotNull(session.FindByAutomationId("Base64.InputField"));
        Assert.NotNull(session.FindByAutomationId("Base64.EncodeButton"));
        Assert.NotNull(session.FindByAutomationId("Base64.DecodeButton"));
        Assert.NotNull(session.FindByAutomationId("Base64.UrlSafeCheck"));

        WpfTestHost.SwitchLocale("fr");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("Base64.EncodeButton").Name,
            WpfTestHost.Translate("ToolBase64BtnEncode"),
            "base64 encode button in fr");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void EncodeButton_EncodesPlainText()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Base64.InputField").AsTextBox().Text = "abc";
        session.FindByAutomationId("Base64.EncodeButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("Base64.OutputField").AsTextBox().Text,
            "YWJj",
            "base64 encoded output");
        WaitHelpers.WaitUntilTextMatches(
            () => ReadText(session.FindByAutomationId("Base64.StatusText")),
            value => value.Contains("Encoded", StringComparison.Ordinal),
            "base64 encoded status");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void DecodeButton_DecodesBase64Text()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Base64.InputField").AsTextBox().Text = "YWJj";
        session.FindByAutomationId("Base64.DecodeButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("Base64.OutputField").AsTextBox().Text,
            "abc",
            "base64 decoded output");
        WaitHelpers.WaitUntilTextMatches(
            () => ReadText(session.FindByAutomationId("Base64.StatusText")),
            value => value.Contains("Decoded", StringComparison.Ordinal),
            "base64 decoded status");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void UrlSafeToggle_TogglesState()
    {
        using var session = OpenTool();

        var check = session.FindByAutomationId("Base64.UrlSafeCheck").AsCheckBox();
        var initial = check.IsChecked;

        check.Toggle();

        WaitHelpers.WaitUntil(
            () => session.FindByAutomationId("Base64.UrlSafeCheck").AsCheckBox().IsChecked != initial,
            "base64 urlsafe toggle");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyOutputButton_CopiesEncodedText()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Base64.InputField").AsTextBox().Text = "abc";
        session.FindByAutomationId("Base64.EncodeButton").AsButton().Invoke();
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("Base64.OutputField").AsTextBox().Text,
            "YWJj",
            "base64 output before copy");

        session.FindByAutomationId("Base64.CopyOutputButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            ReadClipboardText,
            "YWJj",
            "base64 clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Base64.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("Base64.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "base64 help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("Base64.CloseHelpButton")),
            "base64 help panel collapsed");
    }

    private static bool IsCollapsed(AutomationElement? element)
        => element is null || element.Properties.IsOffscreen.ValueOrDefault;
}
