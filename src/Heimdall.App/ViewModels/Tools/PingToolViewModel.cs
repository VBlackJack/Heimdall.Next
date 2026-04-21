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
using CommunityToolkit.Mvvm.ComponentModel;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Network;

namespace Heimdall.App.ViewModels.Tools;

/// <summary>
/// ViewModel for the continuous ping monitor.
/// </summary>
public sealed partial class PingToolViewModel : ObservableObject, IDisposable
{
    private readonly IPingService _service;
    private readonly List<PingProbeResult> _history = [];
    private readonly List<PingDataPoint> _dataPoints = new(PingStatsEngine.MaxDataPoints + 1);
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private LocalizationManager? _localizer;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private int _sentCount;
    [ObservableProperty] private PingStatsSnapshot _stats = new(0, 0, 0, 0, 0, 0, 0, 0);
    [ObservableProperty] private PingProbeResult? _latestResult;
    [ObservableProperty] private bool _sessionCompleted;

    public PingToolViewModel(IPingService? service = null)
    {
        _service = service ?? new PingService();
    }

    public void Initialize(LocalizationManager? localizer)
    {
        _localizer = localizer;
    }

    public void SetGateway(SshGatewayDto? gateway)
    {
        _service.SetGateway(gateway);
    }

    public IReadOnlyList<PingProbeResult> GetHistory()
    {
        lock (_lock)
        {
            return [.. _history];
        }
    }

    public IReadOnlyList<PingDataPoint> GetDataPoints()
    {
        lock (_lock)
        {
            return [.. _dataPoints];
        }
    }

    public (PingInputs? Inputs, string? ErrorKey) ValidateInputs(string? hostText, string? timeoutText, string? countText)
    {
        return PingStatsEngine.ValidateInputs(hostText, timeoutText, countText);
    }

    public async Task StartAsync(PingInputs inputs, int intervalMs)
    {
        if (IsRunning)
        {
            return;
        }

        lock (_lock)
        {
            _history.Clear();
            _dataPoints.Clear();
        }

        SentCount = 0;
        Stats = new PingStatsSnapshot(0, 0, 0, 0, 0, 0, 0, 0);
        LatestResult = null;
        SessionCompleted = false;
        ShowError = false;
        ErrorText = string.Empty;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsRunning = true;

        try
        {
            try
            {
                await _service.StartSessionAsync(ct);
            }
            catch (Exception ex)
            {
                ShowError = true;
                ErrorText = FormatMessage("ToolPingGatewayError", ex.Message);
                return;
            }

            var seq = 0;

            await DoPingAsync(inputs, ++seq, ct);
            if (ShouldStop(inputs, ct))
            {
                return;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));

            while (!ct.IsCancellationRequested)
            {
                bool tick;
                try
                {
                    tick = await timer.WaitForNextTickAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!tick)
                {
                    break;
                }

                if (ShouldStop(inputs, ct))
                {
                    break;
                }

                await DoPingAsync(inputs, ++seq, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Session was stopped by the caller.
        }
        finally
        {
            _service.EndSession();
            IsRunning = false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void Reset()
    {
        lock (_lock)
        {
            _history.Clear();
            _dataPoints.Clear();
        }

        SentCount = 0;
        Stats = new PingStatsSnapshot(0, 0, 0, 0, 0, 0, 0, 0);
        LatestResult = null;
        SessionCompleted = false;
        ShowError = false;
        ErrorText = string.Empty;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _cts = null;
        _service.Dispose();
    }

    private bool ShouldStop(PingInputs inputs, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return true;
        }

        if (inputs.Count > 0 && SentCount >= inputs.Count)
        {
            SessionCompleted = true;
            return true;
        }

        return false;
    }

    private async Task DoPingAsync(PingInputs inputs, int seq, CancellationToken ct)
    {
        PingProbeResult result;
        try
        {
            result = await _service.PingAsync(inputs.Host, seq, inputs.TimeoutMs, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var message = ex.InnerException?.Message ?? ex.Message;
            result = new PingProbeResult(seq, timestamp, -1, PingStatus.Error, 0, message, inputs.Host);
        }

        lock (_lock)
        {
            _history.Add(result);

            var dataPoint = new PingDataPoint(result.Latency, result.Status != PingStatus.Success);
            _dataPoints.Add(dataPoint);
            if (_dataPoints.Count > PingStatsEngine.MaxDataPoints)
            {
                _dataPoints.RemoveAt(0);
            }
        }

        SentCount = seq;
        Stats = PingStatsEngine.ComputeStats(GetHistory());
        LatestResult = result;
    }

    private string Lk(string key) => _localizer?[key] ?? key;

    private string FormatMessage(string key, params object[] args)
    {
        var template = Lk(key);
        return template.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(template, args)
            : $"{template}: {string.Join(" ", args)}";
    }
}
