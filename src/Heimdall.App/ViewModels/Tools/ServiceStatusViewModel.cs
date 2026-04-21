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
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Localization;
using Heimdall.Core.Security;
using Heimdall.Core.SystemInfo;

namespace Heimdall.App.ViewModels.Tools;

/// <summary>
/// ViewModel for the service status dashboard.
/// </summary>
public sealed partial class ServiceStatusViewModel : ObservableObject, IDisposable
{
    public const int PostActionReloadDelayMs = 2000;

    private readonly IServiceStatusService _service;
    private LocalizationManager? _localizer;
    private CancellationTokenSource? _cts;
    private readonly List<ServiceEntry> _allServices = [];
    private bool _disposed;
    private bool _isLoading;
    private string? _lastErrorKey;
    private object[] _lastErrorArgs = [];
    private bool _hasRefreshSnapshot;

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _runningOnly;
    [ObservableProperty] private string _helpText = string.Empty;
    [ObservableProperty] private string _filterWatermark = string.Empty;
    [ObservableProperty] private bool _isHelpVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private int _stoppedCount;

    public ServiceStatusViewModel(IServiceStatusService? service = null)
    {
        _service = service ?? new ServiceStatusService();
        DisplayedServices = [];
        DisplayedServices.CollectionChanged += OnDisplayedServicesChanged;
    }

    public ObservableCollection<ServiceEntry> DisplayedServices { get; }

    public bool HasResults => DisplayedServices.Count > 0;

    public bool HasRefreshSnapshot => _hasRefreshSnapshot;

    public event EventHandler<string>? CopyResultsRequested;

    public void Initialize(LocalizationManager? localizer) => UpdateLocalizer(localizer);

    public void UpdateLocalizer(LocalizationManager? localizer)
    {
        if (ReferenceEquals(_localizer, localizer))
        {
            return;
        }

        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
        }

        _localizer = localizer;
        if (_localizer is not null)
        {
            _localizer.LocaleChanged += OnLocaleChanged;
        }

        RefreshLocalizedMessages();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (_disposed || _isLoading)
        {
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(TimeSpan.FromSeconds(30));

        _isLoading = true;
        IsBusy = true;
        HasError = false;
        ErrorText = string.Empty;
        _lastErrorKey = null;
        _lastErrorArgs = [];

        try
        {
            var services = await _service.LoadAsync(_cts.Token).ConfigureAwait(true);
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            _allServices.Clear();
            _allServices.AddRange(services);
            UpdateCounts();
            ApplyFilter();
            _hasRefreshSnapshot = true;
            OnPropertyChanged(nameof(HasRefreshSnapshot));
        }
        catch (OperationCanceledException)
        {
            _lastErrorKey = "ToolServicesErrorTimeout";
            _lastErrorArgs = [];
            HasError = true;
            ErrorText = L(_lastErrorKey);
        }
        catch (Exception ex)
        {
            _lastErrorKey = "ToolServicesErrorFailed";
            _lastErrorArgs = [ex.Message];
            HasError = true;
            ErrorText = string.Format(CultureInfo.InvariantCulture, L(_lastErrorKey), _lastErrorArgs);
        }
        finally
        {
            _isLoading = false;
            IsBusy = false;
            RefreshCommand.NotifyCanExecuteChanged();
            StartServiceCommand.NotifyCanExecuteChanged();
            StopServiceCommand.NotifyCanExecuteChanged();
            RestartServiceCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyResults))]
    private void CopyResults()
    {
        if (DisplayedServices.Count == 0)
        {
            return;
        }

        CopyResultsRequested?.Invoke(this, BuildClipboardText());
    }

    [RelayCommand]
    private void ToggleHelp()
    {
        IsHelpVisible = !IsHelpVisible;
    }

    [RelayCommand(CanExecute = nameof(CanExecuteServiceAction))]
    private async Task StartServiceAsync(ServiceEntry? service)
    {
        if (service is null || _disposed)
        {
            return;
        }

        _service.StartService(service.Name);
        await Task.Delay(PostActionReloadDelayMs).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteServiceAction))]
    private async Task StopServiceAsync(ServiceEntry? service)
    {
        if (service is null || _disposed)
        {
            return;
        }

        _service.StopService(service.Name);
        await Task.Delay(PostActionReloadDelayMs).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteServiceAction))]
    private async Task RestartServiceAsync(ServiceEntry? service)
    {
        if (service is null || _disposed)
        {
            return;
        }

        _service.RestartService(service.Name);
        await Task.Delay(PostActionReloadDelayMs).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
            _localizer = null;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        DisplayedServices.CollectionChanged -= OnDisplayedServicesChanged;
        GC.SuppressFinalize(this);
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnRunningOnlyChanged(bool value)
    {
        ApplyFilter();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        StartServiceCommand.NotifyCanExecuteChanged();
        StopServiceCommand.NotifyCanExecuteChanged();
        RestartServiceCommand.NotifyCanExecuteChanged();
    }

    private bool CanRefresh() => !_isLoading;

    private bool CanCopyResults() => DisplayedServices.Count > 0;

    private bool CanExecuteServiceAction(ServiceEntry? service) => !_isLoading && service is not null;

    private void OnDisplayedServicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CopyResultsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasResults));
    }

    private void OnLocaleChanged(string _)
    {
        RefreshLocalizedMessages();
    }

    private void RefreshLocalizedMessages()
    {
        HelpText = L("ToolHelpSERVICES").Replace("\\n", "\n", StringComparison.Ordinal);
        FilterWatermark = L("ToolWatermarkServiceFilter");

        if (HasError && !string.IsNullOrWhiteSpace(_lastErrorKey))
        {
            ErrorText = _lastErrorArgs.Length == 0
                ? L(_lastErrorKey)
                : string.Format(CultureInfo.InvariantCulture, L(_lastErrorKey), _lastErrorArgs);
        }
    }

    private void UpdateCounts()
    {
        TotalCount = _allServices.Count;
        RunningCount = _allServices.Count(IsRunning);
        StoppedCount = _allServices.Count(entry => entry.Status.Contains("Stopped", StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyFilter()
    {
        DisplayedServices.Clear();

        foreach (var service in _allServices)
        {
            if (RunningOnly && !IsRunning(service))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(FilterText) &&
                !service.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase) &&
                !service.DisplayName.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DisplayedServices.Add(service);
        }
    }

    private static bool IsRunning(ServiceEntry entry) =>
        entry.Status.Contains("Running", StringComparison.OrdinalIgnoreCase);

    private string BuildClipboardText()
    {
        return string.Join(
            Environment.NewLine,
            DisplayedServices.Select(service =>
                $"{InputValidator.SanitizeCsvCell(service.Name)}\t{InputValidator.SanitizeCsvCell(service.DisplayName)}\t{InputValidator.SanitizeCsvCell(service.Status)}\t{InputValidator.SanitizeCsvCell(service.StartType)}"));
    }

    private string L(string key) => _localizer?[key] ?? key;
}
