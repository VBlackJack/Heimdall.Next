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

using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Generates OpenSSH config blocks from form inputs or from the entire Heimdall server inventory.
/// Output follows the standard ~/.ssh/config format with only non-default fields included.
/// </summary>
public partial class SshConfigGeneratorView : UserControl, IToolView
{
    private const int DefaultSshPort = 22;
    private const int DefaultAliveInterval = 0;
    private const string ConfigIndent = "    ";

    private LocalizationManager? _localizer;
    private ToolContext? _context;

    public SshConfigGeneratorView()
    {
        InitializeComponent();
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _context = context;
        ApplyLocalization();

        // Enter key handlers for single-line inputs
        TxtHostAlias.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnGenerateClick(s, e); };
        TxtHostName.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnGenerateClick(s, e); };
        TxtUser.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnGenerateClick(s, e); };
        TxtPort.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnGenerateClick(s, e); };
        TxtIdentityFile.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnGenerateClick(s, e); };
        TxtProxyJump.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnGenerateClick(s, e); };
        TxtServerAliveInterval.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnGenerateClick(s, e); };

        // Pre-fill from context if available
        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            TxtHostName.Text = context.TargetHost;
            TxtHostAlias.Text = context.DisplayName ?? context.TargetHost;
        }

        if (!string.IsNullOrWhiteSpace(context?.Username))
        {
            TxtUser.Text = context.Username;
        }

        if (context?.TargetPort is > 0)
        {
            TxtPort.Text = context.TargetPort.Value.ToString();
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHostAlias.Focus();
            TxtHostAlias.SelectAll();
        });
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolSshConfigTitle");
        LblHostAlias.Text = L("ToolSshConfigHostAlias");
        LblHostName.Text = L("ToolSshConfigHostName");
        LblUser.Text = L("ToolSshConfigUser");
        LblPort.Text = L("ToolSshConfigPort");
        LblIdentityFile.Text = L("ToolSshConfigIdentityFile");
        LblProxyJump.Text = L("ToolSshConfigProxyJump");
        LblForwardAgent.Text = L("ToolSshConfigForwardAgent");
        LblServerAliveInterval.Text = L("ToolSshConfigAliveInterval");
        BtnGenerate.Content = L("ToolSshConfigBtnGenerate");
        BtnGenerateAll.Content = L("ToolSshConfigBtnGenerateAll");
        BtnCopy.Content = L("ToolSshConfigBtnCopy");

        AutomationProperties.SetName(TxtHostAlias, L("ToolSshConfigHostAlias"));
        AutomationProperties.SetName(TxtHostName, L("ToolSshConfigHostName"));
        AutomationProperties.SetName(TxtUser, L("ToolSshConfigUser"));
        AutomationProperties.SetName(TxtPort, L("ToolSshConfigPort"));
        AutomationProperties.SetName(TxtIdentityFile, L("ToolSshConfigIdentityFile"));
        AutomationProperties.SetName(TxtProxyJump, L("ToolSshConfigProxyJump"));
        AutomationProperties.SetName(ChkForwardAgent, L("ToolSshConfigForwardAgent"));
        AutomationProperties.SetName(TxtServerAliveInterval, L("ToolSshConfigAliveInterval"));
        AutomationProperties.SetName(BtnGenerate, L("ToolSshConfigBtnGenerate"));
        AutomationProperties.SetName(BtnGenerateAll, L("ToolSshConfigBtnGenerateAll"));
        AutomationProperties.SetName(BtnCopy, L("ToolSshConfigBtnCopy"));
        AutomationProperties.SetName(TxtOutput, L("ToolSshConfigOutput"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        TxtEmptyState.Text = L("ToolSshConfigEmptyState");
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        var alias = TxtHostAlias.Text.Trim();
        var hostname = TxtHostName.Text.Trim();

        if (string.IsNullOrWhiteSpace(hostname))
        {
            TxtOutput.Text = L("ToolSshConfigErrorHostRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            alias = hostname;
        }

        var block = GenerateConfigBlock(
            alias,
            hostname,
            TxtUser.Text.Trim(),
            ParseInt(TxtPort.Text, DefaultSshPort),
            TxtIdentityFile.Text.Trim(),
            TxtProxyJump.Text.Trim(),
            ChkForwardAgent.IsChecked == true,
            ParseInt(TxtServerAliveInterval.Text, DefaultAliveInterval));

        TxtOutput.Text = block;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        OutputPanel.Visibility = Visibility.Visible;
    }

    private void OnGenerateAllClick(object sender, RoutedEventArgs e)
    {
        // This generates a placeholder message since server inventory access
        // requires dependency injection which tools don't have direct access to.
        // The context-based prefill covers the single-server use case.
        TxtOutput.Text = L("ToolSshConfigGenerateAllHint");
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        OutputPanel.Visibility = Visibility.Visible;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtOutput.Text))
        {
            try { Clipboard.SetText(TxtOutput.Text); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private static string GenerateConfigBlock(
        string alias,
        string hostname,
        string user,
        int port,
        string identityFile,
        string proxyJump,
        bool forwardAgent,
        int aliveInterval)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Host {alias}");
        sb.AppendLine($"{ConfigIndent}HostName {hostname}");

        if (!string.IsNullOrWhiteSpace(user))
        {
            sb.AppendLine($"{ConfigIndent}User {user}");
        }

        if (port != DefaultSshPort)
        {
            sb.AppendLine($"{ConfigIndent}Port {port}");
        }

        if (!string.IsNullOrWhiteSpace(identityFile))
        {
            sb.AppendLine($"{ConfigIndent}IdentityFile {identityFile}");
        }

        if (!string.IsNullOrWhiteSpace(proxyJump))
        {
            sb.AppendLine($"{ConfigIndent}ProxyJump {proxyJump}");
        }

        if (forwardAgent)
        {
            sb.AppendLine($"{ConfigIndent}ForwardAgent yes");
        }

        if (aliveInterval > 0)
        {
            sb.AppendLine($"{ConfigIndent}ServerAliveInterval {aliveInterval}");
        }

        return sb.ToString().TrimEnd();
    }

    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text.Trim(), out var value) ? value : fallback;
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpSSHCONFIG");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        // Reserved for future resource cleanup.
    }
}
