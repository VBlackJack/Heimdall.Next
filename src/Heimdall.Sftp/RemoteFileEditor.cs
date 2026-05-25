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

using System.Collections.Concurrent;
using System.Diagnostics;
using Heimdall.Core.Ssh;
using Heimdall.Ssh;
using Renci.SshNet;

namespace Heimdall.Sftp;

/// <summary>
/// Manages remote file editing sessions: downloads a file to a local temp directory,
/// opens it in an external editor, and auto-uploads changes via a
/// <see cref="FileSystemWatcher"/> with debounce protection.
/// </summary>
public sealed class RemoteFileEditor : IDisposable
{
    /// <summary>Minimum interval between consecutive auto-uploads for the same file.</summary>
    public static TimeSpan UploadDebounceInterval { get; set; } = TimeSpan.FromSeconds(2);
    private const string RemoteTempPrefix = "/tmp/.heimdall_";
    private static readonly TimeSpan UploadDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly IRemoteBrowser _browser;
    private readonly string _editorPath;
    private readonly HostKeyStore _hostKeyStore;
    private readonly IHostKeyVerifier _hostKeyVerifier;
    private readonly ConcurrentDictionary<string, EditSession> _activeSessions = new();
    private bool _disposed;

    /// <summary>
    /// Raised after an auto-upload attempt. Parameters: remote path, success flag.
    /// </summary>
    public event Action<string, bool>? FileUploaded;

    /// <summary>
    /// Raised when an auto-upload is rejected because the host key changed
    /// after the sudo edit session was opened.
    /// </summary>
    public event Action<HostKeyRotationEvent>? HostKeyRotatedDuringUpload;

    /// <summary>
    /// Creates a new <see cref="RemoteFileEditor"/> backed by the given SFTP browser.
    /// </summary>
    /// <param name="browser">Connected SFTP browser used for file transfers.</param>
    /// <param name="hostKeyStore">TOFU host key store for server verification on SSH connections.</param>
    /// <param name="hostKeyVerifier">Verifier used when a host key is unknown or changed.</param>
    /// <param name="editorPath">
    /// Path to the external editor executable (defaults to <c>notepad.exe</c>).
    /// </param>
    public RemoteFileEditor(
        IRemoteBrowser browser,
        HostKeyStore hostKeyStore,
        IHostKeyVerifier hostKeyVerifier,
        string editorPath = "notepad.exe")
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(hostKeyStore);
        ArgumentNullException.ThrowIfNull(hostKeyVerifier);
        _browser = browser;
        _editorPath = editorPath;
        _hostKeyStore = hostKeyStore;
        _hostKeyVerifier = hostKeyVerifier;
    }

    /// <summary>
    /// Opens a remote file for editing: downloads it locally, launches the
    /// configured editor, and starts watching for changes to auto-upload.
    /// </summary>
    /// <param name="remotePath">Full remote path to the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// The file is already open for editing in another session.
    /// </exception>
    public async Task EditFileAsync(string remotePath, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);

        // Close previous edit session for this file if one exists
        CloseEdit(remotePath);

        string localPath = CreateTempFilePath(remotePath);

        await _browser.DownloadFileAsync(remotePath, localPath, ct).ConfigureAwait(false);

        var session = new EditSession
        {
            RemotePath = remotePath,
            LocalPath = localPath,
            IsSudo = false,
            LastUploadTime = DateTime.UtcNow
        };

        if (!_activeSessions.TryAdd(remotePath, session))
        {
            session.Dispose();
            CleanupTempFile(localPath);
        }

        StartWatcher(session);
        LaunchEditor(_editorPath, localPath);
    }

    /// <summary>
    /// Opens a privileged (sudo) remote file for editing. The file is downloaded
    /// via <c>sudo cat</c> over SSH and uploaded back via <c>sudo tee</c>.
    /// </summary>
    /// <param name="remotePath">Full remote path to the privileged file.</param>
    /// <param name="sshParams">SSH connection parameters for the sudo SSH session.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task EditFileSudoAsync(
        string remotePath,
        SshConnectionParams sshParams,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentNullException.ThrowIfNull(sshParams);

        // Close previous edit session for this file if one exists
        CloseEdit(remotePath);

        string localPath = CreateTempFilePath(remotePath);

        var pinnedVerifier = await SshConnectionFactory.ResolveHostKeyAsync(
                sshParams,
                _hostKeyStore,
                _hostKeyVerifier,
                ct)
            .ConfigureAwait(false);

        // Download with sudo via SSH command
        var connectionInfo = SshConnectionFactory.Create(sshParams);
        using var sshClient = new SshClient(connectionInfo);

        SshConnectionFactory.AttachPinnedHostKeyVerification(
            sshClient,
            sshParams.Host,
            sshParams.Port,
            pinnedVerifier);

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            sshClient.Connect();
        }, ct).ConfigureAwait(false);

        try
        {
            using var downloadCmd = await Task.Run(() =>
                sshClient.RunCommand(BuildSudoDownloadCommand(remotePath)),
                ct).ConfigureAwait(false);

            if (downloadCmd.ExitStatus != 0)
            {
                throw new InvalidOperationException(
                    $"sudo base64 failed (exit {downloadCmd.ExitStatus}): {downloadCmd.Error}");
            }

            await WriteBase64DecodedFileAsync(
                localPath,
                downloadCmd.Result,
                ct).ConfigureAwait(false);
        }
        finally
        {
            sshClient.Disconnect();
        }

        var session = new EditSession
        {
            RemotePath = remotePath,
            LocalPath = localPath,
            IsSudo = true,
            SshParams = sshParams,
            Verifier = pinnedVerifier,
            LastUploadTime = DateTime.UtcNow
        };

        if (!_activeSessions.TryAdd(remotePath, session))
        {
            session.Dispose();
            CleanupTempFile(localPath);
        }

        StartWatcher(session);
        LaunchEditor(_editorPath, localPath);
    }

    /// <summary>
    /// Closes an active edit session, stopping the file watcher and cleaning up
    /// the temporary local file.
    /// </summary>
    /// <param name="remotePath">Remote path of the file being edited.</param>
    public void CloseEdit(string remotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);

        if (_activeSessions.TryRemove(remotePath, out var session))
        {
            DrainSession(session);
            CleanupTempFile(session.LocalPath);
        }
    }

    /// <summary>Returns the list of remote paths currently open for editing.</summary>
    public IReadOnlyList<string> GetActiveEdits()
    {
        return _activeSessions.Keys.ToList();
    }

    internal IReadOnlyDictionary<string, EditSession> ActiveSessionsForTesting => _activeSessions;

    internal bool AddSessionForTesting(EditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _activeSessions.TryAdd(session.RemotePath, session);
    }

    internal void TriggerOnFileChangedForTesting(EditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        OnFileChanged(session);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var kvp in _activeSessions.ToArray())
        {
            if (_activeSessions.TryRemove(kvp.Key, out var session))
            {
                DrainSession(session);
                CleanupTempFile(session.LocalPath);
            }
        }

        _activeSessions.Clear();
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private static string CreateTempFilePath(string remotePath)
    {
        string fileName = Path.GetFileName(remotePath);
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "Heimdall",
            "edit",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDir);

        // Restrict temp directory ACL — edited files may contain sensitive
        // server configs (root-owned files downloaded via sudo)
        if (OperatingSystem.IsWindows())
        {
            try { Heimdall.Core.Security.AclEnforcer.SetDirectoryAcl(tempDir); }
            catch (Exception ex)
            {
                Heimdall.Core.Logging.FileLogger.Error(
                    $"Failed to restrict temp directory ACL for SFTP editor — edited files may be readable by other users: {ex.Message}");
            }
        }

        return Path.Combine(tempDir, fileName);
    }

    private static void CleanupTempFile(string localPath)
    {
        try
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }

            string? dir = Path.GetDirectoryName(localPath);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: false);
            }
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"RemoteFileEditor temp cleanup failed for {localPath}: {ex.Message}");
        }
    }

    private void StartWatcher(EditSession session)
    {
        string? directory = Path.GetDirectoryName(session.LocalPath);
        string fileName = Path.GetFileName(session.LocalPath);

        if (directory is null)
        {
            return;
        }

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            // Atomic-save editors (VS Code, Notepad++, Sublime) write to a temp file
            // then rename, so we need FileName and Size in addition to LastWrite.
            NotifyFilter = NotifyFilters.LastWrite
                         | NotifyFilters.FileName
                         | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, _) => OnFileChanged(session);
        watcher.Created += (_, _) => OnFileChanged(session);
        watcher.Renamed += (_, _) => OnFileChanged(session);

        session.Watcher = watcher;
    }

    private void OnFileChanged(EditSession session)
    {
        CancellationToken ct;
        try
        {
            ct = session.UploadCts.Token;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        var upload = OnFileChangedAsync(session, ct);
        session.CurrentUpload = upload;
        _ = upload.ContinueWith(
            static task =>
            {
                if (task.Exception is { } exception)
                {
                    Heimdall.Core.Logging.FileLogger.Warn(
                        $"RemoteFileEditor auto-upload task faulted unexpectedly: {exception.GetBaseException().Message}");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task OnFileChangedAsync(EditSession session, CancellationToken ct)
    {
        var enteredSemaphore = false;

        if (!session.ShouldUpload)
        {
            return;
        }

        bool success;
        try
        {
            // Serialize uploads per file — prevents concurrent saves from overlapping
            if (!await session.UploadSemaphore.WaitAsync(0, ct).ConfigureAwait(false))
            {
                return; // Another upload is in progress, skip (debounce will catch the next one)
            }

            enteredSemaphore = true;
            ct.ThrowIfCancellationRequested();
            session.LastUploadTime = DateTime.UtcNow;

            if (session.IsSudo && session.SshParams is not null)
            {
                await UploadWithSudoAsync(session, ct).ConfigureAwait(false);
            }
            else
            {
                await _browser.UploadFileAsync(
                    session.LocalPath,
                    session.RemotePath,
                    ct).ConfigureAwait(false);
            }

            success = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Heimdall.Core.Logging.FileLogger.Info(
                $"RemoteFileEditor auto-upload cancelled for {session.RemotePath}.");
            FileUploaded?.Invoke(session.RemotePath, false);
            return;
        }
        catch (Heimdall.Ssh.HostKeyRejectedException ex)
        {
            // A host key change between the open-edit step and the upload step
            // is a security event, not a benign upload failure.
            Heimdall.Core.Logging.FileLogger.Error(
                $"RemoteFileEditor: host key rejected during upload of {session.RemotePath} "
                + $"({ex.Host}:{ex.Port}, presented={ex.PresentedFingerprint}, stored={ex.StoredFingerprint ?? "<none>"}). Upload aborted.");
            HostKeyRotatedDuringUpload?.Invoke(new HostKeyRotationEvent(
                session.RemotePath,
                ex.PresentedFingerprint,
                ex.StoredFingerprint,
                ex.Host,
                ex.Port));
            FileUploaded?.Invoke(session.RemotePath, false);
            return;
        }
        catch (Exception ex)
        {
            success = false;
            Heimdall.Core.Logging.FileLogger.Warn(
                $"RemoteFileEditor auto-upload failed for {session.RemotePath}: {ex.Message}");
        }
        finally
        {
            if (enteredSemaphore)
            {
                try
                {
                    session.UploadSemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                    // The session may have been closed while a non-cancellable
                    // upload API was still unwinding.
                }
            }
        }

        FileUploaded?.Invoke(session.RemotePath, success);
    }

    internal static string BuildSudoDownloadCommand(string remotePath)
    {
        string escapedPath = PathEscaper.EscapeForShell(remotePath);
        return $"sudo base64 -- {escapedPath}";
    }

    internal static async Task WriteBase64DecodedFileAsync(
        string localPath,
        string? base64Content,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Content ?? "");
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "sudo base64 returned invalid base64 output.",
                ex);
        }

        await File.WriteAllBytesAsync(localPath, bytes, ct).ConfigureAwait(false);
    }

    internal static async Task UploadWithSudoAsync(EditSession session, CancellationToken ct = default)
    {
        if (session.SshParams is null)
        {
            throw new InvalidOperationException("SSH parameters required for sudo upload.");
        }

        if (session.Verifier is null)
        {
            throw new InvalidOperationException(
                "Sudo edit session must have a cached pinned verifier; was the session created via EditFileSudoAsync?");
        }

        string escapedPath = PathEscaper.EscapeForShell(session.RemotePath);
        string tempRemotePath = $"{RemoteTempPrefix}edit_{Guid.NewGuid():N}";

        var connectionInfo = SshConnectionFactory.Create(session.SshParams);
        using var sftpClient = new SftpClient(connectionInfo);
        using var sshClient = new SshClient(connectionInfo);

        SshConnectionFactory.AttachPinnedHostKeyVerification(
            sftpClient,
            session.SshParams.Host,
            session.SshParams.Port,
            session.Verifier);
        SshConnectionFactory.AttachPinnedHostKeyVerification(
            sshClient,
            session.SshParams.Host,
            session.SshParams.Port,
            session.Verifier);

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            sftpClient.Connect();
            ct.ThrowIfCancellationRequested();
            sshClient.Connect();
        }, ct).ConfigureAwait(false);

        try
        {
            // Upload to temp location via SFTP (unprivileged)
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var fileStream = File.OpenRead(session.LocalPath);
                sftpClient.UploadFile(fileStream, tempRemotePath);
                ct.ThrowIfCancellationRequested();
            }, ct).ConfigureAwait(false);

            // Move to final location with sudo
            string escapedTemp = PathEscaper.EscapeForShell(tempRemotePath);
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var result = sshClient.RunCommand(
                    $"cat {escapedTemp} | sudo tee -- {escapedPath} > /dev/null");
                ct.ThrowIfCancellationRequested();

                if (result.ExitStatus != 0)
                {
                    throw new InvalidOperationException(
                        $"sudo tee failed (exit {result.ExitStatus}): {result.Error}");
                }
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            // Always clean up temp file, even if tee failed
            try
            {
                string escapedTemp2 = PathEscaper.EscapeForShell(tempRemotePath);
                using var rmCmd = sshClient.RunCommand($"sudo rm -f {escapedTemp2}");
            }
            catch (Exception ex)
            {
                Heimdall.Core.Logging.FileLogger.Warn(
                    $"RemoteFileEditor failed to clean up temp file {tempRemotePath}: {ex.Message}");
            }

            sftpClient.Disconnect();
            sshClient.Disconnect();
        }
    }

    private static void DrainSession(EditSession session)
    {
        try
        {
            session.UploadCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        var pendingUpload = session.CurrentUpload;
        if (pendingUpload is not null && !pendingUpload.IsCompleted)
        {
            try
            {
                if (!pendingUpload.Wait(UploadDrainTimeout))
                {
                    Heimdall.Core.Logging.FileLogger.Warn(
                        $"RemoteFileEditor upload drain timed out for {session.RemotePath}.");
                }
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(static inner =>
                inner is OperationCanceledException or TaskCanceledException))
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Heimdall.Core.Logging.FileLogger.Warn(
                    $"RemoteFileEditor upload drain observed a fault for {session.RemotePath}: {ex.Message}");
            }
        }

        session.Dispose();
    }

    /// <summary>
    /// Resolves the configured editor to a concrete executable path.
    /// </summary>
    /// <param name="editorPath">Configured editor path, or null/empty for the default editor.</param>
    /// <returns>The editor executable path to launch.</returns>
    internal static string ResolveEditorPath(string? editorPath)
    {
        var trimmed = editorPath?.Trim();
        var isDefault = string.IsNullOrEmpty(trimmed)
            || string.Equals(trimmed, "notepad.exe", StringComparison.OrdinalIgnoreCase);

        if (isDefault && OperatingSystem.IsWindows())
        {
            var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return Path.Combine(systemDirectory, "notepad.exe");
        }

        return string.IsNullOrEmpty(trimmed) ? "notepad.exe" : trimmed;
    }

    private static void LaunchEditor(string editorPath, string localPath)
    {
        Process? proc = null;
        try
        {
            var resolvedEditorPath = ResolveEditorPath(editorPath);
            var psi = new ProcessStartInfo
            {
                FileName = resolvedEditorPath,
                UseShellExecute = false
            };

            // ArgumentList performs proper Win32-aware quoting per arg, so a
            // local path containing quotes, spaces, or shell metacharacters
            // cannot break out of the editor argument.
            psi.ArgumentList.Add(localPath);
            proc = Process.Start(psi);
        }
        finally
        {
            proc?.Dispose();
        }
    }
}

/// <summary>
/// Carries details for a host-key rotation detected during a sudo auto-upload.
/// </summary>
public sealed record HostKeyRotationEvent(
    string RemotePath,
    string PresentedFingerprint,
    string? StoredFingerprint,
    string Host,
    int Port);

/// <summary>
/// Tracks state for a single remote file editing session.
/// </summary>
internal sealed class EditSession : IDisposable
{
    /// <summary>Full remote path of the file being edited.</summary>
    public required string RemotePath { get; init; }

    /// <summary>Local temp file path.</summary>
    public required string LocalPath { get; init; }

    /// <summary>Whether this file requires sudo for writes.</summary>
    public bool IsSudo { get; init; }

    /// <summary>SSH connection parameters for sudo operations. Null for non-sudo edits.</summary>
    public SshConnectionParams? SshParams { get; init; }

    /// <summary>
    /// Pinned host-key verifier resolved when the sudo edit session opened.
    /// Non-null for sudo sessions; null for direct-browser sessions.
    /// </summary>
    public PinnedFingerprintVerifier? Verifier { get; init; }

    /// <summary>File system watcher for auto-upload on save.</summary>
    public FileSystemWatcher? Watcher { get; set; }

    /// <summary>Serializes upload operations per file to prevent concurrent save races.</summary>
    public SemaphoreSlim UploadSemaphore { get; } = new(1, 1);

    /// <summary>Cancels in-flight auto-upload work when the edit session closes.</summary>
    public CancellationTokenSource UploadCts { get; } = new();

    /// <summary>Most recent auto-upload task, retained so teardown can observe it.</summary>
    public Task? CurrentUpload { get; set; }

    /// <summary>Timestamp of the last successful upload (UTC).</summary>
    public DateTime LastUploadTime { get; set; }

    /// <summary>
    /// Returns true if enough time has elapsed since the last upload to allow
    /// another upload (debounce guard).
    /// </summary>
    public bool ShouldUpload =>
        (DateTime.UtcNow - LastUploadTime) >= RemoteFileEditor.UploadDebounceInterval;

    /// <inheritdoc/>
    public void Dispose()
    {
        Watcher?.Dispose();
        UploadSemaphore.Dispose();
        UploadCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
