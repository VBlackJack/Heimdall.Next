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
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
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

    private readonly ConcurrentQueue<string> _pendingTerminalMessages = new();

    private SshShellSession? _session;
    private Heimdall.Terminal.ITerminalSession? _terminalSession;
    private SessionTabViewModel? _sessionTab;
    private System.Threading.Timer? _keepAliveTimer;
    private Action<ReadOnlyMemory<byte>>? _terminalDataHandler;
    private Action<int>? _terminalExitHandler;
    private bool _disposed;
    private bool _webViewInitializationStarted;
    private bool _webViewInitialized;
    private bool _terminalReady;
    private bool _webViewUnavailable;
    private bool _initialTerminalFocusApplied;
    private bool _sleepPreventionActive;

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

    private void OnReconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Core.Logging.FileLogger.Info("EmbeddedSSH Reconnect requested by user");
        ReconnectRequested?.Invoke();
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
                                "The clipboard contains a potentially dangerous command.\n\nPaste anyway?",
                                "Paste Warning",
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
                                $"Paste {lineCount} lines into terminal?\n\nMulti-line paste executes commands automatically.",
                                "Multi-line Paste",
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

    private static string GetTerminalHtml()
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

        return html;
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
