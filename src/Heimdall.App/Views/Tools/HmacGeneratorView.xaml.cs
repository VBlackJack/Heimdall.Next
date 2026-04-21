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

using System.ComponentModel;
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

public partial class HmacGeneratorView : UserControl, IToolView
{
    private readonly HmacGeneratorViewModel _vm;
    private LocalizationManager? _localizer;
    private DispatcherTimer? _debounceTimer;
    private bool _syncingKey;
    private bool _disposed;

    public HmacGeneratorView()
    {
        InitializeComponent();
        var service = (Application.Current as App)?.Services?.GetService<IHmacGeneratorService>();
        _vm = new HmacGeneratorViewModel(service);
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        InitializeDebounceTimer();
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        if (_localizer is not null) { _localizer.LocaleChanged -= OnLocaleChanged; }
        _localizer = localizer;
        if (_localizer is not null) { _localizer.LocaleChanged += OnLocaleChanged; }
        _vm.Initialize(localizer);
        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            TxtInput.Text = context.Argument;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
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
        if (_localizer is not null) { _localizer.LocaleChanged -= OnLocaleChanged; _localizer = null; }
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _debounceTimer?.Stop();
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private void InitializeDebounceTimer()
    {
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            _vm.RequestRecompute();
        };
    }

    private void ApplyLocalization()
    {
        TxtHelpContent.Text = L("ToolHelpHMAC").Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HmacGeneratorViewModel.IsKeyVisible))
        {
            return;
        }

        _syncingKey = true;
        try
        {
            if (_vm.IsKeyVisible)
            {
                TxtKey.Text = PwdKey.Password;
                TxtKey.Visibility = Visibility.Visible;
                PwdKey.Visibility = Visibility.Collapsed;
            }
            else
            {
                PwdKey.Password = TxtKey.Text;
                PwdKey.Visibility = Visibility.Visible;
                TxtKey.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            _syncingKey = false;
        }
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingKey) { return; }
        _vm.UpdateInputText(TxtInput.Text);
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingKey) { return; }
        _vm.UpdateKeyText(PwdKey.Password);
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnTxtKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingKey) { return; }
        _vm.UpdateKeyText(TxtKey.Text);
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.OutputText))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_vm.OutputText);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
        catch (ExternalException)
        {
            // Clipboard locked by another process.
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e) =>
        HelpPanel.Visibility = HelpPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void OnCloseHelpClick(object sender, RoutedEventArgs e) => HelpPanel.Visibility = Visibility.Collapsed;

    private void OnLocaleChanged(string _) => ApplyLocalization();

    private string L(string key) => _localizer?[key] ?? key;
}
