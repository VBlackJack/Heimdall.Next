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
using System.Windows.Automation;
using System.Windows.Controls;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Thin WPF shell for the network interfaces tool.
/// </summary>
public partial class NetworkInterfacesView : UserControl, IToolView
{
    private readonly NetworkInterfacesViewModel _vm;
    private LocalizationManager? _localizer;
    private bool _disposed;

    public NetworkInterfacesView()
    {
        _vm = new NetworkInterfacesViewModel();
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.CopyResultsRequested += OnCopyResultsRequested;
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
        }

        _localizer = localizer;
        if (_localizer is not null)
        {
            _localizer.LocaleChanged += OnLocaleChanged;
        }

        _vm.Initialize(localizer);
        ApplyLocalization();
        _vm.RefreshCommand.Execute(null);
    }

    public bool CanClose() => true;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
            _localizer = null;
        }

        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.CopyResultsRequested -= OnCopyResultsRequested;
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ApplyLocalization()
    {
        AutomationProperties.SetName(BtnRefresh, L("ToolNetIfBtnRefresh"));
        AutomationProperties.SetName(BtnCopy, L("ToolBtnCopyToClipboard"));
        AutomationProperties.SetName(InterfacesGrid, L("ToolNetIfTitle"));
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkInterfacesViewModel.StatusText))
        {
            TxtStatus.Text = _vm.StatusText;
        }
    }

    private void OnCopyResultsRequested(object? sender, string text)
    {
        try
        {
            Clipboard.SetText(text);
            CopyFeedbackHelper.ShowCopyFeedback(BtnCopy);
        }
        catch (ExternalException)
        {
            // Clipboard locked by another process.
        }
    }

    private void OnLocaleChanged(string _)
    {
        ApplyLocalization();
    }

    private string L(string key) => _localizer?[key] ?? key;
}
