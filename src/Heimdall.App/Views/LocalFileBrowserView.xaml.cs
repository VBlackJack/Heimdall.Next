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

using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Localization;
using Heimdall.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.App.Views;

/// <summary>
/// Simple local filesystem browser for local shell sessions.
/// Shows files and folders with navigation, mimicking the SFTP panel UX.
/// </summary>
public partial class LocalFileBrowserView : UserControl
{
    private readonly LocalizationManager? _localizer;
    private readonly LocalFileBrowserViewModel _viewModel;

    /// <summary>
    /// Raised when the user requests navigation to a directory path in the terminal.
    /// The subscriber should send a cd command to the active shell session.
    /// </summary>
    public event Action<string>? NavigateToPathRequested
    {
        add => _viewModel.NavigateToPathRequested += value;
        remove => _viewModel.NavigateToPathRequested -= value;
    }

    /// <summary>
    /// Raised when the user requests execution of a script file in the terminal.
    /// The subscriber should send the appropriate run command to the active shell session.
    /// </summary>
    public event Action<string>? RunInShellRequested
    {
        add => _viewModel.RunInShellRequested += value;
        remove => _viewModel.RunInShellRequested -= value;
    }

    /// <summary>
    /// Raised when the user wants to edit a file in the embedded editor.
    /// </summary>
    public event Action<string>? EditInEditorRequested
    {
        add => _viewModel.EditInEditorRequested += value;
        remove => _viewModel.EditInEditorRequested -= value;
    }

    /// <summary>
    /// Refreshes the current directory listing.
    /// </summary>
    public void RefreshCurrentDirectory() => _ = _viewModel.Refresh();

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalFileBrowserView"/> class using the user profile as the start path.
    /// </summary>
    public LocalFileBrowserView()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalFileBrowserView"/> class.
    /// </summary>
    /// <param name="startPath">The initial directory shown in the browser.</param>
    /// <param name="localizer">Optional localization manager used for context menu strings and dialogs.</param>
    /// <param name="editorPath">Optional external editor path used by Open in editor.</param>
    public LocalFileBrowserView(string startPath, LocalizationManager? localizer = null, string? editorPath = null)
    {
        _localizer = localizer;
        _viewModel = new LocalFileBrowserViewModel(startPath, localizer, editorPath);

        InitializeComponent();
        DataContext = _viewModel;
        ApplyLocalization();
        Loaded += OnViewLoaded;
    }

    /// <summary>
    /// Applies localized strings to the filter placeholder and context menu items.
    /// </summary>
    private void ApplyLocalization()
    {
        FilterTextBox.Tag = L10n("FileBrowserFilterPlaceholder");

        CtxOpen.Header = L10n("FileBrowserCtxOpen");
        CtxOpenWith.Header = L10n("FileBrowserCtxOpenWith");
        CtxOpenInExplorer.Header = L10n("FileBrowserCtxOpenInExplorer");
        CtxOpenInTerminal.Header = L10n("FileBrowserCtxOpenInTerminal");
        CtxOpenInEditor.Header = L10n("FileBrowserCtxOpenInEditor");
        CtxRunInShell.Header = L10n("FileBrowserCtxRunInShell");
        CtxCopy.Header = L10n("FileBrowserCtxCopy");
        CtxPaste.Header = L10n("FileBrowserCtxPaste");
        CtxCopyPath.Header = L10n("FileBrowserCtxCopyPath");
        CtxRename.Header = L10n("FileBrowserCtxRename");
        CtxDelete.Header = L10n("FileBrowserCtxDelete");
        CtxNewFolder.Header = L10n("FileBrowserCtxNewFolder");
        CtxProperties.Header = L10n("FileBrowserCtxProperties");
        CtxRefresh.Header = L10n("FileBrowserCtxRefresh");
    }

    /// <summary>
    /// Resolves a localized string by key, falling back to the key itself when no localizer is available.
    /// </summary>
    private string L10n(string key) => _localizer?.GetString(key) ?? key;

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.SetDialogService((Application.Current as App)?.Services?.GetService<IDialogService>());
    }

    private void OnFileListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FileListView?.View is not GridView gridView || gridView.Columns.Count < 3)
        {
            return;
        }

        double fixedWidth = 0;
        for (int i = 1; i < gridView.Columns.Count; i++)
        {
            fixedWidth += gridView.Columns[i].ActualWidth;
        }

        double available = FileListView.ActualWidth
            - fixedWidth
            - SystemParameters.VerticalScrollBarWidth
            - 10;

        if (available > 200)
        {
            gridView.Columns[0].Width = available;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (PathTextBox.IsFocused)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F5:
                _ = _viewModel.Refresh();
                e.Handled = true;
                break;

            case Key.F2:
                OnCtxRename(sender, e);
                e.Handled = true;
                break;

            case Key.Delete:
                OnCtxDelete(sender, e);
                e.Handled = true;
                break;

            case Key.Enter:
                OnCtxOpen(sender, e);
                e.Handled = true;
                break;

            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                OnCtxCopy(sender, e);
                e.Handled = true;
                break;

            case Key.V when Keyboard.Modifiers == ModifierKeys.Control:
                OnCtxPaste(sender, e);
                e.Handled = true;
                break;
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.NavigateBack();
    }

    private void OnUpClick(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.NavigateUp();
    }

    private void OnHomeClick(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.NavigateHome();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.Refresh();
    }

    private void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = _viewModel.NavigateToPath(PathTextBox.Text?.Trim());
            e.Handled = true;
        }
    }

    private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry)
        {
            return;
        }

        if (_viewModel.HandleFileDoubleClick(entry))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = entry.FullPath,
                UseShellExecute = true
            })?.Dispose();
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn($"[LocalFileBrowser] file open: {ex.Message}");
        }
    }

    private void OnCtxOpen(object sender, RoutedEventArgs e)
    {
        OnFileDoubleClick(sender, null!);
    }

    private void OnCtxOpenWith(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry || entry.IsDirectory)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = $"shell32.dll,OpenAs_RunDLL \"{entry.FullPath}\"",
                UseShellExecute = false
            })?.Dispose();
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn($"Open With failed: {ex.Message}");
        }
    }

    private void OnCtxOpenInExplorer(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is LocalFileEntry entry)
        {
            try
            {
                if (!entry.IsDirectory)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{entry.FullPath}\""
                    })?.Dispose();
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{entry.FullPath}\""
                    })?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Heimdall.Core.Logging.FileLogger.Warn($"[LocalFileBrowser] open in Explorer: {ex.Message}");
            }
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_viewModel.CurrentPath}\""
                })?.Dispose();
            }
            catch (Exception ex)
            {
                Heimdall.Core.Logging.FileLogger.Warn($"[LocalFileBrowser] open Explorer: {ex.Message}");
            }
        }
    }

    private void OnCtxOpenInTerminal(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is LocalFileEntry { IsDirectory: true } entry)
        {
            _viewModel.InvokeNavigateToPath(entry.FullPath);
        }
    }

    private void OnCtxOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry || entry.IsDirectory)
        {
            return;
        }

        if (!TryCreateEditorStartInfo(_viewModel.EditorPath, entry.FullPath, out var processStartInfo, out var rejectionKey))
        {
            ShowEditorLaunchWarning(L10n(rejectionKey!));
            return;
        }

        try
        {
            Process.Start(processStartInfo!)?.Dispose();
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn($"Failed to open editor: {ex.Message}");
        }
    }

    private void OnCtxRunInShell(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry || entry.IsDirectory)
        {
            return;
        }

        _viewModel.InvokeRunInShell(entry.FullPath);
    }

    private void OnCtxCopy(object sender, RoutedEventArgs e)
    {
        var selected = FileListView.SelectedItems.Cast<LocalFileEntry>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var fileList = new StringCollection();
        foreach (var entry in selected)
        {
            fileList.Add(entry.FullPath);
        }

        Clipboard.SetFileDropList(fileList);
    }

    private async void OnCtxPaste(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsFileDropList())
        {
            return;
        }

        var fileList = Clipboard.GetFileDropList();
        if (fileList is null)
        {
            return;
        }

        await _viewModel.PasteFilesAsync(fileList.Cast<string>().ToList());
    }

    internal static bool TryCreateEditorStartInfo(
        string? configuredEditorPath,
        string filePath,
        out ProcessStartInfo? processStartInfo,
        out string? rejectionKey)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var editorPath = ResolveEditorPath(configuredEditorPath);
        if (InputValidator.IsShellTarget(editorPath))
        {
            processStartInfo = null;
            rejectionKey = "EditorRejectedShellTarget";
            return false;
        }

        processStartInfo = CreateEditorStartInfo(editorPath, filePath);
        rejectionKey = null;
        return true;
    }

    internal static string ResolveEditorPath(string? configuredEditorPath)
    {
        var editorPath = string.IsNullOrWhiteSpace(configuredEditorPath)
            ? @"%windir%\system32\notepad.exe"
            : configuredEditorPath;
        return Environment.ExpandEnvironmentVariables(editorPath);
    }

    internal static ProcessStartInfo CreateEditorStartInfo(string editorPath, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editorPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = editorPath,
            UseShellExecute = false
        };
        processStartInfo.ArgumentList.Add(filePath);
        return processStartInfo;
    }

    private void ShowEditorLaunchWarning(string message)
    {
        var dialogService = (Application.Current as App)?.Services?.GetService<IDialogService>();
        if (dialogService is not null)
        {
            dialogService.ShowWarning(L10n("FileBrowserCtxOpenInEditor"), message);
            return;
        }

        Heimdall.Core.Logging.FileLogger.Warn($"[LocalFileBrowser] editor launch rejected: {message}");
    }

    private void OnCtxCopyPath(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is LocalFileEntry entry)
        {
            Clipboard.SetText(entry.FullPath);
        }
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var hasSelection = FileListView.SelectedItems.Count > 0;
        var singleSelection = FileListView.SelectedItems.Count == 1;
        var isDirectory = singleSelection && FileListView.SelectedItem is LocalFileEntry { IsDirectory: true };
        var isFile = singleSelection && FileListView.SelectedItem is LocalFileEntry { IsDirectory: false };
        var isRunnable = isFile && FileListView.SelectedItem is LocalFileEntry entry
            && _viewModel.IsRunnableFile(entry.Name);

        CtxOpen.IsEnabled = hasSelection;
        CtxOpenWith.IsEnabled = isFile;
        CtxOpenWith.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        CtxOpenInExplorer.IsEnabled = true;
        CtxOpenInEditor.IsEnabled = isFile;
        CtxOpenInEditor.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        CtxRunInShell.IsEnabled = isRunnable;
        CtxRunInShell.Visibility = isRunnable ? Visibility.Visible : Visibility.Collapsed;
        CtxCopy.IsEnabled = hasSelection;
        CtxPaste.IsEnabled = Clipboard.ContainsFileDropList();
        CtxCopyPath.IsEnabled = hasSelection;
        CtxDelete.IsEnabled = hasSelection;
        CtxRename.IsEnabled = singleSelection;
        CtxOpenInTerminal.IsEnabled = isDirectory;
        CtxNewFolder.IsEnabled = true;
        CtxProperties.IsEnabled = singleSelection;
        CtxRefresh.IsEnabled = true;
    }

    private async void OnCtxDelete(object sender, RoutedEventArgs e)
    {
        var selected = FileListView.SelectedItems.Cast<LocalFileEntry>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        await _viewModel.DeleteEntriesAsync(selected);
    }

    private async void OnCtxRename(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry)
        {
            return;
        }

        await _viewModel.RenameEntryAsync(entry);
    }

    private async void OnCtxNewFolder(object sender, RoutedEventArgs e)
    {
        await _viewModel.CreateFolderAsync();
    }

    private void OnCtxProperties(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry)
        {
            return;
        }

        _viewModel.ShowProperties(entry);
    }
}
