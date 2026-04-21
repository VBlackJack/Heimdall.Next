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
using Heimdall.App.ViewModels.Tools;
using Heimdall.App.Views.Tools;

namespace Heimdall.App.UiTests.Pilots;

[Collection(DesktopUiCollection.Name)]
public sealed class CronJobManagerSmokeTests : UiTestBase<CronJobManagerView>
{
    private const string SampleCrontab = "0 2 * * * /backup.sh";

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_DisplaysCrontabInput()
    {
        using var session = OpenTool();

        var input = session.FindByAutomationId("CronJob.CrontabInput");
        WaitHelpers.WaitUntilVisible(input, "cron crontab input visible", TimeSpan.FromSeconds(2));
        Assert.NotNull(input);
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void ParseButton_FillsDetailPanel_ForValidCrontab()
    {
        using var session = OpenTool();

        session.FindByAutomationId("CronJob.CrontabInput").AsTextBox().Text = SampleCrontab;
        session.FindByAutomationId("CronJob.ParseButton").AsButton().Invoke();

        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control => ((CronJobViewModel)control.DataContext!).CronEntries.Count == 1),
            "cron parsed entries");
        session.InvokeOnUi(control =>
        {
            var vm = (CronJobViewModel)control.DataContext!;
            vm.SelectedCronEntry = vm.CronEntries[0];
        });

        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.Border)control.FindName("CronDetailPanel")).Visibility == System.Windows.Visibility.Visible),
            "cron detail panel visible");
        WaitHelpers.WaitUntilTextMatches(
            () => session.InvokeOnUi(control => ((System.Windows.Controls.TextBlock)control.FindName("TxtDetailCommand")).Text),
            value => value.Contains("/backup.sh", StringComparison.Ordinal),
            "cron detail command");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyAllButton_CopiesParsedEntry()
    {
        using var session = OpenTool();

        session.FindByAutomationId("CronJob.CrontabInput").AsTextBox().Text = SampleCrontab;
        session.FindByAutomationId("CronJob.ParseButton").AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => session.InvokeOnUi(control => ((CronJobViewModel)control.DataContext!).CronEntries.Count == 1),
            "cron parsed entry before copy");

        session.FindByAutomationId("CronJob.CopyAllButton").AsButton().Invoke();
        WaitHelpers.WaitUntilTextMatches(
            ReadClipboardText,
            value => value.Contains("/backup.sh", StringComparison.Ordinal) && value.Contains("0 2 * * *", StringComparison.Ordinal),
            "cron clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("CronJob.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("CronJob.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "cron help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("CronJob.CloseHelpButton")),
            "cron help panel collapsed");
    }

    private static bool IsCollapsed(AutomationElement? element)
        => element is null || element.Properties.IsOffscreen.ValueOrDefault;
}
