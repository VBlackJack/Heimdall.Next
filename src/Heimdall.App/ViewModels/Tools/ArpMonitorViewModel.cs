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
using Heimdall.Core.Localization;
using System.Windows;
using System.Windows.Media;

namespace Heimdall.App.ViewModels.Tools;

internal sealed partial class ArpMonitorViewModel : ObservableObject, IDisposable
{
    private readonly IArpTableReader _reader;
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

    public ObservableCollection<ArpEntry> Entries { get; } = [];

    public event EventHandler<ArpMacChangedEventArgs>? MacChangedDetected;

    public ArpMonitorViewModel(IArpTableReader? reader = null)
    {
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
        var successBrush = ResolveBrush("SuccessBrush", Brushes.Green);
        var stableBrush = ResolveBrush("TextSecondaryBrush", Brushes.Gray);
        var warningBrush = ResolveBrush("WarningBrush", Brushes.Orange);
        var errorBrush = ResolveBrush("ErrorBrush", Brushes.Red);

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
                    existing.Status = "changed";
                    existing.StatusDisplay = L("ToolArpStatusChanged");
                    existing.StatusBrush = warningBrush;
                    existing.LastSeen = now;
                    MacChangedDetected?.Invoke(this, new ArpMacChangedEventArgs(ip, previousMac, mac));
                }
                else
                {
                    existing.Status = "stable";
                    existing.StatusDisplay = L("ToolArpStatusStable");
                    existing.StatusBrush = stableBrush;
                    existing.LastSeen = now;
                }
            }
            else
            {
                var entry = new ArpEntry
                {
                    Ip = ip,
                    Mac = mac,
                    Status = "new",
                    StatusDisplay = L("ToolArpStatusNew"),
                    StatusBrush = successBrush,
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
                known.StatusBrush = errorBrush;
            }
        }

        _lastRefreshTimestamp = now;
        HasResults = Entries.Count > 0;
        HasError = false;
        ErrorMessage = string.Empty;
        RebuildLocalizedState();
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
                    entry.StatusBrush = ResolveBrush("SuccessBrush", Brushes.Green);
                    break;
                case "changed":
                    entry.StatusDisplay = L("ToolArpStatusChanged");
                    entry.StatusBrush = ResolveBrush("WarningBrush", Brushes.Orange);
                    break;
                case "gone":
                    entry.StatusDisplay = L("ToolArpStatusGone");
                    entry.StatusBrush = ResolveBrush("ErrorBrush", Brushes.Red);
                    break;
                default:
                    entry.StatusDisplay = L("ToolArpStatusStable");
                    entry.StatusBrush = ResolveBrush("TextSecondaryBrush", Brushes.Gray);
                    break;
            }
        }
    }

    private static Brush ResolveBrush(string resourceKey, Brush fallback)
        => Application.Current?.TryFindResource(resourceKey) as Brush ?? fallback;

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.BeginInvoke(action).Task;
    }

    private string L(string key) => _localizer?[key] ?? key;
}

internal sealed record ArpMacChangedEventArgs(string Ip, string PreviousMac, string NewMac);
