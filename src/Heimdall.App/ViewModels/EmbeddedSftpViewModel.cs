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
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Heimdall.App.Services;
using Heimdall.Core.Localization;
using Heimdall.Core.Utilities;
using Heimdall.Sftp;
using Heimdall.Ssh;

namespace Heimdall.App.ViewModels;

/// <summary>
/// ViewModel for the embedded SFTP/FTP file browser. Manages directory state,
/// navigation history, listing, filtering, sorting, and status display.
/// File operations, transfers, and connection lifecycle remain in the code-behind.
/// </summary>
public sealed partial class EmbeddedSftpViewModel : ObservableObject
{
    private const string RemoteTempPrefix = "/tmp/.heimdall_";
    private readonly Stack<string> _navigationHistory = new();
    private readonly Dispatcher _uiDispatcher;
    private IRemoteBrowser? _browser;
    private SshConnectionParams? _sshParams;
    private HostKeyStore? _hostKeyStore;
    private LocalizationManager? _localizer;
    private IDialogService? _dialogService;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbeddedSftpViewModel"/> class.
    /// </summary>
    public EmbeddedSftpViewModel()
    {
        _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Files = [];
        Bookmarks = [];
        UnfilteredEntries = [];
        CurrentPath = "/";
        HomeDirectory = "/";
        SortColumn = "Name";
        SortDirection = ListSortDirection.Ascending;
        ShowHidden = true;
    }

    /// <summary>The current remote directory path.</summary>
    [ObservableProperty]
    private string _currentPath = "/";

    /// <summary>Whether backward navigation is available.</summary>
    [ObservableProperty]
    private bool _canGoBack;

    /// <summary>Whether a remote directory listing is currently running.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>The current status text displayed by the view.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Whether the current status represents an error (for view-side styling).</summary>
    [ObservableProperty]
    private bool _isErrorStatus;

    /// <summary>Whether hidden entries should be shown.</summary>
    [ObservableProperty]
    private bool _showHidden = true;

    /// <summary>Whether sudo directory listing mode is enabled.</summary>
    [ObservableProperty]
    private bool _sudoMode;

    /// <summary>The active sort column name.</summary>
    [ObservableProperty]
    private string _sortColumn = "Name";

    /// <summary>The active sort direction.</summary>
    [ObservableProperty]
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    /// <summary>The text shown in the item counter area.</summary>
    [ObservableProperty]
    private string _itemCountText = string.Empty;

    /// <summary>The text shown for the current selection summary.</summary>
    [ObservableProperty]
    private string _selectionInfoText = string.Empty;

    /// <summary>Whether the empty-directory overlay should be visible.</summary>
    [ObservableProperty]
    private bool _showEmptyDirectory;

    /// <summary>Whether the remote browser is currently connected.</summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>The current text filter applied to file names.</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>The currently visible remote entries.</summary>
    public ObservableCollection<SftpFileInfo> Files { get; }

    /// <summary>The remote home directory captured during initialization.</summary>
    public string HomeDirectory { get; private set; }

    /// <summary>The owning session tab for status synchronization.</summary>
    public SessionTabViewModel? SessionTab { get; private set; }

    /// <summary>Bookmarks associated with the remote browser.</summary>
    public List<string> Bookmarks { get; }

    /// <summary>The full unfiltered listing for the current directory.</summary>
    public List<SftpFileInfo> UnfilteredEntries { get; internal set; }

    /// <summary>
    /// Raised when the user requests a split action from the embedded view.
    /// </summary>
    public event Action? SplitRequested;

    /// <summary>
    /// Raised when the user requests opening a path in the terminal.
    /// </summary>
    public event Action<string>? OpenInTerminalRequested;

    /// <summary>
    /// Stores session-scoped dependencies used by the view model.
    /// </summary>
    public void Initialize(
        IRemoteBrowser browser,
        SessionTabViewModel sessionTab,
        string displayName,
        string endpoint,
        LocalizationManager localizer,
        IDialogService dialogService,
        SshConnectionParams? sshParams = null,
        HostKeyStore? hostKeyStore = null)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(sessionTab);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(dialogService);

        bool firstInitialization = _browser is null;
        _browser = browser;
        SessionTab = sessionTab;
        _localizer = localizer;
        _dialogService = dialogService;
        _sshParams = sshParams;
        _hostKeyStore = hostKeyStore;

        if (firstInitialization)
        {
            HomeDirectory = browser.CurrentDirectory;
            CurrentPath = browser.CurrentDirectory;
        }

        IsConnected = true;
        UpdateStatus(_localizer["SftpStatusConnected"]);
    }

    /// <summary>
    /// Updates the dialog service instance used by the view model.
    /// </summary>
    internal void SetDialogService(IDialogService? dialogService)
    {
        _dialogService = dialogService;
    }

    /// <summary>
    /// Marks the view model as disposed so future async operations short-circuit.
    /// </summary>
    internal void MarkDisposed()
    {
        _disposed = true;
        IsConnected = false;
    }

    /// <summary>
    /// Loads a remote directory listing and updates the filtered file view.
    /// </summary>
    public Task LoadDirectoryAsync(string path)
    {
        return LoadDirectoryCoreAsync(path, pushToHistory: true);
    }

    /// <summary>
    /// Applies the current hidden-file, text filter, and sort settings.
    /// </summary>
    public void ApplyFilterAndSort()
    {
        IEnumerable<SftpFileInfo> filtered = UnfilteredEntries;

        if (!ShowHidden)
        {
            filtered = filtered.Where(f => !f.Name.StartsWith('.'));
        }

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            filtered = filtered.Where(f =>
                f.Name.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        IEnumerable<SftpFileInfo> sorted = SortColumn switch
        {
            "Size" => SortDirection == ListSortDirection.Ascending
                ? filtered.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Size)
                : filtered.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Size),
            "Modified" => SortDirection == ListSortDirection.Ascending
                ? filtered.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.LastModified)
                : filtered.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.LastModified),
            "Permissions" => SortDirection == ListSortDirection.Ascending
                ? filtered.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Permissions)
                : filtered.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Permissions),
            "Owner" => SortDirection == ListSortDirection.Ascending
                ? filtered.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Owner)
                : filtered.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Owner),
            _ => SortDirection == ListSortDirection.Ascending
                ? filtered.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase),
        };

        Files.Clear();
        foreach (var entry in sorted)
        {
            Files.Add(entry);
        }

        ShowEmptyDirectory = Files.Count == 0;

        int totalCount = UnfilteredEntries.Count;
        int visibleCount = Files.Count;
        ItemCountText = visibleCount == totalCount
            ? $"{totalCount} items"
            : $"{visibleCount}/{totalCount} items";
    }

    /// <summary>
    /// Navigates to the previous directory in history.
    /// </summary>
    public Task NavigateBack()
    {
        return _navigationHistory.TryPop(out var previousPath)
            ? LoadDirectoryCoreAsync(previousPath, pushToHistory: false)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Navigates to the parent directory of the current path.
    /// </summary>
    public Task NavigateUp()
    {
        string parent = GetParentPath(CurrentPath);
        return string.Equals(parent, CurrentPath, StringComparison.Ordinal)
            ? Task.CompletedTask
            : LoadDirectoryCoreAsync(parent, pushToHistory: true);
    }

    /// <summary>
    /// Navigates to the captured home directory.
    /// </summary>
    public Task NavigateHome()
    {
        return LoadDirectoryCoreAsync(HomeDirectory, pushToHistory: true);
    }

    /// <summary>
    /// Reloads the current directory without pushing navigation history.
    /// </summary>
    public Task Refresh()
    {
        return LoadDirectoryCoreAsync(CurrentPath, pushToHistory: false);
    }

    /// <summary>
    /// Navigates to the path entered in the path bar.
    /// </summary>
    public Task NavigateToPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? Task.CompletedTask
            : LoadDirectoryCoreAsync(path.Trim(), pushToHistory: true);
    }

    /// <summary>
    /// Handles double-click behavior for a listed remote entry.
    /// </summary>
    public bool HandleFileDoubleClick(SftpFileInfo file)
    {
        if (!file.IsDirectory)
        {
            return false;
        }

        _ = LoadDirectoryAsync(file.FullPath);
        return true;
    }

    /// <summary>
    /// Updates the selection summary text for the current selection.
    /// </summary>
    public void UpdateSelectionInfo(IReadOnlyList<SftpFileInfo> selectedFiles)
    {
        if (selectedFiles.Count <= 1)
        {
            SelectionInfoText = string.Empty;
            return;
        }

        long totalSize = selectedFiles
            .Where(f => !f.IsDirectory)
            .Sum(f => f.Size);

        SelectionInfoText = _localizer?.Format("SftpSelectedCount", selectedFiles.Count.ToString())
            ?? $"{selectedFiles.Count} selected";

        if (totalSize > 0)
        {
            SelectionInfoText += $" ({FormatSize(totalSize)})";
        }
    }

    /// <summary>
    /// Updates the active sort column and applies the new sort order.
    /// </summary>
    public void ToggleSortColumn(string columnName)
    {
        if (string.Equals(SortColumn, columnName, StringComparison.Ordinal))
        {
            SortDirection = SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            SortColumn = columnName;
            SortDirection = ListSortDirection.Ascending;
        }

        ApplyFilterAndSort();
    }

    /// <summary>
    /// Toggles sudo listing mode and refreshes the current directory.
    /// </summary>
    public async Task ToggleSudoMode()
    {
        await RunOnUiAsync(() =>
        {
            SudoMode = !SudoMode;
            UpdateStatus(SudoMode
                ? (_localizer?["SftpSudoModeEnabled"] ?? "Sudo mode enabled — browsing as root")
                : (_localizer?["SftpSudoModeDisabled"] ?? "Sudo mode disabled"));
        });

        await Refresh().ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the shared status text and session connection state.
    /// </summary>
    public void UpdateStatus(string text)
    {
        StatusText = text;
        IsErrorStatus = false;
        IsConnected = _browser?.IsConnected == true;

        if (SessionTab is not null)
        {
            SessionTab.Status = IsConnected ? "Connected" : "Disconnected";
        }
    }

    /// <summary>
    /// Sets an error status message and returns it for caller-side styling.
    /// </summary>
    public string SetErrorStatus(string message)
    {
        StatusText = message;
        IsErrorStatus = true;
        IsConnected = _browser?.IsConnected == true;

        if (SessionTab is not null)
        {
            SessionTab.Status = IsConnected ? "Connected" : "Disconnected";
        }

        return message;
    }

    /// <summary>
    /// Raises the split request event.
    /// </summary>
    public void RequestSplit()
    {
        SplitRequested?.Invoke();
    }

    /// <summary>
    /// Raises the open-in-terminal request event.
    /// </summary>
    public void RequestOpenInTerminal(string path)
    {
        OpenInTerminalRequested?.Invoke(path);
    }

    /// <summary>
    /// Adds the current path to bookmarks if not already present.
    /// Returns true if the bookmark was added.
    /// </summary>
    public bool AddBookmark()
    {
        if (Bookmarks.Contains(CurrentPath))
        {
            return false;
        }

        Bookmarks.Add(CurrentPath);
        UpdateStatus(_localizer?.Format("SftpBookmarkAdded", CurrentPath)
            ?? $"Bookmarked: {CurrentPath}");
        return true;
    }

    /// <summary>
    /// Creates an authenticated SSH client using the session connection settings.
    /// </summary>
    internal async Task<Renci.SshNet.SshClient> CreateSudoSshClientAsync(CancellationToken ct = default)
    {
        if (_sshParams is null)
        {
            throw new InvalidOperationException("SSH params not available for sudo.");
        }

        var connInfo = SshConnectionFactory.Create(_sshParams);
        var ssh = new Renci.SshNet.SshClient(connInfo);

        if (_hostKeyStore is not null)
        {
            SshConnectionFactory.AttachHostKeyVerification(
                ssh, _sshParams.Host, _sshParams.Port, _hostKeyStore);
        }

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            ssh.Connect();
        }, ct).ConfigureAwait(false);

        return ssh;
    }

    /// <summary>
    /// Executes a single sudo command over SSH (chmod, mv, rm, mkdir, etc.).
    /// </summary>
    internal async Task RunSudoCommandAsync(string command, CancellationToken ct = default)
    {
        using var ssh = await CreateSudoSshClientAsync(ct);
        try
        {
            using var cmd = await Task.Run(() => ssh.RunCommand(command), ct).ConfigureAwait(false);
            if (cmd.ExitStatus != 0)
            {
                throw new InvalidOperationException($"Command failed (exit {cmd.ExitStatus}): {cmd.Error}");
            }
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
    internal async Task DownloadViaSudoAsync(string remotePath, string localPath, CancellationToken ct)
    {
        string escaped = PathEscaper.EscapeForShell(remotePath);
        using var ssh = await CreateSudoSshClientAsync(ct);

        try
        {
            using var cmd = await Task.Run(() => ssh.RunCommand($"sudo cat {escaped}"), ct)
                .ConfigureAwait(false);

            if (cmd.ExitStatus != 0)
            {
                throw new InvalidOperationException($"sudo cat failed (exit {cmd.ExitStatus}): {cmd.Error}");
            }

            await File.WriteAllTextAsync(localPath, cmd.Result ?? string.Empty, System.Text.Encoding.UTF8, ct)
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
    internal async Task UploadViaSudoAsync(string localPath, string remotePath, CancellationToken ct)
    {
        if (_browser is null)
        {
            throw new InvalidOperationException("Browser not available for sudo upload.");
        }

        string escaped = PathEscaper.EscapeForShell(remotePath);
        string tempRemote = $"{RemoteTempPrefix}upload_{Guid.NewGuid():N}";

        await _browser.UploadFileAsync(localPath, tempRemote, ct).ConfigureAwait(false);

        using var ssh = await CreateSudoSshClientAsync(ct);
        try
        {
            string escapedTemp = PathEscaper.EscapeForShell(tempRemote);
            using var cmd = await Task.Run(() =>
                ssh.RunCommand($"cat {escapedTemp} | sudo tee -- {escaped} > /dev/null && sudo rm -f {escapedTemp}"),
                ct).ConfigureAwait(false);

            if (cmd.ExitStatus != 0)
            {
                throw new InvalidOperationException($"sudo tee failed (exit {cmd.ExitStatus}): {cmd.Error}");
            }
        }
        finally
        {
            ssh.Disconnect();
        }
    }

    /// <summary>
    /// Prompts for a folder name and creates it in the current remote directory.
    /// </summary>
    public async Task CreateFolderAsync()
    {
        if (_disposed || _browser is null || _dialogService is null)
        {
            return;
        }

        string? folderName = await _dialogService.ShowInputAsync(
            L10n("SftpNewFolderTitle"),
            L10n("SftpNewFolderName"));

        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        try
        {
            string remotePath = CombineRemotePath(CurrentPath, folderName);
            try
            {
                await _browser.CreateDirectoryAsync(remotePath);
            }
            catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
            {
                Core.Logging.FileLogger.Info("EmbeddedSFTP mkdir permission denied, falling back to sudo");
                await RunSudoCommandAsync($"sudo mkdir -p {PathEscaper.EscapeForShell(remotePath)}");
            }

            await RunOnUiAsync(() => UpdateStatus(L10n("SftpSuccessMkdir")));
            await Refresh().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
                SetErrorStatus(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message));
        }
    }

    /// <summary>
    /// Prompts for a new name and renames the selected remote entry.
    /// </summary>
    public async Task RenameEntryAsync(SftpFileInfo file)
    {
        if (_disposed || _browser is null || _dialogService is null)
        {
            return;
        }

        string? newName = await _dialogService.ShowInputAsync(
            L10n("SftpBtnRename"),
            L10n("SftpNewFolderName"),
            file.Name);

        if (string.IsNullOrWhiteSpace(newName)
            || string.Equals(newName, file.Name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            string newPath = CombineRemotePath(CurrentPath, newName);
            try
            {
                await _browser.RenameAsync(file.FullPath, newPath);
            }
            catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
            {
                Core.Logging.FileLogger.Info("EmbeddedSFTP rename permission denied, falling back to sudo");
                await RunSudoCommandAsync(
                    $"sudo mv {PathEscaper.EscapeForShell(file.FullPath)} {PathEscaper.EscapeForShell(newPath)}");
            }

            await RunOnUiAsync(() => UpdateStatus(L10n("SftpSuccessRename")));
            await Refresh().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
                SetErrorStatus(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message));
        }
    }

    /// <summary>
    /// Confirms and deletes the selected remote entries.
    /// </summary>
    public async Task DeleteEntriesAsync(IReadOnlyList<SftpFileInfo> entries)
    {
        if (entries.Count == 0 || _disposed || _browser is null || _dialogService is null)
        {
            return;
        }

        string itemName = entries.Count == 1 ? entries[0].Name : $"{entries.Count} items";
        string message = _localizer?.Format("SftpConfirmDelete", itemName)
            ?? $"Delete \"{itemName}\"?";

        bool confirmed = await _dialogService.ShowConfirmAsync(
            L10n("SftpConfirmDeleteTitle"),
            message,
            "warning");

        if (!confirmed)
        {
            return;
        }

        try
        {
            foreach (var file in entries)
            {
                try
                {
                    await _browser.DeleteAsync(file.FullPath);
                }
                catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
                {
                    Core.Logging.FileLogger.Info(
                        $"EmbeddedSFTP delete permission denied, falling back to sudo for {file.Name}");
                    string flag = file.IsDirectory ? "-rf" : "-f";
                    await RunSudoCommandAsync(
                        $"sudo rm {flag} {PathEscaper.EscapeForShell(file.FullPath)}");
                }
            }

            await RunOnUiAsync(() => UpdateStatus(L10n("SftpSuccessDelete")));
            await Refresh().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
                SetErrorStatus(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message));
        }
    }

    /// <summary>
    /// Prompts for new octal permissions and applies chmod to the selected entry.
    /// </summary>
    public async Task ChmodAsync(SftpFileInfo file)
    {
        if (_disposed || _browser is null || _dialogService is null)
        {
            return;
        }

        string currentOctal = PermissionsToOctal(file.Permissions);

        string? newPerms = await _dialogService.ShowInputAsync(
            _localizer?.Format("SftpChmodTitle", file.Name) ?? $"chmod {file.Name}",
            L10n("SftpChmodLabel"),
            currentOctal);

        if (string.IsNullOrWhiteSpace(newPerms))
        {
            return;
        }

        if (!int.TryParse(newPerms, NumberStyles.None, null, out int octal)
            || octal < 0
            || octal > 777
            || newPerms.Any(c => c < '0' || c > '7'))
        {
            SetErrorStatus(L10n("ErrorInvalidOctalPermission"));
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
                Core.Logging.FileLogger.Info("EmbeddedSFTP chmod permission denied, falling back to sudo");
                await RunSudoCommandAsync(
                    $"sudo chmod {newPerms} {PathEscaper.EscapeForShell(file.FullPath)}");
            }

            await RunOnUiAsync(() => UpdateStatus(L10n("SftpChmodSuccess")));
            await Refresh().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
                SetErrorStatus(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message));
        }
    }

    /// <summary>
    /// Displays a properties dialog for the selected remote entry.
    /// </summary>
    public void ShowProperties(SftpFileInfo file)
    {
        if (_dialogService is null)
        {
            return;
        }

        string type = file.IsDirectory
            ? L10n("SftpPropertiesTypeDirectory")
            : L10n("SftpPropertiesTypeFile");

        string sizeText = file.IsDirectory ? "-" : FormatSize(file.Size);
        string octal = PermissionsToOctal(file.Permissions);

        string body = $"{L10n("SftpPropertiesName")} {file.Name}\n" +
                      $"{L10n("SftpPropertiesType")} {type}\n" +
                      $"{L10n("SftpPropertiesSize")} {sizeText}\n" +
                      $"{L10n("SftpPropertiesModified")} {file.LastModified:yyyy-MM-dd HH:mm:ss}\n" +
                      $"{L10n("SftpPropertiesPermissions")} {file.Permissions} ({octal})\n" +
                      $"Owner: {file.Owner}  Group: {file.Group}\n" +
                      $"Path: {file.FullPath}";

        _dialogService.ShowInfo(
            _localizer?.Format("SftpPropertiesTitle", file.Name) ?? $"Properties — {file.Name}",
            body);
    }

    /// <summary>
    /// Returns the parent path for a remote directory path.
    /// </summary>
    public static string GetParentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        string trimmed = path.TrimEnd('/');
        int lastSlash = trimmed.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : trimmed[..lastSlash];
    }

    /// <summary>
    /// Combines a remote directory path and child name.
    /// </summary>
    public static string CombineRemotePath(string directory, string name)
    {
        return $"{directory.TrimEnd('/')}/{name}";
    }

    /// <summary>
    /// Converts a rwxrwxrwx permission string to its octal form.
    /// </summary>
    public static string PermissionsToOctal(string perms)
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

    /// <summary>
    /// Formats a byte count using the shared file-size formatter.
    /// </summary>
    public static string FormatSize(long bytes) => FileSize.Format(bytes);

    /// <summary>
    /// Determines whether the provided exception represents a permission error.
    /// </summary>
    public static bool IsPermissionDenied(Exception ex)
    {
        string typeName = ex.GetType().Name;
        if (typeName.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (typeName.Contains("PathNotFound", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("NoSuchFile", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string message = ex.Message + (ex.InnerException?.Message ?? "");

        if (message.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
            || message.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not permitted", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SSH_FX_PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return message.Contains("Failure", StringComparison.Ordinal)
            && (typeName.Contains("Sftp", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("Ssh", StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadDirectoryCoreAsync(string path, bool pushToHistory)
    {
        if (_disposed || _browser is null || !_browser.IsConnected || IsLoading)
        {
            return;
        }

        await RunOnUiAsync(() => IsLoading = true);

        try
        {
            await RunOnUiAsync(() => UpdateStatus(_localizer?["SftpStatusLoading"] ?? "Loading..."));

            IReadOnlyList<SftpFileInfo> entries;

            if (SudoMode && _sshParams is not null)
            {
                entries = await ListDirectoryViaSudoAsync(path).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    entries = await _browser.ListDirectoryAsync(path).ConfigureAwait(false);
                }
                catch (Exception ex) when (_sshParams is not null && IsPermissionDenied(ex))
                {
                    Core.Logging.FileLogger.Info(
                        $"EmbeddedSFTP listdir permission denied, falling back to sudo for {path}");
                    entries = await ListDirectoryViaSudoAsync(path).ConfigureAwait(false);
                }
            }

            await RunOnUiAsync(() =>
            {
                if (pushToHistory && !string.Equals(path, CurrentPath, StringComparison.Ordinal))
                {
                    _navigationHistory.Push(CurrentPath);
                }

                CurrentPath = path;
                UnfilteredEntries = [.. entries];
                ApplyFilterAndSort();
                CanGoBack = _navigationHistory.Count > 0;
                UpdateStatus(_localizer?["SftpStatusReady"] ?? "Ready");
            });
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"EmbeddedSFTP LoadDirectory failed: {ex.Message}");
            await RunOnUiAsync(() =>
                SetErrorStatus(_localizer?.Format("SftpStatusTransferFailed", ex.Message) ?? ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() => IsLoading = false);
        }
    }

    private async Task<IReadOnlyList<SftpFileInfo>> ListDirectoryViaSudoAsync(string path)
    {
        string escaped = PathEscaper.EscapeForShell(path);
        using var ssh = await CreateSudoSshClientAsync().ConfigureAwait(false);

        try
        {
            using var cmd = await Task.Run(() =>
                ssh.RunCommand($"sudo ls -la --time-style=long-iso {escaped}")).ConfigureAwait(false);

            if (cmd.ExitStatus != 0)
            {
                throw new InvalidOperationException($"sudo ls failed (exit {cmd.ExitStatus}): {cmd.Error}");
            }

            return ParseLsOutput(cmd.Result ?? string.Empty, path);
        }
        finally
        {
            ssh.Disconnect();
        }
    }

    private static IReadOnlyList<SftpFileInfo> ParseLsOutput(string output, string parentPath)
    {
        var results = new List<SftpFileInfo>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.StartsWith("total ", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, 8, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8)
            {
                continue;
            }

            string permissions = parts[0];
            if (permissions.Length < 2 || !"dl-cbps".Contains(permissions[0]))
            {
                continue;
            }

            string owner = parts[2];
            string group = parts[3];
            _ = long.TryParse(parts[4], out long size);

            DateTime lastModified = DateTime.MinValue;
            _ = DateTime.TryParse($"{parts[5]} {parts[6]}", out lastModified);

            string name = parts[7];
            if (name is "." or "..")
            {
                continue;
            }

            int arrowIndex = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                name = name[..arrowIndex];
            }

            bool isDirectory = permissions[0] == 'd';
            string fullPath = parentPath.EndsWith("/", StringComparison.Ordinal)
                ? $"{parentPath}{name}"
                : $"{parentPath}/{name}";

            results.Add(new SftpFileInfo(
                name, fullPath, isDirectory, size, lastModified,
                permissions, owner, group));
        }

        return results;
    }

    partial void OnFilterTextChanged(string value)
    {
        if (!IsLoading)
        {
            ApplyFilterAndSort();
        }
    }

    partial void OnShowHiddenChanged(bool value)
    {
        if (!IsLoading)
        {
            ApplyFilterAndSort();
        }
    }

    private Task RunOnUiAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _uiDispatcher.InvokeAsync(action).Task;
    }

    private string L10n(string key) => _localizer?.GetString(key) ?? key;
}
