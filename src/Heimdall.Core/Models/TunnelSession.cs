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

using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Heimdall.Core.Models;

/// <summary>
/// Represents an active SSH tunnel session with its associated process lifecycle.
/// </summary>
public partial class TunnelSession : ObservableObject, IDisposable
{
    private Process? _process;
    private bool _disposed;

    [ObservableProperty]
    private string _serverName = string.Empty;

    [ObservableProperty]
    private int _localPort;

    [ObservableProperty]
    private int _processId;

    [ObservableProperty]
    private DateTime _startTime = DateTime.UtcNow;

    /// <summary>
    /// The underlying tunnel process. Internal to prevent external manipulation.
    /// </summary>
    internal Process? Process
    {
        get => _process;
        set => _process = value;
    }

    /// <summary>
    /// Formatted start time for display purposes.
    /// </summary>
    public string StartTimeFormatted => StartTime.ToString("HH:mm:ss");

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing && _process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(1000);
                }

                _process.Dispose();
            }
            catch (InvalidOperationException)
            {
                // Process already exited between HasExited check and Kill call
            }
            finally
            {
                _process = null;
            }
        }

        _disposed = true;
    }

    ~TunnelSession()
    {
        Dispose(false);
    }
}
