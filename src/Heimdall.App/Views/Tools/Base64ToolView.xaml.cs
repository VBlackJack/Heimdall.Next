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

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Heimdall.App.Views.Tools;

public partial class Base64ToolView : UserControl, IToolView
{
    private readonly Base64ToolViewModel _vm;
    private LocalizationManager? _localizer;
    private bool _disposed;

    public Base64ToolView()
    {
        InitializeComponent();
        _vm = new Base64ToolViewModel((Application.Current as App)?.Services?.GetService<IBase64ToolService>());
        DataContext = _vm;
    }

    public async void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        if (_localizer is not null) { _localizer.LocaleChanged -= OnLocaleChanged; }
        _localizer = localizer;
        if (_localizer is not null) { _localizer.LocaleChanged += OnLocaleChanged; }

        _vm.Initialize(localizer);
        if (!string.IsNullOrWhiteSpace(context?.Argument)) { await _vm.PrefillInput(context.Argument); }
        else { _vm.MarkInitialized(); }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            InputText.Focus();
            if (!string.IsNullOrEmpty(InputText.Text)) { InputText.SelectAll(); }
        });
    }

    public bool CanClose() => true;

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        if (_localizer is not null) { _localizer.LocaleChanged -= OnLocaleChanged; }
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void OnDecodeClick(object sender, RoutedEventArgs e)
    {
        await _vm.DecodeCommand.ExecuteAsync(null);
        if (!_vm.IsFileMode || _vm.TryGetLastDecodedBytes() is not { } decodedBytes) { return; }

        var dialog = new SaveFileDialog { Title = L("ToolBase64SaveFileTitle"), Filter = L("ToolBase64SaveFileFilter") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) { await _vm.SaveFileAsync(dialog.FileName, CancellationToken.None); }
    }

    private async void OnBrowseFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = L("ToolBase64OpenFileTitle"), Filter = L("ToolBase64OpenFileFilter") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) { await _vm.LoadFileAsync(dialog.FileName, CancellationToken.None); }
    }

    private void OnCopyOutputClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.OutputText)) { return; }
        try
        {
            Clipboard.SetText(_vm.OutputText);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (ExternalException) { }
    }

    private async void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        {
            await _vm.EncodeCommand.ExecuteAsync(null);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Enter)
        {
            OnDecodeClick(sender, e);
            e.Handled = true;
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e) => _vm.OnInputTextChangedFromView();

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        UpdateHelpText();
        HelpPanel.Visibility = HelpPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e) => HelpPanel.Visibility = Visibility.Collapsed;

    private void OnLocaleChanged(string _)
    {
        if (HelpPanel.Visibility == Visibility.Visible) { UpdateHelpText(); }
    }

    private void UpdateHelpText() => TxtHelpContent.Text = L("ToolHelpBASE64").Replace("\\n", "\n", StringComparison.Ordinal);

    private string L(string key) => _localizer?[key] ?? key;
}
