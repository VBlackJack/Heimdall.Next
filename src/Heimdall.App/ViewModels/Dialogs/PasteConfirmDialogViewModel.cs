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

namespace Heimdall.App.ViewModels.Dialogs;

public enum PasteRisk
{
    MultiLine,
    Dangerous
}

/// <summary>
/// ViewModel for the terminal paste confirmation dialog.
/// </summary>
public sealed partial class PasteConfirmDialogViewModel : ObservableObject
{
    private const int PreviewMaxLines = 50;
    private const int PreviewMaxChars = 4000;
    private readonly LocalizationManager _localizer;

    public PasteConfirmDialogViewModel(
        PasteRisk risk,
        string previewText,
        LocalizationManager localizer)
    {
        ArgumentNullException.ThrowIfNull(previewText);
        ArgumentNullException.ThrowIfNull(localizer);

        _localizer = localizer;
        Risk = risk;
        LineCount = CountLines(previewText);
        PreviewText = TruncatePreview(previewText, PreviewMaxLines, PreviewMaxChars);
        IsTruncated = PreviewText.Length < previewText.Length
            || CountLines(PreviewText) < LineCount;
    }

    public PasteRisk Risk { get; }

    public int LineCount { get; }

    public string PreviewText { get; }

    public bool IsTruncated { get; }

    public bool IsDangerous => Risk == PasteRisk.Dangerous;

    public bool IsMultiLine => Risk == PasteRisk.MultiLine;

    public string Title => _localizer[
        IsDangerous ? "PasteWarningTitle" : "PasteMultiLineTitle"];

    public string Message => IsDangerous
        ? _localizer["PasteWarningMessage"]
        : string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            _localizer["PasteMultiLineMessage"],
            LineCount);

    public string CancelButtonText => _localizer["BtnCancel"];

    public string PasteButtonText => _localizer["BtnPaste"];

    public string TruncationNotice => _localizer["PasteConfirmTruncationNotice"];

    public bool? Decision { get; private set; }

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Cancel()
    {
        Decision = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Paste()
    {
        Decision = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static int CountLines(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;
    }

    private static string TruncatePreview(string text, int maxLines, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var lines = text.Split('\n');
        var capped = lines.Length > maxLines
            ? string.Join("\n", lines.Take(maxLines))
            : text;

        if (capped.Length > maxChars)
        {
            capped = capped[..maxChars];
        }

        return capped;
    }
}
