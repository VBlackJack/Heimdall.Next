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
using Heimdall.Core.Localization;
using Heimdall.Core.Logging;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;

namespace Heimdall.App.ViewModels.Tunnels;

/// <summary>
/// View-model backing the Tunnels retractable panel and Tunnels tab
/// DataGrid. Owns the active tunnel list, close / copy / close-all
/// commands, and the tunnel-route resolver used by session tabs to
/// display "via GatewayA &#x2192; GatewayB" annotations.
/// </summary>
/// <remarks>
/// Composition: instantiated inside <see cref="MainViewModel"/>'s
/// constructor (<see cref="MainViewModel.Tunnels"/>) — no DI registration.
/// Subscribes to <see cref="TunnelManager.TunnelOpened"/> /
/// <see cref="TunnelManager.TunnelClosed"/> in its constructor and
/// unsubscribes in <see cref="Dispose"/> (which <see cref="MainViewModel.Dispose"/>
/// calls from its own cleanup path).
/// </remarks>
public sealed partial class TunnelsViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly LocalizationManager _localizer;
    private readonly TunnelManager _tunnelManager;
    private readonly ConnectionStateMachine _connectionSm;
    private bool _disposed;

    /// <summary>
    /// Creates a new tunnels VM and wires up the tunnel manager events.
    /// </summary>
    public TunnelsViewModel(
        MainViewModel main,
        LocalizationManager localizer,
        TunnelManager tunnelManager,
        ConnectionStateMachine connectionSm)
    {
        _main = main;
        _localizer = localizer;
        _tunnelManager = tunnelManager;
        _connectionSm = connectionSm;

        _tunnelManager.TunnelOpened += OnTunnelOpened;
        _tunnelManager.TunnelClosed += OnTunnelClosed;
        _localizer.LocaleChanged += OnLocaleChanged;
    }

    // ── Observable state ─────────────────────────────────────────────

    /// <summary>Whether the retractable tunnels panel in the status bar is expanded.</summary>
    [ObservableProperty]
    private bool _isPanelOpen;

    /// <summary>Live snapshot of active tunnels — bound by the DataGrid.</summary>
    [ObservableProperty]
    private ObservableCollection<TunnelInfo> _list = new();

    /// <summary>Row currently selected in the tunnels DataGrid.</summary>
    [ObservableProperty]
    private TunnelInfo? _selectedItem;

    /// <summary>
    /// Number of active tunnels — bound by the status-bar badge and the
    /// tunnels tab count indicator. Maintained alongside
    /// <see cref="List"/> on every refresh.
    /// </summary>
    [ObservableProperty]
    private int _count;

    /// <summary>True when there are no active tunnels.</summary>
    public bool HasNoTunnels => List.Count == 0;

    /// <summary>Localized tunnel panel header text before the live count.</summary>
    public string TunnelPanelHeaderPrefix
    {
        get
        {
            var header = _localizer["TunnelPanelHeader"];
            var idx = header.IndexOf("{0}", StringComparison.Ordinal);
            return idx >= 0 ? header[..idx] : header;
        }
    }

    /// <summary>Localized tunnel panel header text after the live count.</summary>
    public string TunnelPanelHeaderSuffix
    {
        get
        {
            var header = _localizer["TunnelPanelHeader"];
            var idx = header.IndexOf("{0}", StringComparison.Ordinal);
            return idx >= 0 ? header[(idx + 3)..] : string.Empty;
        }
    }

    /// <summary>
    /// Generated partial: refreshes the <see cref="HasNoTunnels"/>
    /// notification whenever the list collection is replaced.
    /// </summary>
    partial void OnListChanged(ObservableCollection<TunnelInfo> value)
    {
        OnPropertyChanged(nameof(HasNoTunnels));
    }

    // ── Commands ─────────────────────────────────────────────────────

    /// <summary>
    /// Toggles the retractable tunnels panel in the status bar. Bound to
    /// the tunnel icon button.
    /// </summary>
    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    /// <summary>
    /// Force-closes a single tunnel, refreshes the list and reports the
    /// result in the shell status bar.
    /// </summary>
    [RelayCommand]
    private void Close(TunnelInfo? tunnel)
    {
        if (tunnel is null)
        {
            return;
        }

        _tunnelManager.ForceCloseTunnel(tunnel.LocalPort);
        RefreshList();
        _main.StatusText = _localizer.Format("StatusTunnelClosed", tunnel.LocalPort);
    }

    /// <summary>
    /// Closes every active tunnel, refreshes the list and reports the
    /// result in the shell status bar.
    /// </summary>
    [RelayCommand]
    private void CloseAll()
    {
        _tunnelManager.CloseAllTunnels();
        Count = 0;
        RefreshList();
        _main.StatusText = _localizer["StatusAllTunnelsClosed"];
    }

    /// <summary>
    /// Copies the local port of the given tunnel to the clipboard.
    /// Silent-warn on clipboard failures (common in RDP sessions).
    /// </summary>
    [RelayCommand]
    private void CopyPort(TunnelInfo? tunnel)
    {
        if (tunnel is null)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(tunnel.LocalPort.ToString());
            _main.StatusText = _localizer.Format("StatusPortCopied", tunnel.LocalPort);
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"[TunnelsViewModel] clipboard copy: {ex.Message}");
        }
    }

    // ── Public helpers ───────────────────────────────────────────────

    /// <summary>
    /// Rebuilds <see cref="List"/> and <see cref="Count"/> from the
    /// tunnel manager's current active set. Called on startup
    /// (<see cref="MainViewModel.LoadAsync"/>), on tab switch to Tunnels,
    /// and after any tunnel-manager event.
    /// </summary>
    public void RefreshList()
    {
        var tunnels = _tunnelManager.GetActiveTunnels();
        List = new ObservableCollection<TunnelInfo>(tunnels);
        Count = tunnels.Count;
    }

    /// <summary>
    /// Resolves the tunnel chain route for a server, returning a display
    /// string like <c>"via GatewayA"</c> or <c>"via GatewayA &#x2192; GatewayB"</c>
    /// for chained tunnels. Returns an empty string for direct connections
    /// or when the server has no active tunnel.
    /// </summary>
    public string ResolveRoute(string serverId)
    {
        var settings = _main.CurrentSettings;
        if (settings is null) return string.Empty;

        var stateData = _connectionSm.GetStateData(serverId);
        if (stateData?.TunnelLocalPort is null) return string.Empty;

        // Find which gateway hosts this tunnel by matching the tunnel's ServerName
        var tunnels = _tunnelManager.GetActiveTunnels();
        var tunnel = tunnels.FirstOrDefault(t => t.LocalPort == stateData.TunnelLocalPort);
        if (tunnel is null) return string.Empty;

        var gatewayId = settings.SshGateways
            .FirstOrDefault(g => string.Equals(g.Host, tunnel.ServerName, StringComparison.OrdinalIgnoreCase))?.Id;

        if (string.IsNullOrEmpty(gatewayId))
            return $"via {tunnel.ServerName}";

        // Walk the gateway chain to build the full route
        var names = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!string.IsNullOrEmpty(gatewayId) && visited.Add(gatewayId))
        {
            var gw = settings.SshGateways.FirstOrDefault(
                g => string.Equals(g.Id, gatewayId, StringComparison.OrdinalIgnoreCase));
            if (gw is null) break;
            names.Add(string.IsNullOrWhiteSpace(gw.Name) ? gw.Host : gw.Name);
            gatewayId = gw.ParentGatewayId;
        }

        if (names.Count == 0) return string.Empty;
        names.Reverse();
        return "via " + string.Join(" \u2192 ", names);
    }

    // ── Event handlers ───────────────────────────────────────────────

    private void OnTunnelOpened(TunnelInfo info)
    {
        RefreshList();
        IsPanelOpen = true;
    }

    private void OnTunnelClosed(int localPort, string? error)
    {
        RefreshList();

        if (!string.IsNullOrEmpty(error))
        {
            _main.StatusText = _localizer.Format("StatusTunnelClosed", localPort) + $" ({error})";
        }
    }

    private void OnLocaleChanged(string _)
    {
        OnPropertyChanged(nameof(TunnelPanelHeaderPrefix));
        OnPropertyChanged(nameof(TunnelPanelHeaderSuffix));
    }

    // ── IDisposable ──────────────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tunnelManager.TunnelOpened -= OnTunnelOpened;
        _tunnelManager.TunnelClosed -= OnTunnelClosed;
        _localizer.LocaleChanged -= OnLocaleChanged;
    }
}
