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
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heimdall.App.ViewModels;
using Heimdall.Ssh;

namespace Heimdall.App.Views;

/// <summary>
/// WPF host for an interactive SSH shell session. Displays output in a
/// monospace TextBlock with ANSI escape sequence stripping and captures
/// keyboard input via a hidden TextBox for forwarding to the remote shell.
/// This is an intentionally simple implementation; a full xterm.js or
/// Microsoft.Terminal.Control upgrade can replace this view later.
/// </summary>
public partial class EmbeddedSshView : UserControl, IDisposable
{
    private static readonly int MaxOutputLength = 50_000;
    private static readonly int TrimTargetLength = 40_000;

    private static readonly Regex AnsiEscapePattern = new(
        @"\x1b\[[0-9;]*[a-zA-Z]|\x1b\][^\x07]*\x07|\x1b\(B",
        RegexOptions.Compiled);

    private readonly StringBuilder _outputBuffer = new();

    private SshShellSession? _session;
    private Heimdall.Terminal.ITerminalSession? _terminalSession;
    private SessionTabViewModel? _sessionTab;
    private bool _disposed;

    private void WriteToSession(byte[] data)
    {
        if (_session is not null) WriteToSession(data);
        else _terminalSession?.Write(data);
    }

    private void WriteToSession(string text)
    {
        if (_session is not null) WriteToSession(text);
        else _terminalSession?.Write(text);
    }

    private bool IsSessionConnected =>
        (_session?.IsConnected ?? false) || (_terminalSession?.IsRunning ?? false);

    public EmbeddedSshView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        GotFocus += OnGotFocus;
        PreviewMouseDown += OnPreviewMouseDown;
    }

    /// <summary>
    /// Wires the view to a connected SSH shell session. Must be called
    /// exactly once, immediately after construction.
    /// </summary>
    /// <param name="session">A connected <see cref="SshShellSession"/>.</param>
    /// <param name="sessionTab">The tab ViewModel for status updates.</param>
    /// <param name="displayName">Server display name for the header.</param>
    /// <param name="endpoint">Endpoint description (host:port) for the header.</param>
    public void InitializeSession(
        SshShellSession session,
        SessionTabViewModel sessionTab,
        string displayName,
        string endpoint)
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
    }

    /// <summary>
    /// Initialize with a ConPTY/Plink terminal session (ITerminalSession).
    /// Adapts the different event signatures.
    /// </summary>
    public void InitializeTerminalSession(
        Heimdall.Terminal.ITerminalSession terminalSession,
        SessionTabViewModel sessionTab,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(terminalSession);
        ArgumentNullException.ThrowIfNull(sessionTab);

        if (_disposed) throw new ObjectDisposedException(nameof(EmbeddedSshView));

        _terminalSession = terminalSession;
        _sessionTab = sessionTab;

        SessionTitleText.Text = displayName;
        EndpointTextBlock.Text = "via Plink";
        UpdateStatus("Connected");

        _terminalSession.DataReceived += data => OnDataReceived(data.ToArray());
        _terminalSession.ProcessExited += code => OnDisconnected($"Process exited with code {code}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Loaded -= OnLoaded;
        GotFocus -= OnGotFocus;
        PreviewMouseDown -= OnPreviewMouseDown;

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
            try { _terminalSession.Kill(); } catch { }
            try { _terminalSession.Dispose(); } catch { }
            _terminalSession = null;
        }

        Core.Logging.FileLogger.Info("EmbeddedSSH Dispose completed");
    }

    // ------------------------------------------------------------------
    // Event handlers
    // ------------------------------------------------------------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InputCapture.Focus();
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, InputCapture))
        {
            InputCapture.Focus();
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        InputCapture.Focus();
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _session is null)
        {
            return;
        }

        try
        {
            Core.Logging.FileLogger.Info("EmbeddedSSH Disconnect requested by user");
            UpdateStatus("Disconnected");
            _session.Disconnect();
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"EmbeddedSSH manual disconnect failed: {ex.Message}");
            UpdateStatus("Error");
        }
    }

    /// <summary>
    /// Receives raw bytes from the SSH shell read loop (background thread)
    /// and marshals the update to the WPF dispatcher.
    /// </summary>
    private void OnDataReceived(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);

        Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            var cleaned = AnsiEscapePattern.Replace(text, string.Empty);
            _outputBuffer.Append(cleaned);

            if (_outputBuffer.Length > MaxOutputLength)
            {
                _outputBuffer.Remove(0, _outputBuffer.Length - TrimTargetLength);
            }

            TerminalOutput.Text = _outputBuffer.ToString();
            TerminalScroll.ScrollToEnd();
        });
    }

    /// <summary>
    /// Handles unexpected disconnects from the SSH read loop.
    /// </summary>
    private void OnDisconnected(string? errorMessage)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (errorMessage is not null)
            {
                _outputBuffer.Append("\r\n[Session disconnected: ");
                _outputBuffer.Append(errorMessage);
                _outputBuffer.Append("]\r\n");
                TerminalOutput.Text = _outputBuffer.ToString();
                TerminalScroll.ScrollToEnd();
            }

            UpdateStatus("Disconnected");
        });
    }

    // ------------------------------------------------------------------
    // Keyboard input
    // ------------------------------------------------------------------

    private void OnTerminalKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsSessionConnected)
        {
            return;
        }

        // Ctrl+C / Ctrl+D / Ctrl+Z / Ctrl+L
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            byte? controlByte = e.Key switch
            {
                Key.C => 0x03,
                Key.D => 0x04,
                Key.Z => 0x1A,
                Key.L => 0x0C,
                _ => null
            };

            if (controlByte is not null)
            {
                WriteToSession(new byte[] { controlByte.Value });
                e.Handled = true;
                return;
            }
        }

        // Ctrl+V paste
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (Clipboard.ContainsText())
            {
                var pasteText = Clipboard.GetText();
                if (!string.IsNullOrEmpty(pasteText))
                {
                    WriteToSession(pasteText);
                }
            }

            e.Handled = true;
            return;
        }

        // Special keys mapped to VT escape sequences
        byte[]? sequence = e.Key switch
        {
            Key.Enter => [(byte)'\r'],
            Key.Back => [0x7F],
            Key.Tab => [0x09],
            Key.Escape => [0x1B],
            Key.Up => Encoding.UTF8.GetBytes("\x1b[A"),
            Key.Down => Encoding.UTF8.GetBytes("\x1b[B"),
            Key.Right => Encoding.UTF8.GetBytes("\x1b[C"),
            Key.Left => Encoding.UTF8.GetBytes("\x1b[D"),
            Key.Home => Encoding.UTF8.GetBytes("\x1b[H"),
            Key.End => Encoding.UTF8.GetBytes("\x1b[F"),
            Key.Delete => Encoding.UTF8.GetBytes("\x1b[3~"),
            Key.Insert => Encoding.UTF8.GetBytes("\x1b[2~"),
            Key.PageUp => Encoding.UTF8.GetBytes("\x1b[5~"),
            Key.PageDown => Encoding.UTF8.GetBytes("\x1b[6~"),
            Key.F1 => Encoding.UTF8.GetBytes("\x1bOP"),
            Key.F2 => Encoding.UTF8.GetBytes("\x1bOQ"),
            Key.F3 => Encoding.UTF8.GetBytes("\x1bOR"),
            Key.F4 => Encoding.UTF8.GetBytes("\x1bOS"),
            Key.F5 => Encoding.UTF8.GetBytes("\x1b[15~"),
            Key.F6 => Encoding.UTF8.GetBytes("\x1b[17~"),
            Key.F7 => Encoding.UTF8.GetBytes("\x1b[18~"),
            Key.F8 => Encoding.UTF8.GetBytes("\x1b[19~"),
            Key.F9 => Encoding.UTF8.GetBytes("\x1b[20~"),
            Key.F10 => Encoding.UTF8.GetBytes("\x1b[21~"),
            Key.F11 => Encoding.UTF8.GetBytes("\x1b[23~"),
            Key.F12 => Encoding.UTF8.GetBytes("\x1b[24~"),
            _ => null
        };

        if (sequence is not null)
        {
            WriteToSession(sequence);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles printable character input forwarded by the hidden TextBox.
    /// </summary>
    private void OnTerminalTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!IsSessionConnected)
        {
            return;
        }

        if (!string.IsNullOrEmpty(e.Text))
        {
            WriteToSession(e.Text);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void UpdateStatus(string status)
    {
        if (_sessionTab is not null)
        {
            _sessionTab.Status = status;
        }

        StatusTextBlock.Text = status;
        DisconnectButton.IsEnabled = !_disposed
            && !string.Equals(status, "Disconnected", StringComparison.OrdinalIgnoreCase);

        StatusTextBlock.Foreground = string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase)
            ? GetBrush("ErrorBrush", Brushes.IndianRed)
            : GetBrush("TextPrimaryBrush", Brushes.White);
    }

    private Brush GetBrush(string resourceKey, Brush fallback)
    {
        return TryFindResource(resourceKey) as Brush ?? fallback;
    }
}
