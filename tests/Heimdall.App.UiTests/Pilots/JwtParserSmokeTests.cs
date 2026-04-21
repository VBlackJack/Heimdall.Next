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

using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using Heimdall.App.UiTests.Infrastructure;
using Heimdall.App.Views.Tools;

namespace Heimdall.App.UiTests.Pilots;

[Collection(DesktopUiCollection.Name)]
public sealed class JwtParserSmokeTests : UiTestBase<JwtParserView>
{
    private const string SampleJwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
        "eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ." +
        "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_DisplaysInputField()
    {
        using var session = OpenTool();
        Assert.NotNull(session.FindByAutomationId("Jwt.InputField"));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void PasteSampleJwt_PopulatesOutputs()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Jwt.InputField").AsTextBox().Text = SampleJwt;

        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("Jwt.HeaderOutput").AsTextBox().Text,
            value => value.Contains("HS256", StringComparison.Ordinal),
            "jwt header output");
        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("Jwt.PayloadOutput").AsTextBox().Text,
            value => value.Contains("John Doe", StringComparison.Ordinal),
            "jwt payload output");
        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("Jwt.SignatureOutput").AsTextBox().Text,
            value => Regex.IsMatch(value, "^[0-9a-f]+$"),
            "jwt signature hex");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void InvalidFormat_KeepsOutputsEmpty()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Jwt.InputField").AsTextBox().Text = "a.b";

        Thread.Sleep(300);
        Assert.Equal(string.Empty, session.FindByAutomationId("Jwt.HeaderOutput").AsTextBox().Text);
        Assert.Equal(string.Empty, session.FindByAutomationId("Jwt.PayloadOutput").AsTextBox().Text);
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyHeaderButton_CopiesHeaderText()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Jwt.InputField").AsTextBox().Text = SampleJwt;
        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("Jwt.HeaderOutput").AsTextBox().Text,
            value => value.Contains("HS256", StringComparison.Ordinal),
            "jwt header output before copy");

        session.FindByAutomationId("Jwt.CopyHeaderButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            ReadClipboardText,
            session.FindByAutomationId("Jwt.HeaderOutput").AsTextBox().Text,
            "jwt header clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void VerifyButton_EnablesForSupportedHmacSecret()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Jwt.InputField").AsTextBox().Text = SampleJwt;
        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("Jwt.HeaderOutput").AsTextBox().Text,
            value => value.Contains("HS256", StringComparison.Ordinal),
            "jwt parsed before verify");

        session.FindByAutomationId("Jwt.SecretInput").AsTextBox().Text = "your-256-bit-secret";
        var verifyButton = session.FindByAutomationId("Jwt.VerifyButton").AsButton();

        WaitHelpers.WaitUntil(() => verifyButton.IsEnabled, "jwt verify button enabled");
        verifyButton.Invoke();
        WaitHelpers.WaitUntil(() => verifyButton.IsEnabled, "jwt verify button remains enabled");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Jwt.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("Jwt.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "jwt help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("Jwt.CloseHelpButton")),
            "jwt help panel collapsed");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void LocaleSwitch_UpdatesVerifyButtonLabel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Jwt.InputField").AsTextBox().Text = SampleJwt;
        WaitHelpers.WaitUntil(() => session.TryFindByAutomationId("Jwt.VerifyButton") is not null, "jwt verify button visible");

        WpfTestHost.SwitchLocale("fr");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("Jwt.VerifyButton").Name,
            WpfTestHost.Translate("ToolJwtBtnVerify"),
            "jwt verify button in fr");
    }

    private static bool IsCollapsed(AutomationElement? element)
        => element is null || element.Properties.IsOffscreen.ValueOrDefault;
}
