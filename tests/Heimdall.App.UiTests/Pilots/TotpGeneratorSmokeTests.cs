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
public sealed class TotpGeneratorSmokeTests : UiTestBase<TotpGeneratorView>
{
    private const string ValidSecret = "JBSWY3DPEHPK3PXP";

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_DisplaysSecretInput()
    {
        using var session = OpenTool();

        Assert.NotNull(session.FindByAutomationId("TotpGenerator.SecretInput"));
        Assert.NotNull(session.FindByAutomationId("TotpGenerator.GenerateButton"));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void ValidSecret_ShowsCodeAndProgress()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TotpGenerator.SecretInput").AsTextBox().Text = ValidSecret;
        session.FindByAutomationId("TotpGenerator.GenerateButton").AsButton().Invoke();

        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.Border)control.FindName("CodePanel")).Visibility == System.Windows.Visibility.Visible),
            "totp code panel visible",
            TimeSpan.FromSeconds(5));
        WaitHelpers.WaitUntilTextMatches(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.TextBlock)control.FindName("TxtCode")).Text),
            value => Regex.IsMatch(value, "^\\d{6}$"),
            "totp 6-digit code",
            TimeSpan.FromSeconds(5));
        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.ProgressBar)control.FindName("ProgressTime")).Visibility == System.Windows.Visibility.Visible),
            "totp progress visible",
            TimeSpan.FromSeconds(5));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyButton_CopiesSixDigitCode()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TotpGenerator.SecretInput").AsTextBox().Text = ValidSecret;
        session.FindByAutomationId("TotpGenerator.GenerateButton").AsButton().Invoke();
        WaitHelpers.WaitUntilTextMatches(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.TextBlock)control.FindName("TxtCode")).Text),
            value => Regex.IsMatch(value, "^\\d{6}$"),
            "totp code before copy",
            TimeSpan.FromSeconds(5));

        session.FindByAutomationId("TotpGenerator.CopyButton").AsButton().Invoke();
        WaitHelpers.WaitUntilTextMatches(
            ReadClipboardText,
            value => Regex.IsMatch(value, "^\\d{6}$"),
            "totp clipboard",
            TimeSpan.FromSeconds(2));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void InvalidSecret_ShowsErrorAndKeepsCodePanelHidden()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TotpGenerator.SecretInput").AsTextBox().Text = "!!!";
        session.FindByAutomationId("TotpGenerator.GenerateButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextMatches(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.TextBlock)control.FindName("TxtError")).Text),
            value => !string.IsNullOrWhiteSpace(value),
            "totp error text",
            TimeSpan.FromSeconds(5));
        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.Border)control.FindName("CodePanel")).Visibility != System.Windows.Visibility.Visible),
            "totp code panel hidden",
            TimeSpan.FromSeconds(5));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("TotpGenerator.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("TotpGenerator.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "totp help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("TotpGenerator.CloseHelpButton")),
            "totp help panel collapsed");
    }
}
