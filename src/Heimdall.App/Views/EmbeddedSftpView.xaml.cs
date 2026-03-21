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
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Localization;
using Heimdall.Core.Utilities;
using Heimdall.Sftp;
using Heimdall.Ssh;
using Microsoft.Win32;

namespace Heimdall.App.Views;

/// <summary>
/// WPF host for an interactive SFTP file browser backed by <see cref="SftpBrowser"/>.
/// Provides directory navigation, file transfers, inline editing, drag-drop upload,
/// column sorting, file filtering, bookmarks, chmod, and keyboard shortcuts.
/// </summary>
public partial class EmbeddedSftpView : UserControl, IDisposable
{
    private static readonly TimeSpan SftpOperationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StatusResetDelay = TimeSpan.FromSeconds(5);
    private const string RemoteTempPrefix = "/tmp/.heimdall_";

    private IRemoteBrowser? _browser;
    private RemoteFileEditor? _editor;
    private SessionTabViewModel? _sessionTab;
    private LocalizationManager? _localizer;
    private IDialogService? _dialogService;
    private SshConnectionParams? _sshParams;
    private Heimdall.Ssh.HostKeyStore? _hostKeyStore;
    private CancellationTokenSource? _transferCts;
    private System.Threading.Timer? _healthTimer;
    private System.Threading.Timer? _errorResetTimer;

    private readonly ObservableCollection<SftpFileInfo> _fileList = [];
    private readonly Stack<string> _navigationHistory = new();
    private readonly List<string> _bookmarks = [];
    private List<SftpFileInfo> _unfilteredEntries = [];
    private string _currentPath = "/";
    private string _homeDirectory = "/";
    private bool _disposed;
    private bool _isLoading;
    private bool _showHidden = true;
    private bool _sudoMode;
    private string _sortColumn = "Name";
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    /// <summary>
    /// Raised when the user clicks the Split button in the header strip.
    /// The subscriber (EmbeddedSessionManager) shows the split picker context menu.
    /// </summary>
    public event Action? SplitRequested;

    /// <summary>
    /// Raised when the user selects "Open in terminal" from the context menu.
    /// The parameter is the remote directory path to cd into.
    /// </summary>
    public event Action<string>? OpenInTerminalRequested;

    public EmbeddedSftpView()
    {
        InitializeComponent();
        FileListView.ItemsSource = _fileList;
    }

    /// <summary>
    /// Wires the view to a connected SFTP browser session.
    /// Must be called exactly once, immediately after construction.
    /// </summary>
    public void InitializeSession(
        IRemoteBrowser browser,
        SessionTabViewModel sessionTab,
        string displayName,
        string endpoint,
        LocalizationManager localizer,
        IDialogService dialogService,
        SshConnectionParams? sshParams = null,
        Heimdall.Ssh.HostKeyStore? hostKeyStore = null)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(sessionTab);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(dialogService);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EmbeddedSftpView));
        }

        _browser = browser;
        _sessionTab = sessionTab;
        _localizer = localizer;
        _dialogService = dialogService;
        _sshParams = sshParams;
        _hostKeyStore = hostKeyStore;

        _editor = new RemoteFileEditor(browser, hostKeyStore: hostKeyStore);
        _editor.FileUploaded += OnEditorFileUploaded;

        // Hide sudo toggle for FTP sessions (no SSH channel for sudo)
        BtnSudoMode.Visibility = sshParams is not null
            ? Visibility.Visible : Visibility.Collapsed;

        SessionTitleText.Text = displayName;
        EndpointTextBlock.Text = endpoint;

        _browser.DirectoryChanged += OnDirectoryChanged;
        _browser.TransferProgress += OnTransferProgress;
        _browser.Disconnected += OnBrowserDisconnected;

        _homeDirectory = browser.CurrentDirectory;

        ApplyLocalization();
        UpdateStatus(_localizer["SftpStatusConnected"]);
        StartHealthTimer();

        _ = LoadDirectoryAsync(browser.CurrentDirectory);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopHealthTimer();
        _errorResetTimer?.Dispose();
        _transferCts?.Cancel();
        _transferCts?.Dispose();

        if (_editor is not null)
        {
            _editor.FileUploaded -= OnEditorFileUploaded;
            _editor.Dispose();
            _editor = null;
        }

        if (_browser is not null)
        {
            _browser.DirectoryChanged -= OnDirectoryChanged;
            _browser.TransferProgress -= OnTransferProgress;
            _browser.Disconnected -= OnBrowserDisconnected;

            try { _browser.Disconnect(); }
            catch (ObjectDisposedException) { /* Expected when disposing already-closed connection */ }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn(
                    $"EmbeddedSFTP Disconnect during dispose failed: {ex.Message}");
            }

            try { _browser.Dispose(); }
            catch (ObjectDisposedException) { /* Expected when disposing already-closed browser */ }

            _browser = null;
        }

        Core.Logging.FileLogger.Info("EmbeddedSFTP Dispose completed");
    }

    // ------------------------------------------------------------------
    // Localization
    // ------------------------------------------------------------------

    private void ApplyLocalization()
    {
        if (_localizer is null)
        {
            return;
        }

        DisconnectButton.Content = _localizer["SftpBtnClose"];
        ReconnectButton.Content = _localizer["SftpBtnReconnect"];
        BtnBack.ToolTip = _localizer["SftpBtnBack"];
        BtnUp.ToolTip = _localizer["SftpBtnUp"];
        BtnHome.ToolTip = _localizer["SftpHomeDir"];
        BtnRefresh.ToolTip = _localizer["SftpBtnRefresh"];
        BtnUploadText.Text = _localizer["SftpBtnUpload"];
        BtnNewFolderText.Text = _localizer["SftpBtnNewFolder"];
        BtnCancelTransfer.Content = _localizer["SftpBtnCancelTransfer"];
        BtnBookmark.ToolTip = _localizer["SftpBtnBookmark"];
        BtnBookmarks.ToolTip = _localizer["SftpBtnBookmarks"];
        ToggleHiddenCheckBox.ToolTip = _localizer["SftpToggleHidden"];
        SplitButton.ToolTip = _localizer["ToolTipSplitPane"];
        BtnSudoMode.ToolTip = _localizer["SftpSudoModeTooltip"];
        BtnSudoModeText.Text = _localizer["SftpSudoModeLabel"];

        FilterTextBox.Tag = _localizer["SftpFilterPlaceholder"];

        CtxOpen.Header = _localizer["SftpCtxOpen"];
        CtxEdit.Header = _localizer["SftpCtxEdit"];
        CtxEditExternal.Header = _localizer["SftpCtxEditExternal"];
        CtxDownload.Header = _localizer["SftpBtnDownload"];
        CtxRename.Header = _localizer["SftpBtnRename"];
        CtxDelete.Header = _localizer["SftpBtnDelete"];
        CtxChmod.Header = _localizer["SftpCtxChmod"];
        CtxCopyPath.Header = _localizer["SftpCopyPath"];
        CtxProperties.Header = _localizer["SftpCtxProperties"];
        CtxUploadHere.Header = _localizer["SftpCtxUploadHere"];
        CtxOpenInTerminal.Header = _localizer["SftpCtxOpenInTerminal"];

        EmptyDirectoryText.Text = _localizer["SftpEmptyDirectory"];
        DragDropOverlayText.Text = _localizer["SftpDragDropOverlay"];

        // Accessibility: automation names for toolbar buttons
        System.Windows.Automation.AutomationProperties.SetName(BtnBack, _localizer["SftpBtnBack"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnUp, _localizer["SftpBtnUp"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnHome, _localizer["SftpHomeDir"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnRefresh, _localizer["SftpBtnRefresh"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnUpload, _localizer["SftpBtnUpload"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnNewFolder, _localizer["SftpBtnNewFolder"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnBookmark, _localizer["SftpBtnBookmark"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnBookmarks, _localizer["SftpBtnBookmarks"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnSudoMode, _localizer["SftpSudoModeTooltip"]);
        System.Windows.Automation.AutomationProperties.SetName(DisconnectButton, _localizer["SftpBtnClose"]);
        System.Windows.Automation.AutomationProperties.SetName(SplitButton, _localizer["ToolTipSplitPane"]);
        System.Windows.Automation.AutomationProperties.SetName(ToggleHiddenCheckBox, _localizer["SftpToggleHidden"]);

        if (FileListView.View is GridView gridView)
        {
            if (gridView.Columns.Count > 0) gridView.Columns[0].Header = _localizer["SftpColName"];
            if (gridView.Columns.Count > 1) gridView.Columns[1].Header = _localizer["SftpColSize"];
            if (gridView.Columns.Count > 2) gridView.Columns[2].Header = _localizer["SftpColModified"];
            if (gridView.Columns.Count > 3) gridView.Columns[3].Header = _localizer["SftpColPermissions"];
            if (gridView.Columns.Count > 4) gridView.Columns[4].Header = _localizer["SftpColOwner"];
        }
    }

    // ------------------------------------------------------------------
    // Keyboard shortcuts
    // ------------------------------------------------------------------

    private void OnViewKeyDown(object sender, KeyEventArgs e)
    {
        if (_disposed || _browser is null || !_browser.IsConnected)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F5:
                OnRefreshClick(this, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.F2:
                if (FileListView.SelectedItem is SftpFileInfo)
                {
                    OnCtxRenameClick(this, new RoutedEventArgs());
                }
                e.Handled = true;
                break;

            case Key.Delete:
                if (FileListView.SelectedItems.Count > 0)
                {
                    OnCtxDeleteClick(this, new RoutedEventArgs());
                }
                e.Handled = true;
                break;

            case Key.Enter:
                if (FileListView.SelectedItem is SftpFileInfo enterFile)
                {
                    if (enterFile.IsDirectory)
                    {
                        _ = LoadDirectoryAsync(enterFile.FullPath);
                    }
                    else
                    {
                        _ = EditFileAsync(enterFile);
                    }
                }
                e.Handled = true;
                break;

            case Key.Back:
                OnBackClick(this, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                OnCtxCopyPathClick(this, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                FilterTextBox.Focus();
                e.Handled = true;
                break;
        }
    }

    // ------------------------------------------------------------------
    // Directory navigation
    // ------------------------------------------------------------------

    private async Task LoadDirectoryAsync(string path)
    {
        if (_disposed || _browser is null || !_browser.IsConnected || _isLoading)
        {
            return;
        }

        _isLoading = true;
        SetToolbarEnabled(false);
        LoadingBar.Visibility = Visibility.Visible;

        try
        {
            UpdateStatus(_localizer?["SftpStatusLoading"] ?? "Loading...");

            IReadOnlyList<SftpFileInfo> entries;

            if (_sudoMode && _sshParams is not null)
            {
                // sudo mode: list via SSH exec channel
                entries = await ListDirectoryViaSudoAsync(path);
            }
            else
            {
                try
                {
                    entries = await _browser.ListDirectoryAsync(path);
                }
                catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
                {
                    // Auto-fallback to sudo for this listing
                    Core.Logging.FileLogger.Info(
                        $"EmbeddedSFTP listdir permission denied, falling back to sudo for {path}");
                    entries = await ListDirectoryViaSudoAsync(path);
                }
            }

            if (!string.Equals(path, _currentPath, StringComparison.Ordinal))
            {
                _navigationHistory.Push(_currentPath);
            }

            _currentPath = path;
            _unfilteredEntries = [.. entries];

            await Dispatcher.InvokeAsync(() =>
            {
                PathTextBox.Text = _currentPath;
                ApplyFilterAndSort();
                BtnBack.IsEnabled = _navigationHistory.Count > 0;
                UpdateStatus(_localizer?["SftpStatusReady"] ?? "Ready");
            });
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"EmbeddedSFTP LoadDirectory failed: {ex.Message}");
            ShowError(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message);
        }
        finally
        {
            _isLoading = false;
            SetToolbarEnabled(true);
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFilterAndSort()
    {
        // Guard: called during InitializeComponent before all elements are ready
        if (ItemCountText is null || EmptyDirectoryText is null || FileListView is null)
        {
            return;
        }

        string filter = FilterTextBox?.Text?.Trim() ?? "";
        bool showHidden = ToggleHiddenCheckBox.IsChecked == true;

        var filtered = _unfilteredEntries.AsEnumerable();

        // Hide dotfiles unless toggled
        if (!showHidden)
        {
            filtered = filtered.Where(f => !f.Name.StartsWith('.'));
        }

        // Text filter
        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(f =>
                f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        // Sort: directories first, then by selected column
        var sorted = _sortColumn switch
        {
            "Size" => _sortDirection == ListSortDirection.Ascending
                ? filtered.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Size)
                : filtered.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Size),
            "Modified" => _sortDirection == ListSortDirection.Ascending
                ? filtered.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.LastModified)
                : filtered.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.LastModified),
            "Permissions" => _sortDirection == ListSortDirection.Ascending
                ? filtered.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Permissions)
                : filtered.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Permissions),
            "Owner" => _sortDirection == ListSortDirection.Ascending
                ? filtered.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Owner)
                : filtered.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Owner),
            _ => _sortDirection == ListSortDirection.Ascending
                ? filtered.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase),
        };

        _fileList.Clear();
        foreach (var entry in sorted)
        {
            _fileList.Add(entry);
        }

        EmptyDirectoryText.Visibility = _fileList.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        int totalCount = _unfilteredEntries.Count;
        int visibleCount = _fileList.Count;
        ItemCountText.Text = visibleCount == totalCount
            ? $"{totalCount} items"
            : $"{visibleCount}/{totalCount} items";
    }

    // ------------------------------------------------------------------
    // Column sorting
    // ------------------------------------------------------------------

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header || header.Column is null)
        {
            return;
        }

        string? columnName = header.Column.Header?.ToString();
        if (string.IsNullOrEmpty(columnName))
        {
            return;
        }

        // Map localized headers back to property names
        if (FileListView.View is GridView gridView)
        {
            int colIndex = gridView.Columns.IndexOf(header.Column);
            columnName = colIndex switch
            {
                0 => "Name",
                1 => "Size",
                2 => "Modified",
                3 => "Permissions",
                4 => "Owner",
                _ => columnName
            };
        }

        if (string.Equals(_sortColumn, columnName, StringComparison.Ordinal))
        {
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            _sortColumn = columnName;
            _sortDirection = ListSortDirection.Ascending;
        }

        ApplyFilterAndSort();
        UpdateColumnHeaders();
    }

    private void UpdateColumnHeaders()
    {
        if (FileListView?.View is not GridView gridView)
        {
            return;
        }

        string[] names = ["Name", "Size", "Modified", "Permissions", "Owner"];
        string arrow = _sortDirection == ListSortDirection.Ascending ? " \u25B2" : " \u25BC";

        for (int i = 0; i < gridView.Columns.Count && i < names.Length; i++)
        {
            string baseName = _localizer?[$"SftpCol{names[i]}"] ?? names[i];
            gridView.Columns[i].Header = string.Equals(names[i], _sortColumn, StringComparison.Ordinal)
                ? baseName + arrow
                : baseName;
        }
    }

    // ------------------------------------------------------------------
    // Filter and hidden files
    // ------------------------------------------------------------------

    private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoading)
        {
            ApplyFilterAndSort();
        }
    }

    private void OnToggleHiddenChanged(object sender, RoutedEventArgs e)
    {
        _showHidden = ToggleHiddenCheckBox.IsChecked == true;
        if (!_isLoading)
        {
            ApplyFilterAndSort();
        }
    }

    // ------------------------------------------------------------------
    // Selection info
    // ------------------------------------------------------------------

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int count = FileListView.SelectedItems.Count;
        if (count > 1)
        {
            long totalSize = GetSelectedFiles()
                .Where(f => !f.IsDirectory)
                .Sum(f => f.Size);
            SelectionInfoText.Text = _localizer?.Format("SftpSelectedCount", count.ToString())
                ?? $"{count} selected";
            if (totalSize > 0)
            {
                SelectionInfoText.Text += $" ({FormatSize(totalSize)})";
            }
        }
        else
        {
            SelectionInfoText.Text = "";
        }
    }

    // ------------------------------------------------------------------
    // Navigation events
    // ------------------------------------------------------------------

    private void OnDirectoryChanged(string newPath)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                _currentPath = newPath;
                PathTextBox.Text = newPath;
            }
        });
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_navigationHistory.TryPop(out var previousPath))
        {
            _currentPath = previousPath;
            _ = LoadDirectoryAsync(previousPath);
        }
    }

    private void OnUpClick(object sender, RoutedEventArgs e)
    {
        string parent = GetParentPath(_currentPath);
        if (!string.Equals(parent, _currentPath, StringComparison.Ordinal))
        {
            _ = LoadDirectoryAsync(parent);
        }
    }

    private void OnHomeClick(object sender, RoutedEventArgs e)
    {
        _ = LoadDirectoryAsync(_homeDirectory);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        string path = _currentPath;
        _currentPath = path;
        _ = LoadDirectoryAsync(path);
    }

    private void OnGoPathClick(object sender, RoutedEventArgs e) => NavigateToPathBar();

    private void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateToPathBar();
            e.Handled = true;
        }
    }

    private void NavigateToPathBar()
    {
        var path = PathTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _ = LoadDirectoryAsync(path);
        }
    }

    private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is not SftpFileInfo file)
        {
            return;
        }

        if (file.IsDirectory)
        {
            _ = LoadDirectoryAsync(file.FullPath);
        }
        else
        {
            _ = EditFileAsync(file);
        }
    }

    // ------------------------------------------------------------------
    // Bookmarks
    // ------------------------------------------------------------------

    private void OnFileListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FileListView?.View is not GridView gv || gv.Columns.Count < 5)
        {
            return;
        }

        double fixedWidth = 0;
        for (int i = 1; i < gv.Columns.Count; i++)
        {
            fixedWidth += gv.Columns[i].ActualWidth;
        }

        double available = FileListView.ActualWidth
            - fixedWidth
            - SystemParameters.VerticalScrollBarWidth
            - 10;

        if (available > 200)
        {
            gv.Columns[0].Width = available;
        }
    }

    private void OnBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (!_bookmarks.Contains(_currentPath))
        {
            _bookmarks.Add(_currentPath);
            UpdateStatus(_localizer?.Format("SftpBookmarkAdded", _currentPath)
                ?? $"Bookmarked: {_currentPath}");
        }
    }

    private void OnBookmarksClick(object sender, RoutedEventArgs e)
    {
        if (_bookmarks.Count == 0)
        {
            UpdateStatus(_localizer?["SftpBookmarkEmpty"] ?? "No bookmarks");
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = BtnBookmarks,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        foreach (string path in _bookmarks)
        {
            var item = new MenuItem
            {
                Header = path,
                Icon = new TextBlock
                {
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    Text = "\uE8B7",
                    FontSize = 14
                }
            };
            string capturedPath = path;
            item.Click += (_, _) => _ = LoadDirectoryAsync(capturedPath);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------
    // File operations
    // ------------------------------------------------------------------

    private async void OnUploadClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _browser is null || !_browser.IsConnected)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = _localizer?["SftpBtnUpload"] ?? "Upload"
        };

        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
        {
            return;
        }

        await UploadFilesAsync(dialog.FileNames);
    }

    private async Task UploadFilesAsync(string[] localPaths)
    {
        if (_disposed || _browser is null)
        {
            return;
        }

        _transferCts?.Cancel();
        _transferCts?.Dispose();
        _transferCts = new CancellationTokenSource();
        var ct = _transferCts.Token;

        ShowTransferPanel(true);

        try
        {
            for (int i = 0; i < localPaths.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                string localPath = localPaths[i];
                string fileName = Path.GetFileName(localPath);
                string remotePath = CombineRemotePath(_currentPath, fileName);

                TransferText.Text = _localizer?.Format(
                    "SftpStatusUploadingProgress", fileName,
                    $"{i + 1}", $"{localPaths.Length}") ?? $"Uploading {fileName}...";

                try
                {
                    await _browser.UploadFileAsync(localPath, remotePath, ct);
                }
                catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
                {
                    Core.Logging.FileLogger.Info(
                        $"EmbeddedSFTP upload permission denied, falling back to sudo for {fileName}");
                    await UploadViaSudoAsync(localPath, remotePath, ct);
                }
            }

            UpdateStatus(_localizer?["SftpStatusTransferComplete"] ?? "Transfer complete");
        }
        catch (OperationCanceledException)
        {
            UpdateStatus(_localizer?["SftpStatusTransferCancelled"] ?? "Transfer cancelled");
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"EmbeddedSFTP upload failed [{ex.GetType().Name}]: {ex.Message} (sshParams={(_sshParams is not null ? "present" : "null")})");
            ShowError(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message);
        }
        finally
        {
            ShowTransferPanel(false);
            _ = LoadDirectoryAsync(_currentPath);
        }
    }

    private async void OnCtxDownloadClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedFiles();
        if (selected.Count == 0 || _browser is null)
        {
            return;
        }

        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = _localizer?["SftpBtnDownload"] ?? "Download"
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        _transferCts?.Cancel();
        _transferCts?.Dispose();
        _transferCts = new CancellationTokenSource();
        var ct = _transferCts.Token;

        ShowTransferPanel(true);

        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var file = selected[i];
                if (file.IsDirectory)
                {
                    continue;
                }

                string localPath = Path.Combine(dialog.SelectedPath, file.Name);

                TransferText.Text = _localizer?.Format(
                    "SftpStatusDownloadingFile", file.Name,
                    $"{i + 1}/{selected.Count}") ?? $"Downloading {file.Name}...";

                try
                {
                    await _browser.DownloadFileAsync(file.FullPath, localPath, ct);
                }
                catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
                {
                    Core.Logging.FileLogger.Info(
                        $"EmbeddedSFTP download permission denied, falling back to sudo for {file.Name}");
                    await DownloadViaSudoAsync(file.FullPath, localPath, ct);
                }
            }

            UpdateStatus(_localizer?["SftpStatusTransferComplete"] ?? "Transfer complete");
        }
        catch (OperationCanceledException)
        {
            UpdateStatus(_localizer?["SftpStatusTransferCancelled"] ?? "Transfer cancelled");
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn(
                $"EmbeddedSFTP download failed [{ex.GetType().Name}]: {ex.Message} (sshParams={(_sshParams is not null ? "present" : "null")})");
            ShowError(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message);
        }
        finally
        {
            ShowTransferPanel(false);
        }
    }

    private async void OnNewFolderClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _browser is null || _dialogService is null)
        {
            return;
        }

        string? folderName = await _dialogService.ShowInputAsync(
            _localizer?["SftpNewFolderTitle"] ?? "New Folder",
            _localizer?["SftpNewFolderName"] ?? "Folder name:");

        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        try
        {
            string remotePath = CombineRemotePath(_currentPath, folderName);
            try
            {
                await _browser.CreateDirectoryAsync(remotePath);
            }
            catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
            {
                Core.Logging.FileLogger.Info($"EmbeddedSFTP mkdir permission denied, falling back to sudo");
                await RunSudoCommandAsync($"sudo mkdir -p {Heimdall.Sftp.PathEscaper.EscapeForShell(remotePath)}");
            }
            UpdateStatus(_localizer?["SftpSuccessMkdir"] ?? "Folder created.");
            _ = LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            ShowError(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message);
        }
    }

    private async void OnCtxRenameClick(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not SftpFileInfo file ||
            _browser is null || _dialogService is null)
        {
            return;
        }

        string? newName = await _dialogService.ShowInputAsync(
            _localizer?["SftpBtnRename"] ?? "Rename",
            _localizer?["SftpNewFolderName"] ?? "New name:",
            file.Name);

        if (string.IsNullOrWhiteSpace(newName) ||
            string.Equals(newName, file.Name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            string newPath = CombineRemotePath(_currentPath, newName);
            try
            {
                await _browser.RenameAsync(file.FullPath, newPath);
            }
            catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
            {
                Core.Logging.FileLogger.Info($"EmbeddedSFTP rename permission denied, falling back to sudo");
                await RunSudoCommandAsync(
                    $"sudo mv {Heimdall.Sftp.PathEscaper.EscapeForShell(file.FullPath)} {Heimdall.Sftp.PathEscaper.EscapeForShell(newPath)}");
            }
            UpdateStatus(_localizer?["SftpSuccessRename"] ?? "Renamed.");
            _ = LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            ShowError(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message);
        }
    }

    private async void OnCtxDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedFiles();
        if (selected.Count == 0 || _browser is null || _dialogService is null)
        {
            return;
        }

        string itemName = selected.Count == 1 ? selected[0].Name : $"{selected.Count} items";
        string message = _localizer?.Format("SftpConfirmDelete", itemName)
            ?? $"Delete \"{itemName}\"?";

        bool confirmed = await _dialogService.ShowConfirmAsync(
            _localizer?["SftpConfirmDeleteTitle"] ?? "Confirm Delete",
            message,
            "warning");

        if (!confirmed)
        {
            return;
        }

        try
        {
            foreach (var file in selected)
            {
                try
                {
                    await _browser.DeleteAsync(file.FullPath);
                }
                catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
                {
                    Core.Logging.FileLogger.Info($"EmbeddedSFTP delete permission denied, falling back to sudo for {file.Name}");
                    string flag = file.IsDirectory ? "-rf" : "-f";
                    await RunSudoCommandAsync(
                        $"sudo rm {flag} {Heimdall.Sftp.PathEscaper.EscapeForShell(file.FullPath)}");
                }
            }

            UpdateStatus(_localizer?["SftpSuccessDelete"] ?? "Deleted.");
            _ = LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            ShowError(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Chmod (permissions editor)
    // ------------------------------------------------------------------

    private async void OnCtxChmodClick(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not SftpFileInfo file ||
            _browser is null || _dialogService is null)
        {
            return;
        }

        // Convert rwxrwxrwx to octal for the default value
        string currentOctal = PermissionsToOctal(file.Permissions);

        string? newPerms = await _dialogService.ShowInputAsync(
            _localizer?.Format("SftpChmodTitle", file.Name) ?? $"chmod {file.Name}",
            _localizer?["SftpChmodLabel"] ?? "Permissions (octal, e.g. 755):",
            currentOctal);

        if (string.IsNullOrWhiteSpace(newPerms))
        {
            return;
        }

        if (!int.TryParse(newPerms, System.Globalization.NumberStyles.None, null, out int octal)
            || octal < 0 || octal > 777
            || newPerms.Any(c => c < '0' || c > '7'))
        {
            ShowError(_localizer?["ErrorInvalidOctalPermission"] ?? "Invalid octal permission value.");
            return;
        }

        try
        {
            try
            {
                await _browser.ChmodAsync(file.FullPath, Convert.ToInt16(newPerms, 8));
            }
            catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
            {
                Core.Logging.FileLogger.Info($"EmbeddedSFTP chmod permission denied, falling back to sudo");
                await RunSudoCommandAsync(
                    $"sudo chmod {newPerms} {Heimdall.Sftp.PathEscaper.EscapeForShell(file.FullPath)}");
            }
            UpdateStatus(_localizer?["SftpChmodSuccess"] ?? "Permissions changed.");
            _ = LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            ShowError(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Properties dialog
    // ------------------------------------------------------------------

    private async void OnCtxPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not SftpFileInfo file || _dialogService is null)
        {
            return;
        }

        string type = file.IsDirectory
            ? (_localizer?["SftpPropertiesTypeDirectory"] ?? "Directory")
            : (_localizer?["SftpPropertiesTypeFile"] ?? "File");

        string sizeText = file.IsDirectory ? "-" : FormatSize(file.Size);
        string octal = PermissionsToOctal(file.Permissions);

        string body = $"{_localizer?["SftpPropertiesName"] ?? "Name:"} {file.Name}\n" +
                       $"{_localizer?["SftpPropertiesType"] ?? "Type:"} {type}\n" +
                       $"{_localizer?["SftpPropertiesSize"] ?? "Size:"} {sizeText}\n" +
                       $"{_localizer?["SftpPropertiesModified"] ?? "Modified:"} {file.LastModified:yyyy-MM-dd HH:mm:ss}\n" +
                       $"{_localizer?["SftpPropertiesPermissions"] ?? "Permissions:"} {file.Permissions} ({octal})\n" +
                       $"Owner: {file.Owner}  Group: {file.Group}\n" +
                       $"Path: {file.FullPath}";

        _dialogService.ShowInfo(
            _localizer?.Format("SftpPropertiesTitle", file.Name) ?? $"Properties — {file.Name}",
            body);
    }

    // ------------------------------------------------------------------
    // Context menu actions
    // ------------------------------------------------------------------

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        bool hasSelection = FileListView.SelectedItem is not null;
        bool isFile = FileListView.SelectedItem is SftpFileInfo f && !f.IsDirectory;
        bool isDir = FileListView.SelectedItem is SftpFileInfo d && d.IsDirectory;

        CtxOpen.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        CtxEdit.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        CtxDownload.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        CtxRename.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        CtxDelete.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        CtxChmod.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        CtxCopyPath.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        CtxProperties.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;

        // Always visible
        CtxUploadHere.Visibility = Visibility.Visible;
        CtxOpenInTerminal.Visibility = Visibility.Visible;
    }

    private void OnCtxOpenClick(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is SftpFileInfo file)
        {
            if (file.IsDirectory)
            {
                _ = LoadDirectoryAsync(file.FullPath);
            }
            else
            {
                _ = EditFileAsync(file);
            }
        }
    }

    private void OnCtxEditClick(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is SftpFileInfo file && !file.IsDirectory)
        {
            _ = EditFileAsync(file);
        }
    }

    private void OnCtxEditExternalClick(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is SftpFileInfo file && !file.IsDirectory && _editor is not null)
        {
            _ = EditFileExternalAsync(file);
        }
    }

    private async Task EditFileExternalAsync(SftpFileInfo file)
    {
        if (_disposed || _editor is null)
        {
            return;
        }

        try
        {
            UpdateStatus(_localizer?.Format("SftpStatusEditing", file.Name)
                ?? $"Editing: {file.Name}");

            try
            {
                await _editor.EditFileAsync(file.FullPath);
            }
            catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
            {
                Core.Logging.FileLogger.Info(
                    $"EmbeddedSFTP external edit permission denied, falling back to sudo for {file.Name}");
                await _editor.EditFileSudoAsync(file.FullPath, _sshParams);
            }
        }
        catch (Exception ex)
        {
            ShowError(_localizer?.Format("SftpStatusTransferFailed", ex.Message)
                ?? ex.Message);
        }
    }

    private void OnCtxCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is SftpFileInfo file)
        {
            Clipboard.SetText(file.FullPath);
            UpdateStatus(_localizer?.Format("SftpStatusPathCopied", file.FullPath)
                ?? $"Copied: {file.FullPath}");
        }
    }

    private void OnCtxOpenInTerminalClick(object sender, RoutedEventArgs e)
    {
        string targetDir = _currentPath;
        if (FileListView.SelectedItem is SftpFileInfo file && file.IsDirectory)
        {
            targetDir = file.FullPath;
        }

        OpenInTerminalRequested?.Invoke(targetDir);
    }

    private async Task EditFileAsync(SftpFileInfo file)
    {
        if (_disposed || _browser is null)
        {
            return;
        }

        try
        {
            UpdateStatus(_localizer?.Format("SftpStatusEditing", file.Name)
                ?? $"Editing: {file.Name}");

            // Download file content for embedded editing
            string tempPath = Path.Combine(Path.GetTempPath(), "Heimdall", "edit", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempPath);
            string localPath = Path.Combine(tempPath, Path.GetFileName(file.Name));

            bool useSudo = false;
            try
            {
                await _browser.DownloadFileAsync(file.FullPath, localPath);
            }
            catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
            {
                // Sudo download fallback
                Core.Logging.FileLogger.Info(
                    $"EmbeddedSFTP edit permission denied, falling back to sudo for {file.Name}");
                useSudo = true;

                if (_editor is not null)
                {
                    await _editor.EditFileSudoAsync(file.FullPath, _sshParams);
                    return;
                }
            }

            if (!useSudo)
            {
                string content = await File.ReadAllTextAsync(localPath);
                var remotePath = file.FullPath;

                // Open in embedded AvalonEdit editor
                var editorView = new EmbeddedEditorView(_localizer);
                editorView.OpenContent(file.Name, content);

                // Track whether an upload is in progress to prevent close during save
                bool isSaving = false;

                // Save → upload back to server
                editorView.FileSaved += async (_, savedContent) =>
                {
                    isSaving = true;
                    try
                    {
                        // Write to temp, then upload
                        await File.WriteAllTextAsync(localPath, savedContent);
                        await _browser.UploadFileAsync(localPath, remotePath);
                        UpdateStatus(_localizer?.Format("SftpStatusAutoUploaded", file.Name)
                            ?? $"Uploaded: {file.Name}");
                    }
                    catch (Exception uploadEx)
                    {
                        ShowError(_localizer?.Format("SftpStatusTransferFailed", uploadEx.Message)
                            ?? uploadEx.Message);
                    }
                    finally
                    {
                        isSaving = false;
                    }
                };

                // Close → restore SFTP panel, refresh listing
                var sftpPanel = this;
                var parentTab = _sessionTab;
                editorView.CloseRequested += () =>
                {
                    if (isSaving)
                    {
                        // Upload still in progress; ignore close to avoid deleting temp file
                        return;
                    }

                    if (parentTab is not null)
                    {
                        // If this SFTP view is the secondary in a split, swap back
                        if (parentTab.SecondaryHostControl == editorView)
                        {
                            parentTab.SecondaryHostControl = sftpPanel;
                        }
                    }
                    _ = LoadDirectoryAsync(_currentPath);
                };

                // Replace this SFTP view with the editor in the split pane
                if (_sessionTab is not null && _sessionTab.IsSplit &&
                    _sessionTab.SecondaryHostControl == this)
                {
                    _sessionTab.SecondaryHostControl = editorView;
                }
                else if (_sessionTab is not null && _sessionTab.HostControl == this)
                {
                    // SFTP is the primary — swap to editor
                    _sessionTab.HostControl = editorView;
                    editorView.CloseRequested += () =>
                    {
                        if (isSaving) return;
                        if (_sessionTab.HostControl == editorView)
                        {
                            _sessionTab.HostControl = sftpPanel;
                        }
                    };
                }

                // Cleanup temp on close
                editorView.CloseRequested += () =>
                {
                    if (isSaving) return;
                    try { File.Delete(localPath); Directory.Delete(tempPath); } catch (Exception ex) { Core.Logging.FileLogger.Warn($"[EmbeddedSftpView] temp file cleanup: {ex.Message}"); }
                };
            }
        }
        catch (Exception ex)
        {
            ShowError(_localizer?.Format("SftpStatusEditOpenFailed", ex.Message) ?? ex.Message);
        }
    }

    private static bool IsPermissionDenied(Exception ex)
    {
        // SSH.NET throws SftpPermissionDeniedException for explicit permission errors
        var typeName = ex.GetType().Name;
        if (typeName.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase))
            return true;

        // Exclude path-not-found (not a permission issue)
        if (typeName.Contains("PathNotFound", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("NoSuchFile", StringComparison.OrdinalIgnoreCase))
            return false;

        string msg = ex.Message + (ex.InnerException?.Message ?? "");

        // Exact permission error strings
        if (msg.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("not permitted", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("SSH_FX_PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
            return true;

        // SSH.NET surfaces SSH_FX_FAILURE as SshException("Failure") or
        // SftpStatusException("Failure") when the server doesn't distinguish
        // SSH_FX_PERMISSION_DENIED (3) from SSH_FX_FAILURE (4).
        // This is the most common pattern for root-owned file operations.
        if (msg.Contains("Failure", StringComparison.Ordinal)
            && (typeName.Contains("Sftp", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("Ssh", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    /// <summary>
    /// Creates an authenticated SSH client using the same connection factory
    /// and host key verification as the main session (Pageant, keys, TOFU).
    /// </summary>
    private async Task<Renci.SshNet.SshClient> CreateSudoSshClientAsync(CancellationToken ct = default)
    {
        if (_sshParams is null)
            throw new InvalidOperationException("SSH params not available for sudo.");

        var connInfo = Heimdall.Ssh.SshConnectionFactory.Create(_sshParams);
        var ssh = new Renci.SshNet.SshClient(connInfo);

        if (_hostKeyStore is not null)
        {
            Heimdall.Ssh.SshConnectionFactory.AttachHostKeyVerification(
                ssh, _sshParams.Host, _sshParams.Port, _hostKeyStore);
        }

        await Task.Run(() => { ct.ThrowIfCancellationRequested(); ssh.Connect(); }, ct)
            .ConfigureAwait(false);

        return ssh;
    }

    private void OnSudoModeToggle(object sender, RoutedEventArgs e)
    {
        _sudoMode = BtnSudoMode.IsChecked == true;

        if (_sudoMode)
        {
            BtnSudoModeText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            UpdateStatus(_localizer?["SftpSudoModeEnabled"] ?? "Sudo mode enabled — browsing as root");
        }
        else
        {
            BtnSudoModeText.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
            UpdateStatus(_localizer?["SftpSudoModeDisabled"] ?? "Sudo mode disabled");
        }

        // Reload current directory with new mode
        _ = LoadDirectoryAsync(_currentPath);
    }

    /// <summary>
    /// Lists a directory via <c>sudo ls -la</c> over SSH exec channel,
    /// bypassing SFTP permission restrictions for root-owned directories.
    /// </summary>
    private async Task<IReadOnlyList<SftpFileInfo>> ListDirectoryViaSudoAsync(string path)
    {
        var escaped = Heimdall.Sftp.PathEscaper.EscapeForShell(path);
        using var ssh = await CreateSudoSshClientAsync();

        try
        {
            // -la = long format + hidden files, --time-style=long-iso for consistent date format
            using var cmd = await Task.Run(() =>
                ssh.RunCommand($"sudo ls -la --time-style=long-iso {escaped}"))
                .ConfigureAwait(false);

            if (cmd.ExitStatus != 0)
                throw new InvalidOperationException($"sudo ls failed (exit {cmd.ExitStatus}): {cmd.Error}");

            return ParseLsOutput(cmd.Result ?? "", path);
        }
        finally
        {
            ssh.Disconnect();
        }
    }

    /// <summary>
    /// Parses the output of <c>ls -la --time-style=long-iso</c> into SftpFileInfo records.
    /// Format: <c>drwxr-xr-x 2 root root 4096 2026-03-18 14:30 dirname</c>
    /// </summary>
    private static IReadOnlyList<SftpFileInfo> ParseLsOutput(string output, string parentPath)
    {
        var results = new List<SftpFileInfo>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // Skip the "total NNN" header line
            if (line.StartsWith("total ", StringComparison.Ordinal))
                continue;

            // Format: drwxr-xr-x 2 root root 4096 2026-03-18 14:30 dirname
            // Columns: permissions links owner group size date time name...
            // Split into max 8 parts so the filename (which may contain spaces) stays intact
            var parts = line.Split((char[]?)null, 8, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8)
                continue;

            string permissions = parts[0];
            // Skip entries that don't start with a permission char (d, -, l, c, b, p, s)
            if (permissions.Length < 2 || !"dl-cbps".Contains(permissions[0]))
                continue;

            string owner = parts[2];
            string group = parts[3];
            long.TryParse(parts[4], out long size);

            // Parse date+time: "2026-03-18 14:30"
            DateTime lastModified = DateTime.MinValue;
            DateTime.TryParse($"{parts[5]} {parts[6]}", out lastModified);

            // Name is everything after the 7th column (handles spaces in filenames)
            string name = parts[7];

            // Skip . and ..
            if (name is "." or "..")
                continue;

            // Handle symlinks: "name -> target"
            int arrowIdx = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIdx >= 0)
                name = name[..arrowIdx];

            bool isDirectory = permissions.Length > 0 && permissions[0] == 'd';
            string fullPath = parentPath.EndsWith('/')
                ? $"{parentPath}{name}"
                : $"{parentPath}/{name}";

            results.Add(new SftpFileInfo(
                name, fullPath, isDirectory, size, lastModified,
                permissions, owner, group));
        }

        return results;
    }

    /// <summary>
    /// Executes a single sudo command over SSH (chmod, mv, rm, mkdir, etc.).
    /// </summary>
    private async Task RunSudoCommandAsync(string command, CancellationToken ct = default)
    {
        using var ssh = await CreateSudoSshClientAsync(ct);
        try
        {
            using var cmd = await Task.Run(() => ssh.RunCommand(command), ct).ConfigureAwait(false);
            if (cmd.ExitStatus != 0)
                throw new InvalidOperationException($"Command failed (exit {cmd.ExitStatus}): {cmd.Error}");
        }
        finally
        {
            ssh.Disconnect();
        }
    }

    /// <summary>
    /// Downloads a file via <c>sudo cat</c> over a direct SSH exec channel,
    /// bypassing SFTP permission restrictions.
    /// </summary>
    private async Task DownloadViaSudoAsync(string remotePath, string localPath, CancellationToken ct)
    {
        var escaped = Heimdall.Sftp.PathEscaper.EscapeForShell(remotePath);
        using var ssh = await CreateSudoSshClientAsync(ct);

        try
        {
            using var cmd = await Task.Run(() => ssh.RunCommand($"sudo cat {escaped}"), ct)
                .ConfigureAwait(false);

            if (cmd.ExitStatus != 0)
                throw new InvalidOperationException($"sudo cat failed (exit {cmd.ExitStatus}): {cmd.Error}");

            await File.WriteAllTextAsync(localPath, cmd.Result ?? "", System.Text.Encoding.UTF8, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            ssh.Disconnect();
        }
    }

    /// <summary>
    /// Uploads a file via SFTP to a temp path, then moves it to the target
    /// location using <c>sudo tee</c> over SSH.
    /// </summary>
    private async Task UploadViaSudoAsync(string localPath, string remotePath, CancellationToken ct)
    {
        if (_browser is null)
            throw new InvalidOperationException("Browser not available for sudo upload.");

        var escaped = Heimdall.Sftp.PathEscaper.EscapeForShell(remotePath);
        string tempRemote = $"{RemoteTempPrefix}upload_{Guid.NewGuid():N}";

        // Upload to temp path (writable by current user)
        await _browser.UploadFileAsync(localPath, tempRemote, ct).ConfigureAwait(false);

        // Move to final location via sudo
        using var ssh = await CreateSudoSshClientAsync(ct);
        try
        {
            var escapedTemp = Heimdall.Sftp.PathEscaper.EscapeForShell(tempRemote);
            using var cmd = await Task.Run(() =>
                ssh.RunCommand($"cat {escapedTemp} | sudo tee -- {escaped} > /dev/null && sudo rm -f {escapedTemp}"),
                ct).ConfigureAwait(false);

            if (cmd.ExitStatus != 0)
                throw new InvalidOperationException($"sudo tee failed (exit {cmd.ExitStatus}): {cmd.Error}");
        }
        finally
        {
            ssh.Disconnect();
        }
    }

    // ------------------------------------------------------------------
    // Drag and drop
    // ------------------------------------------------------------------

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (_disposed || _browser is null || !_browser.IsConnected)
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        bool hasFiles = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop);
        e.Effects = hasFiles
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;

        if (hasFiles)
        {
            DragOverlay.Visibility = Visibility.Visible;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
    }

    private async void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;

        if (_disposed || _browser is null || !_browser.IsConnected)
        {
            return;
        }

        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return;
        }

        var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        if (paths is null || paths.Length == 0)
        {
            return;
        }

        var files = paths.Where(p => File.Exists(p)).ToArray();
        int folderCount = paths.Length - files.Length;

        if (folderCount > 0 && _dialogService is not null)
        {
            string msg = _localizer?.Format("SftpDropFoldersSkipped", folderCount.ToString())
                ?? $"{folderCount} folder(s) will be skipped. Continue?";

            if (files.Length == 0)
            {
                UpdateStatus(_localizer?["SftpStatusFoldersNotSupported"]
                    ?? "Folder upload not supported.");
                return;
            }

            bool proceed = await _dialogService.ShowConfirmAsync(
                _localizer?["SftpDropFoldersTitle"] ?? "Folders detected",
                msg);

            if (!proceed)
            {
                return;
            }
        }

        if (files.Length > 0)
        {
            await UploadFilesAsync(files);
        }
    }

    // ------------------------------------------------------------------
    // Transfer progress
    // ------------------------------------------------------------------

    private void OnTransferProgress(SftpTransferProgress progress)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            double percent = progress.TotalBytes > 0
                ? (double)progress.BytesTransferred / progress.TotalBytes * 100
                : 0;

            TransferProgressBar.Value = percent;

            string transferred = FormatSize(progress.BytesTransferred);
            string total = FormatSize(progress.TotalBytes);
            string direction = progress.IsUpload ? "\u2191" : "\u2193";
            TransferText.Text = $"{direction} {progress.FileName} — {transferred} / {total} ({percent:F0}%)";
        });
    }

    private void OnCancelTransferClick(object sender, RoutedEventArgs e)
    {
        _transferCts?.Cancel();
    }

    private void ShowTransferPanel(bool visible)
    {
        TransferPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        TransferProgressBar.Value = 0;
    }

    // ------------------------------------------------------------------
    // Connection lifecycle
    // ------------------------------------------------------------------

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Core.Logging.FileLogger.Info("EmbeddedSFTP Disconnect requested by user");
        UpdateStatus(_localizer?["SftpStatusDisconnected"] ?? "Disconnected");

        try
        {
            _browser?.Disconnect();
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"EmbeddedSFTP manual disconnect failed: {ex.Message}");
        }

        ShowDisconnectedState();
    }

    private void OnSplitClick(object sender, RoutedEventArgs e)
    {
        SplitRequested?.Invoke();
    }

    private async void OnReconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _sshParams is null)
        {
            return;
        }

        UpdateStatus(_localizer?["SftpStatusReconnecting"] ?? "Reconnecting...");

        try
        {
            // Detach event handlers from the old browser instance
            if (_browser is not null)
            {
                _browser.DirectoryChanged -= OnDirectoryChanged;
                _browser.TransferProgress -= OnTransferProgress;
                _browser.Disconnected -= OnBrowserDisconnected;
                StopHealthTimer();

                try { _browser.Dispose(); }
                catch (ObjectDisposedException) { /* already disposed */ }
            }

            // Create a fresh browser and reconnect
            var newBrowser = new SftpBrowser();
            await newBrowser.ConnectAsync(_sshParams);

            _browser = newBrowser;
            _browser.DirectoryChanged += OnDirectoryChanged;
            _browser.TransferProgress += OnTransferProgress;
            _browser.Disconnected += OnBrowserDisconnected;

            // Recreate editor with new browser
            if (_editor is not null)
            {
                _editor.FileUploaded -= OnEditorFileUploaded;
                _editor.Dispose();
            }
            _editor = new RemoteFileEditor(newBrowser);
            _editor.FileUploaded += OnEditorFileUploaded;

            // Restore connected UI state
            DisconnectButton.Visibility = Visibility.Visible;
            ReconnectButton.Visibility = Visibility.Collapsed;
            SetToolbarEnabled(true);
            StartHealthTimer();

            UpdateStatus(_localizer?["SftpStatusConnected"] ?? "Connected");
            _ = LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error($"EmbeddedSFTP reconnection failed: {ex.Message}");
            ShowError(_localizer?.Format("SftpStatusTransferFailed", ex.Message)
                ?? $"Reconnection failed: {ex.Message}");
        }
    }

    private void OnBrowserDisconnected(string? errorMessage)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            string status = string.IsNullOrWhiteSpace(errorMessage)
                ? (_localizer?["SftpStatusDisconnected"] ?? "Disconnected")
                : (_localizer?.Format("SftpErrorSessionDied", errorMessage)
                    ?? $"Session lost: {errorMessage}");

            UpdateStatus(status);
            ShowDisconnectedState();
        });
    }

    private void ShowDisconnectedState()
    {
        SetToolbarEnabled(false);
        DisconnectButton.Visibility = Visibility.Collapsed;
        ReconnectButton.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------
    // Health check
    // ------------------------------------------------------------------

    private void StartHealthTimer()
    {
        _healthTimer = new System.Threading.Timer(
            _ => CheckHealth(), null,
            SftpOperationTimeout,
            SftpOperationTimeout);
    }

    private void StopHealthTimer()
    {
        _healthTimer?.Dispose();
        _healthTimer = null;
    }

    private void CheckHealth()
    {
        if (_disposed || _browser is null)
        {
            return;
        }

        if (!_browser.IsConnected)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed)
                {
                    UpdateStatus(_localizer?["SftpStatusHealthCheckFailed"] ?? "Connection lost");
                    ShowDisconnectedState();
                }
            });

            StopHealthTimer();
        }
    }

    // ------------------------------------------------------------------
    // Editor auto-upload callback
    // ------------------------------------------------------------------

    private void OnEditorFileUploaded(string remotePath, bool success)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            string fileName = Path.GetFileName(remotePath);

            if (success)
            {
                UpdateStatus(_localizer?.Format("SftpStatusAutoUploaded", fileName)
                    ?? $"Auto-uploaded: {fileName}");
            }
            else
            {
                ShowError(_localizer?.Format("SftpStatusAutoUploadFailed", fileName, "upload error")
                    ?? $"Auto-upload failed: {fileName}");
            }
        });
    }

    // ------------------------------------------------------------------
    // UI helpers
    // ------------------------------------------------------------------

    private void UpdateStatus(string text)
    {
        bool connected = _browser?.IsConnected == true;

        if (_sessionTab is not null)
        {
            _sessionTab.Status = connected ? "Connected" : "Disconnected";
        }

        StatusTextBlock.Text = text;
        StatusBarText.Text = text;
        HealthDot.Fill = GetBrush(
            connected ? "SuccessBrush" : "ErrorBrush",
            connected ? Brushes.Green : Brushes.Red);
    }

    private void ShowError(string message)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = GetBrush("ErrorBrush", Brushes.IndianRed);
        StatusBarText.Text = message;

        // Dispose previous timer to prevent leaks
        _errorResetTimer?.Dispose();
        _errorResetTimer = new System.Threading.Timer(_ =>
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                StatusTextBlock.Foreground = GetBrush("TextPrimaryBrush", Brushes.White);
            });
        }, null, StatusResetDelay, Timeout.InfiniteTimeSpan);
    }

    private void SetToolbarEnabled(bool enabled)
    {
        BtnBack.IsEnabled = enabled && _navigationHistory.Count > 0;
        BtnUp.IsEnabled = enabled;
        BtnHome.IsEnabled = enabled;
        BtnRefresh.IsEnabled = enabled;
        BtnUpload.IsEnabled = enabled;
        BtnNewFolder.IsEnabled = enabled;
        BtnGoPath.IsEnabled = enabled;
        BtnBookmark.IsEnabled = enabled;
        PathTextBox.IsEnabled = enabled;
    }

    private List<SftpFileInfo> GetSelectedFiles()
    {
        return FileListView.SelectedItems.Cast<SftpFileInfo>().ToList();
    }

    private Brush GetBrush(string resourceKey, Brush fallback)
    {
        return TryFindResource(resourceKey) as Brush ?? fallback;
    }

    private static string GetParentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        string trimmed = path.TrimEnd('/');
        int lastSlash = trimmed.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : trimmed[..lastSlash];
    }

    private static string CombineRemotePath(string directory, string name)
    {
        return $"{directory.TrimEnd('/')}/{name}";
    }

    private static string PermissionsToOctal(string perms)
    {
        if (string.IsNullOrEmpty(perms) || perms.Length != 9)
        {
            return "000";
        }

        static int TriadToDigit(char r, char w, char x) =>
            (r != '-' ? 4 : 0) + (w != '-' ? 2 : 0) + (x != '-' ? 1 : 0);

        int owner = TriadToDigit(perms[0], perms[1], perms[2]);
        int group = TriadToDigit(perms[3], perms[4], perms[5]);
        int other = TriadToDigit(perms[6], perms[7], perms[8]);

        return $"{owner}{group}{other}";
    }

    private static string FormatSize(long bytes) => FileSize.Format(bytes);
}
