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

using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Jwt;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Tools;

public sealed partial class JwtParserViewModel : ObservableObject, IDisposable
{
    private readonly IJwtParserToolService _service;
    private readonly IUiDispatcher _uiDispatcher;
    private LocalizationManager? _localizer;
    private bool _disposed;
    private JwtDecoded? _decoded;
    private JwtAlgorithmKind _algorithmKind = JwtAlgorithmKind.Unknown;

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _headerText = string.Empty;
    [ObservableProperty] private string _payloadText = string.Empty;
    [ObservableProperty] private string _signatureHexText = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _isErrorVisible;
    [ObservableProperty] private string _expirationText = string.Empty;
    [ObservableProperty] private JwtExpirationStatus _expirationStatus = JwtExpirationStatus.NoExpiry;
    [ObservableProperty] private bool _isExpirationVisible;
    [ObservableProperty] private bool _isEmptyStateVisible = true;
    [ObservableProperty] private bool _isVerifySectionVisible;
    [ObservableProperty] private bool _isHmacVerifyVisible;
    [ObservableProperty] private bool _isUnsupportedAlgVisible;
    [ObservableProperty] private string _secretText = string.Empty;
    [ObservableProperty] private string _verifyResultText = string.Empty;
    [ObservableProperty] private bool _isVerifyResultValid;
    [ObservableProperty] private bool _isVerifyResultVisible;

    public JwtParserViewModel(IUiDispatcher uiDispatcher, IJwtParserToolService? service = null)
    {
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _service = service ?? new JwtParserToolService();
    }

    public void Initialize(LocalizationManager? localizer)
    {
        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
        }

        _localizer = localizer;
        if (_localizer is not null)
        {
            _localizer.LocaleChanged += OnLocaleChanged;
        }
    }

    public void PrefillInput(string? input)
    {
        if (!string.IsNullOrWhiteSpace(input))
        {
            InputText = input;
        }
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

        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private void Parse()
    {
        if (_disposed)
        {
            return;
        }

        var input = InputText.Trim();
        if (string.IsNullOrEmpty(input))
        {
            ClearOutput();
            return;
        }

        if (!_service.TryDecode(input, out var decoded, out var error) || decoded is null)
        {
            ClearOutput();
            ShowError(error switch
            {
                JwtDecodeError.InvalidFormat => L("ToolJwtErrorInvalidFormat"),
                _ => L("ToolJwtErrorDecodeFailed"),
            });
            return;
        }

        _decoded = decoded;
        HeaderText = decoded.PrettyHeaderJson;
        PayloadText = decoded.PrettyPayloadJson;
        SignatureHexText = decoded.SignatureHex;
        IsEmptyStateVisible = false;
        IsErrorVisible = false;
        ErrorText = string.Empty;
        UpdateExpiration();
        UpdateVerificationSection(decoded.HeaderJson);
        ResetVerificationResult();
        VerifyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private void Verify()
    {
        if (_decoded is null || string.IsNullOrWhiteSpace(SecretText))
        {
            return;
        }

        var result = _service.VerifyHmac(_decoded, _algorithmKind, SecretText);
        switch (result)
        {
            case JwtHmacVerificationResult.Valid:
                IsVerifyResultVisible = true;
                IsVerifyResultValid = true;
                VerifyResultText = "\u2714 " + L("ToolJwtSignatureValid");
                break;
            case JwtHmacVerificationResult.Invalid:
                IsVerifyResultVisible = true;
                IsVerifyResultValid = false;
                VerifyResultText = "\u2716 " + L("ToolJwtSignatureInvalid");
                break;
            default:
                ResetVerificationResult();
                break;
        }
    }

    partial void OnSecretTextChanged(string value) => VerifyCommand.NotifyCanExecuteChanged();

    private bool CanVerify()
        => !_disposed && _decoded is not null && _algorithmKind is JwtAlgorithmKind.Hmac256 or JwtAlgorithmKind.Hmac384 or JwtAlgorithmKind.Hmac512 && !string.IsNullOrWhiteSpace(SecretText);

    private void UpdateExpiration()
    {
        var evaluation = _service.EvaluateExpiration(PayloadText, DateTimeOffset.UtcNow);
        ExpirationStatus = evaluation.Status;
        if (evaluation.Status == JwtExpirationStatus.InvalidClaim)
        {
            IsExpirationVisible = false;
            ExpirationText = string.Empty;
            return;
        }

        IsExpirationVisible = true;
        ExpirationText = evaluation.Status switch
        {
            JwtExpirationStatus.Expired => string.Format(
                CultureInfo.CurrentCulture,
                L("ToolJwtExpired"),
                evaluation.ExpiresAt!.Value.ToLocalTime().ToString("F", CultureInfo.CurrentCulture)),
            JwtExpirationStatus.Valid => string.Format(
                CultureInfo.CurrentCulture,
                L("ToolJwtValid"),
                evaluation.ExpiresAt!.Value.ToLocalTime().ToString("F", CultureInfo.CurrentCulture)),
            _ => L("ToolJwtNoExpiry"),
        };
    }

    private void UpdateVerificationSection(string headerJson)
    {
        _algorithmKind = _service.ClassifyAlgorithm(ExtractAlgorithm(headerJson));
        switch (_algorithmKind)
        {
            case JwtAlgorithmKind.Hmac256:
            case JwtAlgorithmKind.Hmac384:
            case JwtAlgorithmKind.Hmac512:
                IsVerifySectionVisible = true;
                IsHmacVerifyVisible = true;
                IsUnsupportedAlgVisible = false;
                break;
            case JwtAlgorithmKind.Rsa:
            case JwtAlgorithmKind.Ecdsa:
            case JwtAlgorithmKind.RsaPss:
                IsVerifySectionVisible = true;
                IsHmacVerifyVisible = false;
                IsUnsupportedAlgVisible = true;
                break;
            default:
                IsVerifySectionVisible = false;
                IsHmacVerifyVisible = false;
                IsUnsupportedAlgVisible = false;
                break;
        }
    }

    private void ResetVerificationResult()
    {
        IsVerifyResultVisible = false;
        IsVerifyResultValid = false;
        VerifyResultText = string.Empty;
    }

    private void ShowError(string message)
    {
        _decoded = null;
        _algorithmKind = JwtAlgorithmKind.Unknown;
        HeaderText = string.Empty;
        PayloadText = string.Empty;
        SignatureHexText = string.Empty;
        ErrorText = message;
        IsErrorVisible = true;
        IsEmptyStateVisible = false;
        IsExpirationVisible = false;
        ExpirationText = string.Empty;
        IsVerifySectionVisible = false;
        IsHmacVerifyVisible = false;
        IsUnsupportedAlgVisible = false;
        ResetVerificationResult();
        VerifyCommand.NotifyCanExecuteChanged();
    }

    private void ClearOutput()
    {
        _decoded = null;
        _algorithmKind = JwtAlgorithmKind.Unknown;
        HeaderText = string.Empty;
        PayloadText = string.Empty;
        SignatureHexText = string.Empty;
        ErrorText = string.Empty;
        IsErrorVisible = false;
        IsEmptyStateVisible = true;
        IsExpirationVisible = false;
        ExpirationText = string.Empty;
        IsVerifySectionVisible = false;
        IsHmacVerifyVisible = false;
        IsUnsupportedAlgVisible = false;
        ResetVerificationResult();
        VerifyCommand.NotifyCanExecuteChanged();
    }

    private void OnLocaleChanged(string _locale)
    {
        void RefreshForLocaleChange()
        {
            if (string.IsNullOrWhiteSpace(InputText))
            {
                return;
            }

            var rerunVerification = IsVerifyResultVisible && CanVerify();
            Parse();
            if (rerunVerification)
            {
                Verify();
            }
        }

        if (_uiDispatcher.CheckAccess())
        {
            RefreshForLocaleChange();
        }
        else
        {
            _ = _uiDispatcher.InvokeAsync(RefreshForLocaleChange);
        }
    }

    private static string? ExtractAlgorithm(string headerJson)
    {
        using var document = JsonDocument.Parse(headerJson);
        return document.RootElement.TryGetProperty("alg", out var property)
            ? property.GetString()
            : null;
    }

    private string L(string key) => _localizer?[key] ?? key;
}
