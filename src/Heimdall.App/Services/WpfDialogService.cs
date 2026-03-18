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
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.App.Views.Dialogs;
using Heimdall.Core.Localization;

namespace Heimdall.App.Services;

/// <summary>
/// WPF implementation of <see cref="IDialogService"/>.
/// Creates and shows modal dialog windows, transferring results back to callers.
/// </summary>
public class WpfDialogService(LocalizationManager localizer) : IDialogService
{
    private readonly LocalizationManager _localizer = localizer;

    /// <inheritdoc/>
    public Task<bool> ShowConfirmAsync(string title, string message, string severity = "info")
    {
        var image = severity switch
        {
            "warning" or "danger" => MessageBoxImage.Warning,
            "error" => MessageBoxImage.Error,
            _ => MessageBoxImage.Question
        };

        var result = MessageBox.Show(
            GetOwnerWindow(),
            message,
            title,
            MessageBoxButton.YesNo,
            image);

        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    /// <inheritdoc/>
    public Task<string?> ShowInputAsync(string title, string prompt, string? defaultValue = null)
    {
        var dialog = new InputDialog(_localizer)
        {
            Title = title,
            Prompt = prompt,
            InputText = defaultValue ?? "",
            Owner = GetOwnerWindow()
        };

        string? result = dialog.ShowDialog() == true ? dialog.InputText : null;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<ServerDialogResult?> ShowServerDialogAsync(ServerDialogViewModel? editVm = null)
    {
        var vm = editVm ?? new ServerDialogViewModel();
        vm.Localizer ??= _localizer;
        var dialog = new ServerDialog(_localizer)
        {
            DataContext = vm,
            Owner = GetOwnerWindow()
        };

        ServerDialogResult? result = dialog.ShowDialog() == true
            ? new ServerDialogResult(vm.ToDto(), true)
            : null;

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<GatewayDialogResult?> ShowGatewayDialogAsync(GatewayDialogViewModel? editVm = null)
    {
        var vm = editVm ?? new GatewayDialogViewModel();
        vm.Localizer ??= _localizer;
        var dialog = new GatewayDialog
        {
            DataContext = vm,
            Owner = GetOwnerWindow()
        };

        GatewayDialogResult? result = dialog.ShowDialog() == true
            ? new GatewayDialogResult(vm.ToDto(), true)
            : null;

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<ProjectDialogResult?> ShowProjectDialogAsync(ProjectDialogViewModel? editVm = null)
    {
        var vm = editVm ?? new ProjectDialogViewModel();
        vm.Localizer ??= _localizer;
        var dialog = new ProjectDialog
        {
            DataContext = vm,
            Owner = GetOwnerWindow()
        };

        ProjectDialogResult? result = dialog.ShowDialog() == true
            ? new ProjectDialogResult(vm.ToDto(), true)
            : null;

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task ShowPinDialogAsync(PinDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var dialog = new PinDialog(_localizer)
        {
            DataContext = viewModel,
            Owner = GetOwnerWindow()
        };

        dialog.ShowDialog();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void ShowError(string title, string message)
    {
        MessageBox.Show(
            GetOwnerWindow(),
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <inheritdoc/>
    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(
            GetOwnerWindow(),
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// Gets the current main window as dialog owner for proper centering and modal behavior.
    /// Returns null if no main window is available (e.g., during startup).
    /// </summary>
    private static Window? GetOwnerWindow()
    {
        return Application.Current?.MainWindow;
    }
}
