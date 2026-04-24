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

    private readonly IRemoteBrowser _browser;
    private readonly string _editorPath;
    private readonly HostKeyStore? _hostKeyStore;
    private readonly IHostKeyVerifier? _hostKeyVerifier;
    private readonly ConcurrentDictionary<string, EditSession> _activeSessions = new();
    private bool _disposed;

    /// <summary>
    /// Raised after an auto-upload attempt. Parameters: remote path, success flag.
    /// </summary>
    public event Action<string, bool>? FileUploaded;

    /// <summary>
    /// Creates a new <see cref="RemoteFileEditor"/> backed by the given SFTP browser.
    /// </summary>
    /// <param name="browser">Connected SFTP browser used for file transfers.</param>
    /// <param name="editorPath">
    /// Path to the external editor executable (defaults to <c>notepad.exe</c>).
    /// </param>
    /// <param name="hostKeyStore">
    /// Optional TOFU host key store for server verification on sudo SSH connections.
    /// </param>
    public RemoteFileEditor(
        IRemoteBrowser browser,
        string editorPath = "notepad.exe",
        HostKeyStore? hostKeyStore = null,
        IHostKeyVerifier? hostKeyVerifier = null)
    {
        ArgumentNullException.ThrowIfNull(browser);
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
        string escapedPath = PathEscaper.EscapeForShell(remotePath);

        PinnedFingerprintVerifier? pinnedVerifier = null;
        if (_hostKeyStore is not null)
        {
            if (_hostKeyVerifier is null)
            {
                throw new InvalidOperationException("IHostKeyVerifier is required when HostKeyStore is provided.");
            }

            pinnedVerifier = await SshConnectionFactory.ResolveHostKeyAsync(
                    sshParams,
                    _hostKeyStore,
                    _hostKeyVerifier,
                    ct)
                .ConfigureAwait(false);
        }

        // Download with sudo via SSH command
        var connectionInfo = SshConnectionFactory.Create(sshParams);
        using var sshClient = new SshClient(connectionInfo);

        if (pinnedVerifier is not null)
        {
            SshConnectionFactory.AttachPinnedHostKeyVerification(
                sshClient,
                sshParams.Host,
                sshParams.Port,
                pinnedVerifier);
        }

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            sshClient.Connect();
        }, ct).ConfigureAwait(false);

        try
        {
            using var downloadCmd = await Task.Run(() =>
                sshClient.RunCommand($"sudo cat {escapedPath}"),
                ct).ConfigureAwait(false);

            if (downloadCmd.ExitStatus != 0)
            {
                throw new InvalidOperationException(
                    $"sudo cat failed (exit {downloadCmd.ExitStatus}): {downloadCmd.Error}");
            }

            // Write downloaded content to local temp file
            var result = downloadCmd.Result ?? "";
            await File.WriteAllTextAsync(
                localPath,
                result,
                System.Text.Encoding.UTF8,
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
            HostKeyStore = _hostKeyStore,
            HostKeyVerifier = _hostKeyVerifier,
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
            session.Dispose();
            CleanupTempFile(session.LocalPath);
        }
    }

    /// <summary>Returns the list of remote paths currently open for editing.</summary>
    public IReadOnlyList<string> GetActiveEdits()
    {
        return _activeSessions.Keys.ToList();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var kvp in _activeSessions)
        {
            kvp.Value.Dispose();
            CleanupTempFile(kvp.Value.LocalPath);
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
        _ = OnFileChangedAsync(session);
    }

    private async Task OnFileChangedAsync(EditSession session)
    {
        if (!session.ShouldUpload)
        {
            return;
        }

        // Serialize uploads per file — prevents concurrent saves from overlapping
        if (!await session.UploadSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            return; // Another upload is in progress, skip (debounce will catch the next one)
        }

        bool success;
        try
        {
            session.LastUploadTime = DateTime.UtcNow;

            if (session.IsSudo && session.SshParams is not null)
            {
                await UploadWithSudoAsync(session).ConfigureAwait(false);
            }
            else
            {
                await _browser.UploadFileAsync(
                    session.LocalPath,
                    session.RemotePath).ConfigureAwait(false);
            }

            success = true;
        }
        catch (Exception ex)
        {
            success = false;
            Heimdall.Core.Logging.FileLogger.Warn(
                $"RemoteFileEditor auto-upload failed for {session.RemotePath}: {ex.Message}");
        }
        finally
        {
            session.UploadSemaphore.Release();
        }

        FileUploaded?.Invoke(session.RemotePath, success);
    }

    private static async Task UploadWithSudoAsync(EditSession session)
    {
        if (session.SshParams is null)
        {
            throw new InvalidOperationException("SSH parameters required for sudo upload.");
        }

        string escapedPath = PathEscaper.EscapeForShell(session.RemotePath);
        string tempRemotePath = $"{RemoteTempPrefix}edit_{Guid.NewGuid():N}";

        PinnedFingerprintVerifier? pinnedVerifier = null;
        if (session.HostKeyStore is not null)
        {
            if (session.HostKeyVerifier is null)
            {
                throw new InvalidOperationException("IHostKeyVerifier is required when HostKeyStore is provided.");
            }

            pinnedVerifier = await SshConnectionFactory.ResolveHostKeyAsync(
                    session.SshParams,
                    session.HostKeyStore,
                    session.HostKeyVerifier)
                .ConfigureAwait(false);
        }

        var connectionInfo = SshConnectionFactory.Create(session.SshParams);
        using var sftpClient = new SftpClient(connectionInfo);
        using var sshClient = new SshClient(connectionInfo);

        if (pinnedVerifier is not null)
        {
            SshConnectionFactory.AttachPinnedHostKeyVerification(
                sftpClient,
                session.SshParams.Host,
                session.SshParams.Port,
                pinnedVerifier);
            SshConnectionFactory.AttachPinnedHostKeyVerification(
                sshClient,
                session.SshParams.Host,
                session.SshParams.Port,
                pinnedVerifier);
        }

        await Task.Run(() =>
        {
            sftpClient.Connect();
            sshClient.Connect();
        }).ConfigureAwait(false);

        try
        {
            // Upload to temp location via SFTP (unprivileged)
            await Task.Run(() =>
            {
                using var fileStream = File.OpenRead(session.LocalPath);
                sftpClient.UploadFile(fileStream, tempRemotePath);
            }).ConfigureAwait(false);

            // Move to final location with sudo
            string escapedTemp = PathEscaper.EscapeForShell(tempRemotePath);
            await Task.Run(() =>
            {
                using var result = sshClient.RunCommand(
                    $"cat {escapedTemp} | sudo tee -- {escapedPath} > /dev/null");

                if (result.ExitStatus != 0)
                {
                    throw new InvalidOperationException(
                        $"sudo tee failed (exit {result.ExitStatus}): {result.Error}");
                }
            }).ConfigureAwait(false);
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

    private static void LaunchEditor(string editorPath, string localPath)
    {
        Process? proc = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(editorPath) &&
                !string.Equals(editorPath, "notepad.exe", StringComparison.OrdinalIgnoreCase))
            {
                proc = Process.Start(new ProcessStartInfo
                {
                    FileName = editorPath,
                    Arguments = $"\"{localPath}\"",
                    UseShellExecute = false
                });
            }
            else
            {
                proc = Process.Start(new ProcessStartInfo
                {
                    FileName = localPath,
                    UseShellExecute = true
                });
            }
        }
        finally
        {
            proc?.Dispose();
        }
    }
}

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

    /// <summary>TOFU host key store for server verification on sudo connections.</summary>
    public HostKeyStore? HostKeyStore { get; init; }

    /// <summary>Host key verifier for sudo SSH connections.</summary>
    public IHostKeyVerifier? HostKeyVerifier { get; init; }

    /// <summary>File system watcher for auto-upload on save.</summary>
    public FileSystemWatcher? Watcher { get; set; }

    /// <summary>Serializes upload operations per file to prevent concurrent save races.</summary>
    public SemaphoreSlim UploadSemaphore { get; } = new(1, 1);

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
        GC.SuppressFinalize(this);
    }
}
