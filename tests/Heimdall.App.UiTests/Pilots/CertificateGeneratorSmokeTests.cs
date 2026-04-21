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
public sealed class CertificateGeneratorSmokeTests : UiTestBase<CertificateGeneratorView>
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void View_Loads_Successfully()
    {
        using var session = OpenTool();

        var cnInput = session.FindByAutomationId("CertificateGenerator.CnInput");
        WaitHelpers.WaitUntilVisible(cnInput, "certificate generator CN input visible", TimeSpan.FromSeconds(2));
        Assert.NotNull(cnInput);
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void GenerateButton_ProducesSelfSignedPem()
    {
        using var session = OpenTool();

        session.FindByAutomationId("CertificateGenerator.CnInput").AsTextBox().Text = "test.local";
        session.FindByAutomationId("CertificateGenerator.GenerateButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("CertificateGenerator.CertOutput").AsTextBox().Text,
            value => value.StartsWith("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal),
            "certificate pem output",
            TimeSpan.FromSeconds(5));
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void EmptyCn_ShowsValidationError()
    {
        using var session = OpenTool();

        session.FindByAutomationId("CertificateGenerator.CnInput").AsTextBox().Text = string.Empty;
        session.FindByAutomationId("CertificateGenerator.GenerateButton").AsButton().Invoke();

        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control =>
            {
                var block = (System.Windows.Controls.TextBlock)control.FindName("ValidationText");
                return block.Visibility == System.Windows.Visibility.Visible && !string.IsNullOrWhiteSpace(block.Text);
            }),
            "certificate validation error");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyCertButton_CopiesPem()
    {
        using var session = OpenTool();

        session.FindByAutomationId("CertificateGenerator.CnInput").AsTextBox().Text = "test.local";
        session.FindByAutomationId("CertificateGenerator.GenerateButton").AsButton().Invoke();
        WaitHelpers.WaitUntilTextMatches(
            () => session.FindByAutomationId("CertificateGenerator.CertOutput").AsTextBox().Text,
            value => value.StartsWith("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal),
            "certificate pem before copy",
            TimeSpan.FromSeconds(5));

        session.FindByAutomationId("CertificateGenerator.CopyCertButton").AsButton().Invoke();
        WaitHelpers.WaitUntilTextMatches(
            ReadClipboardText,
            value => value.StartsWith("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal),
            "certificate clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("CertificateGenerator.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("CertificateGenerator.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "certificate help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("CertificateGenerator.CloseHelpButton")),
            "certificate help panel collapsed");
    }
}
