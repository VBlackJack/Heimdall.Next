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
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Tools;

internal sealed partial class ArpMonitorViewModel : ObservableObject, IDisposable
{
    private readonly IArpTableReader _reader;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Dictionary<string, ArpEntry> _knownEntries = new(StringComparer.OrdinalIgnoreCase);
    private LocalizationManager? _localizer;
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private bool _disposed;
    private string _lastRefreshTimestamp = string.Empty;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _lastRefreshText = string.Empty;
    [ObservableProperty] private string _totalText = string.Empty;
    [ObservableProperty] private string _emptyStateText = string.Empty;
    [ObservableProperty] private bool _isAlertVisible;
    [ObservableProperty] private string _alertTitle = string.Empty;
    [ObservableProperty] private string _alertMessage = string.Empty;

    public ObservableCollection<ArpEntry> Entries { get; } = [];

    public event EventHandler<string>? CopyResultsRequested;

    public ArpMonitorViewModel(IUiDispatcher uiDispatcher, IArpTableReader? reader = null)
    {
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _reader = reader ?? new DefaultArpTableReader();
    }

    public void Initialize(LocalizationManager? localizer)
    {
        _localizer = localizer;
        RebuildLocalizedState();
    }

    public async Task StartAsync(int intervalMs, CancellationToken cancellationToken = default)
    {
        if (_disposed || IsRunning)
        {
            return;
        }

        _pollingCts?.Dispose();
        _pollingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsRunning = true;

        await RefreshCoreAsync();

        if (_disposed || !IsRunning || _pollingCts is null)
        {
            return;
        }

        _pollingTask = RunPollingLoopAsync(TimeSpan.FromMilliseconds(intervalMs), _pollingCts.Token);
    }

    public void Stop()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
        _pollingTask = null;
        IsRunning = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync();

    [RelayCommand]
    private void DismissAlert()
    {
        IsAlertVisible = false;
    }

    [RelayCommand]
    private void CopyAll()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        CopyResultsRequested?.Invoke(this, BuildClipboardText());
    }

    partial void OnIsRunningChanged(bool value)
    {
        EmptyStateText = value
            ? L("ToolArpEmptyStateRunning")
            : L("ToolArpEmptyState");
    }

    private async Task RunPollingLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await RefreshCoreAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshCoreAsync()
    {
        if (_disposed)
        {
            return;
        }

        var shouldRefresh = false;
        await RunOnUiAsync(() =>
        {
            if (_disposed || IsRefreshing)
            {
                return;
            }

            IsRefreshing = true;
            HasError = false;
            ErrorMessage = string.Empty;
            shouldRefresh = true;
        });

        if (!shouldRefresh)
        {
            return;
        }

        try
        {
            var current = await Task.Run(() => _reader.ReadAsync(CancellationToken.None));
            var now = DateTime.Now.ToString("HH:mm:ss");
            await RunOnUiAsync(() => ApplySnapshot(current, now));
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"ArpMonitor: failed to read ARP table: {ex.Message}");
            await RunOnUiAsync(() =>
            {
                HasError = true;
                ErrorMessage = L("ToolArpErrorReadFailed");
                HasResults = Entries.Count > 0;
            });
        }
        finally
        {
            await RunOnUiAsync(() => IsRefreshing = false);
        }
    }

    private void ApplySnapshot(IReadOnlyDictionary<string, string> current, string now)
    {
        var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? alertIp = null;
        string? alertPreviousMac = null;
        string? alertNewMac = null;

        foreach (var (ip, mac) in current)
        {
            seenIps.Add(ip);

            if (_knownEntries.TryGetValue(ip, out var existing))
            {
                if (!string.Equals(existing.Mac, mac, StringComparison.OrdinalIgnoreCase))
                {
                    var previousMac = existing.Mac;
                    existing.PreviousMac = previousMac;
                    existing.Mac = mac;
                    existing.Vendor = ArpOuiLookup.Lookup(mac);
                    existing.Status = "changed";
                    existing.StatusDisplay = L("ToolArpStatusChanged");
                    existing.State = ArpEntryState.Changed;
                    existing.LastSeen = now;
                    alertIp = ip;
                    alertPreviousMac = previousMac;
                    alertNewMac = mac;
                }
                else
                {
                    existing.Status = "stable";
                    existing.StatusDisplay = L("ToolArpStatusStable");
                    existing.State = ArpEntryState.Stable;
                    existing.LastSeen = now;
                }
            }
            else
            {
                var entry = new ArpEntry
                {
                    Ip = ip,
                    Mac = mac,
                    Vendor = ArpOuiLookup.Lookup(mac),
                    Status = "new",
                    StatusDisplay = L("ToolArpStatusNew"),
                    State = ArpEntryState.New,
                    FirstSeen = now,
                    LastSeen = now,
                    PreviousMac = ""
                };
                _knownEntries[ip] = entry;
                Entries.Add(entry);
            }
        }

        foreach (var known in _knownEntries.Values)
        {
            if (!seenIps.Contains(known.Ip))
            {
                known.Status = "gone";
                known.StatusDisplay = L("ToolArpStatusGone");
                known.State = ArpEntryState.Gone;
            }
        }

        _lastRefreshTimestamp = now;
        HasResults = Entries.Count > 0;
        HasError = false;
        ErrorMessage = string.Empty;
        RebuildLocalizedState();

        if (!string.IsNullOrEmpty(alertIp) && alertPreviousMac is not null && alertNewMac is not null)
        {
            AlertTitle = L("ToolArpAlertTitle");
            AlertMessage = string.Format(L("ToolArpAlertMacChanged"), alertIp, alertPreviousMac, alertNewMac);
            IsAlertVisible = true;
        }
    }

    private void RebuildLocalizedState()
    {
        EmptyStateText = IsRunning
            ? L("ToolArpEmptyStateRunning")
            : L("ToolArpEmptyState");
        TotalText = string.Format(L("ToolArpTotal"), Entries.Count);
        LastRefreshText = string.IsNullOrEmpty(_lastRefreshTimestamp)
            ? string.Empty
            : string.Format(L("ToolArpLastRefresh"), _lastRefreshTimestamp);

        if (HasError)
        {
            ErrorMessage = L("ToolArpErrorReadFailed");
        }

        foreach (var entry in Entries)
        {
            switch (entry.Status)
            {
                case "new":
                    entry.StatusDisplay = L("ToolArpStatusNew");
                    entry.State = ArpEntryState.New;
                    break;
                case "changed":
                    entry.StatusDisplay = L("ToolArpStatusChanged");
                    entry.State = ArpEntryState.Changed;
                    break;
                case "gone":
                    entry.StatusDisplay = L("ToolArpStatusGone");
                    entry.State = ArpEntryState.Gone;
                    break;
                default:
                    entry.StatusDisplay = L("ToolArpStatusStable");
                    entry.State = ArpEntryState.Stable;
                    break;
            }
        }
    }

    private string BuildClipboardText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{L("ToolArpColIp")}\t{L("ToolArpColMac")}\t{L("ToolArpColVendor")}\t{L("ToolArpColStatus")}\t{L("ToolArpColFirstSeen")}\t{L("ToolArpColLastSeen")}");

        foreach (var entry in Entries)
        {
            sb.AppendLine($"{entry.Ip}\t{entry.Mac}\t{entry.Vendor}\t{entry.StatusDisplay}\t{entry.FirstSeen}\t{entry.LastSeen}");
        }

        sb.AppendLine();
        sb.AppendLine(string.Format(L("ToolArpTotal"), Entries.Count));
        return sb.ToString();
    }

    private Task RunOnUiAsync(Action action)
    {
        if (_uiDispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _uiDispatcher.InvokeAsync(action);
    }

    private string L(string key) => _localizer?[key] ?? key;
}
