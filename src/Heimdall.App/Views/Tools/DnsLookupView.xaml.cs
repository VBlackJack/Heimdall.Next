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

using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// WPF shell for the DNS record lookup tool. Business logic lives in the
/// service, the Core engine (<see cref="Heimdall.Core.Network.DnsRecordType"/>,
/// <see cref="Heimdall.Core.Network.NslookupOutputParser"/>), and the
/// <see cref="DnsLookupViewModel"/>.
/// </summary>
public partial class DnsLookupView : UserControl, IToolView
{
    private readonly DnsLookupViewModel _vm;
    private LocalizationManager? _localizer;
    private bool _disposed;
    private Action<bool>? _setBusy;
    private readonly ToolAsyncStateController _viewState;
    private List<SshGatewayDto>? _gateways;
    private SshGatewayDto? _selectedGateway;

    public DnsLookupView()
    {
        _vm = new DnsLookupViewModel();
        InitializeComponent();
        DataContext = _vm;

        _viewState = new ToolAsyncStateController(
            isBusy => _setBusy?.Invoke(isBusy),
            LoadingBar,
            TxtError,
            EmptyStatePanel,
            ResultsPanel,
            TxtStatus,
            BtnLookup,
            TxtHostname,
            CmbRecordType,
            CmbDnsServer,
            CmbRouteVia);

        _vm.PropertyChanged += OnVmPropertyChanged;
        RefreshUiFromVm();
    }

    /// <inheritdoc />
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
        ApplyLocalization();

        _vm.Hostname = string.Empty;
        if (!string.IsNullOrWhiteSpace(context?.TargetHost))
        {
            _vm.Hostname = context.TargetHost!;
        }

        if (context?.SshGateways is IList gateways)
        {
            _gateways = gateways.Cast<SshGatewayDto>().ToList();
        }

        PopulateRouteSelector();
        _vm.SetGateway(_selectedGateway);
        RefreshUiFromVm();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtHostname.Focus();
            TxtHostname.SelectAll();
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
        PopulateRouteSelector();
        ApplyBusyButtonState();
        AutomationProperties.SetName(CmbRouteVia, L("ToolTunnelRouteVia"));
        AutomationProperties.SetName(LoadingBar, L("ToolDnsA11yLoading"));
    }

    private void PopulateRouteSelector()
    {
        var selectedGateway = _selectedGateway;

        CmbRouteVia.Items.Clear();
        CmbRouteVia.Items.Add(new ComboBoxItem { Content = L("ToolTunnelDirect") });

        var selectedIndex = 0;
        if (_gateways is not null)
        {
            for (var i = 0; i < _gateways.Count; i++)
            {
                var gateway = _gateways[i];
                var label = $"{gateway.Name} ({gateway.Host}:{gateway.Port})";
                CmbRouteVia.Items.Add(new ComboBoxItem { Content = label, Tag = gateway });
                if (ReferenceEquals(selectedGateway, gateway))
                {
                    selectedIndex = i + 1;
                }
            }
        }

        CmbRouteVia.SelectedIndex = selectedIndex;
    }

    private void OnRouteViaChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbRouteVia.SelectedItem is ComboBoxItem item && item.Tag is SshGatewayDto gateway)
        {
            _selectedGateway = gateway;
        }
        else
        {
            _selectedGateway = null;
        }

        _vm.SetGateway(_selectedGateway);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DnsLookupViewModel.IsBusy):
            case nameof(DnsLookupViewModel.ShowError):
            case nameof(DnsLookupViewModel.ErrorText):
            case nameof(DnsLookupViewModel.HasResults):
            case nameof(DnsLookupViewModel.StatusText):
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
            _viewState.ShowError(
                _vm.ErrorText,
                _vm.StatusText,
                showEmptyState: !_vm.HasResults,
                keepResultsVisible: _vm.HasResults);
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
            BtnLookup.Content = L("BtnCancel");
            BtnLookup.Style = (Style)FindResource("SecondaryButtonStyle");
            BtnLookup.Command = _vm.CancelCommand;
            AutomationProperties.SetName(BtnLookup, L("BtnCancel"));
            return;
        }

        BtnLookup.Content = L("ToolDnsBtnLookup");
        BtnLookup.Style = (Style)FindResource("PrimaryButtonStyle");
        BtnLookup.Command = _vm.LookupCommand;
        AutomationProperties.SetName(BtnLookup, L("ToolDnsBtnLookup"));
    }

    private void OnCopyResultsClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.CopyResultsCommand.CanExecute(null))
        {
            return;
        }

        _vm.CopyResultsCommand.Execute(null);
        CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
    }

    private void OnLocaleChanged(string _)
    {
        ApplyLocalization();
    }

    private string L(string key) => _localizer?[key] ?? key;
}
