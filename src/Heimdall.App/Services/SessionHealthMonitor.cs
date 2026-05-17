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
using Heimdall.Core.Configuration;
using Heimdall.Core.Logging;
using Heimdall.Core.SessionHealth;

namespace Heimdall.App.Services;

/// <summary>
/// Background reachability monitor. Loads the server inventory from
/// <see cref="IConfigManager"/> on every tick, runs a throttled batch of
/// <see cref="IHealthProbe"/> calls, and exposes the result both as a queryable
/// dictionary and as a per-server <see cref="StatusChanged"/> event the UI can
/// subscribe to.
/// </summary>
/// <remarks>
/// Gateway-fronted servers (<see cref="ServerProfileDto.SshGatewayId"/> set)
/// and servers whose protocol exposes no probe port (Citrix, Local Shell) are
/// recorded as <see cref="HealthStatus.Unknown"/> without consuming a probe
/// slot — the MVP scope chose direct TCP only.
/// </remarks>
public sealed class SessionHealthMonitor : IDisposable
{
    private readonly IConfigManager _configManager;
    private readonly IHealthProbe _probe;
    private readonly ConcurrentDictionary<string, HealthState> _states = new(StringComparer.Ordinal);
    private readonly object _lifecycleGate = new();

    private SemaphoreSlim? _throttle;
    private System.Threading.Timer? _timer;
    private CancellationTokenSource? _cycleCts;
    private AppSettings? _currentSettings;
    private bool _disposed;

    public event Action<string, HealthState>? StatusChanged;

    public SessionHealthMonitor(IConfigManager configManager, IHealthProbe probe)
    {
        _configManager = configManager;
        _probe = probe;
        _configManager.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>Current per-server state snapshot. Safe to enumerate concurrently.</summary>
    public IReadOnlyDictionary<string, HealthState> States => _states;

    /// <summary>Returns the last known state for a server, or <see cref="HealthState.Initial"/> when never probed.</summary>
    public HealthState GetState(string serverId)
        => _states.TryGetValue(serverId, out var state) ? state : HealthState.Initial;

    /// <summary>
    /// Boots the monitor with the given settings snapshot. Safe to call repeatedly;
    /// each call cancels the in-flight cycle and re-arms the timer with the new
    /// interval. When <see cref="AppSettings.SessionHealthMonitorEnabled"/> is
    /// false, the timer stays stopped and existing state is cleared.
    /// </summary>
    public void Start(AppSettings settings) => Start(settings, armTimer: true);

    /// <summary>
    /// Internal seam used by unit tests to set up the throttle and settings without
    /// arming the background Timer (which would race with manual
    /// <see cref="RunCycleAsync"/> calls).
    /// </summary>
    internal void Start(AppSettings settings, bool armTimer)
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;

            _currentSettings = settings;
            StopUnsafe();

            if (!settings.SessionHealthMonitorEnabled)
            {
                _states.Clear();
                return;
            }

            var intervalSeconds = Math.Max(15, settings.SessionHealthCheckIntervalSeconds);
            var maxConcurrent = Math.Max(1, settings.SessionHealthMaxConcurrent);

            _throttle = new SemaphoreSlim(maxConcurrent, maxConcurrent);
            _cycleCts = new CancellationTokenSource();
            if (armTimer)
            {
                _timer = new System.Threading.Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(intervalSeconds));
            }
        }
    }

    /// <summary>Stops the timer and cancels any in-flight cycle.</summary>
    public void Stop()
    {
        lock (_lifecycleGate)
        {
            StopUnsafe();
        }
    }

    private void StopUnsafe()
    {
        _timer?.Dispose();
        _timer = null;

        _cycleCts?.Cancel();
        _cycleCts?.Dispose();
        _cycleCts = null;

        _throttle?.Dispose();
        _throttle = null;
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
            _configManager.SettingsChanged -= OnSettingsChanged;
            StopUnsafe();
        }
    }

    private void OnSettingsChanged(AppSettings settings) => Start(settings);

    private async void OnTimerTick(object? _)
    {
        var cts = _cycleCts;
        if (cts is null || cts.IsCancellationRequested) return;

        try
        {
            await RunCycleAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cycle cancelled by Stop()/Dispose()/settings change — expected.
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"SessionHealthMonitor cycle failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes a single probe cycle. Exposed as internal so unit tests can drive
    /// the scheduler deterministically without relying on the Timer.
    /// </summary>
    internal async Task RunCycleAsync(CancellationToken ct)
    {
        var settings = _currentSettings;
        var throttle = _throttle;
        if (settings is null || throttle is null) return;

        var profiles = await _configManager.LoadServersAsync().ConfigureAwait(false);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        var tasks = new List<Task>(profiles.Count);
        foreach (var dto in profiles)
        {
            if (string.IsNullOrEmpty(dto.Id)) continue;
            seenIds.Add(dto.Id);
            tasks.Add(ProbeOneAsync(dto, settings.SessionHealthProbeTimeoutMs, throttle, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Drop state for servers removed from the inventory between cycles.
        foreach (var key in _states.Keys.ToList())
        {
            if (!seenIds.Contains(key))
            {
                _states.TryRemove(key, out _);
            }
        }
    }

    private async Task ProbeOneAsync(ServerProfileDto dto, int timeoutMs, SemaphoreSlim throttle, CancellationToken ct)
    {
        // Gateway-fronted servers and protocols without a probe port short-circuit
        // before they queue against the throttle, leaving slots free for probes
        // that will actually hit the network.
        if (!string.IsNullOrEmpty(dto.SshGatewayId))
        {
            PublishState(dto.Id, new HealthState(HealthStatus.Unknown, DateTime.UtcNow, null, "behind-gateway"));
            return;
        }

        var port = ResolveProbePort(dto);
        if (!port.HasValue || port.Value <= 0)
        {
            PublishState(dto.Id, new HealthState(HealthStatus.Unknown, DateTime.UtcNow, null, "no-port"));
            return;
        }

        if (string.IsNullOrWhiteSpace(dto.RemoteServer))
        {
            PublishState(dto.Id, new HealthState(HealthStatus.Unknown, DateTime.UtcNow, null, "no-host"));
            return;
        }

        await throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (ct.IsCancellationRequested) return;

            PublishState(dto.Id, new HealthState(HealthStatus.Probing, DateTime.UtcNow, null, null));
            var result = await _probe.ProbeAsync(dto.RemoteServer, port.Value, timeoutMs, ct).ConfigureAwait(false);
            PublishState(dto.Id, result);
        }
        finally
        {
            throttle.Release();
        }
    }

    /// <summary>
    /// Maps a server profile to its probe port. SSH and SFTP share the SSH port;
    /// Citrix (StoreFront HTTP/HTTPS in this codebase) and Local Shell are
    /// intentionally non-probable in the MVP.
    /// </summary>
    internal static int? ResolveProbePort(ServerProfileDto dto)
    {
        return (dto.ConnectionType ?? string.Empty).ToUpperInvariant() switch
        {
            "RDP" => dto.RemotePort,
            "SSH" or "SFTP" => dto.SshPort,
            "VNC" => dto.VncPort,
            "FTP" => dto.FtpPort,
            "TELNET" => dto.TelnetPort,
            _ => null
        };
    }

    private void PublishState(string serverId, HealthState state)
    {
        _states[serverId] = state;
        StatusChanged?.Invoke(serverId, state);
    }
}
