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

using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.App.Views.Dialogs;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using TwinShell.Core.Enums;
using TwinShell.Core.Interfaces;
using TwinShell.Core.Models;
using ActionModel = TwinShell.Core.Models.Action;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Embedded tool wrapping TwinShell's command library.
/// Provides searchable access to 500+ PowerShell and Bash commands
/// with parameterized command generation, CRUD, favorites, and history.
/// </summary>
public partial class CommandLibraryView : UserControl, IToolView
{
    private const long MaxImportFileSizeBytes = 50 * 1024 * 1024;

    private LocalizationManager? _localizer;
    private Action<string>? _sendCommand;
    private IServiceProvider? _services;

    private List<ActionEntry> _allEntries = [];
    private ICollectionView? _collectionView;
    private ActionModel? _selectedAction;
    private CommandTemplate? _activeTemplate;
    private List<ParameterEntry> _parameters = [];
    private HashSet<string> _favoriteIds = [];
    private List<string> _categoryList = [];

    private CancellationTokenSource? _searchCts;
    private ISearchService? _searchService;
    private ICommandGeneratorService? _commandGenerator;
    private bool _commandValid;
    private string? _targetHost;

    private bool _suppressFilterEvents;
    private bool _isSyncing;
    private bool _isBusy;
    private bool _favoritesFilterActive;
    private System.Windows.Threading.DispatcherTimer? _historyCopyTimer;
    private IServiceScope? _serviceScope;

    public CommandLibraryView()
    {
        InitializeComponent();
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _localizer = localizer;
        _sendCommand = context?.SendCommandAction;
        _targetHost = context?.TargetHost?.Trim();
        _services = ((App)System.Windows.Application.Current).Services;

        ApplyLocalization();
        PopulateFilterCombos(context?.ConnectionType);
        _ = LoadActionsAsync(_targetHost);
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = L("ToolCmdLibTitle");
        TxtSearch.Tag = L("ToolCmdLibSearchPlaceholder");
        TxtNoResults.Text = L("ToolCmdLibNoResults");
        TxtEmptyState.Text = L("ToolCmdLibEmptyState");
        BtnCopy.Content = L("ToolCmdLibBtnCopy");
        BtnSend.Content = L("ToolCmdLibBtnSend");
        TxtHistoryTitle.Text = L("ToolCmdLibHistoryTitle");
        TxtHistoryEmpty.Text = L("ToolCmdLibHistoryEmpty");
        TxtResultCount.Text = string.Format(L("ToolCmdLibResultCountFormat"), 0, 0);

        System.Windows.Automation.AutomationProperties.SetName(TxtSearch, L("ToolCmdLibSearchPlaceholder"));
        System.Windows.Automation.AutomationProperties.SetName(ActionList, L("ToolCmdLibTitle"));
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
        System.Windows.Automation.AutomationProperties.SetName(BtnAdd, L("ToolCmdLibBtnAdd"));
        System.Windows.Automation.AutomationProperties.SetName(BtnEdit, L("ToolCmdLibBtnEdit"));
        System.Windows.Automation.AutomationProperties.SetName(BtnDelete, L("ToolCmdLibBtnDelete"));
        System.Windows.Automation.AutomationProperties.SetName(BtnHistory, L("ToolCmdLibBtnHistory"));
        BtnEdit.ToolTip = L("ToolCmdLibBtnEdit");
        BtnDelete.ToolTip = L("ToolCmdLibBtnDelete");
        System.Windows.Automation.AutomationProperties.SetName(BtnExport, L("ToolCmdLibBtnExport"));
        System.Windows.Automation.AutomationProperties.SetName(BtnImport, L("ToolCmdLibBtnImport"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSync, L("ToolCmdLibBtnSync"));
        BtnSync.ToolTip = L("ToolCmdLibBtnSync");
        System.Windows.Automation.AutomationProperties.SetName(BtnClearHistory, L("ToolCmdLibHistoryClear"));
        BtnHelp.ToolTip = L("ToolHelpTooltip");
        BtnAdd.ToolTip = L("ToolCmdLibBtnAdd");
        BtnExport.ToolTip = L("ToolCmdLibBtnExport");
        BtnImport.ToolTip = L("ToolCmdLibBtnImport");
        BtnHistory.ToolTip = L("ToolCmdLibBtnHistory");
        BtnClearHistory.ToolTip = L("ToolCmdLibHistoryClear");

        System.Windows.Automation.AutomationProperties.SetName(BtnClearSearch, L("A11yClearSearch"));
        BtnFavoriteFilter.ToolTip = L("ToolCmdLibFilterFavorites");
        System.Windows.Automation.AutomationProperties.SetName(BtnFavoriteFilter, L("ToolCmdLibFilterFavorites"));

        RbWindows.Content = L("ToolCmdLibPlatformWindows");
        RbLinux.Content = L("ToolCmdLibPlatformLinux");

        System.Windows.Automation.AutomationProperties.SetName(CmbRisk, L("ToolCmdLibDialogLblRisk"));
        System.Windows.Automation.AutomationProperties.SetName(CmbPlatform, L("ToolCmdLibDialogLblPlatform"));
        System.Windows.Automation.AutomationProperties.SetName(CmbCategory, L("ToolCmdLibDialogLblCategory"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopy, L("ToolCmdLibBtnCopy"));
        System.Windows.Automation.AutomationProperties.SetName(BtnSend, L("ToolCmdLibBtnSend"));
        System.Windows.Automation.AutomationProperties.SetName(RbWindows, L("ToolCmdLibPlatformWindows"));
        System.Windows.Automation.AutomationProperties.SetName(RbLinux, L("ToolCmdLibPlatformLinux"));

        BtnSend.Visibility = _sendCommand is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateFilterCombos(string? connectionType)
    {
        _suppressFilterEvents = true;

        CmbPlatform.Items.Clear();
        CmbPlatform.Items.Add(L("ToolCmdLibFilterPlatformAll"));
        CmbPlatform.Items.Add(L("ToolCmdLibPlatformWindows"));
        CmbPlatform.Items.Add(L("ToolCmdLibPlatformLinux"));

        var autoIndex = connectionType?.ToUpperInvariant() switch
        {
            "SSH" => 2,
            "LOCAL" => 1,
            "RDP" => 1,
            _ => 0
        };
        CmbPlatform.SelectedIndex = autoIndex;

        CmbRisk.Items.Clear();
        CmbRisk.Items.Add(L("ToolCmdLibFilterRiskAll"));
        CmbRisk.Items.Add(L("ToolCmdLibRiskInfo"));
        CmbRisk.Items.Add(L("ToolCmdLibRiskRun"));
        CmbRisk.Items.Add(L("ToolCmdLibRiskDangerous"));
        CmbRisk.SelectedIndex = 0;

        CmbCategory.Items.Clear();
        CmbCategory.Items.Add(L("ToolCmdLibFilterCategoryAll"));
        CmbCategory.SelectedIndex = 0;

        _suppressFilterEvents = false;
    }

    // ── Data loading ──────────────────────────────────────────────

    private async Task LoadActionsAsync(string? targetHost)
    {
        if (_services is null) return;

        EmptyState.Visibility = Visibility.Visible;
        TxtEmptyState.Text = L("ToolCmdLibStatusLoading");
        LoadingBar.Visibility = Visibility.Visible;

        try
        {
            _serviceScope?.Dispose();
            _serviceScope = _services.CreateScope();
            var scope = _serviceScope;
            var actionService = scope.ServiceProvider.GetRequiredService<IActionService>();
            _searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
            _commandGenerator = scope.ServiceProvider.GetRequiredService<ICommandGeneratorService>();

            // Load favorites
            var favService = scope.ServiceProvider.GetRequiredService<IFavoritesService>();
            var favs = await favService.GetAllFavoritesAsync();
            _favoriteIds = new HashSet<string>(favs.Select(f => f.ActionId), StringComparer.Ordinal);

            var actions = (await actionService.GetAllActionsAsync()).ToList();
            _allEntries = actions.Select(a => new ActionEntry(a, this)).ToList();

            // Populate category combo (keep "All" at top)
            _suppressFilterEvents = true;
            _categoryList = actions.Select(a => a.Category).Distinct().OrderBy(c => c).ToList();
            while (CmbCategory.Items.Count > 1)
                CmbCategory.Items.RemoveAt(CmbCategory.Items.Count - 1);
            foreach (var cat in _categoryList)
            {
                CmbCategory.Items.Add(cat);
            }
            _suppressFilterEvents = false;

            ActionList.ItemsSource = _allEntries;
            _collectionView = CollectionViewSource.GetDefaultView(_allEntries);
            _collectionView.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(ActionEntry.Category)));
            _collectionView.Filter = FilterPredicate;

            EmptyState.Visibility = _allEntries.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
            TxtEmptyState.Text = _allEntries.Count == 0
                ? L("ToolCmdLibEmptyLibrary") : "";

            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                TxtSearch.Focus();
            });
        }
        catch (Exception ex)
        {
            TxtEmptyState.Text = L("ToolCmdLibStatusError") is { } fmt && fmt.Contains("{0}")
                ? string.Format(fmt, ex.Message) : ex.Message;
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Failed to load actions: {ex.Message}");
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ReloadActionsAsync()
    {
        var selectedIndex = CmbCategory.SelectedIndex;
        var platformIndex = CmbPlatform.SelectedIndex;
        var riskIndex = CmbRisk.SelectedIndex;
        var searchText = TxtSearch.Text;

        _collectionView = null;
        _searchResults = null;
        _searchRankedIds = null;
        _searchMatchIds = null;
        _lastSearchTerm = "";
        GeneratorPanel.Visibility = Visibility.Collapsed;

        await LoadActionsAsync(null);

        _suppressFilterEvents = true;
        CmbCategory.SelectedIndex = Math.Min(selectedIndex, CmbCategory.Items.Count - 1);
        CmbPlatform.SelectedIndex = platformIndex;
        CmbRisk.SelectedIndex = riskIndex;
        TxtSearch.Text = searchText;
        _suppressFilterEvents = false;

        _collectionView?.Refresh();
        UpdateEmptyStates();
    }

    // ── Search ────────────────────────────────────────────────────

    private string _lastSearchTerm = "";
    private List<ActionModel>? _searchResults;
    private List<string>? _searchRankedIds;
    private HashSet<string>? _searchMatchIds;

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var term = TxtSearch.Text.Trim();

        BtnClearSearch.Visibility = TxtSearch.Text.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;

        try { await Task.Delay(200, token); }
        catch (OperationCanceledException) { return; }

        if (term != _lastSearchTerm && _searchService is not null && term.Length > 0)
        {
            _lastSearchTerm = term;
            var allActions = _allEntries.Select(e2 => e2.Source).ToList();
            var ranked = (await _searchService.SearchAsync(allActions, term)).ToList();
            _searchResults = ranked;

            // Build ranked ID list to preserve relevance order
            _searchRankedIds = ranked.Select(r => r.Id).ToList();
            _searchMatchIds = new HashSet<string>(_searchRankedIds, StringComparer.Ordinal);
        }
        else if (term.Length == 0)
        {
            _lastSearchTerm = "";
            _searchResults = null;
            _searchRankedIds = null;
            _searchMatchIds = null;
        }

        // When searching, apply relevance-based sort; otherwise group by category
        if (_collectionView is not null)
        {
            if (_searchRankedIds is not null)
            {
                _collectionView.GroupDescriptions.Clear();
                _collectionView.SortDescriptions.Clear();
                _collectionView.SortDescriptions.Add(
                    new SortDescription(nameof(ActionEntry.SearchRank), ListSortDirection.Ascending));
            }
            else if (_collectionView.GroupDescriptions.Count == 0)
            {
                _collectionView.SortDescriptions.Clear();
                _collectionView.GroupDescriptions.Add(
                    new PropertyGroupDescription(nameof(ActionEntry.Category)));
            }

            _collectionView.Refresh();
        }

        UpdateEmptyStates();
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        _collectionView?.Refresh();
        UpdateEmptyStates();
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = string.Empty;
        TxtSearch.Focus();
    }

    private void OnFavoriteFilterClick(object sender, RoutedEventArgs e)
    {
        _favoritesFilterActive = !_favoritesFilterActive;
        BtnFavoriteFilter.Foreground = _favoritesFilterActive
            ? TryGetBrush("WarningBrush", Brushes.Orange)
            : TryGetBrush("TextSecondaryBrush", Brushes.Gray);
        _collectionView?.Refresh();
        UpdateEmptyStates();
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not ActionEntry entry) return false;

        // Platform filter
        if (CmbPlatform.SelectedIndex == 1
            && entry.Source.Platform != Platform.Windows
            && entry.Source.Platform != Platform.Both)
            return false;

        if (CmbPlatform.SelectedIndex == 2
            && entry.Source.Platform != Platform.Linux
            && entry.Source.Platform != Platform.Both)
            return false;

        // Risk filter
        if (CmbRisk.SelectedIndex > 0)
        {
            var expectedLevel = (CriticalityLevel)(CmbRisk.SelectedIndex - 1);
            if (entry.Source.Level != expectedLevel) return false;
        }

        // Favorites filter (independent toggle)
        if (_favoritesFilterActive && !_favoriteIds.Contains(entry.Source.Id))
            return false;

        // Category filter (index 0=All, 1+=real categories)
        if (CmbCategory.SelectedIndex > 0)
        {
            var selectedCategory = CmbCategory.SelectedItem as string;
            if (entry.Source.Category != selectedCategory) return false;
        }

        // Search filter
        if (_searchMatchIds is not null)
        {
            return _searchMatchIds.Contains(entry.Source.Id);
        }

        return true;
    }

    private void UpdateEmptyStates()
    {
        var visibleCount = _collectionView?.Cast<object>().Count() ?? 0;
        TxtResultCount.Text = string.Format(L("ToolCmdLibResultCountFormat"), visibleCount, _allEntries.Count);

        if (visibleCount > 0)
        {
            TxtNoResults.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        // No results — determine which empty state to show
        var hasSearch = TxtSearch.Text.Trim().Length > 0;
        var hasFilters = _favoritesFilterActive
            || CmbCategory.SelectedIndex > 0
            || CmbPlatform.SelectedIndex > 0
            || CmbRisk.SelectedIndex > 0;

        if (hasSearch || hasFilters)
        {
            // Active search or filters produced no match
            TxtNoResults.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
        }
        else if (_allEntries.Count == 0)
        {
            // Library is genuinely empty
            TxtNoResults.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }
        else
        {
            TxtNoResults.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
        }
    }

    // ── Selection & generator ──────────────────────────────────────

    private void OnActionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ActionList.SelectedItem is not ActionEntry entry)
        {
            GeneratorPanel.Visibility = Visibility.Collapsed;
            return;
        }

        _selectedAction = entry.Source;

        var hasWin = _selectedAction.WindowsCommandTemplate is not null;
        var hasLinux = _selectedAction.LinuxCommandTemplate is not null;

        // Show platform switcher when both templates exist
        TemplateSwitcher.Visibility = hasWin && hasLinux
            ? Visibility.Visible : Visibility.Collapsed;

        if (hasWin && hasLinux)
        {
            // Default selection based on platform filter
            var preferWindows = CmbPlatform.SelectedIndex == 1;
            _suppressTemplateSwitch = true;
            RbWindows.IsChecked = preferWindows || CmbPlatform.SelectedIndex == 0;
            RbLinux.IsChecked = !preferWindows && CmbPlatform.SelectedIndex != 0;
            _suppressTemplateSwitch = false;
            _activeTemplate = (RbWindows.IsChecked == true)
                ? _selectedAction.WindowsCommandTemplate
                : _selectedAction.LinuxCommandTemplate;
        }
        else
        {
            _activeTemplate = hasWin
                ? _selectedAction.WindowsCommandTemplate
                : _selectedAction.LinuxCommandTemplate;
        }

        if (_activeTemplate is null)
        {
            GeneratorPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ApplyActiveTemplate();

        // Hide Edit/Delete for system (seed) actions
        var canEdit = _selectedAction.IsUserCreated ? Visibility.Visible : Visibility.Collapsed;
        BtnEdit.Visibility = canEdit;
        BtnDelete.Visibility = canEdit;

        HistoryPanel.Visibility = Visibility.Collapsed;
        GeneratorPanel.Visibility = Visibility.Visible;
    }

    private bool _suppressTemplateSwitch;

    private void OnTemplatePlatformChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressTemplateSwitch || _selectedAction is null) return;

        _activeTemplate = (RbWindows.IsChecked == true)
            ? _selectedAction.WindowsCommandTemplate
            : _selectedAction.LinuxCommandTemplate;

        if (_activeTemplate is not null)
        {
            ApplyActiveTemplate();
        }
    }

    private void ApplyActiveTemplate()
    {
        if (_activeTemplate is null) return;

        TxtTemplateName.Text = _activeTemplate.Name;
        _parameters = _activeTemplate.Parameters
            .Select(p => new ParameterEntry
            {
                Name = p.Name,
                Label = p.Label,
                Value = GetInitialParameterValue(p),
                Required = p.Required,
                Description = p.Description,
                Type = p.Type ?? "string"
            })
            .ToList();

        ParametersList.ItemsSource = _parameters;
        GenerateCommand();
        PopulateDetailPanel();
    }

    private void PopulateDetailPanel()
    {
        if (_selectedAction is null)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var hasNotes = !string.IsNullOrWhiteSpace(_selectedAction.Notes);
        TxtNotes.Text = hasNotes ? _selectedAction.Notes!.Trim() : "";
        TxtNotes.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;

        // Collect examples relevant to the active template's platform
        var examples = (_activeTemplate?.Platform == TwinShell.Core.Enums.Platform.Windows
            ? _selectedAction.WindowsExamples
            : _selectedAction.LinuxExamples) ?? [];
        if (examples.Count == 0)
            examples = _selectedAction.Examples ?? [];

        ExamplesList.ItemsSource = examples.Count > 0 ? examples : null;
        ExamplesList.Visibility = examples.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var links = _selectedAction.Links ?? [];
        LinksList.ItemsSource = links.Count > 0 ? links : null;
        LinksList.Visibility = links.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        DetailPanel.Visibility = hasNotes || examples.Count > 0 || links.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnParameterChanged(object sender, TextChangedEventArgs e)
    {
        GenerateCommand();
    }

    private void GenerateCommand()
    {
        if (_activeTemplate is null || _commandGenerator is null) return;

        var values = new Dictionary<string, string>();
        foreach (var p in _parameters)
            values[p.Name] = p.Value;

        try
        {
            var valid = _commandGenerator.ValidateParameters(_activeTemplate, values, out var errors);
            if (valid && errors.Count == 0)
            {
                TxtGenerated.Text = _commandGenerator.GenerateCommand(_activeTemplate, values);
                TxtGenerated.Foreground = TryGetBrush("AccentBrush", System.Windows.Media.Brushes.DodgerBlue);
                TxtValidationError.Text = string.Empty;
                TxtValidationError.Visibility = Visibility.Collapsed;
                _commandValid = true;
            }
            else
            {
                TxtGenerated.Text = _activeTemplate.CommandPattern;
                TxtGenerated.Foreground = TryGetBrush("TextSecondaryBrush", System.Windows.Media.Brushes.Gray);
                TxtValidationError.Text = string.Join(
                    Environment.NewLine,
                    errors.Select(static error => $"- {error}"));
                TxtValidationError.Visibility = Visibility.Visible;
                _commandValid = false;
            }
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn($"[CommandLibrary] Generate failed: {ex.Message}");
            TxtGenerated.Text = _activeTemplate.CommandPattern;
            TxtGenerated.Foreground = TryGetBrush("TextSecondaryBrush", System.Windows.Media.Brushes.Gray);
            TxtValidationError.Text = string.Format(L("ToolCmdLibGenerateError"), ex.Message);
            TxtValidationError.Visibility = Visibility.Visible;
            _commandValid = false;
        }

        BtnCopy.IsEnabled = _commandValid;
        BtnSend.IsEnabled = _commandValid;
    }

    // ── Copy / Send + History recording ───────────────────────────

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var text = TxtGenerated.Text;
        if (string.IsNullOrEmpty(text)) return;

        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { return; }

        CopyFeedbackHelper.ShowCopyFeedback(BtnCopy);
        RecordHistory(text);
    }

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        var text = TxtGenerated.Text;
        if (string.IsNullOrEmpty(text) || _sendCommand is null) return;

        _sendCommand(text);
        CopyFeedbackHelper.ShowCopyFeedback(BtnSend);
        RecordHistory(text);
    }

    private void RecordHistory(string generatedCommand)
    {
        if (_services is null || _selectedAction is null || _activeTemplate is null) return;

        var action = _selectedAction;
        var template = _activeTemplate;
        var paramValues = new Dictionary<string, string>();
        foreach (var p in _parameters)
            paramValues[p.Name] = p.Value;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _services.CreateScope();
                var historyService = scope.ServiceProvider.GetRequiredService<ICommandHistoryService>();
                await historyService.AddCommandAsync(
                    action.Id, generatedCommand, paramValues,
                    template.Platform, action.Title, action.Category);
            }
            catch (Exception ex)
            {
                Heimdall.Core.Logging.FileLogger.Warn(
                    $"[CommandLibrary] Failed to record history: {ex.Message}");
            }
        });
    }

    // ── Favorites ─────────────────────────────────────────────────

    private async void OnFavoriteToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ActionEntry entry } || _services is null) return;

        try
        {
            using var scope = _services.CreateScope();
            var favService = scope.ServiceProvider.GetRequiredService<IFavoritesService>();
            var isFav = await favService.ToggleFavoriteAsync(entry.Source.Id);

            if (isFav) _favoriteIds.Add(entry.Source.Id);
            else _favoriteIds.Remove(entry.Source.Id);

            // Refresh the list to update icons
            _collectionView?.Refresh();
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Favorite toggle failed: {ex.Message}");
            ShowErrorToast(ex.Message);
        }
    }

    // ── CRUD ──────────────────────────────────────────────────────

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;

        var vm = new CommandActionDialogViewModel
        {
            DialogTitle = L("ToolCmdLibDialogTitleAdd"),
            Localizer = _localizer,
            AvailableCategories = _categoryList.ToList()
        };

        var dialog = new CommandActionDialog
        {
            DataContext = vm,
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var action = vm.ToAction();
            using var scope = _services.CreateScope();
            var actionService = scope.ServiceProvider.GetRequiredService<IActionService>();
            await actionService.CreateActionAsync(action);
            await ReloadActionsAsync();
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Create action failed: {ex.Message}");
            ShowErrorToast(ex.Message);
        }
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        var entry = ActionList.SelectedItem as ActionEntry;
        if (entry is null || _services is null) return;

        var vm = CommandActionDialogViewModel.FromAction(entry.Source);
        vm.DialogTitle = L("ToolCmdLibDialogTitleEdit");
        vm.Localizer = _localizer;
        vm.AvailableCategories = _categoryList.ToList();

        var dialog = new CommandActionDialog
        {
            DataContext = vm,
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var updated = vm.ToAction();
            using var scope = _services.CreateScope();
            var actionService = scope.ServiceProvider.GetRequiredService<IActionService>();
            await actionService.UpdateActionAsync(updated);
            await ReloadActionsAsync();
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Update action failed: {ex.Message}");
            ShowErrorToast(ex.Message);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var entry = ActionList.SelectedItem as ActionEntry;
        if (entry is null || _services is null) return;

        var confirmed = MessageDialog.ShowConfirm(
            Window.GetWindow(this),
            L("ToolCmdLibDeleteConfirmTitle"),
            string.Format(L("ToolCmdLibDeleteConfirmMessage"), entry.Title),
            "warning",
            L("BtnYes"), L("BtnNo"));

        if (!confirmed) return;

        try
        {
            using var scope = _services.CreateScope();
            var actionService = scope.ServiceProvider.GetRequiredService<IActionService>();
            await actionService.DeleteActionAsync(entry.Source.Id);
            await ReloadActionsAsync();
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Delete action failed: {ex.Message}");
            ShowErrorToast(ex.Message);
        }
    }

    // ── Import / Export ──────────────────────────────────────────

    private static readonly System.Text.Json.JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private static readonly System.Text.Json.JsonSerializerOptions ImportOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(
            System.Text.Json.JsonNamingPolicy.CamelCase) }
    };

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = $"commands-export-{DateTime.Now:yyyyMMdd}.json"
        };

        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        _isBusy = true;
        try
        {
            using var scope = _services.CreateScope();
            var actionService = scope.ServiceProvider.GetRequiredService<IActionService>();
            var actions = (await actionService.GetAllActionsAsync()).ToList();

            var envelope = new
            {
                schemaVersion = "1.0",
                exportDate = DateTime.UtcNow,
                totalActions = actions.Count,
                actions
            };

            var json = System.Text.Json.JsonSerializer.Serialize(envelope, ExportOptions);
            await System.IO.File.WriteAllTextAsync(dlg.FileName, json);

            MessageDialog.ShowMessage(
                Window.GetWindow(this),
                L("ToolCmdLibExportSuccess"),
                string.Format(L("ToolCmdLibExportSuccessMessage"), actions.Count, dlg.FileName),
                L("BtnOk"));
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Export failed: {ex.Message}");
            MessageDialog.ShowMessage(
                Window.GetWindow(this),
                L("ToolCmdLibExportError"),
                ex.Message,
                L("BtnOk"));
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Multiselect = false
        };

        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        _isBusy = true;
        try
        {
            var fileInfo = new System.IO.FileInfo(dlg.FileName);
            if (fileInfo.Length > MaxImportFileSizeBytes)
            {
                MessageDialog.ShowMessage(
                    Window.GetWindow(this),
                    L("ToolCmdLibImportError"),
                    L("ToolCmdLibImportFileTooLarge"),
                    L("BtnOk"));
                return;
            }

            var json = await System.IO.File.ReadAllTextAsync(dlg.FileName);
            var actions = ParseImportJson(json);

            if (actions is null || actions.Count == 0)
            {
                MessageDialog.ShowMessage(
                    Window.GetWindow(this),
                    L("ToolCmdLibImportError"),
                    L("ToolCmdLibImportInvalidFormat"),
                    L("BtnOk"));
                return;
            }

            int imported = 0, updated = 0, skipped = 0;

            using var scope = _services.CreateScope();
            var actionService = scope.ServiceProvider.GetRequiredService<IActionService>();

            foreach (var action in actions)
            {
                if (string.IsNullOrWhiteSpace(action.Title)
                    || string.IsNullOrWhiteSpace(action.Category)
                    || action.Title.Length > 200
                    || action.Category.Length > 100)
                {
                    skipped++;
                    continue;
                }

                var existing = await actionService.GetActionByPublicIdAsync(action.PublicId)
                    ?? await actionService.GetActionByIdAsync(action.Id);
                if (existing is not null)
                {
                    if (existing.IsUserCreated)
                    {
                        action.Id = existing.Id;
                        action.PublicId = existing.PublicId;
                        action.IsUserCreated = existing.IsUserCreated;
                        action.UpdatedAt = DateTime.UtcNow;
                        await actionService.UpdateActionAsync(action);
                        updated++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                else
                {
                    action.IsUserCreated = true;
                    action.CreatedAt = DateTime.UtcNow;
                    action.UpdatedAt = DateTime.UtcNow;
                    await actionService.CreateActionAsync(action);
                    imported++;
                }
            }

            await ReloadActionsAsync();

            MessageDialog.ShowMessage(
                Window.GetWindow(this),
                L("ToolCmdLibImportResultTitle"),
                string.Format(L("ToolCmdLibImportResultMessage"), imported, updated, skipped),
                L("BtnOk"));
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Import failed: {ex.Message}");
            MessageDialog.ShowMessage(
                Window.GetWindow(this),
                L("ToolCmdLibImportError"),
                ex.Message,
                L("BtnOk"));
        }
        finally
        {
            _isBusy = false;
        }
    }

    private static List<ActionModel>? ParseImportJson(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Bulk format: { "actions": [...] }
            if (root.TryGetProperty("actions", out var actionsElement)
                && actionsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<ActionModel>>(
                    actionsElement.GetRawText(), ImportOptions);
            }

            // Single-action format: { "id": "...", "title": "..." }
            if (root.TryGetProperty("id", out _) && root.TryGetProperty("title", out _))
            {
                var single = System.Text.Json.JsonSerializer.Deserialize<ActionModel>(
                    json, ImportOptions);
                return single is not null ? [single] : null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // ── Git Sync ──────────────────────────────────────────────────

    private async void OnSyncClick(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;

        // Check if configured
        var configManager = _services.GetRequiredService<Heimdall.Core.Configuration.ConfigManager>();
        var settings = await configManager.LoadSettingsAsync();

        if (!settings.CmdLibGitSyncEnabled || string.IsNullOrWhiteSpace(settings.CmdLibGitSyncUrl))
        {
            MessageDialog.ShowMessage(
                Window.GetWindow(this),
                L("ToolCmdLibSyncNotConfigured"),
                L("ToolCmdLibSyncNotConfiguredDesc"),
                "warning",
                L("BtnOk"));
            return;
        }

        var gitSync = _services.GetRequiredService<TwinShell.Core.Interfaces.IGitSyncService>();

        BtnSync.IsEnabled = false;
        _isSyncing = true;
        TxtSyncStatus.Text = L("ToolCmdLibSyncInProgress");
        TxtSyncStatus.Visibility = Visibility.Visible;

        try
        {
            var result = await Task.Run(() => gitSync.FullSyncAsync());

            if (result.Success)
            {
                await ReloadActionsAsync();
                var hasWarnings = result.Warnings?.Count > 0;
                MessageDialog.ShowMessage(
                    Window.GetWindow(this),
                    L(hasWarnings ? "ToolCmdLibSyncPartial" : "ToolCmdLibSyncComplete"),
                    result.Message ?? L("ToolCmdLibSyncComplete"),
                    hasWarnings ? "warning" : "info",
                    L("BtnOk"));
            }
            else
            {
                await ReloadActionsAsync();
                MessageDialog.ShowMessage(
                    Window.GetWindow(this),
                    L("ToolCmdLibSyncError"),
                    result.ErrorDetails ?? result.Message ?? L("ToolCmdLibSyncError"),
                    "error",
                    L("BtnOk"));
            }
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Sync failed: {ex.Message}");
            MessageDialog.ShowMessage(
                Window.GetWindow(this),
                L("ToolCmdLibSyncError"),
                ex.Message,
                "error",
                L("BtnOk"));
        }
        finally
        {
            _isSyncing = false;
            TxtSyncStatus.Visibility = Visibility.Collapsed;
            BtnSync.IsEnabled = true;
        }
    }

    // ── History panel ─────────────────────────────────────────────

    private void OnHistoryToggle(object sender, RoutedEventArgs e)
    {
        if (HistoryPanel.Visibility == Visibility.Visible)
        {
            HistoryPanel.Visibility = Visibility.Collapsed;
            return;
        }

        GeneratorPanel.Visibility = Visibility.Collapsed;
        HistoryPanel.Visibility = Visibility.Visible;
        _ = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        if (_services is null) return;

        try
        {
            using var scope = _services.CreateScope();
            var historyService = scope.ServiceProvider.GetRequiredService<ICommandHistoryService>();
            var recent = (await historyService.GetRecentAsync(50)).ToList();

            var items = recent.Select(h => new HistoryEntry
            {
                ActionTitle = h.ActionTitle,
                GeneratedCommand = h.GeneratedCommand,
                Timestamp = h.CreatedAt.ToLocalTime().ToString("g")
            }).ToList();

            HistoryList.ItemsSource = items;
            TxtHistoryEmpty.Visibility = items.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Failed to load history: {ex.Message}");
            ShowErrorToast(ex.Message);
        }
    }

    private void OnHistoryItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryEntry entry) return;

        try { Clipboard.SetText(entry.GeneratedCommand); }
        catch (System.Runtime.InteropServices.ExternalException) { return; }

        ShowHistoryCopyFeedback();
    }

    private void ShowHistoryCopyFeedback()
    {
        TxtHistoryCopied.Text = L("ToolCmdLibCopied");
        TxtHistoryCopied.Visibility = Visibility.Visible;

        if (_historyCopyTimer is null)
        {
            _historyCopyTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _historyCopyTimer.Tick += OnHistoryCopyTimerTick;
        }

        _historyCopyTimer.Stop();
        _historyCopyTimer.Start();
    }

    private void OnHistoryCopyTimerTick(object? sender, EventArgs e)
    {
        TxtHistoryCopied.Visibility = Visibility.Collapsed;
        _historyCopyTimer?.Stop();
    }

    private void OnHistoryCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string command } || string.IsNullOrEmpty(command)) return;

        try { Clipboard.SetText(command); }
        catch (System.Runtime.InteropServices.ExternalException) { return; }

        if (sender is Button btn)
            CopyFeedbackHelper.ShowCopyFeedback(btn);
    }

    private async void OnClearHistory(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;

        var confirmed = MessageDialog.ShowConfirm(
            Window.GetWindow(this),
            L("ToolCmdLibHistoryClearTitle"),
            L("ToolCmdLibHistoryClearConfirm"),
            "warning",
            L("BtnYes"), L("BtnNo"));

        if (!confirmed) return;

        try
        {
            using var scope = _services.CreateScope();
            var historyService = scope.ServiceProvider.GetRequiredService<ICommandHistoryService>();
            await historyService.ClearAllAsync();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Failed to clear history: {ex.Message}");
            ShowErrorToast(ex.Message);
        }
    }

    // ── Help ───────────────────────────────────────────────────────

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (HelpPanel.Visibility == Visibility.Visible)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
            return;
        }
        TxtHelpContent.Text = L("ToolHelpCMDLIB").Replace("\\n", "\n");
        HelpPanel.Visibility = Visibility.Visible;
    }

    private void OnCloseHelpClick(object sender, RoutedEventArgs e)
    {
        HelpPanel.Visibility = Visibility.Collapsed;
    }

    // ── Example actions ──────────────────────────────────────────────

    private void OnExampleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string command } && !string.IsNullOrEmpty(command))
        {
            TxtGenerated.Text = command;
            TxtGenerated.Foreground = TryGetBrush("AccentBrush", System.Windows.Media.Brushes.DodgerBlue);
            TxtValidationError.Text = string.Empty;
            TxtValidationError.Visibility = Visibility.Collapsed;
            _commandValid = true;
            BtnCopy.IsEnabled = true;
            BtnSend.IsEnabled = true;
        }
    }

    private void OnExampleCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string command } && !string.IsNullOrEmpty(command))
        {
            try { Clipboard.SetText(command); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }

            if (sender is Button btn)
                CopyFeedbackHelper.ShowCopyFeedback(btn);
        }
    }

    // ── Link navigation ──────────────────────────────────────────────

    private void OnLinkNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            if (e.Uri.Scheme is not ("http" or "https")) return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Heimdall.Core.Logging.FileLogger.Warn(
                $"[CommandLibrary] Failed to open link: {ex.Message}");
            ShowErrorToast(ex.Message);
        }
        e.Handled = true;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private string GetInitialParameterValue(TemplateParameter parameter)
    {
        if (!string.IsNullOrWhiteSpace(_targetHost)
            && CanPrefillTargetHost(parameter, _targetHost))
        {
            return _targetHost;
        }

        return parameter.DefaultValue ?? "";
    }

    private static bool CanPrefillTargetHost(TemplateParameter parameter, string targetHost)
    {
        if (string.Equals(parameter.Type, "ipaddress", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.TryParse(targetHost, out _);
        }

        return IsHostParameter(parameter.Name, parameter.Type);
    }

    private static bool IsHostParameter(string name, string? type)
    {
        if (string.Equals(type, "hostname", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "ipaddress", StringComparison.OrdinalIgnoreCase))
            return true;

        var n = name.ToLowerInvariant();
        return n is "host" or "hostname" or "target" or "targethost"
            or "computername" or "server" or "ip" or "ipaddress"
            or "remotehost" or "remotecomputer";
    }

    private void ShowErrorToast(string message)
    {
        MessageDialog.ShowMessage(
            Window.GetWindow(this),
            L("ToolCmdLibErrorTitle"),
            message,
            "error",
            L("BtnOk"));
    }

    private string L(string key) => _localizer?[key] ?? key;

    private static Brush TryGetBrush(string key, Brush fallback)
        => System.Windows.Application.Current.TryFindResource(key) as Brush ?? fallback;

    public bool CanClose() => !_isSyncing && !_isBusy;

    public void Dispose()
    {
        if (_historyCopyTimer is not null)
        {
            _historyCopyTimer.Tick -= OnHistoryCopyTimerTick;
            _historyCopyTimer.Stop();
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _serviceScope?.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── View models ────────────────────────────────────────────────

    internal sealed class ActionEntry
    {
        private readonly CommandLibraryView _parent;
        public ActionModel Source { get; }

        public ActionEntry(ActionModel source, CommandLibraryView parent)
        {
            Source = source;
            _parent = parent;
        }

        public string Title => Source.Title;
        public string Description => Source.Description ?? "";
        public string Category => Source.Category;

        public string FavoriteIcon => _parent._favoriteIds.Contains(Source.Id) ? "\u2605" : "\u2606";

        public string FavoriteTooltip => _parent._favoriteIds.Contains(Source.Id)
            ? _parent.L("ToolCmdLibFavoriteRemove")
            : _parent.L("ToolCmdLibFavoriteAdd");

        public int SearchRank => _parent._searchRankedIds?.IndexOf(Source.Id) is >= 0 and var idx
            ? idx : int.MaxValue;

        public string PlatformLabel => Source.Platform switch
        {
            Platform.Windows => _parent.L("ToolCmdLibPlatformLabelWin"),
            Platform.Linux => _parent.L("ToolCmdLibPlatformLabelLin"),
            _ => _parent.L("ToolCmdLibPlatformLabelBoth")
        };

        public Brush RiskBrush => Source.Level switch
        {
            CriticalityLevel.Info => TryGetBrush("TextSecondaryBrush", Brushes.Gray),
            CriticalityLevel.Run => TryGetBrush("WarningBrush", Brushes.Orange),
            CriticalityLevel.Dangerous => TryGetBrush("ErrorBrush", Brushes.Red),
            _ => TryGetBrush("TextSecondaryBrush", Brushes.Gray)
        };

        public string RiskLabel => Source.Level switch
        {
            CriticalityLevel.Info => _parent.L("ToolCmdLibRiskInfo"),
            CriticalityLevel.Run => _parent.L("ToolCmdLibRiskRun"),
            CriticalityLevel.Dangerous => _parent.L("ToolCmdLibRiskDangerous"),
            _ => ""
        };

        public string RiskBadge => Source.Level switch
        {
            CriticalityLevel.Info => _parent.L("ToolCmdLibRiskBadgeInfo"),
            CriticalityLevel.Run => _parent.L("ToolCmdLibRiskBadgeRun"),
            CriticalityLevel.Dangerous => _parent.L("ToolCmdLibRiskBadgeDanger"),
            _ => ""
        };
    }

    internal sealed class ParameterEntry
    {
        public string Name { get; init; } = "";
        public string Label { get; init; } = "";
        public string Value { get; set; } = "";
        public bool Required { get; init; }
        public string? Description { get; init; }
        public string Type { get; init; } = "string";
        public string DisplayLabel
            => $"{(string.IsNullOrWhiteSpace(Label) ? Name : Label)}{(Required ? " *" : "")}";
        public string DisplayTooltip => string.IsNullOrEmpty(Description)
            ? $"[{Type}]"
            : $"[{Type}] {Description}";
    }

    internal sealed class HistoryEntry
    {
        public string ActionTitle { get; init; } = "";
        public string GeneratedCommand { get; init; } = "";
        public string Timestamp { get; init; } = "";
    }
}
