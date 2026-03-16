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

namespace Heimdall.App.Services;

/// <summary>
/// Fallback <see cref="IDialogService"/> implementation using system MessageBox.
/// Server, gateway, project, and PIN dialogs return null (not yet implemented)
/// until Phase 5B provides the XAML dialog views.
/// </summary>
public class MessageBoxDialogService : IDialogService
{
    /// <inheritdoc/>
    public Task<bool> ShowConfirmAsync(string title, string message, string severity = "info")
    {
        var icon = severity switch
        {
            "warning" => MessageBoxImage.Warning,
            "error" => MessageBoxImage.Error,
            _ => MessageBoxImage.Question,
        };

        var result = MessageBox.Show(
            message, title, MessageBoxButton.OKCancel, icon);

        return Task.FromResult(result == MessageBoxResult.OK);
    }

    /// <inheritdoc/>
    public Task<string?> ShowInputAsync(string title, string prompt, string? defaultValue = null)
    {
        // Input dialog requires a XAML view (Phase 5B)
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc/>
    public Task<ServerDialogResult?> ShowServerDialogAsync(ServerDialogViewModel? editVm = null)
    {
        // Server dialog requires a XAML view (Phase 5B)
        return Task.FromResult<ServerDialogResult?>(null);
    }

    /// <inheritdoc/>
    public Task<GatewayDialogResult?> ShowGatewayDialogAsync(GatewayDialogViewModel? editVm = null)
    {
        // Gateway dialog requires a XAML view (Phase 5B)
        return Task.FromResult<GatewayDialogResult?>(null);
    }

    /// <inheritdoc/>
    public Task<ProjectDialogResult?> ShowProjectDialogAsync(ProjectDialogViewModel? editVm = null)
    {
        // Project dialog requires a XAML view (Phase 5B)
        return Task.FromResult<ProjectDialogResult?>(null);
    }

    /// <inheritdoc/>
    public Task ShowPinDialogAsync(PinDialogViewModel viewModel)
    {
        // PIN dialog requires a XAML view (Phase 5B)
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <inheritdoc/>
    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
