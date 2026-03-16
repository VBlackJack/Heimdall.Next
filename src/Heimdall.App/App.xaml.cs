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

using System.IO;
using System.Windows;
using Heimdall.App.Services;
using Heimdall.App.ViewModels;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Security;
using Heimdall.Core.StateMachine;
using Heimdall.Ssh;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.App;

/// <summary>
/// Application entry point. Configures dependency injection
/// and initializes core services before showing the main window.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Initialize core services
        var configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        await configManager.InitializeAsync();

        var localization = _serviceProvider.GetRequiredService<LocalizationManager>();
        var settings = await configManager.LoadSettingsAsync();
        await localization.LoadAsync(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales"),
            settings.DefaultLocale);

        // Check for legacy Heimdall installation and offer migration on first run
        await TryMigrateLegacyAsync(configManager, localization);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<ConfigManager>(_ => new ConfigManager(
            AppDomain.CurrentDomain.BaseDirectory));
        services.AddSingleton<LocalizationManager>();
        services.AddSingleton<ConnectionStateMachine>();
        services.AddSingleton<ApplicationStatusMachine>();
        services.AddSingleton<HostKeyStore>();
        services.AddSingleton<PinManager>();

        // SSH/Tunnel services
        services.AddSingleton<TunnelManager>();

        // Application services
        services.AddSingleton<NavigationService>();
        services.AddSingleton<ConnectionService>();
        services.AddSingleton<IDialogService, WpfDialogService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ServerListViewModel>();
        services.AddTransient<ConnectionViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Windows
        services.AddTransient<MainWindow>();
    }

    /// <summary>
    /// Detects a legacy Heimdall (PowerShell) installation in the sibling
    /// RDPManager directory and offers to import its configuration.
    /// </summary>
    private static async Task TryMigrateLegacyAsync(
        ConfigManager configManager, LocalizationManager localization)
    {
        var legacyPath = Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "RDPManager"));

        if (!MigrationService.DetectLegacyInstallation(legacyPath))
        {
            return;
        }

        var prompt = MessageBox.Show(
            localization["MigrationDetectedPrompt"],
            localization["MigrationTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (prompt != MessageBoxResult.Yes)
        {
            return;
        }

        var migrationService = new MigrationService(configManager, localization);
        var result = await migrationService.ImportFromLegacyAsync(legacyPath);

        if (result.Success)
        {
            MessageBox.Show(
                localization.Format("MigrationSuccess", result.ServersImported),
                localization["MigrationTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(
                localization.Format("MigrationFailed", result.Error ?? ""),
                localization["MigrationTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Dispose tunnel manager to close all active tunnels
        if (_serviceProvider is not null)
        {
            var tunnelManager = _serviceProvider.GetService<TunnelManager>();
            tunnelManager?.Dispose();
        }

        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
