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

using Heimdall.App.ViewModels.Dialogs;

namespace Heimdall.App.Services;

/// <summary>
/// Abstraction for showing dialogs from ViewModels without referencing WPF types.
/// All methods are async to support both modal dialogs and potential future
/// non-blocking implementations.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a confirmation dialog with OK/Cancel buttons.
    /// </summary>
    /// <param name="title">Dialog title (locale key or pre-resolved text).</param>
    /// <param name="message">Dialog message body.</param>
    /// <param name="severity">Visual severity hint: "info", "warning", or "error".</param>
    /// <returns>True if the user confirmed, false if cancelled.</returns>
    Task<bool> ShowConfirmAsync(string title, string message, string severity = "info");

    /// <summary>
    /// Shows a three-choice dialog (Save / Discard / Cancel).
    /// </summary>
    /// <returns>True = Save, false = Discard, null = Cancel.</returns>
    Task<bool?> ShowSaveDiscardCancelAsync(string title, string message);

    /// <summary>
    /// Shows a text input dialog and returns the entered value.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="prompt">Descriptive prompt label.</param>
    /// <param name="defaultValue">Pre-filled text, or null for empty.</param>
    /// <returns>The entered string, or null if the user cancelled.</returns>
    Task<string?> ShowInputAsync(string title, string prompt, string? defaultValue = null);

    /// <summary>
    /// Shows the server add/edit dialog.
    /// </summary>
    /// <param name="editVm">Pre-populated ViewModel for edit mode, or null for add mode.</param>
    /// <returns>The dialog result containing the DTO and save status, or null if cancelled.</returns>
    Task<ServerDialogResult?> ShowServerDialogAsync(ServerDialogViewModel? editVm = null);

    /// <summary>
    /// Shows the SSH gateway add/edit dialog.
    /// </summary>
    /// <param name="editVm">Pre-populated ViewModel for edit mode, or null for add mode.</param>
    /// <returns>The dialog result containing the DTO and save status, or null if cancelled.</returns>
    Task<GatewayDialogResult?> ShowGatewayDialogAsync(GatewayDialogViewModel? editVm = null);

    /// <summary>
    /// Shows the project add/edit dialog.
    /// </summary>
    /// <param name="editVm">Pre-populated ViewModel for edit mode, or null for add mode.</param>
    /// <returns>The dialog result containing the DTO and save status, or null if cancelled.</returns>
    Task<ProjectDialogResult?> ShowProjectDialogAsync(ProjectDialogViewModel? editVm = null);

    /// <summary>
    /// Shows the scheduled task add/edit dialog.
    /// </summary>
    /// <param name="editVm">Pre-populated ViewModel for edit mode, or null for add mode.</param>
    /// <returns>The dialog result containing the DTO and save status, or null if cancelled.</returns>
    Task<ScheduledTaskDialogResult?> ShowScheduledTaskDialogAsync(ScheduledTaskDialogViewModel? editVm = null);

    /// <summary>
    /// Shows the PIN entry dialog for authentication.
    /// </summary>
    /// <param name="viewModel">The PIN dialog ViewModel managing verification state.</param>
    Task ShowPinDialogAsync(PinDialogViewModel viewModel);

    /// <summary>
    /// Shows a non-blocking error notification.
    /// </summary>
    /// <param name="title">Error title.</param>
    /// <param name="message">Error details.</param>
    void ShowError(string title, string message);

    /// <summary>
    /// Shows a non-blocking informational notification.
    /// </summary>
    /// <param name="title">Info title.</param>
    /// <param name="message">Info details.</param>
    void ShowInfo(string title, string message);

    /// <summary>
    /// Shows a non-blocking warning notification.
    /// </summary>
    /// <param name="title">Warning title.</param>
    /// <param name="message">Warning details.</param>
    void ShowWarning(string title, string message);
}
