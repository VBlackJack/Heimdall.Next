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
using Heimdall.Core.StateMachine;
using Heimdall.Sftp;
using Heimdall.Ssh;

namespace Heimdall.App.Services;

/// <summary>
/// Creates visual hosts for connection sessions so the shell can render
/// embedded protocol surfaces without teaching the ViewModel layer about WPF.
/// </summary>
public sealed class EmbeddedSessionManager : IEmbeddedSessionManager
{
    private readonly LocalizationManager _localizer;
    private readonly IDialogService _dialogService;
    private readonly HostKeyStore _hostKeyStore;
    private readonly ConnectionStateMachine _connectionSm;
    private readonly ToolRegistry _toolRegistry;

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

    /// <summary>
    /// Optional callback for cross-tool navigation. Allows tool views to open other tools.
    /// Parameters: (string toolId, string title, ToolContext? context).
    /// Wired by MainViewModel to delegate to <c>OpenToolTabAsync</c>.
    /// </summary>
    public Func<string, string, ToolContext?, Task>? OpenToolCallback { get; set; }

    public EmbeddedSessionManager(
        LocalizationManager localizer,
        IDialogService dialogService,
        HostKeyStore hostKeyStore,
        ConnectionStateMachine connectionSm,
        ToolRegistry toolRegistry)
    {
        _localizer = localizer;
        _dialogService = dialogService;
        _hostKeyStore = hostKeyStore;
        _connectionSm = connectionSm;
        _toolRegistry = toolRegistry;
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
            var resizeDelay = settings?.RdpResizeEnableDelayMs ?? 10000;
            view.InitializeSession(rdp.Server, sessionTab, antiIdleInterval, _localizer, rdp.TunnelPort, resizeDelay);
            WireSplitRequested(view, sessionTab);
            view.ReconnectRequested += () =>
                ReconnectRequestedCallback?.Invoke(
                    sessionTab,
                    !string.IsNullOrEmpty(sessionTab.OriginalServerId) ? sessionTab.OriginalServerId : sessionTab.ServerId,
                    sessionTab.ConnectionType);
            return view;
        }

        if (string.Equals(connectionType, "SSH", StringComparison.OrdinalIgnoreCase) &&
            session is SshSessionResult sshResult)
        {
            // Legacy SSH materialization path. The normal SSH pipeline now mounts
            // the view earlier via CreateConnectingSshHostControl and attaches here
            // only as a defensive fallback if SessionStarting was bypassed.
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
            // External elevated window: no embedded terminal, show info panel
            if (localBundle.IsExternal)
            {
                var infoPanel = new System.Windows.Controls.TextBlock
                {
                    Text = _localizer?["LocalShellExternalElevated"] ?? "Elevated shell launched in external window.",
                    FontSize = 14,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Foreground = GetBrush("TextSecondaryBrush", System.Windows.Media.Brushes.Gray),
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    TextAlignment = System.Windows.TextAlignment.Center
                };
                return infoPanel;
            }

            var termView = CreateTerminalSshView(sessionTab, localBundle.Session!, displayName, 0, settings, localBundle.IsElevated);

            // Auto-attach local file browser panel in a vertical split
            var fileBrowser = new Views.LocalFileBrowserView(
                localBundle.WorkingDirectory, _localizer, settings?.ExternalEditorPath);

            fileBrowser.NavigateToPathRequested += (path) =>
            {
                var cdCommand = FormatCdCommand(localBundle.ShellExecutable, path);
                localBundle.Session!.Write(System.Text.Encoding.UTF8.GetBytes(cdCommand));
            };

            fileBrowser.RunInShellRequested += (path) =>
            {
                var command = FormatRunCommand(localBundle.ShellExecutable, path);
                localBundle.Session!.Write(System.Text.Encoding.UTF8.GetBytes(command));
            };

            // Edit in embedded editor: swap file browser with AvalonEdit editor
            var isEditInEditorHandlerAttached = false;
            Action<string> editInEditorRequestedHandler = OnEditInEditorRequested;

            fileBrowser.Loaded += OnFileBrowserLoaded;
            fileBrowser.Unloaded += OnFileBrowserUnloaded;
            AttachEditInEditorHandler();

            async void OnEditInEditorRequested(string path)
            {
                var editorView = new Views.EmbeddedEditorView();
                await editorView.OpenFile(path);

                // When editor closes, restore the file browser
                editorView.Unloaded += OnEditorUnloaded;
                editorView.CloseRequested += OnEditorCloseRequested;

                void OnEditorCloseRequested()
                {
                    DetachEditorCloseRequestedHandler();

                    var browserPane = Heimdall.Core.Models.SplitTreeHelper.FindPaneByHostControl(
                        sessionTab.RootContent, editorView);
                    if (browserPane is not null)
                    {
                        browserPane.HostControl = fileBrowser;
                    }

                    fileBrowser.RefreshCurrentDirectory();
                }

                void OnEditorUnloaded(object? sender, RoutedEventArgs e)
                {
                    DetachEditorCloseRequestedHandler();
                }

                void DetachEditorCloseRequestedHandler()
                {
                    editorView.Unloaded -= OnEditorUnloaded;
                    // Detach to prevent handler leak identified by audit-2026-04-22 (PERF-01).
                    editorView.CloseRequested -= OnEditorCloseRequested;
                }

                var editorPane = Heimdall.Core.Models.SplitTreeHelper.FindPaneByHostControl(
                    sessionTab.RootContent, fileBrowser);
                if (editorPane is not null)
                {
                    editorPane.HostControl = editorView;
                }
            }

            void OnFileBrowserLoaded(object? sender, RoutedEventArgs e)
            {
                AttachEditInEditorHandler();
            }

            void OnFileBrowserUnloaded(object? sender, RoutedEventArgs e)
            {
                DetachEditInEditorHandler();
            }

            void AttachEditInEditorHandler()
            {
                if (isEditInEditorHandlerAttached)
                {
                    return;
                }

                fileBrowser.EditInEditorRequested += editInEditorRequestedHandler;
                isEditInEditorHandlerAttached = true;
            }

            void DetachEditInEditorHandler()
            {
                if (!isEditInEditorHandlerAttached)
                {
                    return;
                }

                // Detach to prevent handler leak identified by audit-2026-04-22 (PERF-01).
                fileBrowser.EditInEditorRequested -= editInEditorRequestedHandler;
                isEditInEditorHandlerAttached = false;
            }

            // Wrap the terminal view's pane with a file browser in a vertical split
            var fileBrowserPane = new Heimdall.Core.Models.SessionPaneModel
            {
                ConnectionType = "LOCAL",
                Title = displayName,
                Status = "Connected"
            };
            fileBrowserPane.HostControl = fileBrowser;

            var currentRoot = sessionTab.RootContent;
            sessionTab.RootContent = new Heimdall.Core.Models.SplitContainerModel
            {
                First = currentRoot,
                Second = fileBrowserPane,
                Orientation = Heimdall.Core.Models.SplitOrientation.Vertical,
                SplitRatio = 0.5
            };

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
            view.SessionConnected += (serverId) =>
            {
                _connectionSm.TryTransition(serverId, ConnectionState.Connected);
                sessionTab.Status = _localizer["StatusConnected"];
                Core.Logging.FileLogger.Info($"VNC connected: {serverId}");
            };
            view.SessionError += (serverId, errorMsg) =>
            {
                var localizedMsg = _localizer.Format("ErrorVncConnectionFailed", errorMsg);
                _connectionSm.SetError(serverId, localizedMsg);
                sessionTab.Status = localizedMsg;
                Core.Logging.FileLogger.Error($"VNC error for {serverId}: {errorMsg}");
            };
            WireVncSplitRequested(view, sessionTab);
            WireVncReconnectRequested(view, sessionTab);
            _ = view.InitializeSessionAsync(vnc, sessionTab, displayName, _localizer)
                .ContinueWith(t =>
                {
                    if (t.Exception is not null)
                    {
                        Core.Logging.FileLogger.Error(
                            $"VNC init failed for {sessionTab.ServerId}: {t.Exception.GetBaseException()}");
                    }
                },
                    TaskContinuationOptions.OnlyOnFaulted);
            return view;
        }

        if (string.Equals(connectionType, "TELNET", StringComparison.OrdinalIgnoreCase)
            && session is TerminalSessionResult telnetResult)
        {
            return CreateTerminalSshView(sessionTab, telnetResult.Session, displayName, 0, settings);
        }

        return new DisposablePlaceholderView(displayName, connectionType, session);
    }

    /// <summary>
    /// Creates an <see cref="EmbeddedSshView"/> mounted in the "Connecting"
    /// state before <c>SshHandler.ConnectAsync</c> has produced a session.
    /// Use <see cref="AttachSshSession"/> once the session is available.
    /// </summary>
    public EmbeddedSshView CreateConnectingSshHostControl(
        SessionTabViewModel sessionTab,
        string displayName,
        ServerProfileDto server,
        AppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(sessionTab);
        ArgumentNullException.ThrowIfNull(server);

        var view = new EmbeddedSshView { Localizer = _localizer, TerminalSettings = settings };
        view.InitializeConnecting(sessionTab, displayName, BuildSshEndpointLabel(server));

        WireBroadcast(view);
        WireSplitRequested(view, sessionTab);
        WireReconnectRequested(view, sessionTab);

        return view;
    }

    /// <summary>
    /// Attaches a freshly-connected SSH session result to a tab whose host
    /// control was previously created by <see cref="CreateConnectingSshHostControl"/>.
    /// </summary>
    public void AttachSshSession(
        SessionTabViewModel sessionTab,
        ISessionResult sessionResult,
        AppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(sessionTab);
        ArgumentNullException.ThrowIfNull(sessionResult);

        if (sessionTab.HostControl is not EmbeddedSshView view)
        {
            throw new InvalidOperationException(
                "AttachSshSession expects the tab's HostControl to be an EmbeddedSshView created by CreateConnectingSshHostControl.");
        }

        var keepAlive = settings?.SshTmoutResetIntervalSeconds ?? 240;
        switch (sessionResult)
        {
            case SshSessionResult sshResult:
                view.AttachSession(sshResult.Session, keepAlive);
                break;
            case TerminalSessionResult terminalResult:
                view.AttachTerminalSession(terminalResult.Session, keepAlive);
                break;
            default:
                throw new InvalidOperationException(
                    $"AttachSshSession expects an SSH session result, got {sessionResult.GetType().Name}.");
        }
    }

    private static string BuildSshEndpointLabel(ServerProfileDto server)
    {
        var user = string.IsNullOrWhiteSpace(server.SshUsername) ? "?" : server.SshUsername;
        var host = string.IsNullOrWhiteSpace(server.RemoteServer) ? "?" : server.RemoteServer;
        var port = server.SshPort > 0 ? server.SshPort : 22;
        return $"{user}@{host}:{port}";
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
        AppSettings? settings = null,
        bool isElevated = false)
    {
        var view = new EmbeddedSshView { Localizer = _localizer, TerminalSettings = settings };
        view.InitializeTerminalSession(terminalSession, tab, displayName, keepAliveIntervalSeconds);
        if (isElevated)
        {
            view.SetElevatedIndicator(true);
        }
        else
        {
            view.ShowElevateButton(true);
        }

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

        // Wire "Open in Terminal" to send a cd command to any SSH terminal
        // in the same tab's split tree.
        view.OpenInTerminalRequested += (path) =>
        {
            foreach (var pane in Heimdall.Core.Models.SplitTreeHelper.EnumerateLeaves(tab.RootContent))
            {
                if (pane.HostControl is EmbeddedSshView sshView)
                {
                    sshView.WriteCommand($"cd \"{path}\"");
                    break;
                }
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

    private void WireVncSplitRequested(EmbeddedVncView view, SessionTabViewModel tab)
    {
        view.RequestSplit += (_) => SplitRequestedCallback?.Invoke(tab);
    }

    private void WireVncReconnectRequested(EmbeddedVncView view, SessionTabViewModel tab)
    {
        view.RequestReconnect += (_) =>
            ReconnectRequestedCallback?.Invoke(
                tab,
                !string.IsNullOrEmpty(tab.OriginalServerId) ? tab.OriginalServerId : tab.ServerId,
                tab.ConnectionType);
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

    /// <summary>
    /// Creates a host control for a tool tab (non-connection UI surface).
    /// Uses the centralized <see cref="ToolRegistry"/> to instantiate the correct view.
    /// </summary>
    public object CreateToolControl(
        SessionTabViewModel sessionTab,
        string toolId,
        ToolContext? context,
        AppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(sessionTab);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);

        var descriptor = _toolRegistry.GetById(toolId);
        if (descriptor is null)
        {
            Core.Logging.FileLogger.Warn($"Unknown tool ID: {toolId}");
            return new TextBlock
            {
                Text = $"Tool: {toolId}",
                FontSize = 18,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = GetBrush("TextPrimaryBrush", Brushes.White)
            };
        }

        var view = _toolRegistry.CreateView(toolId);

        // Enrich context with SSH gateways so tools can offer "Route via" tunnel support
        if (settings?.SshGateways is { Count: > 0 } gateways)
        {
            context = (context ?? new ToolContext()) with
            {
                SshGateways = (System.Collections.IList)gateways
            };
        }

        // Inject cross-tool navigation callback so tools can open other tools
        if (OpenToolCallback is not null)
        {
            context = (context ?? new ToolContext()) with
            {
                OpenToolAction = OpenToolCallback
            };
        }

        // Inject busy state callback so tools can signal long-running operations.
        // Inject send-to-terminal callback for command injection into sibling terminals.
        context = (context ?? new ToolContext()) with
        {
            SetBusyAction = busy => sessionTab.IsBusy = busy,
            SendCommandAction = command =>
            {
                foreach (var pane in Core.Models.SplitTreeHelper.EnumerateLeaves(sessionTab.RootContent))
                {
                    if (pane.HostControl is Views.EmbeddedSshView sshView)
                    {
                        sshView.WriteCommand(command);
                        return;
                    }
                }
            }
        };

        view.Initialize(context, _localizer);
        return view;
    }
}
