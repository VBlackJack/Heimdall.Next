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
using Microsoft.Extensions.DependencyInjection;
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
    private readonly EmbeddedSftpViewModel _viewModel;

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

    private bool _disposed;

    /// <summary>
    /// Raised when the user clicks the Split button in the header strip.
    /// The subscriber (EmbeddedSessionManager) shows the split picker context menu.
    /// </summary>
    public event Action? SplitRequested
    {
        add => _viewModel.SplitRequested += value;
        remove => _viewModel.SplitRequested -= value;
    }

    /// <summary>
    /// Raised when the user selects "Open in terminal" from the context menu.
    /// The parameter is the remote directory path to cd into.
    /// </summary>
    public event Action<string>? OpenInTerminalRequested
    {
        add => _viewModel.OpenInTerminalRequested += value;
        remove => _viewModel.OpenInTerminalRequested -= value;
    }

    public EmbeddedSftpView()
    {
        InitializeComponent();
        var services = (Application.Current as App)?.Services;
        var uiDispatcher = services?.GetRequiredService<IUiDispatcher>()
            ?? throw new InvalidOperationException("IUiDispatcher is not registered.");
        _viewModel = new EmbeddedSftpViewModel(uiDispatcher);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
        _viewModel.Initialize(
            browser,
            sessionTab,
            displayName,
            endpoint,
            localizer,
            dialogService,
            sshParams,
            hostKeyStore);

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

        ApplyLocalization();
        UpdateStatus(_localizer["SftpStatusConnected"]);
        StartHealthTimer();

        _ = NavigateRemoteAsync(browser.CurrentDirectory);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.MarkDisposed();

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

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

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
        BtnUploadText.Text = _localizer["SftpBtnUpload"];
        BtnNewFolderText.Text = _localizer["SftpBtnNewFolder"];
        BtnCancelTransfer.ToolTip = _localizer["TooltipCancelTransfer"];
        System.Windows.Automation.AutomationProperties.SetName(BtnCancelTransfer, _localizer["A11yCancelTransfer"]);
        BtnBookmarkMenu.ToolTip = _localizer["SftpBtnBookmark"];
        MnuBookmarkAdd.Header = _localizer["SftpBtnBookmark"];
        MnuBookmarkManage.Header = _localizer["SftpBtnBookmarks"];
        ToggleHiddenCheckBox.ToolTip = _localizer["SftpToggleHidden"];
        BtnSudoMode.ToolTip = _localizer["SftpSudoModeTooltip"];
        BtnSudoModeText.Text = _localizer["SftpSudoModeLabel"];

        // Tooltips
        BtnBack.ToolTip = _localizer["TooltipNavigateBack"];
        BtnUp.ToolTip = _localizer["TooltipNavigateUp"];
        BtnHome.ToolTip = _localizer["TooltipNavigateHome"];
        BtnRefresh.ToolTip = _localizer["TooltipRefreshDirectory"];
        BtnUpload.ToolTip = _localizer["TooltipUploadFiles"];
        BtnNewFolder.ToolTip = _localizer["TooltipCreateFolder"];
        DisconnectButton.ToolTip = _localizer["TooltipDisconnectSession"];
        ReconnectButton.ToolTip = _localizer["TooltipReconnectSession"];
        SplitButton.ToolTip = _localizer["TooltipSplitSession"];

        BtnGoPath.ToolTip = _localizer["SftpBtnGoPath"];
        System.Windows.Automation.AutomationProperties.SetName(BtnGoPath, _localizer["SftpBtnGoPath"]);

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
        System.Windows.Automation.AutomationProperties.SetName(BtnBack, _localizer["A11yNavigateBack"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnUp, _localizer["A11yNavigateUp"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnHome, _localizer["A11yNavigateHome"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnRefresh, _localizer["A11yRefreshDirectory"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnUpload, _localizer["A11yUploadFiles"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnNewFolder, _localizer["A11yCreateFolder"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnBookmarkMenu, _localizer["SftpBtnBookmark"]);
        System.Windows.Automation.AutomationProperties.SetName(BtnSudoMode, _localizer["SftpSudoModeTooltip"]);
        System.Windows.Automation.AutomationProperties.SetName(DisconnectButton, _localizer["A11yDisconnectSession"]);
        System.Windows.Automation.AutomationProperties.SetName(ReconnectButton, _localizer["A11yReconnectSession"]);
        System.Windows.Automation.AutomationProperties.SetName(SplitButton, _localizer["A11ySplitSession"]);
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
                    if (!_viewModel.HandleFileDoubleClick(enterFile))
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

        _viewModel.ToggleSortColumn(columnName);
        UpdateColumnHeaders();
    }

    private void UpdateColumnHeaders()
    {
        if (FileListView?.View is not GridView gridView)
        {
            return;
        }

        string[] names = ["Name", "Size", "Modified", "Permissions", "Owner"];
        string arrow = _viewModel.SortDirection == System.ComponentModel.ListSortDirection.Ascending
            ? " \u25B2"
            : " \u25BC";

        for (int i = 0; i < gridView.Columns.Count && i < names.Length; i++)
        {
            string baseName = _localizer?[$"SftpCol{names[i]}"] ?? names[i];
            gridView.Columns[i].Header = string.Equals(names[i], _viewModel.SortColumn, StringComparison.Ordinal)
                ? baseName + arrow
                : baseName;
        }
    }

    // ------------------------------------------------------------------
    // Selection info
    // ------------------------------------------------------------------

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.UpdateSelectionInfo(GetSelectedFiles());
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
                _viewModel.CurrentPath = newPath;
            }
        });
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
            _ = _viewModel.NavigateToPath(path);
        }
    }

    private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is not SftpFileInfo file)
        {
            return;
        }

        if (!_viewModel.HandleFileDoubleClick(file))
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

    private void OnBookmarkMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void OnBookmarkClick(object sender, RoutedEventArgs e)
    {
        _viewModel.AddBookmark();
    }

    private void OnBookmarksClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Bookmarks.Count == 0)
        {
            UpdateStatus(_localizer?["SftpBookmarkEmpty"] ?? "No bookmarks");
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = BtnBookmarkMenu,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        foreach (string path in _viewModel.Bookmarks)
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
            item.Click += (_, _) => _ = NavigateRemoteAsync(capturedPath);
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
                string remotePath = EmbeddedSftpViewModel.CombineRemotePath(_viewModel.CurrentPath, fileName);

                TransferText.Text = _localizer?.Format(
                    "SftpStatusUploadingProgress", fileName,
                    $"{i + 1}", $"{localPaths.Length}") ?? $"Uploading {fileName}...";

                try
                {
                    await _browser.UploadFileAsync(localPath, remotePath, ct);
                }
                catch (Exception ex) when (_sshParams is not null && EmbeddedSftpViewModel.IsPermissionDenied(ex))
                {
                    Core.Logging.FileLogger.Info(
                        $"EmbeddedSFTP upload permission denied, falling back to sudo for {fileName}");
                    await _viewModel.UploadViaSudoAsync(localPath, remotePath, ct);
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
            _ = RefreshRemoteAsync();
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
                catch (Exception ex) when (_sshParams is not null && EmbeddedSftpViewModel.IsPermissionDenied(ex))
                {
                    Core.Logging.FileLogger.Info(
                        $"EmbeddedSFTP download permission denied, falling back to sudo for {file.Name}");
                    await _viewModel.DownloadViaSudoAsync(file.FullPath, localPath, ct);
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
        await _viewModel.CreateFolderAsync();
    }

    private async void OnCtxRenameClick(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is SftpFileInfo file)
        {
            await _viewModel.RenameEntryAsync(file);
        }
    }

    private async void OnCtxDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedFiles();
        if (selected.Count > 0)
        {
            await _viewModel.DeleteEntriesAsync(selected);
        }
    }

    // ------------------------------------------------------------------
    // Chmod (permissions editor)
    // ------------------------------------------------------------------

    private async void OnCtxChmodClick(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is SftpFileInfo file)
        {
            await _viewModel.ChmodAsync(file);
        }
    }

    // ------------------------------------------------------------------
    // Properties dialog
    // ------------------------------------------------------------------

    private void OnCtxPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is SftpFileInfo file)
        {
            _viewModel.ShowProperties(file);
        }
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
                _ = NavigateRemoteAsync(file.FullPath);
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
            catch (Exception ex) when (_sshParams is not null && EmbeddedSftpViewModel.IsPermissionDenied(ex))
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
        string targetDir = _viewModel.CurrentPath;
        if (FileListView.SelectedItem is SftpFileInfo file && file.IsDirectory)
        {
            targetDir = file.FullPath;
        }

        _viewModel.RequestOpenInTerminal(targetDir);
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
            catch (Exception ex) when (_sshParams is not null && EmbeddedSftpViewModel.IsPermissionDenied(ex))
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
                        // Find the pane containing the editor and swap back to SFTP
                        var editorPane = Heimdall.Core.Models.SplitTreeHelper.FindPaneByHostControl(
                            parentTab.RootContent, editorView);
                        if (editorPane is not null)
                        {
                            editorPane.HostControl = sftpPanel;
                        }
                    }
                    _ = RefreshRemoteAsync();
                };

                // Replace this SFTP view with the editor in the pane that contains it
                if (_sessionTab is not null)
                {
                    var sftpPane = Heimdall.Core.Models.SplitTreeHelper.FindPaneByHostControl(
                        _sessionTab.RootContent, this);
                    if (sftpPane is not null)
                    {
                        sftpPane.HostControl = editorView;
                    }
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

    private void OnSudoModeToggle(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.ToggleSudoMode();
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

            string transferred = EmbeddedSftpViewModel.FormatSize(progress.BytesTransferred);
            string total = EmbeddedSftpViewModel.FormatSize(progress.TotalBytes);
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
        _viewModel.RequestSplit();
    }

    private async void OnReconnectClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _sshParams is null)
        {
            return;
        }

        string reconnectPath = _viewModel.CurrentPath;
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
            _viewModel.Initialize(
                newBrowser,
                _sessionTab ?? throw new InvalidOperationException("Session tab not available."),
                SessionTitleText.Text,
                EndpointTextBlock.Text,
                _localizer ?? throw new InvalidOperationException("Localizer not available."),
                _dialogService ?? throw new InvalidOperationException("Dialog service not available."),
                _sshParams,
                _hostKeyStore);
            _viewModel.CurrentPath = reconnectPath;

            // Restore connected UI state
            DisconnectButton.Visibility = Visibility.Visible;
            ReconnectButton.Visibility = Visibility.Collapsed;
            SetToolbarEnabled(true);
            StartHealthTimer();

            UpdateStatus(_localizer?["SftpStatusConnected"] ?? "Connected");
            _ = RefreshRemoteAsync();
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
        _viewModel.UpdateStatus(text);
        UpdateHealthDot();
    }

    private void ShowError(string message)
    {
        _viewModel.SetErrorStatus(message);
    }

    private void SetToolbarEnabled(bool enabled)
    {
        BtnBack.IsEnabled = enabled && _viewModel.CanGoBack;
        BtnUp.IsEnabled = enabled;
        BtnHome.IsEnabled = enabled;
        BtnRefresh.IsEnabled = enabled;
        BtnUpload.IsEnabled = enabled;
        BtnNewFolder.IsEnabled = enabled;
        BtnGoPath.IsEnabled = enabled;
        BtnBookmarkMenu.IsEnabled = enabled;
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

    private Task NavigateRemoteAsync(string path)
    {
        return _viewModel.NavigateToPath(path);
    }

    private Task RefreshRemoteAsync()
    {
        return _viewModel.Refresh();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(EmbeddedSftpViewModel.IsLoading):
                case nameof(EmbeddedSftpViewModel.CanGoBack):
                case nameof(EmbeddedSftpViewModel.IsConnected):
                    SetToolbarEnabled(!_viewModel.IsLoading && _viewModel.IsConnected);
                    UpdateHealthDot();
                    break;
                case nameof(EmbeddedSftpViewModel.SudoMode):
                    UpdateSudoModeVisuals();
                    break;
                case nameof(EmbeddedSftpViewModel.SortColumn):
                case nameof(EmbeddedSftpViewModel.SortDirection):
                    UpdateColumnHeaders();
                    break;
                case nameof(EmbeddedSftpViewModel.IsErrorStatus):
                    if (_viewModel.IsErrorStatus)
                    {
                        StatusTextBlock.Foreground = GetBrush("ErrorBrush", Brushes.IndianRed);
                        _errorResetTimer?.Dispose();
                        _errorResetTimer = new System.Threading.Timer(_ =>
                        {
                            _ = Dispatcher.BeginInvoke(() =>
                            {
                                StatusTextBlock.Foreground = GetBrush("TextPrimaryBrush", Brushes.White);
                            });
                        }, null, StatusResetDelay, Timeout.InfiniteTimeSpan);
                    }
                    else
                    {
                        StatusTextBlock.Foreground = GetBrush("TextPrimaryBrush", Brushes.White);
                    }
                    break;
            }
        });
    }

    private void UpdateHealthDot()
    {
        HealthDot.Fill = GetBrush(
            _viewModel.IsConnected ? "SuccessBrush" : "ErrorBrush",
            _viewModel.IsConnected ? Brushes.Green : Brushes.Red);
    }

    private void UpdateSudoModeVisuals()
    {
        BtnSudoMode.IsChecked = _viewModel.SudoMode;
        BtnSudoModeText.Foreground = _viewModel.SudoMode
            ? Brushes.OrangeRed
            : GetBrush("TextPrimaryBrush", Brushes.White);
    }
}
