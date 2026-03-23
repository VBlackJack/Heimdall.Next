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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
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
public partial class JwtParserView : UserControl, IToolView
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

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtInput.Focus();
            TxtInput.SelectAll();
        });
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

        BtnCopyHeader.Content = L("ToolJwtBtnCopyHeader");
        BtnCopyPayload.Content = L("ToolJwtBtnCopyPayload");
        BtnCopySignature.Content = L("ToolJwtBtnCopySignature");
        BtnCopyHeader.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopyPayload.ToolTip = L("ToolBtnCopyToClipboard");
        BtnCopySignature.ToolTip = L("ToolBtnCopyToClipboard");
        LblVerifyTitle.Text = L("ToolJwtVerifyHmacTitle");
        LblSecret.Text = L("ToolJwtSecretLabel");
        BtnVerify.Content = L("ToolJwtBtnVerify");
        TxtUnsupportedAlg.Text = L("ToolJwtUnsupportedAlg");

        AutomationProperties.SetName(TxtInput, L("ToolJwtInputLabel"));
        AutomationProperties.SetName(TxtHeader, L("ToolJwtHeaderLabel"));
        AutomationProperties.SetName(TxtPayload, L("ToolJwtPayloadLabel"));
        AutomationProperties.SetName(TxtSignature, L("ToolJwtSignatureLabel"));
        AutomationProperties.SetName(TxtSecret, L("ToolJwtSecretLabel"));
        AutomationProperties.SetName(BtnVerify, L("ToolJwtBtnVerify"));

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));

        TxtInput.Tag = L("ToolWatermarkPasteJwt");
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
            UpdateVerifySection(headerJson);
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
                    var errorBrush = (SolidColorBrush)FindResource("ErrorBrush");
                    ExpirationBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, errorBrush.Color.R, errorBrush.Color.G, errorBrush.Color.B));
                    ExpirationBorder.BorderBrush = errorBrush;
                    ExpirationBorder.BorderThickness = new Thickness(1);
                    TxtExpiration.Foreground = errorBrush;
                    TxtExpiration.Text = string.Format(L("ToolJwtExpired"), expTime.ToLocalTime().ToString("F"));
                }
                else
                {
                    var successBrush = (SolidColorBrush)FindResource("SuccessBrush");
                    ExpirationBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, successBrush.Color.R, successBrush.Color.G, successBrush.Color.B));
                    ExpirationBorder.BorderBrush = successBrush;
                    ExpirationBorder.BorderThickness = new Thickness(1);
                    TxtExpiration.Foreground = successBrush;
                    TxtExpiration.Text = string.Format(L("ToolJwtValid"), expTime.ToLocalTime().ToString("F"));
                }

                ExpirationBorder.Visibility = Visibility.Visible;
            }
            else
            {
                var secondaryBrush = (SolidColorBrush)FindResource("TextSecondaryBrush");
                ExpirationBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, secondaryBrush.Color.R, secondaryBrush.Color.G, secondaryBrush.Color.B));
                ExpirationBorder.BorderBrush = secondaryBrush;
                ExpirationBorder.BorderThickness = new Thickness(1);
                TxtExpiration.Foreground = secondaryBrush;
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
        VerifySection.Visibility = Visibility.Collapsed;
        TxtVerifyResult.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
    }

    private void OnCopyHeaderClick(object sender, RoutedEventArgs e)
    {
        CopyTextToClipboard(TxtHeader.Text, sender as Button);
    }

    private void OnCopyPayloadClick(object sender, RoutedEventArgs e)
    {
        CopyTextToClipboard(TxtPayload.Text, sender as Button);
    }

    private void OnCopySignatureClick(object sender, RoutedEventArgs e)
    {
        CopyTextToClipboard(TxtSignature.Text, sender as Button);
    }

    private static void CopyTextToClipboard(string? text, Button? btn)
    {
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
            CopyFeedbackHelper.ShowCopyFeedback(btn);
        }
    }

    private void UpdateVerifySection(string headerJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(headerJson);
            if (doc.RootElement.TryGetProperty("alg", out var algElement))
            {
                var alg = algElement.GetString() ?? string.Empty;

                if (alg is "HS256" or "HS384" or "HS512")
                {
                    VerifySection.Visibility = Visibility.Visible;
                    HmacVerifyPanel.Visibility = Visibility.Visible;
                    TxtUnsupportedAlg.Visibility = Visibility.Collapsed;
                    TxtVerifyResult.Visibility = Visibility.Collapsed;
                }
                else if (alg.StartsWith("RS", StringComparison.Ordinal) ||
                         alg.StartsWith("ES", StringComparison.Ordinal) ||
                         alg.StartsWith("PS", StringComparison.Ordinal))
                {
                    VerifySection.Visibility = Visibility.Visible;
                    HmacVerifyPanel.Visibility = Visibility.Collapsed;
                    TxtUnsupportedAlg.Visibility = Visibility.Visible;
                }
                else
                {
                    VerifySection.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                VerifySection.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            VerifySection.Visibility = Visibility.Collapsed;
        }
    }

    private void OnVerifyClick(object sender, RoutedEventArgs e)
    {
        var input = TxtInput.Text?.Trim();
        var secret = TxtSecret.Text;

        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(secret))
        {
            return;
        }

        VerifyHmacSignature(input, secret);
    }

    private void VerifyHmacSignature(string jwt, string secret)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return;

        var headerJson = DecodeBase64Url(parts[0]);
        if (headerJson is null) return;

        string? alg;
        try
        {
            using var doc = JsonDocument.Parse(headerJson);
            alg = doc.RootElement.TryGetProperty("alg", out var algElement)
                ? algElement.GetString()
                : null;
        }
        catch
        {
            return;
        }

        var payload = parts[0] + "." + parts[1];
        var signature = parts[2];

        var key = Encoding.UTF8.GetBytes(secret);
        var inputBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = alg switch
        {
            "HS256" => (HMAC)new HMACSHA256(key),
            "HS384" => new HMACSHA384(key),
            "HS512" => new HMACSHA512(key),
            _ => null
        };

        if (hmac is null) return;

        var computed = Base64UrlEncode(hmac.ComputeHash(inputBytes));
        var isValid = string.Equals(computed, signature, StringComparison.Ordinal);

        TxtVerifyResult.Visibility = Visibility.Visible;

        if (isValid)
        {
            TxtVerifyResult.Text = "\u2714 " + L("ToolJwtSignatureValid");
            TxtVerifyResult.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            TxtVerifyResult.Text = "\u2716 " + L("ToolJwtSignatureInvalid");
            TxtVerifyResult.Foreground = (Brush)FindResource("ErrorBrush");
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpJWT");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
