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
public sealed class HashGeneratorSmokeTests : UiTestBase<HashGeneratorView>
{
    private const string Sha256Hello = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_DisplaysInputField()
    {
        using var session = OpenTool();

        var input = session.FindByAutomationId("HashGenerator.InputField");
        WaitHelpers.WaitUntilVisible(input, "hash generator input visible", TimeSpan.FromSeconds(2));
        Assert.NotNull(input);
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void InputHello_PopulatesHashes_AndRowsDiffer()
    {
        using var session = OpenTool();

        session.FindByAutomationId("HashGenerator.InputField").AsTextBox().Text = "hello";

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("HashGenerator.Result.SHA256").AsTextBox().Text,
            Sha256Hello,
            "hash generator sha256 output",
            TimeSpan.FromSeconds(5));
        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("HashGenerator.Result.SHA1").AsTextBox().Text,
            value => !string.IsNullOrWhiteSpace(value) && !string.Equals(value, Sha256Hello, StringComparison.Ordinal),
            "hash generator distinct sha1 output",
            TimeSpan.FromSeconds(5));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void VerifyInput_ShowsSha256Match()
    {
        using var session = OpenTool();

        session.FindByAutomationId("HashGenerator.InputField").AsTextBox().Text = "hello";
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("HashGenerator.Result.SHA256").AsTextBox().Text,
            Sha256Hello,
            "hash generator sha256 output before verify",
            TimeSpan.FromSeconds(5));

        session.FindByAutomationId("HashGenerator.VerifyInput").AsTextBox().Text = Sha256Hello;
        WaitHelpers.WaitUntilTextMatches(
            () => ReadText(session.FindByAutomationId("HashGenerator.VerifyResult")),
            value => value.Contains("SHA256", StringComparison.OrdinalIgnoreCase),
            "hash generator verify result");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopySha256Button_CopiesHash()
    {
        using var session = OpenTool();

        session.FindByAutomationId("HashGenerator.InputField").AsTextBox().Text = "hello";
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("HashGenerator.Result.SHA256").AsTextBox().Text,
            Sha256Hello,
            "hash generator sha256 output before copy",
            TimeSpan.FromSeconds(5));

        session.FindByAutomationId("HashGenerator.Copy.SHA256").AsButton().Invoke();
        WaitHelpers.WaitUntilTextEquals(
            ReadClipboardText,
            Sha256Hello,
            "hash generator clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("HashGenerator.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("HashGenerator.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "hash generator help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("HashGenerator.CloseHelpButton")),
            "hash generator help panel collapsed");
    }
}
