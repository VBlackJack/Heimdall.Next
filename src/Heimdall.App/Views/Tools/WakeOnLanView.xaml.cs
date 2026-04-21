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
using System.Windows.Controls;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Network;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Thin WPF shell for the Wake-on-LAN tool. The business logic lives in
/// <see cref="WakeOnLanViewModel"/> and <see cref="WakeOnLanService"/>.
/// </summary>
public partial class WakeOnLanView : UserControl, IToolView
{
    private readonly WakeOnLanViewModel _vm;
    private Action<bool>? _setBusy;
    private bool _disposed;

    public WakeOnLanView()
    {
        _vm = new WakeOnLanViewModel();
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _setBusy = context?.SetBusyAction;
        _vm.Initialize(localizer);
        _vm.MacAddress = string.Empty;
        _vm.BroadcastAddress = WakeOnLanViewModel.DefaultBroadcastAddress;
        _vm.Port = WakeOnLanViewModel.DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (MacAddressParser.TryNormalize(context?.Argument, out var argumentMac))
        {
            _vm.MacAddress = argumentMac;
        }
        else if (MacAddressParser.TryNormalize(context?.TargetHost, out var targetMac))
        {
            _vm.MacAddress = targetMac;
        }

        RefreshUiFromVm();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtMac.Focus();
            TxtMac.SelectAll();
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
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WakeOnLanViewModel.IsBusy))
        {
            RefreshUiFromVm();
        }
    }

    private void RefreshUiFromVm()
    {
        _setBusy?.Invoke(_vm.IsBusy);
    }
}
