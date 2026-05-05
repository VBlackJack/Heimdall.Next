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

using Heimdall.Core.Ssh;
using Heimdall.Sftp;
using Heimdall.Ssh;
using System.IO;

namespace Heimdall.App.Tests;

public sealed class RemoteFileEditorTaskTrackingTests
{
    [Fact]
    public async Task OnFileChanged_Tracks_CurrentUpload_OnSession()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new FakeRemoteBrowser((_, _, _) => gate.Task);
        using var editor = CreateEditor(browser);
        using var session = CreateSession();

        editor.TriggerOnFileChangedForTesting(session);

        await WaitUntilAsync(() => browser.UploadCallCount == 1 && session.CurrentUpload is not null);
        Assert.NotNull(session.CurrentUpload);
        Assert.False(session.CurrentUpload!.IsCompleted);

        gate.SetResult();
        await WaitForTaskAsync(session.CurrentUpload);

        Assert.True(session.CurrentUpload.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CloseEdit_Cancels_InFlightUpload()
    {
        var browser = new FakeRemoteBrowser((_, _, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct));
        using var editor = CreateEditor(browser);
        var session = CreateSession();
        var uploadToken = session.UploadCts.Token;
        Assert.True(editor.AddSessionForTesting(session));

        editor.TriggerOnFileChangedForTesting(session);
        await WaitUntilAsync(() => browser.UploadCallCount == 1 && session.CurrentUpload is not null);

        editor.CloseEdit(session.RemotePath);

        Assert.True(uploadToken.IsCancellationRequested);
        Assert.DoesNotContain(session.RemotePath, editor.ActiveSessionsForTesting.Keys);
        Assert.True(session.CurrentUpload!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Dispose_Cancels_All_InFlightUploads()
    {
        var browser = new FakeRemoteBrowser((_, _, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct));
        var editor = CreateEditor(browser);
        var first = CreateSession("/remote/first.txt");
        var second = CreateSession("/remote/second.txt");
        var firstToken = first.UploadCts.Token;
        var secondToken = second.UploadCts.Token;
        Assert.True(editor.AddSessionForTesting(first));
        Assert.True(editor.AddSessionForTesting(second));

        editor.TriggerOnFileChangedForTesting(first);
        editor.TriggerOnFileChangedForTesting(second);
        await WaitUntilAsync(() =>
            browser.UploadCallCount == 2
            && first.CurrentUpload is not null
            && second.CurrentUpload is not null);

        editor.Dispose();

        Assert.True(firstToken.IsCancellationRequested);
        Assert.True(secondToken.IsCancellationRequested);
        Assert.Empty(editor.ActiveSessionsForTesting);
        Assert.True(first.CurrentUpload!.IsCompletedSuccessfully);
        Assert.True(second.CurrentUpload!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task OnFileChangedAsync_HostKeyRejected_RaisesEvent_DoesNotRethrow()
    {
        var browser = new FakeRemoteBrowser((_, _, _) =>
            throw new HostKeyRejectedException(
                "gw.example.com",
                22,
                "ssh-ed25519",
                "SHA256:BBB",
                "SHA256:AAA"));
        using var editor = CreateEditor(browser);
        using var session = CreateSession();
        HostKeyRotationEvent? rotation = null;
        var uploadEvents = new List<(string RemotePath, bool Success)>();
        editor.HostKeyRotatedDuringUpload += e => rotation = e;
        editor.FileUploaded += (path, success) => uploadEvents.Add((path, success));

        editor.TriggerOnFileChangedForTesting(session);
        await WaitUntilAsync(() => session.CurrentUpload is not null);
        await WaitForTaskAsync(session.CurrentUpload!);

        Assert.True(session.CurrentUpload!.IsCompletedSuccessfully);
        Assert.NotNull(rotation);
        Assert.Equal(session.RemotePath, rotation!.RemotePath);
        Assert.Equal("SHA256:BBB", rotation.PresentedFingerprint);
        Assert.Equal("SHA256:AAA", rotation.StoredFingerprint);
        Assert.Equal("gw.example.com", rotation.Host);
        Assert.Equal(22, rotation.Port);
        var uploadEvent = Assert.Single(uploadEvents);
        Assert.Equal(session.RemotePath, uploadEvent.RemotePath);
        Assert.False(uploadEvent.Success);
    }

    [Fact]
    public async Task OnFileChangedAsync_Cancellation_RaisesFileUploadedFalse()
    {
        var browser = new FakeRemoteBrowser((_, _, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct));
        using var editor = CreateEditor(browser);
        using var session = CreateSession();
        var uploadEvents = new List<(string RemotePath, bool Success)>();
        editor.FileUploaded += (path, success) => uploadEvents.Add((path, success));

        editor.TriggerOnFileChangedForTesting(session);
        await WaitUntilAsync(() => browser.UploadCallCount == 1 && session.CurrentUpload is not null);

        session.UploadCts.Cancel();
        await WaitForTaskAsync(session.CurrentUpload!);

        Assert.True(session.CurrentUpload!.IsCompletedSuccessfully);
        var uploadEvent = Assert.Single(uploadEvents);
        Assert.Equal(session.RemotePath, uploadEvent.RemotePath);
        Assert.False(uploadEvent.Success);
    }

    [Fact]
    public void EditSession_Dispose_DisposesUploadCts()
    {
        var session = CreateSession();

        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.UploadCts.Cancel());
    }

    [Fact]
    public async Task CloseEdit_AfterUploadCompletes_DoesNotThrow()
    {
        var browser = new FakeRemoteBrowser();
        using var editor = CreateEditor(browser);
        var session = CreateSession();
        Assert.True(editor.AddSessionForTesting(session));

        editor.TriggerOnFileChangedForTesting(session);
        await WaitUntilAsync(() => session.CurrentUpload is not null);
        await WaitForTaskAsync(session.CurrentUpload!);

        var ex = Record.Exception(() => editor.CloseEdit(session.RemotePath));

        Assert.Null(ex);
        Assert.DoesNotContain(session.RemotePath, editor.ActiveSessionsForTesting.Keys);
    }

    private static RemoteFileEditor CreateEditor(FakeRemoteBrowser browser)
    {
        return new RemoteFileEditor(
            browser,
            new HostKeyStore(),
            RejectingHostKeyVerifier.Instance);
    }

    private static EditSession CreateSession(string? remotePath = null)
    {
        var resolvedRemotePath = remotePath ?? $"/remote/{Guid.NewGuid():N}.txt";
        return new EditSession
        {
            RemotePath = resolvedRemotePath,
            LocalPath = Path.Combine(Path.GetTempPath(), "HeimdallTests", Guid.NewGuid().ToString("N"), "file.txt"),
            IsSudo = false,
            LastUploadTime = DateTime.UtcNow - RemoteFileEditor.UploadDebounceInterval - TimeSpan.FromSeconds(1)
        };
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 2000)
    {
        var timeoutAt = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < timeoutAt)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(predicate(), "Condition was not met before timeout.");
    }

    private static async Task WaitForTaskAsync(Task task, int timeoutMs = 2000)
    {
        var timeout = Task.Delay(timeoutMs);
        var completed = await Task.WhenAny(task, timeout);
        Assert.Same(task, completed);
        await task;
    }

    private sealed class FakeRemoteBrowser : IRemoteBrowser
    {
        private readonly Func<string, string, CancellationToken, Task> _uploadFileAsync;
        private int _uploadCallCount;

        public FakeRemoteBrowser(Func<string, string, CancellationToken, Task>? uploadFileAsync = null)
        {
            _uploadFileAsync = uploadFileAsync ?? ((_, _, _) => Task.CompletedTask);
        }

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

        public Task<IReadOnlyList<SftpFileInfo>> ListDirectoryAsync(
            string? path = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SftpFileInfo>>([]);

        public Task<string> GetCurrentDirectoryAsync(CancellationToken ct = default) =>
            Task.FromResult(CurrentDirectory);

        public Task ChangeDirectoryAsync(string path, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DownloadFileAsync(
            string remotePath,
            string localPath,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UploadFileAsync(
            string localPath,
            string remotePath,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _uploadCallCount);
            return _uploadFileAsync(localPath, remotePath, ct);
        }

        public Task CreateDirectoryAsync(string path, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string path, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ChmodAsync(string path, short mode, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default) =>
            Task.CompletedTask;

        public void Disconnect()
        {
        }

        public void Dispose()
        {
        }
    }
}
