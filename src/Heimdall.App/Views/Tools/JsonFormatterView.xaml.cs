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

namespace Heimdall.App.Views.Tools;

public partial class JsonFormatterView : UserControl, IToolView
{
    private readonly JsonFormatterViewModel _vm;
    private LocalizationManager? _localizer;
    private bool _disposed;
    public JsonFormatterView()
    {
        InitializeComponent();
        _vm = new JsonFormatterViewModel((Application.Current as App)?.Services?.GetService<IJsonFormatterToolService>());
        DataContext = _vm;
    }
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        if (_localizer is not null) { _localizer.LocaleChanged -= OnLocaleChanged; }
        _localizer = localizer;
        if (_localizer is not null) { _localizer.LocaleChanged += OnLocaleChanged; }
        _vm.Initialize(localizer);
        _vm.PrefillInput(context?.Argument);
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            InputText.Focus();
            InputText.SelectAll();
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
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter && _vm.PrettifyCommand.CanExecute(null))
        {
            _vm.PrettifyCommand.Execute(null);
            e.Handled = true;
        }
    }
    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        UpdateHelpText();
        HelpPanel.Visibility = HelpPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnCloseHelpClick(object sender, RoutedEventArgs e) => HelpPanel.Visibility = Visibility.Collapsed;
    private void OnLocaleChanged(string _) { if (HelpPanel.Visibility == Visibility.Visible) { UpdateHelpText(); } }
    private void UpdateHelpText() => TxtHelpContent.Text = L("ToolHelpJSON").Replace("\\n", "\n", StringComparison.Ordinal);
    private string L(string key) => _localizer?[key] ?? key;
}
