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

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Heimdall.App.ViewModels;

namespace Heimdall.App;

/// <summary>
/// Partial class containing all UI localization methods for <see cref="MainWindow"/>.
/// Owns the <c>Apply*Localization</c> family that pushes localized strings onto
/// named XAML elements at startup and whenever the active locale changes, along
/// with the helpers that exist solely to feed those methods (credential
/// provider presets, external tool placeholder list, external tool preview).
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Sets all user-facing strings from locale resources.
    /// Called once after DataContext is assigned.
    /// </summary>
    private void ApplyLocalization()
    {
        if (DataContext is not MainViewModel vm) return;

        ApplySettingsLocalization(vm);
    }

    private void ApplySettingsLocalization(MainViewModel vm)
    {
        PopulateCredProvPresets(vm);

        // Check if a token is already stored (asynchronously loaded from settings)
        _ = Task.Run(async () =>
        {
            var cfgMgr = (System.Windows.Application.Current as App)?.Services?
                .GetService(typeof(Core.Configuration.ConfigManager)) as Core.Configuration.ConfigManager;
            if (cfgMgr is null) return;
            var s = await cfgMgr.LoadSettingsAsync();
            Dispatcher.Invoke(() => UpdateTokenStatus(!string.IsNullOrEmpty(s.CmdLibGitSyncToken)));
        });

        UpdateExternalToolProviderStatus(vm);
        PopulateExtToolPlaceholderList(vm);
        UpdateExtToolPreview();
    }

    private void UpdateExtToolPreview()
    {
        if (DataContext is not MainViewModel vm || vm.Settings.SelectedExternalTool is null)
        {
            Mw_ExtToolPreview.Text = "";
            return;
        }

        var tool = vm.Settings.SelectedExternalTool;
        var selectedServer = vm.ServerList.SelectedServer;
        string preview;
        if (selectedServer is not null)
        {
            var def = new Core.Configuration.ExternalToolDefinition
            {
                ExecutablePath = tool.ExecutablePath,
                Arguments = tool.Arguments
            };
            var resolved = def.ResolveArguments(
                selectedServer.RemoteServer, selectedServer.EffectivePort, selectedServer.Username,
                serverName: selectedServer.DisplayName, protocol: selectedServer.ConnectionType,
                keyFile: selectedServer.SshKeyPath, project: selectedServer.ProjectName,
                gateway: selectedServer.GatewayName);
            preview = $"{tool.ExecutablePath} {resolved}";
        }
        else
        {
            preview = $"{tool.ExecutablePath} {tool.Arguments}";
        }

        Mw_ExtToolPreview.Text = preview;
    }

    private void PopulateCredProvPresets(MainViewModel vm)
    {
        Mw_SettingsCredProvPreset.Items.Clear();
        foreach (var (label, _) in CredProvPresets)
        {
            Mw_SettingsCredProvPreset.Items.Add(label);
        }
        Mw_SettingsCredProvPreset.SelectedIndex = 0;
    }

    private void PopulateExtToolPlaceholderList(MainViewModel vm)
    {
        Mw_ExtToolPlaceholderList.Items.Clear();
        foreach (var (variable, descKey) in Core.Configuration.ExternalToolDefinition.SupportedPlaceholders)
        {
            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 2) };
            var variableLabel = new TextBlock
            {
                Text = variable,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = (double)FindResource("FontSizeCaption"),
                VerticalAlignment = VerticalAlignment.Center
            };
            variableLabel.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            panel.Children.Add(variableLabel);

            var descLabel = new TextBlock
            {
                Text = $" \u2014 {vm.Localize(descKey)}",
                FontSize = (double)FindResource("FontSizeCaption"),
                Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center
            };
            descLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            panel.Children.Add(descLabel);

            Mw_ExtToolPlaceholderList.Items.Add(panel);
        }
    }
}
