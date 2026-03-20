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
using System.Windows.Input;
using System.Windows.Media;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Password generator with Random, Syllable, and Passphrase modes, plus a 5-level strength indicator.
/// </summary>
public partial class PasswordGeneratorView : UserControl, IDisposable
{
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitChars = "0123456789";
    private const string SymbolChars = "!@#$%^&*()-_=+[]{}|;:',.<>?/~`";
    private const string AmbiguousChars = "0Oo1lI|";
    private const string ShellDangerousChars = "$^&*'\"\\|`(){}[]<>!~;";
    private const int PhoneticMaxLength = 32;

    // NATO phonetic alphabet — internationally standardized (ICAO), not translated.
    private static readonly Dictionary<char, string> NatoAlphabet = new()
    {
        ['A'] = "Alpha", ['B'] = "Bravo", ['C'] = "Charlie", ['D'] = "Delta",
        ['E'] = "Echo", ['F'] = "Foxtrot", ['G'] = "Golf", ['H'] = "Hotel",
        ['I'] = "India", ['J'] = "Juliet", ['K'] = "Kilo", ['L'] = "Lima",
        ['M'] = "Mike", ['N'] = "November", ['O'] = "Oscar", ['P'] = "Papa",
        ['Q'] = "Quebec", ['R'] = "Romeo", ['S'] = "Sierra", ['T'] = "Tango",
        ['U'] = "Uniform", ['V'] = "Victor", ['W'] = "Whiskey", ['X'] = "X-ray",
        ['Y'] = "Yankee", ['Z'] = "Zulu",
        ['0'] = "Zero", ['1'] = "One", ['2'] = "Two", ['3'] = "Three",
        ['4'] = "Four", ['5'] = "Five", ['6'] = "Six", ['7'] = "Seven",
        ['8'] = "Eight", ['9'] = "Nine"
    };

    // Special character technical names — universal terminology, not translated.
    private static readonly Dictionary<char, string> SpecialCharNames = new()
    {
        ['!'] = "Exclamation", ['@'] = "At", ['#'] = "Hash", ['$'] = "Dollar",
        ['%'] = "Percent", ['^'] = "Caret", ['&'] = "Ampersand", ['*'] = "Asterisk",
        ['('] = "OpenParen", [')'] = "CloseParen", ['-'] = "Dash", ['_'] = "Underscore",
        ['='] = "Equals", ['+'] = "Plus", ['['] = "OpenBracket", [']'] = "CloseBracket",
        ['{'] = "OpenBrace", ['}'] = "CloseBrace", ['\\'] = "Backslash", ['/'] = "Slash",
        [';'] = "Semicolon", [':'] = "Colon", ['\''] = "Apostrophe", ['"'] = "Quote",
        [','] = "Comma", ['.'] = "Period", ['<'] = "LessThan", ['>'] = "GreaterThan",
        ['?'] = "Question", ['~'] = "Tilde", ['`'] = "Backtick", ['|'] = "Pipe",
        [' '] = "Space"
    };

    // QWERTY -> AZERTY key mapping (keys that differ)
    private static readonly Dictionary<char, char> QwertyToAzerty = new()
    {
        ['q'] = 'a', ['w'] = 'z', ['a'] = 'q', ['z'] = 'w', ['m'] = ',',
        ['Q'] = 'A', ['W'] = 'Z', ['A'] = 'Q', ['Z'] = 'W', ['M'] = '?',
        [';'] = 'm', ['\''] = '\u00f9', ['['] = '^', [']'] = '$', ['\\'] = '*',
        // Number row: QWERTY unshifted -> AZERTY unshifted
        ['1'] = '&', ['2'] = '\u00e9', ['3'] = '"', ['4'] = '\'', ['5'] = '(',
        ['6'] = '-', ['7'] = '\u00e8', ['8'] = '_', ['9'] = '\u00e7', ['0'] = '\u00e0'
    };

    // AZERTY -> QWERTY key mapping (reverse)
    private static readonly Dictionary<char, char> AzertyToQwerty = new()
    {
        ['a'] = 'q', ['z'] = 'w', ['q'] = 'a', ['w'] = 'z', [','] = 'm',
        ['A'] = 'Q', ['Z'] = 'W', ['Q'] = 'A', ['W'] = 'Z', ['?'] = 'M',
        ['m'] = ';', ['\u00f9'] = '\'', ['^'] = '[', ['$'] = ']', ['*'] = '\\',
        // Number row: AZERTY unshifted -> QWERTY unshifted
        ['&'] = '1', ['\u00e9'] = '2', ['"'] = '3', ['\''] = '4', ['('] = '5',
        ['-'] = '6', ['\u00e8'] = '7', ['_'] = '8', ['\u00e7'] = '9', ['\u00e0'] = '0'
    };

    private static readonly string[] Consonants =
        ["b","c","d","f","g","h","j","k","l","m","n","p","r","s","t","v","w","x","z"];
    private static readonly string[] Vowels = ["a","e","i","o","u","y"];

    private static readonly string[] FallbackEnglishWords =
    [
        "anchor","apple","arrow","badge","beach","bridge","cabin","candle","castle","cherry",
        "circle","cloud","coffee","copper","coral","crane","crystal","delta","desert","dolphin",
        "dragon","eagle","ember","falcon","flame","forest","garden","glacier","golden","hammer",
        "harbor","helmet","honey","hunter","island","jacket","jewel","jungle","ladder","lantern",
        "marble","meadow","mirror","monkey","mountain","nature","noble","ocean","oracle","palace"
    ];
    private static readonly string[] FallbackFrenchWords =
    [
        "abricot","amande","ancre","aurore","balcon","barque","bonnet","bougie","branche","cabane",
        "canard","cerise","chalet","chemin","cheval","citron","coffre","comete","cristal","dauphin",
        "desert","dragon","enigme","etoile","faucon","flamme","fleuve","fortin","galion","glacier",
        "harpon","jardin","jasmin","jungle","lanterne","marbre","miroir","montagne","moulin","nature",
        "oiseau","olive","orange","palmier","pensee","portail","radeau","renard","soleil","volcan"
    ];

    private LocalizationManager? _localizer;
    private bool _initialized;
    private bool _suspendGeneration;
    private string[] _englishWords = [];
    private string[] _frenchWords = [];

    private enum GeneratorMode { Random, Syllable, Passphrase }

    private enum SyllableCase { Mixed, Lower, Upper, Title, Alternating, WordCase, Inverse }

    private enum Placement { Random, Start, End, Middle }

    public PasswordGeneratorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the view with optional context and localizer.
    /// </summary>
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        LoadWordLists();
        PopulateComboBoxes();
        ApplyLocalization();

        if (context?.Argument is { } arg && int.TryParse(arg, out var length))
        {
            length = Math.Clamp(length, 4, 128);
            LengthSlider.Value = length;
        }

        _initialized = true;
        GeneratePassword();
    }

    private void LoadWordLists()
    {
        _englishWords = LoadWordListFile("wordlist_en.txt", FallbackEnglishWords);
        _frenchWords = LoadWordListFile("wordlist_fr.txt", FallbackFrenchWords);
    }

    private static string[] LoadWordListFile(string fileName, string[] fallback)
    {
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(appDir, "Assets", fileName);
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path, Encoding.UTF8)
                    .Select(l => l.Trim().ToLowerInvariant())
                    .Where(l => l.Length >= 3 && l.Length <= 12)
                    .Distinct()
                    .ToArray();
                if (lines.Length >= 50) return lines;
            }
        }
        catch
        {
            Heimdall.Core.Logging.FileLogger.Warn($"[PasswordGenerator] Failed to load word list: {fileName}");
        }
        return fallback;
    }

    private void PopulateComboBoxes()
    {
        CmbSylCase.Items.Clear();
        CmbSylCase.Items.Add(L("ToolPwdGenCaseMixed"));
        CmbSylCase.Items.Add(L("ToolPwdGenCaseLower"));
        CmbSylCase.Items.Add(L("ToolPwdGenCaseUpper"));
        CmbSylCase.Items.Add(L("ToolPwdGenCaseTitle"));
        CmbSylCase.Items.Add(L("ToolPwdGenCaseAlternating"));
        CmbSylCase.Items.Add(L("ToolPwdGenCaseWordCase"));
        CmbSylCase.Items.Add(L("ToolPwdGenCaseInverse"));
        CmbSylCase.SelectedIndex = 0;

        CmbSylPlacement.Items.Clear();
        CmbSylPlacement.Items.Add(L("ToolPwdGenPlacementRandom"));
        CmbSylPlacement.Items.Add(L("ToolPwdGenPlacementStart"));
        CmbSylPlacement.Items.Add(L("ToolPwdGenPlacementEnd"));
        CmbSylPlacement.Items.Add(L("ToolPwdGenPlacementMiddle"));
        CmbSylPlacement.SelectedIndex = 0;

        TxtCustomSpecials.Text = SymbolChars;

        CmbPpLanguage.Items.Clear();
        CmbPpLanguage.Items.Add(L("ToolPwdGenLangEnglish"));
        CmbPpLanguage.Items.Add(L("ToolPwdGenLangFrench"));
        CmbPpLanguage.SelectedIndex = 0;

        CmbKeyboardLayout.Items.Clear();
        CmbKeyboardLayout.Items.Add(L("ToolPwdGenLayoutQwerty"));
        CmbKeyboardLayout.Items.Add(L("ToolPwdGenLayoutAzerty"));
        CmbKeyboardLayout.SelectedIndex = 0;
    }

    private void ApplyLocalization()
    {
        TitleText.Text = L("ToolPwdGenTitle");
        BtnGenerate.Content = L("ToolPwdGenBtnGenerate");
        BtnCopy.Content = L("ToolPwdGenBtnCopy");
        BtnCopy.ToolTip = L("ToolBtnCopyToClipboard");

        // Mode radio buttons
        RbModeRandom.Content = L("ToolPwdGenModeRandom");
        RbModeSyllable.Content = L("ToolPwdGenModeSyllable");
        RbModePassphrase.Content = L("ToolPwdGenModePassphrase");

        // Random mode
        LengthLabel.Text = L("ToolPwdGenLength");
        ChkUppercase.Content = L("ToolPwdGenUppercase");
        ChkLowercase.Content = L("ToolPwdGenLowercase");
        ChkDigits.Content = L("ToolPwdGenDigits");
        ChkSymbols.Content = L("ToolPwdGenSymbols");
        ChkExcludeAmbiguous.Content = L("ToolPwdGenExcludeAmbiguous");
        ChkCliSafe.Content = L("ToolPwdGenCliSafe");
        CustomSpecialsLabel.Text = L("ToolPwdGenCustomSpecials");

        // Presets
        PresetsLabel.Text = L("ToolPwdGenPresets");
        BtnPresetPin4.Content = L("ToolPwdGenPresetPin4");
        BtnPresetPin6.Content = L("ToolPwdGenPresetPin6");
        BtnPresetWifi.Content = L("ToolPwdGenPresetWifi");
        BtnPresetApiKey.Content = L("ToolPwdGenPresetApiKey");
        BtnPresetMysql.Content = L("ToolPwdGenPresetMysql");
        BtnPresetPassphrase4.Content = L("ToolPwdGenPresetPassphrase4");

        // Phonetic + keyboard layout
        PhoneticLabel.Text = L("ToolPwdGenPhonetic");
        KeyboardLayoutLabel.Text = L("ToolPwdGenKeyboardLayout");

        // Syllable mode
        SylLengthLabel.Text = L("ToolPwdGenBaseLength");
        SylCaseLabel.Text = L("ToolPwdGenCase");
        SylDigitsLabel.Text = L("ToolPwdGenDigits");
        SylSpecialsLabel.Text = L("ToolPwdGenSymbols");
        SylPlacementLabel.Text = L("ToolPwdGenPlacement");

        // Passphrase mode
        PpWordCountLabel.Text = L("ToolPwdGenWordCount");
        PpSeparatorLabel.Text = L("ToolPwdGenSeparator");
        PpLanguageLabel.Text = L("ToolPwdGenLanguage");
        ChkPpCapitalize.Content = L("ToolPwdGenCapitalize");
        ChkPpDigit.Content = L("ToolPwdGenAddDigit");
        ChkPpSpecial.Content = L("ToolPwdGenAddSpecial");

        // Accessibility
        System.Windows.Automation.AutomationProperties.SetName(BtnGenerate, L("ToolPwdGenBtnGenerate"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolPwdGenBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(PasswordOutput, L("ToolPwdGenTitle"));
        System.Windows.Automation.AutomationProperties.SetName(LengthSlider, L("ToolPwdGenLength"));
        System.Windows.Automation.AutomationProperties.SetName(ChkUppercase, L("ToolPwdGenUppercase"));
        System.Windows.Automation.AutomationProperties.SetName(ChkLowercase, L("ToolPwdGenLowercase"));
        System.Windows.Automation.AutomationProperties.SetName(ChkDigits, L("ToolPwdGenDigits"));
        System.Windows.Automation.AutomationProperties.SetName(ChkSymbols, L("ToolPwdGenSymbols"));
        System.Windows.Automation.AutomationProperties.SetName(ChkExcludeAmbiguous, L("ToolPwdGenExcludeAmbiguous"));
        System.Windows.Automation.AutomationProperties.SetName(ChkCliSafe, L("ToolPwdGenCliSafe"));
        System.Windows.Automation.AutomationProperties.SetName(TxtCustomSpecials, L("ToolPwdGenCustomSpecials"));
        System.Windows.Automation.AutomationProperties.SetName(CmbSylPlacement, L("ToolPwdGenPlacement"));
        System.Windows.Automation.AutomationProperties.SetName(CmbKeyboardLayout, L("ToolPwdGenKeyboardLayout"));
        System.Windows.Automation.AutomationProperties.SetName(RbModeRandom, L("ToolPwdGenModeRandom"));
        System.Windows.Automation.AutomationProperties.SetName(RbModeSyllable, L("ToolPwdGenModeSyllable"));
        System.Windows.Automation.AutomationProperties.SetName(RbModePassphrase, L("ToolPwdGenModePassphrase"));
        System.Windows.Automation.AutomationProperties.SetName(SylLengthSlider, L("ToolPwdGenBaseLength"));
        System.Windows.Automation.AutomationProperties.SetName(CmbSylCase, L("ToolPwdGenCase"));
        System.Windows.Automation.AutomationProperties.SetName(SylDigitsSlider, L("ToolPwdGenDigits"));
        System.Windows.Automation.AutomationProperties.SetName(SylSpecialsSlider, L("ToolPwdGenSymbols"));
        System.Windows.Automation.AutomationProperties.SetName(PpWordCountSlider, L("ToolPwdGenWordCount"));
        System.Windows.Automation.AutomationProperties.SetName(TxtPpSeparator, L("ToolPwdGenSeparator"));
        System.Windows.Automation.AutomationProperties.SetName(CmbPpLanguage, L("ToolPwdGenLanguage"));
        System.Windows.Automation.AutomationProperties.SetName(ChkPpCapitalize, L("ToolPwdGenCapitalize"));
        System.Windows.Automation.AutomationProperties.SetName(ChkPpDigit, L("ToolPwdGenAddDigit"));
        System.Windows.Automation.AutomationProperties.SetName(ChkPpSpecial, L("ToolPwdGenAddSpecial"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetPin4, L("ToolPwdGenPresetPin4"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetPin6, L("ToolPwdGenPresetPin6"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetWifi, L("ToolPwdGenPresetWifi"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetApiKey, L("ToolPwdGenPresetApiKey"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetMysql, L("ToolPwdGenPresetMysql"));
        System.Windows.Automation.AutomationProperties.SetName(BtnPresetPassphrase4, L("ToolPwdGenPresetPassphrase4"));

        // ToolTip for generate button
        BtnGenerate.ToolTip = L("ToolPwdGenBtnGenerate");
    }

    private GeneratorMode CurrentMode
    {
        get
        {
            if (RbModeSyllable.IsChecked == true) return GeneratorMode.Syllable;
            if (RbModePassphrase.IsChecked == true) return GeneratorMode.Passphrase;
            return GeneratorMode.Random;
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        PanelRandom.Visibility = CurrentMode == GeneratorMode.Random ? Visibility.Visible : Visibility.Collapsed;
        PanelSyllable.Visibility = CurrentMode == GeneratorMode.Syllable ? Visibility.Visible : Visibility.Collapsed;
        PanelPassphrase.Visibility = CurrentMode == GeneratorMode.Passphrase ? Visibility.Visible : Visibility.Collapsed;
        PanelPlacement.Visibility = CurrentMode != GeneratorMode.Random ? Visibility.Visible : Visibility.Collapsed;

        if (!_suspendGeneration)
            GeneratePassword();
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        GeneratePassword();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(PasswordOutput.Text))
        {
            Clipboard.SetText(PasswordOutput.Text);
            CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
        }
    }

    private void OnParameterChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suspendGeneration) return;

        // Update all slider value displays
        LengthValueText.Text = ((int)LengthSlider.Value).ToString();
        SylLengthValueText.Text = ((int)SylLengthSlider.Value).ToString();
        SylDigitsValueText.Text = ((int)SylDigitsSlider.Value).ToString();
        SylSpecialsValueText.Text = ((int)SylSpecialsSlider.Value).ToString();
        PpWordCountValueText.Text = ((int)PpWordCountSlider.Value).ToString();

        GeneratePassword();
    }

    private void OnSymbolsChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suspendGeneration) return;

        PanelCustomSpecials.Visibility = ChkSymbols.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

        OnParameterChanged(sender, e);
    }

    private void GeneratePassword()
    {
        switch (CurrentMode)
        {
            case GeneratorMode.Random:
                GenerateRandomPassword();
                break;
            case GeneratorMode.Syllable:
                GenerateSyllablePassword();
                break;
            case GeneratorMode.Passphrase:
                GeneratePassphrase();
                break;
        }
    }

    // ── Random mode ──────────────────────────────────────────────────────────

    private void GenerateRandomPassword()
    {
        var charset = BuildCharset();
        if (charset.Length == 0)
        {
            PasswordOutput.Text = string.Empty;
            UpdateStrengthIndicator(0, 0);
            UpdatePhoneticDisplay(string.Empty);
            UpdateKeyboardLayoutDisplay(string.Empty);
            return;
        }

        var length = (int)LengthSlider.Value;
        LengthValueText.Text = length.ToString();

        var password = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            password.Append(charset[CryptoRandomInt(charset.Length)]);
        }

        PasswordOutput.Text = password.ToString();

        var entropyPerChar = Math.Log2(charset.Length);
        var totalEntropy = entropyPerChar * length;
        UpdateStrengthIndicator(totalEntropy, charset.Length);
        UpdatePhoneticDisplay(password.ToString());
        UpdateKeyboardLayoutDisplay(password.ToString());
    }

    private string BuildCharset()
    {
        var sb = new StringBuilder();
        if (ChkUppercase.IsChecked == true) sb.Append(UppercaseChars);
        if (ChkLowercase.IsChecked == true) sb.Append(LowercaseChars);
        if (ChkDigits.IsChecked == true) sb.Append(DigitChars);
        if (ChkSymbols.IsChecked == true)
        {
            var effectiveSymbols = GetEffectiveSymbols();
            if (effectiveSymbols.Length > 0)
                sb.Append(effectiveSymbols);
        }

        if (ChkExcludeAmbiguous.IsChecked == true)
        {
            var charset = sb.ToString();
            sb.Clear();
            foreach (var c in charset)
            {
                if (!AmbiguousChars.Contains(c))
                    sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private string GetEffectiveSymbols()
    {
        var symbols = ChkSymbols?.IsChecked == true && !string.IsNullOrWhiteSpace(TxtCustomSpecials?.Text)
            ? new string(TxtCustomSpecials.Text.Where(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)).Distinct().ToArray())
            : SymbolChars;

        if (ChkCliSafe?.IsChecked == true)
            symbols = new string(symbols.Where(c => !ShellDangerousChars.Contains(c)).ToArray());

        return symbols;
    }

    // ── Syllable mode ────────────────────────────────────────────────────────

    private void GenerateSyllablePassword()
    {
        var targetLength = (int)SylLengthSlider.Value;
        SylLengthValueText.Text = targetLength.ToString();

        var caseMode = (SyllableCase)CmbSylCase.SelectedIndex;
        var digitCount = (int)SylDigitsSlider.Value;
        SylDigitsValueText.Text = digitCount.ToString();
        var specialCount = (int)SylSpecialsSlider.Value;
        SylSpecialsValueText.Text = specialCount.ToString();
        var placement = (Placement)CmbSylPlacement.SelectedIndex;
        var effectiveSymbols = GetEffectiveSymbols();

        // Build syllable base
        var sb = new StringBuilder();
        var syllableIndex = 0;
        var charIndex = 0;
        while (sb.Length < targetLength)
        {
            var consonant = Consonants[CryptoRandomInt(Consonants.Length)];
            var vowel = Vowels[CryptoRandomInt(Vowels.Length)];

            switch (caseMode)
            {
                case SyllableCase.Upper:
                    consonant = consonant.ToUpperInvariant();
                    vowel = vowel.ToUpperInvariant();
                    break;
                case SyllableCase.Title:
                    if (sb.Length == 0 || (syllableIndex > 0 && sb.Length % 2 == 0))
                        consonant = consonant.ToUpperInvariant();
                    break;
                case SyllableCase.Mixed:
                    if (CryptoRandomInt(4) == 0)
                        consonant = consonant.ToUpperInvariant();
                    if (CryptoRandomInt(4) == 0)
                        vowel = vowel.ToUpperInvariant();
                    break;
                case SyllableCase.Alternating:
                    consonant = charIndex % 2 == 0
                        ? consonant.ToLowerInvariant()
                        : consonant.ToUpperInvariant();
                    charIndex++;
                    vowel = charIndex % 2 == 0
                        ? vowel.ToLowerInvariant()
                        : vowel.ToUpperInvariant();
                    charIndex++;
                    break;
                case SyllableCase.WordCase:
                    consonant = consonant.ToUpperInvariant();
                    break;
                case SyllableCase.Inverse:
                    consonant = consonant.ToUpperInvariant();
                    vowel = vowel.ToUpperInvariant();
                    break;
                // Lower: no changes needed
            }

            sb.Append(consonant);
            if (sb.Length < targetLength)
                sb.Append(vowel);

            syllableIndex++;
        }

        // Truncate to exact target length
        if (sb.Length > targetLength)
            sb.Length = targetLength;

        // For Inverse case: lowercase the last character
        if (caseMode == SyllableCase.Inverse && sb.Length > 0)
        {
            sb[sb.Length - 1] = char.ToLowerInvariant(sb[sb.Length - 1]);
        }

        // Convert to char array for insertion
        var chars = new List<char>(sb.ToString());

        // Insert digits and specials using the selected placement
        InsertExtras(chars, digitCount, specialCount, placement, effectiveSymbols);

        var finalPassword = new string(chars.ToArray());
        PasswordOutput.Text = finalPassword;
        SylTotalLengthText.Text = string.Format(L("ToolPwdGenTotalLength"), finalPassword.Length);

        // Entropy: syllable choices + digit/special insertions
        var syllablePoolSize = Consonants.Length * Vowels.Length; // 19*6 = 114
        var syllableCount = (targetLength + 1) / 2;
        var entropy = Math.Log2(syllablePoolSize) * syllableCount;
        if (digitCount > 0) entropy += Math.Log2(DigitChars.Length) * digitCount;
        if (specialCount > 0 && effectiveSymbols.Length > 0) entropy += Math.Log2(effectiveSymbols.Length) * specialCount;
        if (caseMode == SyllableCase.Mixed) entropy += syllableCount; // ~1 bit per syllable for case

        UpdateStrengthIndicator(entropy, syllablePoolSize);
        UpdatePhoneticDisplay(finalPassword);
        UpdateKeyboardLayoutDisplay(finalPassword);
    }

    private void InsertExtras(List<char> chars, int digitCount, int specialCount, Placement placement, string symbols)
    {
        var extras = new List<char>();
        for (var i = 0; i < digitCount; i++)
            extras.Add(DigitChars[CryptoRandomInt(DigitChars.Length)]);
        if (symbols.Length > 0)
        {
            for (var i = 0; i < specialCount; i++)
                extras.Add(symbols[CryptoRandomInt(symbols.Length)]);
        }

        switch (placement)
        {
            case Placement.Start:
                chars.InsertRange(0, extras);
                break;
            case Placement.End:
                chars.AddRange(extras);
                break;
            case Placement.Middle:
                var mid = chars.Count / 2;
                chars.InsertRange(mid, extras);
                break;
            default: // Random
                foreach (var c in extras)
                    chars.Insert(CryptoRandomInt(chars.Count + 1), c);
                break;
        }
    }

    // ── Passphrase mode ──────────────────────────────────────────────────────

    private void GeneratePassphrase()
    {
        var wordCount = (int)PpWordCountSlider.Value;
        PpWordCountValueText.Text = wordCount.ToString();

        var separator = TxtPpSeparator.Text;
        var isFrench = CmbPpLanguage.SelectedIndex == 1;
        var capitalize = ChkPpCapitalize.IsChecked == true;
        var addDigit = ChkPpDigit.IsChecked == true;
        var addSpecial = ChkPpSpecial.IsChecked == true;

        var wordList = isFrench ? _frenchWords : _englishWords;
        if (wordList.Length == 0)
        {
            PasswordOutput.Text = string.Empty;
            UpdateStrengthIndicator(0, 0);
            UpdatePhoneticDisplay(string.Empty);
            UpdateKeyboardLayoutDisplay(string.Empty);
            return;
        }

        var words = new string[wordCount];
        for (var i = 0; i < wordCount; i++)
        {
            var word = wordList[CryptoRandomInt(wordList.Length)];
            if (capitalize && word.Length > 0)
                word = char.ToUpperInvariant(word[0]) + word[1..];
            words[i] = word;
        }

        var passphrase = new StringBuilder(string.Join(separator, words));
        var effectiveSymbols = GetEffectiveSymbols();
        var placement = (Placement)CmbSylPlacement.SelectedIndex;

        var passphraseChars = new List<char>(passphrase.ToString());
        var digitExtras = addDigit ? 1 : 0;
        var specialExtras = addSpecial ? 1 : 0;
        InsertExtras(passphraseChars, digitExtras, specialExtras, placement, effectiveSymbols);

        var finalPassword = new string(passphraseChars.ToArray());
        PasswordOutput.Text = finalPassword;

        // Entropy calculation
        var entropy = Math.Log2(wordList.Length) * wordCount;
        if (addDigit) entropy += Math.Log2(DigitChars.Length);
        if (addSpecial && effectiveSymbols.Length > 0) entropy += Math.Log2(effectiveSymbols.Length);

        UpdateStrengthIndicator(entropy, wordList.Length);
        UpdatePhoneticDisplay(finalPassword);
        UpdateKeyboardLayoutDisplay(finalPassword);
    }

    // ── Strength indicator ───────────────────────────────────────────────────

    private void UpdateStrengthIndicator(double entropy, int poolSize)
    {
        string strengthKey;
        Brush barBrush;
        double widthPercent;

        switch (entropy)
        {
            case < 20:
                strengthKey = "ToolPwdGenStrengthCritical";
                barBrush = (Brush)FindResource("ErrorBrush");
                widthPercent = 0.10;
                break;
            case < 40:
                strengthKey = "ToolPwdGenStrengthWeak";
                barBrush = (Brush)FindResource("WarningBrush");
                widthPercent = 0.25;
                break;
            case < 60:
                strengthKey = "ToolPwdGenStrengthFair";
                barBrush = (Brush)FindResource("WarningBrush");
                widthPercent = 0.50;
                break;
            case < 80:
                strengthKey = "ToolPwdGenStrengthGood";
                barBrush = (Brush)FindResource("InfoBrush");
                widthPercent = 0.75;
                break;
            default:
                strengthKey = "ToolPwdGenStrengthStrong";
                barBrush = (Brush)FindResource("SuccessBrush");
                widthPercent = 1.0;
                break;
        }

        StrengthLabel.Text = $"{L(strengthKey)} ({entropy:F0} {L("ToolPwdGenBits")})";
        StrengthBar.Background = barBrush;

        // Update strength bar fill via Grid column proportions
        StrengthBarFillColumn.Width = new GridLength(widthPercent, GridUnitType.Star);
        StrengthBarEmptyColumn.Width = new GridLength(1 - widthPercent, GridUnitType.Star);

        // Entropy info line
        var entropyFormat = CurrentMode == GeneratorMode.Passphrase
            ? L("ToolPwdGenEntropyDict")
            : L("ToolPwdGenEntropy");
        EntropyText.Text = string.Format(entropyFormat, entropy.ToString("F1"), poolSize);

        // Issues list
        UpdateIssuesList();
    }

    private void UpdateIssuesList()
    {
        var password = PasswordOutput.Text;
        if (string.IsNullOrEmpty(password))
        {
            StrengthIssues.Text = string.Empty;
            return;
        }

        var issues = new List<string>();

        if (password.Length < 8)
            issues.Add(L("ToolPwdGenIssueTooShort"));

        if (CurrentMode == GeneratorMode.Random)
        {
            if (ChkUppercase.IsChecked == true && !password.Any(char.IsUpper))
                issues.Add(L("ToolPwdGenIssueNoUpper"));
            if (ChkLowercase.IsChecked == true && !password.Any(char.IsLower))
                issues.Add(L("ToolPwdGenIssueNoLower"));
            if (ChkDigits.IsChecked == true && !password.Any(char.IsDigit))
                issues.Add(L("ToolPwdGenIssueNoDigit"));
            if (ChkSymbols.IsChecked == true)
            {
                var effectiveSymbols = GetEffectiveSymbols();
                if (effectiveSymbols.Length > 0 && !password.Any(c => effectiveSymbols.Contains(c)))
                    issues.Add(L("ToolPwdGenIssueNoSpecial"));
            }
        }
        // Syllable and Passphrase modes: only warn about length

        StrengthIssues.Text = issues.Count > 0
            ? string.Join("  \u2022  ", issues)
            : string.Empty;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a cryptographically random integer in the range [0, exclusiveMax).
    /// </summary>
    private static int CryptoRandomInt(int exclusiveMax)
    {
        return RandomNumberGenerator.GetInt32(exclusiveMax);
    }

    private string L(string key) => _localizer?[key] ?? key;

    // ── Preset handlers ──────────────────────────────────────────────────────

    private void ApplyRandomPreset(int length, bool upper, bool lower, bool digits, bool symbols)
    {
        if (!_initialized) return;
        _suspendGeneration = true;
        try
        {
            RbModeRandom.IsChecked = true;
            OnModeChanged(RbModeRandom, new RoutedEventArgs());
            ChkUppercase.IsChecked = upper;
            ChkLowercase.IsChecked = lower;
            ChkDigits.IsChecked = digits;
            ChkSymbols.IsChecked = symbols;
            ChkExcludeAmbiguous.IsChecked = false;
            ChkCliSafe.IsChecked = false;
            TxtCustomSpecials.Text = SymbolChars;
            LengthSlider.Value = length;
        }
        finally
        {
            _suspendGeneration = false;
        }
        GeneratePassword();
    }

    private void OnPresetPin4(object sender, RoutedEventArgs e) =>
        ApplyRandomPreset(4, false, false, true, false);

    private void OnPresetPin6(object sender, RoutedEventArgs e) =>
        ApplyRandomPreset(6, false, false, true, false);

    private void OnPresetWifi(object sender, RoutedEventArgs e) =>
        ApplyRandomPreset(63, true, true, true, true);

    private void OnPresetApiKey(object sender, RoutedEventArgs e) =>
        ApplyRandomPreset(32, true, false, true, false);

    private void OnPresetMysql(object sender, RoutedEventArgs e) =>
        ApplyRandomPreset(16, true, true, true, false);

    private void OnPresetPassphrase4(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _suspendGeneration = true;
        try
        {
            RbModePassphrase.IsChecked = true;
            OnModeChanged(RbModePassphrase, new RoutedEventArgs());
            PpWordCountSlider.Value = 4;
            ChkPpCapitalize.IsChecked = true;
            ChkPpDigit.IsChecked = true;
            ChkPpSpecial.IsChecked = true;
            TxtPpSeparator.Text = "-";
            CmbPpLanguage.SelectedIndex = 0;
        }
        finally
        {
            _suspendGeneration = false;
        }
        GeneratePassword();
    }

    // ── Phonetic pronunciation ───────────────────────────────────────────────

    private void UpdatePhoneticDisplay(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > PhoneticMaxLength)
        {
            PanelPhonetic.Visibility = Visibility.Collapsed;
            return;
        }

        var parts = new List<string>();
        foreach (var c in password)
        {
            if (char.IsLetter(c))
            {
                var upperKey = char.ToUpperInvariant(c);
                if (NatoAlphabet.TryGetValue(upperKey, out var nato))
                {
                    parts.Add(char.IsUpper(c) ? nato.ToUpperInvariant() : nato.ToLowerInvariant());
                }
                else
                {
                    parts.Add(c.ToString());
                }
            }
            else if (char.IsDigit(c))
            {
                parts.Add(NatoAlphabet.TryGetValue(c, out var digitWord)
                    ? $"{c}:{digitWord}"
                    : c.ToString());
            }
            else
            {
                parts.Add(SpecialCharNames.TryGetValue(c, out var name)
                    ? $"{c}:{name}"
                    : c.ToString());
            }
        }

        PhoneticText.Text = string.Join(" - ", parts);
        PanelPhonetic.Visibility = Visibility.Visible;
    }

    // ── Keyboard layout translation ──────────────────────────────────────────

    private void OnKeyboardLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        UpdateKeyboardLayoutDisplay(PasswordOutput.Text);
    }

    private void UpdateKeyboardLayoutDisplay(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > PhoneticMaxLength)
        {
            PanelKeyboardLayout.Visibility = Visibility.Collapsed;
            return;
        }

        var isQwerty = CmbKeyboardLayout.SelectedIndex == 0;
        var mapping = isQwerty ? QwertyToAzerty : AzertyToQwerty;
        var labelKey = isQwerty ? "ToolPwdGenOnAzerty" : "ToolPwdGenOnQwerty";

        var sb = new StringBuilder(password.Length);
        foreach (var c in password)
        {
            sb.Append(mapping.TryGetValue(c, out var mapped) ? mapped : c);
        }

        KeyboardLayoutText.Text = $"{L(labelKey)} {sb}";
        PanelKeyboardLayout.Visibility = Visibility.Visible;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            GeneratePassword();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            PasswordOutput.Text = string.Empty;
            UpdatePhoneticDisplay(string.Empty);
            UpdateKeyboardLayoutDisplay(string.Empty);
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
