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

using System.Windows;
using System.Windows.Input;
using Heimdall.App.ViewModels;

namespace Heimdall.App;

/// <summary>
/// Main application window. All logic lives in <see cref="MainViewModel"/>.
/// Code-behind is limited to keyboard shortcut routing, double-click, and window lifecycle.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Wire toolbar navigation tabs to ViewModel.SelectedTab
        TabServers.Checked += (_, _) => viewModel.SelectedTab = "Servers";
        TabTunnels.Checked += (_, _) => viewModel.SelectedTab = "Tunnels";
        TabScheduled.Checked += (_, _) => viewModel.SelectedTab = "Scheduled";
        TabSettings.Checked += (_, _) => viewModel.SelectedTab = "Settings";

        Loaded += async (_, _) =>
        {
            if (viewModel.LoadCommand.CanExecute(null))
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };

        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.N when Keyboard.Modifiers == ModifierKeys.Control:
                if (vm.ServerList.AddServerCommand.CanExecute(null))
                {
                    vm.ServerList.AddServerCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.Delete:
                if (vm.ServerList.SelectedServer is not null &&
                    vm.ServerList.DeleteServerCommand.CanExecute(vm.ServerList.SelectedServer))
                {
                    vm.ServerList.DeleteServerCommand.Execute(vm.ServerList.SelectedServer);
                }
                e.Handled = true;
                break;

            case Key.E when Keyboard.Modifiers == ModifierKeys.Control:
                if (vm.ServerList.SelectedServer is not null &&
                    vm.ServerList.EditServerCommand.CanExecute(vm.ServerList.SelectedServer))
                {
                    vm.ServerList.EditServerCommand.Execute(vm.ServerList.SelectedServer);
                }
                e.Handled = true;
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Handles double-click on a server row to initiate a connection.
    /// Ensures the click target is a DataGrid row (not a header or empty area).
    /// </summary>
    private void OnServerDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        // Only connect if a row is selected (ignore header/empty clicks)
        if (vm.ServerList.SelectedServer is not null &&
            vm.ServerList.ConnectCommand.CanExecute(vm.ServerList.SelectedServer))
        {
            vm.ServerList.ConnectCommand.Execute(vm.ServerList.SelectedServer);
        }
    }
}
