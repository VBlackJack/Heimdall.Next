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
using Heimdall.App.Services;
using Heimdall.Core.Localization;
using Heimdall.Core.Permissions;

namespace Heimdall.App.ViewModels.Tools;

public sealed partial class ChmodCalculatorViewModel : ObservableObject, IDisposable
{
    private readonly IChmodCalculatorToolService _service;
    private LocalizationManager? _localizer;
    private bool _initialized;
    private bool _disposed;
    private bool _syncing;
    private string? _lastSymbolicNotation;

    [ObservableProperty] private bool _ownerR;
    [ObservableProperty] private bool _ownerW;
    [ObservableProperty] private bool _ownerX;
    [ObservableProperty] private bool _groupR;
    [ObservableProperty] private bool _groupW;
    [ObservableProperty] private bool _groupX;
    [ObservableProperty] private bool _othersR;
    [ObservableProperty] private bool _othersW;
    [ObservableProperty] private bool _othersX;
    [ObservableProperty] private string _octalText = string.Empty;
    [ObservableProperty] private string _symbolicText = string.Empty;
    [ObservableProperty] private string _symbolicInputText = string.Empty;
    [ObservableProperty] private string _symbolicInputErrorText = string.Empty;
    [ObservableProperty] private bool _hasSymbolicInputError;
    [ObservableProperty] private string _commandPreviewText = string.Empty;

    public string HelpText => L("ToolHelpCHMOD").Replace("\\n", "\n", StringComparison.Ordinal);
    public string OwnerRAutomationName => A11y("ToolChmodOwner", "ToolChmodRead");
    public string OwnerWAutomationName => A11y("ToolChmodOwner", "ToolChmodWrite");
    public string OwnerXAutomationName => A11y("ToolChmodOwner", "ToolChmodExecute");
    public string GroupRAutomationName => A11y("ToolChmodGroup", "ToolChmodRead");
    public string GroupWAutomationName => A11y("ToolChmodGroup", "ToolChmodWrite");
    public string GroupXAutomationName => A11y("ToolChmodGroup", "ToolChmodExecute");
    public string OthersRAutomationName => A11y("ToolChmodOthers", "ToolChmodRead");
    public string OthersWAutomationName => A11y("ToolChmodOthers", "ToolChmodWrite");
    public string OthersXAutomationName => A11y("ToolChmodOthers", "ToolChmodExecute");

    public ChmodCalculatorViewModel(IChmodCalculatorToolService? service = null)
    {
        _service = service ?? new ChmodCalculatorToolService();
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

    public void ApplyPrefill(string? argument)
    {
        var mode = _service.TryParseOctal(argument, out var parsed)
            ? parsed
            : PosixMode.Preset755;
        ApplyMode(mode);
    }

    public void MarkInitialized() => _initialized = true;

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
    private void ApplyPreset(string? octal)
    {
        if (_disposed || !_service.TryParseOctal(octal, out var mode))
        {
            return;
        }

        ClearSymbolicError();
        _lastSymbolicNotation = null;
        ApplyMode(mode);
    }

    [RelayCommand]
    private void ApplySymbolic()
    {
        if (_disposed)
        {
            return;
        }

        var input = SymbolicInputText.Trim();
        if (string.IsNullOrEmpty(input))
        {
            ClearSymbolicError();
            return;
        }

        if (!_service.TryParseSymbolic(input, out var mode))
        {
            HasSymbolicInputError = true;
            SymbolicInputErrorText = L("ToolChmodErrorInvalidSymbolic");
            return;
        }

        ClearSymbolicError();
        _lastSymbolicNotation = input;
        ApplyMode(mode, preserveSymbolicNotation: true);
    }

    partial void OnOwnerRChanged(bool value) => OnBitChanged();
    partial void OnOwnerWChanged(bool value) => OnBitChanged();
    partial void OnOwnerXChanged(bool value) => OnBitChanged();
    partial void OnGroupRChanged(bool value) => OnBitChanged();
    partial void OnGroupWChanged(bool value) => OnBitChanged();
    partial void OnGroupXChanged(bool value) => OnBitChanged();
    partial void OnOthersRChanged(bool value) => OnBitChanged();
    partial void OnOthersWChanged(bool value) => OnBitChanged();
    partial void OnOthersXChanged(bool value) => OnBitChanged();

    partial void OnOctalTextChanged(string value)
    {
        if (!_initialized || _disposed || _syncing || !_service.TryParseOctal(value, out var mode))
        {
            return;
        }

        ClearSymbolicError();
        _lastSymbolicNotation = null;
        ApplyMode(mode);
    }

    public void OnLocaleChanged()
    {
        if (HasSymbolicInputError)
        {
            SymbolicInputErrorText = L("ToolChmodErrorInvalidSymbolic");
        }

        OnPropertyChanged(nameof(HelpText));
        OnPropertyChanged(nameof(OwnerRAutomationName));
        OnPropertyChanged(nameof(OwnerWAutomationName));
        OnPropertyChanged(nameof(OwnerXAutomationName));
        OnPropertyChanged(nameof(GroupRAutomationName));
        OnPropertyChanged(nameof(GroupWAutomationName));
        OnPropertyChanged(nameof(GroupXAutomationName));
        OnPropertyChanged(nameof(OthersRAutomationName));
        OnPropertyChanged(nameof(OthersWAutomationName));
        OnPropertyChanged(nameof(OthersXAutomationName));
    }

    private void OnBitChanged()
    {
        if (!_initialized || _disposed || _syncing)
        {
            return;
        }

        ClearSymbolicError();
        _lastSymbolicNotation = null;
        RecomputeFromBits();
    }

    private void ApplyMode(PosixMode mode, bool preserveSymbolicNotation = false)
    {
        if (!preserveSymbolicNotation)
        {
            _lastSymbolicNotation = null;
        }

        _syncing = true;
        OwnerR = mode.OwnerRead;
        OwnerW = mode.OwnerWrite;
        OwnerX = mode.OwnerExecute;
        GroupR = mode.GroupRead;
        GroupW = mode.GroupWrite;
        GroupX = mode.GroupExecute;
        OthersR = mode.OthersRead;
        OthersW = mode.OthersWrite;
        OthersX = mode.OthersExecute;
        OctalText = mode.ToOctal();
        SymbolicText = mode.ToSymbolic();
        _syncing = false;
        UpdateCommandPreview(mode);
    }

    private void RecomputeFromBits()
    {
        var mode = PosixMode.Empty
            .WithBit(PosixRole.Owner, PosixPermission.Read, OwnerR)
            .WithBit(PosixRole.Owner, PosixPermission.Write, OwnerW)
            .WithBit(PosixRole.Owner, PosixPermission.Execute, OwnerX)
            .WithBit(PosixRole.Group, PosixPermission.Read, GroupR)
            .WithBit(PosixRole.Group, PosixPermission.Write, GroupW)
            .WithBit(PosixRole.Group, PosixPermission.Execute, GroupX)
            .WithBit(PosixRole.Others, PosixPermission.Read, OthersR)
            .WithBit(PosixRole.Others, PosixPermission.Write, OthersW)
            .WithBit(PosixRole.Others, PosixPermission.Execute, OthersX);

        _syncing = true;
        OctalText = mode.ToOctal();
        SymbolicText = mode.ToSymbolic();
        _syncing = false;
        UpdateCommandPreview(mode);
    }

    private void UpdateCommandPreview(PosixMode mode)
    {
        CommandPreviewText = $"chmod {(_lastSymbolicNotation ?? mode.ToOctal())} filename";
    }

    private void ClearSymbolicError()
    {
        HasSymbolicInputError = false;
        SymbolicInputErrorText = string.Empty;
    }

    private void OnLocaleChanged(string _) => OnLocaleChanged();

    private string A11y(string roleKey, string permissionKey) => $"{L(roleKey)} {L(permissionKey)}";
    private string L(string key) => _localizer?[key] ?? key;
}
