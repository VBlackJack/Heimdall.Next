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

public partial class DateTimeConverterView : UserControl, IToolView
{
    private readonly DateTimeConverterViewModel _vm;
    private LocalizationManager? _localizer;
    private DispatcherTimer? _debounceTimer;
    private bool _disposed;
    public DateTimeConverterView()
    {
        InitializeComponent();
        _vm = new DateTimeConverterViewModel((Application.Current as App)?.Services?.GetService<IDateTimeConverterToolService>());
        DataContext = _vm;
        TxtInput.KeyDown += OnInputKeyDown;
        InitializeDebounceTimer();
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        if (_localizer is not null) { _localizer.LocaleChanged -= OnLocaleChanged; }
        _localizer = localizer;
        if (_localizer is not null) { _localizer.LocaleChanged += OnLocaleChanged; }
        _vm.Initialize(localizer);
        _vm.PrefillInput(context?.Argument);
        _vm.MarkInitialized();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { TxtInput.Focus(); TxtInput.SelectAll(); });
    }
    private void InitializeDebounceTimer() { _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) }; _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); _vm.ConvertCurrentInput(); }; }
    private void OnInputKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { _debounceTimer?.Stop(); _vm.ConvertCurrentInput(); e.Handled = true; } }
    private void OnInputTextChanged(object sender, TextChangedEventArgs e) { _debounceTimer?.Stop(); _debounceTimer?.Start(); }
    private void OnCopyUnixClick(object sender, RoutedEventArgs e) => CopyText(_vm.UnixTimestampText, sender as Button);
    private void OnCopyIsoUtcClick(object sender, RoutedEventArgs e) => CopyText(_vm.IsoUtcText, sender as Button);
    private void OnCopyIsoLocalClick(object sender, RoutedEventArgs e) => CopyText(_vm.IsoLocalText, sender as Button);
    private void OnCopyLocalTimeClick(object sender, RoutedEventArgs e) => CopyText(_vm.LocalTimeText, sender as Button);
    private void OnCopyTzTimeClick(object sender, RoutedEventArgs e) => CopyText(_vm.TimezoneTimeText, sender as Button);
    private static void CopyText(string? text, Button? button) { if (string.IsNullOrEmpty(text)) { return; } try { Clipboard.SetText(text); CopyFeedbackHelper.ShowCopyFeedback(button); } catch (ExternalException) { } }
    private void OnHelpClick(object sender, RoutedEventArgs e) { UpdateHelpText(); HelpPanel.Visibility = HelpPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }
    private void OnCloseHelpClick(object sender, RoutedEventArgs e) => HelpPanel.Visibility = Visibility.Collapsed;
    private void OnLocaleChanged(string _) { if (HelpPanel.Visibility == Visibility.Visible) { UpdateHelpText(); } }
    private void UpdateHelpText() => TxtHelpContent.Text = L("ToolHelpDATETIME").Replace("\\n", "\n", StringComparison.Ordinal);
    private string L(string key) => _localizer?[key] ?? key;
    public bool CanClose() => true;

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        TxtInput.KeyDown -= OnInputKeyDown;
        _debounceTimer?.Stop();
        _debounceTimer = null;
        if (_localizer is not null) { _localizer.LocaleChanged -= OnLocaleChanged; }
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }
}
