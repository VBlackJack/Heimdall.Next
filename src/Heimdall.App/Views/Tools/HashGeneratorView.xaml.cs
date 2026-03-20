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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Hash generator tool that computes cryptographic hashes of arbitrary text input.
/// Supports MD5, SHA1, SHA256, SHA384, and SHA512.
/// </summary>
public partial class HashGeneratorView : UserControl, IDisposable
{
    private LocalizationManager? _localizer;
    private DispatcherTimer? _debounceTimer;

    private static readonly string[] Algorithms = ["MD5", "SHA1", "SHA256", "SHA384", "SHA512"];

    public HashGeneratorView()
    {
        InitializeComponent();
        InitializeAlgorithms();
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
    }

    private void InitializeAlgorithms()
    {
        foreach (var algo in Algorithms)
        {
            CmbAlgorithm.Items.Add(algo);
        }

        CmbAlgorithm.SelectedIndex = 2; // SHA256
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
            ComputeHash();
        };
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolHashGeneratorTitle");
        LblAlgorithm.Text = L("ToolHashAlgorithmLabel");
        LblInput.Text = L("ToolHashInputLabel");
        LblOutput.Text = L("ToolHashOutputLabel");
        BtnCopy.Content = L("ToolHashBtnCopy");

        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolHashBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(CmbAlgorithm, L("ToolHashAlgorithmLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtInput, L("ToolHashInputLabel"));
        System.Windows.Automation.AutomationProperties.SetName(TxtOutput, L("ToolHashOutputLabel"));

        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnAlgorithmChanged(object sender, SelectionChangedEventArgs e)
    {
        ComputeHash();
    }

    private void ComputeHash()
    {
        var input = TxtInput.Text;
        if (string.IsNullOrEmpty(input))
        {
            TxtOutput.Text = string.Empty;
            TxtByteLength.Text = string.Empty;
            return;
        }

        var algorithmName = CmbAlgorithm.SelectedItem as string ?? "SHA256";

        try
        {
            using var algorithm = CreateHashAlgorithm(algorithmName);
            if (algorithm is null)
            {
                TxtOutput.Text = L("ToolHashErrorUnsupported");
                TxtByteLength.Text = string.Empty;
                return;
            }

            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = algorithm.ComputeHash(inputBytes);
            var hex = Convert.ToHexStringLower(hashBytes);

            TxtOutput.Text = hex;
            TxtByteLength.Text = string.Format(
                L("ToolHashByteLengthFormat"),
                hashBytes.Length,
                hashBytes.Length * 8);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"HashGenerator computation failed: {ex.Message}");
            TxtOutput.Text = string.Empty;
            TxtByteLength.Text = string.Empty;
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var hash = TxtOutput.Text;
        if (!string.IsNullOrEmpty(hash))
        {
            try
            {
                Clipboard.SetText(hash);
                CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
            }
            catch (Exception ex)
            {
                Core.Logging.FileLogger.Warn($"HashGenerator clipboard copy failed: {ex.Message}");
            }
        }
    }

    private static HashAlgorithm? CreateHashAlgorithm(string name) => name switch
    {
        "MD5" => MD5.Create(),
        "SHA1" => SHA1.Create(),
        "SHA256" => SHA256.Create(),
        "SHA384" => SHA384.Create(),
        "SHA512" => SHA512.Create(),
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
