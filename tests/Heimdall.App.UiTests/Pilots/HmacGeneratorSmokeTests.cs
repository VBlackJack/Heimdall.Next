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
public sealed class HmacGeneratorSmokeTests : UiTestBase<HmacGeneratorView>
{
    private const string ExpectedHex = "1b2c16b75bd2a870c114153ccda5bcfca63314bc722fa160d690de133ccbb9db";
    private static readonly string ExpectedBase64 = Convert.ToBase64String(Convert.FromHexString(ExpectedHex));

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_DisplaysMessageInput()
    {
        using var session = OpenTool();

        var messageInput = session.FindByAutomationId("HmacGenerator.MessageInput");
        WaitHelpers.WaitUntilVisible(messageInput, "hmac message input visible", TimeSpan.FromSeconds(2));
        Assert.NotNull(messageInput);
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void KnownVector_GeneratesExpectedHexOutput()
    {
        using var session = OpenTool();

        EnterVisibleKeyAndMessage(session, "secret", "data");

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("HmacGenerator.OutputField").AsTextBox().Text,
            ExpectedHex,
            "hmac hex output",
            TimeSpan.FromMilliseconds(800));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Base64Option_ReformatsOutput()
    {
        using var session = OpenTool();

        EnterVisibleKeyAndMessage(session, "secret", "data");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("HmacGenerator.OutputField").AsTextBox().Text,
            ExpectedHex,
            "hmac output before format switch",
            TimeSpan.FromMilliseconds(800));

        session.FindByAutomationId("HmacGenerator.Base64FormatOption").AsRadioButton().IsChecked = true;
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("HmacGenerator.OutputField").AsTextBox().Text,
            ExpectedBase64,
            "hmac base64 output",
            TimeSpan.FromMilliseconds(800));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void ToggleKeyVisibility_SwapsVisibleEditor()
    {
        using var session = OpenTool();

        Assert.True(session.InvokeOnUi(control =>
        {
            var pwd = (System.Windows.Controls.PasswordBox)control.FindName("PwdKey");
            var txt = (System.Windows.Controls.TextBox)control.FindName("TxtKey");
            return pwd.Visibility == System.Windows.Visibility.Visible && txt.Visibility == System.Windows.Visibility.Collapsed;
        }));

        session.FindByAutomationId("HmacGenerator.ToggleKeyVisibilityButton").AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control =>
            {
                var pwd = (System.Windows.Controls.PasswordBox)control.FindName("PwdKey");
                var txt = (System.Windows.Controls.TextBox)control.FindName("TxtKey");
                return pwd.Visibility == System.Windows.Visibility.Collapsed && txt.Visibility == System.Windows.Visibility.Visible;
            }),
            "hmac visible key textbox");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyButton_CopiesCurrentOutput()
    {
        using var session = OpenTool();

        EnterVisibleKeyAndMessage(session, "secret", "data");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("HmacGenerator.OutputField").AsTextBox().Text,
            ExpectedHex,
            "hmac output before copy",
            TimeSpan.FromMilliseconds(800));

        session.FindByAutomationId("HmacGenerator.CopyButton").AsButton().Invoke();
        WaitHelpers.WaitUntilTextEquals(
            ReadClipboardText,
            ExpectedHex,
            "hmac clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("HmacGenerator.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("HmacGenerator.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "hmac help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("HmacGenerator.CloseHelpButton")),
            "hmac help panel collapsed");
    }

    private static void EnterVisibleKeyAndMessage(HostedToolWindow<HmacGeneratorView> session, string key, string message)
    {
        session.FindByAutomationId("HmacGenerator.ToggleKeyVisibilityButton").AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control =>
            {
                var txt = (System.Windows.Controls.TextBox)control.FindName("TxtKey");
                return txt.Visibility == System.Windows.Visibility.Visible;
            }),
            "hmac visible key editor");

        session.FindByAutomationId("HmacGenerator.KeyVisibleInput").AsTextBox().Text = key;
        session.FindByAutomationId("HmacGenerator.MessageInput").AsTextBox().Text = message;
    }
}
