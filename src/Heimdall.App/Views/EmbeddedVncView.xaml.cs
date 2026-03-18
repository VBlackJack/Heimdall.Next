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

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Localization;
using Microsoft.Web.WebView2.Core;

namespace Heimdall.App.Views;

/// <summary>
/// WPF host for an embedded VNC session rendered through WebView2 + noVNC.
/// A local WebSocket proxy bridges the noVNC client to the VNC server's TCP socket.
/// </summary>
public partial class EmbeddedVncView : UserControl, IDisposable
{
    private VncSessionResult? _session;
    private SessionTabViewModel? _sessionTab;
    private WebSocketVncProxy? _proxy;
    private LocalizationManager? _localizer;
    private bool _webViewReady;
    private bool _disposed;

    public EmbeddedVncView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the VNC view with session parameters and starts the WebSocket proxy.
    /// </summary>
    public async void InitializeSession(
        VncSessionResult session,
        SessionTabViewModel sessionTab,
        string displayName,
        LocalizationManager? localizer)
    {
        _session = session;
        _sessionTab = sessionTab;
        _localizer = localizer;

        SessionTitleText.Text = displayName;
        EndpointTextBlock.Text = $"{session.Host}:{session.Port}";
        StatusTextBlock.Text = localizer?["StatusVncConnecting"] ?? "Connecting...";

        // Localize static UI elements
        DisconnectButton.Content = localizer?["BtnDisconnectSession"] ?? "Disconnect";
        FallbackTitleText.Text = localizer?["VncFallbackTitle"] ?? "Embedded VNC viewer unavailable";
        FallbackMessageText.Text = localizer?["VncFallbackMessage"] ?? "WebView2 could not be initialized on this machine.";

        // Start the WebSocket-to-TCP proxy
        _proxy = new WebSocketVncProxy(session.Host, session.Port);
        _proxy.Start();

        await InitializeWebViewAsync().ConfigureAwait(false);
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(
                    Path.GetTempPath(), "Heimdall", "WebView2", "VNC"))
                .ConfigureAwait(true);

            await VncWebView.EnsureCoreWebView2Async(env).ConfigureAwait(true);

            VncWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            VncWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            VncWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            VncWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            VncWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // Load the VNC HTML page
            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "vnc.html");
            if (File.Exists(htmlPath))
            {
                VncWebView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            }
            else
            {
                ShowFallback("VNC viewer HTML not found.");
            }
        }
        catch (Exception ex)
        {
            ShowFallback(ex.Message);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (message == "ready:")
        {
            _webViewReady = true;
            SendConnectCommand();
            return;
        }

        if (message == "connected:")
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = _localizer?["StatusVncConnected"] ?? "Connected";
            });
            return;
        }

        if (message.StartsWith("disconnected:", StringComparison.Ordinal))
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = _localizer?["BtnDisconnectSession"] ?? "Disconnected";
            });
            return;
        }

        if (message.StartsWith("error:", StringComparison.Ordinal))
        {
            var errorMsg = message["error:".Length..];
            Core.Logging.FileLogger.Error($"VNC error: {errorMsg}");
            return;
        }

        if (message.StartsWith("desktop-name:", StringComparison.Ordinal))
        {
            var desktopName = message["desktop-name:".Length..];
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(desktopName))
                {
                    SessionTitleText.Text = desktopName;
                }
            });
            return;
        }

        if (message.StartsWith("clipboard-write:", StringComparison.Ordinal))
        {
            var b64 = message["clipboard-write:".Length..];
            try
            {
                var text = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(b64));
                Dispatcher.Invoke(() => Clipboard.SetText(text));
            }
            catch (FormatException)
            {
                // Invalid base64; ignore.
            }
            return;
        }

        if (message == "clipboard-read:")
        {
            Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    var b64 = Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes(text));
                    PostWebMessage($"clipboard-paste:{b64}");
                }
            });
            return;
        }

        if (message == "credentials-required:")
        {
            // If we have a password, re-send the connect command with it
            if (_session?.Password is not null)
            {
                SendConnectCommand();
            }
        }
    }

    private void SendConnectCommand()
    {
        if (_proxy is null || !_webViewReady)
        {
            return;
        }

        var connectParams = new
        {
            wsUrl = $"ws://127.0.0.1:{_proxy.ListenPort}",
            password = _session?.Password
        };

        var json = JsonSerializer.Serialize(connectParams);
        PostWebMessage($"connect:{json}");
    }

    private void PostWebMessage(string message)
    {
        if (VncWebView.CoreWebView2 is not null)
        {
            VncWebView.CoreWebView2.PostWebMessageAsString(message);
        }
    }

    private void ShowFallback(string message)
    {
        Dispatcher.Invoke(() =>
        {
            FallbackMessageText.Text = message;
            FallbackPanel.Visibility = Visibility.Visible;
            VncWebView.Visibility = Visibility.Collapsed;
        });
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        PostWebMessage("disconnect:");
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (VncWebView.CoreWebView2 is not null)
        {
            VncWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        _proxy?.Dispose();
        _proxy = null;

        try
        {
            VncWebView.Dispose();
        }
        catch (InvalidOperationException)
        {
            // WebView2 may already be disposed.
        }
    }
}
