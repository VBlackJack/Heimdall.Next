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
using System.Windows.Threading;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.App.Views.Tools;

public partial class UrlEncoderView : UserControl, IToolView
{
    private readonly UrlEncoderViewModel _vm;
    private LocalizationManager? _localizer;
    private bool _disposed;

    public UrlEncoderView()
    {
        InitializeComponent();
        _vm = new UrlEncoderViewModel((Application.Current as App)?.Services?.GetService<IUrlEncoderToolService>());
        DataContext = _vm;
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        if (_localizer is not null) { _localizer.LocaleChanged -= OnLocaleChanged; }
        _localizer = localizer;
        if (_localizer is not null) { _localizer.LocaleChanged += OnLocaleChanged; }

        _vm.Initialize(localizer);
        _vm.Reset();
        _vm.PrefillDecoded(context?.Argument);

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            TxtDecoded.Focus();
            if (!string.IsNullOrEmpty(TxtDecoded.Text)) { TxtDecoded.SelectAll(); }
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

    private void OnCopyDecodedClick(object sender, RoutedEventArgs e) => CopyText(_vm.DecodedText, sender as Button);

    private void OnCopyEncodedClick(object sender, RoutedEventArgs e) => CopyText(_vm.EncodedText, sender as Button);

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

    private void CopyText(string text, Button? button)
    {
        if (string.IsNullOrEmpty(text)) { return; }
        try
        {
            Clipboard.SetText(text);
            CopyFeedbackHelper.ShowCopyFeedback(button);
        }
        catch (ExternalException) { }
    }

    private void UpdateHelpText() => TxtHelpContent.Text = L("ToolHelpURLENC").Replace("\\n", "\n", StringComparison.Ordinal);

    private string L(string key) => _localizer?[key] ?? key;
}
