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

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Heimdall.App.Services;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels;

/// <summary>
/// ViewModel for the embedded text editor. Manages file state, load/save logic,
/// and editor metadata while AvalonEdit interop stays in the code-behind.
/// </summary>
public sealed partial class EmbeddedEditorViewModel : ObservableObject
{
    private readonly LocalizationManager? _localizer;
    private IDialogService? _dialogService;

    [ObservableProperty]
    private string _displayTitle = "";

    [ObservableProperty]
    private string _cursorPositionText = "";

    [ObservableProperty]
    private string _syntaxName = "Plain Text";

    [ObservableProperty]
    private bool _isModified;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbeddedEditorViewModel"/> class.
    /// </summary>
    /// <param name="localizer">Optional localization manager used for dialog strings.</param>
    /// <param name="dialogService">Optional dialog service used for user prompts and notifications.</param>
    public EmbeddedEditorViewModel(
        LocalizationManager? localizer = null,
        IDialogService? dialogService = null)
    {
        _localizer = localizer;
        _dialogService = dialogService;
    }

    /// <summary>
    /// Gets the current file path or remote file name displayed by the editor.
    /// </summary>
    public string? FilePath { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the current content represents a remote file.
    /// </summary>
    public bool IsRemote { get; private set; }

    /// <summary>
    /// Gets the last load error message, if any.
    /// </summary>
    public string? LoadErrorMessage { get; private set; }

    /// <summary>
    /// Raised when the current content is saved.
    /// </summary>
    public event Action<string, string>? FileSaved;

    /// <summary>
    /// Raised when the editor requests to close.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Updates the dialog service after construction when the view resolves it lazily from DI.
    /// </summary>
    /// <param name="dialogService">The dialog service to use for prompts and notifications.</param>
    internal void SetDialogService(IDialogService? dialogService)
    {
        _dialogService = dialogService;
    }

    /// <summary>
    /// Loads a local file and updates the editor state.
    /// </summary>
    /// <param name="filePath">The path of the local file to open.</param>
    /// <returns>The file content on success; otherwise <see langword="null"/>.</returns>
    public async Task<string?> LoadFileAsync(string filePath)
    {
        FilePath = filePath;
        IsRemote = false;
        IsModified = false;
        LoadErrorMessage = null;
        UpdateDisplayTitle();

        try
        {
            return await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            SyntaxName = "Plain Text";
            LoadErrorMessage = ex.Message;
            Heimdall.Core.Logging.FileLogger.Warn($"EmbeddedEditor failed to open: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads provided content for remote editing and updates the editor state.
    /// </summary>
    /// <param name="fileName">The displayed file name or remote path.</param>
    /// <param name="syntaxOverride">Optional explicit syntax name.</param>
    public void LoadContent(string fileName, string? syntaxOverride = null)
    {
        FilePath = fileName;
        IsRemote = true;
        IsModified = false;
        LoadErrorMessage = null;
        SyntaxName = string.IsNullOrWhiteSpace(syntaxOverride) ? "Plain Text" : syntaxOverride;
        UpdateDisplayTitle();
    }

    /// <summary>
    /// Saves the current content and raises the save event used by existing consumers.
    /// </summary>
    /// <param name="currentText">The current editor text to persist.</param>
    /// <returns>
    /// <see langword="true"/> on success; otherwise <see langword="false"/>. For remote files,
    /// <see langword="true"/> means the save was dispatched; the modified state is cleared
    /// by <see cref="ConfirmRemoteSaved"/> when the upload is confirmed.
    /// </returns>
    public async Task<bool> SaveAsync(string currentText)
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            return false;
        }

        try
        {
            if (!IsRemote)
            {
                await File.WriteAllTextAsync(FilePath, currentText);
                IsModified = false;
            }

            FileSaved?.Invoke(FilePath, currentText);
            return true;
        }
        catch (Exception ex)
        {
            string title = L("EditorSaveErrorTitle");
            string message = string.Format(L("EditorSaveErrorMessage"), ex.Message);

            if (_dialogService is not null)
            {
                _dialogService.ShowError(title, message);
            }
            else
            {
                Heimdall.Core.Logging.FileLogger.Warn($"EmbeddedEditor save failed: {ex.Message}");
            }

            return false;
        }
    }

    /// <summary>
    /// Clears the modified state after a remote save has been confirmed by the
    /// consumer that performed the upload.
    /// </summary>
    public void ConfirmRemoteSaved()
    {
        IsModified = false;
    }

    /// <summary>
    /// Requests to close the editor, prompting for confirmation when unsaved changes exist.
    /// </summary>
    public async Task RequestClose()
    {
        if (IsModified)
        {
            if (_dialogService is null)
            {
                return;
            }

            bool confirmed = await _dialogService.ShowConfirmAsync(
                L("EditorUnsavedTitle"),
                L("EditorUnsavedMessage"),
                "warning");

            if (!confirmed)
            {
                return;
            }
        }

        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Updates the formatted cursor position shown in the status bar.
    /// </summary>
    /// <param name="line">The current caret line number.</param>
    /// <param name="column">The current caret column number.</param>
    public void UpdateCursorPosition(int line, int column)
    {
        CursorPositionText = _localizer?.Format("EditorCursorPosition", line.ToString(), column.ToString())
            ?? $"Ln {line}, Col {column}";
    }

    /// <summary>
    /// Marks the document as modified when the editor text changes.
    /// </summary>
    public void NotifyTextChanged()
    {
        if (!IsModified)
        {
            IsModified = true;
        }
    }

    partial void OnIsModifiedChanged(bool value)
    {
        _ = value;
        UpdateDisplayTitle();
    }

    private void UpdateDisplayTitle()
    {
        string name = Path.GetFileName(FilePath) ?? (_localizer?["EditorUntitled"] ?? "Untitled");
        DisplayTitle = IsModified ? $"{name} *" : name;
    }
    private string L(string key) => _localizer?[key] ?? key;
}
