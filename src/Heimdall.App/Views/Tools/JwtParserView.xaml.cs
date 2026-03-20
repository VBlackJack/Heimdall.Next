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

using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// JWT parser tool that decodes and displays the header, payload, and signature
/// of a JSON Web Token with color-coded sections and expiration status.
/// </summary>
public partial class JwtParserView : UserControl, IDisposable
{
    private LocalizationManager? _localizer;
    private DispatcherTimer? _debounceTimer;

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    public JwtParserView()
    {
        InitializeComponent();
        InitializeDebounceTimer();
    }

    /// <summary>
    /// Initializes the tool with localization and optional context.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        ApplyLocalization();

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            TxtInput.Text = context.Argument;
        }
    }

    private void InitializeDebounceTimer()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            ParseJwt();
        };
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolJwtTitle");
        LblInput.Text = L("ToolJwtInputLabel");
        LblHeader.Text = L("ToolJwtHeaderLabel");
        LblPayload.Text = L("ToolJwtPayloadLabel");
        LblSignature.Text = L("ToolJwtSignatureLabel");

        System.Windows.Automation.AutomationProperties.SetName(TxtInput, L("ToolJwtInputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtHeader, L("ToolJwtHeaderLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtPayload, L("ToolJwtPayloadLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtSignature, L("ToolJwtSignatureLabel"));
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void ParseJwt()
    {
        var input = TxtInput.Text?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            ClearOutput();
            return;
        }

        var parts = input.Split('.');
        if (parts.Length != 3)
        {
            ClearOutput();
            ShowError(L("ToolJwtErrorInvalidFormat"));
            return;
        }

        try
        {
            var headerJson = DecodeBase64Url(parts[0]);
            var payloadJson = DecodeBase64Url(parts[1]);
            var signatureBytes = DecodeBase64UrlBytes(parts[2]);

            if (headerJson is null || payloadJson is null)
            {
                ClearOutput();
                ShowError(L("ToolJwtErrorDecodeFailed"));
                return;
            }

            TxtHeader.Text = PrettyPrintJson(headerJson);
            TxtPayload.Text = PrettyPrintJson(payloadJson);
            TxtSignature.Text = Convert.ToHexStringLower(signatureBytes ?? []);

            TxtError.Visibility = Visibility.Collapsed;
            UpdateExpirationStatus(payloadJson);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"JwtParser decode failed: {ex.Message}");
            ClearOutput();
            ShowError(L("ToolJwtErrorDecodeFailed"));
        }
    }

    private void UpdateExpirationStatus(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("exp", out var expElement) &&
                expElement.TryGetInt64(out var expUnix))
            {
                var expTime = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                var now = DateTimeOffset.UtcNow;

                if (expTime < now)
                {
                    ExpirationBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 80, 80));
                    ExpirationBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 80, 80));
                    ExpirationBorder.BorderThickness = new Thickness(1);
                    TxtExpiration.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 80, 80));
                    TxtExpiration.Text = string.Format(L("ToolJwtExpired"), expTime.ToLocalTime().ToString("F"));
                }
                else
                {
                    ExpirationBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 80, 200, 80));
                    ExpirationBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 200, 80));
                    ExpirationBorder.BorderThickness = new Thickness(1);
                    TxtExpiration.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 200, 80));
                    TxtExpiration.Text = string.Format(L("ToolJwtValid"), expTime.ToLocalTime().ToString("F"));
                }

                ExpirationBorder.Visibility = Visibility.Visible;
            }
            else
            {
                ExpirationBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 180, 180, 180));
                ExpirationBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180));
                ExpirationBorder.BorderThickness = new Thickness(1);
                TxtExpiration.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180));
                TxtExpiration.Text = L("ToolJwtNoExpiry");
                ExpirationBorder.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            ExpirationBorder.Visibility = Visibility.Collapsed;
        }
    }

    private static string? DecodeBase64Url(string base64Url)
    {
        var bytes = DecodeBase64UrlBytes(base64Url);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private static byte[]? DecodeBase64UrlBytes(string base64Url)
    {
        try
        {
            var padded = base64Url
                .Replace('-', '+')
                .Replace('_', '/');

            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            return Convert.FromBase64String(padded);
        }
        catch
        {
            return null;
        }
    }

    private static string PrettyPrintJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, PrettyJsonOptions);
        }
        catch
        {
            return json;
        }
    }

    private void ClearOutput()
    {
        TxtHeader.Text = string.Empty;
        TxtPayload.Text = string.Empty;
        TxtSignature.Text = string.Empty;
        ExpirationBorder.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
    }

    private string L(string key) => _localizer?[key] ?? key;

    public void Dispose()
    {
        if (_debounceTimer is not null)
        {
            _debounceTimer.Stop();
            _debounceTimer = null;
        }
    }
}
