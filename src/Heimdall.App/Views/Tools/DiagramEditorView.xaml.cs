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
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Microsoft.Web.WebView2.Core;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Embedded draw.io diagram editor hosted via WebView2 in embed mode.
/// Supports creating, opening, saving, and exporting .drawio files.
/// </summary>
public partial class DiagramEditorView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private bool _disposed;
    private string? _currentFilePath;
    private string? _lastSavedXml;
    private const string VirtualHost = "heimdall-drawio.local";

    public DiagramEditorView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        // If context has an argument (file path), load it
        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            _currentFilePath = context.Argument;
        }

        _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            if (!Services.WebView2Helper.IsAvailable)
            {
                ShowFallback(L("ErrorWebView2NotFound"));
                return;
            }

            var env = await Services.WebView2Helper.CreateEnvironmentAsync("DrawIO");
            await DiagramWebView.EnsureCoreWebView2Async(env);

            var core = DiagramWebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;

            // Virtual host mapping for local draw.io files
            var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "drawio");
            core.SetVirtualHostNameToFolderMapping(
                VirtualHost, assetsPath,
                CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += OnWebMessageReceived;

            // Block navigation away from draw.io
            core.NavigationStarting += (_, navArgs) =>
            {
                if (navArgs.Uri is not null
                    && !navArgs.Uri.StartsWith($"https://{VirtualHost}", StringComparison.OrdinalIgnoreCase)
                    && !navArgs.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                    && !navArgs.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    navArgs.Cancel = true;
                }
            };

            // Load the host page
            core.Navigate($"https://{VirtualHost}/heimdall-host.html");
        }
        catch (Exception ex)
        {
            ShowFallback(ex.Message);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(message)) return;

        if (message == "ready:")
        {
            // If we have a file to load, send it
            if (_currentFilePath is not null && File.Exists(_currentFilePath))
            {
                var xml = File.ReadAllText(_currentFilePath);
                PostWebMessage($"load:{xml}");
            }
            return;
        }

        if (message.StartsWith("save:", StringComparison.Ordinal))
        {
            _lastSavedXml = message["save:".Length..];
            return;
        }

        if (message.StartsWith("export:", StringComparison.Ordinal))
        {
            var data = message["export:".Length..];
            HandleExportData(data);
            return;
        }

        if (message == "exit:")
        {
            // User clicked exit in draw.io — could close the tab
            return;
        }
    }

    private void HandleExportData(string data)
    {
        // PNG exports are base64 data URIs, SVG exports are XML
        var dialog = new Microsoft.Win32.SaveFileDialog();

        if (data.StartsWith("data:image/png", StringComparison.OrdinalIgnoreCase))
        {
            dialog.Filter = "PNG (*.png)|*.png";
            dialog.FileName = "diagram.png";

            if (dialog.ShowDialog() != true) return;

            var base64Start = data.IndexOf(",", StringComparison.Ordinal);
            if (base64Start < 0) return;

            var base64Data = data[(base64Start + 1)..];
            var bytes = Convert.FromBase64String(base64Data);
            File.WriteAllBytes(dialog.FileName, bytes);
        }
        else
        {
            dialog.Filter = "SVG (*.svg)|*.svg";
            dialog.FileName = "diagram.svg";

            if (dialog.ShowDialog() != true) return;

            File.WriteAllText(dialog.FileName, data, Encoding.UTF8);
        }
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        _currentFilePath = null;
        _lastSavedXml = null;
        PostWebMessage("load:");
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Draw.io (*.drawio)|*.drawio|XML (*.xml)|*.xml|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            _currentFilePath = dialog.FileName;
            var xml = File.ReadAllText(dialog.FileName);
            PostWebMessage($"load:{xml}");
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastSavedXml)) return;

        if (_currentFilePath is null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Draw.io (*.drawio)|*.drawio",
                FileName = "diagram.drawio"
            };
            if (dialog.ShowDialog() != true) return;
            _currentFilePath = dialog.FileName;
        }

        File.WriteAllText(_currentFilePath, _lastSavedXml, Encoding.UTF8);
    }

    private void OnExportPngClick(object sender, RoutedEventArgs e)
    {
        PostWebMessage("export-png:");
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpDIAGRAM");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PostWebMessage(string message)
    {
        if (DiagramWebView.CoreWebView2 is not null)
        {
            DiagramWebView.CoreWebView2.PostWebMessageAsString(message);
        }
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolDiagramTitle");
        BtnNew.Content = L("ToolDiagramBtnNew");
        BtnOpen.Content = L("ToolDiagramBtnOpen");
        BtnSave.Content = L("ToolDiagramBtnSave");
        BtnExportPng.Content = L("ToolDiagramBtnExportPng");

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnNew, L("ToolDiagramBtnNew"));
        System.Windows.Automation.AutomationProperties.SetName(BtnOpen, L("ToolDiagramBtnOpen"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSave, L("ToolDiagramBtnSave"));
        System.Windows.Automation.AutomationProperties.SetName(BtnExportPng, L("ToolDiagramBtnExportPng"));
    }

    private void ShowFallback(string message)
    {
        DiagramWebView.Visibility = Visibility.Collapsed;
        FallbackPanel.Visibility = Visibility.Visible;
        FallbackTitle.Text = L("ToolDiagramTitle");
        FallbackMessage.Text = message;
    }

    private string L(string key) => _localizer?[key] ?? key;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (DiagramWebView.CoreWebView2 is not null)
            {
                DiagramWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            }
            DiagramWebView.Dispose();
        }
        catch
        {
            // Disposal may fail if WebView2 was never initialized
        }

        GC.SuppressFinalize(this);
    }
}
