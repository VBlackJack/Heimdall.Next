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
public sealed class IpConverterSmokeTests : UiTestBase<IpConverterView>
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_AndLocaleSwitch_UpdatesCopyLabel()
    {
        using var session = OpenTool();

        Assert.NotNull(session.FindByAutomationId("IpConverter.InputField"));
        Assert.NotNull(session.FindByAutomationId("IpConverter.HelpButton"));

        WpfTestHost.SwitchLocale("fr");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("IpConverter.InputField").Name,
            WpfTestHost.Translate("ToolIpConvInputLabel"),
            "ip converter input label in fr");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void ValidInput_ShowsAllRepresentations()
    {
        using var session = OpenTool();

        session.FindByAutomationId("IpConverter.InputField").AsTextBox().Text = "192.168.1.1";

        WaitHelpers.WaitUntilTextEquals(
            () => ReadText(session.FindByAutomationId("IpConverter.DottedOutput")),
            "192.168.1.1",
            "ip converter dotted output");
        WaitHelpers.WaitUntilTextEquals(
            () => ReadText(session.FindByAutomationId("IpConverter.DecimalOutput")),
            "3232235777",
            "ip converter decimal output");
        WaitHelpers.WaitUntilTextEquals(
            () => ReadText(session.FindByAutomationId("IpConverter.HexOutput")),
            "0xC0A80101",
            "ip converter hex output");
        WaitHelpers.WaitUntilTextEquals(
            () => ReadText(session.FindByAutomationId("IpConverter.BinaryOutput")),
            "11000000.10101000.00000001.00000001",
            "ip converter binary output");
        WaitHelpers.WaitUntilTextEquals(
            () => ReadText(session.FindByAutomationId("IpConverter.MappedIpv6Output")),
            "::ffff:c0a8:0101",
            "ip converter mapped ipv6 output");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void InvalidInput_ShowsErrorAndHidesResults()
    {
        using var session = OpenTool();

        session.FindByAutomationId("IpConverter.InputField").AsTextBox().Text = "not.an.ip";

        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.TextBlock)control.FindName("TxtError")).Visibility == System.Windows.Visibility.Visible),
            "ip converter error visible");
        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.Border)control.FindName("ResultsPanel")).Visibility == System.Windows.Visibility.Collapsed),
            "ip converter results hidden");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyDottedButton_CopiesValue()
    {
        using var session = OpenTool();

        session.FindByAutomationId("IpConverter.InputField").AsTextBox().Text = "192.168.1.1";
        WaitHelpers.WaitUntilTextEquals(
            () => ReadText(session.FindByAutomationId("IpConverter.DottedOutput")),
            "192.168.1.1",
            "ip converter dotted output before copy");

        session.FindByAutomationId("IpConverter.CopyDottedButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            ReadClipboardText,
            "192.168.1.1",
            "ip converter clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void AlternateInput_ShowsMappedIpv6()
    {
        using var session = OpenTool();

        session.FindByAutomationId("IpConverter.InputField").AsTextBox().Text = "127.0.0.1";

        WaitHelpers.WaitUntilTextEquals(
            () => ReadText(session.FindByAutomationId("IpConverter.MappedIpv6Output")),
            "::ffff:7f00:0001",
            "ip converter mapped ipv6 for loopback");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("IpConverter.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("IpConverter.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "ip converter help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("IpConverter.CloseHelpButton")),
            "ip converter help panel collapsed");
    }
}
