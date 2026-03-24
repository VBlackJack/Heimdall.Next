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
using System.Windows;
using System.Windows.Controls;
using Heimdall.Core.Models;

namespace Heimdall.App.Views;

/// <summary>
/// Leaf pane view in the recursive split tree. Displays a single session's host
/// control with loading and disconnected overlays. Overlay buttons route to
/// MainViewModel via visual tree traversal.
/// </summary>
public partial class SessionPaneControl : UserControl
{
    private SessionPaneModel? _model;

    public SessionPaneControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ReconnectButton.Click += OnReconnectClick;
        ClosePaneButton.Click += OnClosePaneClick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncContent();
        UpdateOverlays();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
        }

        ReconnectButton.Click -= OnReconnectClick;
        ClosePaneButton.Click -= OnClosePaneClick;
        DataContextChanged -= OnDataContextChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _model = null;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from previous model
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
        }

        _model = e.NewValue as SessionPaneModel;

        if (_model is not null)
        {
            _model.PropertyChanged += OnModelPropertyChanged;
        }

        SyncContent();
        UpdateOverlays();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionPaneModel.HostControl):
                SyncContent();
                UpdateOverlays();
                break;
            case nameof(SessionPaneModel.Status):
                UpdateOverlays();
                break;
        }
    }

    private void SyncContent()
    {
        HostPresenter.Content = _model?.HostControl;
    }

    private void UpdateOverlays()
    {
        if (_model is null)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            DisconnectedOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var hasContent = _model.HostControl is not null;
        var status = _model.Status ?? "";

        // Loading: pane exists but host control not yet assigned (connection in progress)
        LoadingOverlay.Visibility = !hasContent
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Disconnected: content exists but status indicates disconnect/error
        DisconnectedOverlay.Visibility = hasContent
            && (string.Equals(status, nameof(ConnectionState.Disconnected), StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, nameof(ConnectionState.Error), StringComparison.OrdinalIgnoreCase))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ── Overlay button handlers (route to MainViewModel) ─────────

    private void OnReconnectClick(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;

        var vm = FindMainViewModel();
        if (vm is null) return;

        // Find which session owns this pane
        foreach (var session in vm.Connection.ActiveSessions)
        {
            if (SplitTreeHelper.FindPane(session.RootContent, _model.PaneId) is not null)
            {
                _ = vm.ReconnectPaneAsync(session, _model.PaneId);
                return;
            }
        }
    }

    private void OnClosePaneClick(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;

        var vm = FindMainViewModel();
        if (vm is null) return;

        // Find which session owns this pane
        foreach (var session in vm.Connection.ActiveSessions)
        {
            if (SplitTreeHelper.FindPane(session.RootContent, _model.PaneId) is not null)
            {
                vm.ClosePane(session, _model.PaneId);
                return;
            }
        }
    }

    private ViewModels.MainViewModel? FindMainViewModel()
    {
        return Application.Current.MainWindow?.DataContext as ViewModels.MainViewModel;
    }
}
