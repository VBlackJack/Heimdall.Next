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

using CommunityToolkit.Mvvm.ComponentModel;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Network;
using Heimdall.Core.Security;

namespace Heimdall.App.ViewModels.Tools;

/// <summary>
/// ViewModel for the banner grabber tool.
/// </summary>
public sealed partial class BannerGrabViewModel : ObservableObject, IDisposable
{
    private readonly IBannerGrabService _service;
    private readonly List<BannerResult> _allResults = [];
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private LocalizationManager? _localizer;

    [ObservableProperty] private bool _isGrabbing;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _showError;
    [ObservableProperty] private int _completed;
    [ObservableProperty] private int _total;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _progressCountText = string.Empty;
    [ObservableProperty] private string _resultCountText = string.Empty;

    public BannerGrabViewModel(IBannerGrabService? service = null)
    {
        _service = service ?? new BannerGrabService();
    }

    public void Initialize(LocalizationManager? localizer)
    {
        _localizer = localizer;
    }

    public void SetGateway(SshGatewayDto? gateway)
    {
        _service.SetGateway(gateway);
    }

    public async Task GrabAsync(string host, string portsText)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            ClearResults();
            SetError(Lk("ToolValidationHostRequired"));
            return;
        }

        if (!InputValidator.Validate(host, "Address"))
        {
            ClearResults();
            SetError(Lk("ErrorInvalidHost"));
            return;
        }

        var ports = BannerGrabEngine.ParsePorts(portsText);
        if (ports.Count == 0)
        {
            ClearResults();
            SetError(Lk("ToolValidationPortRangeRequired"));
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        lock (_lock)
        {
            _allResults.Clear();
        }

        IsGrabbing = true;
        ShowError = false;
        ErrorText = string.Empty;
        Completed = 0;
        Total = ports.Count;
        ProgressPercent = 0;
        ProgressCountText = string.Format(Lk("ToolBannerProgress"), 0, ports.Count);
        UpdateResultCountText();

        try
        {
            var progress = new Progress<BannerGrabProgress>(update =>
            {
                Completed = update.Completed;
                Total = update.Total;
                ProgressPercent = update.Total > 0 ? (int)(update.Completed * 100.0 / update.Total) : 0;
                ProgressCountText = string.Format(Lk("ToolBannerProgress"), update.Completed, update.Total);

                if (update.LatestResult is { } probe)
                {
                    var result = new BannerResult
                    {
                        Port = probe.Port,
                        Service = probe.Service,
                        Banner = probe.Banner ?? string.Empty,
                        ResponseTime = probe.ResponseTime,
                        HasBanner = !string.IsNullOrWhiteSpace(probe.Banner),
                    };

                    lock (_lock)
                    {
                        if (!_allResults.Contains(result))
                        {
                            _allResults.Add(result);
                        }
                    }
                }

                UpdateResultCountText();
            });

            var probes = await _service.GrabAsync(
                host,
                ports,
                ((IProgress<BannerGrabProgress>)progress).Report,
                _cts.Token);

            lock (_lock)
            {
                _allResults.Clear();
                _allResults.AddRange(probes.Select(probe => new BannerResult
                {
                    Port = probe.Port,
                    Service = probe.Service,
                    Banner = probe.Banner ?? string.Empty,
                    ResponseTime = probe.ResponseTime,
                    HasBanner = !string.IsNullOrWhiteSpace(probe.Banner),
                }));
            }

            Completed = _allResults.Count;
            Total = ports.Count;
            ProgressPercent = Total > 0 ? (int)(Completed * 100.0 / Total) : 0;
            ProgressCountText = string.Format(Lk("ToolBannerProgress"), Completed, Total);
            UpdateResultCountText();
        }
        catch (OperationCanceledException)
        {
            UpdateResultCountText();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            IsGrabbing = false;
        }
    }

    public void CancelGrab()
    {
        _cts?.Cancel();
    }

    public IReadOnlyList<BannerResult> GetAllResults()
    {
        lock (_lock)
        {
            return [.. _allResults.OrderBy(result => result.Port)];
        }
    }

    public IReadOnlyList<BannerResult> GetFilteredResults(bool bannerOnly)
    {
        var snapshot = GetAllResults();
        return bannerOnly ? [.. snapshot.Where(result => result.HasBanner)] : snapshot;
    }

    public string BuildCsvExport()
    {
        return BannerGrabEngine.BuildCsvExport(GetAllResults(), CreateLocalize());
    }

    public string BuildClipboardText(IReadOnlyList<BannerResult> visibleResults)
    {
        return BannerGrabEngine.BuildClipboardText(visibleResults, CreateLocalize());
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void ClearResults()
    {
        lock (_lock)
        {
            _allResults.Clear();
        }

        Completed = 0;
        Total = 0;
        ProgressPercent = 0;
        ProgressCountText = string.Empty;
        UpdateResultCountText();
    }

    private void SetError(string message)
    {
        ShowError = true;
        ErrorText = message;
    }

    private void UpdateResultCountText()
    {
        List<BannerResult> snapshot;
        lock (_lock)
        {
            snapshot = [.. _allResults];
        }

        var withBanner = snapshot.Count(result => result.HasBanner);
        ResultCountText = $"{withBanner} / {snapshot.Count}";
    }

    private string Lk(string key) => _localizer?[key] ?? key;

    private Func<string, string> CreateLocalize() => key => _localizer?[key] ?? key;
}
