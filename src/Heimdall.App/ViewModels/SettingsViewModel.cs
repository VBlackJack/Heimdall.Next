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
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels;

/// <summary>
/// ViewModel for the application settings tab.
/// Tracks dirty state and delegates persistence to <see cref="ConfigManager"/>.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;
    private readonly LocalizationManager _localizer;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string _plinkPath = "";

    [ObservableProperty]
    private string _defaultLocale = "en";

    [ObservableProperty]
    private string _defaultTheme = "Dark";

    [ObservableProperty]
    private bool _enableLogging = true;

    [ObservableProperty]
    private int _antiIdleInterval = 60;

    [ObservableProperty]
    private int _maxEmbeddedSessions = 10;

    [ObservableProperty]
    private string _externalEditorPath = "";

    [ObservableProperty]
    private bool _sftpAutoOpenOnSsh = true;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private ObservableCollection<GatewayItemViewModel> _gateways = new();

    [ObservableProperty]
    private GatewayItemViewModel? _selectedGateway;

    public SettingsViewModel(
        ConfigManager configManager,
        LocalizationManager localizer,
        IDialogService dialogService)
    {
        _configManager = configManager;
        _localizer = localizer;
        _dialogService = dialogService;
    }

    /// <summary>
    /// Populates ViewModel properties from the loaded <see cref="AppSettings"/>.
    /// Does not mark the ViewModel as dirty.
    /// </summary>
    public void LoadFromSettings(AppSettings settings)
    {
        PlinkPath = settings.PlinkPath;
        DefaultLocale = settings.DefaultLocale;
        DefaultTheme = settings.DefaultTheme;
        EnableLogging = settings.EnableLogging;
        AntiIdleInterval = settings.AntiIdleIntervalSeconds;
        MaxEmbeddedSessions = settings.MaxEmbeddedSessions;
        ExternalEditorPath = settings.ExternalEditorPath;
        SftpAutoOpenOnSsh = settings.SftpAutoOpenOnSsh;

        Gateways = new ObservableCollection<GatewayItemViewModel>(
            settings.SshGateways.Select(g => new GatewayItemViewModel
            {
                Id = g.Id,
                Name = g.Name,
                Host = g.Host,
                Port = g.Port,
                User = g.User,
                HasKey = !string.IsNullOrEmpty(g.KeyPath),
                HasPassword = !string.IsNullOrEmpty(g.SshPasswordEncrypted),
                ParentGatewayId = g.ParentGatewayId
            }));

        IsDirty = false;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var settings = await _configManager.LoadSettingsAsync();

        settings.PlinkPath = PlinkPath;
        settings.DefaultLocale = DefaultLocale;
        settings.DefaultTheme = DefaultTheme;
        settings.EnableLogging = EnableLogging;
        settings.AntiIdleIntervalSeconds = AntiIdleInterval;
        settings.MaxEmbeddedSessions = MaxEmbeddedSessions;
        settings.ExternalEditorPath = ExternalEditorPath;
        settings.SftpAutoOpenOnSsh = SftpAutoOpenOnSsh;

        await _configManager.SaveSettingsAsync(settings);

        if (!string.Equals(_localizer.CurrentLocale, DefaultLocale, StringComparison.OrdinalIgnoreCase))
        {
            await _localizer.SwitchLocaleAsync(DefaultLocale);
        }

        IsDirty = false;
    }

    [RelayCommand]
    private Task ResetToDefaultsAsync(CancellationToken cancellationToken)
    {
        var defaults = new AppSettings();
        LoadFromSettings(defaults);
        IsDirty = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ExportConfigAsync(CancellationToken cancellationToken)
    {
        // Export file dialog requires XAML view (Phase 5B)
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ImportConfigAsync(CancellationToken cancellationToken)
    {
        // Import file dialog requires XAML view (Phase 5B)
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task AddGatewayAsync(CancellationToken cancellationToken)
    {
        var vm = new GatewayDialogViewModel();
        // Populate parent gateway options (exclude self)
        var settings = await _configManager.LoadSettingsAsync();
        vm.AvailableParents = new ObservableCollection<GatewayOption>(
            settings.SshGateways.Select(g => new GatewayOption(g.Id, $"{g.Name} ({g.Host})")));

        var result = await _dialogService.ShowGatewayDialogAsync(vm);
        if (result?.Saved == true)
        {
            result.Gateway.Id = Guid.NewGuid().ToString();
            settings.SshGateways.Add(result.Gateway);
            await _configManager.SaveSettingsAsync(settings);

            Gateways.Add(new GatewayItemViewModel
            {
                Id = result.Gateway.Id,
                Name = result.Gateway.Name,
                Host = result.Gateway.Host,
                Port = result.Gateway.Port,
                User = result.Gateway.User,
                HasKey = !string.IsNullOrEmpty(result.Gateway.KeyPath),
                HasPassword = !string.IsNullOrEmpty(result.Gateway.SshPasswordEncrypted)
            });
        }
    }

    [RelayCommand]
    private async Task EditGatewayAsync(CancellationToken cancellationToken)
    {
        if (SelectedGateway == null) return;

        var settings = await _configManager.LoadSettingsAsync();
        var gwDto = settings.SshGateways.FirstOrDefault(g => g.Id == SelectedGateway.Id);
        if (gwDto == null) return;

        var vm = GatewayDialogViewModel.FromDto(gwDto);
        vm.AvailableParents = new ObservableCollection<GatewayOption>(
            settings.SshGateways
                .Where(g => g.Id != gwDto.Id)
                .Select(g => new GatewayOption(g.Id, $"{g.Name} ({g.Host})")));

        var result = await _dialogService.ShowGatewayDialogAsync(vm);
        if (result?.Saved == true)
        {
            var idx = settings.SshGateways.FindIndex(g => g.Id == gwDto.Id);
            if (idx >= 0)
            {
                result.Gateway.Id = gwDto.Id;
                settings.SshGateways[idx] = result.Gateway;
                await _configManager.SaveSettingsAsync(settings);

                SelectedGateway.Name = result.Gateway.Name;
                SelectedGateway.Host = result.Gateway.Host;
                SelectedGateway.Port = result.Gateway.Port;
                SelectedGateway.User = result.Gateway.User;
                SelectedGateway.HasKey = !string.IsNullOrEmpty(result.Gateway.KeyPath);
                SelectedGateway.HasPassword = !string.IsNullOrEmpty(result.Gateway.SshPasswordEncrypted);
            }
        }
    }

    [RelayCommand]
    private async Task DeleteGatewayAsync(CancellationToken cancellationToken)
    {
        if (SelectedGateway == null) return;

        var confirmed = await _dialogService.ShowConfirmAsync(
            "Delete Gateway",
            $"Delete gateway '{SelectedGateway.Name}'? Servers using this gateway will lose their tunnel configuration.",
            "danger");

        if (!confirmed) return;

        var settings = await _configManager.LoadSettingsAsync();
        settings.SshGateways.RemoveAll(g => g.Id == SelectedGateway.Id);
        await _configManager.SaveSettingsAsync(settings);

        Gateways.Remove(SelectedGateway);
        SelectedGateway = null;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Mark dirty when any settings property changes (except IsDirty itself)
        if (e.PropertyName is not (nameof(IsDirty)))
        {
            IsDirty = true;
        }
    }
}
