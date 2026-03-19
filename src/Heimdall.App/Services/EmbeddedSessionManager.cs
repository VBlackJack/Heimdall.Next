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
using System.Windows.Controls;
using System.Windows.Media;
using Heimdall.App.ViewModels;
using Heimdall.App.Views;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Sftp;
using Heimdall.Ssh;

namespace Heimdall.App.Services;

/// <summary>
/// Creates visual hosts for connection sessions so the shell can render
/// embedded protocol surfaces without teaching the ViewModel layer about WPF.
/// </summary>
public sealed class EmbeddedSessionManager
{
    private readonly LocalizationManager _localizer;
    private readonly IDialogService _dialogService;
    private readonly HostKeyStore _hostKeyStore;

    /// <summary>
    /// Optional callback invoked when a terminal view broadcasts input.
    /// Parameters: (byte[] data, object? senderView).
    /// Wired by MainViewModel to relay keystrokes to all other terminals.
    /// </summary>
    public Action<byte[], object?>? BroadcastCallback { get; set; }

    /// <summary>
    /// Optional callback invoked when an embedded view's Split button is clicked.
    /// Parameters: (SessionTabViewModel session).
    /// Wired by MainWindow code-behind to show the split picker context menu.
    /// </summary>
    public Action<SessionTabViewModel>? SplitRequestedCallback { get; set; }

    /// <summary>
    /// Func that returns the current broadcast mode state.
    /// Wired by MainViewModel so newly created views show the badge immediately.
    /// </summary>
    public Func<bool>? IsBroadcastActive { get; set; }

    /// <summary>
    /// Optional callback invoked when an embedded SSH view requests reconnection.
    /// Parameters: (SessionTabViewModel session, string serverId, string connectionType).
    /// Wired by MainViewModel to restart the connection using the original server.
    /// </summary>
    public Action<SessionTabViewModel, string, string>? ReconnectRequestedCallback { get; set; }

    public EmbeddedSessionManager(
        LocalizationManager localizer,
        IDialogService dialogService,
        HostKeyStore hostKeyStore)
    {
        _localizer = localizer;
        _dialogService = dialogService;
        _hostKeyStore = hostKeyStore;
    }

    public object CreateHostControl(
        SessionTabViewModel sessionTab,
        string displayName,
        string connectionType,
        ISessionResult session,
        AppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(sessionTab);
        ArgumentNullException.ThrowIfNull(session);

        var antiIdleInterval = settings?.AntiIdleIntervalSeconds ?? 60;
        var sshKeepAliveInterval = settings?.SshTmoutResetIntervalSeconds ?? 240;

        if (string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase) &&
            session is RdpSessionResult rdp)
        {
            var view = new EmbeddedRdpView();
            view.InitializeSession(rdp.Server, sessionTab, antiIdleInterval, _localizer, rdp.TunnelPort);
            WireSplitRequested(view, sessionTab);
            return view;
        }

        if (string.Equals(connectionType, "SSH", StringComparison.OrdinalIgnoreCase) &&
            session is SshSessionResult sshResult)
        {
            return CreateSshView(sessionTab, sshResult.Session, displayName, sshKeepAliveInterval, settings);
        }

        if (string.Equals(connectionType, "SSH", StringComparison.OrdinalIgnoreCase) &&
            session is TerminalSessionResult termResult)
        {
            return CreateTerminalSshView(sessionTab, termResult.Session, displayName, sshKeepAliveInterval, settings);
        }

        if (string.Equals(connectionType, "LOCAL", StringComparison.OrdinalIgnoreCase) &&
            session is LocalShellBundle localBundle)
        {
            var termView = CreateTerminalSshView(sessionTab, localBundle.Session, displayName, 0, settings);

            // Auto-attach local file browser panel in a vertical split
            var fileBrowser = new Views.LocalFileBrowserView(
                localBundle.WorkingDirectory, _localizer, settings?.ExternalEditorPath);

            fileBrowser.NavigateToPathRequested += (path) =>
            {
                var cdCommand = FormatCdCommand(localBundle.ShellExecutable, path);
                localBundle.Session.Write(System.Text.Encoding.UTF8.GetBytes(cdCommand));
            };

            fileBrowser.RunInShellRequested += (path) =>
            {
                var command = FormatRunCommand(localBundle.ShellExecutable, path);
                localBundle.Session.Write(System.Text.Encoding.UTF8.GetBytes(command));
            };

            // Edit in embedded editor: swap file browser with AvalonEdit editor
            fileBrowser.EditInEditorRequested += (path) =>
            {
                var editorView = new Views.EmbeddedEditorView();
                editorView.OpenFile(path);

                // When editor closes, restore the file browser
                editorView.CloseRequested += () =>
                {
                    sessionTab.SecondaryHostControl = fileBrowser;
                    fileBrowser.RefreshCurrentDirectory();
                };

                sessionTab.SecondaryHostControl = editorView;
            };

            sessionTab.SecondaryHostControl = fileBrowser;
            sessionTab.SplitOrientation = Heimdall.Core.Models.SplitOrientation.Vertical;
            sessionTab.IsSplit = true;

            return termView;
        }

        if (string.Equals(connectionType, "SFTP", StringComparison.OrdinalIgnoreCase) &&
            session is SftpSessionBundle bundle)
        {
            return CreateSftpView(sessionTab, bundle.Browser, displayName, bundle.SshParams);
        }

        if (string.Equals(connectionType, "FTP", StringComparison.OrdinalIgnoreCase) &&
            session is FtpSessionBundle ftpBundle)
        {
            return CreateSftpView(sessionTab, ftpBundle.Browser, displayName, null);
        }

        if (string.Equals(connectionType, "CITRIX", StringComparison.OrdinalIgnoreCase)
            && session is CitrixSessionResult citrix)
        {
            var view = new EmbeddedCitrixView();
            view.InitializeSession(citrix, sessionTab, displayName, _localizer);
            view.SetConnectionInfo(citrix.StoreFrontUrl, citrix.AppName);
            return view;
        }

        if (string.Equals(connectionType, "VNC", StringComparison.OrdinalIgnoreCase)
            && session is VncSessionResult vnc)
        {
            var view = new EmbeddedVncView();
            view.InitializeSession(vnc, sessionTab, displayName, _localizer);
            return view;
        }

        if (string.Equals(connectionType, "TELNET", StringComparison.OrdinalIgnoreCase)
            && session is TerminalSessionResult telnetResult)
        {
            return CreateTerminalSshView(sessionTab, telnetResult.Session, displayName, 0, settings);
        }

        return new DisposablePlaceholderView(displayName, connectionType, session);
    }

    private EmbeddedSshView CreateSshView(
        SessionTabViewModel tab,
        SshShellSession session,
        string displayName,
        int keepAliveIntervalSeconds,
        AppSettings? settings = null)
    {
        var view = new EmbeddedSshView { Localizer = _localizer, TerminalSettings = settings };
        view.InitializeSession(session, tab, displayName, string.Empty, keepAliveIntervalSeconds);
        WireBroadcast(view);
        WireSplitRequested(view, tab);
        WireReconnectRequested(view, tab);
        return view;
    }

    private EmbeddedSshView CreateTerminalSshView(
        SessionTabViewModel tab,
        Heimdall.Terminal.ITerminalSession terminalSession,
        string displayName,
        int keepAliveIntervalSeconds,
        AppSettings? settings = null)
    {
        var view = new EmbeddedSshView { Localizer = _localizer, TerminalSettings = settings };
        view.InitializeTerminalSession(terminalSession, tab, displayName, keepAliveIntervalSeconds);
        WireBroadcast(view);
        WireSplitRequested(view, tab);
        WireReconnectRequested(view, tab);
        return view;
    }

    private void WireBroadcast(EmbeddedSshView view)
    {
        var callback = BroadcastCallback;
        if (callback is not null)
        {
            view.BroadcastInput += (bytes) => callback(bytes, view);
        }

        // Show broadcast badge if broadcast mode is already active
        if (IsBroadcastActive?.Invoke() == true)
        {
            view.SetBroadcastIndicator(true);
        }
    }

    private EmbeddedSftpView CreateSftpView(
        SessionTabViewModel tab,
        IRemoteBrowser browser,
        string displayName,
        SshConnectionParams? sshParams)
    {
        var view = new EmbeddedSftpView();
        view.InitializeSession(
            browser, tab, displayName, string.Empty,
            _localizer, _dialogService, sshParams, _hostKeyStore);

        // Wire "Open in Terminal" to send a cd command to the SSH session
        // sharing the same tab (primary host is an SSH terminal).
        view.OpenInTerminalRequested += (path) =>
        {
            // Check both primary and secondary host for a terminal
            if (tab.HostControl is EmbeddedSshView primarySsh)
            {
                primarySsh.WriteCommand($"cd \"{path}\"");
            }
            else if (tab.SecondaryHostControl is EmbeddedSshView secondarySsh)
            {
                secondarySsh.WriteCommand($"cd \"{path}\"");
            }
        };

        WireSplitRequested(view, tab);
        return view;
    }

    private void WireReconnectRequested(EmbeddedSshView view, SessionTabViewModel tab)
    {
        view.ReconnectRequested += () =>
            ReconnectRequestedCallback?.Invoke(
                tab,
                !string.IsNullOrEmpty(tab.OriginalServerId) ? tab.OriginalServerId : tab.ServerId,
                tab.ConnectionType);
    }

    private void WireSplitRequested(EmbeddedSshView view, SessionTabViewModel tab)
    {
        view.SplitRequested += () => SplitRequestedCallback?.Invoke(tab);
    }

    private void WireSplitRequested(EmbeddedRdpView view, SessionTabViewModel tab)
    {
        view.SplitRequested += () => SplitRequestedCallback?.Invoke(tab);
    }

    private void WireSplitRequested(EmbeddedSftpView view, SessionTabViewModel tab)
    {
        view.SplitRequested += () => SplitRequestedCallback?.Invoke(tab);
    }

    /// <summary>
    /// Builds the correct <c>cd</c> command for the detected shell type.
    /// PowerShell uses <c>cd "path"</c>, cmd uses <c>cd /d "path"</c>,
    /// and bash/wsl uses <c>cd 'path'</c>.
    /// </summary>
    private static string FormatCdCommand(string shellExecutable, string path)
    {
        var shellExe = (shellExecutable ?? "powershell.exe").ToLowerInvariant();

        if (shellExe.Contains("cmd"))
            return $"cd /d \"{path}\"\n";

        if (shellExe.Contains("wsl") || shellExe.Contains("bash"))
            return $"cd '{path}'\n";

        // PowerShell (powershell.exe, pwsh.exe) is the default
        return $"cd \"{path}\"\n";
    }

    /// <summary>
    /// Builds the correct run/execute command for the detected shell type.
    /// PowerShell uses <c>&amp; "path"</c>, cmd uses <c>"path"</c>,
    /// and bash/wsl uses <c>'path'</c>.
    /// </summary>
    private static string FormatRunCommand(string shellExecutable, string path)
    {
        var shellExe = (shellExecutable ?? "powershell.exe").ToLowerInvariant();

        if (shellExe.Contains("cmd"))
            return $"\"{path}\"\n";

        if (shellExe.Contains("wsl") || shellExe.Contains("bash"))
            return $"'{path}'\n";

        // PowerShell (powershell.exe, pwsh.exe) is the default
        return $"& \"{path}\"\n";
    }

    private static Brush GetBrush(string resourceKey, Brush fallback)
    {
        return Application.Current.TryFindResource(resourceKey) as Brush ?? fallback;
    }

    private sealed class DisposablePlaceholderView : Border, IDisposable
    {
        private readonly IDisposable? _session;
        private bool _disposed;

        public DisposablePlaceholderView(string displayName, string connectionType, ISessionResult session)
        {
            _session = session as IDisposable;

            Background = GetBrush("BackgroundBrush", Brushes.Transparent);
            Child = BuildContent(displayName, connectionType);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _session?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by the session engine.
            }
        }

        private static FrameworkElement BuildContent(string displayName, string connectionType)
        {
            var message = string.Equals(connectionType, "SFTP", StringComparison.OrdinalIgnoreCase)
                ? "The SFTP session is connected, but the embedded browser view is not wired yet."
                : string.Format(
                    "The {0} session is connected, but no embedded view is available yet.",
                    connectionType);

            var outer = new Border
            {
                Margin = new Thickness(24),
                Padding = new Thickness(24),
                CornerRadius = new CornerRadius(16),
                Background = GetBrush("CardBrush", Brushes.Black),
                BorderBrush = GetBrush("BorderBrush", Brushes.DimGray),
                BorderThickness = new Thickness(1),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            var stack = new StackPanel
            {
                MaxWidth = 460
            };

            stack.Children.Add(new TextBlock
            {
                Text = displayName,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("TextPrimaryBrush", Brushes.White)
            });

            stack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = GetBrush("TextSecondaryBrush", Brushes.Gainsboro)
            });

            outer.Child = stack;
            return outer;
        }
    }
}
