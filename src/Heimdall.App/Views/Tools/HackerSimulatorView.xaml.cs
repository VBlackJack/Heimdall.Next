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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Hollywood-style fake terminal with animated hacking scenarios.
/// Supports a catalog of demo, ops, and enterprise scenarios ranging from Matrix-style
/// visual effects to offensive sequences, deployment orchestration, hardening, and identity flows.
/// </summary>
public partial class HackerSimulatorView : UserControl, IToolView
{
    // ── Constants ─────────────────────────────────────────────

    private const double MatrixFontSize = 15.0;
    private const int MatrixTickMs = 45;
    private const int ScriptTickMs = 30;
    private const int MatrixMinTrail = 8;
    private const int MatrixMaxTrail = 26;
    private const double MatrixMinSpeed = 50.0;
    private const double MatrixMaxSpeed = 200.0;
    private const int CursorBlinkMs = 530;

    private const string MatrixChars =
        "\uff66\uff67\uff68\uff69\uff6a\uff6b\uff6c\uff6d\uff6e\uff6f" +
        "\uff70\uff71\uff72\uff73\uff74\uff75\uff76\uff77\uff78\uff79" +
        "\uff7a\uff7b\uff7c\uff7d\uff7e\uff7f\uff80\uff81\uff82\uff83" +
        "\uff84\uff85\uff86\uff87\uff88\uff89\uff8a\uff8b\uff8c\uff8d" +
        "\uff8e\uff8f\uff90\uff91\uff92\uff93\uff94\uff95\uff96\uff97" +
        "\uff98\uff99\uff9a\uff9b\uff9c\uff9d" +
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private const string GlitchChars = "\u2588\u2591\u2592\u2593\u2580\u2584@#$!?";

    // ── Static brushes (frozen) ───────────────────────────────

    private static readonly SolidColorBrush s_green = Freeze(0, 255, 65);
    private static readonly SolidColorBrush s_red = Freeze(255, 68, 68);
    private static readonly SolidColorBrush s_cyan = Freeze(0, 255, 255);
    private static readonly SolidColorBrush s_yellow = Freeze(255, 215, 0);
    private static readonly SolidColorBrush s_white = Freeze(255, 255, 255);
    private static readonly SolidColorBrush s_gray = Freeze(100, 100, 100);
    private static readonly SolidColorBrush s_amber = Freeze(255, 176, 0);
    private static readonly SolidColorBrush s_matrixGreen = Freeze(0, 200, 50);
    private static readonly SolidColorBrush s_matrixHead = Freeze(200, 255, 220);
    private static readonly LinearGradientBrush s_trailMask;

    static HackerSimulatorView()
    {
        s_trailMask = new LinearGradientBrush(
            System.Windows.Media.Colors.Transparent,
            System.Windows.Media.Colors.White,
            new System.Windows.Point(0, 0),
            new System.Windows.Point(0, 1));
        s_trailMask.Freeze();
    }

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly record struct ScenarioTheme(SolidColorBrush TextColor, System.Windows.Media.Color ShadowColor);

    // ── Fake data pools ───────────────────────────────────────

    private static readonly string[] s_fakeFiles =
    [
        "Project_Omega_Blueprint.pdf", "Financial_Report_Q4.xlsx",
        "Employee_Database_Full.csv",  "Satellite_Coordinates.json",
        "Intercepted_Comms.tar.gz",    "Launch_Codes_Final.txt",
        "Board_Meeting_Minutes.docx",  "Security_Audit.pdf",
        "Server_Credentials.kdbx",     "Prototype_Schematics.dwg",
        "Intelligence_Brief.pdf",      "Network_Topology.svg",
        "Classified_Personnel.xlsx",   "Research_Data_Alpha.hdf5",
        "VPN_Private_Keys.pem",        "Source_Code_Archive.tar.bz2",
    ];

    private static readonly string[] s_serviceAccounts =
    [
        "svc_sqlserver", "svc_exchange", "svc_backup", "svc_iis",
        "svc_sharepoint", "svc_adfs", "svc_sccm", "svc_crm",
    ];

    // ── Inner types ───────────────────────────────────────────

    private sealed class MatrixDrop
    {
        public double Y;
        public double Speed;
        public int TrailLength;
        public char[] Chars = [];
        public TextBlock Trail = null!;
        public TextBlock Head = null!;
    }

    private enum Act { AppendLine, UpdateLastLine, Clear, Delay, LoopRestart, TypeText, GlitchBurst }

    private readonly record struct ScriptAction(
        Act Type, string Text, SolidColorBrush? Color, int DelayMs);

    // ── Instance fields ───────────────────────────────────────

    private LocalizationManager? _localizer;
    private DispatcherTimer? _timer;
    private Random _rng = new();
    private bool _isRunning;
    private bool _disposed;
    private double _speedMultiplier = 1.0;
    private bool _isFullscreen;
    private bool _randomMode;
    private SolidColorBrush _themeText = s_green;

    // Matrix rain state
    private readonly List<MatrixDrop> _drops = [];
    private double _cellWidth;
    private double _cellHeight;

    // Script engine state
    private List<ScriptAction> _script = [];
    private int _scriptIndex;
    private int _ticksRemaining;

    // TypeText state
    private bool _typingInProgress;
    private Run? _typingRun;
    private string _typingFullText = "";
    private int _typingCharIndex;
    private int _typingCharDelay;

    // GlitchBurst state
    private int _glitchTicksLeft;
    private readonly List<(Run Run, string Original)> _glitchTargets = [];

    // Blinking cursor state
    private DispatcherTimer? _cursorTimer;
    private Run? _cursorRun;
    private bool _cursorVisible;

    // ── Constructor & IToolView ───────────────────────────────

    public HackerSimulatorView() => InitializeComponent();

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        if (_localizer != null)
            _localizer.LocaleChanged -= OnLocaleChanged;

        _localizer = localizer;
        if (_localizer != null)
            _localizer.LocaleChanged += OnLocaleChanged;

        LoadSimulatorPreferences();
        EnsureScenarioCatalog();
        ApplyLocalization();
        ApplyVintageMonitorState();
        TerminalGrid.PreviewMouseRightButtonDown += OnTerminalPreviewRightClick;
        TerminalGrid.PreviewMouseLeftButtonDown += OnTerminalPreviewMouseDown;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_disposed)
                return;

            RefreshScenarioPicker(restartIfSelectionChanges: false);
            StartScenario(newSession: true);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_localizer != null)
            _localizer.LocaleChanged -= OnLocaleChanged;
        StopScenario();
        StopVintageMonitorFlicker();
        GC.SuppressFinalize(this);
    }

    // ── Event handlers ────────────────────────────────────────

    private void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
            StopScenario();
        else
            StartScenario(newSession: true);
    }

    private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _speedMultiplier = e.NewValue;
        if (LblSpeedValue != null)
            LblSpeedValue.Text = $"{_speedMultiplier:F2}x";
    }

    private void OnMatrixCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_disposed && _isRunning && GetCurrentScenario().IsMatrix)
            RebuildMatrixColumns();
    }

    private void OnTerminalPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _isFullscreen = !_isFullscreen;
            ToolbarBorder.Visibility = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
            e.Handled = true;
        }
    }

    private void OnTerminalPreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        // Stop event tunneling to prevent MainWindow.OnSessionTabRightClick
        // from intercepting and showing the session tab context menu instead.
        e.Handled = true;
        BuildContextMenu();
        TerminalGrid.ContextMenu!.IsOpen = true;
    }

    private void BuildContextMenu()
    {
        EnsureScenarioCatalog();

        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 15, 15)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Foreground = s_green,
        };

        foreach (var group in GetContextMenuScenarios().GroupBy(s => s.Category).OrderBy(g => g.Key))
        {
            menu.Items.Add(new MenuItem
            {
                Header = GetCategoryLabel(group.Key),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                IsEnabled = false,
                Foreground = s_gray,
            });

            foreach (var scenario in group)
            {
                string scenarioId = scenario.Id;
                var item = new MenuItem
                {
                    Header = FormatScenarioDisplay(scenario),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    IsCheckable = true,
                    IsChecked = string.Equals(_currentScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += (_, _) =>
                {
                    ClearPlaylistSelection();
                    _randomMode = false;
                    if (ChkRandomMode != null)
                        ChkRandomMode.IsChecked = false;
                    _currentScenarioId = scenarioId;
                    SyncScenarioSelection();
                    PersistSimulatorPreferences();
                    if (_isRunning)
                    {
                        StopScenario();
                        StartScenario(newSession: true);
                    }
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
        }

        if (menu.Items.Count > 0 && menu.Items[^1] is Separator)
            menu.Items.RemoveAt(menu.Items.Count - 1);

        menu.Items.Add(new Separator());

        var randomItem = new MenuItem
        {
            Header = L("ToolHackerSimRandomInFilter"),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            IsCheckable = true,
            IsChecked = _randomMode,
            Foreground = s_yellow,
        };
        randomItem.Click += (_, _) =>
        {
            _randomMode = !_randomMode;
            if (ChkRandomMode != null)
                ChkRandomMode.IsChecked = _randomMode;
            UpdateScenarioLabel();
            PersistSimulatorPreferences();
        };
        menu.Items.Add(randomItem);

        // Assign to all terminal surfaces so right-click works in both modes
        TerminalBorder.ContextMenu = menu;
        MatrixCanvas.ContextMenu = menu;
        TerminalGrid.ContextMenu = menu;
    }

    private void ApplyScenarioTheme(ScenarioDefinition scenario)
    {
        _themeText = scenario.Theme.TextColor;
        TerminalOutput.Foreground = scenario.Theme.TextColor;
        TerminalOutput.Effect = new DropShadowEffect
        {
            Color = scenario.Theme.ShadowColor,
            BlurRadius = 3,
            ShadowDepth = 0,
            Opacity = 0.3,
        };
    }

    // ── Scenario lifecycle ────────────────────────────────────

    private void StartScenario(bool newSession = false, bool reuseSeed = false)
    {
        EnsureScenarioCatalog();
        if (newSession || _sessionSeed == 0)
            ResetRunSession(reuseSeed);

        PrepareRandomForScenarioExecution();
        var scenario = GetCurrentScenario();
        _isRunning = true;
        try
        {
            BtnStartStop.Content = $"\u25A0 {L("ToolHackerSimBtnStop")}";
            UpdateScenarioLabel();
            ApplyScenarioTheme(scenario);
            BeginTranscriptSection(scenario);

            if (scenario.IsMatrix)
            {
                TerminalBorder.Visibility = Visibility.Collapsed;
                MatrixCanvas.Visibility = Visibility.Visible;
                TranscriptAppendLine(Tx("[visual] matrix rain backdrop active", "[visuel] pluie matrix active"));
                RebuildMatrixColumns();
                _timer = new DispatcherTimer(DispatcherPriority.Render)
                { Interval = TimeSpan.FromMilliseconds(MatrixTickMs) };
                _timer.Tick += OnMatrixTick;
                _timer.Start();
            }
            else
            {
                MatrixCanvas.Visibility = Visibility.Collapsed;
                TerminalBorder.Visibility = Visibility.Visible;
                TerminalOutput.Inlines.Clear();
                _script = scenario.Builder();
                _scriptIndex = 0;
                _ticksRemaining = 0;
                _typingInProgress = false;
                _glitchTicksLeft = 0;
                _timer = new DispatcherTimer(DispatcherPriority.Background)
                { Interval = TimeSpan.FromMilliseconds(ScriptTickMs) };
                _timer.Tick += OnScriptTick;
                _timer.Start();
            }

            UpdatePlaybackControls();
        }
        catch
        {
            _isRunning = false;
            throw;
        }
    }

    private void StopScenario()
    {
        _isRunning = false;
        _timer?.Stop();
        _timer = null;
        _typingInProgress = false;
        RestoreGlitch();
        StopBlinkingCursor();
        TranscriptCompleteTypingLine();
        BtnStartStop.Content = $"\u25B6 {L("ToolHackerSimBtnStart")}";
        _drops.Clear();
        MatrixCanvas.Children.Clear();
        UpdatePlaybackControls();
    }

    private void UpdateScenarioLabel()
    {
        var scenario = GetCurrentScenario();
        bool isFavorite = _favoriteScenarioIds.Contains(scenario.Id);

        if (LblCurrentScenario != null)
        {
            string randomBadge = _randomMode ? $" \u00b7 {L("ToolHackerSimRandom")}" : string.Empty;
            string favoriteBadge = isFavorite ? "\u2605 " : string.Empty;
            LblCurrentScenario.Text = $"{favoriteBadge}{GetScenarioTitle(scenario)} \u00b7 {GetCategoryLabel(scenario.Category)} \u00b7 {GetRealismLabel(scenario.Realism)}{randomBadge}";
        }

        if (LblScenarioSubtitle != null)
        {
            string subtitle = GetScenarioSubtitle(scenario);
            if (GetSelectedPlaylist() is { } playlist)
                subtitle = $"{subtitle} \u00b7 {L("ToolHackerSimPlaylist")} : {Tx(playlist.Title)}";
            if (_filterFallbackActive)
                subtitle = $"{subtitle} \u00b7 {L("ToolHackerSimFilterFallback")}";
            LblScenarioSubtitle.Text = subtitle;
        }
    }

    // ── Matrix rain engine ────────────────────────────────────

    private void MeasureCells()
    {
        if (_cellWidth > 0) return;
        double ppd;
        try { ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
        catch { ppd = 1.0; }

        var ft1 = new FormattedText("M", CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight, new Typeface("Consolas"),
            MatrixFontSize, Brushes.White, ppd);
        var ft2 = new FormattedText("\uff71", CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight, new Typeface("Consolas"),
            MatrixFontSize, Brushes.White, ppd);

        _cellWidth = Math.Max(
            Math.Ceiling(ft1.WidthIncludingTrailingWhitespace),
            Math.Ceiling(ft2.WidthIncludingTrailingWhitespace)) + 1;
        _cellHeight = Math.Ceiling(ft1.Height);
    }

    private void RebuildMatrixColumns()
    {
        MeasureCells();
        _drops.Clear();
        MatrixCanvas.Children.Clear();

        double w = MatrixCanvas.ActualWidth;
        double h = MatrixCanvas.ActualHeight;
        if (w < 10 || h < 10) return;

        int cols = Math.Max(1, (int)(w / _cellWidth));
        for (int i = 0; i < cols; i++)
        {
            var drop = CreateDrop(i * _cellWidth, h, randomizeY: true);
            _drops.Add(drop);
            MatrixCanvas.Children.Add(drop.Trail);
            MatrixCanvas.Children.Add(drop.Head);
            System.Windows.Controls.Panel.SetZIndex(drop.Head, 1);
        }
    }

    private MatrixDrop CreateDrop(double x, double canvasHeight, bool randomizeY)
    {
        int trailLen = _rng.Next(MatrixMinTrail, MatrixMaxTrail + 1);
        double speed = MatrixMinSpeed + _rng.NextDouble() * (MatrixMaxSpeed - MatrixMinSpeed);
        var chars = new char[trailLen];
        for (int i = 0; i < trailLen; i++)
            chars[i] = MatrixChars[_rng.Next(MatrixChars.Length)];

        double headY = randomizeY
            ? _rng.NextDouble() * (canvasHeight + trailLen * _cellHeight)
              - trailLen * _cellHeight * 0.5
            : -(trailLen * _cellHeight);

        var trail = new TextBlock
        {
            Text = string.Join("\n", chars),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = MatrixFontSize,
            Foreground = s_matrixGreen,
            LineHeight = _cellHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            OpacityMask = s_trailMask,
        };

        var head = new TextBlock
        {
            Text = chars[^1].ToString(),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = MatrixFontSize,
            Foreground = s_matrixHead,
            LineHeight = _cellHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };

        Canvas.SetLeft(trail, x);
        Canvas.SetLeft(head, x);
        Canvas.SetTop(trail, headY - (trailLen - 1) * _cellHeight);
        Canvas.SetTop(head, headY);

        return new MatrixDrop
        {
            Y = headY,
            Speed = speed,
            TrailLength = trailLen,
            Chars = chars,
            Trail = trail,
            Head = head,
        };
    }

    private void OnMatrixTick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        double h = MatrixCanvas.ActualHeight;
        if (h < 10) return;

        double dt = MatrixTickMs / 1000.0 * _speedMultiplier;

        for (int di = 0; di < _drops.Count; di++)
        {
            var d = _drops[di];
            d.Y += d.Speed * dt;

            if (d.Y > h + _cellHeight * 8)
            {
                d.Y = -(d.TrailLength * _cellHeight);
                d.Speed = MatrixMinSpeed + _rng.NextDouble() * (MatrixMaxSpeed - MatrixMinSpeed);
                for (int i = 0; i < d.Chars.Length; i++)
                    d.Chars[i] = MatrixChars[_rng.Next(MatrixChars.Length)];
                d.Trail.Text = string.Join("\n", d.Chars);
                d.Head.Text = d.Chars[^1].ToString();
            }
            else if (_rng.Next(100) < 12)
            {
                int idx = _rng.Next(d.Chars.Length);
                d.Chars[idx] = MatrixChars[_rng.Next(MatrixChars.Length)];
                d.Trail.Text = string.Join("\n", d.Chars);
                if (idx == d.Chars.Length - 1)
                    d.Head.Text = d.Chars[^1].ToString();
            }

            Canvas.SetTop(d.Trail, d.Y - (d.TrailLength - 1) * _cellHeight);
            Canvas.SetTop(d.Head, d.Y);
        }
    }

    // ── Script engine ─────────────────────────────────────────

    private void OnScriptTick(object? sender, EventArgs e)
    {
        if (_disposed || _scriptIndex >= _script.Count) return;

        // Handle GlitchBurst countdown independently
        if (_glitchTicksLeft > 0)
        {
            _glitchTicksLeft--;
            if (_glitchTicksLeft == 0)
                RestoreGlitch();
        }

        // Handle TypeText character-by-character
        if (_typingInProgress)
        {
            if (_typingRun != null && _typingCharIndex < _typingFullText.Length)
            {
                _typingRun.Text += _typingFullText[_typingCharIndex];
                _typingCharIndex++;
                TranscriptUpdateTypingLine(_typingRun.Text);
                TerminalScroller.ScrollToEnd();

                // Calculate delay ticks for next character
                _ticksRemaining = Math.Max(1,
                    (int)(_typingCharDelay / (ScriptTickMs * _speedMultiplier)));
                return;
            }
            // Typing done — finalize with newline
            if (_typingRun != null)
                _typingRun.Text += "\n";
            _typingInProgress = false;
            _typingRun = null;
            TranscriptCompleteTypingLine();
            _ticksRemaining = 0;
            return;
        }

        if (_ticksRemaining > 0) { _ticksRemaining--; return; }

        var a = _script[_scriptIndex++];

        switch (a.Type)
        {
            case Act.AppendLine:
                TerminalOutput.Inlines.Add(
                    new Run(a.Text + "\n") { Foreground = a.Color ?? _themeText });
                TranscriptAppendLine(a.Text);
                TerminalScroller.ScrollToEnd();
                break;

            case Act.UpdateLastLine:
                if (TerminalOutput.Inlines.LastInline is Run last)
                {
                    last.Text = a.Text + "\n";
                    if (a.Color != null) last.Foreground = a.Color;
                }
                TranscriptUpdateLastLine(a.Text);
                break;

            case Act.Clear:
                TerminalOutput.Inlines.Clear();
                break;

            case Act.Delay:
                break;

            case Act.LoopRestart:
                // Start blinking cursor during the delay
                StartBlinkingCursor();
                // In playlist mode, advance deterministically to the next scripted step.
                if (GetSelectedPlaylist() is not null)
                {
                    AdvancePlaylistScenario();
                    ApplyScenarioTheme(GetCurrentScenario());
                    UpdateScenarioLabel();
                }
                else if (_randomMode)
                {
                    var nextScenario = PickRandomScenario();
                    _currentScenarioId = nextScenario.Id;
                    ApplyScenarioTheme(nextScenario);
                    SyncScenarioSelection();
                    UpdateScenarioLabel();
                    PersistSimulatorPreferences();
                }
                // Delay ticks will be computed below; after delay, clear and restart
                _ticksRemaining = a.DelayMs > 0
                    ? Math.Max(1, (int)(a.DelayMs / (ScriptTickMs * _speedMultiplier)))
                    : 0;
                // The next tick after delay will process the "restart" when _scriptIndex
                // is reset below. But we need to defer the actual clear + rebuild.
                // Use a wrapper: set index to count so the next cycle after delay
                // will trigger the deferred restart via a continuation check.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    if (_disposed || !_isRunning) return;
                    // Wait for the delay to elapse, then restart
                    var restartTimer = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromMilliseconds(
                            Math.Max(ScriptTickMs, a.DelayMs / _speedMultiplier))
                    };
                    restartTimer.Tick += (_, _) =>
                    {
                        restartTimer.Stop();
                        if (_disposed || !_isRunning) return;
                        StopBlinkingCursor();
                        TerminalOutput.Inlines.Clear();
                        PrepareRandomForScenarioExecution();
                        BeginTranscriptSection(GetCurrentScenario());
                        _script = GetCurrentScenario().Builder();
                        _scriptIndex = 0;
                        _ticksRemaining = 0;
                    };
                    restartTimer.Start();
                });
                // Park the script index beyond the end so normal ticks are no-ops
                // until the restart timer fires
                _scriptIndex = _script.Count;
                return; // Skip the default delay computation below

            case Act.TypeText:
                _typingInProgress = true;
                try
                {
                    _typingFullText = a.Text;
                    _typingCharIndex = 0;
                    _typingCharDelay = a.DelayMs;
                    _typingRun = new Run("") { Foreground = a.Color ?? _themeText };
                    TerminalOutput.Inlines.Add(_typingRun);
                    TranscriptBeginTypingLine();
                    TerminalScroller.ScrollToEnd();
                    _ticksRemaining = Math.Max(1,
                        (int)(_typingCharDelay / (ScriptTickMs * _speedMultiplier)));
                }
                catch
                {
                    _typingInProgress = false;
                    throw;
                }
                return;

            case Act.GlitchBurst:
                ExecuteGlitchBurst();
                break;
        }

        _ticksRemaining = a.DelayMs > 0
            ? Math.Max(1, (int)(a.DelayMs / (ScriptTickMs * _speedMultiplier)))
            : 0;
    }

    // ── GlitchBurst helpers ──────────────────────────────────

    private void ExecuteGlitchBurst()
    {
        RestoreGlitch(); // Clear any previous glitch

        int targetCount = _rng.Next(3, 9);
        var inlines = TerminalOutput.Inlines;
        int inlineCount = 0;
        var allRuns = new List<Run>();
        var enumerator = inlines.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is Run r && r.Text.Length > 1)
            {
                allRuns.Add(r);
                inlineCount++;
            }
        }

        if (inlineCount == 0) return;

        int count = Math.Min(targetCount, inlineCount);
        for (int i = 0; i < count; i++)
        {
            var run = allRuns[_rng.Next(allRuns.Count)];
            _glitchTargets.Add((run, run.Text));

            // Corrupt the text with glitch characters
            var corrupted = run.Text.ToCharArray();
            int mutations = _rng.Next(2, Math.Min(6, corrupted.Length));
            for (int m = 0; m < mutations; m++)
            {
                int pos = _rng.Next(corrupted.Length);
                corrupted[pos] = GlitchChars[_rng.Next(GlitchChars.Length)];
            }
            run.Text = new string(corrupted);
        }

        _glitchTicksLeft = 3;
    }

    private void RestoreGlitch()
    {
        for (int i = 0; i < _glitchTargets.Count; i++)
        {
            var (run, original) = _glitchTargets[i];
            run.Text = original;
        }
        _glitchTargets.Clear();
        _glitchTicksLeft = 0;
    }

    // ── Blinking cursor helpers ──────────────────────────────

    private void StartBlinkingCursor()
    {
        StopBlinkingCursor();
        _cursorRun = new Run("root@target:~# \u2588") { Foreground = _themeText };
        TerminalOutput.Inlines.Add(_cursorRun);
        TerminalScroller.ScrollToEnd();
        _cursorVisible = true;
        try
        {
            _cursorTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(CursorBlinkMs)
            };
            _cursorTimer.Tick += OnCursorBlink;
            _cursorTimer.Start();
        }
        catch
        {
            _cursorVisible = false;
            throw;
        }
    }

    private void StopBlinkingCursor()
    {
        _cursorTimer?.Stop();
        _cursorTimer = null;
        if (_cursorRun != null)
        {
            TerminalOutput.Inlines.Remove(_cursorRun);
            _cursorRun = null;
        }
    }

    private void OnCursorBlink(object? sender, EventArgs e)
    {
        if (_cursorRun == null) return;
        _cursorVisible = !_cursorVisible;
        _cursorRun.Text = _cursorVisible ? "root@target:~# \u2588" : "root@target:~#  ";
    }

    // ── Script helpers ────────────────────────────────────────

    private static ScriptAction Line(string text, SolidColorBrush? color = null, int delay = 80)
        => new(Act.AppendLine, text, color, delay);

    private static ScriptAction Upd(string text, SolidColorBrush? color = null, int delay = 50)
        => new(Act.UpdateLastLine, text, color, delay);

    private static ScriptAction Wait(int ms)
        => new(Act.Delay, "", null, ms);

    private static ScriptAction Loop(int delay = 5000)
        => new(Act.LoopRestart, "", null, delay);

    private static ScriptAction Type(string text, SolidColorBrush? color = null, int charDelayMs = 50)
        => new(Act.TypeText, text, color, charDelayMs);

    private static ScriptAction Glitch()
        => new(Act.GlitchBurst, "", null, 100);

    private static string Bar(int pct, int w = 25)
    {
        int f = pct * w / 100;
        return $"[{new string('\u2588', f)}{new string('\u2591', w - f)}] {pct,3}%";
    }

    private string RandIp()
        => $"{_rng.Next(10, 192)}.{_rng.Next(0, 255)}.{_rng.Next(0, 255)}.{_rng.Next(1, 254)}";

    private string RandHex(int len)
    {
        const string hex = "0123456789ABCDEF";
        Span<char> buf = stackalloc char[len];
        for (int i = 0; i < len; i++)
            buf[i] = hex[_rng.Next(16)];
        return new string(buf);
    }

    private string RandMac()
        => $"{RandHex(2)}:{RandHex(2)}:{RandHex(2)}:{RandHex(2)}:{RandHex(2)}:{RandHex(2)}";

    private void AddFileTransfer(List<ScriptAction> s, int idx, int total, string file)
    {
        string pre = $"  [{idx:D3}/{total}] {file,-35}";
        s.Add(Line($"{pre} {Bar(0)}", delay: 60));
        for (int p = 20; p <= 100; p += 20)
        {
            int d = p == 100 ? 120 : 40 + _rng.Next(80);
            s.Add(Upd($"{pre} {Bar(p)} {_rng.Next(8, 28)}.{_rng.Next(0, 9)} MB/s", delay: d));
        }
    }

    private void AddBlockDecrypt(List<ScriptAction> s, int block, int total)
    {
        string pre = $"  Block {block,3}/{total}";
        s.Add(Line($"{pre} {Bar(0)}", delay: 40));
        for (int p = 25; p <= 100; p += 25)
            s.Add(Upd($"{pre} {Bar(p)} {_rng.Next(15, 32)}.{_rng.Next(0, 9)} MB/s",
                delay: 30 + _rng.Next(50)));
    }

    // ── Scenario 1: Penetration Test ─────────────────────────

    private List<ScriptAction> BuildPentestScript()
    {
        var s = new List<ScriptAction>();
        string net = $"10.{_rng.Next(1, 200)}.{_rng.Next(1, 200)}";
        string gw = $"{net}.1";
        string t1 = $"{net}.{_rng.Next(10, 50)}";
        string t2 = $"{net}.{_rng.Next(100, 200)}";
        string me = RandIp();
        int modules = _rng.Next(800, 1200);
        int hosts = _rng.Next(12, 28);

        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_cyan, 30));
        s.Add(Line("  \u2551     ADVANCED PENETRATION TESTING FRAMEWORK      \u2551", s_cyan, 30));
        s.Add(Line($"  \u2551                  v4.2.1-dev                     \u2551", s_cyan, 30));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_cyan, 30));
        s.Add(Line("", delay: 300));

        // Init
        s.Add(Line($"[*] Framework initialized \u2014 {modules} exploit modules loaded", delay: 150));
        s.Add(Line($"[*] Target network: {net}.0/24", delay: 100));
        s.Add(Line("[*] Starting reconnaissance phase...", delay: 500));
        s.Add(Line("", delay: 100));

        // Discovery
        s.Add(Line("\u2500\u2500\u2500 Phase 1: Host Discovery \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 300));
        s.Add(Line($"[+] Host {gw} \u2014 UP (0.003s) \u2014 Gateway", delay: 120));
        s.Add(Line($"[+] Host {t1} \u2014 UP (0.012s) \u2014 Linux Server", delay: 120));
        s.Add(Line($"[+] Host {t2} \u2014 UP (0.008s) \u2014 Windows Server", delay: 120));
        s.Add(Line($"[+] Host {net}.{_rng.Next(50, 99)} \u2014 UP (0.015s) \u2014 Network Printer", delay: 120));
        s.Add(Line($"[+] Host {net}.{_rng.Next(200, 250)} \u2014 UP (0.021s) \u2014 IP Camera", delay: 120));
        s.Add(Line($"[*] {hosts} hosts discovered on {net}.0/24", delay: 300));
        s.Add(Line("", delay: 100));

        // Port scan
        s.Add(Line("\u2500\u2500\u2500 Phase 2: Port Scan \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 200));
        s.Add(Line($"[*] Scanning {t1}...", delay: 400));
        s.Add(Line($"  22/tcp   open   ssh       OpenSSH 8.9p1 Ubuntu", s_white, 60));
        s.Add(Line($"  80/tcp   open   http      nginx/1.24.0", s_white, 60));
        s.Add(Line($"  443/tcp  open   https     nginx/1.24.0", s_white, 60));
        s.Add(Line($"  3306/tcp open   mysql     MySQL 8.0.35", s_white, 60));
        s.Add(Line($"  6379/tcp open   redis     Redis 7.2.3", s_white, 60));
        s.Add(Line($"  8080/tcp open   http      Tomcat 10.1.18", s_white, 100));
        s.Add(Line("", delay: 200));

        // Vuln scan
        s.Add(Line("\u2500\u2500\u2500 Phase 3: Vulnerability Assessment \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 200));
        s.Add(Line("[*] Running vulnerability detection modules...", delay: 800));
        s.Add(Line($"[!] CRITICAL: CVE-2024-3094 on {t1}:22 \u2014 xz-utils backdoor", s_red, 200));
        s.Add(Line($"[!] HIGH: CVE-2023-44487 on {t1}:443 \u2014 HTTP/2 Rapid Reset", s_amber, 150));
        s.Add(Line($"[!] MEDIUM: Redis {t1}:6379 \u2014 no authentication", s_yellow, 150));
        s.Add(Line("", delay: 200));

        // Exploitation
        s.Add(Line("\u2500\u2500\u2500 Phase 4: Exploitation \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 200));
        s.Add(Line("[*] Selected exploit: xz_backdoor_rce (CVE-2024-3094)", delay: 200));
        s.Add(Line($"[*] Payload: reverse_tcp/meterpreter (LHOST={me} LPORT=4444)", delay: 200));
        s.Add(Glitch());
        s.Add(Line($"[*] Launching exploit against {t1}...", s_yellow, 1000));
        s.Add(Line("[+] Exploit successful!", s_cyan, 300));
        s.Add(Line($"[+] Meterpreter session 1 opened ({me}:4444 \u2192 {t1}:38901)", s_cyan, 500));
        s.Add(Line("", delay: 200));

        // Post-exploitation
        s.Add(Line("\u2500\u2500\u2500 Phase 5: Post-Exploitation \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 200));
        s.Add(Line("meterpreter > getuid", s_white, 400));
        s.Add(Line("Server username: www-data", delay: 200));
        s.Add(Line("meterpreter > shell", s_white, 400));
        s.Add(Line("$ whoami", s_white, 300));
        s.Add(Line("www-data", delay: 200));
        s.Add(Line("$ sudo -l", s_white, 400));
        s.Add(Line("(ALL) NOPASSWD: ALL", s_cyan, 400));
        s.Add(Line("$ sudo su -", s_white, 400));
        s.Add(Line("# whoami", s_white, 300));
        s.Add(Line("root", s_red, 500));
        s.Add(Line("# cat /etc/shadow | head -3", s_white, 300));
        s.Add(Line($"root:$6$r{RandHex(6)}$Kx8v...truncated:19722:0:99999:7:::", s_red, 100));
        s.Add(Line("daemon:*:19722:0:99999:7:::", s_red, 100));
        s.Add(Line($"admin:$6$s{RandHex(6)}$Hm2q...truncated:19801:0:99999:7:::", s_red, 200));
        s.Add(Line("", delay: 600));

        // Access granted banner
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_cyan, 50));
        s.Add(Line("  \u2551                                                  \u2551", s_cyan, 50));
        s.Add(Line("  \u2551          ACCESS GRANTED \u2014 ROOT SHELL            \u2551", s_cyan, 50));
        s.Add(Line("  \u2551                                                  \u2551", s_cyan, 50));
        s.Add(Line($"  \u2551  Target: {t1,-39}\u2551", s_cyan, 50));
        s.Add(Line("  \u2551  Privilege: root (uid=0)                        \u2551", s_cyan, 50));
        s.Add(Line("  \u2551  Sessions: 1 active                             \u2551", s_cyan, 50));
        s.Add(Line("  \u2551                                                  \u2551", s_cyan, 50));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_cyan, 50));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 2: Data Exfiltration ─────────────────────────

    private List<ScriptAction> BuildDataExfilScript()
    {
        var s = new List<ScriptAction>();
        string vault = RandIp();
        int totalFiles = _rng.Next(400, 700);
        double totalGb = totalFiles * 0.004 + _rng.NextDouble() * 0.5;

        s.Add(Line("", delay: 200));
        s.Add(Line("[*] Establishing encrypted channel...", delay: 600));
        s.Add(Line($"[+] AES-256-GCM tunnel established via {RandIp()}", s_cyan, 300));
        s.Add(Line($"[*] Connecting to secure vault at {vault}...", delay: 500));
        s.Add(Line("[+] Authentication successful (certificate-based)", s_cyan, 300));
        s.Add(Line("", delay: 100));

        // Enumerate
        s.Add(Line("[*] Enumerating classified documents...", delay: 800));
        s.Add(Line($"[+] /vault/TOP_SECRET/     \u2014 {_rng.Next(80, 180)} files ({_rng.Next(400, 900)} MB)", s_cyan, 120));
        s.Add(Line($"[+] /vault/CONFIDENTIAL/   \u2014 {_rng.Next(200, 400)} files ({_rng.Next(800, 1500)} MB)", s_cyan, 120));
        s.Add(Line($"[+] /vault/RESTRICTED/     \u2014 {_rng.Next(50, 120)} files ({_rng.Next(200, 500)} MB)", s_cyan, 120));
        s.Add(Line($"[*] Total: {totalFiles} files ({totalGb:F1} GB)", delay: 400));
        s.Add(Line("", delay: 100));

        s.Add(Line("[*] Starting exfiltration...", s_yellow, 300));
        s.Add(Line("", delay: 60));

        // File transfers with progress bars
        for (int i = 0; i < 8; i++)
        {
            string file = s_fakeFiles[_rng.Next(s_fakeFiles.Length)];
            AddFileTransfer(s, i + 1, totalFiles, file);
        }

        s.Add(Line("  ...", s_gray, 200));
        s.Add(Line($"  [{totalFiles:D3}/{totalFiles}] (remaining files transferred)", s_gray, 400));
        s.Add(Line("", delay: 200));

        // Summary
        s.Add(Line($"[+] Transfer complete: {totalFiles} files, {totalGb:F1} GB", s_cyan, 400));
        s.Add(Line("", delay: 200));

        // Cleanup
        s.Add(Line("[*] Erasing evidence...", s_yellow, 500));
        s.Add(Line("[+] Access logs purged", delay: 200));
        s.Add(Line("[+] Audit trail modified", delay: 200));
        s.Add(Line("[+] Timestamps sanitized", delay: 200));
        s.Add(Line("[+] Connection fingerprint removed", delay: 300));
        s.Add(Line("", delay: 100));
        s.Add(Line("[*] Disconnecting...", delay: 400));
        s.Add(Line("[+] Channel closed. No traces remaining.", s_cyan, 200));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 3: Kernel Panic ──────────────────────────────

    private List<ScriptAction> BuildKernelPanicScript()
    {
        var s = new List<ScriptAction>();
        double ts = 342.784 + _rng.NextDouble();
        string Ts() { ts += _rng.NextDouble() * 0.008; return $"[{ts,14:F6}]"; }
        int pid = _rng.Next(1000, 9999);

        // Normal boot/operation
        s.Add(Line($"{Ts()} systemd[1]: Started Daily apt download activities.", s_white, 150));
        s.Add(Line($"{Ts()} kernel: EXT4-fs (sda1): mounted filesystem with ordered data mode.", s_white, 150));
        s.Add(Line($"{Ts()} sshd[{_rng.Next(1000, 5000)}]: Accepted publickey for admin from {RandIp()} port {_rng.Next(40000, 60000)} ssh2", s_white, 150));
        s.Add(Line($"{Ts()} kernel: TCP: request_sock_TCP: Possible SYN flooding on port 443.", s_white, 200));
        s.Add(Line($"{Ts()} kernel: ACPI: EC: interrupt blocked", s_white, 100));
        s.Add(Line("", delay: 400));

        // Warnings
        s.Add(Line($"{Ts()} kernel: WARNING: possible circular locking dependency detected", s_yellow, 200));
        s.Add(Line($"{Ts()} kernel: INFO: task kworker/3:1:{pid} blocked for more than 120 seconds.", s_yellow, 200));
        s.Add(Line($"{Ts()} kernel: \"echo 0 > /proc/sys/kernel/hung_task_timeout_secs\" disables this message.", s_gray, 200));
        s.Add(Line("", delay: 300));

        // BUG
        s.Add(Line($"{Ts()} BUG: unable to handle kernel NULL pointer dereference at 0000000000000008", s_red, 200));
        s.Add(Line($"{Ts()} PGD 0 P4D 0", s_red, 80));
        s.Add(Line($"{Ts()} Oops: 0000 [#1] SMP PTI", s_red, 100));
        s.Add(Line($"{Ts()} CPU: 3 PID: {pid} Comm: kworker/3:1 Tainted: G           O      6.8.0-generic #42", s_white, 100));
        s.Add(Line($"{Ts()} Hardware name: QEMU Standard PC (Q35 + ICH9, 2009), BIOS 1.16.3-1", s_white, 100));
        s.Add(Line($"{Ts()} RIP: 0010:__schedule+0x3e8/0x1370", s_white, 100));
        s.Add(Line($"{Ts()} Code: 48 8b 45 00 48 85 c0 0f 84 4b 0a 00 00 48 89 c7 e8 bb cf ff ff 48 ...", s_gray, 80));
        s.Add(Line("", delay: 100));

        // Registers
        s.Add(Line($"{Ts()} RSP: 0018:ffffc900{RandHex(8)} EFLAGS: 00010046", s_gray, 60));
        s.Add(Line($"{Ts()} RAX: 0000000000000000 RBX: ffff8881{RandHex(8)} RCX: 0000000000000000", s_gray, 60));
        s.Add(Line($"{Ts()} RDX: 0000000000000001 RSI: 0000000000000086 RDI: 0000000000000000", s_gray, 60));
        s.Add(Line($"{Ts()} RBP: ffffc900{RandHex(8)} R08: 0000000000000000 R09: 0000000000000001", s_gray, 60));
        s.Add(Line($"{Ts()} R10: ffff8881{RandHex(8)} R11: 0000000000000000 R12: ffff8882{RandHex(8)}", s_gray, 60));
        s.Add(Line($"{Ts()} R13: 0000000000000000 R14: ffff8881{RandHex(8)} R15: ffff8882{RandHex(8)}", s_gray, 80));
        s.Add(Line("", delay: 80));

        // Call trace
        s.Add(Line($"{Ts()} Call Trace:", s_white, 100));
        s.Add(Line($"{Ts()}  <TASK>", s_gray, 40));
        s.Add(Line($"{Ts()}  schedule+0x5e/0xd0", s_gray, 40));
        s.Add(Line($"{Ts()}  schedule_timeout+0x25/0x130", s_gray, 40));
        s.Add(Line($"{Ts()}  wait_for_completion_timeout+0x8a/0x140", s_gray, 40));
        s.Add(Line($"{Ts()}  __flush_work+0x17a/0x2a0", s_gray, 40));
        s.Add(Line($"{Ts()}  flush_work+0x10/0x20", s_gray, 40));
        s.Add(Line($"{Ts()}  __cancel_work_timer+0x108/0x190", s_gray, 40));
        s.Add(Line($"{Ts()}  cancel_delayed_work_sync+0x13/0x20", s_gray, 40));
        s.Add(Line($"{Ts()}  </TASK>", s_gray, 60));
        s.Add(Line($"{Ts()} ---[ end trace 0000000000000000 ]---", s_white, 200));
        s.Add(Line("", delay: 300));

        // Panic
        s.Add(Glitch());
        s.Add(Line($"{Ts()}", delay: 500));
        s.Add(Line($"{Ts()} Kernel panic - not syncing: Fatal exception in interrupt", s_red, 300));
        s.Add(Line($"{Ts()} ---[ end Kernel panic - not syncing: Fatal exception in interrupt ]---", s_red, 1500));
        s.Add(Line("", delay: 800));

        // Reboot
        s.Add(new ScriptAction(Act.Clear, "", null, 1000));
        s.Add(Line("", delay: 200));
        s.Add(Line("BIOS POST: System memory 65536 MB OK", s_white, 400));
        s.Add(Line("Initializing USB Controllers... Done", s_white, 300));
        s.Add(Line("Loading GRUB2...", s_white, 500));
        s.Add(Line("", delay: 200));
        s.Add(Line("GNU GRUB version 2.06", s_white, 300));
        s.Add(Line("Booting 'Ubuntu (recovery mode)'...", s_white, 600));
        s.Add(Line("", delay: 200));
        s.Add(Line($"[    0.000000] Linux version 6.8.0-generic (buildd@lcy02-amd64-{_rng.Next(10, 99)})", s_white, 200));
        s.Add(Line($"[    0.000000] Command line: BOOT_IMAGE=/vmlinuz-6.8.0-generic root=/dev/sda1 ro recovery nomodeset", s_white, 200));
        s.Add(Line("", delay: 400));

        s.Add(Loop());
        return s;
    }

    // ── Scenario 4: Decryption Sequence ───────────────────────

    private List<ScriptAction> BuildDecryptionScript()
    {
        var s = new List<ScriptAction>();
        int blocks = _rng.Next(96, 256);
        double archiveGb = blocks * 0.02 + _rng.NextDouble() * 0.5;

        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_cyan, 30));
        s.Add(Line("  \u2551   CIPHER v3.7 \u2014 Military Grade Decryption Suite  \u2551", s_cyan, 30));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_cyan, 30));
        s.Add(Line("", delay: 300));

        s.Add(Line("[*] Loading encrypted archive: VAULT_OMEGA.enc", delay: 300));
        s.Add(Line("[*] Cipher: AES-256-GCM | PBKDF2-SHA512 (600,000 rounds)", delay: 200));
        s.Add(Line($"[*] Archive size: {archiveGb:F1} GB | {blocks} encrypted blocks", delay: 200));
        s.Add(Line("", delay: 200));

        // Key derivation
        s.Add(Line("[*] Phase 1: Key derivation...", s_yellow, 400));
        s.Add(Line($"  Deriving key from passphrase {Bar(0)}", delay: 60));
        for (int p = 10; p <= 100; p += 10)
            s.Add(Upd($"  Deriving key from passphrase {Bar(p)}", delay: 100));
        s.Add(Line($"[+] Key: {RandHex(4)} {RandHex(4)} {RandHex(4)} {RandHex(4)} {RandHex(4)} {RandHex(4)} {RandHex(4)} {RandHex(4)}", s_cyan, 200));
        s.Add(Line($"[+] IV:  {RandHex(4)} {RandHex(4)} {RandHex(4)} {RandHex(4)} {RandHex(4)} {RandHex(4)} {RandHex(4)} {RandHex(4)}", s_cyan, 200));
        s.Add(Line("", delay: 200));

        // Block decryption
        s.Add(Line("[*] Phase 2: Decrypting blocks...", s_yellow, 300));
        AddBlockDecrypt(s, 1, blocks);
        AddBlockDecrypt(s, 2, blocks);
        AddBlockDecrypt(s, 3, blocks);
        s.Add(Line("  ...", s_gray, 200));
        AddBlockDecrypt(s, blocks - 1, blocks);
        AddBlockDecrypt(s, blocks, blocks);
        s.Add(Line("", delay: 300));

        // Reveal
        s.Add(Line("[+] \u2550\u2550\u2550 DECRYPTION COMPLETE \u2550\u2550\u2550", s_cyan, 400));
        s.Add(Line("", delay: 100));
        s.Add(Line("[+] Archive contents:", s_cyan, 200));
        s.Add(Line($"  /classified/operation_midnight.pdf       (2.3 MB)", s_white, 100));
        s.Add(Line($"  /classified/agent_roster.xlsx            (847 KB)", s_white, 100));
        s.Add(Line($"  /classified/satellite_coords.json        (12 KB)", s_white, 100));
        s.Add(Line($"  /classified/intercepted_comms/           (1.8 GB)", s_white, 100));
        s.Add(Line($"  /classified/launch_codes.txt             (256 B)", s_white, 100));
        s.Add(Line($"  /classified/black_budget_FY2026.pdf      (4.1 MB)", s_white, 100));
        s.Add(Line("", delay: 200));

        s.Add(Line($"[+] Total: 6 items, {archiveGb:F1} GB decrypted", s_cyan, 200));
        s.Add(Line("[+] HMAC-SHA512 verification: PASSED", s_cyan, 150));
        s.Add(Line("[+] File integrity: ALL BLOCKS VALID", s_cyan, 150));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 5: SQL Injection ─────────────────────────────

    private List<ScriptAction> BuildSqlInjectionScript()
    {
        var s = new List<ScriptAction>();
        string target = RandIp();
        string db = _rng.Next(2) == 0 ? "MySQL 8.0.35" : "PostgreSQL 16.2";
        int tables = _rng.Next(24, 60);

        s.Add(Line("", delay: 200));
        s.Add(Line("[*] sqlmap/1.8.4 - automatic SQL injection and database takeover tool", s_cyan, 150));
        s.Add(Line($"[*] Target URL: https://{target}/api/v2/users?id=1", delay: 200));
        s.Add(Line("[*] Testing connection to the target URL...", delay: 400));
        s.Add(Line("[+] URL is reachable", delay: 200));
        s.Add(Line("", delay: 100));

        s.Add(Line("[*] Testing if the target URL is stable...", delay: 500));
        s.Add(Line("[*] Testing parameter 'id' for SQL injection...", delay: 600));
        s.Add(Line("[+] Parameter 'id' is vulnerable (generic UNION query)", s_cyan, 300));
        s.Add(Line($"[+] Back-end DBMS: {db}", s_cyan, 200));
        s.Add(Line("", delay: 200));

        // Database enumeration
        s.Add(Line("[*] Enumerating databases...", delay: 500));
        s.Add(Line("[+] Available databases:", s_cyan, 200));
        s.Add(Line("  [*] information_schema", s_white, 80));
        s.Add(Line("  [*] mysql", s_white, 80));
        s.Add(Line("  [*] production_db", s_white, 80));
        s.Add(Line("  [*] admin_portal", s_white, 80));
        s.Add(Line("  [*] customer_data", s_white, 100));
        s.Add(Line("", delay: 200));

        // Table enumeration
        s.Add(Line("[*] Enumerating tables for database 'production_db'...", delay: 600));
        s.Add(Line($"[+] {tables} tables found", s_cyan, 200));
        s.Add(Line("  users, sessions, payments, orders, products, api_keys,", s_white, 60));
        s.Add(Line("  credentials, audit_log, permissions, tokens, invoices,", s_white, 60));
        s.Add(Line("  admin_users, config, secrets, oauth_clients, migrations", s_white, 100));
        s.Add(Line("", delay: 200));

        // Column enumeration
        s.Add(Line("[*] Enumerating columns for table 'admin_users'...", delay: 500));
        s.Add(Line("[+] Table 'admin_users': id, username, password_hash, email, role, mfa_secret, last_login", s_cyan, 200));
        s.Add(Line("", delay: 200));

        // Data dump
        s.Add(Line("[*] Dumping table 'admin_users'...", s_yellow, 400));
        s.Add(Line("", delay: 100));
        s.Add(Line("  +----+----------------+----------------------------------------------+--------------------------+-------+", s_white, 60));
        s.Add(Line("  | id | username       | password_hash                                | email                    | role  |", s_white, 60));
        s.Add(Line("  +----+----------------+----------------------------------------------+--------------------------+-------+", s_white, 60));
        s.Add(Line($"  | 1  | admin          | $2b$12${RandHex(8)}...{RandHex(6)}          | admin@corp.internal      | super |", s_white, 120));
        s.Add(Line($"  | 2  | j.smith        | $2b$12${RandHex(8)}...{RandHex(6)}          | j.smith@corp.internal    | admin |", s_white, 120));
        s.Add(Line($"  | 3  | dba_backup     | $2b$12${RandHex(8)}...{RandHex(6)}          | dba@corp.internal        | super |", s_white, 120));
        s.Add(Line($"  | 4  | svc_deploy     | $2b$12${RandHex(8)}...{RandHex(6)}          | deploy@corp.internal     | admin |", s_white, 120));
        s.Add(Line($"  | 5  | root           | $2b$12${RandHex(8)}...{RandHex(6)}          | root@corp.internal       | super |", s_white, 120));
        s.Add(Line("  +----+----------------+----------------------------------------------+--------------------------+-------+", s_white, 80));
        s.Add(Line("", delay: 200));

        // API keys
        s.Add(Line("[*] Dumping table 'api_keys'...", s_yellow, 400));
        s.Add(Line($"  [+] AWS_ACCESS_KEY: AKIA{RandHex(16)}", s_red, 150));
        s.Add(Line($"  [+] AWS_SECRET_KEY: {RandHex(40)}", s_red, 150));
        s.Add(Line($"  [+] STRIPE_SK: sk_live_{RandHex(24)}", s_red, 150));
        s.Add(Line($"  [+] SENDGRID_KEY: SG.{RandHex(22)}.{RandHex(43)}", s_red, 150));
        s.Add(Line("", delay: 300));

        s.Add(Line($"[+] Fetched {_rng.Next(800, 2500)} entries from {tables} tables", s_cyan, 200));
        s.Add(Line("[+] Data saved to: /tmp/dump_production_db.csv", s_cyan, 200));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 6: Brute Force SSH ───────────────────────────

    private List<ScriptAction> BuildBruteForceScript()
    {
        var s = new List<ScriptAction>();
        string target = RandIp();
        int port = 22;
        string[] users = ["root", "admin", "ubuntu", "deploy", "jenkins", "postgres", "www-data", "oracle", "backup", "ftpuser"];
        string[] passwords = [
            "password", "123456", "admin", "root", "toor", "letmein",
            "changeme", "welcome1", "P@ssw0rd", "qwerty", "master",
            "dragon", "shadow", "monkey", "abc123", "mustang",
            "access14", "trustno1", "iloveyou", "batman",
        ];

        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_cyan, 30));
        s.Add(Line("  \u2551        HYDRA v9.5 \u2014 Network Login Cracker          \u2551", s_cyan, 30));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_cyan, 30));
        s.Add(Line("", delay: 200));

        s.Add(Line($"[*] Target: {target}:{port} (SSH)", delay: 150));
        s.Add(Line($"[*] Userlist: {users.Length} usernames", delay: 100));
        s.Add(Line($"[*] Passlist: /usr/share/wordlists/rockyou.txt ({_rng.Next(14000000, 14500000):N0} passwords)", delay: 100));
        s.Add(Line("[*] Threads: 16 | Timeout: 30s | Retry: 3", delay: 100));
        s.Add(Line($"[*] Starting brute force attack at {DateTime.Now:HH:mm:ss}...", s_yellow, 300));
        s.Add(Line("", delay: 100));

        // Fast scrolling failed attempts
        int attempts = 0;
        for (int wave = 0; wave < 6; wave++)
        {
            for (int i = 0; i < 4; i++)
            {
                attempts++;
                string user = users[_rng.Next(users.Length)];
                string pass = passwords[_rng.Next(passwords.Length)];
                s.Add(Line($"  [{attempts:D5}] {target} \u2014 ssh://{user}:{pass} \u2014 FAILED", s_gray, 25));
            }
        }

        // Partial progress
        int tried = _rng.Next(4200, 8500);
        s.Add(Line("", delay: 100));
        s.Add(Line($"[*] Status: {tried:N0} attempts | {tried / 16:N0}/s | ~{_rng.Next(20, 45)}% complete", s_yellow, 400));
        s.Add(Line("", delay: 100));

        // More fast attempts
        for (int wave = 0; wave < 4; wave++)
        {
            for (int i = 0; i < 4; i++)
            {
                attempts++;
                string user = users[_rng.Next(users.Length)];
                string pass = passwords[_rng.Next(passwords.Length)];
                s.Add(Line($"  [{attempts + tried:D5}] {target} \u2014 ssh://{user}:{pass} \u2014 FAILED", s_gray, 25));
            }
        }

        // Success
        string foundUser = "root";
        string foundPass = $"S3cur3!{_rng.Next(2020, 2027)}";
        s.Add(Line("", delay: 200));
        s.Add(Line($"  [{attempts + tried + _rng.Next(100, 500):D5}] {target} \u2014 ssh://{foundUser}:{foundPass} \u2014 SUCCESS!", s_cyan, 500));
        s.Add(Line("", delay: 300));

        s.Add(Line($"[+] VALID CREDENTIALS FOUND:", s_cyan, 200));
        s.Add(Line($"[+]   Host:     {target}:{port}", s_cyan, 100));
        s.Add(Line($"[+]   Username: {foundUser}", s_cyan, 100));
        s.Add(Line($"[+]   Password: {foundPass}", s_cyan, 100));
        s.Add(Line("", delay: 200));

        // Connection
        s.Add(Line($"[*] Verifying credentials...", delay: 500));
        s.Add(Line($"[+] SSH connection established!", s_cyan, 300));
        s.Add(Line($"# uname -a", s_white, 300));
        s.Add(Line($"Linux prod-web-{_rng.Next(1, 20):D2} 6.8.0-{_rng.Next(30, 50)}-generic #42 SMP x86_64 GNU/Linux", delay: 200));
        s.Add(Line($"# id", s_white, 300));
        s.Add(Line("uid=0(root) gid=0(root) groups=0(root)", s_red, 200));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 7: Ransomware Deployment ─────────────────────

    private List<ScriptAction> BuildRansomwareScript()
    {
        var s = new List<ScriptAction>();
        string[] extensions = [".docx", ".xlsx", ".pdf", ".pptx", ".sql", ".bak", ".vmdk", ".pst", ".mdb", ".jpg"];
        string[] dirs = [
            @"C:\Users\Administrator\Documents",
            @"C:\Users\Administrator\Desktop",
            @"C:\Shares\Finance",
            @"C:\Shares\HR_Confidential",
            @"C:\Shares\Engineering",
            @"C:\Backup\SQL_Dumps",
            @"C:\inetpub\wwwroot",
            @"D:\VMware\Production",
        ];
        string ransomId = RandHex(16);

        s.Add(Line("", delay: 200));
        s.Add(Line("[*] DARKLOCK v2.1 \u2014 Initializing...", s_red, 300));
        s.Add(Line($"[*] Campaign ID: {ransomId}", delay: 150));
        s.Add(Line("[*] Generating RSA-4096 keypair...", delay: 600));
        s.Add(Line($"[+] Public key fingerprint: {RandHex(4)}:{RandHex(4)}:{RandHex(4)}:{RandHex(4)}:{RandHex(4)}", delay: 200));
        s.Add(Line("[+] AES-256 session key derived", delay: 200));
        s.Add(Line("", delay: 200));

        // Disable recovery
        s.Add(Line("[*] Disabling recovery mechanisms...", s_yellow, 300));
        s.Add(Line("  [+] Volume Shadow Copies deleted (vssadmin)", delay: 150));
        s.Add(Line("  [+] Windows Recovery disabled (bcdedit)", delay: 150));
        s.Add(Line("  [+] System Restore points cleared", delay: 150));
        s.Add(Line("  [+] Windows Defender real-time protection disabled", delay: 150));
        s.Add(Line("  [+] Event logs cleared", delay: 200));
        s.Add(Line("", delay: 200));

        // Network propagation
        s.Add(Line("[*] Scanning local network for SMB shares...", s_yellow, 500));
        for (int i = 0; i < 4; i++)
        {
            string host = $"192.168.1.{_rng.Next(10, 200)}";
            s.Add(Line($"  [+] \\\\{host}\\C$ \u2014 accessible (admin share)", delay: 120));
        }
        s.Add(Line("", delay: 200));

        // File encryption
        s.Add(Line("[*] Starting encryption process...", s_red, 400));
        int totalEncrypted = 0;

        for (int di = 0; di < dirs.Length; di++)
        {
            int count = _rng.Next(30, 180);
            totalEncrypted += count;
            s.Add(Line($"  [*] Scanning {dirs[di]}...", s_gray, 80));
            for (int j = 0; j < 3; j++)
            {
                string ext = extensions[_rng.Next(extensions.Length)];
                string fname = s_fakeFiles[_rng.Next(s_fakeFiles.Length)];
                string encName = fname.Replace(".", $".{RandHex(6)}.");
                s.Add(Line($"    {fname} \u2192 {encName}.darklock", s_amber, 30));
            }
            s.Add(Line($"  [{count} files encrypted]", s_red, 60));
        }

        s.Add(Line("", delay: 300));
        s.Add(Line($"[+] Encryption complete: {totalEncrypted:N0} files across {dirs.Length} directories", s_red, 400));
        s.Add(Line("", delay: 500));

        // Ransom note
        s.Add(Glitch());
        s.Add(new ScriptAction(Act.Clear, "", null, 500));
        s.Add(Line("", delay: 300));
        s.Add(Line("  ================================================", s_red, 40));
        s.Add(Line("  =                                              =", s_red, 40));
        s.Add(Line("  =          YOUR FILES HAVE BEEN ENCRYPTED      =", s_red, 40));
        s.Add(Line("  =                                              =", s_red, 40));
        s.Add(Line("  ================================================", s_red, 40));
        s.Add(Line("", delay: 200));
        s.Add(Line("  All your important files have been encrypted using", s_white, 80));
        s.Add(Line("  military-grade AES-256 + RSA-4096 encryption.", s_white, 80));
        s.Add(Line("", delay: 100));
        s.Add(Line($"  Your unique ID: {ransomId}", s_yellow, 100));
        s.Add(Line("", delay: 100));
        s.Add(Line("  To decrypt your files, you must pay:", s_white, 80));
        s.Add(Line($"  {_rng.Next(5, 25)} BTC (~${_rng.Next(150, 800):N0},000 USD)", s_cyan, 200));
        s.Add(Line("", delay: 100));
        s.Add(Line($"  Bitcoin wallet: bc1q{RandHex(38).ToLowerInvariant()}", s_yellow, 100));
        s.Add(Line("", delay: 100));
        s.Add(Line("  WARNING: You have 72 hours to pay.", s_red, 150));
        s.Add(Line("  After deadline, decryption key will be destroyed.", s_red, 150));
        s.Add(Line("  DO NOT attempt to recover files manually.", s_red, 150));
        s.Add(Line("", delay: 200));
        s.Add(Line($"  Time remaining: 71:59:{_rng.Next(10, 59)}", s_amber, 200));
        s.Add(Line("", delay: 200));
        s.Add(Loop(6000));

        return s;
    }

    // ── Scenario 8: WiFi WPA2 Cracking ────────────────────────

    private List<ScriptAction> BuildWifiCrackScript()
    {
        var s = new List<ScriptAction>();
        string bssid = RandMac();
        string[] ssids = ["CorpWiFi-5G", "NETGEAR-PROD", "TP-LINK_OFFICE", "Linksys-Guest", "ASUS-INTERNAL", "FreeBox-Admin"];
        string ssid = ssids[_rng.Next(ssids.Length)];
        int channel = _rng.Next(1, 14);

        s.Add(Line("", delay: 200));
        s.Add(Line("[*] aircrack-ng 1.7 \u2014 WiFi Security Auditing Suite", s_cyan, 150));
        s.Add(Line("", delay: 200));

        // Monitor mode
        s.Add(Line("[*] Enabling monitor mode on wlan0...", delay: 300));
        s.Add(Line("[+] Monitor mode enabled: wlan0mon", s_cyan, 200));
        s.Add(Line("", delay: 200));

        // Scanning
        s.Add(Line("[*] Scanning for wireless networks...", delay: 800));
        s.Add(Line("", delay: 100));
        s.Add(Line("  BSSID              CH   ENC    CIPHER  PWR   ESSID", s_gray, 60));
        s.Add(Line("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 30));

        // Target network
        s.Add(Line($"  {bssid}   {channel,2}   WPA2   CCMP    -42   {ssid}", s_white, 100));
        // Other networks
        for (int i = 0; i < 4; i++)
        {
            string otherBssid = RandMac();
            string[] others = ["Livebox-" + RandHex(4), "FreeWifi_secure", "AndroidAP", "HP-Print-" + RandHex(2), "xfinitywifi"];
            int pwr = -_rng.Next(50, 85);
            s.Add(Line($"  {otherBssid}   {_rng.Next(1, 14),2}   WPA2   CCMP    {pwr}   {others[_rng.Next(others.Length)]}", s_gray, 60));
        }
        s.Add(Line("", delay: 300));

        // Target selection and deauth
        s.Add(Line($"[*] Target: {ssid} ({bssid}) on channel {channel}", s_yellow, 200));
        s.Add(Line("[*] Waiting for WPA2 4-way handshake...", delay: 300));
        s.Add(Line("[*] Sending deauthentication frames to force reconnection...", s_yellow, 500));
        s.Add(Line($"  [*] DeAuth \u2192 {bssid} (broadcast) \u2014 64 packets sent", delay: 200));
        s.Add(Line($"  [*] DeAuth \u2192 {bssid} (broadcast) \u2014 64 packets sent", delay: 200));
        s.Add(Line("", delay: 600));

        // Handshake capture
        s.Add(Line($"[+] WPA2 HANDSHAKE CAPTURED! ({bssid})", s_cyan, 400));
        s.Add(Line($"[+] Saved to: /tmp/capture-{RandHex(6)}.cap", s_cyan, 200));
        s.Add(Line("", delay: 300));

        // Dictionary attack
        s.Add(Line("[*] Starting dictionary attack...", s_yellow, 300));
        s.Add(Line("[*] Wordlist: /usr/share/wordlists/rockyou.txt", delay: 100));
        s.Add(Line("", delay: 200));

        // Progress with speed
        int totalKeys = 14344391;
        string[] attempts = ["password1", "iloveyou", "sunshine", "princess", "football", "charlie", "shadow", "master123", "dragon1", "monkey"];
        for (int p = 10; p <= 90; p += 10)
        {
            int tested = totalKeys * p / 100;
            int speed = _rng.Next(8000, 15000);
            string attempt = attempts[_rng.Next(attempts.Length)];
            s.Add(Line($"  {Bar(p)} {tested:N0}/{totalKeys:N0} keys | {speed:N0} k/s | current: {attempt}", delay: 120));
        }

        // Found!
        string password = $"Summer{_rng.Next(2020, 2027)}!";
        s.Add(Line("", delay: 300));
        s.Add(Line($"[+] KEY FOUND!", s_cyan, 400));
        s.Add(Line("", delay: 100));
        s.Add(Line($"  ESSID:      {ssid}", s_white, 100));
        s.Add(Line($"  BSSID:      {bssid}", s_white, 100));
        s.Add(Line($"  Encryption: WPA2-CCMP", s_white, 100));
        s.Add(Line($"  Passphrase: {password}", s_cyan, 100));
        s.Add(Line("", delay: 200));
        s.Add(Line($"[*] Tested {_rng.Next(9000000, 12000000):N0} keys in {_rng.Next(12, 35)} minutes", delay: 200));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 9: MITM / ARP Spoofing ──────────────────────

    private List<ScriptAction> BuildMitmScript()
    {
        var s = new List<ScriptAction>();
        string net = $"192.168.{_rng.Next(1, 254)}";
        string gateway = $"{net}.1";
        string attacker = $"{net}.{_rng.Next(100, 200)}";
        string gwMac = RandMac();
        int hostCount = _rng.Next(14, 30);

        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_cyan, 30));
        s.Add(Line("  \u2551    ETTERCAP 0.8.3.1 \u2014 MITM Attack Framework      \u2551", s_cyan, 30));
        s.Add(Line("  \u2551           (arp:remote/oneway)                     \u2551", s_cyan, 30));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_cyan, 30));
        s.Add(Line("", delay: 300));

        // Interface setup
        s.Add(Line($"[*] Listening on eth0 ({attacker})", delay: 150));
        s.Add(Line($"[*] Gateway: {gateway} ({gwMac})", delay: 150));
        s.Add(Line("[*] Enabling IP forwarding...", delay: 200));
        s.Add(Line("[+] IP forwarding enabled", s_cyan, 200));
        s.Add(Line("", delay: 200));

        // Host discovery
        s.Add(Line("[*] Scanning for active hosts on the LAN...", delay: 800));
        for (int i = 0; i < 6; i++)
        {
            string ip = $"{net}.{_rng.Next(2, 254)}";
            s.Add(Line($"  [+] {ip,-18} {RandMac()}", s_white, 80));
        }
        s.Add(Line($"[+] {hostCount} hosts discovered", s_cyan, 200));
        s.Add(Line("", delay: 200));

        // ARP poisoning
        s.Add(Line("[*] Starting ARP poisoning attack...", s_yellow, 400));
        for (int i = 0; i < 4; i++)
        {
            string victim = $"{net}.{_rng.Next(2, 254)}";
            s.Add(Line($"  [*] ARP poison: {victim} <\u2500\u2500> {gateway} (redirecting through us)", delay: 100));
        }
        s.Add(Line($"[+] ARP cache poisoned on {hostCount} hosts", s_cyan, 300));
        s.Add(Line("[+] All traffic now routed through {attacker}", s_cyan, 200));
        s.Add(Line("", delay: 300));

        // Sniffing phase
        s.Add(Line("[*] Sniffing traffic... (SSL Strip enabled)", s_yellow, 500));
        s.Add(Line("", delay: 200));

        // HTTP POST intercept
        string victimIp = $"{net}.{_rng.Next(10, 200)}";
        s.Add(Line($"[+] HTTP POST intercepted from {victimIp} \u2192 login.corp-portal.com", s_cyan, 300));
        s.Add(Line("  Content-Type: application/x-www-form-urlencoded", s_gray, 60));
        s.Add(Line($"  username=j.anderson&password=Pr0duct10n!{_rng.Next(2020, 2027)}", s_red, 200));
        s.Add(Line("", delay: 200));

        // FTP credentials
        string ftpVictim = $"{net}.{_rng.Next(10, 200)}";
        s.Add(Line($"[+] FTP credentials captured ({ftpVictim} \u2192 ftp.internal.corp)", s_cyan, 200));
        s.Add(Line($"  USER backup_admin", s_white, 80));
        s.Add(Line($"  PASS F1l3Srv!{_rng.Next(100, 999)}", s_red, 150));
        s.Add(Line("", delay: 200));

        // SMTP credentials
        string smtpVictim = $"{net}.{_rng.Next(10, 200)}";
        s.Add(Line($"[+] SMTP AUTH intercepted ({smtpVictim} \u2192 mail.corp.internal)", s_cyan, 200));
        s.Add(Line($"  AUTH LOGIN: {RandHex(12).ToLowerInvariant()}@corp.internal", s_white, 80));
        s.Add(Line($"  PASSWORD: Outl00k@{_rng.Next(2020, 2027)}", s_red, 150));
        s.Add(Line("", delay: 200));

        // Session cookie
        s.Add(Line($"[+] Session cookie captured from {victimIp}", s_cyan, 200));
        s.Add(Line($"  Cookie: JSESSIONID={RandHex(32).ToLowerInvariant()}", s_white, 100));
        s.Add(Line($"  Domain: admin.corp-portal.com", s_gray, 80));
        s.Add(Line("", delay: 300));

        // Summary
        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2550\u2550\u2550 INTERCEPT SUMMARY \u2550\u2550\u2550", s_cyan, 200));
        s.Add(Line($"  HTTP credentials:  3 sets", s_white, 80));
        s.Add(Line($"  FTP credentials:   1 set", s_white, 80));
        s.Add(Line($"  SMTP credentials:  1 set", s_white, 80));
        s.Add(Line($"  Session cookies:   4 captured", s_white, 80));
        s.Add(Line($"  Packets analyzed:  {_rng.Next(45000, 120000):N0}", s_white, 80));
        s.Add(Line("", delay: 200));
        s.Add(Type("CREDENTIALS HARVESTED", s_cyan));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 10: Phishing Campaign ────────────────────────

    private List<ScriptAction> BuildPhishingScript()
    {
        var s = new List<ScriptAction>();
        string campaignId = RandHex(8);
        int totalTargets = _rng.Next(450, 800);
        string domain = "hr-benefits-update.com";

        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_amber, 30));
        s.Add(Line("  \u2551      GoPhish v0.12.1 \u2014 Phishing Framework          \u2551", s_amber, 30));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_amber, 30));
        s.Add(Line("", delay: 300));

        // Campaign setup
        s.Add(Line($"[*] Campaign ID: {campaignId}", delay: 150));
        s.Add(Line($"[*] SMTP relay: smtp-{RandHex(4).ToLowerInvariant()}.{domain}", delay: 100));
        s.Add(Line($"[*] Landing page: https://{domain}/benefits-enrollment", delay: 100));
        s.Add(Line($"[*] Template: 'Annual Benefits Enrollment Reminder'", delay: 100));
        s.Add(Line($"[*] Target list: {totalTargets} corporate email addresses", delay: 150));
        s.Add(Line("[*] SPF/DKIM/DMARC bypass: enabled", delay: 100));
        s.Add(Line("", delay: 200));

        // Sending emails with progress
        s.Add(Line("[*] Launching email campaign...", s_yellow, 300));
        s.Add(Line($"  Sending emails {Bar(0)}", delay: 60));
        for (int p = 10; p <= 100; p += 10)
        {
            int sent = totalTargets * p / 100;
            s.Add(Upd($"  Sending emails {Bar(p)} ({sent}/{totalTargets})", delay: 150));
        }
        s.Add(Line($"[+] All {totalTargets} emails dispatched successfully", s_cyan, 300));
        s.Add(Line($"[+] Bounce rate: {_rng.Next(2, 8)}%", delay: 100));
        s.Add(Line("", delay: 400));

        // Live tracking stats
        s.Add(Line("[*] Monitoring campaign metrics (live)...", s_yellow, 400));
        s.Add(Line("", delay: 200));

        int opened = 0;
        int clicked = 0;
        int submitted = 0;

        for (int wave = 0; wave < 6; wave++)
        {
            opened += _rng.Next(30, 80);
            clicked += _rng.Next(10, 35);
            submitted += _rng.Next(5, 18);
            string stats = $"  Sent: {totalTargets}  |  Opened: {opened}  |  Clicked: {clicked}  |  Submitted: {submitted}";
            if (wave == 0)
                s.Add(Line(stats, s_white, 300));
            else
                s.Add(Upd(stats, s_white, 300));
        }
        s.Add(Line("", delay: 300));

        // Credential harvest table
        s.Add(Line("[+] Credentials harvested:", s_cyan, 200));
        s.Add(Line("", delay: 100));
        s.Add(Line("  +---+------------------------+-------------------+------------------+", s_white, 40));
        s.Add(Line("  | # | Email                  | Username          | Password         |", s_white, 40));
        s.Add(Line("  +---+------------------------+-------------------+------------------+", s_white, 40));

        string[] depts = ["sales", "hr", "finance", "eng", "ops", "legal", "marketing", "support"];
        string[] firstNames = ["m.johnson", "s.williams", "r.martinez", "k.brown", "l.davis", "a.wilson"];

        for (int i = 0; i < 6; i++)
        {
            string name = firstNames[i];
            string dept = depts[_rng.Next(depts.Length)];
            string pass = $"Corp{_rng.Next(2020, 2027)}!{_rng.Next(10, 99)}";
            s.Add(Line($"  | {i + 1} | {name + "@corp.com",-22} | {name,-17} | {pass,-16} |", s_white, 120));
        }
        s.Add(Line("  +---+------------------------+-------------------+------------------+", s_white, 40));
        s.Add(Line($"  ... and {submitted - 6} more", s_gray, 100));
        s.Add(Line("", delay: 300));

        // Summary
        s.Add(Line($"[+] Campaign {campaignId} complete", s_cyan, 200));
        s.Add(Line($"[+] Open rate: {opened * 100 / totalTargets}% | Click rate: {clicked * 100 / totalTargets}% | Harvest rate: {submitted * 100 / totalTargets}%", s_cyan, 200));
        s.Add(Line("", delay: 200));
        s.Add(Type($"{submitted} CREDENTIALS HARVESTED", s_cyan));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 11: Cryptomining ─────────────────────────────

    private List<ScriptAction> BuildCryptominingScript()
    {
        var s = new List<ScriptAction>();
        string wallet = $"4{RandHex(94).ToLowerInvariant()}";
        string pool = $"pool.supportxmr.com:443";
        int threads = _rng.Next(16, 64);

        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_yellow, 30));
        s.Add(Line("  \u2551         XMRig 6.21.0 \u2014 RandomX Miner              \u2551", s_yellow, 30));
        s.Add(Line("  \u2551            (built with CUDA + OpenCL)              \u2551", s_yellow, 30));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_yellow, 30));
        s.Add(Line("", delay: 300));

        // Configuration
        s.Add(Line($"[*] Wallet: {wallet[..16]}...{wallet[^8..]}", delay: 100));
        s.Add(Line($"[*] Pool: {pool} (TLS)", delay: 100));
        s.Add(Line($"[*] Threads: {threads} (auto-detected)", delay: 100));
        s.Add(Line("[*] Huge Pages: 1024/1024 (100%)", delay: 100));
        s.Add(Line("", delay: 200));

        // Pool connection
        s.Add(Line("[*] Connecting to mining pool...", delay: 500));
        s.Add(Line($"[+] Connected to {pool}", s_cyan, 200));
        s.Add(Line($"[+] Pool difficulty: {_rng.Next(100000, 500000):N0}", delay: 100));
        s.Add(Line("[+] New job received", delay: 150));
        s.Add(Line("", delay: 200));

        // GPU detection
        s.Add(Line("[*] Detecting GPU devices...", delay: 400));
        s.Add(Line("[+] GPU #0: NVIDIA GeForce RTX 4090 (24576 MB)", s_cyan, 150));
        s.Add(Line("[+] GPU #1: NVIDIA GeForce RTX 4090 (24576 MB)", s_cyan, 150));
        s.Add(Line("[+] CUDA 12.4 runtime initialized", delay: 100));
        s.Add(Line("", delay: 300));

        // Mining with live stats
        s.Add(Line("[*] Mining started...", s_yellow, 400));
        s.Add(Line("", delay: 200));

        double hashrate = 18500.0 + _rng.NextDouble() * 3000;
        int shares = 0;
        double earnings = 0.0;

        for (int tick = 0; tick < 8; tick++)
        {
            double hr = hashrate + (_rng.NextDouble() - 0.5) * 800;
            int temp0 = _rng.Next(62, 78);
            int temp1 = _rng.Next(60, 76);
            int power0 = _rng.Next(280, 350);
            int power1 = _rng.Next(270, 340);
            shares += _rng.Next(2, 8);
            earnings += _rng.NextDouble() * 0.0004;

            string line = $"  [H/s] {hr:F0}  |  GPU0: {temp0}\u00b0C/{power0}W  GPU1: {temp1}\u00b0C/{power1}W  |  Shares: {shares}";
            if (tick == 0)
                s.Add(Line(line, s_white, 250));
            else
                s.Add(Upd(line, s_white, 250));
        }
        s.Add(Line("", delay: 100));

        // Share accepted messages
        for (int i = 0; i < 5; i++)
        {
            shares++;
            s.Add(Line($"  [{DateTime.Now.AddMinutes(i):HH:mm:ss}] Share #{shares} accepted (diff {_rng.Next(100000, 400000):N0})", s_cyan, 150));
        }
        s.Add(Line("", delay: 200));

        // Earnings estimate
        s.Add(Line("  \u2500\u2500\u2500 Earnings Estimate \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 150));
        s.Add(Line($"  Hashrate (avg): {hashrate:F0} H/s", s_white, 80));
        s.Add(Line($"  Shares accepted: {shares}", s_white, 80));
        s.Add(Line($"  Est. daily: {earnings * 24:F6} XMR (~${earnings * 24 * 150:F2} USD)", s_yellow, 100));
        s.Add(Line($"  Est. monthly: {earnings * 24 * 30:F4} XMR (~${earnings * 24 * 30 * 150:F2} USD)", s_yellow, 100));
        s.Add(Line($"  Power draw: ~{_rng.Next(600, 720)}W total", s_white, 80));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 12: Active Directory Attack ──────────────────

    private List<ScriptAction> BuildAdAttackScript()
    {
        var s = new List<ScriptAction>();
        string domain = "CORP.INTERNAL";
        string dcHost = $"DC01.{domain}";
        string dcIp = $"10.{_rng.Next(1, 200)}.{_rng.Next(1, 200)}.{_rng.Next(1, 10)}";

        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_red, 30));
        s.Add(Line("  \u2551    BloodHound CE 5.0 \u2014 AD Attack Path Engine     \u2551", s_red, 30));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_red, 30));
        s.Add(Line("", delay: 300));

        // Domain enumeration
        s.Add(Line($"[*] Target domain: {domain}", delay: 150));
        s.Add(Line($"[*] Domain controller: {dcHost} ({dcIp})", delay: 150));
        s.Add(Line("[*] Starting SharpHound collection...", delay: 600));
        s.Add(Line("", delay: 200));

        int users = _rng.Next(1200, 3500);
        int groups = _rng.Next(150, 400);
        int computers = _rng.Next(300, 900);
        s.Add(Line($"[+] Enumerated {users} users", s_white, 100));
        s.Add(Line($"[+] Enumerated {groups} groups", s_white, 100));
        s.Add(Line($"[+] Enumerated {computers} computers", s_white, 100));
        s.Add(Line($"[+] Enumerated {_rng.Next(40, 100)} GPOs", s_white, 100));
        s.Add(Line($"[+] Enumerated {_rng.Next(500, 2000)} sessions", s_white, 100));
        s.Add(Line("[+] Collection complete. Uploading to Neo4j...", s_cyan, 400));
        s.Add(Line("", delay: 300));

        // Attack path analysis
        s.Add(Line("[*] Analyzing attack paths...", s_yellow, 600));
        s.Add(Line($"[!] Found {_rng.Next(12, 35)} paths to Domain Admin", s_red, 200));
        s.Add(Line("[!] Shortest path: 3 hops via Kerberoasting", s_red, 200));
        s.Add(Line("", delay: 300));

        // Kerberoasting
        s.Add(Line("\u2500\u2500\u2500 Phase 1: Kerberoasting \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 200));
        s.Add(Line("[*] Requesting TGS tickets for SPNs...", delay: 400));

        for (int i = 0; i < 4; i++)
        {
            string svc = s_serviceAccounts[_rng.Next(s_serviceAccounts.Length)];
            s.Add(Line($"  [+] TGS for {svc}@{domain} \u2014 RC4_HMAC_MD5", s_white, 120));
        }
        s.Add(Line("", delay: 200));

        s.Add(Line("[*] Cracking TGS tickets with hashcat (mode 13100)...", s_yellow, 500));
        s.Add(Line($"  [+] {s_serviceAccounts[0]}:Summer{_rng.Next(2020, 2027)}!", s_red, 200));
        s.Add(Line($"  [+] {s_serviceAccounts[2]}:Backup@{_rng.Next(2020, 2027)}", s_red, 200));
        s.Add(Line($"  [+] {s_serviceAccounts[4]}:Shar3p0int!{_rng.Next(10, 99)}", s_red, 200));
        s.Add(Line($"[+] 3/{s_serviceAccounts.Length} service account passwords cracked", s_cyan, 300));
        s.Add(Line("", delay: 300));

        // DCSync
        s.Add(Line("\u2500\u2500\u2500 Phase 2: DCSync Attack \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 200));
        s.Add(Glitch());
        s.Add(Line($"[*] Performing DCSync against {dcHost}...", s_yellow, 600));
        s.Add(Line("[*] Replicating NTDS.dit via DRS...", delay: 400));
        s.Add(Line("", delay: 200));

        s.Add(Line("[+] NTLM Hashes extracted:", s_cyan, 200));
        s.Add(Line($"  Administrator:500:{RandHex(32).ToLowerInvariant()}:{RandHex(32).ToLowerInvariant()}:::", s_red, 120));
        s.Add(Line($"  krbtgt:502:{RandHex(32).ToLowerInvariant()}:{RandHex(32).ToLowerInvariant()}:::", s_red, 120));
        s.Add(Line($"  {s_serviceAccounts[1]}:1103:{RandHex(32).ToLowerInvariant()}:{RandHex(32).ToLowerInvariant()}:::", s_red, 120));
        s.Add(Line($"  {s_serviceAccounts[3]}:1105:{RandHex(32).ToLowerInvariant()}:{RandHex(32).ToLowerInvariant()}:::", s_red, 120));
        s.Add(Line($"  ... {users - 4} more hashes", s_gray, 100));
        s.Add(Line("", delay: 300));

        // Golden Ticket
        s.Add(Line("\u2500\u2500\u2500 Phase 3: Golden Ticket \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 200));
        s.Add(Line("[*] Forging Golden Ticket with krbtgt hash...", delay: 500));
        s.Add(Line($"[+] User: Administrator", s_white, 100));
        s.Add(Line($"[+] Domain: {domain}", s_white, 100));
        s.Add(Line($"[+] SID: S-1-5-21-{_rng.Next(100000000, 999999999)}-{_rng.Next(100000000, 999999999)}-{_rng.Next(100000000, 999999999)}", s_white, 100));
        s.Add(Line($"[+] Groups: 512 513 518 519 520", s_white, 100));
        s.Add(Line("[+] Ticket lifetime: 10 years", s_white, 100));
        s.Add(Line($"[+] Golden Ticket saved: ticket.kirbi ({_rng.Next(1200, 1800)} bytes)", s_cyan, 300));
        s.Add(Line("", delay: 200));

        s.Add(Line("[+] Injecting ticket into current session...", delay: 400));
        s.Add(Line("[+] klist: Ticket for krbtgt/CORP.INTERNAL@CORP.INTERNAL", s_cyan, 200));
        s.Add(Line("", delay: 200));
        s.Add(Type("DOMAIN ADMIN ACHIEVED", s_red));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 13: Firmware Backdoor ────────────────────────

    private List<ScriptAction> BuildFirmwareScript()
    {
        var s = new List<ScriptAction>();
        string chipset = "Intel Q370 (SPI Flash)";
        int firmwareSize = _rng.Next(16, 32);
        string deviceModel = $"ProLiant DL380 Gen{_rng.Next(10, 12)}";

        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_amber, 30));
        s.Add(Line("  \u2551   FIRMWARE IMPLANT TOOLKIT v2.4 \u2014 SPI Flash     \u2551", s_amber, 30));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_amber, 30));
        s.Add(Line("", delay: 300));

        // Target info
        s.Add(Line($"[*] Target device: {deviceModel}", delay: 150));
        s.Add(Line($"[*] Chipset: {chipset}", delay: 100));
        s.Add(Line($"[*] Flash size: {firmwareSize} MB", delay: 100));
        s.Add(Line("[*] Connection: JTAG via Bus Pirate v4", delay: 100));
        s.Add(Line("", delay: 200));

        // Firmware dump
        s.Add(Line("[*] Phase 1: Dumping SPI firmware...", s_yellow, 400));
        s.Add(Line($"  Reading SPI flash {Bar(0)}", delay: 60));
        for (int p = 10; p <= 100; p += 10)
        {
            int mb = firmwareSize * p / 100;
            s.Add(Upd($"  Reading SPI flash {Bar(p)} ({mb}/{firmwareSize} MB)", delay: 200));
        }
        s.Add(Line($"[+] Firmware dumped: firmware_original.bin ({firmwareSize} MB)", s_cyan, 300));
        s.Add(Line($"[+] SHA-256: {RandHex(64).ToLowerInvariant()}", delay: 100));
        s.Add(Line("", delay: 300));

        // Binwalk analysis
        s.Add(Line("[*] Phase 2: Analyzing firmware structure...", s_yellow, 400));
        s.Add(Line("[*] Running binwalk extraction...", delay: 500));
        s.Add(Line("", delay: 100));

        s.Add(Line("  DECIMAL       HEXADECIMAL     DESCRIPTION", s_gray, 60));
        s.Add(Line("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 30));
        s.Add(Line($"  0             0x{0:X8}      Intel Flash Descriptor (IFD)", s_white, 80));
        s.Add(Line($"  4096          0x{4096:X8}      BIOS Region (UEFI)", s_white, 80));
        s.Add(Line($"  {_rng.Next(1000000, 3000000),-13} 0x{_rng.Next(0x100000, 0x300000):X8}      Intel Management Engine (ME)", s_white, 80));
        s.Add(Line($"  {_rng.Next(4000000, 8000000),-13} 0x{_rng.Next(0x400000, 0x800000):X8}      GbE Region", s_white, 80));
        s.Add(Line($"  {_rng.Next(8000000, 12000000),-13} 0x{_rng.Next(0x800000, 0xC00000):X8}      Platform Data", s_white, 80));
        s.Add(Line("", delay: 200));

        s.Add(Line("[+] UEFI DXE modules extracted: 47 drivers", s_cyan, 200));
        s.Add(Line("[+] Identified authentication module: SecurityCheckDxe.efi", s_cyan, 200));
        s.Add(Line("", delay: 300));

        // Patching
        s.Add(Line("[*] Phase 3: Patching authentication routine...", s_yellow, 400));
        s.Add(Line("[*] Disassembling SecurityCheckDxe.efi...", delay: 400));
        s.Add(Line("", delay: 100));

        string authAddr = $"0x{_rng.Next(0x1000, 0x9FFF):X4}";
        string patchAddr = $"0x{_rng.Next(0x1000, 0x9FFF):X4}";
        s.Add(Line($"  {authAddr}: 48 83 EC 28      sub  rsp, 0x28", s_gray, 60));
        s.Add(Line($"  {patchAddr}: 48 8B 4C 24 30  mov  rcx, [rsp+0x30]", s_gray, 60));
        s.Add(Line($"  {patchAddr}: E8 {RandHex(2)} {RandHex(2)} 00 00  call VerifyPassword", s_gray, 60));
        s.Add(Line($"  {patchAddr}: 85 C0            test eax, eax", s_gray, 60));
        s.Add(Line($"  {patchAddr}: 75 12            jne  AuthFailed", s_gray, 80));
        s.Add(Line("", delay: 200));

        s.Add(Line("[*] Applying NOPs to authentication check...", s_red, 300));
        s.Add(Line($"  Patching {patchAddr}: 75 12 \u2192 90 90 (JNE \u2192 NOP NOP)", s_red, 200));
        s.Add(Line("[+] Authentication bypass patched", s_cyan, 200));
        s.Add(Line("", delay: 300));

        // Reverse shell injection
        s.Add(Line("[*] Phase 4: Injecting reverse shell DXE driver...", s_yellow, 400));
        s.Add(Line($"[*] Payload: EFI_SHELL_CONNECT (callback to {RandIp()}:8443)", delay: 200));
        s.Add(Line("[*] Inserting into DXE dispatch list...", delay: 300));
        s.Add(Line($"[+] Implant GUID: {RandHex(8)}-{RandHex(4)}-{RandHex(4)}-{RandHex(4)}-{RandHex(12)}", s_cyan, 200));
        s.Add(Line("[+] DXE driver injected at volume offset 0x3E8000", s_cyan, 200));
        s.Add(Line("", delay: 300));

        // Reflash
        s.Add(Line("[*] Phase 5: Reflashing modified firmware...", s_red, 400));
        s.Add(Line($"  Writing SPI flash {Bar(0)}", delay: 60));
        for (int p = 10; p <= 100; p += 10)
        {
            int mb = firmwareSize * p / 100;
            s.Add(Upd($"  Writing SPI flash {Bar(p)} ({mb}/{firmwareSize} MB)", delay: 250));
        }
        s.Add(Line("[+] Firmware reflash complete", s_cyan, 300));
        s.Add(Line($"[+] New SHA-256: {RandHex(64).ToLowerInvariant()}", delay: 100));
        s.Add(Line("", delay: 200));

        // Verification
        s.Add(Line("[*] Verifying implant activation...", delay: 500));
        s.Add(Line("[+] Read-back verify: PASSED", s_cyan, 150));
        s.Add(Line("[+] Secure Boot bypass: CONFIRMED", s_cyan, 150));
        s.Add(Line("[+] Implant persistence: SURVIVES OS REINSTALL", s_red, 200));
        s.Add(Line("", delay: 200));
        s.Add(Type("BACKDOOR VERIFIED", s_red));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 14: Supply Chain Attack ──────────────────────

    private List<ScriptAction> BuildSupplyChainScript()
    {
        var s = new List<ScriptAction>();
        string packageName = $"lodash-utils-{_rng.Next(2, 9)}";
        string originalPackage = "lodash-utils";
        string c2Domain = $"cdn-analytics-{RandHex(4).ToLowerInvariant()}.com";

        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_amber, 30));
        s.Add(Line("  \u2551   SUPPLY CHAIN IMPLANT \u2014 npm Package Attack     \u2551", s_amber, 30));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_amber, 30));
        s.Add(Line("", delay: 300));

        // Clone
        s.Add(Line($"[*] Phase 1: Cloning target package '{originalPackage}'...", delay: 300));
        s.Add(Line($"[+] Downloaded {originalPackage}@4.17.21 (source)", s_white, 150));
        s.Add(Line($"[*] Creating typosquatted clone: {packageName}", delay: 200));
        s.Add(Line($"[+] Package structure replicated", delay: 150));
        s.Add(Line("", delay: 200));

        // Payload injection
        s.Add(Line("[*] Phase 2: Injecting payload into postinstall hook...", s_yellow, 400));
        s.Add(Line("  [*] Modifying package.json: scripts.postinstall", delay: 150));
        s.Add(Line($"  [*] Payload: reverse shell to {c2Domain}:443", delay: 150));
        s.Add(Line("  [*] Obfuscation: base64 + eval chain (3 layers)", delay: 150));
        s.Add(Line("  [*] Anti-analysis: process.env.CI detection (skip in CI)", delay: 150));
        s.Add(Line("[+] Payload injected and obfuscated", s_cyan, 200));
        s.Add(Line("", delay: 200));

        // Version bump & publish
        s.Add(Line("[*] Phase 3: Publishing to npm registry...", s_yellow, 300));
        s.Add(Line($"[*] Version: 4.17.22 (one patch above legitimate)", delay: 150));
        s.Add(Line("[*] npm publish --access public", delay: 400));
        s.Add(Line($"+ {packageName}@4.17.22", s_cyan, 200));
        s.Add(Line("[+] Published successfully to npm registry", s_cyan, 200));
        s.Add(Line("", delay: 300));

        // Install tracking
        s.Add(Line("[*] Phase 4: Monitoring installation telemetry...", s_yellow, 400));
        s.Add(Line("", delay: 200));

        int installs = 0;
        for (int wave = 0; wave < 8; wave++)
        {
            installs += _rng.Next(15, 80);
            string line = $"  [{DateTime.Now.AddHours(wave):yyyy-MM-dd HH:mm}] Installs: {installs:N0} | C2 callbacks: {installs * 85 / 100}";
            if (wave == 0)
                s.Add(Line(line, s_white, 250));
            else
                s.Add(Upd(line, s_white, 250));
        }
        s.Add(Line("", delay: 200));

        // Dependent packages
        s.Add(Line("[+] Packages depending on our implant:", s_cyan, 200));
        string[] depPkgs = [
            "enterprise-dashboard", "react-admin-panel", "user-auth-service",
            "data-pipeline-core", "api-gateway-utils", "cms-backend-lib",
        ];
        for (int i = 0; i < depPkgs.Length; i++)
        {
            s.Add(Line($"  [{i + 1}] {depPkgs[i]}@{_rng.Next(1, 5)}.{_rng.Next(0, 20)}.{_rng.Next(0, 50)} ({_rng.Next(500, 15000):N0} weekly downloads)", s_white, 100));
        }
        s.Add(Line("", delay: 300));

        // Compromised hosts phoning home
        s.Add(Line("[*] Compromised hosts phoning home:", s_yellow, 200));
        int totalHosts = 0;
        for (int i = 0; i < 5; i++)
        {
            totalHosts += _rng.Next(8, 35);
            s.Add(Line($"  [{DateTime.Now.AddMinutes(i * 15):HH:mm:ss}] {RandIp()} \u2014 {depPkgs[_rng.Next(depPkgs.Length)]} \u2014 env: {(_rng.Next(2) == 0 ? "production" : "staging")}", s_white, 120));
        }
        s.Add(Line("", delay: 200));

        s.Add(Line($"[+] Total installs: {installs:N0} | Active C2 sessions: {totalHosts}", s_cyan, 200));
        s.Add(Line("", delay: 200));
        s.Add(Type($"{totalHosts} HOSTS COMPROMISED", s_red));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Scenario 15: SCADA / ICS Attack ──────────────────────

    private List<ScriptAction> BuildScadaScript()
    {
        var s = new List<ScriptAction>();
        string net = $"10.{_rng.Next(100, 200)}.{_rng.Next(1, 50)}";

        s.Add(Line("", delay: 200));
        s.Add(Line("  \u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557", s_red, 30));
        s.Add(Line("  \u2551   MODBUS SCANNER v1.3 \u2014 SCADA/ICS Exploitation  \u2551", s_red, 30));
        s.Add(Line("  \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d", s_red, 30));
        s.Add(Line("", delay: 300));

        // Network scan
        s.Add(Line($"[*] Scanning OT network: {net}.0/24 (Modbus TCP/502)", delay: 200));
        s.Add(Line("[*] Protocol: Modbus TCP | Ports: 502, 102, 44818", delay: 100));
        s.Add(Line("[*] Starting PLC discovery...", delay: 600));
        s.Add(Line("", delay: 200));

        // PLC discovery
        string plc1 = $"{net}.{_rng.Next(10, 30)}";
        string plc2 = $"{net}.{_rng.Next(31, 60)}";
        string plc3 = $"{net}.{_rng.Next(61, 90)}";

        s.Add(Line($"[+] PLC found: {plc1} \u2014 Siemens S7-1500 (FW v2.9.7)", s_cyan, 150));
        s.Add(Line($"    Unit ID: 1 | Protocol: S7comm | Port: 102", s_gray, 80));
        s.Add(Line($"[+] PLC found: {plc2} \u2014 Allen-Bradley ControlLogix 5580 (FW v33.011)", s_cyan, 150));
        s.Add(Line($"    Unit ID: 2 | Protocol: EtherNet/IP | Port: 44818", s_gray, 80));
        s.Add(Line($"[+] PLC found: {plc3} \u2014 Schneider Modicon M340 (FW v3.60)", s_cyan, 150));
        s.Add(Line($"    Unit ID: 3 | Protocol: Modbus TCP | Port: 502", s_gray, 80));
        s.Add(Line($"[+] HMI found: {net}.{_rng.Next(91, 120)} \u2014 Wonderware InTouch 2020 R2", s_cyan, 150));
        s.Add(Line($"[+] Historian: {net}.{_rng.Next(121, 150)} \u2014 OSIsoft PI Server", s_cyan, 150));
        s.Add(Line("", delay: 300));

        // Sensor register readings
        s.Add(Line("[*] Reading process registers from PLCs...", s_yellow, 400));
        s.Add(Line("", delay: 200));

        s.Add(Line("  Register Map (Modbus Holding Registers):", s_gray, 100));
        s.Add(Line("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", s_gray, 30));

        int pressure = _rng.Next(180, 220);
        int flow = _rng.Next(450, 550);
        int temp = _rng.Next(340, 380);
        int level = _rng.Next(70, 85);

        s.Add(Line($"  HR40001  Reactor Pressure    {pressure} PSI    (normal: 150-250)", s_white, 100));
        s.Add(Line($"  HR40002  Coolant Flow Rate   {flow} GPM    (normal: 400-600)", s_white, 100));
        s.Add(Line($"  HR40003  Core Temperature    {temp}\u00b0F     (normal: 300-400)", s_white, 100));
        s.Add(Line($"  HR40004  Tank Level          {level}%       (normal: 60-90)", s_white, 100));
        s.Add(Line($"  HR40005  Safety Interlock    1 (ARMED)    (0=DISARMED)", s_white, 100));
        s.Add(Line($"  HR40006  Emergency Shutdown  0 (READY)    (1=TRIGGERED)", s_white, 100));
        s.Add(Line("", delay: 400));

        // Value manipulation
        s.Add(Line("[*] Phase 2: Manipulating process values...", s_red, 400));
        s.Add(Line("", delay: 200));

        s.Add(Line($"[!] Writing HR40001 (Pressure): {pressure} \u2192 {pressure + 180} PSI", s_red, 200));
        s.Add(Line($"[!] Writing HR40002 (Flow Rate): {flow} \u2192 {flow - 350} GPM", s_red, 200));
        s.Add(Line("[+] Register writes confirmed by PLC", s_cyan, 200));
        s.Add(Line("", delay: 300));

        // Safety system disable
        s.Add(Line("[*] Phase 3: Disabling Safety Instrumented System (SIS)...", s_red, 400));
        s.Add(Glitch());
        s.Add(Line("[!] Writing HR40005 (Safety Interlock): 1 \u2192 0 (DISARMED)", s_red, 300));
        s.Add(Line("[+] Safety interlock disabled", s_red, 200));
        s.Add(Line("", delay: 200));

        // Alarm suppression
        s.Add(Line("[*] Suppressing HMI alarms...", s_yellow, 400));
        s.Add(Line("  [+] ALARM_HIGH_PRESSURE \u2014 suppressed", delay: 100));
        s.Add(Line("  [+] ALARM_LOW_FLOW \u2014 suppressed", delay: 100));
        s.Add(Line("  [+] ALARM_SAFETY_BYPASS \u2014 suppressed", delay: 100));
        s.Add(Line("  [+] ALARM_TEMP_CRITICAL \u2014 suppressed", delay: 100));
        s.Add(Line("[+] All critical alarms masked on HMI", s_cyan, 200));
        s.Add(Line("", delay: 300));

        // Temperature escalation
        s.Add(Line("[*] Monitoring process deviation...", s_yellow, 400));
        s.Add(Line("", delay: 200));

        int risingTemp = temp;
        for (int i = 0; i < 6; i++)
        {
            risingTemp += _rng.Next(15, 40);
            int risingPressure = pressure + 180 + i * _rng.Next(20, 50);
            SolidColorBrush tempColor = risingTemp > 500 ? s_red : (risingTemp > 420 ? s_amber : s_white);
            string line = $"  [{i * 5}s] Pressure: {risingPressure} PSI | Temp: {risingTemp}\u00b0F | Flow: {flow - 350 + _rng.Next(-10, 10)} GPM";
            if (i == 0)
                s.Add(Line(line, tempColor, 300));
            else
                s.Add(Upd(line, tempColor, 300));
        }
        s.Add(Line("", delay: 200));

        s.Add(Line($"[!] CRITICAL: Core temperature exceeding maximum ({risingTemp}\u00b0F > 500\u00b0F)", s_red, 300));
        s.Add(Line("[!] CRITICAL: Pressure relief valve NOT responding (SIS disabled)", s_red, 300));
        s.Add(Line("", delay: 200));
        s.Add(Type("SAFETY SYSTEMS BYPASSED", s_red));
        s.Add(Line("", delay: 200));
        s.Add(Loop());

        return s;
    }

    // ── Localization ──────────────────────────────────────────

    private void ApplyLocalization()
    {
        LblSpeed.Text = L("ToolHackerSimSpeed");
        BtnStartStop.Content = _isRunning
            ? $"\u25A0 {L("ToolHackerSimBtnStop")}"
            : $"\u25B6 {L("ToolHackerSimBtnStart")}";
        LblScenario.Text = L("ToolHackerSimLblScenario");
        LblSearch.Text = L("ToolHackerSimLblSearch");
        LblCategory.Text = L("ToolHackerSimLblCategory");
        LblRealism.Text = L("ToolHackerSimLblRealism");
        LblPlaylist.Text = L("ToolHackerSimLblPlaylist");
        TxtScenarioSearch.ToolTip = L("ToolHackerSimSearchTip");
        PopulateFilterControls();
        PopulatePlaylistControl();
        UpdateFavoriteButton();
        UpdateScenarioLabel();
        UpdatePlaybackControls();
    }

    private string L(string key) => _localizer?[key] ?? key;
}
