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

    public IServiceProvider? Services => _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Show splash screen during initialization (custom window for controlled size).
        // Temporarily switch to explicit shutdown so closing the splash doesn't kill the app
        // (WPF treats the first Window shown as MainWindow when ShutdownMode is OnMainWindowClose).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var splash = CreateSplashWindow();

        // Register global exception handlers BEFORE any awaits — async void
        // resumes on the dispatcher, so unhandled exceptions from awaited calls
        // must already be caught at this point.
        DispatcherUnhandledException += (_, args) =>
        {
            Heimdall.Core.Logging.FileLogger.Error("Unhandled exception", args.Exception);
            var errorTitle = "Heimdall Error";
            var errorBody = $"{args.Exception.Message}\n\n{args.Exception.StackTrace}";
            try
            {
                var loc = _serviceProvider?.GetService<LocalizationManager>();
                if (loc is not null)
                {
                    errorTitle = loc["ErrorUnhandledTitle"];
                    errorBody = loc.Format("ErrorUnhandledMessage",
                        args.Exception.Message, args.Exception.StackTrace ?? "");
                }
            }
            catch (Exception ex) { Core.Logging.FileLogger.Warn($"[App] localization lookup: {ex.Message}"); }
            MessageBox.Show(
                errorBody,
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

        // Register Windows-1252 codepage for MobaXterm .ini import
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

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

        // Apply sleep prevention setting
        SleepPrevention.Enabled = settings.PreventSleepDuringSession;

        // Pre-warm RDP COM/DLL chain and WinForms runtime on a background STA thread.
        // Forces loading of mstscax.dll + 22 static dependencies (~300-500ms) at startup
        // instead of on the first RDP connection.
        PreWarmRdpRuntime();

        // Initialize HMAC key for credential protection
        await InitializeHmacKeyAsync(configManager, settings);

        // Load trusted SSH host keys into the TOFU store
        var hostKeyStore = _serviceProvider.GetRequiredService<HostKeyStore>();
        if (settings.TrustedHostKeys.Count > 0)
        {
            var entries = settings.TrustedHostKeys.Select(kvp =>
            {
                ParseHostKeyEntry(kvp.Key, out var host, out var port);
                return (host, port, (string?)kvp.Value);
            });
            hostKeyStore.LoadFromConfig(entries);
        }

        // Persist newly trusted host keys back to settings via transactional merge.
        // MergeHostKeyAsync holds the write lock across load+mutate+save,
        // preventing concurrent TOFU events from overwriting each other.
        hostKeyStore.HostKeyEvent += (key, fingerprint, trusted) =>
        {
            if (!trusted)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await configManager.MergeHostKeyAsync(key, fingerprint);
                }
                catch (Exception ex)
                {
                    Heimdall.Core.Logging.FileLogger.Warn(
                        $"Failed to persist host key for {key}: {ex.Message}");
                }
            });
        };

        // Subscribe to runtime settings changes for logging and theme updates
        configManager.SettingsChanged += OnSettingsChanged;

        // Apply the saved theme before showing any window
        ApplyThemeFromSettings(settings.DefaultTheme);

        // Check for legacy Heimdall installation and offer migration on first run
        await TryMigrateLegacyAsync(configManager, localization);

        // Close splash before showing main window
        splash.Close();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        _mainViewModel = mainWindow.DataContext as MainViewModel;
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    /// <summary>
    /// Creates a borderless splash window with the splash image scaled to 600x448
    /// (preserving the 2400x1792 aspect ratio). Centered on screen, topmost.
    /// </summary>
    private static Window CreateSplashWindow()
    {
        var splashPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Assets", "splash-screen.png");

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            ShowInTaskbar = false,
            Width = 600,
            Height = 448,
            ResizeMode = ResizeMode.NoResize
        };

        if (System.IO.File.Exists(splashPath))
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage(new Uri(splashPath));
            window.Content = new System.Windows.Controls.Image
            {
                Source = bitmap,
                Stretch = System.Windows.Media.Stretch.Uniform
            };
        }

        window.Show();
        return window;
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
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<ConnectionService>();
        services.AddSingleton<EmbeddedSessionManager>();
        services.AddSingleton<SplitService>();
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
    /// Parses a host key entry in the form "host:port", handling IPv6 bracket
    /// notation (e.g., "[2001:db8::1]:22") correctly.
    /// </summary>
    private static void ParseHostKeyEntry(string key, out string host, out int port)
    {
        port = 22;

        if (key.StartsWith('['))
        {
            // IPv6 bracket notation: [host]:port
            var closeBracket = key.IndexOf(']');
            if (closeBracket > 0)
            {
                host = key[1..closeBracket];
                if (closeBracket + 2 < key.Length && key[closeBracket + 1] == ':')
                {
                    int.TryParse(key[(closeBracket + 2)..], out port);
                }
                return;
            }
        }

        // Standard host:port — split on the last colon only (handles bare IPv6 without brackets)
        var lastColon = key.LastIndexOf(':');
        if (lastColon > 0 && int.TryParse(key[(lastColon + 1)..], out var parsedPort))
        {
            host = key[..lastColon];
            port = parsedPort;
        }
        else
        {
            host = key;
        }
    }

    /// <summary>
    /// Pre-warms the RDP COM control and WinForms runtime on a background STA thread.
    /// This forces mstscax.dll and its 22+ static dependencies into process memory,
    /// eliminating the 300-500ms cold-start penalty on the first actual RDP connection.
    /// </summary>
    private static void PreWarmRdpRuntime()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Force COM activation of MsTscAx — loads mstscax.dll + all static dependencies.
                // Do NOT create WindowsFormsHost here: it is a WPF FrameworkElement and
                // initializing the WPF-WinForms bridge on a background thread corrupts
                // the interop layer for the real UI thread.
                using var host = new Heimdall.Rdp.ActiveX.RdpActiveXHost();
                _ = host.Handle;

                sw.Stop();
                Heimdall.Core.Logging.FileLogger.Info(
                    $"RDP COM pre-warm completed in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                sw.Stop();
                Heimdall.Core.Logging.FileLogger.Warn(
                    $"RDP COM pre-warm failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
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
            catch (Exception ex) { Core.Logging.FileLogger.Warn($"[App] session cleanup: {ex.Message}"); }

            // Close all active tunnels (Plink tunnel processes)
            try
            {
                var tunnelManager = _serviceProvider.GetService<TunnelManager>();
                tunnelManager?.Dispose();
            }
            catch (Exception ex) { Core.Logging.FileLogger.Warn($"[App] tunnel cleanup: {ex.Message}"); }

            // Stop scheduled task engine
            try
            {
                _mainViewModel?.StopScheduler();
            }
            catch (Exception ex) { Core.Logging.FileLogger.Warn($"[App] scheduler cleanup: {ex.Message}"); }

            // Stop managed X11 server
            try
            {
                var x11Manager = _serviceProvider.GetService<X11ServerManager>();
                x11Manager?.Stop();
            }
            catch (Exception ex) { Core.Logging.FileLogger.Warn($"[App] X11 cleanup: {ex.Message}"); }

            // Release sleep prevention
            try
            {
                SleepPrevention.ForceRelease();
            }
            catch (Exception ex) { Core.Logging.FileLogger.Warn($"[App] sleep prevention cleanup: {ex.Message}"); }
        }

        _serviceProvider?.Dispose();

        Core.Logging.FileLogger.Info("Heimdall.Next shutdown complete");
        Core.Logging.FileLogger.Flush();
        base.OnExit(e);
    }
}
