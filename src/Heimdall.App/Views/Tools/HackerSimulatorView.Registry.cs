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
using System.Windows.Media;
using System.Windows.Threading;

namespace Heimdall.App.Views.Tools;

public partial class HackerSimulatorView
{
    private enum ScenarioCategory { Visual, Attack, Deployment, Hardening, Incident, Identity }
    private enum ScenarioRealism { Demo, Ops, Enterprise }

    private readonly record struct LocalizedText(string En, string Fr)
    {
        public string Resolve(bool isFrench)
            => isFrench ? Fr : En;
    }

    private sealed record ScenarioDefinition(
        string Id,
        string TitleKey,
        LocalizedText Subtitle,
        ScenarioCategory Category,
        ScenarioRealism Realism,
        ScenarioTheme Theme,
        string[] Tags,
        Func<List<ScriptAction>> Builder,
        bool IsMatrix = false,
        bool AllowRandom = true);

    private sealed record ScenarioPickerItem(ScenarioDefinition Scenario, string Display);
    private sealed record CategoryFilterOption(string Id, string Display, ScenarioCategory? Category, bool FavoritesOnly = false);
    private sealed record RealismFilterOption(string Id, string Display, ScenarioRealism? Realism);

    private readonly List<ScenarioDefinition> _scenarioDefinitions = [];
    private readonly HashSet<string> _favoriteScenarioIds = new(StringComparer.OrdinalIgnoreCase);
    private string _currentScenarioId = "matrix";
    private string? _lastRandomScenarioId;
    private bool _suppressToolbarEvents;
    private bool _filterFallbackActive;

    private bool IsFrenchLocale
        => string.Equals(_localizer?.CurrentLocale, "fr", StringComparison.OrdinalIgnoreCase);

    private string Tx(string en, string fr)
        => IsFrenchLocale ? fr : en;

    private string Tx(LocalizedText text)
        => text.Resolve(IsFrenchLocale);

    private string Fx(string en, string fr, params object[] args)
        => string.Format(CultureInfo.InvariantCulture, Tx(en, fr), args);

    private static ScenarioTheme Theme(SolidColorBrush brush, byte r, byte g, byte b)
        => new(brush, System.Windows.Media.Color.FromRgb(r, g, b));

    private void EnsureScenarioCatalog()
    {
        if (_scenarioDefinitions.Count > 0)
            return;

        EnsureExternalCatalogLoaded();

        _scenarioDefinitions.AddRange(
        [
            new("matrix", "ToolHackerSimScenarioMatrix",
                new("Animated digital rain backdrop.", "Animation de pluie numerique."),
                ScenarioCategory.Visual, ScenarioRealism.Demo, Theme(s_green, 0, 255, 65),
                ["matrix", "visual", "ambient"], () => [], IsMatrix: true, AllowRandom: false),

            new("pentest", "ToolHackerSimScenarioPentest",
                new("Classic offensive enumeration and exploitation flow.", "Flux classique d'enumeration offensive et d'exploitation."),
                ScenarioCategory.Attack, ScenarioRealism.Demo, Theme(s_green, 0, 255, 65),
                ["nmap", "enum", "pentest"], BuildPentestScript),

            new("exfil", "ToolHackerSimScenarioExfil",
                new("Bulk extraction of sensitive files with progress tracking.", "Extraction massive de fichiers sensibles avec suivi de progression."),
                ScenarioCategory.Attack, ScenarioRealism.Demo, Theme(s_cyan, 0, 255, 255),
                ["exfil", "data", "transfer"], BuildDataExfilScript),

            new("panic", "ToolHackerSimScenarioPanic",
                new("Critical fault cascade rendered like a kernel crash.", "Cascade de defauts critiques affichee comme un crash noyau."),
                ScenarioCategory.Incident, ScenarioRealism.Demo, Theme(s_amber, 255, 176, 0),
                ["kernel", "panic", "crash"], BuildKernelPanicScript),

            new("decrypt", "ToolHackerSimScenarioDecrypt",
                new("Stylized decryption job with block-level progress.", "Traitement de dechiffrement stylise avec progression par blocs."),
                ScenarioCategory.Incident, ScenarioRealism.Demo, Theme(s_cyan, 0, 255, 255),
                ["decrypt", "crypto", "sequence"], BuildDecryptionScript),

            new("sqli", "ToolHackerSimScenarioSqli",
                new("Manual SQL injection sequence with schema extraction.", "Sequence d'injection SQL manuelle avec extraction du schema."),
                ScenarioCategory.Attack, ScenarioRealism.Demo, Theme(s_green, 0, 255, 65),
                ["sql", "injection", "database"], BuildSqlInjectionScript),

            new("brute", "ToolHackerSimScenarioBrute",
                new("SSH brute-force style credential testing.", "Test de credentiels de type brute-force SSH."),
                ScenarioCategory.Attack, ScenarioRealism.Demo, Theme(s_green, 0, 255, 65),
                ["ssh", "brute-force", "password"], BuildBruteForceScript),

            new("ransom", "ToolHackerSimScenarioRansom",
                new("Ransomware-style encryption and extortion screen.", "Ecran de chiffrement et d'extorsion de type ransomware."),
                ScenarioCategory.Incident, ScenarioRealism.Demo, Theme(s_red, 255, 68, 68),
                ["ransomware", "encryption", "incident"], BuildRansomwareScript),

            new("wifi", "ToolHackerSimScenarioWifi",
                new("Wireless audit flow with capture and dictionary attack.", "Flux d'audit sans fil avec capture et attaque par dictionnaire."),
                ScenarioCategory.Attack, ScenarioRealism.Demo, Theme(s_cyan, 0, 255, 255),
                ["wifi", "wpa2", "aircrack"], BuildWifiCrackScript),

            new("mitm", "ToolHackerSimScenarioMitm",
                new("ARP spoofing and interception storyline.", "Mise en scene d'usurpation ARP et d'interception."),
                ScenarioCategory.Attack, ScenarioRealism.Demo, Theme(s_green, 0, 255, 65),
                ["mitm", "arp", "spoofing"], BuildMitmScript),

            new("phishing", "ToolHackerSimScenarioPhishing",
                new("Credential harvesting campaign metrics.", "Metriques d'une campagne de collecte d'identifiants."),
                ScenarioCategory.Identity, ScenarioRealism.Demo, Theme(s_amber, 255, 176, 0),
                ["phishing", "identity", "mail"], BuildPhishingScript),

            new("cryptomine", "ToolHackerSimScenarioCryptomine",
                new("Unauthorized resource hijacking for cryptomining.", "Detournement non autorise de ressources pour du cryptominage."),
                ScenarioCategory.Incident, ScenarioRealism.Demo, Theme(s_yellow, 255, 215, 0),
                ["crypto", "miner", "resource"], BuildCryptominingScript),

            new("ad", "ToolHackerSimScenarioAd",
                new("Directory abuse chain focused on privilege escalation.", "Chaine d'abus d'annuaire orientee elevation de privileges."),
                ScenarioCategory.Identity, ScenarioRealism.Demo, Theme(s_red, 255, 68, 68),
                ["active-directory", "kerberos", "identity"], BuildAdAttackScript),

            new("firmware", "ToolHackerSimScenarioFirmware",
                new("Embedded persistence through firmware tampering.", "Persistance embarquee via alteration du firmware."),
                ScenarioCategory.Attack, ScenarioRealism.Enterprise, Theme(s_amber, 255, 176, 0),
                ["firmware", "uefi", "persistence"], BuildFirmwareScript),

            new("supplychain", "ToolHackerSimScenarioSupplyChain",
                new("Dependency and build pipeline compromise narrative.", "Scenario de compromission de dependances et de chaine de build."),
                ScenarioCategory.Attack, ScenarioRealism.Enterprise, Theme(s_amber, 255, 176, 0),
                ["supply-chain", "build", "dependency"], BuildSupplyChainScript),

            new("scada", "ToolHackerSimScenarioScada",
                new("Industrial control compromise with process impact.", "Compromission de controle industriel avec impact sur le procede."),
                ScenarioCategory.Attack, ScenarioRealism.Enterprise, Theme(s_red, 255, 68, 68),
                ["scada", "ics", "plc"], BuildScadaScript),

            // Infrastructure, deployment, and hardening scenarios are loaded
            // from config/hacker-simulator.scenarios.default.json via the
            // external scenario pack system (see ExternalContent.cs).
        ]);

        foreach (var externalScenario in GetExternalScenarioDefinitions())
        {
            int index = _scenarioDefinitions.FindIndex(s =>
                string.Equals(s.Id, externalScenario.Id, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
                _scenarioDefinitions[index] = externalScenario;
            else
                _scenarioDefinitions.Add(externalScenario);
        }
    }

    private ScenarioDefinition GetCurrentScenario()
    {
        EnsureScenarioCatalog();
        return _scenarioDefinitions.FirstOrDefault(s => string.Equals(s.Id, _currentScenarioId, StringComparison.OrdinalIgnoreCase))
            ?? _scenarioDefinitions[0];
    }

    private IReadOnlyList<ScenarioDefinition> GetFilteredScenarios(bool randomEligibleOnly = false)
    {
        EnsureScenarioCatalog();

        IEnumerable<ScenarioDefinition> query = _scenarioDefinitions;

        if (randomEligibleOnly)
            query = query.Where(s => s.AllowRandom);

        if (GetSelectedCategoryOption() is CategoryFilterOption categoryOption)
        {
            if (categoryOption.FavoritesOnly)
                query = query.Where(s => _favoriteScenarioIds.Contains(s.Id));
            else if (categoryOption.Category is ScenarioCategory category)
                query = query.Where(s => s.Category == category);
        }

        if (GetSelectedRealismOption()?.Realism is ScenarioRealism realism)
            query = query.Where(s => s.Realism == realism);

        string term = TxtScenarioSearch?.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(s => ScenarioMatchesSearch(s, term));

        return query.ToList();
    }

    private void PopulateFilterControls()
    {
        if (CmbCategory == null || CmbRealism == null || ChkRandomMode == null)
            return;

        string? selectedCategoryId = (CmbCategory.SelectedItem as CategoryFilterOption)?.Id;
        string? selectedRealismId = (CmbRealism.SelectedItem as RealismFilterOption)?.Id;

        var categories = new List<CategoryFilterOption>
        {
            new("all", L("ToolHackerSimFilterAll"), null),
            new("favorites", L("ToolHackerSimFilterFavorites"), null, FavoritesOnly: true),
            new("visual", GetCategoryLabel(ScenarioCategory.Visual), ScenarioCategory.Visual),
            new("attack", GetCategoryLabel(ScenarioCategory.Attack), ScenarioCategory.Attack),
            new("deployment", GetCategoryLabel(ScenarioCategory.Deployment), ScenarioCategory.Deployment),
            new("hardening", GetCategoryLabel(ScenarioCategory.Hardening), ScenarioCategory.Hardening),
            new("incident", GetCategoryLabel(ScenarioCategory.Incident), ScenarioCategory.Incident),
            new("identity", GetCategoryLabel(ScenarioCategory.Identity), ScenarioCategory.Identity),
        };

        var realismLevels = new List<RealismFilterOption>
        {
            new("all", L("ToolHackerSimFilterAllLevels"), null),
            new("demo", GetRealismLabel(ScenarioRealism.Demo), ScenarioRealism.Demo),
            new("ops", GetRealismLabel(ScenarioRealism.Ops), ScenarioRealism.Ops),
            new("enterprise", GetRealismLabel(ScenarioRealism.Enterprise), ScenarioRealism.Enterprise),
        };

        _suppressToolbarEvents = true;
        CmbCategory.ItemsSource = categories;
        CmbCategory.SelectedItem = categories.FirstOrDefault(o => o.Id == selectedCategoryId) ?? categories[0];
        CmbRealism.ItemsSource = realismLevels;
        CmbRealism.SelectedItem = realismLevels.FirstOrDefault(o => o.Id == selectedRealismId) ?? realismLevels[0];
        ChkRandomMode.Content = L("ToolHackerSimRandomInFilter");
        ChkRandomMode.IsChecked = _randomMode;
        _suppressToolbarEvents = false;
    }

    private void RefreshScenarioPicker(bool restartIfSelectionChanges)
    {
        if (CmbScenario == null)
            return;

        EnsureScenarioCatalog();

        string previousScenarioId = _currentScenarioId;
        var filtered = GetFilteredScenarios().ToList();
        _filterFallbackActive = filtered.Count == 0;
        if (_filterFallbackActive)
            filtered = _scenarioDefinitions.ToList();

        var items = filtered
            .Select(s => new ScenarioPickerItem(s, FormatScenarioDisplay(s)))
            .ToList();

        _suppressToolbarEvents = true;
        CmbScenario.ItemsSource = items;
        CmbScenario.SelectedItem = items.FirstOrDefault(i => i.Scenario.Id == previousScenarioId) ?? items[0];
        _suppressToolbarEvents = false;

        if (CmbScenario.SelectedItem is ScenarioPickerItem selected)
            _currentScenarioId = selected.Scenario.Id;

        UpdateFavoriteButton();
        UpdateScenarioLabel();
        BuildContextMenu();

        if (restartIfSelectionChanges && _isRunning && !string.Equals(previousScenarioId, _currentScenarioId, StringComparison.OrdinalIgnoreCase))
        {
            StopScenario();
            StartScenario(newSession: true);
        }
    }

    private void SyncScenarioSelection()
    {
        if (CmbScenario == null)
            return;

        var existing = CmbScenario.Items
            .OfType<ScenarioPickerItem>()
            .FirstOrDefault(i => i.Scenario.Id == _currentScenarioId);

        if (existing != null)
        {
            _suppressToolbarEvents = true;
            CmbScenario.SelectedItem = existing;
            _suppressToolbarEvents = false;
            UpdateFavoriteButton();
            UpdateScenarioLabel();
            BuildContextMenu();
            return;
        }

        RefreshScenarioPicker(restartIfSelectionChanges: false);
    }

    private ScenarioDefinition PickRandomScenario()
    {
        var candidates = GetFilteredScenarios(randomEligibleOnly: true).ToList();
        if (candidates.Count == 0)
            candidates = _scenarioDefinitions.Where(s => s.AllowRandom).ToList();

        if (candidates.Count == 0)
            return GetCurrentScenario();

        if (candidates.Count == 1)
        {
            _lastRandomScenarioId = candidates[0].Id;
            return candidates[0];
        }

        ScenarioDefinition next;
        do
        {
            next = candidates[_rng.Next(candidates.Count)];
        }
        while (string.Equals(next.Id, _lastRandomScenarioId, StringComparison.OrdinalIgnoreCase));

        _lastRandomScenarioId = next.Id;
        return next;
    }

    private IReadOnlyList<ScenarioDefinition> GetContextMenuScenarios()
    {
        var filtered = GetFilteredScenarios().ToList();
        return filtered.Count > 0 ? filtered : _scenarioDefinitions;
    }

    private string GetScenarioTitle(ScenarioDefinition scenario)
        => L(scenario.TitleKey);

    private string GetScenarioSubtitle(ScenarioDefinition scenario)
        => Tx(scenario.Subtitle);

    private string FormatScenarioDisplay(ScenarioDefinition scenario)
        => $"{(_favoriteScenarioIds.Contains(scenario.Id) ? "★ " : string.Empty)}{GetScenarioTitle(scenario)}";

    private bool ScenarioMatchesSearch(ScenarioDefinition scenario, string term)
    {
        string haystack = string.Join(" | ",
            GetScenarioTitle(scenario),
            GetScenarioSubtitle(scenario),
            string.Join(' ', scenario.Tags));

        return haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private CategoryFilterOption? GetSelectedCategoryOption()
        => CmbCategory?.SelectedItem as CategoryFilterOption;

    private RealismFilterOption? GetSelectedRealismOption()
        => CmbRealism?.SelectedItem as RealismFilterOption;

    private string GetCategoryLabel(ScenarioCategory category) => category switch
    {
        ScenarioCategory.Visual => L("ToolHackerSimCategoryVisual"),
        ScenarioCategory.Attack => L("ToolHackerSimCategoryAttack"),
        ScenarioCategory.Deployment => L("ToolHackerSimCategoryDeployment"),
        ScenarioCategory.Hardening => L("ToolHackerSimCategoryHardening"),
        ScenarioCategory.Incident => L("ToolHackerSimCategoryIncident"),
        ScenarioCategory.Identity => L("ToolHackerSimCategoryIdentity"),
        _ => category.ToString(),
    };

    private string GetRealismLabel(ScenarioRealism realism) => realism switch
    {
        ScenarioRealism.Demo => L("ToolHackerSimRealismDemo"),
        ScenarioRealism.Ops => L("ToolHackerSimRealismOps"),
        ScenarioRealism.Enterprise => L("ToolHackerSimRealismEnterprise"),
        _ => realism.ToString(),
    };

    private void UpdateFavoriteButton()
    {
        if (BtnFavorite == null)
            return;

        bool isFavorite = _favoriteScenarioIds.Contains(_currentScenarioId);
        BtnFavorite.Content = isFavorite
            ? L("ToolHackerSimFavoriteRemove")
            : L("ToolHackerSimFavoriteAdd");
        BtnFavorite.Foreground = isFavorite ? s_yellow : s_gray;
        BtnFavorite.BorderBrush = isFavorite ? s_yellow : s_gray;
        BtnFavorite.ToolTip = isFavorite
            ? L("ToolHackerSimFavoriteRemoveTip")
            : L("ToolHackerSimFavoriteAddTip");
    }

    private void OnScenarioSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarEvents || CmbScenario?.SelectedItem is not ScenarioPickerItem selected)
            return;

        string previousScenarioId = _currentScenarioId;
        _currentScenarioId = selected.Scenario.Id;
        ClearPlaylistSelection(persist: false);
        UpdateFavoriteButton();
        UpdateScenarioLabel();
        BuildContextMenu();
        PersistSimulatorPreferences();

        if (_isRunning && !string.Equals(previousScenarioId, _currentScenarioId, StringComparison.OrdinalIgnoreCase))
        {
            StopScenario();
            StartScenario(newSession: true);
        }
    }

    private void OnScenarioFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarEvents)
            return;

        RefreshScenarioPicker(restartIfSelectionChanges: true);
    }

    private void OnScenarioSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressToolbarEvents)
            return;

        RefreshScenarioPicker(restartIfSelectionChanges: true);
    }

    private void OnRandomModeClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarEvents)
            return;

        _randomMode = ChkRandomMode?.IsChecked == true;
        UpdateScenarioLabel();
        BuildContextMenu();
        PersistSimulatorPreferences();
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (_favoriteScenarioIds.Contains(_currentScenarioId))
            _favoriteScenarioIds.Remove(_currentScenarioId);
        else
            _favoriteScenarioIds.Add(_currentScenarioId);

        UpdateFavoriteButton();
        RefreshScenarioPicker(restartIfSelectionChanges: false);
        PersistSimulatorPreferences();
    }

    private void OnLocaleChanged(string locale)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_disposed)
                return;

            ApplyLocalization();
            RefreshScenarioPicker(restartIfSelectionChanges: false);

            if (_isRunning)
            {
                StopScenario();
                StartScenario(newSession: true, reuseSeed: true);
            }
        });
    }
}
