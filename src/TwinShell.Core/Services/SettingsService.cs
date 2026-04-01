using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TwinShell.Core.Helpers;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;

namespace TwinShell.Core.Services;

/// <summary>
/// Service for managing user settings and preferences.
/// Settings are persisted in JSON format at %APPDATA%/TwinShell/settings.json
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;
    private readonly ILogger<SettingsService>? _logger;
    private UserSettings _currentSettings;

    private static JsonSerializerOptions JsonOptions => JsonOptionsHelper.DefaultWithEnumConverter;

    public SettingsService(ILogger<SettingsService>? logger = null)
        : this(null, logger)
    {
    }

    /// <summary>
    /// Creates a SettingsService with a custom settings directory (for testing).
    /// </summary>
    /// <param name="customSettingsDirectory">Custom directory path, or null to use default</param>
    /// <param name="logger">Optional logger</param>
    public SettingsService(string? customSettingsDirectory, ILogger<SettingsService>? logger = null)
    {
        _logger = logger;

        // Use custom directory or default to %APPDATA%/TwinShell
        _settingsDirectory = customSettingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TwinShell");

        _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        _currentSettings = UserSettings.Default;
    }

    /// <inheritdoc/>
    public UserSettings CurrentSettings => _currentSettings;

    /// <inheritdoc/>
    public async Task<UserSettings> LoadSettingsAsync()
    {
        try
        {
            // Ensure directory exists
            if (!Directory.Exists(_settingsDirectory))
            {
                Directory.CreateDirectory(_settingsDirectory);
            }

            // Check if settings file exists
            if (!File.Exists(_settingsFilePath))
            {
                // Create default settings file
                _currentSettings = UserSettings.Default;
                await SaveSettingsAsync(_currentSettings).ConfigureAwait(false);
                return _currentSettings;
            }

            // SECURITY: Read and decrypt settings
            // BUGFIX: ConfigureAwait(false) prevents deadlock when called from UI thread
            var encrypted = await File.ReadAllBytesAsync(_settingsFilePath).ConfigureAwait(false);
            string json;

            try
            {
                // Try to decrypt (new format)
                json = DecryptData(encrypted);
            }
            catch (CryptographicException)
            {
                // If decryption fails, assume it's an old unencrypted file
                // Read as plain text for backward compatibility
                json = Encoding.UTF8.GetString(encrypted);
            }

            var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);

            if (settings == null || !ValidateSettings(settings))
            {
                // If deserialization fails or validation fails, use defaults
                _currentSettings = UserSettings.Default;
                await SaveSettingsAsync(_currentSettings).ConfigureAwait(false);
                return _currentSettings;
            }

            _currentSettings = settings;
            return _currentSettings;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings");
            // On any error, return default settings
            _currentSettings = UserSettings.Default;
            return _currentSettings;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SaveSettingsAsync(UserSettings settings)
    {
        try
        {
            // Validate before saving
            if (!ValidateSettings(settings))
            {
                return false;
            }

            // Ensure directory exists
            if (!Directory.Exists(_settingsDirectory))
            {
                Directory.CreateDirectory(_settingsDirectory);
            }

            // SECURITY: Encrypt settings before saving
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var encrypted = EncryptData(json);
            // BUGFIX: ConfigureAwait(false) prevents deadlock when called from UI thread
            await File.WriteAllBytesAsync(_settingsFilePath, encrypted).ConfigureAwait(false);

            // SECURITY: Set restrictive file permissions
            if (OperatingSystem.IsWindows())
            {
                SetRestrictivePermissions(_settingsFilePath);
            }
            else
            {
                SetUnixFilePermissions(_settingsFilePath);
            }

            _currentSettings = settings;
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save settings");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<UserSettings> ResetToDefaultAsync()
    {
        _currentSettings = UserSettings.Default;
        // BUGFIX: ConfigureAwait(false) prevents deadlock when called from UI thread
        await SaveSettingsAsync(_currentSettings).ConfigureAwait(false);
        return _currentSettings;
    }

    /// <inheritdoc/>
    public string GetSettingsFilePath()
    {
        return _settingsFilePath;
    }

    /// <inheritdoc/>
    public bool ValidateSettings(UserSettings settings)
    {
        if (settings == null)
            return false;

        // Validate AutoCleanupDays (minimum 1 day, maximum 3650 days / 10 years)
        if (settings.AutoCleanupDays < 1 || settings.AutoCleanupDays > 3650)
            return false;

        // Validate MaxHistoryItems (minimum 10, maximum 100000)
        if (settings.MaxHistoryItems < 10 || settings.MaxHistoryItems > 100000)
            return false;

        return true;
    }

    /// <summary>
    /// Encrypts data using user-specific key (DPAPI on Windows, AES with user-derived key on Linux)
    /// </summary>
    private byte[] EncryptData(string data)
    {
        var dataBytes = Encoding.UTF8.GetBytes(data);

        if (OperatingSystem.IsWindows())
        {
            // Use DPAPI on Windows (user-specific encryption)
            return ProtectedData.Protect(dataBytes, null, DataProtectionScope.CurrentUser);
        }
        else
        {
            // Use AES with user-derived key on Linux/Mac
            using var aes = Aes.Create();
            aes.Key = DeriveUserKey();

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            var encrypted = encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);

            // Combine IV + ciphertext
            var result = new byte[aes.IV.Length + encrypted.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

            return result;
        }
    }

    /// <summary>
    /// Decrypts data using user-specific key
    /// </summary>
    private string DecryptData(byte[] encryptedData)
    {
        if (OperatingSystem.IsWindows())
        {
            // Use DPAPI on Windows
            var decrypted = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        else
        {
            // Use AES on Linux/Mac
            using var aes = Aes.Create();
            aes.Key = DeriveUserKey();

            // Extract IV and ciphertext
            var iv = new byte[aes.IV.Length];
            var ciphertext = new byte[encryptedData.Length - aes.IV.Length];

            Buffer.BlockCopy(encryptedData, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(encryptedData, iv.Length, ciphertext, 0, ciphertext.Length);

            using var decryptor = aes.CreateDecryptor(aes.Key, iv);
            var decrypted = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

            return Encoding.UTF8.GetString(decrypted);
        }
    }

    /// <summary>
    /// Derives a user-specific encryption key using stored or generated random salt
    /// </summary>
    private byte[] DeriveUserKey()
    {
        // SECURITY: Use username + machine name as entropy for key derivation
        var entropy = Environment.UserName + Environment.MachineName;

        // SECURITY: Use random salt stored in a separate file
        var salt = GetOrCreateRandomSalt();

        // SECURITY: 100,000 iterations for PBKDF2 as recommended by OWASP
        return Rfc2898DeriveBytes.Pbkdf2(
            entropy,
            salt,
            iterations: 100000,
            HashAlgorithmName.SHA256,
            outputLength: 32); // 256-bit key
    }

    /// <summary>
    /// Gets or creates a random salt for key derivation.
    /// Salt is stored in a separate file to ensure it persists across application restarts.
    /// </summary>
    private byte[] GetOrCreateRandomSalt()
    {
        var saltFilePath = Path.Combine(_settingsDirectory, ".salt");

        try
        {
            // Check if salt file exists and is valid
            if (File.Exists(saltFilePath))
            {
                var existingSalt = File.ReadAllBytes(saltFilePath);
                if (existingSalt.Length == 32) // Expected salt size
                {
                    return existingSalt;
                }
            }

            // Generate new random salt
            var salt = RandomNumberGenerator.GetBytes(32);

            // Ensure directory exists
            if (!Directory.Exists(_settingsDirectory))
            {
                Directory.CreateDirectory(_settingsDirectory);
            }

            // Save salt
            File.WriteAllBytes(saltFilePath, salt);

            // Set restrictive permissions on salt file
            if (OperatingSystem.IsWindows())
            {
                SetRestrictivePermissions(saltFilePath);
            }
            else
            {
                SetUnixFilePermissions(saltFilePath);
            }

            return salt;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read/create salt file, falling back to deterministic salt");

            // Fallback to deterministic salt (less secure but maintains backward compatibility)
            var saltInput = $"TwinShell.Settings.v1.{Environment.MachineName}.{Environment.UserName}";
            return SHA256.HashData(Encoding.UTF8.GetBytes(saltInput));
        }
    }

    /// <summary>
    /// Sets restrictive file permissions (Windows only)
    /// </summary>
    private void SetRestrictivePermissions(string filePath)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return;

            var fileInfo = new FileInfo(filePath);
            var fileSecurity = fileInfo.GetAccessControl();

            // Remove inherited permissions
            fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // Clear all existing rules
            var existingRules = fileSecurity.GetAccessRules(true, false, typeof(System.Security.Principal.NTAccount));
            foreach (System.Security.AccessControl.FileSystemAccessRule rule in existingRules)
            {
                fileSecurity.RemoveAccessRule(rule);
            }

            // Add permission only for current user
            var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                var accessRule = new System.Security.AccessControl.FileSystemAccessRule(
                    currentUser,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow);

                fileSecurity.AddAccessRule(accessRule);
            }

            fileInfo.SetAccessControl(fileSecurity);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to set restrictive permissions on settings file");
        }
    }

    /// <summary>
    /// Sets restrictive file permissions on Linux/Mac (chmod 600 - owner read/write only)
    /// </summary>
    private void SetUnixFilePermissions(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return;

            // Use File.SetUnixFileMode to set permissions to 600 (owner read/write only)
            // This is only available on .NET 7+ and Unix systems
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to set Unix file permissions on {FilePath}", filePath);
        }
    }
}
