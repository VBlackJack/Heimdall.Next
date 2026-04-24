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

using Heimdall.Ssh;
using Heimdall.Core.Ssh;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Heimdall.Sftp;

/// <summary>
/// SFTP browser backed by SSH.NET's native <see cref="SftpClient"/>.
/// Provides async file operations with progress reporting and cancellation support.
/// </summary>
public sealed class SftpBrowser : IRemoteBrowser
{
    private SftpClient? _client;
    private bool _disposed;
    private readonly SemaphoreSlim _clientLock = new(1, 1);

    /// <summary>Raised when the current working directory changes.</summary>
    public event Action<string>? DirectoryChanged;

    /// <summary>Raised during file transfers to report progress.</summary>
    public event Action<SftpTransferProgress>? TransferProgress;

    /// <summary>
    /// Raised when the connection is lost. The parameter contains an error
    /// message if the disconnection was unexpected, or null for a clean disconnect.
    /// </summary>
    public event Action<string?>? Disconnected;

    /// <summary>Current remote working directory.</summary>
    public string CurrentDirectory { get; private set; } = "/";

    /// <summary>Whether the SFTP client is connected to the remote host.</summary>
    public bool IsConnected => _client?.IsConnected ?? false;

    /// <summary>
    /// Connects to the remote host using the supplied SSH connection parameters.
    /// </summary>
    /// <param name="connectionParams">SSH connection parameters (host, credentials, etc.).</param>
    /// <param name="hostKeyStore">Optional TOFU host key store for server verification.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Already connected.</exception>
    public async Task ConnectAsync(
        SshConnectionParams connectionParams,
        HostKeyStore? hostKeyStore = null,
        IHostKeyVerifier? hostKeyVerifier = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_client?.IsConnected == true)
        {
            throw new InvalidOperationException("SFTP browser is already connected.");
        }

        PinnedFingerprintVerifier? pinnedVerifier = null;
        if (hostKeyStore is not null)
        {
            if (hostKeyVerifier is null)
            {
                throw new InvalidOperationException("IHostKeyVerifier is required when HostKeyStore is provided.");
            }

            pinnedVerifier = await SshConnectionFactory.ResolveHostKeyAsync(
                    connectionParams,
                    hostKeyStore,
                    hostKeyVerifier,
                    ct)
                .ConfigureAwait(false);
        }

        var connectionInfo = SshConnectionFactory.Create(connectionParams);
        _client = new SftpClient(connectionInfo);

        if (pinnedVerifier is not null)
        {
            SshConnectionFactory.AttachPinnedHostKeyVerification(
                _client,
                connectionParams.Host,
                connectionParams.Port,
                pinnedVerifier);
        }

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            _client.Connect();
        }, ct).ConfigureAwait(false);

        _client.ErrorOccurred += OnErrorOccurred;

        CurrentDirectory = _client.WorkingDirectory ?? "/";
        DirectoryChanged?.Invoke(CurrentDirectory);
    }

    /// <summary>
    /// Lists all entries in the specified directory, or the current directory if
    /// <paramref name="path"/> is null.
    /// </summary>
    /// <param name="path">Remote directory path, or null for the current directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of file/directory entries (excluding "." and "..").</returns>
    public async Task<IReadOnlyList<SftpFileInfo>> ListDirectoryAsync(
        string? path = null,
        CancellationToken ct = default)
    {
        var client = GetConnectedClient();
        string targetPath = path ?? CurrentDirectory;

        await _clientLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return client.ListDirectory(targetPath);
            }, ct).ConfigureAwait(false);

            var result = new List<SftpFileInfo>();
            foreach (ISftpFile entry in entries)
            {
                if (entry.Name is "." or "..")
                {
                    continue;
                }

                result.Add(ToSftpFileInfo(entry));
            }

            return result;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>Returns the current remote working directory path.</summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> GetCurrentDirectoryAsync(CancellationToken ct = default)
    {
        var client = GetConnectedClient();

        string dir = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return client.WorkingDirectory ?? "/";
        }, ct).ConfigureAwait(false);

        return dir;
    }

    /// <summary>
    /// Changes the current directory to <paramref name="path"/> and raises
    /// <see cref="DirectoryChanged"/>.
    /// </summary>
    /// <param name="path">Absolute or relative remote path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ChangeDirectoryAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var client = GetConnectedClient();

        await _clientLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                client.ChangeDirectory(path);
            }, ct).ConfigureAwait(false);

            CurrentDirectory = client.WorkingDirectory ?? "/";
            DirectoryChanged?.Invoke(CurrentDirectory);
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>
    /// Downloads a remote file to a local path, reporting progress via
    /// <see cref="TransferProgress"/>.
    /// </summary>
    /// <param name="remotePath">Full remote file path.</param>
    /// <param name="localPath">Local destination path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DownloadFileAsync(
        string remotePath,
        string localPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        var client = GetConnectedClient();

        string fileName = Path.GetFileName(remotePath);
        long totalBytes = 0;

        await _clientLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Retrieve file size for progress reporting
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var attrs = client.GetAttributes(remotePath);
                totalBytes = attrs.Size;
            }, ct).ConfigureAwait(false);

            await using var fileStream = new FileStream(
                localPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                client.DownloadFile(remotePath, fileStream, bytesTransferred =>
                {
                    ct.ThrowIfCancellationRequested();
                    TransferProgress?.Invoke(new SftpTransferProgress(
                        fileName,
                        (long)bytesTransferred,
                        totalBytes,
                        IsUpload: false));
                });
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>
    /// Uploads a local file to a remote path, reporting progress via
    /// <see cref="TransferProgress"/>.
    /// </summary>
    /// <param name="localPath">Local source file path.</param>
    /// <param name="remotePath">Full remote destination path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UploadFileAsync(
        string localPath,
        string remotePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        var client = GetConnectedClient();

        string fileName = Path.GetFileName(localPath);
        var fileInfo = new FileInfo(localPath);
        long totalBytes = fileInfo.Length;

        await _clientLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var fileStream = new FileStream(
                localPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                client.UploadFile(fileStream, remotePath, bytesTransferred =>
                {
                    ct.ThrowIfCancellationRequested();
                    TransferProgress?.Invoke(new SftpTransferProgress(
                        fileName,
                        (long)bytesTransferred,
                        totalBytes,
                        IsUpload: true));
                });
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>Creates a directory on the remote host.</summary>
    /// <param name="path">Full remote path for the new directory.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var client = GetConnectedClient();

        await _clientLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                client.CreateDirectory(path);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>
    /// Deletes a file or directory. Directories are deleted recursively.
    /// </summary>
    /// <param name="path">Full remote path to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var client = GetConnectedClient();

        await _clientLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var attrs = client.GetAttributes(path);

                if (attrs.IsDirectory)
                {
                    DeleteDirectoryRecursive(client, path, ct);
                }
                else
                {
                    client.DeleteFile(path);
                }
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>
    /// Changes the POSIX permissions of a remote file or directory.
    /// </summary>
    /// <param name="path">Full remote path.</param>
    /// <param name="mode">Permission mode as a short (e.g., 0x1ED for 755 octal).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ChmodAsync(string path, short mode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var client = GetConnectedClient();

        await _clientLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var attrs = client.GetAttributes(path);

                attrs.OwnerCanRead = (mode & 0x100) != 0;
                attrs.OwnerCanWrite = (mode & 0x080) != 0;
                attrs.OwnerCanExecute = (mode & 0x040) != 0;
                attrs.GroupCanRead = (mode & 0x020) != 0;
                attrs.GroupCanWrite = (mode & 0x010) != 0;
                attrs.GroupCanExecute = (mode & 0x008) != 0;
                attrs.OthersCanRead = (mode & 0x004) != 0;
                attrs.OthersCanWrite = (mode & 0x002) != 0;
                attrs.OthersCanExecute = (mode & 0x001) != 0;

                client.SetAttributes(path, attrs);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>Renames (moves) a remote file or directory.</summary>
    /// <param name="oldPath">Current remote path.</param>
    /// <param name="newPath">New remote path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RenameAsync(
        string oldPath,
        string newPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
        var client = GetConnectedClient();

        await _clientLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                client.RenameFile(oldPath, newPath);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>Disconnects from the remote host and releases the SFTP client.</summary>
    public void Disconnect()
    {
        if (_client is null)
        {
            return;
        }

        _client.ErrorOccurred -= OnErrorOccurred;

        if (_client.IsConnected)
        {
            try
            {
                _client.Disconnect();
            }
            catch (Exception ex)
            {
                Heimdall.Core.Logging.FileLogger.Warn($"[SftpBrowser] disconnect: {ex.Message}");
            }
        }

        _client.Dispose();
        _client = null;

        Disconnected?.Invoke(null);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disconnect();
        _clientLock.Dispose();
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private SftpClient GetConnectedClient()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_client is null || !_client.IsConnected)
        {
            throw new InvalidOperationException("SFTP browser is not connected.");
        }

        return _client;
    }

    private void OnErrorOccurred(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
    {
        Disconnected?.Invoke(e.Exception.Message);
    }

    private static void DeleteDirectoryRecursive(
        SftpClient client,
        string path,
        CancellationToken ct)
    {
        foreach (ISftpFile entry in client.ListDirectory(path))
        {
            ct.ThrowIfCancellationRequested();

            if (entry.Name is "." or "..")
            {
                continue;
            }

            string fullPath = $"{path.TrimEnd('/')}/{entry.Name}";

            if (entry.IsDirectory)
            {
                DeleteDirectoryRecursive(client, fullPath, ct);
            }
            else
            {
                client.DeleteFile(fullPath);
            }
        }

        client.DeleteDirectory(path);
    }

    private static SftpFileInfo ToSftpFileInfo(ISftpFile entry)
    {
        // Build a rwxrwxrwx permission string from the file attributes
        string permissions = FormatPermissions(entry);

        return new SftpFileInfo(
            Name: entry.Name,
            FullPath: entry.FullName,
            IsDirectory: entry.IsDirectory,
            Size: entry.Attributes.Size,
            LastModified: entry.LastWriteTimeUtc,
            Permissions: permissions,
            Owner: entry.Attributes.GetOwnerIdOrDefault().ToString(),
            Group: entry.Attributes.GetGroupIdOrDefault().ToString());
    }

    private static string FormatPermissions(ISftpFile entry)
    {
        var attrs = entry.Attributes;

        // SSH.NET exposes permission bits via Attributes
        // Build standard rwxrwxrwx string from the octal permissions
        int mode = attrs.GetPermissionsOrDefault();

        return string.Create(9, mode, static (span, m) =>
        {
            span[0] = (m & 0x100) != 0 ? 'r' : '-';
            span[1] = (m & 0x080) != 0 ? 'w' : '-';
            span[2] = (m & 0x040) != 0 ? 'x' : '-';
            span[3] = (m & 0x020) != 0 ? 'r' : '-';
            span[4] = (m & 0x010) != 0 ? 'w' : '-';
            span[5] = (m & 0x008) != 0 ? 'x' : '-';
            span[6] = (m & 0x004) != 0 ? 'r' : '-';
            span[7] = (m & 0x002) != 0 ? 'w' : '-';
            span[8] = (m & 0x001) != 0 ? 'x' : '-';
        });
    }
}

/// <summary>
/// Extension helpers to safely read SSH.NET <see cref="SftpFileAttributes"/> fields
/// that may not be present in all SFTP server implementations.
/// </summary>
internal static class SftpFileAttributesExtensions
{
    public static int GetOwnerIdOrDefault(this SftpFileAttributes attrs)
    {
        try { return attrs.UserId; }
        catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[SftpBrowser] read UserId: {ex.Message}"); return -1; }
    }

    public static int GetGroupIdOrDefault(this SftpFileAttributes attrs)
    {
        try { return attrs.GroupId; }
        catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[SftpBrowser] read GroupId: {ex.Message}"); return -1; }
    }

    public static int GetPermissionsOrDefault(this SftpFileAttributes attrs)
    {
        try
        {
            // SftpFileAttributes exposes permissions as individual bool properties
            // but we need the raw octal. Calculate from the bool flags.
            int mode = 0;
            if (attrs.OwnerCanRead) mode |= 0x100;
            if (attrs.OwnerCanWrite) mode |= 0x080;
            if (attrs.OwnerCanExecute) mode |= 0x040;
            if (attrs.GroupCanRead) mode |= 0x020;
            if (attrs.GroupCanWrite) mode |= 0x010;
            if (attrs.GroupCanExecute) mode |= 0x008;
            if (attrs.OthersCanRead) mode |= 0x004;
            if (attrs.OthersCanWrite) mode |= 0x002;
            if (attrs.OthersCanExecute) mode |= 0x001;
            return mode;
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn($"[SftpBrowser] read permissions: {ex.Message}");
            return 0;
        }
    }
}

/// <summary>Represents a file or directory entry from a remote SFTP listing.</summary>
/// <param name="Name">File or directory name (without path).</param>
/// <param name="FullPath">Full remote path.</param>
/// <param name="IsDirectory">Whether this entry is a directory.</param>
/// <param name="Size">File size in bytes (0 for directories).</param>
/// <param name="LastModified">Last modification time (UTC).</param>
/// <param name="Permissions">POSIX permission string, e.g., "rwxr-xr-x".</param>
/// <param name="Owner">Numeric owner ID as a string.</param>
/// <param name="Group">Numeric group ID as a string.</param>
public sealed record SftpFileInfo(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime LastModified,
    string Permissions,
    string Owner,
    string Group);

/// <summary>Progress information for an SFTP file transfer.</summary>
/// <param name="FileName">Name of the file being transferred.</param>
/// <param name="BytesTransferred">Number of bytes transferred so far.</param>
/// <param name="TotalBytes">Total file size in bytes.</param>
/// <param name="IsUpload">True for uploads, false for downloads.</param>
public sealed record SftpTransferProgress(
    string FileName,
    long BytesTransferred,
    long TotalBytes,
    bool IsUpload);
