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
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private MainViewModel? _mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Register global exception handlers BEFORE any awaits — async void
        // resumes on the dispatcher, so unhandled exceptions from awaited calls
        // must already be caught at this point.
        DispatcherUnhandledException += (_, args) =>
        {
            Heimdall.Core.Logging.FileLogger.Error("Unhandled exception", args.Exception);
            var errorTitle = "Heimdall Error";
            try
            {
                var loc = _serviceProvider?.GetService<LocalizationManager>();
                if (loc is not null)
                    errorTitle = loc["ErrorUnhandledTitle"];
            }
            catch { /* Localization may not be initialized yet */ }
            MessageBox.Show(
                $"Unhandled error:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                errorTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Heimdall.Core.Logging.FileLogger.Error(
                "Unobserved task exception", args.Exception.InnerException ?? args.Exception);
            args.SetObserved();
        };

        // Initialize file logger
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Heimdall.Core.Logging.FileLogger.Initialize(logDir);
        Heimdall.Core.Logging.ConnectionHistory.Initialize(logDir);
        Heimdall.Core.Logging.FileLogger.Info("Heimdall.Next starting");

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

        // Initialize HMAC key for credential protection
        await InitializeHmacKeyAsync(configManager, settings);

        // Load trusted SSH host keys into the TOFU store
        var hostKeyStore = _serviceProvider.GetRequiredService<HostKeyStore>();
        if (settings.TrustedHostKeys.Count > 0)
        {
            var entries = settings.TrustedHostKeys.Select(kvp =>
            {
                var parts = kvp.Key.Split(':');
                var host = parts[0];
                var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 22;
                return (host, port, (string?)kvp.Value);
            });
            hostKeyStore.LoadFromConfig(entries);
        }

        // Persist newly trusted host keys back to settings
        hostKeyStore.HostKeyEvent += (key, fingerprint, trusted) =>
        {
            if (trusted && !settings.TrustedHostKeys.ContainsKey(key))
            {
                settings.TrustedHostKeys[key] = fingerprint;
                _ = Task.Run(async () =>
                {
                    try { await configManager.SaveSettingsAsync(settings); }
                    catch (Exception ex)
                    {
                        Heimdall.Core.Logging.FileLogger.Warn(
                            $"Failed to persist host key for {key}: {ex.Message}");
                    }
                });
            }
        };

        // Subscribe to runtime settings changes for logging and theme updates
        configManager.SettingsChanged += OnSettingsChanged;

        // Apply the saved theme before showing any window
        ApplyThemeFromSettings(settings.DefaultTheme);

        // Check for legacy Heimdall installation and offer migration on first run
        await TryMigrateLegacyAsync(configManager, localization);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        _mainViewModel = mainWindow.DataContext as MainViewModel;
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
        services.AddSingleton<X11ServerManager>();
        services.AddSingleton<ConnectionService>();
        services.AddSingleton<EmbeddedSessionManager>();
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
    /// Ensures an HMAC key exists in settings and initializes the
    /// <see cref="CredentialProtector"/> for use across the application.
    /// Generates a new key on first run.
    /// </summary>
    private static async Task InitializeHmacKeyAsync(
        ConfigManager configManager, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.HmacKey))
        {
            // First run: generate and persist an HMAC key
            settings.HmacKey = HmacIntegrity.GenerateKey();
            settings.HmacKeyCreatedAt = DateTime.UtcNow;
            await configManager.SaveSettingsAsync(settings);
            Heimdall.Core.Logging.FileLogger.Info("HMAC key generated for credential integrity");
        }

        // Decrypt the DPAPI-protected HMAC key to raw form for CredentialProtector
        try
        {
            var rawKey = DpapiProvider.Unprotect(settings.HmacKey);
            CredentialProtector.Initialize(rawKey);
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Error(
                "Failed to initialize HMAC key — credentials will use plain DPAPI", ex);
            CredentialProtector.Initialize(null);
        }
    }

    /// <summary>
    /// Detects a legacy Heimdall (PowerShell) installation by searching
    /// for an RDPManager sibling directory up the directory tree.
    /// Only prompts on first run (when servers.json does not yet contain data).
    /// </summary>
    private static async Task TryMigrateLegacyAsync(
        ConfigManager configManager, LocalizationManager localization)
    {
        // Only offer migration when servers.json is empty or missing (first run)
        var existingServers = await configManager.LoadServersAsync();
        if (existingServers.Count > 0)
        {
            return;
        }

        // Walk up from the base directory looking for a sibling RDPManager folder
        var searchDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        string? legacyPath = null;

        while (searchDir?.Parent != null)
        {
            var candidate = Path.Combine(searchDir.Parent.FullName, "RDPManager");
            if (MigrationService.DetectLegacyInstallation(candidate))
            {
                legacyPath = candidate;
                break;
            }

            candidate = Path.Combine(searchDir.FullName, "RDPManager");
            if (MigrationService.DetectLegacyInstallation(candidate))
            {
                legacyPath = candidate;
                break;
            }

            searchDir = searchDir.Parent;
        }

        if (legacyPath is null)
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

    /// <summary>
    /// Handles runtime settings changes by updating logging state and theme.
    /// Invoked on the thread that saved the settings; theme swap is dispatched
    /// to the UI thread.
    /// </summary>
    private void OnSettingsChanged(Core.Configuration.AppSettings newSettings)
    {
        // Update logging state
        Core.Logging.FileLogger.SetEnabled(newSettings.EnableLogging);

        // Apply theme change on the UI thread
        Dispatcher.InvokeAsync(() => ApplyThemeFromSettings(newSettings.DefaultTheme));
    }

    /// <summary>
    /// Replaces the default theme dictionary loaded from App.xaml with the
    /// one selected in the user's saved settings (Light or Dark).
    /// </summary>
    private void ApplyThemeFromSettings(string themeName)
    {
        if (string.Equals(themeName, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            // App.xaml already loads DarkTheme.xaml by default.
            return;
        }

        var themeUri = new Uri("Themes/LightTheme.xaml", UriKind.Relative);
        var newTheme = new ResourceDictionary { Source = themeUri };

        var existing = Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("Theme") == true);

        if (existing is not null)
        {
            Resources.MergedDictionaries.Remove(existing);
        }

        Resources.MergedDictionaries.Add(newTheme);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            // Close all active sessions (SSH, SFTP, RDP, Local — disposes host controls + kills processes)
            try
            {
                _mainViewModel?.Connection.CloseAllSessionsCommand.Execute(null);
            }
            catch { /* Best-effort shutdown: session cleanup must not prevent exit */ }

            // Close all active tunnels (Plink tunnel processes)
            try
            {
                var tunnelManager = _serviceProvider.GetService<TunnelManager>();
                tunnelManager?.Dispose();
            }
            catch { /* Best-effort shutdown: tunnel cleanup must not prevent exit */ }

            // Stop scheduled task engine
            try
            {
                _mainViewModel?.StopScheduler();
            }
            catch { /* Best-effort shutdown */ }

            // Stop managed X11 server
            try
            {
                var x11Manager = _serviceProvider.GetService<X11ServerManager>();
                x11Manager?.Stop();
            }
            catch { /* Best-effort shutdown */ }

            // Release sleep prevention
            try
            {
                SleepPrevention.ForceRelease();
            }
            catch { /* Best-effort shutdown */ }
        }

        _serviceProvider?.Dispose();

        Core.Logging.FileLogger.Info("Heimdall.Next shutdown complete");
        Core.Logging.FileLogger.Flush();
        base.OnExit(e);
    }
}
