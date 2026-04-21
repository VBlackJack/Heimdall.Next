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
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Thin WPF shell for the DNS batch resolver tool.
/// </summary>
public partial class DnsBatchResolverView : UserControl, IToolView
{
    private readonly DnsBatchResolverViewModel _vm;
    private readonly ToolAsyncStateController _viewState;
    private Action<bool>? _setBusy;
    private LocalizationManager? _localizer;
    private bool _disposed;

    public DnsBatchResolverView()
    {
        _vm = new DnsBatchResolverViewModel();
        InitializeComponent();
        DataContext = _vm;

        _viewState = new ToolAsyncStateController(
            isBusy => _setBusy?.Invoke(isBusy),
            LoadingBar,
            TxtError,
            EmptyStatePanel,
            ResultsPanel,
            TxtStatus,
            BtnResolve,
            TxtHostnames);

        _vm.PropertyChanged += OnVmPropertyChanged;
        RefreshUiFromVm();
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

        _setBusy = context?.SetBusyAction;
        _vm.Initialize(localizer);
        _vm.HostnamesInput = context?.TargetHost ?? string.Empty;

        ApplyLocalization();
        RefreshUiFromVm();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHostnames.Focus();
            TxtHostnames.SelectAll();
        });
    }

    public bool CanClose() => !_vm.IsBusy;

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
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ApplyLocalization()
    {
        ApplyBusyButtonState();
        AutomationProperties.SetName(BtnResolve, _vm.IsBusy ? L("ToolDnsBatchBtnStop") : L("ToolDnsBatchBtnResolve"));
        AutomationProperties.SetName(LoadingBar, L("ToolDnsBatchTitle"));
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DnsBatchResolverViewModel.IsBusy):
            case nameof(DnsBatchResolverViewModel.ShowError):
            case nameof(DnsBatchResolverViewModel.ErrorText):
            case nameof(DnsBatchResolverViewModel.HasResults):
            case nameof(DnsBatchResolverViewModel.StatusText):
                RefreshUiFromVm();
                break;
        }
    }

    private void RefreshUiFromVm()
    {
        ApplyBusyButtonState();

        if (_vm.IsBusy)
        {
            _viewState.Begin(_vm.StatusText);
            return;
        }

        _viewState.End();

        if (_vm.ShowError)
        {
            _viewState.ShowError(_vm.ErrorText, _vm.StatusText, showEmptyState: !_vm.HasResults, keepResultsVisible: _vm.HasResults);
            return;
        }

        if (_vm.HasResults)
        {
            _viewState.ShowResults(_vm.StatusText);
            return;
        }

        _viewState.Reset();
    }

    private void ApplyBusyButtonState()
    {
        if (_vm.IsBusy)
        {
            BtnResolve.Content = L("ToolDnsBatchBtnStop");
            BtnResolve.Style = (Style)FindResource("SecondaryButtonStyle");
            BtnResolve.Command = _vm.CancelCommand;
            AutomationProperties.SetName(BtnResolve, L("ToolDnsBatchBtnStop"));
            return;
        }

        BtnResolve.Content = L("ToolDnsBatchBtnResolve");
        BtnResolve.Style = (Style)FindResource("PrimaryButtonStyle");
        BtnResolve.Command = _vm.ResolveCommand;
        AutomationProperties.SetName(BtnResolve, L("ToolDnsBatchBtnResolve"));
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasResults)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{ColHostname.Header}\t{ColIpv4.Header}\t{ColIpv6.Header}\t{ColTime.Header}\t{ColStatus.Header}");
            foreach (var row in _vm.Results)
            {
                sb.AppendLine($"{row.Hostname}\t{row.Ipv4}\t{row.Ipv6}\t{row.ResolveTimeMs}\t{row.Status}");
            }

            Clipboard.SetText(sb.ToString());
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
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
