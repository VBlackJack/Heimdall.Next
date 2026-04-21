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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Network;

namespace Heimdall.App.ViewModels.Tools;

/// <summary>
/// ViewModel for the TCP traceroute tool.
/// </summary>
public sealed partial class TracerouteViewModel : ObservableObject, IDisposable
{
    private readonly ITracerouteService _service;
    private CancellationTokenSource? _cts;
    private LocalizationManager? _localizer;
    private bool _gatewayConfigured;

    [ObservableProperty] private bool _isTracing;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private int _currentHop;
    [ObservableProperty] private int _maxHops;
    [ObservableProperty] private bool _progressIndeterminate;
    [ObservableProperty] private bool _sessionCompleted;
    [ObservableProperty] private string _statusText = string.Empty;

    public TracerouteViewModel(ITracerouteService? service = null)
    {
        _service = service ?? new TracerouteService();
    }

    public ObservableCollection<TraceHopResult> Hops { get; } = [];

    public void Initialize(LocalizationManager? localizer)
    {
        _localizer = localizer;
    }

    public void SetGateway(SshGatewayDto? gateway)
    {
        _gatewayConfigured = gateway is not null;
        _service.SetGateway(gateway);
    }

    public (TraceInputs? Inputs, string? ErrorKey) ValidateInputs(string? hostText, string? maxHopsText)
    {
        return TracerouteEngine.ValidateInputs(hostText, maxHopsText);
    }

    [RelayCommand]
    public async Task TraceAsync(TraceInputs inputs)
    {
        if (IsTracing)
        {
            return;
        }

        Hops.Clear();
        CurrentHop = 0;
        MaxHops = inputs.MaxHops;
        ShowError = false;
        ErrorText = string.Empty;
        StatusText = Lk("ToolTraceResolving");
        SessionCompleted = false;
        ProgressIndeterminate = false;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsTracing = true;

        var onHop = new Progress<TraceHopResult>(hop =>
        {
            Hops.Add(hop);
        });

        var onProgress = new Progress<(int Current, int Total)>(progress =>
        {
            CurrentHop = progress.Current;
            MaxHops = progress.Total;
            ProgressIndeterminate = progress.Current == 0;
            StatusText = progress.Current > 0
                ? string.Format(Lk("ToolTraceHopProgress"), progress.Current, progress.Total)
                : Lk("ToolTraceResolving");
        });

        var onHostname = new Progress<HopHostnameUpdate>(update =>
        {
            if (update.HopIndex >= 0 &&
                update.HopIndex < Hops.Count &&
                string.Equals(Hops[update.HopIndex].Address, update.Address, StringComparison.Ordinal))
            {
                Hops[update.HopIndex] = Hops[update.HopIndex] with { Hostname = update.Hostname };
            }
        });

        try
        {
            var completed = await _service.TraceAsync(inputs, onHop, onProgress, onHostname, ct);
            if (completed)
            {
                SessionCompleted = true;
                ProgressIndeterminate = false;
                StatusText = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    Lk("ToolTraceComplete"),
                    Hops.Count);
            }
            else if (!ct.IsCancellationRequested)
            {
                ShowError = true;
                ErrorText = FormatMessage(
                    _gatewayConfigured ? "ToolTunnelFailed" : "ToolTraceErrorResolve",
                    "Unknown error");
            }
        }
        catch (OperationCanceledException)
        {
            // User-initiated stop.
        }
        catch (Exception ex)
        {
            ShowError = true;
            ErrorText = FormatMessage(
                _gatewayConfigured ? "ToolTunnelFailed" : "ToolTraceErrorResolve",
                ex.Message);
        }
        finally
        {
            ProgressIndeterminate = false;
            IsTracing = false;
        }
    }

    [RelayCommand]
    public void Stop()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
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
