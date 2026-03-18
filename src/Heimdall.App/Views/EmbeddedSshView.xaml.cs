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

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;
using Heimdall.Ssh;
using Microsoft.Web.WebView2.Core;

namespace Heimdall.App.Views;

/// <summary>
/// WPF host for an interactive SSH shell session rendered through WebView2 + xterm.js.
/// The browser surface handles VT parsing, ANSI colors, cursor movement, and scrollback.
/// </summary>
public partial class EmbeddedSshView : UserControl, IDisposable
{
    private static readonly (string Tag, string RelativePath, string WrapperStart, string WrapperEnd)[] InlineAssets =
    [
        ("<link rel=\"stylesheet\" href=\"./Terminal/xterm.min.css\" />", Path.Combine("Terminal", "xterm.min.css"), "<style>", "</style>"),
        ("<script src=\"./Terminal/xterm.min.js\"></script>", Path.Combine("Terminal", "xterm.min.js"), "<script>", "</script>"),
        ("<script src=\"./Terminal/addon-fit.min.js\"></script>", Path.Combine("Terminal", "addon-fit.min.js"), "<script>", "</script>"),
        ("<script src=\"./Terminal/addon-webgl.min.js\"></script>", Path.Combine("Terminal", "addon-webgl.min.js"), "<script>", "</script>")
    ];

    private static readonly byte[] KeepAliveCr = [0x0D];

    /// <summary>
    /// Maps color scheme names to xterm.js theme JSON object literals.
    /// Keys must match the values stored in <see cref="AppSettings.TerminalColorScheme"/>.
    /// </summary>
    private static readonly FrozenDictionary<string, string> ColorSchemes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dracula"] = """
                {
                    background: '#282A36',
                    foreground: '#F8F8F2',
                    cursor: '#BD93F9',
                    cursorAccent: '#282A36',
                    selectionBackground: 'rgba(68, 71, 90, 0.7)',
                    selectionForeground: '#F8F8F2',
                    black: '#21222C',
                    red: '#FF5555',
                    green: '#50FA7B',
                    yellow: '#F1FA8C',
                    blue: '#BD93F9',
                    magenta: '#FF79C6',
                    cyan: '#8BE9FD',
                    white: '#F8F8F2',
                    brightBlack: '#6272A4',
                    brightRed: '#FF6E6E',
                    brightGreen: '#69FF94',
                    brightYellow: '#FFFFA5',
                    brightBlue: '#D6ACFF',
                    brightMagenta: '#FF92DF',
                    brightCyan: '#A4FFFF',
                    brightWhite: '#FFFFFF'
                }
                """,
            ["Solarized Dark"] = """
                {
                    background: '#002B36',
                    foreground: '#839496',
                    cursor: '#93A1A1',
                    cursorAccent: '#002B36',
                    selectionBackground: 'rgba(7, 54, 66, 0.7)',
                    selectionForeground: '#93A1A1',
                    black: '#073642',
                    red: '#DC322F',
                    green: '#859900',
                    yellow: '#B58900',
                    blue: '#268BD2',
                    magenta: '#D33682',
                    cyan: '#2AA198',
                    white: '#EEE8D5',
                    brightBlack: '#586E75',
                    brightRed: '#CB4B16',
                    brightGreen: '#586E75',
                    brightYellow: '#657B83',
                    brightBlue: '#839496',
                    brightMagenta: '#6C71C4',
                    brightCyan: '#93A1A1',
                    brightWhite: '#FDF6E3'
                }
                """,
            ["Monokai"] = """
                {
                    background: '#272822',
                    foreground: '#F8F8F2',
                    cursor: '#F8F8F0',
                    cursorAccent: '#272822',
                    selectionBackground: 'rgba(73, 72, 62, 0.7)',
                    selectionForeground: '#F8F8F2',
                    black: '#272822',
                    red: '#F92672',
                    green: '#A6E22E',
                    yellow: '#F4BF75',
                    blue: '#66D9EF',
                    magenta: '#AE81FF',
                    cyan: '#A1EFE4',
                    white: '#F8F8F2',
                    brightBlack: '#75715E',
                    brightRed: '#F92672',
                    brightGreen: '#A6E22E',
                    brightYellow: '#F4BF75',
                    brightBlue: '#66D9EF',
                    brightMagenta: '#AE81FF',
                    brightCyan: '#A1EFE4',
                    brightWhite: '#F9F8F5'
                }
                """,
            ["Nord"] = """
                {
                    background: '#2E3440',
                    foreground: '#D8DEE9',
                    cursor: '#D8DEE9',
                    cursorAccent: '#2E3440',
                    selectionBackground: 'rgba(67, 76, 94, 0.7)',
                    selectionForeground: '#ECEFF4',
                    black: '#3B4252',
                    red: '#BF616A',
                    green: '#A3BE8C',
                    yellow: '#EBCB8B',
                    blue: '#81A1C1',
                    magenta: '#B48EAD',
                    cyan: '#88C0D0',
                    white: '#E5E9F0',
                    brightBlack: '#4C566A',
                    brightRed: '#BF616A',
                    brightGreen: '#A3BE8C',
                    brightYellow: '#EBCB8B',
                    brightBlue: '#81A1C1',
                    brightMagenta: '#B48EAD',
                    brightCyan: '#8FBCBB',
                    brightWhite: '#ECEFF4'
                }
                """,
            ["Default"] = "{}"
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Regex to strip ANSI/VT escape sequences from terminal output for transcript logging.</summary>
    private static readonly Regex AnsiEscapeRegex = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\].*?(?:\x07|\x1B\\))",
        RegexOptions.Compiled);

    private readonly ConcurrentQueue<string> _pendingTerminalMessages = new();
    private readonly object _logLock = new();

    private StreamWriter? _logStream;
    private string? _logFilePath;
    private SshShellSession? _session;
    private Heimdall.Terminal.ITerminalSession? _terminalSession;
    private SessionTabViewModel? _sessionTab;
    private System.Threading.Timer? _keepAliveTimer;
    private Action<ReadOnlyMemory<byte>>? _terminalDataHandler;
    private Action<int>? _terminalExitHandler;
    private Core.Localization.LocalizationManager? _localizer;
    private ServerHealthMonitor? _healthMonitor;
    private readonly List<MacroEntry> _macroEntries = [];
    private readonly Stopwatch _macroStopwatch = new();

    private bool _healthPanelVisible;
    private bool _disposed;
    private bool _webViewInitializationStarted;
    private bool _webViewInitialized;
    private bool _terminalReady;
    private bool _webViewUnavailable;
    private bool _initialTerminalFocusApplied;
    private bool _sleepPreventionActive;
    private bool _userInitiatedDisconnect;
    private bool _isRecording;

    /// <summary>Localizer for translating user-facing strings. Set by EmbeddedSessionManager.</summary>
    public Core.Localization.LocalizationManager? Localizer
    {
        get => _localizer;
        set => _localizer = value;
    }

    /// <summary>Terminal appearance settings (font, color scheme). Set by EmbeddedSessionManager before Loaded fires.</summary>
    public AppSettings? TerminalSettings { get; set; }

    private bool IsSessionConnected =>
        (_session?.IsConnected ?? false) || (_terminalSession?.IsRunning ?? false);

    /// <summary>
    /// Raised when the user clicks the Split button in the header strip.
    /// The subscriber (EmbeddedSessionManager) shows the split picker context menu.
    /// </summary>
    public event Action? SplitRequested;

    /// <summary>
    /// Raised when the user types input and broadcast mode may be active.
    /// The subscriber (MainViewModel) decides whether to relay to other terminals.
    /// </summary>
    public event Action<byte[]>? BroadcastInput;

    /// <summary>
    /// Raised when the user clicks the Reconnect button after a disconnection.
    /// The subscriber (EmbeddedSessionManager) re-establishes the connection
    /// using the original server parameters.
    /// </summary>
    public event Action? ReconnectRequested;

    public EmbeddedSshView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        IsVisibleChanged += OnVisibilityChanged;
    }

    /// <summary>
    /// Wires the view to a connected SSH shell session. Must be called
    /// exactly once, immediately after construction.
    /// </summary>
    public void InitializeSession(
        SshShellSession session,
        SessionTabViewModel sessionTab,
        string displayName,
        string endpoint,
        int keepAliveIntervalSeconds = 240)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sessionTab);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EmbeddedSshView));
        }

        _session = session;
        _sessionTab = sessionTab;

        LocalizeButtons();
        SessionTitleText.Text = displayName;
        EndpointTextBlock.Text = endpoint;
        UpdateStatus("Connected");

        _session.DataReceived += OnDataReceived;
        _session.Disconnected += OnDisconnected;

        StartKeepAliveTimer(keepAliveIntervalSeconds);
        AcquireSleepPrevention();
    }

    /// <summary>
    /// Initializes the view with a terminal session backed by Plink/ConPTY.
    /// </summary>
    public void InitializeTerminalSession(
        Heimdall.Terminal.ITerminalSession terminalSession,
        SessionTabViewModel sessionTab,
        string displayName,
        int keepAliveIntervalSeconds = 240)
    {
        ArgumentNullException.ThrowIfNull(terminalSession);
        ArgumentNullException.ThrowIfNull(sessionTab);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EmbeddedSshView));
        }

        _terminalSession = terminalSession;
        _sessionTab = sessionTab;

        LocalizeButtons();
        SessionTitleText.Text = displayName;
        EndpointTextBlock.Text = "via Plink";
        UpdateStatus("Connected");

        _terminalDataHandler = OnTerminalDataReceived;
        _terminalExitHandler = OnTerminalProcessExited;

        _terminalSession.DataReceived += _terminalDataHandler;
        _terminalSession.ProcessExited += _terminalExitHandler;

        StartKeepAliveTimer(keepAliveIntervalSeconds);
        AcquireSleepPrevention();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopHealthMonitor();
        StopTranscript();
        StopKeepAliveTimer();
        ReleaseSleepPrevention();

        Loaded -= OnLoaded;
        IsVisibleChanged -= OnVisibilityChanged;

        if (_webViewInitialized && TerminalWebView.CoreWebView2 is not null)
        {
            TerminalWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            TerminalWebView.CoreWebView2.ProcessFailed -= OnWebViewProcessFailed;
        }

        if (_session is not null)
        {
            _session.DataReceived -= OnDataReceived;
            _session.Disconnected -= OnDisconnected;

            try
            {
                _session.Disconnect();
            }
            catch (ObjectDisposedException)
            {
                // Already cleaned up.
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn(
                    $"EmbeddedSSH Disconnect during dispose failed: {ex.Message}");
            }

            try
            {
                _session.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by Disconnect path.
            }

            _session = null;
        }

        if (_terminalSession is not null)
        {
            if (_terminalDataHandler is not null)
            {
                _terminalSession.DataReceived -= _terminalDataHandler;
            }

            if (_terminalExitHandler is not null)
            {
                _terminalSession.ProcessExited -= _terminalExitHandler;
            }

            try { _terminalSession.Kill(); } catch { }
            try { _terminalSession.Dispose(); } catch { }
            _terminalSession = null;
        }

        _terminalDataHandler = null;
        _terminalExitHandler = null;

        if (TerminalWebView is IDisposable disposableWebView)
        {
            try { disposableWebView.Dispose(); } catch { }
        }

        while (_pendingTerminalMessages.TryDequeue(out _))
        {
            // Drain pending messages to release buffers.
        }

        Core.Logging.FileLogger.Info("EmbeddedSSH Dispose completed");
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && _terminalReady && !_disposed)
        {
            // Re-focus terminal when tab becomes visible (tab switch)
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed && _terminalReady)
                {
                    TerminalWebView.Focus();
                    PostTerminalMessage("focus:");
                }
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitializeWebView2Async();
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _userInitiatedDisconnect = true;
            Core.Logging.FileLogger.Info("EmbeddedSSH Disconnect requested by user");
            UpdateStatus("Disconnected");

            if (_session is not null)
            {
                _session.Disconnect();
            }
            else
            {
                _terminalSession?.Kill();
            }
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"EmbeddedSSH manual disconnect failed: {ex.Message}");
            UpdateStatus("Error");
        }
    }

    private void OnSplitClick(object sender, RoutedEventArgs e)
    {
        SplitRequested?.Invoke();
    }

    private void OnHealthToggleClick(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _healthPanelVisible = !_healthPanelVisible;

        if (_healthPanelVisible)
        {
            ShowHealthPanel();
        }
        else
        {
            HideHealthPanel();
        }
    }

    private void ShowHealthPanel()
    {
        HealthPanel.Visibility = Visibility.Visible;
        HealthColumnDef.Width = new GridLength(180);
        LocalizeHealthLabels();

        var client = _session?.Client;
        if (client is null || !client.IsConnected)
        {
            Core.Logging.FileLogger.Warn(
                "ServerHealthMonitor: no connected SSH client available for health monitoring");
            return;
        }

        StopHealthMonitor();

        _healthMonitor = new ServerHealthMonitor();
        _healthMonitor.HealthUpdated += OnHealthDataReceived;
        _ = _healthMonitor.StartAsync(client);

        Core.Logging.FileLogger.Info("ServerHealthMonitor started");
    }

    private void HideHealthPanel()
    {
        HealthPanel.Visibility = Visibility.Collapsed;
        HealthColumnDef.Width = new GridLength(0);
        StopHealthMonitor();
    }

    private void StopHealthMonitor()
    {
        if (_healthMonitor is not null)
        {
            _healthMonitor.HealthUpdated -= OnHealthDataReceived;
            _healthMonitor.Dispose();
            _healthMonitor = null;
            Core.Logging.FileLogger.Info("ServerHealthMonitor stopped");
        }
    }

    private void OnHealthDataReceived(ServerHealthData data)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed || !_healthPanelVisible)
            {
                return;
            }

            CpuProgressBar.Value = data.CpuPercent;
            CpuPercentText.Text = $"{data.CpuPercent:F1}%";

            double ramPercent = data.MemTotalMb > 0
                ? (double)data.MemUsedMb / data.MemTotalMb * 100.0
                : 0;
            RamProgressBar.Value = ramPercent;
            RamDetailText.Text = $"{data.MemUsedMb} / {data.MemTotalMb} MB";

            DiskProgressBar.Value = data.DiskPercent;
            DiskDetailText.Text = $"{data.DiskUsed} / {data.DiskTotal}";
        });
    }

    private void LocalizeHealthLabels()
    {
        HealthCpuLabel.Text = L("HealthPanelCpu");
        HealthRamLabel.Text = L("HealthPanelRam");
        HealthDiskLabel.Text = L("HealthPanelDisk");
        HealthToggleButton.ToolTip = L("HealthPanelToggle");
    }

    private void OnReconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Core.Logging.FileLogger.Info("EmbeddedSSH Reconnect requested by user");
        ReconnectRequested?.Invoke();
    }

    private void OnOverlayReconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        HideReconnectOverlay();
        Core.Logging.FileLogger.Info("EmbeddedSSH Reconnect requested via overlay");
        ReconnectRequested?.Invoke();
    }

    private void OnOverlayCloseClick(object sender, RoutedEventArgs e)
    {
        HideReconnectOverlay();
    }

    private void ShowReconnectOverlay()
    {
        ReconnectOverlay.Visibility = Visibility.Visible;
    }

    private void HideReconnectOverlay()
    {
        ReconnectOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task InitializeWebView2Async()
    {
        if (_disposed || _webViewInitializationStarted)
        {
            return;
        }

        _webViewInitializationStarted = true;

        try
        {
            Core.Logging.FileLogger.Info("EmbeddedSSH initializing WebView2 terminal surface");

            await TerminalWebView.EnsureCoreWebView2Async();
            if (_disposed || TerminalWebView.CoreWebView2 is null)
            {
                return;
            }

            var core = TerminalWebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.WebMessageReceived += OnWebMessageReceived;
            core.ProcessFailed += OnWebViewProcessFailed;

            // Block all navigation away from the inline terminal page
            core.NavigationStarting += (_, navArgs) =>
            {
                // Allow the initial NavigateToString (about:blank origin)
                if (navArgs.Uri is not null
                    && !navArgs.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                    && !navArgs.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    navArgs.Cancel = true;
                    Core.Logging.FileLogger.Warn(
                        $"EmbeddedSSH blocked navigation to: {navArgs.Uri}");
                }
            };

            core.NavigateToString(GetTerminalHtml());
            _webViewInitialized = true;

            Core.Logging.FileLogger.Info("EmbeddedSSH WebView2 NavigateToString issued");
        }
        catch (Exception ex)
        {
            ShowWebViewUnavailable($"WebView2 could not be initialized. {ex.Message}");
            Core.Logging.FileLogger.Warn($"EmbeddedSSH WebView2 initialization failed: {ex.Message}");
        }
    }

    private void OnWebViewProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Core.Logging.FileLogger.Warn($"EmbeddedSSH WebView2 process failed: {e.ProcessFailedKind}");
        ShowWebViewUnavailable("The embedded terminal renderer crashed.");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        // Validate message source — only accept messages from our inline terminal page
        var source = args.Source;
        if (!string.IsNullOrEmpty(source)
            && !source.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            && !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            Core.Logging.FileLogger.Warn(
                $"EmbeddedSSH rejected WebMessage from unexpected source: {source}");
            return;
        }

        string message;

        try
        {
            message = args.TryGetWebMessageAsString();
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"EmbeddedSSH invalid WebView2 message: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        // Open URL: terminal requests to open a link in the default browser
        if (message.StartsWith("open-url:", StringComparison.Ordinal))
        {
            var url = message["open-url:".Length..];
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = uri.AbsoluteUri,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Core.Logging.FileLogger.Warn($"EmbeddedSSH open-url failed: {ex.Message}");
                }
            }
            return;
        }

        if (message.StartsWith("ready:", StringComparison.Ordinal))
        {
            _terminalReady = true;
            Core.Logging.FileLogger.Info($"EmbeddedSSH terminal ready: {message}");

            if (TryParseSize(message.AsSpan("ready:".Length), out var readyCols, out var readyRows))
            {
                ResizeSession(readyCols, readyRows);
            }

            FlushPendingTerminalMessages();
            ApplyInitialTerminalFocus();
            return;
        }

        if (message.StartsWith("resize:", StringComparison.Ordinal))
        {
            if (TryParseSize(message.AsSpan("resize:".Length), out var cols, out var rows))
            {
                ResizeSession(cols, rows);
            }
            return;
        }

        if (message.StartsWith("input:", StringComparison.Ordinal))
        {
            var base64 = message["input:".Length..];

            try
            {
                var bytes = Convert.FromBase64String(base64);
                WriteToSession(bytes);

                // Broadcast to other terminals when broadcast mode is active
                BroadcastInput?.Invoke(bytes);
            }
            catch (FormatException ex)
            {
                Core.Logging.FileLogger.Warn($"EmbeddedSSH invalid input payload: {ex.Message}");
            }
            return;
        }

        // Clipboard write: terminal wants to copy text to system clipboard
        if (message.StartsWith("clipboard-write:", StringComparison.Ordinal))
        {
            try
            {
                var base64 = message["clipboard-write:".Length..];
                var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                if (!string.IsNullOrEmpty(text))
                {
                    Clipboard.SetText(text);
                }
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"EmbeddedSSH clipboard-write failed: {ex.Message}");
            }
            return;
        }

        // Clipboard read: terminal requests paste from system clipboard
        // Protected by SmartPasteGuard to prevent dangerous multi-line or destructive pastes.
        if (message.StartsWith("clipboard-read:", StringComparison.Ordinal))
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(text))
                    {
                        var risk = Heimdall.Terminal.SmartPasteGuard.Evaluate(text);

                        var owner = Window.GetWindow(this);

                        if (risk == Heimdall.Terminal.SmartPasteGuard.PasteRisk.Dangerous)
                        {
                            var proceed = System.Windows.MessageBox.Show(
                                owner,
                                L("PasteWarningMessage"),
                                L("PasteWarningTitle"),
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);

                            if (proceed != MessageBoxResult.Yes)
                            {
                                return;
                            }
                        }
                        else if (risk == Heimdall.Terminal.SmartPasteGuard.PasteRisk.MultiLine)
                        {
                            int lineCount = text.Split('\n').Length;
                            var proceed = System.Windows.MessageBox.Show(
                                owner,
                                string.Format(L("PasteMultiLineMessage"), lineCount),
                                L("PasteMultiLineTitle"),
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (proceed != MessageBoxResult.Yes)
                            {
                                return;
                            }
                        }

                        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                        PostTerminalMessage("clipboard-paste:" + base64);
                    }
                }
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"EmbeddedSSH clipboard-read failed: {ex.Message}");
            }
            return;
        }

        WriteToSession(message);
    }

    private void OnDataReceived(byte[] data)
    {
        QueueOutput(data);
    }

    private void OnTerminalDataReceived(ReadOnlyMemory<byte> data)
    {
        QueueOutput(data.Span);
    }

    private void QueueOutput(ReadOnlySpan<byte> data)
    {
        if (_disposed || _webViewUnavailable || data.IsEmpty)
        {
            return;
        }

        WriteToTranscript(data);

        var message = "data:" + Convert.ToBase64String(data);
        PostTerminalMessage(message);
    }

    private void OnDisconnected(string? errorMessage)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                var disconnectText = $"\r\n\x1b[90m[Session disconnected: {errorMessage}]\x1b[0m\r\n";
                QueueOutput(Encoding.UTF8.GetBytes(disconnectText));
            }

            PostTerminalMessage("session-ended:");
            UpdateStatus("Disconnected");

            if (!_userInitiatedDisconnect)
            {
                ShowReconnectOverlay();
            }
        });
    }

    private void OnTerminalProcessExited(int exitCode)
    {
        OnDisconnected($"Process exited with code {exitCode}");
    }

    private void ResizeSession(int columns, int rows)
    {
        if (_disposed || columns <= 0 || rows <= 0)
        {
            return;
        }

        try
        {
            if (_session is not null)
            {
                _session.Resize(columns, rows);
            }
            else
            {
                _terminalSession?.Resize(columns, rows);
            }
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"EmbeddedSSH resize failed for {columns}x{rows}: {ex.Message}");
        }
    }

    private void WriteToSession(byte[] data)
    {
        if (_disposed || data.Length == 0)
        {
            return;
        }

        if (_isRecording)
        {
            var delayMs = (int)_macroStopwatch.ElapsedMilliseconds;
            _macroStopwatch.Restart();

            _macroEntries.Add(new MacroEntry
            {
                Input = Encoding.UTF8.GetString(data),
                DelayMs = delayMs
            });
        }

        if (_session is not null)
        {
            _session.Write(data);
        }
        else
        {
            _terminalSession?.Write(data);
        }
    }

    /// <summary>
    /// Sends a command string followed by a newline to the active session.
    /// Intended for external callers (e.g. SFTP "Open in Terminal").
    /// </summary>
    public void WriteCommand(string command)
    {
        WriteToSession(command + "\n");
    }

    /// <summary>
    /// Sends raw bytes to the active session without triggering broadcast.
    /// Used by the broadcast relay to avoid infinite loops.
    /// </summary>
    public void WriteBytes(byte[] data) => WriteToSession(data);

    /// <summary>
    /// Shows or hides the broadcast mode indicator badge in the header strip.
    /// </summary>
    public void SetBroadcastIndicator(bool active)
    {
        if (_disposed) return;

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => SetBroadcastIndicator(active));
            return;
        }

        BroadcastBadge.Visibility = active
            ? Visibility.Visible
            : Visibility.Collapsed;

        BroadcastBorder.BorderThickness = active
            ? new Thickness(2)
            : new Thickness(0);
    }

    /// <summary>Whether a transcript recording is currently active.</summary>
    public bool IsTranscriptActive
    {
        get { lock (_logLock) { return _logStream is not null; } }
    }

    /// <summary>
    /// Starts recording terminal output to a log file.
    /// If a transcript is already active, it is stopped first.
    /// </summary>
    public void StartTranscript(string logFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

        lock (_logLock)
        {
            StopTranscriptInternal();

            var dir = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _logStream = new StreamWriter(logFilePath, append: true, Encoding.UTF8)
            {
                AutoFlush = true
            };
            _logFilePath = logFilePath;

            Core.Logging.FileLogger.Info($"Transcript started: {logFilePath}");
        }
    }

    /// <summary>Stops the active transcript recording, if any.</summary>
    public void StopTranscript()
    {
        lock (_logLock)
        {
            StopTranscriptInternal();
        }
    }

    private void StopTranscriptInternal()
    {
        if (_logStream is not null)
        {
            try
            {
                _logStream.Flush();
                _logStream.Dispose();
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"Transcript close error: {ex.Message}");
            }

            Core.Logging.FileLogger.Info($"Transcript stopped: {_logFilePath}");
            _logStream = null;
            _logFilePath = null;
        }
    }

    /// <summary>Writes raw terminal data to the transcript file, stripping ANSI escape sequences.</summary>
    private void WriteToTranscript(ReadOnlySpan<byte> data)
    {
        lock (_logLock)
        {
            if (_logStream is null)
            {
                return;
            }

            try
            {
                var text = Encoding.UTF8.GetString(data);
                var clean = AnsiEscapeRegex.Replace(text, string.Empty);
                if (clean.Length > 0)
                {
                    _logStream.Write(clean);
                }
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"Transcript write error: {ex.Message}");
            }
        }
    }

    private void WriteToSession(string text)
    {
        if (_disposed || string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_session is not null)
        {
            _session.Write(text);
        }
        else
        {
            _terminalSession?.Write(text);
        }
    }

    /// <summary>Whether a macro recording is currently in progress.</summary>
    public bool IsRecordingMacro => _isRecording;

    /// <summary>Clears any previous recording state and begins capturing terminal input.</summary>
    public void StartRecording()
    {
        _macroEntries.Clear();
        _macroStopwatch.Restart();
        _isRecording = true;
        Core.Logging.FileLogger.Info("Macro recording started");
    }

    /// <summary>
    /// Stops recording and returns the captured entries.
    /// Returns an empty list if recording was not active.
    /// </summary>
    public List<MacroEntry> StopRecording()
    {
        _isRecording = false;
        _macroStopwatch.Stop();
        Core.Logging.FileLogger.Info($"Macro recording stopped ({_macroEntries.Count} entries)");
        return new List<MacroEntry>(_macroEntries);
    }

    /// <summary>
    /// Replays a previously recorded macro by sending each entry to the terminal
    /// with the recorded inter-input delays.
    /// </summary>
    public async Task PlayMacro(TerminalMacro macro, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(macro);

        Core.Logging.FileLogger.Info($"Macro playback started: {macro.Name} ({macro.Entries.Count} entries)");

        foreach (var entry in macro.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.DelayMs > 0)
            {
                await Task.Delay(entry.DelayMs, ct);
            }

            var bytes = Encoding.UTF8.GetBytes(entry.Input);
            WriteToSession(bytes);
        }

        Core.Logging.FileLogger.Info($"Macro playback completed: {macro.Name}");
    }

    /// <summary>Resolves a locale key, falling back to the key name if no localizer is set.</summary>
    private string L(string key) => _localizer?[key] ?? key;

    private void LocalizeButtons()
    {
        if (_localizer is null) return;
        DisconnectButton.Content = L("BtnDisconnectSession");
        ReconnectButton.Content = L("BtnReconnectSession");
        FallbackMessageText.Text = L("EmbeddedSshFallbackMessage");
        OverlayReconnectButton.Content = L("BtnReconnectSession");
        OverlayCloseButton.Content = L("BtnCloseOverlay");
        ReconnectMessageText.Text = L("SshDisconnectedMessage");
    }

    private void PostTerminalMessage(string message)
    {
        if (_disposed || _webViewUnavailable)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => PostTerminalMessage(message));
            return;
        }

        if (_terminalReady && TerminalWebView.CoreWebView2 is not null)
        {
            TerminalWebView.CoreWebView2.PostWebMessageAsString(message);
            return;
        }

        _pendingTerminalMessages.Enqueue(message);
    }

    private void FlushPendingTerminalMessages()
    {
        if (_disposed || !_terminalReady || TerminalWebView.CoreWebView2 is null)
        {
            return;
        }

        while (_pendingTerminalMessages.TryDequeue(out var message))
        {
            TerminalWebView.CoreWebView2.PostWebMessageAsString(message);
        }
    }

    private void ApplyInitialTerminalFocus()
    {
        if (_disposed || _webViewUnavailable || _initialTerminalFocusApplied)
        {
            return;
        }

        if (_terminalReady)
        {
            _initialTerminalFocusApplied = true;
            Core.Logging.FileLogger.Info("EmbeddedSSH applying initial terminal focus");
            TerminalWebView.Focus();
            PostTerminalMessage("focus:");
        }
    }

    private void ShowWebViewUnavailable(string message)
    {
        if (_disposed)
        {
            return;
        }

        _webViewUnavailable = true;
        _terminalReady = false;

        while (_pendingTerminalMessages.TryDequeue(out _))
        {
            // Drop buffered output when no renderer is available.
        }

        TerminalWebView.Visibility = System.Windows.Visibility.Collapsed;
        FallbackPanel.Visibility = System.Windows.Visibility.Visible;
        FallbackMessageText.Text = message;
        UpdateStatus("Error");
    }

    private void StartKeepAliveTimer(int intervalSeconds)
    {
        if (intervalSeconds <= 0)
        {
            return;
        }

        _keepAliveTimer = new System.Threading.Timer(
            _ => SendKeepAlive(),
            null,
            TimeSpan.FromSeconds(intervalSeconds),
            TimeSpan.FromSeconds(intervalSeconds));
        Core.Logging.FileLogger.Info(
            $"SSH keepalive timer started ({intervalSeconds}s interval)");
    }

    private void StopKeepAliveTimer()
    {
        if (_keepAliveTimer is null)
        {
            return;
        }

        _keepAliveTimer.Dispose();
        _keepAliveTimer = null;
        Core.Logging.FileLogger.Info("SSH keepalive timer stopped");
    }

    private void SendKeepAlive()
    {
        if (_disposed)
        {
            return;
        }

        if (!IsSessionConnected)
        {
            Core.Logging.FileLogger.Warn("SSH keepalive skipped: session not connected");
            StopKeepAliveTimer();
            return;
        }

        try
        {
            WriteToSession(KeepAliveCr);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"SSH keepalive failed: {ex.Message}");
        }
    }

    private void AcquireSleepPrevention()
    {
        if (!_sleepPreventionActive)
        {
            _sleepPreventionActive = true;
            SleepPrevention.SessionStarted();
        }
    }

    private void ReleaseSleepPrevention()
    {
        if (_sleepPreventionActive)
        {
            _sleepPreventionActive = false;
            SleepPrevention.SessionEnded();
        }
    }

    private void UpdateStatus(string status)
    {
        if (_sessionTab is not null)
        {
            _sessionTab.Status = status;
        }

        StatusTextBlock.Text = status;

        var isDisconnected = string.Equals(status, "Disconnected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase);

        DisconnectButton.IsEnabled = !_disposed && !isDisconnected;
        DisconnectButton.Visibility = isDisconnected ? Visibility.Collapsed : Visibility.Visible;
        ReconnectButton.Visibility = isDisconnected ? Visibility.Visible : Visibility.Collapsed;

        StatusTextBlock.Foreground = string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase)
            ? GetBrush("ErrorBrush", Brushes.IndianRed)
            : GetBrush("TextPrimaryBrush", Brushes.White);
    }

    private Brush GetBrush(string resourceKey, Brush fallback)
    {
        return TryFindResource(resourceKey) as Brush ?? fallback;
    }

    private static bool TryParseSize(ReadOnlySpan<char> payload, out int columns, out int rows)
    {
        columns = 0;
        rows = 0;

        var separatorIndex = payload.IndexOf(',');
        if (separatorIndex <= 0 || separatorIndex >= payload.Length - 1)
        {
            return false;
        }

        return int.TryParse(payload[..separatorIndex], out columns)
            && int.TryParse(payload[(separatorIndex + 1)..], out rows)
            && columns > 0
            && rows > 0;
    }

    private string GetTerminalHtml()
    {
        var html = ReadTerminalAsset("terminal.html");

        foreach (var asset in InlineAssets)
        {
            var content = ReadTerminalAsset(asset.RelativePath);
            html = html.Replace(
                asset.Tag,
                asset.WrapperStart + content + asset.WrapperEnd,
                StringComparison.Ordinal);
        }

        // Inject terminal appearance settings from AppSettings
        var fontFamily = TerminalSettings?.TerminalFontFamily ?? "Consolas";
        var fontSize = TerminalSettings?.TerminalFontSize ?? 14;
        var schemeName = TerminalSettings?.TerminalColorScheme ?? "Dracula";

        if (!ColorSchemes.TryGetValue(schemeName, out var themeJson))
        {
            themeJson = ColorSchemes["Dracula"];
        }

        // Sanitize font family to prevent injection (allow alphanumeric, spaces, commas, quotes, hyphens)
        var safeFontFamily = System.Text.RegularExpressions.Regex.Replace(
            fontFamily, @"[^a-zA-Z0-9\s,'""\-]", "");

        var safeFontSize = Math.Clamp(fontSize, 8, 28);

        html = html.Replace("/*{{TERMINAL_FONT_FAMILY}}*/", safeFontFamily, StringComparison.Ordinal);
        html = html.Replace("/*{{TERMINAL_FONT_SIZE}}*/", safeFontSize.ToString(), StringComparison.Ordinal);
        html = html.Replace("/*{{TERMINAL_THEME}}*/", themeJson, StringComparison.Ordinal);

        // Extract background color from the theme for CSS variables
        var bgColor = ExtractThemeColor(themeJson, "background", "#282A36");
        var fgColor = ExtractThemeColor(themeJson, "foreground", "#F8F8F2");
        var cursorColor = ExtractThemeColor(themeJson, "cursor", "#BD93F9");
        var selectionColor = ExtractThemeColor(themeJson, "selectionBackground", "rgba(68, 71, 90, 0.7)");

        html = html.Replace("/*{{CSS_BG}}*/", bgColor, StringComparison.Ordinal);
        html = html.Replace("/*{{CSS_FG}}*/", fgColor, StringComparison.Ordinal);
        html = html.Replace("/*{{CSS_CURSOR}}*/", cursorColor, StringComparison.Ordinal);
        html = html.Replace("/*{{CSS_SELECTION}}*/", selectionColor, StringComparison.Ordinal);

        return html;
    }

    /// <summary>Extracts a color value from the xterm.js theme JS object literal.</summary>
    private static string ExtractThemeColor(string themeJson, string key, string fallback)
    {
        // Match pattern like: background: '#282A36' or background: 'rgba(...)'
        var match = Regex.Match(themeJson, $@"{key}:\s*'([^']+)'");
        return match.Success ? match.Groups[1].Value : fallback;
    }

    private static string ReadTerminalAsset(string relativePath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "Assets", relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Terminal asset not found: {fullPath}", fullPath);
        }

        return File.ReadAllText(fullPath);
    }
}
