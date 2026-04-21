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

public partial class IpConverterView : UserControl, IToolView
{
    private readonly IpConverterViewModel _vm;
    private LocalizationManager? _localizer;
    private bool _disposed;

    public IpConverterView()
    {
        InitializeComponent();
        _vm = new IpConverterViewModel((Application.Current as App)?.Services?.GetService<IIpConverterToolService>());
        DataContext = _vm;
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        if (_localizer is not null) { _localizer.LocaleChanged -= OnLocaleChanged; }
        _localizer = localizer;
        if (_localizer is not null) { _localizer.LocaleChanged += OnLocaleChanged; }

        _vm.Initialize(localizer);
        _vm.Reset();
        _vm.PrefillInput(context?.TargetHost);

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            TxtInput.Focus();
            TxtInput.SelectAll();
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

    private void OnCopyValueClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) { return; }

        var text = btn.Tag?.ToString() switch
        {
            "Dotted" => _vm.DottedText,
            "Decimal" => _vm.DecimalText,
            "Hex" => _vm.HexText,
            "Binary" => _vm.BinaryText,
            "Ipv6" => _vm.MappedIpv6Text,
            _ => null,
        };

        if (string.IsNullOrEmpty(text)) { return; }
        try
        {
            Clipboard.SetText(text);
            CopyFeedbackHelper.ShowCopyFeedback(btn);
        }
        catch (ExternalException) { }
    }

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

    private void UpdateHelpText() => TxtHelpContent.Text = L("ToolHelpIPCONV").Replace("\\n", "\n", StringComparison.Ordinal);

    private string L(string key) => _localizer?[key] ?? key;
}
