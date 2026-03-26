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
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Heimdall.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Heimdall.App.Views.Tools;

public partial class HackerSimulatorView
{
    private sealed class TranscriptSection
    {
        public required string ScenarioId { get; init; }
        public required string ScenarioTitle { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
        public List<string> Lines { get; } = [];
    }

    private const int SeedMin = 100000;
    private const int SeedMax = 999999;
    private const double VintageBaseOpacity = 0.16;
    private const double VintageFlickerMin = 0.11;
    private const double VintageFlickerRange = 0.08;
    private const int VintageFlickerIntervalMs = 130;

    private readonly Random _uiRng = new();
    private ConfigManager? _configManager;
    private readonly List<TranscriptSection> _transcriptSections = [];
    private TranscriptSection? _activeTranscriptSection;
    private int? _typingTranscriptLineIndex;
    private DispatcherTimer? _vintageTimer;
    private bool _vintageMonitorEnabled;
    private int _sessionSeed;
    private int _scenarioExecutionCounter;

    private void LoadSimulatorPreferences()
    {
        _configManager = (Application.Current as App)?.Services?.GetService<ConfigManager>();
        if (_configManager is null)
            return;

        _ = LoadSimulatorPreferencesAsync();
    }

    private async Task LoadSimulatorPreferencesAsync()
    {
        try
        {
            var settings = await _configManager!.LoadSettingsAsync();
            _favoriteScenarioIds.Clear();
            foreach (string id in settings.HackerSimulatorFavoriteScenarioIds)
                _favoriteScenarioIds.Add(id);

            if (!string.IsNullOrWhiteSpace(settings.HackerSimulatorLastScenarioId))
                _currentScenarioId = settings.HackerSimulatorLastScenarioId;

            _selectedPlaylistId = string.IsNullOrWhiteSpace(settings.HackerSimulatorPlaylistId)
                ? null
                : settings.HackerSimulatorPlaylistId;
            _randomMode = settings.HackerSimulatorRandomMode;
            _vintageMonitorEnabled = settings.HackerSimulatorVintageMonitorEnabled;
        }
        catch (Exception)
        {
            _favoriteScenarioIds.Clear();
            _selectedPlaylistId = null;
            _randomMode = false;
            _vintageMonitorEnabled = false;
        }
    }

    private void PersistSimulatorPreferences()
    {
        if (_configManager is null)
            return;

        try
        {
            List<string> favorites = _favoriteScenarioIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            string currentScenarioId = _currentScenarioId;
            string? playlistId = _selectedPlaylistId;
            bool randomMode = _randomMode;
            bool vintageMode = _vintageMonitorEnabled;

            _ = _configManager.MergeSettingAsync(settings =>
            {
                settings.HackerSimulatorFavoriteScenarioIds = favorites;
                settings.HackerSimulatorLastScenarioId = currentScenarioId;
                settings.HackerSimulatorPlaylistId = playlistId;
                settings.HackerSimulatorRandomMode = randomMode;
                settings.HackerSimulatorVintageMonitorEnabled = vintageMode;
            });
        }
        catch (Exception)
        {
            // Non-critical simulator preferences.
        }
    }

    private void PopulatePlaylistControl()
    {
        if (CmbPlaylist == null)
            return;

        EnsureExternalCatalogLoaded();

        var items = new List<ScenarioPlaylistPickerItem>
        {
            new("none", L("ToolHackerSimNone"), null),
        };

        items.AddRange(_playlistDefinitions.Select(playlist =>
            new ScenarioPlaylistPickerItem(playlist.Id, Tx(playlist.Title), playlist)));

        _suppressPlaylistEvents = true;
        CmbPlaylist.ItemsSource = items;
        CmbPlaylist.SelectedItem = items.FirstOrDefault(i => string.Equals(i.Playlist?.Id, _selectedPlaylistId, StringComparison.OrdinalIgnoreCase))
            ?? items[0];
        _suppressPlaylistEvents = false;
    }

    private ScenarioPlaylistDefinition? GetSelectedPlaylist()
        => _playlistDefinitions.FirstOrDefault(p => string.Equals(p.Id, _selectedPlaylistId, StringComparison.OrdinalIgnoreCase));

    private void ClearPlaylistSelection(bool persist = true)
    {
        if (string.IsNullOrWhiteSpace(_selectedPlaylistId))
            return;

        _selectedPlaylistId = null;
        _playlistCursor = 0;
        PopulatePlaylistControl();
        if (persist)
            PersistSimulatorPreferences();
    }

    private void ResetRunSession(bool reuseSeed)
    {
        if (!reuseSeed || _sessionSeed == 0)
            _sessionSeed = _uiRng.Next(SeedMin, SeedMax);

        _scenarioExecutionCounter = 0;
        ResetTranscript();
        ResetPlaylistToStart();
        UpdatePlaybackControls();
    }

    private void PrepareRandomForScenarioExecution()
    {
        _scenarioExecutionCounter++;
        _rng = new Random(HashCode.Combine(_sessionSeed, _scenarioExecutionCounter, _currentScenarioId));
        UpdatePlaybackControls();
    }

    private void ResetPlaylistToStart()
    {
        EnsureScenarioCatalog();

        var playlist = GetSelectedPlaylist();
        if (playlist is null)
        {
            _playlistCursor = 0;
            return;
        }

        for (int i = 0; i < playlist.ScenarioIds.Count; i++)
        {
            if (_scenarioDefinitions.Any(s => string.Equals(s.Id, playlist.ScenarioIds[i], StringComparison.OrdinalIgnoreCase)))
            {
                _playlistCursor = i;
                _currentScenarioId = playlist.ScenarioIds[i];
                SyncScenarioSelection();
                return;
            }
        }

        _playlistCursor = 0;
    }

    private void AdvancePlaylistScenario()
    {
        EnsureScenarioCatalog();

        var playlist = GetSelectedPlaylist();
        if (playlist is null || playlist.ScenarioIds.Count == 0)
            return;

        for (int offset = 1; offset <= playlist.ScenarioIds.Count; offset++)
        {
            int index = (_playlistCursor + offset) % playlist.ScenarioIds.Count;
            string candidate = playlist.ScenarioIds[index];
            if (_scenarioDefinitions.Any(s => string.Equals(s.Id, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                _playlistCursor = index;
                _currentScenarioId = candidate;
                SyncScenarioSelection();
                PersistSimulatorPreferences();
                return;
            }
        }
    }

    private void UpdatePlaybackControls()
    {
        if (LblSeed != null)
            LblSeed.Text = L("ToolHackerSimSeed");

        if (LblSeedValue != null)
            LblSeedValue.Text = _sessionSeed > 0 ? _sessionSeed.ToString(CultureInfo.InvariantCulture) : "--";

        if (BtnReplaySeed != null)
        {
            BtnReplaySeed.Content = L("ToolHackerSimReplaySeed");
            BtnReplaySeed.IsEnabled = _sessionSeed > 0;
        }

        if (BtnExportTranscript != null)
        {
            BtnExportTranscript.Content = L("ToolHackerSimExportTranscript");
            BtnExportTranscript.IsEnabled = _transcriptSections.Any(s => s.Lines.Count > 0);
        }

        if (ChkVintageMonitor != null)
        {
            ChkVintageMonitor.Content = L("ToolHackerSimVintageMonitor");
            ChkVintageMonitor.IsChecked = _vintageMonitorEnabled;
        }
    }

    private void ResetTranscript()
    {
        _transcriptSections.Clear();
        _activeTranscriptSection = null;
        _typingTranscriptLineIndex = null;
    }

    private void BeginTranscriptSection(ScenarioDefinition scenario)
    {
        _activeTranscriptSection = new TranscriptSection
        {
            ScenarioId = scenario.Id,
            ScenarioTitle = GetScenarioTitle(scenario),
            StartedAt = DateTimeOffset.Now,
        };
        _transcriptSections.Add(_activeTranscriptSection);
        _typingTranscriptLineIndex = null;
        UpdatePlaybackControls();
    }

    private void TranscriptAppendLine(string text)
    {
        if (_activeTranscriptSection is null)
            BeginTranscriptSection(GetCurrentScenario());

        _activeTranscriptSection!.Lines.Add(text);
        UpdatePlaybackControls();
    }

    private void TranscriptUpdateLastLine(string text)
    {
        if (_activeTranscriptSection is null || _activeTranscriptSection.Lines.Count == 0)
        {
            TranscriptAppendLine(text);
            return;
        }

        _activeTranscriptSection.Lines[^1] = text;
    }

    private void TranscriptBeginTypingLine()
    {
        if (_activeTranscriptSection is null)
            BeginTranscriptSection(GetCurrentScenario());

        _activeTranscriptSection!.Lines.Add(string.Empty);
        _typingTranscriptLineIndex = _activeTranscriptSection.Lines.Count - 1;
        UpdatePlaybackControls();
    }

    private void TranscriptUpdateTypingLine(string text)
    {
        if (_activeTranscriptSection is null)
            BeginTranscriptSection(GetCurrentScenario());

        if (_typingTranscriptLineIndex is null)
            TranscriptBeginTypingLine();

        _activeTranscriptSection!.Lines[_typingTranscriptLineIndex!.Value] = text;
    }

    private void TranscriptCompleteTypingLine()
        => _typingTranscriptLineIndex = null;

    private void OnPlaylistSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPlaylistEvents || CmbPlaylist?.SelectedItem is not ScenarioPlaylistPickerItem selected)
            return;

        _selectedPlaylistId = selected.Playlist?.Id;
        _playlistCursor = 0;

        if (selected.Playlist is not null)
            ResetPlaylistToStart();

        PersistSimulatorPreferences();

        if (_isRunning)
        {
            StopScenario();
            StartScenario(newSession: true);
        }
        else
        {
            UpdateScenarioLabel();
            SyncScenarioSelection();
        }
    }

    private void OnReplaySeedClick(object sender, RoutedEventArgs e)
    {
        if (_sessionSeed == 0)
            return;

        StopScenario();
        StartScenario(newSession: true, reuseSeed: true);
    }

    private void OnExportTranscriptClick(object sender, RoutedEventArgs e)
    {
        if (_transcriptSections.Count == 0)
            return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|Markdown (*.md)|*.md",
            FileName = $"heimdall-audit-{(_selectedPlaylistId ?? _currentScenarioId)}-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        bool markdown = string.Equals(Path.GetExtension(dialog.FileName), ".md", StringComparison.OrdinalIgnoreCase);
        string content = BuildTranscriptContent(markdown);
        File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
    }

    private string BuildTranscriptContent(bool markdown)
    {
        var sb = new StringBuilder();
        string headerTitle = L("ToolHackerSimTranscriptTitle");
        string playlistLabel = GetSelectedPlaylist() is { } playlist
            ? Tx(playlist.Title)
            : GetScenarioTitle(GetCurrentScenario());

        if (markdown)
        {
            sb.AppendLine($"# {headerTitle}");
            sb.AppendLine();
            sb.AppendLine($"- {L("ToolHackerSimExported")} : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"- Seed: {_sessionSeed}");
            sb.AppendLine($"- {L("ToolHackerSimMode")} : {playlistLabel}");
            sb.AppendLine();

            foreach (var section in _transcriptSections)
            {
                sb.AppendLine($"## {section.ScenarioTitle}");
                sb.AppendLine();
                sb.AppendLine($"`{section.StartedAt:yyyy-MM-dd HH:mm:ss zzz}`");
                sb.AppendLine();
                sb.AppendLine("```text");
                foreach (string line in section.Lines)
                    sb.AppendLine(line);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine(headerTitle);
            sb.AppendLine(new string('=', headerTitle.Length));
            sb.AppendLine($"{L("ToolHackerSimExported")} : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"Seed : {_sessionSeed}");
            sb.AppendLine($"{L("ToolHackerSimMode")} : {playlistLabel}");
            sb.AppendLine();

            foreach (var section in _transcriptSections)
            {
                sb.AppendLine($"[{section.StartedAt:HH:mm:ss}] {section.ScenarioTitle}");
                sb.AppendLine(new string('-', Math.Max(10, section.ScenarioTitle.Length + 12)));
                foreach (string line in section.Lines)
                    sb.AppendLine(line);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private void ApplyVintageMonitorState()
    {
        if (VintageOverlay == null)
            return;

        VintageOverlay.Visibility = _vintageMonitorEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_vintageMonitorEnabled)
            StartVintageMonitorFlicker();
        else
            StopVintageMonitorFlicker();

        UpdatePlaybackControls();
    }

    private void StartVintageMonitorFlicker()
    {
        if (VintageOverlay == null)
            return;

        VintageOverlay.Opacity = VintageBaseOpacity;
        if (_vintageTimer != null)
            return;

        _vintageTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(VintageFlickerIntervalMs),
        };
        _vintageTimer.Tick += (_, _) =>
        {
            if (!_vintageMonitorEnabled || VintageOverlay == null)
                return;

            VintageOverlay.Opacity = VintageFlickerMin + (_uiRng.NextDouble() * VintageFlickerRange);
        };
        _vintageTimer.Start();
    }

    private void StopVintageMonitorFlicker()
    {
        _vintageTimer?.Stop();
        _vintageTimer = null;

        if (VintageOverlay != null)
            VintageOverlay.Opacity = VintageBaseOpacity;
    }

    private void OnVintageMonitorClick(object sender, RoutedEventArgs e)
    {
        _vintageMonitorEnabled = ChkVintageMonitor?.IsChecked == true;
        ApplyVintageMonitorState();
        PersistSimulatorPreferences();
    }
}
