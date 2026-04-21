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
public sealed class UrlEncoderSmokeTests : UiTestBase<UrlEncoderView>
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_AndLocaleSwitch_UpdatesComponentLabel()
    {
        using var session = OpenTool();

        Assert.NotNull(session.FindByAutomationId("UrlEncoder.DecodedField"));
        Assert.NotNull(session.FindByAutomationId("UrlEncoder.EncodedField"));
        Assert.NotNull(session.FindByAutomationId("UrlEncoder.ComponentEncodingCheck"));

        WpfTestHost.SwitchLocale("fr");
        WaitHelpers.WaitUntilTextEquals(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.CheckBox)control.FindName("ChkComponentEncoding")).Content?.ToString() ?? string.Empty),
            WpfTestHost.Translate("ToolUrlEncComponentMode"),
            "url encoder component label in fr");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void TypingDecoded_EncodesValue()
    {
        using var session = OpenTool();

        session.FindByAutomationId("UrlEncoder.DecodedField").AsTextBox().Text = "hello world";

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("UrlEncoder.EncodedField").AsTextBox().Text,
            "hello%20world",
            "url encoder encoded text");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void TypingEncoded_DecodesValue()
    {
        using var session = OpenTool();

        session.FindByAutomationId("UrlEncoder.EncodedField").AsTextBox().Text = "hello%20world";

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("UrlEncoder.DecodedField").AsTextBox().Text,
            "hello world",
            "url encoder decoded text");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void ComponentModeToggle_ChangesEncodedOutput()
    {
        using var session = OpenTool();

        session.FindByAutomationId("UrlEncoder.DecodedField").AsTextBox().Text = "a/b?c=d";
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("UrlEncoder.EncodedField").AsTextBox().Text,
            "a/b?c=d",
            "url encoder structure-preserving output");

        var toggle = session.FindByAutomationId("UrlEncoder.ComponentEncodingCheck").AsCheckBox();
        if (toggle.IsChecked != true)
        {
            toggle.Toggle();
        }

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("UrlEncoder.EncodedField").AsTextBox().Text,
            "a%2Fb%3Fc%3Dd",
            "url encoder component output");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyEncodedButton_CopiesEncodedText()
    {
        using var session = OpenTool();

        session.FindByAutomationId("UrlEncoder.DecodedField").AsTextBox().Text = "hello world";
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("UrlEncoder.EncodedField").AsTextBox().Text,
            "hello%20world",
            "url encoder output before copy");

        session.FindByAutomationId("UrlEncoder.CopyEncodedButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            ReadClipboardText,
            "hello%20world",
            "url encoder clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("UrlEncoder.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("UrlEncoder.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "url encoder help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("UrlEncoder.CloseHelpButton")),
            "url encoder help panel collapsed");
    }
}
