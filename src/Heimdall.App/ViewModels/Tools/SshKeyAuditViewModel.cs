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
using CommunityToolkit.Mvvm.Input;
using Heimdall.Core.Localization;
using Heimdall.Core.Security;

namespace Heimdall.App.ViewModels.Tools;

/// <summary>
/// Thin ViewModel for the SSH Key Audit tool. Delegates all parsing and
/// security assessment to <see cref="SshKeyAuditEngine"/> and projects
/// the result as bindable properties.
/// </summary>
public sealed partial class SshKeyAuditViewModel : ObservableObject
{
    private LocalizationManager? _localizer;

    [ObservableProperty] private string _keyText = string.Empty;

    [ObservableProperty] private string _algorithm = string.Empty;
    [ObservableProperty] private int _keySize;
    [ObservableProperty] private string _fingerprint = string.Empty;
    [ObservableProperty] private string _format = string.Empty;
    [ObservableProperty] private bool _isPrivateKey;
    [ObservableProperty] private bool _isEncrypted;
    [ObservableProperty] private SecurityRating _rating = SecurityRating.Acceptable;
    [ObservableProperty] private IReadOnlyList<SshKeyAuditFinding> _findings = [];

    [ObservableProperty] private bool _showEmptyState = true;
    [ObservableProperty] private bool _showParseError;
    [ObservableProperty] private bool _showResults;

    public void Initialize(LocalizationManager? localizer)
    {
        _localizer = localizer;
    }

    /// <summary>
    /// Runs the audit on the current <see cref="KeyText"/>.
    /// Called by the view after the debounce timer fires.
    /// </summary>
    [RelayCommand]
    private void RunAudit()
    {
        if (string.IsNullOrWhiteSpace(KeyText))
        {
            ClearResult();
            ShowEmptyState = true;
            return;
        }

        var result = SshKeyAuditEngine.Audit(KeyText, key => _localizer?[key] ?? key);
        if (result is null)
        {
            ClearResult();
            ShowParseError = true;
            return;
        }

        Algorithm = result.Algorithm;
        KeySize = result.KeySize;
        Fingerprint = result.Fingerprint;
        Format = result.Format;
        IsPrivateKey = result.IsPrivateKey;
        IsEncrypted = result.IsEncrypted;
        Rating = result.Rating;
        Findings = result.Findings;

        ShowEmptyState = false;
        ShowParseError = false;
        ShowResults = true;
    }

    private void ClearResult()
    {
        ShowEmptyState = false;
        ShowParseError = false;
        ShowResults = false;
        Algorithm = string.Empty;
        KeySize = 0;
        Fingerprint = string.Empty;
        Format = string.Empty;
        IsPrivateKey = false;
        IsEncrypted = false;
        Rating = SecurityRating.Acceptable;
        Findings = [];
    }
}
