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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Heimdall.App.Views.Dialogs;
using Heimdall.Core.Localization;
using Heimdall.Core.Utilities;

namespace Heimdall.App.Views;

/// <summary>
/// Simple local filesystem browser for local shell sessions.
/// Shows files/folders with navigation, mimicking the SFTP panel UX.
/// </summary>
public partial class LocalFileBrowserView : UserControl
{
    private static readonly HashSet<string> _runnableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".bat", ".cmd", ".sh"
    };

    private readonly ObservableCollection<LocalFileEntry> _files = [];
    private readonly List<LocalFileEntry> _allFiles = [];
    private readonly Stack<string> _history = new();
    private readonly LocalizationManager? _localizer;
    private readonly string _sessionRoot;
    private readonly string? _editorPath;
    private string _currentPath;

    /// <summary>
    /// Raised when the user requests navigation to a directory path in the terminal.
    /// The subscriber should send a cd command to the active shell session.
    /// </summary>
    public event Action<string>? NavigateToPathRequested;

    /// <summary>
    /// Raised when the user requests execution of a script file in the terminal.
    /// The subscriber should send the appropriate run command to the active shell session.
    /// </summary>
    public event Action<string>? RunInShellRequested;

    /// <summary>Raised when the user wants to edit a file in the embedded editor.</summary>
    public event Action<string>? EditInEditorRequested;

    /// <summary>Refreshes the current directory listing.</summary>
    public void RefreshCurrentDirectory() => LoadDirectory(_currentPath);

    public LocalFileBrowserView()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null)
    {
    }

    public LocalFileBrowserView(string startPath, LocalizationManager? localizer = null, string? editorPath = null)
    {
        InitializeComponent();
        _localizer = localizer;
        _editorPath = editorPath;
        _currentPath = Directory.Exists(startPath)
            ? startPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _sessionRoot = _currentPath;
        FileListView.ItemsSource = _files;
        ApplyLocalization();
        LoadDirectory(_currentPath);
    }

    /// <summary>
    /// Applies localized strings to toolbar tooltips, column headers, and context menu items.
    /// </summary>
    private void ApplyLocalization()
    {
        // Filter placeholder
        FilterTextBox.Tag = L10n("FileBrowserFilterPlaceholder");

        // Toolbar tooltips + accessibility
        BtnBack.ToolTip = L10n("FileBrowserToolTipBack");
        BtnUp.ToolTip = L10n("FileBrowserToolTipUp");
        BtnHome.ToolTip = L10n("FileBrowserToolTipHome");
        BtnRefresh.ToolTip = L10n("FileBrowserToolTipRefresh");
        System.Windows.Automation.AutomationProperties.SetName(BtnBack, L10n("FileBrowserToolTipBack"));
        System.Windows.Automation.AutomationProperties.SetName(BtnUp, L10n("FileBrowserToolTipUp"));
        System.Windows.Automation.AutomationProperties.SetName(BtnHome, L10n("FileBrowserToolTipHome"));
        System.Windows.Automation.AutomationProperties.SetName(BtnRefresh, L10n("FileBrowserToolTipRefresh"));

        // Column headers
        if (FileListView.View is GridView gridView && gridView.Columns.Count >= 3)
        {
            gridView.Columns[0].Header = L10n("FileBrowserColName");
            gridView.Columns[1].Header = L10n("FileBrowserColSize");
            gridView.Columns[2].Header = L10n("FileBrowserColModified");
        }

        // Context menu items
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

    private void LoadDirectory(string path)
    {
        try
        {
            LoadingBar.Visibility = Visibility.Visible;

            var entries = new List<LocalFileEntry>();

            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    entries.Add(new LocalFileEntry(info.Name, info.FullName, true, 0, info.LastWriteTime));
                }
                catch (Exception ex) { Core.Logging.FileLogger.Warn($"[LocalFileBrowser] skip inaccessible directory: {ex.Message}"); }
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                try
                {
                    var info = new FileInfo(file);
                    entries.Add(new LocalFileEntry(info.Name, info.FullName, false, info.Length, info.LastWriteTime));
                }
                catch (Exception ex) { Core.Logging.FileLogger.Warn($"[LocalFileBrowser] skip inaccessible file: {ex.Message}"); }
            }

            if (!string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                _history.Push(_currentPath);
            }

            _currentPath = path;
            PathTextBox.Text = _currentPath;

            _allFiles.Clear();
            _allFiles.AddRange(entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase));

            ApplyFilter();

            BtnBack.IsEnabled = _history.Count > 0;
            LoadingBar.Visibility = Visibility.Collapsed;
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            Core.Logging.FileLogger.Warn($"LocalFileBrowser load failed: {ex.Message}");
        }
    }

    private void ApplyFilter()
    {
        var filterText = FilterTextBox?.Text?.Trim() ?? "";

        _files.Clear();

        var source = string.IsNullOrEmpty(filterText)
            ? _allFiles
            : _allFiles.Where(e => e.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var e in source)
        {
            _files.Add(e);
        }

        if (_files.Count == 0)
        {
            EmptyDirectoryText.Text = L10n("FileBrowserEmptyFolder");
            EmptyDirectoryText.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyDirectoryText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        var template = L10n("FileBrowserStatusItems");
        StatusBarText.Text = string.Format(template, _files.Count);
    }

    /// <summary>
    /// Resolves a localized string by key, falling back to the key itself when no localizer is available.
    /// </summary>
    private string L10n(string key) => _localizer?.GetString(key) ?? key;

    // ── Keyboard shortcuts ──────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Skip keyboard shortcuts when the path text box has focus
        if (PathTextBox.IsFocused) return;

        switch (e.Key)
        {
            case Key.F5:
                LoadDirectory(_currentPath);
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

    // ── Navigation ──────────────────────────────────────────────────────

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_history.TryPop(out var prev))
        {
            _currentPath = prev;
            LoadDirectory(prev);
        }
    }

    private void OnUpClick(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentPath);
        if (parent is not null)
        {
            LoadDirectory(parent.FullName);
        }
    }

    private void OnHomeClick(object sender, RoutedEventArgs e)
    {
        LoadDirectory(_sessionRoot);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        LoadDirectory(_currentPath);
    }

    private void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var path = PathTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                LoadDirectory(path);
            }
            e.Handled = true;
        }
    }

    // ── Open / double-click ─────────────────────────────────────────────

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".json", ".xml", ".yaml", ".yml", ".conf", ".cfg", ".ini",
        ".ps1", ".bat", ".cmd", ".sh", ".bash", ".py", ".rb", ".js", ".ts", ".css", ".html",
        ".htm", ".cs", ".java", ".c", ".cpp", ".h", ".hpp", ".sql", ".csv", ".env",
        ".toml", ".properties", ".service", ".timer", ".socket", ".gitignore", ".editorconfig"
    };

    private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry) return;

        if (entry.IsDirectory)
        {
            LoadDirectory(entry.FullPath);
        }
        else if (IsTextFile(entry.Name) && EditInEditorRequested is not null)
        {
            EditInEditorRequested.Invoke(entry.FullPath);
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = entry.FullPath, UseShellExecute = true });
            }
            catch (Exception ex) { Core.Logging.FileLogger.Warn($"[LocalFileBrowser] file open: {ex.Message}"); }
        }
    }

    private static bool IsTextFile(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && TextExtensions.Contains(ext);
    }

    private void OnCtxOpen(object sender, RoutedEventArgs e)
    {
        OnFileDoubleClick(sender, null!);
    }

    private void OnCtxOpenWith(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry || entry.IsDirectory) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = $"shell32.dll,OpenAs_RunDLL \"{entry.FullPath}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Open With failed: {ex.Message}");
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
                    // Highlight the file in Explorer
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{entry.FullPath}\""
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{entry.FullPath}\"" });
                }
            }
            catch (Exception ex) { Core.Logging.FileLogger.Warn($"[LocalFileBrowser] open in Explorer: {ex.Message}"); }
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{_currentPath}\"" });
            }
            catch (Exception ex) { Core.Logging.FileLogger.Warn($"[LocalFileBrowser] open Explorer: {ex.Message}"); }
        }
    }

    private void OnCtxOpenInTerminal(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is LocalFileEntry { IsDirectory: true } entry)
        {
            NavigateToPathRequested?.Invoke(entry.FullPath);
        }
    }

    private void OnCtxOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry || entry.IsDirectory) return;

        var editorPath = _editorPath;
        if (string.IsNullOrWhiteSpace(editorPath))
        {
            editorPath = Environment.ExpandEnvironmentVariables(@"%windir%\system32\notepad.exe");
        }
        else
        {
            editorPath = Environment.ExpandEnvironmentVariables(editorPath);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = editorPath,
                Arguments = $"\"{entry.FullPath}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Failed to open editor: {ex.Message}");
        }
    }

    private void OnCtxRunInShell(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry || entry.IsDirectory) return;
        RunInShellRequested?.Invoke(entry.FullPath);
    }

    // ── Clipboard operations ────────────────────────────────────────────

    private void OnCtxCopy(object sender, RoutedEventArgs e)
    {
        var selected = FileListView.SelectedItems.Cast<LocalFileEntry>().ToList();
        if (selected.Count == 0) return;

        var fileList = new StringCollection();
        foreach (var entry in selected)
        {
            fileList.Add(entry.FullPath);
        }

        Clipboard.SetFileDropList(fileList);
    }

    private void OnCtxPaste(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsFileDropList()) return;

        var fileList = Clipboard.GetFileDropList();
        if (fileList is null) return;

        foreach (var sourcePath in fileList)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) continue;

            try
            {
                var name = Path.GetFileName(sourcePath);
                var destPath = Path.Combine(_currentPath, name);

                if (File.Exists(sourcePath))
                {
                    if (File.Exists(destPath))
                    {
                        var message = string.Format(L10n("FileBrowserPasteOverwriteMessage"), name);
                        var title = L10n("FileBrowserPasteOverwriteTitle");
                        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result != MessageBoxResult.Yes) continue;
                    }
                    File.Copy(sourcePath, destPath, overwrite: true);
                }
                else if (Directory.Exists(sourcePath))
                {
                    CopyDirectoryRecursive(sourcePath, destPath);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = string.Format(L10n("FileBrowserPasteError"), Path.GetFileName(sourcePath), ex.Message);
                MessageBox.Show(errorMsg, L10n("FileBrowserPasteOverwriteTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        LoadDirectory(_currentPath);
    }

    /// <summary>
    /// Recursively copies a directory and its contents to a destination path.
    /// </summary>
    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSubDir);
        }
    }

    private void OnCtxCopyPath(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is LocalFileEntry entry)
        {
            Clipboard.SetText(entry.FullPath);
        }
    }

    // ── Context menu state ──────────────────────────────────────────────

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var hasSelection = FileListView.SelectedItems.Count > 0;
        var singleSelection = FileListView.SelectedItems.Count == 1;
        var isDirectory = singleSelection && FileListView.SelectedItem is LocalFileEntry { IsDirectory: true };
        var isFile = singleSelection && FileListView.SelectedItem is LocalFileEntry { IsDirectory: false };
        var isRunnable = isFile && FileListView.SelectedItem is LocalFileEntry entry
            && _runnableExtensions.Contains(Path.GetExtension(entry.Name));

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

    // ── File operations ─────────────────────────────────────────────────

    private void OnCtxDelete(object sender, RoutedEventArgs e)
    {
        var selected = FileListView.SelectedItems.Cast<LocalFileEntry>().ToList();
        if (selected.Count == 0) return;

        foreach (var entry in selected)
        {
            var message = string.Format(L10n("FileBrowserConfirmDeleteMessage"), entry.Name);
            var title = L10n("FileBrowserConfirmDeleteTitle");
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) continue;

            try
            {
                if (entry.IsDirectory)
                    Directory.Delete(entry.FullPath, recursive: true);
                else
                    File.Delete(entry.FullPath);
            }
            catch (Exception ex)
            {
                var errorMsg = string.Format(L10n("FileBrowserDeleteError"), entry.Name, ex.Message);
                MessageBox.Show(errorMsg, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        LoadDirectory(_currentPath);
    }

    private void OnCtxRename(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry) return;

        var dialog = new InputDialog
        {
            Title = L10n("FileBrowserRenameTitle"),
            Prompt = L10n("FileBrowserRenamePrompt"),
            InputText = entry.Name,
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true) return;

        var newName = dialog.InputText?.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == entry.Name) return;

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.Contains(".."))
        {
            MessageBox.Show(L10n("ErrorInvalidFileName"), L10n("FileBrowserRenameTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var parentDir = Path.GetDirectoryName(entry.FullPath)!;
            var newPath = Path.GetFullPath(Path.Combine(parentDir, newName));
            if (!newPath.StartsWith(parentDir, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(L10n("ErrorInvalidFileName"), L10n("FileBrowserRenameTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (entry.IsDirectory)
                Directory.Move(entry.FullPath, newPath);
            else
                File.Move(entry.FullPath, newPath);

            LoadDirectory(_currentPath);
        }
        catch (Exception ex)
        {
            var errorMsg = string.Format(L10n("FileBrowserRenameError"), entry.Name, ex.Message);
            MessageBox.Show(errorMsg, L10n("FileBrowserRenameTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCtxNewFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog
        {
            Title = L10n("FileBrowserNewFolderTitle"),
            Prompt = L10n("FileBrowserNewFolderPrompt"),
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true) return;

        var folderName = dialog.InputText?.Trim();
        if (string.IsNullOrWhiteSpace(folderName)) return;

        if (folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folderName.Contains(".."))
        {
            MessageBox.Show(L10n("ErrorInvalidFileName"), L10n("FileBrowserNewFolderTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var newPath = Path.GetFullPath(Path.Combine(_currentPath, folderName));
            if (!newPath.StartsWith(_currentPath, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(L10n("ErrorInvalidFileName"), L10n("FileBrowserNewFolderTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Directory.CreateDirectory(newPath);
            LoadDirectory(_currentPath);
        }
        catch (Exception ex)
        {
            var errorMsg = string.Format(L10n("FileBrowserNewFolderError"), ex.Message);
            MessageBox.Show(errorMsg, L10n("FileBrowserNewFolderTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Properties ──────────────────────────────────────────────────────

    private void OnCtxProperties(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not LocalFileEntry entry) return;

        try
        {
            var sb = new StringBuilder();

            sb.AppendLine($"{L10n("FileBrowserPropertiesName")}: {entry.Name}");
            sb.AppendLine($"{L10n("FileBrowserPropertiesPath")}: {entry.FullPath}");

            if (entry.IsDirectory)
            {
                var dirInfo = new DirectoryInfo(entry.FullPath);
                sb.AppendLine($"{L10n("FileBrowserPropertiesType")}: {L10n("FileBrowserPropertiesTypeFolder")}");
                sb.AppendLine($"{L10n("FileBrowserPropertiesCreated")}: {dirInfo.CreationTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"{L10n("FileBrowserPropertiesModified")}: {dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"{L10n("FileBrowserPropertiesAttributes")}: {dirInfo.Attributes}");
            }
            else
            {
                var fileInfo = new FileInfo(entry.FullPath);
                sb.AppendLine($"{L10n("FileBrowserPropertiesType")}: {fileInfo.Extension}");
                sb.AppendLine($"{L10n("FileBrowserPropertiesSize")}: {FormatSize(fileInfo.Length)}");
                sb.AppendLine($"{L10n("FileBrowserPropertiesCreated")}: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"{L10n("FileBrowserPropertiesModified")}: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"{L10n("FileBrowserPropertiesAccessed")}: {fileInfo.LastAccessTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"{L10n("FileBrowserPropertiesAttributes")}: {fileInfo.Attributes}");
            }

            MessageBox.Show(
                sb.ToString(),
                L10n("FileBrowserPropertiesTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Properties read failed: {ex.Message}");
        }
    }

    private static string FormatSize(long bytes) => FileSize.Format(bytes);
}

/// <summary>Local filesystem entry for the file browser ListView.</summary>
public record LocalFileEntry(string Name, string FullPath, bool IsDirectory, long Size, DateTime LastModified);
