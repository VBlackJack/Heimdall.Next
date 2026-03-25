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

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Heimdall.App.Services;
using Microsoft.Web.WebView2.Core;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// WebView2 wrapper hosting the Milkdown Markdown editor.
/// Provides a C# API for content sync, theme switching, and link interception.
/// Falls back gracefully if WebView2 is unavailable.
/// </summary>
public partial class MilkdownEditorControl : UserControl, IDisposable
{
    private static readonly string EditorHtmlPath = Path.Combine(
        AppContext.BaseDirectory, "Assets", "milkdown", "index.html");

    private bool _initialized;
    private bool _editorReady;
    private bool _disposed;
    private string? _pendingContent;
    private bool? _pendingReadOnly;

    /// <summary>Raised when the editor content changes (debounced by JS side).</summary>
    public event Action<string, bool>? ContentChanged;

    /// <summary>Raised when a [[wiki-link]] is clicked.</summary>
    public event Action<string>? LinkClicked;

    /// <summary>Raised when the editor is ready to receive commands.</summary>
    public event Action? EditorReady;

    /// <summary>Returns true if the WebView2 host was created (asset found and runtime present).</summary>
    public bool IsHostInitialized => _initialized;

    /// <summary>Returns true if the Milkdown editor initialized successfully.</summary>
    public bool IsReady => _editorReady;

    /// <summary>Returns true if the Milkdown HTML asset exists. WebView2 availability
    /// is verified at runtime during <see cref="InitializeAsync"/>.</summary>
    public static bool IsAvailable => File.Exists(EditorHtmlPath);

    public MilkdownEditorControl()
    {
        InitializeComponent();
    }

    public async Task InitializeAsync()
    {
        if (_initialized || _disposed)
        {
            return;
        }

        await WaitUntilLoadedAsync().ConfigureAwait(true);

        if (_initialized || _disposed)
        {
            return;
        }

        if (!IsAvailable)
        {
            Core.Logging.FileLogger.Warn(
                $"Milkdown editor asset not found at '{EditorHtmlPath}'");
            LoadingPanel.Visibility = Visibility.Visible;
            return;
        }

        if (!WebView2Helper.IsAvailable)
        {
            Core.Logging.FileLogger.Warn("Milkdown editor unavailable: WebView2 runtime not found");
            LoadingPanel.Visibility = Visibility.Visible;
            return;
        }

        _initialized = true;

        try
        {
            Core.Logging.FileLogger.Info(
                $"Milkdown editor initializing from '{EditorHtmlPath}'");
            var env = await WebView2Helper.CreateEnvironmentAsync("MilkdownEditor")
                .ConfigureAwait(true);

            await EditorWebView.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            Core.Logging.FileLogger.Info("Milkdown WebView2 host initialized");

            var core = EditorWebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;

            // Block external navigation
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationStarting += OnNavigationStarting;
            core.WebMessageReceived -= OnWebMessageReceived;
            core.WebMessageReceived += OnWebMessageReceived;

            // Map the milkdown assets folder as a virtual host
            var assetsFolder = Path.GetDirectoryName(EditorHtmlPath)!;
            core.SetVirtualHostNameToFolderMapping(
                "appassets.local", assetsFolder, CoreWebView2HostResourceAccessKind.Allow);

            Core.Logging.FileLogger.Info(
                $"Milkdown virtual host mapped to '{assetsFolder}'");
            core.Navigate("https://appassets.local/index.html");
            Core.Logging.FileLogger.Info("Milkdown navigation started");
        }
        catch (Exception ex)
        {
            _initialized = false;
            Core.Logging.FileLogger.Warn($"Milkdown editor init failed: {ex.Message}");
            LoadingPanel.Visibility = Visibility.Visible;
        }
    }

    public void SetContent(string markdown)
    {
        if (!_editorReady)
        {
            _pendingContent = markdown;
            return;
        }

        PostMessage("set-content", markdown);
    }

    public void SetTheme(string theme)
    {
        if (_editorReady)
        {
            PostMessage("set-theme", theme);
        }
    }

    public void SetReadOnly(bool readOnly)
    {
        if (!_editorReady)
        {
            _pendingReadOnly = readOnly;
            return;
        }

        _pendingReadOnly = null;
        PostMessage("set-readonly", readOnly);
    }

    public void FocusEditor()
    {
        if (_editorReady)
        {
            PostMessage("focus");
        }
    }

    public void InsertText(string text)
    {
        if (_editorReady)
        {
            PostMessage("insert", text);
        }
    }

    public void SetContextMenuLabels(Dictionary<string, string> labels)
    {
        if (_editorReady)
        {
            PostMessage("set-menu-labels", labels);
        }
    }

    private void PostMessage(string type, object? payload = null)
    {
        if (_disposed || EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var msg = payload is not null
            ? JsonSerializer.Serialize(new { type, payload })
            : JsonSerializer.Serialize(new { type });

        EditorWebView.CoreWebView2.PostWebMessageAsJson(msg);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (args.Uri is not null
            && !args.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            && !args.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            && !args.Uri.Contains("appassets.local", StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
            {
                return;
            }

            var type = typeProp.GetString();
            switch (type)
            {
                case "ready":
                    _editorReady = true;
                    LoadingPanel.Visibility = Visibility.Collapsed;

                    // Apply the current Heimdall theme
                    var isDark = Application.Current?.TryFindResource("BackgroundBrush") is
                        System.Windows.Media.SolidColorBrush bg && bg.Color.R < 128;
                    SetTheme(isDark ? "dark" : "light");

                    if (_pendingReadOnly is { } readOnly)
                    {
                        SetReadOnly(readOnly);
                        _pendingReadOnly = null;
                    }

                    if (_pendingContent is not null)
                    {
                        SetContent(_pendingContent);
                        _pendingContent = null;
                    }

                    EditorReady?.Invoke();
                    break;

                case "change":
                    if (root.TryGetProperty("payload", out var payload))
                    {
                        var markdown = payload.TryGetProperty("markdown", out var md)
                            ? md.GetString() ?? ""
                            : "";
                        var dirty = payload.TryGetProperty("dirty", out var d) && d.GetBoolean();
                        ContentChanged?.Invoke(markdown, dirty);
                    }
                    break;

                case "open-link":
                    if (root.TryGetProperty("payload", out var linkPayload))
                    {
                        var noteRef = linkPayload.GetString();
                        if (!string.IsNullOrWhiteSpace(noteRef))
                        {
                            LinkClicked?.Invoke(noteRef);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Milkdown message parse error: {ex.Message}");
        }
    }

    private Task WaitUntilLoadedAsync()
    {
        if (IsLoaded)
        {
            return Task.CompletedTask;
        }

        // A Collapsed control never receives the Loaded event.
        // When the caller has already set us to Hidden/Visible,
        // yield once to let the layout pass complete, then proceed.
        return Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded).Task;
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
            EditorWebView.Dispose();
        }
        catch
        {
            // WebView2 may already be disposed
        }

        GC.SuppressFinalize(this);
    }
}
