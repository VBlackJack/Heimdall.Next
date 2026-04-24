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

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FlaUI.Core.AutomationElements;
using Heimdall.App.UiTests.Infrastructure;
using Heimdall.App.Views.Tools;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Heimdall.App.UiTests.Pilots;

[Collection(DesktopUiCollection.Name)]
public sealed class ChmodCalculatorSmokeTests : UiTestBase<ChmodCalculatorView>
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Loads_DefaultOctalIs755()
    {
        using var session = OpenTool();

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("Chmod.OctalInput").AsTextBox().Text,
            "755",
            "chmod default octal");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void TypingOctal_UpdatesCheckboxes()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Chmod.OctalInput").AsTextBox().Text = "644";

        WaitHelpers.WaitUntil(() => session.FindByAutomationId("Chmod.OwnerRead").AsCheckBox().IsChecked == true, "owner read on");
        WaitHelpers.WaitUntil(() => session.FindByAutomationId("Chmod.OwnerWrite").AsCheckBox().IsChecked == true, "owner write on");
        WaitHelpers.WaitUntil(() => session.FindByAutomationId("Chmod.OwnerExecute").AsCheckBox().IsChecked == false, "owner execute off");
        WaitHelpers.WaitUntil(() => session.FindByAutomationId("Chmod.GroupRead").AsCheckBox().IsChecked == true, "group read on");
        WaitHelpers.WaitUntil(() => session.FindByAutomationId("Chmod.GroupWrite").AsCheckBox().IsChecked == false, "group write off");
        WaitHelpers.WaitUntil(() => session.FindByAutomationId("Chmod.OthersRead").AsCheckBox().IsChecked == true, "others read on");
        WaitHelpers.WaitUntil(() => session.FindByAutomationId("Chmod.OthersWrite").AsCheckBox().IsChecked == false, "others write off");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void TogglingCheckboxes_UpdatesOctal()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Chmod.OctalInput").AsTextBox().Text = "000";
        var ids = new[]
        {
            "Chmod.OwnerRead", "Chmod.OwnerWrite", "Chmod.OwnerExecute",
            "Chmod.GroupRead", "Chmod.GroupWrite", "Chmod.GroupExecute",
            "Chmod.OthersRead", "Chmod.OthersWrite", "Chmod.OthersExecute",
        };

        foreach (var id in ids)
        {
            session.FindByAutomationId(id).AsCheckBox().Toggle();
        }

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("Chmod.OctalInput").AsTextBox().Text,
            "777",
            "chmod octal after toggling");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void SymbolicInput_AppliesOnEnter()
    {
        using var session = OpenTool();

        session.InvokeOnUi(control =>
        {
            var symbolicInput = FindChildByAutomationId<WpfTextBox>(control, "Chmod.SymbolicInput");
            symbolicInput.Focus();
            symbolicInput.Text = "u=rwx,g=rx,o=r";

            var enterBinding = Assert.Single(symbolicInput.InputBindings.OfType<KeyBinding>());
            Assert.Equal(Key.Enter, enterBinding.Key);
            Assert.NotNull(enterBinding.Command);
            Assert.True(enterBinding.Command!.CanExecute(enterBinding.CommandParameter));
            enterBinding.Command.Execute(enterBinding.CommandParameter);
        });

        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("Chmod.OctalInput").AsTextBox().Text,
            "754",
            "chmod symbolic enter applies");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void CopyCommandButton_CopiesPreview()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Chmod.OctalInput").AsTextBox().Text = "644";
        session.FindByAutomationId("Chmod.CopyCommandButton").AsButton().Invoke();

        WaitHelpers.WaitUntilTextEquals(
            ReadClipboardText,
            "chmod 644 filename",
            "chmod command clipboard");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void HelpButton_TogglesHelpPanel()
    {
        using var session = OpenTool();

        session.FindByAutomationId("Chmod.HelpButton").AsButton().Invoke();
        var closeButton = session.FindByAutomationId("Chmod.CloseHelpButton");
        WaitHelpers.WaitUntilVisible(closeButton, "chmod help panel visible");

        closeButton.AsButton().Invoke();
        WaitHelpers.WaitUntil(
            () => IsCollapsed(session.TryFindByAutomationId("Chmod.CloseHelpButton")),
            "chmod help panel collapsed");
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void LocaleSwitch_UpdatesOwnerReadAutomationName()
    {
        using var session = OpenTool();

        WpfTestHost.SwitchLocale("fr");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("Chmod.OwnerRead").Name,
            $"{WpfTestHost.Translate("ToolChmodOwner")} {WpfTestHost.Translate("ToolChmodRead")}",
            "chmod owner read name in fr");

        WpfTestHost.SwitchLocale("en");
        WaitHelpers.WaitUntilTextEquals(
            () => session.FindByAutomationId("Chmod.OwnerRead").Name,
            $"{WpfTestHost.Translate("ToolChmodOwner")} {WpfTestHost.Translate("ToolChmodRead")}",
            "chmod owner read name in en");
    }

    private static T FindChildByAutomationId<T>(DependencyObject root, string automationId)
        where T : DependencyObject
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);

        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T typed
                && string.Equals(
                    AutomationProperties.GetAutomationId(typed),
                    automationId,
                    StringComparison.Ordinal))
            {
                return typed;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < childCount; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }

        throw new InvalidOperationException($"Could not find child '{automationId}' of type {typeof(T).Name}.");
    }
}
