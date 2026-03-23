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
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Heimdall.Core.Utilities;
using Microsoft.Win32;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Hash generator tool that computes all cryptographic hashes simultaneously.
/// Supports MD5, SHA1, SHA256, SHA384, and SHA512 with verify mode.
/// </summary>
public partial class HashGeneratorView : UserControl, IToolView
{
    private LocalizationManager? _localizer;
    private DispatcherTimer? _debounceTimer;
    private bool _fileMode;

    private static readonly string[] Algorithms = ["MD5", "SHA1", "SHA256", "SHA384", "SHA512", "SHA3-256"];

    /// <summary>
    /// Maximum file size allowed for hashing (50 MB).
    /// </summary>
    private const long MaxFileSizeBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Maps hash hex length to algorithm name for verify auto-detection.
    /// </summary>
    private static readonly Dictionary<int, string> LengthToAlgorithm = new()
    {
        [32] = "MD5",
        [40] = "SHA1",
        [64] = "SHA256",  // Also SHA3-256; prefer SHA256 for auto-detect
        [96] = "SHA384",
        [128] = "SHA512",
    };

    /// <summary>
    /// Whether SHA3-256 is supported on this platform.
    /// </summary>
    private static readonly bool Sha3Supported = SHA3_256.IsSupported;

    private readonly Dictionary<string, System.Windows.Controls.TextBox> _hashOutputBoxes = new();
    private readonly Dictionary<string, Button> _hashCopyButtons = new();
    private readonly Dictionary<string, DockPanel> _hashRows = new();
    private readonly Dictionary<string, string> _currentHashes = new();

    public HashGeneratorView()
    {
        InitializeComponent();
        BuildHashResultRows();
        InitializeDebounceTimer();
    }

    /// <summary>
    /// Initializes the tool with optional context and localizer.
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

    private void BuildHashResultRows()
    {
        foreach (var algo in Algorithms)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };

            var label = new TextBlock
            {
                Text = $"{algo}:",
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Width = 72,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            };
            label.SetValue(DockPanel.DockProperty, Dock.Left);

            var copyBtn = new Button
            {
                Content = "",
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("SecondaryButtonStyle"),
            };
            copyBtn.SetValue(DockPanel.DockProperty, Dock.Right);
            copyBtn.Tag = algo;
            copyBtn.Click += OnCopyHashClick;

            var hashBox = new System.Windows.Controls.TextBox
            {
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Padding = new Thickness(6, 4, 6, 4),
                IsReadOnly = true,
                Background = Brushes.Transparent,
                Foreground = (Brush)FindResource("AccentBrush"),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            row.Children.Add(label);
            row.Children.Add(copyBtn);
            row.Children.Add(hashBox);

            HashResultsPanel.Children.Add(row);

            _hashOutputBoxes[algo] = hashBox;
            _hashCopyButtons[algo] = copyBtn;
            _hashRows[algo] = row;
        }
    }

    private void InitializeDebounceTimer()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            ComputeAllHashes();
        };
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolHashGeneratorTitle");
        LblInput.Text = L("ToolHashInputLabel");
        LblResults.Text = L("ToolHashResultsLabel");
        LblVerify.Text = L("ToolHashVerifyLabel");

        TxtFileDropZone.Text = L("ToolHashFileDropZone");
        BtnBrowseFile.Content = L("ToolHashBtnBrowseFile");
        BtnClearFile.Content = L("ToolHashBtnClearFile");

        System.Windows.Automation.AutomationProperties.SetName(TxtInput, L("ToolHashInputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnClearFile, L("ToolHashBtnClearFile"));
        System.Windows.Automation.AutomationProperties.SetName(TxtVerify, L("ToolHashVerifyLabel"));
        System.Windows.Automation.AutomationProperties.SetName(BtnBrowseFile, L("ToolHashBtnBrowseFile"));

        foreach (var algo in Algorithms)
        {
            if (_hashCopyButtons.TryGetValue(algo, out var btn))
            {
                btn.Content = L("ToolHashBtnCopy");
                btn.ToolTip = L("ToolBtnCopyToClipboard");
                System.Windows.Automation.AutomationProperties.SetName(btn, $"{L("ToolHashBtnCopy")} {algo}");
            }

            if (_hashOutputBoxes.TryGetValue(algo, out var box))
            {
                System.Windows.Automation.AutomationProperties.SetName(box, $"{algo} hash");
            }
        }

        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));

        TxtInput.Tag = L("ToolWatermarkTextToHash");
        TxtVerify.Tag = L("ToolWatermarkPasteHashVerify");
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var helpText = L("ToolHelpHASH");
        MessageBox.Show(helpText, L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_fileMode) return;
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void ComputeAllHashes()
    {
        var input = TxtInput.Text;
        _currentHashes.Clear();

        if (string.IsNullOrEmpty(input))
        {
            foreach (var algo in Algorithms)
            {
                if (_hashOutputBoxes.TryGetValue(algo, out var box))
                {
                    box.Text = string.Empty;
                }
            }
            TxtByteLength.Text = string.Empty;
            UpdateVerifyResult();
            return;
        }

        var inputBytes = Encoding.UTF8.GetBytes(input);

        foreach (var algo in Algorithms)
        {
            try
            {
                using var algorithm = CreateHashAlgorithm(algo);
                if (algorithm is null) continue;

                var hashBytes = algorithm.ComputeHash(inputBytes);
                var hex = Convert.ToHexStringLower(hashBytes);

                _currentHashes[algo] = hex;

                if (_hashOutputBoxes.TryGetValue(algo, out var box))
                {
                    box.Text = hex;
                }
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"HashGenerator {algo} computation failed: {ex.Message}");
                if (_hashOutputBoxes.TryGetValue(algo, out var box))
                {
                    box.Text = string.Empty;
                }
            }
        }

        TxtByteLength.Text = string.Format(
            L("ToolHashByteLengthFormat"),
            inputBytes.Length,
            inputBytes.Length * 8);

        UpdateVerifyResult();
    }

    private void OnVerifyTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateVerifyResult();
    }

    private void UpdateVerifyResult()
    {
        var expected = TxtVerify.Text.Trim().ToLowerInvariant();

        // Reset all row highlights
        var accentBrush = (Brush)FindResource("AccentBrush");
        foreach (var algo in Algorithms)
        {
            if (_hashOutputBoxes.TryGetValue(algo, out var box))
            {
                box.Foreground = accentBrush;
            }
        }

        if (string.IsNullOrEmpty(expected) || _currentHashes.Count == 0)
        {
            TxtVerifyResult.Text = string.Empty;
            return;
        }

        // Auto-detect algorithm by length and check match
        if (LengthToAlgorithm.TryGetValue(expected.Length, out var detectedAlgo))
        {
            if (_currentHashes.TryGetValue(detectedAlgo, out var computedHash) &&
                string.Equals(computedHash, expected, StringComparison.OrdinalIgnoreCase))
            {
                TxtVerifyResult.Text = string.Format(L("ToolHashVerifyMatch"), detectedAlgo);
                TxtVerifyResult.Foreground = (Brush)FindResource("SuccessBrush");

                if (_hashOutputBoxes.TryGetValue(detectedAlgo, out var matchBox))
                {
                    matchBox.Foreground = (Brush)FindResource("SuccessBrush");
                }
                return;
            }
        }

        // Fallback: check all algorithms regardless of length
        foreach (var algo in Algorithms)
        {
            if (_currentHashes.TryGetValue(algo, out var hash) &&
                string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
            {
                TxtVerifyResult.Text = string.Format(L("ToolHashVerifyMatch"), algo);
                TxtVerifyResult.Foreground = (Brush)FindResource("SuccessBrush");

                if (_hashOutputBoxes.TryGetValue(algo, out var matchBox))
                {
                    matchBox.Foreground = (Brush)FindResource("SuccessBrush");
                }
                return;
            }
        }

        TxtVerifyResult.Text = L("ToolHashVerifyNoMatch");
        TxtVerifyResult.Foreground = (Brush)FindResource("ErrorBrush");
    }

    private void OnCopyHashClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string algo) return;

        if (_currentHashes.TryGetValue(algo, out var hash) && !string.IsNullOrEmpty(hash))
        {
            try
            {
                Clipboard.SetText(hash);
                CopyFeedbackHelper.ShowCopyFeedback(btn);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"HashGenerator clipboard copy failed: {ex.Message}");
            }
        }
    }

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files?.Length > 0)
            {
                HashFileAsync(files[0]);
            }
        }
    }

    private void OnBrowseFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            HashFileAsync(dialog.FileName);
        }
    }

    private async void HashFileAsync(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) return;

            if (fileInfo.Length > MaxFileSizeBytes)
            {
                TxtFileStatus.Text = L("ToolHashFileTooLarge");
                TxtFileStatus.Foreground = (Brush)FindResource("ErrorBrush");
                return;
            }

            _fileMode = true;
            TxtInput.IsEnabled = false;
            TxtInput.Text = string.Empty;
            BtnClearFile.Visibility = Visibility.Visible;
            FileHashProgress.Visibility = Visibility.Visible;

            TxtFileStatus.Text = L("ToolHashFileHashing");
            TxtFileStatus.Foreground = (Brush)FindResource("TextSecondaryBrush");
            _currentHashes.Clear();

            var fileBytes = await Task.Run(() => File.ReadAllBytes(filePath));

            foreach (var algo in Algorithms)
            {
                try
                {
                    using var algorithm = CreateHashAlgorithm(algo);
                    if (algorithm is null) continue;

                    var hashBytes = await Task.Run(() => algorithm.ComputeHash(fileBytes));
                    var hex = Convert.ToHexStringLower(hashBytes);

                    _currentHashes[algo] = hex;

                    if (_hashOutputBoxes.TryGetValue(algo, out var box))
                    {
                        box.Text = hex;
                    }
                }
                catch (Exception ex)
                {
                    Core.Logging.FileLogger.Warn($"HashGenerator file {algo} computation failed: {ex.Message}");
                    if (_hashOutputBoxes.TryGetValue(algo, out var box))
                    {
                        box.Text = string.Empty;
                    }
                }
            }

            FileHashProgress.Visibility = Visibility.Collapsed;

            TxtFileStatus.Text = string.Format(
                L("ToolHashFileResult"),
                fileInfo.Name,
                FileSize.Format(fileInfo.Length));
            TxtFileStatus.Foreground = (Brush)FindResource("TextSecondaryBrush");

            TxtByteLength.Text = string.Format(
                L("ToolHashByteLengthFormat"),
                fileInfo.Length,
                fileInfo.Length * 8);

            UpdateVerifyResult();
        }
        catch (Exception ex)
        {
            FileHashProgress.Visibility = Visibility.Collapsed;
            Core.Logging.FileLogger.Warn($"HashGenerator file hash failed: {ex.Message}");
            TxtFileStatus.Text = string.Format(L("ToolHashFileError"), ex.Message);
            TxtFileStatus.Foreground = (Brush)FindResource("ErrorBrush");
        }
    }

    private void OnClearFileClick(object sender, RoutedEventArgs e)
    {
        _fileMode = false;
        TxtInput.IsEnabled = true;
        BtnClearFile.Visibility = Visibility.Collapsed;
        FileHashProgress.Visibility = Visibility.Collapsed;
        TxtFileStatus.Text = string.Empty;
        _currentHashes.Clear();

        foreach (var algo in Algorithms)
        {
            if (_hashOutputBoxes.TryGetValue(algo, out var box))
            {
                box.Text = string.Empty;
            }
        }

        TxtByteLength.Text = string.Empty;
        UpdateVerifyResult();
    }

    private void OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            DropZoneBorder.BorderBrush = (Brush)FindResource("AccentBrush");
        }
    }

    private void OnDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        DropZoneBorder.BorderBrush = (Brush)FindResource("BorderBrush");
    }

    private static HashAlgorithm? CreateHashAlgorithm(string name) => name switch
    {
        "MD5" => MD5.Create(),
        "SHA1" => SHA1.Create(),
        "SHA256" => SHA256.Create(),
        "SHA384" => SHA384.Create(),
        "SHA512" => SHA512.Create(),
        "SHA3-256" when Sha3Supported => SHA3_256.Create(),
        "SHA3-256" => null,
        _ => null
    };

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
