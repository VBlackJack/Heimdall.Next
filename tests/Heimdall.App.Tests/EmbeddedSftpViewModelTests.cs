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

using System.Reflection;
using Heimdall.App.ViewModels;
using Heimdall.Sftp;

namespace Heimdall.App.Tests;

public sealed class EmbeddedSftpViewModelTests
{
    [Fact]
    public void Constructor_RequiresUiDispatcher_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => new EmbeddedSftpViewModel(null!));
    }

    [Fact]
    public void CurrentPath_Set_UpdatesPathBarText()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher);

        viewModel.CurrentPath = "/var/log";

        Assert.Equal("/var/log", viewModel.PathBarText);
    }

    [Fact]
    public void IsToolbarEnabled_RequiresConnectedAndNotLoading()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher)
        {
            IsConnected = true
        };

        Assert.True(viewModel.IsToolbarEnabled);

        viewModel.IsLoading = true;

        Assert.False(viewModel.IsToolbarEnabled);

        viewModel.IsLoading = false;
        viewModel.IsConnected = false;

        Assert.False(viewModel.IsToolbarEnabled);
    }

    [Fact]
    public void CanNavigateBack_RequiresToolbarStateAndBackHistory()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher)
        {
            IsConnected = true
        };

        Assert.False(viewModel.CanNavigateBack);

        viewModel.CanGoBack = true;

        Assert.True(viewModel.CanNavigateBack);

        viewModel.IsLoading = true;

        Assert.False(viewModel.CanNavigateBack);

        viewModel.IsLoading = false;
        viewModel.IsConnected = false;

        Assert.False(viewModel.CanNavigateBack);
    }

    [Fact]
    public void IsDisconnected_IsInverseOfIsConnected()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher);

        Assert.True(viewModel.IsDisconnected);

        viewModel.IsConnected = true;

        Assert.False(viewModel.IsDisconnected);
    }

    [Fact]
    public void SetSelection_UpdatesSelectedFileSelectedFilesAndSelectionInfoText()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher);
        SftpFileInfo file = new(
            "app.log",
            "/var/log/app.log",
            false,
            1,
            DateTime.UnixEpoch,
            "rw-r--r--",
            "1000",
            "1000");
        SftpFileInfo directory = new(
            "archive",
            "/var/log/archive",
            true,
            0,
            DateTime.UnixEpoch,
            "rwxr-xr-x",
            "1000",
            "1000");
        IReadOnlyList<SftpFileInfo> selectedFiles = [file, directory];

        viewModel.SetSelection(selectedFiles, file);

        Assert.Same(file, viewModel.SelectedFile);
        Assert.Same(selectedFiles, viewModel.SelectedFiles);
        Assert.Equal("2 selected (1 B)", viewModel.SelectionInfoText);
    }

    [Fact]
    public void OpenSelectedInTerminalCommand_RaisesDirectoryPathOrCurrentPath()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher)
        {
            CurrentPath = "/home/admin"
        };
        SftpFileInfo directory = new(
            "logs",
            "/home/admin/logs",
            true,
            0,
            DateTime.UnixEpoch,
            "rwxr-xr-x",
            "1000",
            "1000");
        SftpFileInfo file = new(
            "app.log",
            "/home/admin/app.log",
            false,
            10,
            DateTime.UnixEpoch,
            "rw-r--r--",
            "1000",
            "1000");
        string? requestedPath = null;
        viewModel.OpenInTerminalRequested += path => requestedPath = path;

        viewModel.SetSelection([directory], directory);
        viewModel.OpenSelectedInTerminalCommand.Execute(null);

        Assert.Equal("/home/admin/logs", requestedPath);

        requestedPath = null;
        viewModel.SetSelection([file], file);
        viewModel.OpenSelectedInTerminalCommand.Execute(null);

        Assert.Equal("/home/admin", requestedPath);
    }

    [Fact]
    public void SetErrorStatus_SetsIsErrorHighlighted()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher);

        try
        {
            viewModel.SetErrorStatus("Connection failed");

            Assert.True(viewModel.IsErrorHighlighted);
        }
        finally
        {
            viewModel.MarkDisposed();
        }
    }

    [Fact]
    public void UpdateStatus_AfterErrorStatus_ClearsIsErrorHighlighted()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher);

        viewModel.SetErrorStatus("Connection failed");
        viewModel.UpdateStatus("Ready");

        Assert.False(viewModel.IsErrorHighlighted);
    }

    [Fact]
    public void CleartextWarning_DefaultsHidden()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher);

        Assert.False(viewModel.IsCleartextWarningVisible);
        Assert.Equal(string.Empty, viewModel.CleartextWarningText);
    }

    [Fact]
    public void ShowCleartextWarning_SetsTextAndVisibility()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher);

        viewModel.ShowCleartextWarning("Credentials sent in clear text (no TLS)");

        Assert.True(viewModel.IsCleartextWarningVisible);
        Assert.Equal("Credentials sent in clear text (no TLS)", viewModel.CleartextWarningText);
    }

    [Fact]
    public void CleartextWarning_RemainsVisibleWhenStatusChanges()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher);

        viewModel.ShowCleartextWarning("Credentials sent in clear text (no TLS)");
        viewModel.UpdateStatus("Ready");

        Assert.True(viewModel.IsCleartextWarningVisible);
        Assert.Equal("Credentials sent in clear text (no TLS)", viewModel.CleartextWarningText);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task LoadDirectoryAsync_MarkDisposedDuringList_CancelsWithoutErrorAndClearsLoading()
    {
        FakeUiDispatcher dispatcher = new();
        TaskCompletionSource<CancellationToken> capturedToken = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakeRemoteBrowser browser = new()
        {
            ListDirectoryHandler = async (_, ct) =>
            {
                capturedToken.TrySetResult(ct);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return [];
            }
        };
        EmbeddedSftpViewModel viewModel = new(dispatcher);
        SetBrowser(viewModel, browser);

        Task loadTask = viewModel.LoadDirectoryAsync("/slow");
        CancellationToken listingToken = await capturedToken.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(viewModel.IsLoading);
        Assert.False(listingToken.IsCancellationRequested);

        viewModel.MarkDisposed();
        await loadTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(listingToken.IsCancellationRequested);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsErrorStatus);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task NavigateInitialAsync_PreferredPath_LoadsPreferredPathWithoutHistory()
    {
        FakeUiDispatcher dispatcher = new();
        List<string?> requestedPaths = [];
        FakeRemoteBrowser browser = new()
        {
            ListDirectoryHandler = (path, _) =>
            {
                requestedPaths.Add(path);
                return Task.FromResult<IReadOnlyList<SftpFileInfo>>([]);
            }
        };
        EmbeddedSftpViewModel viewModel = new(dispatcher);
        SetBrowser(viewModel, browser);

        await viewModel.NavigateInitialAsync("/var/log");

        Assert.Collection(requestedPaths, path => Assert.Equal("/var/log", path));
        Assert.Equal("/var/log", viewModel.CurrentPath);
        Assert.False(viewModel.CanGoBack);
        Assert.False(viewModel.IsErrorStatus);
    }

    [Fact]
    public async Task NavigateInitialAsync_MissingPreferredPath_FallsBackToHomeWithoutError()
    {
        FakeUiDispatcher dispatcher = new();
        List<string?> requestedPaths = [];
        FakeRemoteBrowser browser = new()
        {
            ListDirectoryHandler = (path, _) =>
            {
                requestedPaths.Add(path);
                if (string.Equals(path, "/missing", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("gone");
                }

                return Task.FromResult<IReadOnlyList<SftpFileInfo>>([]);
            }
        };
        EmbeddedSftpViewModel viewModel = new(dispatcher);
        SetBrowser(viewModel, browser);

        await viewModel.NavigateInitialAsync("/missing");

        Assert.Collection(
            requestedPaths,
            path => Assert.Equal("/missing", path),
            path => Assert.Equal("/", path));
        Assert.Equal("/", viewModel.CurrentPath);
        Assert.False(viewModel.CanGoBack);
        Assert.False(viewModel.IsErrorStatus);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task NavigateInitialAsync_MissingPreferredPath_DoesNotSetErrorDuringFallback()
    {
        FakeUiDispatcher dispatcher = new();
        bool errorWasSetBeforeHomeListing = false;
        EmbeddedSftpViewModel viewModel = new(dispatcher);
        FakeRemoteBrowser browser = new()
        {
            ListDirectoryHandler = (path, _) =>
            {
                if (string.Equals(path, "/missing", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("gone");
                }

                errorWasSetBeforeHomeListing |= viewModel.IsErrorStatus;
                return Task.FromResult<IReadOnlyList<SftpFileInfo>>([]);
            }
        };
        SetBrowser(viewModel, browser);

        await viewModel.NavigateInitialAsync("/missing");

        Assert.False(errorWasSetBeforeHomeListing);
        Assert.False(viewModel.IsErrorStatus);
        Assert.Equal("/", viewModel.CurrentPath);
    }

    [Fact]
    public async Task NavigateInitialAsync_BlankPreferredPath_LoadsHome()
    {
        FakeUiDispatcher dispatcher = new();
        List<string?> requestedPaths = [];
        FakeRemoteBrowser browser = new()
        {
            ListDirectoryHandler = (path, _) =>
            {
                requestedPaths.Add(path);
                return Task.FromResult<IReadOnlyList<SftpFileInfo>>([]);
            }
        };
        EmbeddedSftpViewModel viewModel = new(dispatcher);
        SetBrowser(viewModel, browser);

        await viewModel.NavigateInitialAsync("   ");

        Assert.Collection(requestedPaths, path => Assert.Equal("/", path));
        Assert.Equal("/", viewModel.CurrentPath);
        Assert.False(viewModel.IsErrorStatus);
    }

    [Fact]
    public async Task UploadFilesAsync_WhenTransferAlreadyInProgress_DoesNotUpload()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher)
        {
            IsTransferInProgress = true
        };
        FakeRemoteBrowser browser = new();
        SetBrowser(viewModel, browser);

        await viewModel.UploadFilesAsync(["C:\\temp\\app.log"]);

        Assert.Equal(0, browser.UploadCallCount);
        Assert.True(viewModel.IsTransferInProgress);
        Assert.Equal("A file transfer is already in progress.", viewModel.StatusText);
    }

    [Fact]
    public void CancelTransferCommand_NoTransferRunning_DoesNotThrow()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher);

        Exception? exception = Record.Exception(() => viewModel.CancelTransferCommand.Execute(null));

        Assert.Null(exception);
    }

    [Fact]
    public void UpdateTransferProgress_UpdatesProgressValueAndStatusText()
    {
        FakeUiDispatcher dispatcher = new();
        EmbeddedSftpViewModel viewModel = new(dispatcher);
        SftpTransferProgress progress = new("app.log", 512, 1024, true);

        viewModel.UpdateTransferProgress(progress);

        Assert.Equal(50, viewModel.TransferProgressValue);
        string transferred = EmbeddedSftpViewModel.FormatSize(512);
        string total = EmbeddedSftpViewModel.FormatSize(1024);
        Assert.Equal($"\u2191 app.log \u2014 {transferred} / {total} (50%)", viewModel.TransferStatusText);
    }

    [Fact]
    public async Task RunOnUiAsync_OffUiThread_PostsToDispatcher()
    {
        var dispatcher = new FakeUiDispatcher(checkAccess: false);
        var viewModel = new EmbeddedSftpViewModel(dispatcher);
        var actionRuns = 0;

        await InvokeRunOnUiAsync(viewModel, () => actionRuns++);

        Assert.Equal(1, dispatcher.InvokeAsyncCalls);
        Assert.Equal(1, actionRuns);
    }

    private static Task InvokeRunOnUiAsync(EmbeddedSftpViewModel viewModel, Action action)
    {
        var method = typeof(EmbeddedSftpViewModel).GetMethod("RunOnUiAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = method!.Invoke(viewModel, [action]) as Task;
        return task ?? throw new InvalidOperationException("RunOnUiAsync did not return a Task.");
    }

    private static void SetBrowser(EmbeddedSftpViewModel viewModel, IRemoteBrowser browser)
    {
        FieldInfo? field = typeof(EmbeddedSftpViewModel).GetField(
            "_browser",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(viewModel, browser);
    }

    private sealed class FakeRemoteBrowser : IRemoteBrowser
    {
        private int _uploadCallCount;

        public event Action<string>? DirectoryChanged
        {
            add { }
            remove { }
        }

        public event Action<SftpTransferProgress>? TransferProgress
        {
            add { }
            remove { }
        }

        public event Action<string?>? Disconnected
        {
            add { }
            remove { }
        }

        public string CurrentDirectory => "/";

        public bool IsConnected => true;

        public int UploadCallCount => Volatile.Read(ref _uploadCallCount);

        public Func<string?, CancellationToken, Task<IReadOnlyList<SftpFileInfo>>>? ListDirectoryHandler { get; set; }

        public Task<IReadOnlyList<SftpFileInfo>> ListDirectoryAsync(
            string? path = null,
            CancellationToken ct = default)
        {
            if (ListDirectoryHandler is not null)
            {
                return ListDirectoryHandler(path, ct);
            }

            return Task.FromResult<IReadOnlyList<SftpFileInfo>>([]);
        }

        public Task<string> GetCurrentDirectoryAsync(CancellationToken ct = default)
        {
            return Task.FromResult(CurrentDirectory);
        }

        public Task ChangeDirectoryAsync(string path, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task DownloadFileAsync(
            string remotePath,
            string localPath,
            CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task UploadFileAsync(
            string localPath,
            string remotePath,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _uploadCallCount);
            return Task.CompletedTask;
        }

        public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task ChmodAsync(string path, short mode, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public void Disconnect()
        {
        }

        public void Dispose()
        {
        }
    }
}
