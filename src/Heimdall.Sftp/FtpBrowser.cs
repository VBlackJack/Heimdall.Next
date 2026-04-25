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

using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace Heimdall.Sftp;

/// <summary>
/// FTP file browser backed by <see cref="FtpWebRequest"/> (.NET built-in).
/// Provides the same <see cref="IRemoteBrowser"/> surface as <see cref="SftpBrowser"/>
/// so the embedded file browser view can work with both SFTP and FTP.
/// </summary>
public sealed partial class FtpBrowser : IRemoteBrowser
{
    private const int DefaultBufferSize = 81920;

    private string? _host;
    private int _port;
    private NetworkCredential? _credential;
    private bool _disposed;
    private bool _connected;
    private bool _passiveMode = true;
    private bool _useSsl;
    private readonly SemaphoreSlim _opLock = new(1, 1);

    /// <inheritdoc/>
    public event Action<string>? DirectoryChanged;

    /// <inheritdoc/>
    public event Action<SftpTransferProgress>? TransferProgress;

    /// <inheritdoc/>
    public event Action<string?>? Disconnected;

    /// <inheritdoc/>
    public string CurrentDirectory { get; private set; } = "/";

    /// <inheritdoc/>
    public bool IsConnected => _connected;

    /// <summary>
    /// Connects to the FTP server with the supplied credentials.
    /// </summary>
    public async Task ConnectAsync(
        string host,
        int port,
        string? username,
        string? password,
        bool passiveMode = true,
        bool useSsl = false,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connected)
        {
            throw new InvalidOperationException("FTP browser is already connected.");
        }

        _host = host;
        _port = port > 0 ? port : 21;
        _passiveMode = passiveMode;
        _useSsl = useSsl;
        _credential = new NetworkCredential(
            string.IsNullOrEmpty(username) ? "anonymous" : username,
            password ?? string.Empty);

        // FTP without TLS sends credentials in clear text. Surface this loudly
        // in the log so an operator reviewing connection history can spot when
        // a session was actually transmitted unencrypted, even if the UI shows
        // a green "connected" state.
        if (!useSsl && !string.IsNullOrEmpty(username))
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"FtpBrowser: connecting to ftp://{host}:{_port} without TLS — username and password will be transmitted in clear text. Prefer SFTP when available.");
        }

        // Verify connectivity by listing the root directory
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var request = CreateRequest("/", WebRequestMethods.Ftp.ListDirectory);
            using var response = (FtpWebResponse)request.GetResponse();
            response.Close();
        }, ct).ConfigureAwait(false);

        _connected = true;
        CurrentDirectory = "/";
        DirectoryChanged?.Invoke(CurrentDirectory);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SftpFileInfo>> ListDirectoryAsync(
        string? path = null,
        CancellationToken ct = default)
    {
        EnsureConnected();
        string targetPath = NormalizePath(path ?? CurrentDirectory);

        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return ListDirectoryDetailed(targetPath);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task<string> GetCurrentDirectoryAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        return Task.FromResult(CurrentDirectory);
    }

    /// <inheritdoc/>
    public async Task ChangeDirectoryAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureConnected();

        string resolved = ResolvePath(path);

        // Verify the directory exists by listing it
        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var request = CreateRequest(resolved, WebRequestMethods.Ftp.ListDirectory);
                using var response = (FtpWebResponse)request.GetResponse();
                response.Close();
            }, ct).ConfigureAwait(false);

            CurrentDirectory = resolved;
            DirectoryChanged?.Invoke(CurrentDirectory);
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DownloadFileAsync(
        string remotePath,
        string localPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        EnsureConnected();

        string fileName = Path.GetFileName(remotePath);

        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Get file size for progress reporting
            long totalBytes = 0;
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var sizeRequest = CreateRequest(remotePath, WebRequestMethods.Ftp.GetFileSize);
                    using var sizeResponse = (FtpWebResponse)sizeRequest.GetResponse();
                    totalBytes = sizeResponse.ContentLength;
                    sizeResponse.Close();
                }
                catch
                {
                    // Some FTP servers do not support SIZE; proceed without progress
                }
            }, ct).ConfigureAwait(false);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var request = CreateRequest(remotePath, WebRequestMethods.Ftp.DownloadFile);
                using var response = (FtpWebResponse)request.GetResponse();
                using var responseStream = response.GetResponseStream();
                using var fileStream = new FileStream(
                    localPath, FileMode.Create, FileAccess.Write, FileShare.None, DefaultBufferSize);

                var buffer = new byte[DefaultBufferSize];
                long bytesTransferred = 0;
                int bytesRead;

                while ((bytesRead = responseStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    fileStream.Write(buffer, 0, bytesRead);
                    bytesTransferred += bytesRead;

                    TransferProgress?.Invoke(new SftpTransferProgress(
                        fileName, bytesTransferred, totalBytes, IsUpload: false));
                }
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UploadFileAsync(
        string localPath,
        string remotePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        EnsureConnected();

        string fileName = Path.GetFileName(localPath);
        long totalBytes = new FileInfo(localPath).Length;

        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var request = CreateRequest(remotePath, WebRequestMethods.Ftp.UploadFile);
                request.ContentLength = totalBytes;

                using var fileStream = new FileStream(
                    localPath, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize);
                using var requestStream = request.GetRequestStream();

                var buffer = new byte[DefaultBufferSize];
                long bytesTransferred = 0;
                int bytesRead;

                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    requestStream.Write(buffer, 0, bytesRead);
                    bytesTransferred += bytesRead;

                    TransferProgress?.Invoke(new SftpTransferProgress(
                        fileName, bytesTransferred, totalBytes, IsUpload: true));
                }

                using var response = (FtpWebResponse)request.GetResponse();
                response.Close();
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureConnected();

        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var request = CreateRequest(path, WebRequestMethods.Ftp.MakeDirectory);
                using var response = (FtpWebResponse)request.GetResponse();
                response.Close();
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureConnected();

        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                // Try deleting as file first; if that fails, try as directory
                try
                {
                    var request = CreateRequest(path, WebRequestMethods.Ftp.DeleteFile);
                    using var response = (FtpWebResponse)request.GetResponse();
                    response.Close();
                }
                catch (WebException)
                {
                    var request = CreateRequest(path, WebRequestMethods.Ftp.RemoveDirectory);
                    using var response = (FtpWebResponse)request.GetResponse();
                    response.Close();
                }
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task ChmodAsync(string path, short mode, CancellationToken ct = default)
    {
        // FTP does not natively support chmod; this is a no-op.
        // Some servers support SITE CHMOD but it is non-standard.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPath);
        EnsureConnected();

        await _opLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var request = CreateRequest(oldPath, WebRequestMethods.Ftp.Rename);
                request.RenameTo = newPath;
                using var response = (FtpWebResponse)request.GetResponse();
                response.Close();
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _opLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        _connected = false;
        _credential = null;
        _host = null;
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
        _opLock.Dispose();
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_connected || _host is null)
        {
            throw new InvalidOperationException("FTP browser is not connected.");
        }
    }

    private FtpWebRequest CreateRequest(string remotePath, string method)
    {
        string normalizedPath = NormalizePath(remotePath);
        var uri = new Uri($"ftp://{_host}:{_port}{normalizedPath}");

#pragma warning disable SYSLIB0014 // FtpWebRequest is obsolete but no built-in replacement exists
        var request = (FtpWebRequest)WebRequest.Create(uri);
#pragma warning restore SYSLIB0014

        request.Method = method;
        request.Credentials = _credential!;
        request.UseBinary = true;
        request.UsePassive = _passiveMode;
        request.EnableSsl = _useSsl;
        request.KeepAlive = false;
        request.Timeout = 30_000;

        return request;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        // Ensure path starts with /
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        // Remove trailing slash unless it is the root
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path.TrimEnd('/');
        }

        return path;
    }

    private string ResolvePath(string path)
    {
        if (path.StartsWith('/'))
        {
            return NormalizePath(path);
        }

        // Relative path: resolve against current directory
        string basePath = CurrentDirectory.TrimEnd('/');
        return NormalizePath($"{basePath}/{path}");
    }

    /// <summary>
    /// Lists directory contents using LIST (detailed format) and parses
    /// Unix-style or DOS-style directory listings.
    /// </summary>
    private IReadOnlyList<SftpFileInfo> ListDirectoryDetailed(string path)
    {
        var request = CreateRequest(path, WebRequestMethods.Ftp.ListDirectoryDetails);
        using var response = (FtpWebResponse)request.GetResponse();
        using var reader = new StreamReader(response.GetResponseStream());

        var result = new List<SftpFileInfo>();
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = ParseListLine(line, path);
            if (entry is not null && entry.Name is not "." and not "..")
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>
    /// Hard cap on the length of a filename extracted from an FTP LIST response.
    /// Defends against a hostile or buggy server padding entries with megabytes
    /// of trailing garbage that the regex would otherwise capture wholesale.
    /// </summary>
    internal const int MaxFtpFilenameLength = 4096;

    /// <summary>
    /// Parses a single line from an FTP LIST response.
    /// Supports Unix-style (drwxr-xr-x ...) and DOS-style (01-01-2026 ...) formats.
    /// </summary>
    private static SftpFileInfo? ParseListLine(string line, string parentPath)
    {
        // Try Unix-style format first: drwxr-xr-x 2 user group 4096 Jan 01 12:00 filename
        var unixMatch = UnixListRegex().Match(line);
        if (unixMatch.Success)
        {
            string permissions = unixMatch.Groups[1].Value;
            bool isDirectory = permissions.StartsWith('d');
            long size = long.TryParse(unixMatch.Groups[4].Value, CultureInfo.InvariantCulture, out var s) ? s : 0;
            string dateStr = unixMatch.Groups[5].Value;
            string name = unixMatch.Groups[6].Value;

            if (name.Length > MaxFtpFilenameLength)
            {
                return null;
            }

            DateTime lastModified = ParseUnixDate(dateStr);
            string fullPath = parentPath.TrimEnd('/') + "/" + name;

            return new SftpFileInfo(
                Name: name,
                FullPath: fullPath,
                IsDirectory: isDirectory,
                Size: isDirectory ? 0 : size,
                LastModified: lastModified,
                Permissions: permissions.Length >= 10 ? permissions[1..] : permissions,
                Owner: unixMatch.Groups[2].Value,
                Group: unixMatch.Groups[3].Value);
        }

        // Try DOS-style format: 01-01-26 12:00PM <DIR> filename
        var dosMatch = DosListRegex().Match(line);
        if (dosMatch.Success)
        {
            string dateStr = dosMatch.Groups[1].Value + " " + dosMatch.Groups[2].Value;
            bool isDirectory = dosMatch.Groups[3].Value.Trim().Equals("<DIR>", StringComparison.OrdinalIgnoreCase);
            string sizeStr = dosMatch.Groups[3].Value.Trim();
            long size = 0;
            if (!isDirectory)
            {
                long.TryParse(sizeStr, CultureInfo.InvariantCulture, out size);
            }
            string name = dosMatch.Groups[4].Value;

            if (name.Length > MaxFtpFilenameLength)
            {
                return null;
            }

            DateTime.TryParse(dateStr, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var lastModified);

            string fullPath = parentPath.TrimEnd('/') + "/" + name;

            return new SftpFileInfo(
                Name: name,
                FullPath: fullPath,
                IsDirectory: isDirectory,
                Size: size,
                LastModified: lastModified,
                Permissions: isDirectory ? "rwxr-xr-x" : "rw-r--r--",
                Owner: "-",
                Group: "-");
        }

        return null;
    }

    private static DateTime ParseUnixDate(string dateStr)
    {
        // Formats: "Jan 01 12:00" or "Jan 01  2025"
        if (DateTime.TryParseExact(dateStr.Trim(), new[] { "MMM dd HH:mm", "MMM dd  yyyy", "MMM  d HH:mm", "MMM  d  yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            // If the parsed date has no year component and is in the future, subtract a year
            if (result.Year == DateTime.Now.Year && result > DateTime.Now)
            {
                result = result.AddYears(-1);
            }

            return result;
        }

        return DateTime.MinValue;
    }

    [GeneratedRegex(@"^([drwxstSTlL\-]{10})\s+\d+\s+(\S+)\s+(\S+)\s+(\d+)\s+(\w{3}\s+\d{1,2}\s+[\d:]+)\s+(.+)$")]
    private static partial Regex UnixListRegex();

    [GeneratedRegex(@"^(\d{2}-\d{2}-\d{2,4})\s+(\d{2}:\d{2}[APMapm]{2})\s+(<DIR>|\d+)\s+(.+)$")]
    private static partial Regex DosListRegex();
}
