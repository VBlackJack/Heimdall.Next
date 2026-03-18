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
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Heimdall.App.Views;

/// <summary>
/// Embedded text editor with syntax highlighting powered by AvalonEdit.
/// Used for editing local and remote (SFTP) files with Dracula theme.
/// </summary>
public partial class EmbeddedEditorView : UserControl
{
    private string? _filePath;
    private bool _isModified;
    private bool _isRemote;
    private Core.Localization.LocalizationManager? _localizer;

    /// <summary>Raised when the user saves the file.</summary>
    public event Action<string, string>? FileSaved;

    /// <summary>Raised when the user closes the editor.</summary>
    public event Action? CloseRequested;

    public EmbeddedEditorView(Core.Localization.LocalizationManager? localizer = null)
    {
        _localizer = localizer;
        InitializeComponent();
        ApplyDraculaTheme();

        // Localize button labels
        BtnSave.Content = L("EditorBtnSave");
        BtnClose.Content = L("EditorBtnClose");

        Editor.TextChanged += (_, _) =>
        {
            if (!_isModified)
            {
                _isModified = true;
                UpdateTitle();
            }
        };

        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCursorPosition();
    }

    /// <summary>
    /// Opens a file for editing with automatic syntax detection.
    /// </summary>
    public void OpenFile(string filePath)
    {
        _filePath = filePath;
        _isModified = false;
        _isRemote = false;

        try
        {
            Editor.Text = File.ReadAllText(filePath);
            _isModified = false;

            // Auto-detect syntax highlighting from file extension
            var highlighting = ResolveSyntax(Path.GetExtension(filePath));
            Editor.SyntaxHighlighting = highlighting;

            SyntaxLabel.Text = highlighting?.Name ?? "Plain Text";
            UpdateTitle();
            UpdateCursorPosition();
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"EmbeddedEditor failed to open: {ex.Message}");
            Editor.Text = $"Error loading file: {ex.Message}";
        }
    }

    /// <summary>
    /// Opens the editor with provided content (for remote files).
    /// </summary>
    public void OpenContent(string fileName, string content, string? syntaxName = null)
    {
        _filePath = fileName;
        _isModified = false;
        _isRemote = true;

        Editor.Text = content;
        _isModified = false;

        if (!string.IsNullOrEmpty(syntaxName))
        {
            Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(syntaxName);
        }
        else
        {
            Editor.SyntaxHighlighting = ResolveSyntax(Path.GetExtension(fileName));
        }

        SyntaxLabel.Text = Editor.SyntaxHighlighting?.Name ?? "Plain Text";
        UpdateTitle();
        UpdateCursorPosition();
    }

    /// <summary>Returns the current editor content.</summary>
    public string GetContent() => Editor.Text;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            return;
        }

        try
        {
            // For local files, save directly to disk.
            // Remote files (opened via OpenContent) are handled by the
            // FileSaved event subscriber which uploads via SFTP.
            if (!_isRemote)
            {
                File.WriteAllText(_filePath, Editor.Text);
            }

            _isModified = false;
            UpdateTitle();
            FileSaved?.Invoke(_filePath, Editor.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
                string.Format(L("EditorSaveErrorMessage"), ex.Message),
                L("EditorSaveErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (_isModified)
        {
            var result = MessageBox.Show(Window.GetWindow(this),
                L("EditorUnsavedMessage"),
                L("EditorUnsavedTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        CloseRequested?.Invoke();
    }

    private void UpdateTitle()
    {
        string name = Path.GetFileName(_filePath) ?? "Untitled";
        FileNameText.Text = _isModified ? $"{name} *" : name;
    }

    private void UpdateCursorPosition()
    {
        var caret = Editor.TextArea.Caret;
        CursorPositionText.Text = $"Ln {caret.Line}, Col {caret.Column}";
    }

    /// <summary>Resolves a locale key, falling back to the key name if no localizer is set.</summary>
    private string L(string key) => _localizer?[key] ?? key;

    private static ICSharpCode.AvalonEdit.Highlighting.IHighlightingDefinition? ResolveSyntax(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return null;

        // Try AvalonEdit built-in first
        var hl = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        if (hl is not null) return hl;

        // Fallback mapping for extensions AvalonEdit doesn't know
        return ext.ToLowerInvariant() switch
        {
            ".yml" or ".yaml" or ".toml" => HighlightingManager.Instance.GetDefinition("MarkDown"),
            ".conf" or ".cfg" or ".ini" or ".env" or ".properties" or ".service"
                => HighlightingManager.Instance.GetDefinition("MarkDown"),
            ".ps1" or ".psm1" or ".psd1" => HighlightingManager.Instance.GetDefinition("PowerShell"),
            ".sh" or ".bash" or ".bashrc" or ".zshrc" or ".profile"
                => HighlightingManager.Instance.GetDefinition("Boo"),
            ".md" or ".markdown" => HighlightingManager.Instance.GetDefinition("MarkDown"),
            ".json" or ".jsonc" => HighlightingManager.Instance.GetDefinition("JavaScript"),
            ".ts" or ".tsx" or ".jsx" => HighlightingManager.Instance.GetDefinition("JavaScript"),
            ".scss" or ".less" => HighlightingManager.Instance.GetDefinition("CSS"),
            ".py" or ".pyw" => HighlightingManager.Instance.GetDefinition("Python"),
            ".rb" => HighlightingManager.Instance.GetDefinition("Ruby"),
            ".log" or ".txt" or ".csv" => null,
            _ => null
        };
    }

    private void ApplyDraculaTheme()
    {
        // Dracula editor colors
        Editor.Background = new SolidColorBrush(ColorFromHex("#282A36"));
        Editor.Foreground = new SolidColorBrush(ColorFromHex("#F8F8F2"));
        Editor.LineNumbersForeground = new SolidColorBrush(ColorFromHex("#6272A4"));

        Editor.TextArea.SelectionBrush = new SolidColorBrush(ColorFromHex("#44475A"));
        Editor.TextArea.SelectionForeground = null;

        var currentLineBrush = new SolidColorBrush(ColorFromHex("#44475A"));
        currentLineBrush.Opacity = 0.3;
        Editor.TextArea.TextView.CurrentLineBackground = currentLineBrush;
        Editor.TextArea.TextView.CurrentLineBorder = new System.Windows.Media.Pen(
            new SolidColorBrush(ColorFromHex("#44475A")), 1);

        // Override AvalonEdit default syntax colors with Dracula palette
        // Default colors are designed for white background — too dark on #282A36
        ApplyDraculaSyntaxColors();
    }

    private void ApplyDraculaSyntaxColors()
    {
        var rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // AvalonEdit color name → Dracula hex
            { "Comment", "#6272A4" },
            { "String", "#F1FA8C" },
            { "Char", "#F1FA8C" },
            { "Preprocessor", "#FF79C6" },
            { "Punctuation", "#F8F8F2" },
            { "MethodCall", "#50FA7B" },
            { "NumberLiteral", "#BD93F9" },
            { "Digits", "#BD93F9" },
            { "Keywords", "#FF79C6" },
            { "GotoKeywords", "#FF79C6" },
            { "AccessKeywords", "#FF79C6" },
            { "ValueTypeKeywords", "#8BE9FD" },
            { "ReferenceTypeKeywords", "#8BE9FD" },
            { "ThisOrBaseReference", "#BD93F9" },
            { "NullOrValueKeywords", "#BD93F9" },
            { "ParameterModifiers", "#FF79C6" },
            { "Modifiers", "#FF79C6" },
            { "Visibility", "#FF79C6" },
            { "NamespaceKeywords", "#FF79C6" },
            { "GetSetAddRemove", "#50FA7B" },
            { "TrueFalse", "#BD93F9" },
            { "TypeKeywords", "#8BE9FD" },
            { "SemanticKeywords", "#FF79C6" },
            // XML/HTML
            { "XmlTag", "#FF79C6" },
            { "XmlComment", "#6272A4" },
            { "DocComment", "#6272A4" },
            { "XmlString", "#F1FA8C" },
            { "Assignment", "#FF79C6" },
            { "Entities", "#BD93F9" },
            // PowerShell/Bash
            { "Variable", "#F8F8F2" },
            { "Command", "#50FA7B" },
            { "Operator", "#FF79C6" },
        };

        if (Editor.SyntaxHighlighting is null)
        {
            return;
        }

        foreach (var color in Editor.SyntaxHighlighting.NamedHighlightingColors)
        {
            if (rules.TryGetValue(color.Name, out var hex))
            {
                color.Foreground = new ICSharpCode.AvalonEdit.Highlighting.SimpleHighlightingBrush(
                    ColorFromHex(hex));
            }
        }

        // Also apply to nested rule sets
        ApplyColorsToRuleSet(Editor.SyntaxHighlighting.MainRuleSet, rules);
    }

    private static void ApplyColorsToRuleSet(
        ICSharpCode.AvalonEdit.Highlighting.HighlightingRuleSet? ruleSet,
        Dictionary<string, string> rules)
    {
        if (ruleSet is null) return;

        foreach (var rule in ruleSet.Rules)
        {
            if (rule.Color?.Name is not null && rules.TryGetValue(rule.Color.Name, out var hex))
            {
                rule.Color.Foreground = new ICSharpCode.AvalonEdit.Highlighting.SimpleHighlightingBrush(
                    ColorFromHex(hex));
            }
        }

        foreach (var span in ruleSet.Spans)
        {
            if (span.SpanColor?.Name is not null && rules.TryGetValue(span.SpanColor.Name, out var hex))
            {
                span.SpanColor.Foreground = new ICSharpCode.AvalonEdit.Highlighting.SimpleHighlightingBrush(
                    ColorFromHex(hex));
            }

            ApplyColorsToRuleSet(span.RuleSet, rules);
        }
    }

    private static System.Windows.Media.Color ColorFromHex(string hex)
    {
        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
    }
}
