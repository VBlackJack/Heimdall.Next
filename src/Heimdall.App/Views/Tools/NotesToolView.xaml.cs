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

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Heimdall.App.Services;
using Heimdall.Core.Localization;
using System.Windows.Input;
using Heimdall.Core.Models;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace Heimdall.App.Views.Tools;

public partial class NotesToolView : UserControl, IToolView
{
    private readonly NotesStorageService _storage;
    private readonly DispatcherTimer _saveTimer;
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    private CompletionWindow? _completionWindow;
    private LocalizationManager? _localizer;
    private bool _useMilkdown;
    private ToolContext? _context;
    private IReadOnlyList<NoteListItem> _allNotes = Array.Empty<NoteListItem>();
    private CancellationTokenSource? _refreshCts;
    private NoteSortOrder _sortOrder = NoteSortOrder.DateDescending;
    private bool _dirty;
    private bool _disposed;
    private bool _initializeRequested;
    private bool _initializeStarted;
    private bool _isLoadingNote;
    private bool _suppressSelectionChanged;
    private string? _currentNotePath;
    private string? _selectedTag;

    public NotesToolView()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        _storage = CreateStorageService();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
        _saveTimer.Tick += OnSaveTimerTick;

        Editor.TextChanged += OnEditorTextChanged;
        Editor.TextArea.TextEntered += OnTextAreaTextEntered;
        Editor.TextArea.PreviewMouseDown += OnEditorPreviewMouseDown;
        Editor.IsReadOnly = true;
        Editor.Options.ConvertTabsToSpaces = false;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = false;

        try
        {
            Editor.SyntaxHighlighting = MarkdownHighlighting.Create();
            Editor.TextArea.TextView.LineTransformers.Add(new MarkdownLivePreviewTransformer());
        }
        catch { /* Highlighting is cosmetic — do not block initialization */ }
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _context = context;
        _localizer = localizer;

        ApplyLocalization();
        _initializeRequested = true;
        StartInitializationIfNeeded();
    }

    public bool CanClose()
    {
        _saveTimer.Stop();

        if (!_dirty || _currentNotePath is null)
        {
            return true;
        }

        try
        {
            var content = _useMilkdown ? _lastMilkdownContent : Editor.Text;
            _storage.SaveNoteAsync(_currentNotePath, content).GetAwaiter().GetResult();
            _dirty = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartInitializationIfNeeded(fromLoadedEvent: true);
    }

    private async void StartInitializationIfNeeded(bool fromLoadedEvent = false)
    {
        if (!_initializeRequested || _initializeStarted || _disposed)
        {
            return;
        }

        // IsLoaded can sometimes be false inside the Loaded event handler depending on TabControl virtualization.
        if (!fromLoadedEvent && !IsLoaded)
        {
            return;
        }

        _initializeStarted = true;
        await InitializeAsync(_context).ConfigureAwait(true);
    }

    private async Task InitializeAsync(ToolContext? context)
    {
        try
        {
            Core.Logging.FileLogger.Info(
                $"NotesToolView init start: Loaded={IsLoaded}, " +
                $"MilkdownControlLoaded={MilkdownEditor.IsLoaded}, " +
                $"MilkdownAsset={MilkdownEditorControl.IsAvailable}, " +
                $"WebView2={Services.WebView2Helper.IsAvailable}");

            _storage.EnsureInitialized();
            UpdateSelectionState();
            SetStatus(L("ToolNotesStatusReady"));

            await TryInitializeMilkdownAsync().ConfigureAwait(true);

            var requestedPath = ResolveRequestedPath(context);
            await ReloadNotesAsync(requestedPath).ConfigureAwait(true);

            Core.Logging.FileLogger.Info(
                $"NotesToolView init complete: UseMilkdown={_useMilkdown}, CurrentNote={_currentNotePath ?? "<none>"}");
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error("NotesToolView init failed", ex);
            _useMilkdown = false;
            UpdateSelectionState();
            SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
        }
    }

    private async Task TryInitializeMilkdownAsync()
    {
        Core.Logging.FileLogger.Info(
            $"Milkdown check: IsAvailable={MilkdownEditorControl.IsAvailable}, " +
            $"WebView2={Services.WebView2Helper.IsAvailable}");

        if (!MilkdownEditorControl.IsAvailable)
        {
            Core.Logging.FileLogger.Info("Milkdown not available, using AvalonEdit fallback");
            return;
        }

        try
        {
            MilkdownEditor.ContentChanged += OnMilkdownContentChanged;
            MilkdownEditor.LinkClicked += OnMilkdownLinkClicked;
            MilkdownEditor.EditorReady += OnMilkdownReady;

            // Keep the control in the visual tree during startup without exposing
            // it until the WebView2 host is fully initialized.
            MilkdownEditor.Visibility = Visibility.Hidden;
            await MilkdownEditor.InitializeAsync().ConfigureAwait(true);

            _useMilkdown = true;
            UpdateSelectionState();
            Core.Logging.FileLogger.Info("Milkdown editor initialized successfully");
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Warn($"Milkdown init failed, fallback to AvalonEdit: {ex.Message}");
            _useMilkdown = false;
            MilkdownEditor.Visibility = Visibility.Collapsed;
            Editor.Visibility = Visibility.Visible;
        }
    }

    private void OnMilkdownReady()
    {
        SetStatus(L("ToolNotesStatusReady"));
    }

    private void OnMilkdownContentChanged(string markdown, bool dirty)
    {
        _lastMilkdownContent = markdown;

        if (_isLoadingNote || _currentNotePath is null)
        {
            return;
        }

        if (dirty)
        {
            _dirty = true;
            UpdateSelectedNoteHeader();
            SetStatus(L("ToolNotesStatusModified"));
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private async void OnMilkdownLinkClicked(string noteReference)
    {
        try
        {
            await OpenLinkedNoteAsync(noteReference).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
        }
    }

    private async Task ReloadNotesAsync(string? preferredPath = null)
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();

        try
        {
            var notes = await _storage.ListNotesAsync(SearchTextBox.Text, _sortOrder, _refreshCts.Token).ConfigureAwait(true);
            if (_refreshCts.IsCancellationRequested)
            {
                return;
            }

            _allNotes = notes;
            RebuildTagFilterButtons();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
            return;
        }

        await RefreshVisibleNotesAsync(preferredPath).ConfigureAwait(true);
    }

    private async Task RefreshVisibleNotesAsync(string? preferredPath = null)
    {
        var selectedNote = ApplyVisibleNotes(preferredPath);
        if (selectedNote is not null)
        {
            await OpenNoteAsync(selectedNote.FilePath).ConfigureAwait(true);
        }
        else
        {
            ClearCurrentNote();
        }
    }

    private NoteListItem? ApplyVisibleNotes(string? preferredPath = null)
    {
        var visibleNotes = GetVisibleNotes();
        var tree = NoteTreeNode.BuildTree(visibleNotes, _storage.NotesRootPath);

        _suppressSelectionChanged = true;
        try
        {
            NotesTreeView.ItemsSource = tree;
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        ListFooterText.Text = string.Format(L("ToolNotesCount"), visibleNotes.Count);

        var targetPath = preferredPath ?? _currentNotePath;
        var target = visibleNotes.FirstOrDefault(n =>
            string.Equals(n.FilePath, targetPath, StringComparison.OrdinalIgnoreCase));

        return target ?? visibleNotes.FirstOrDefault();
    }

    private IReadOnlyList<NoteListItem> GetVisibleNotes()
    {
        if (string.IsNullOrWhiteSpace(_selectedTag))
        {
            return _allNotes;
        }

        return _allNotes
            .Where(note => note.Tags.Any(tag => string.Equals(tag, _selectedTag, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void RebuildTagFilterButtons()
    {
        var availableTags = _allNotes
            .SelectMany(note => note.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_selectedTag is not null
            && !availableTags.Contains(_selectedTag, StringComparer.OrdinalIgnoreCase))
        {
            _selectedTag = null;
        }

        var hasTags = availableTags.Count > 0;
        TagsLabelText.Visibility = hasTags ? Visibility.Visible : Visibility.Collapsed;
        TagFilterWrapPanel.Visibility = hasTags ? Visibility.Visible : Visibility.Collapsed;
        TagFilterWrapPanel.Children.Clear();

        if (!hasTags)
        {
            return;
        }

        AddTagFilterButton(null, L("ToolNotesAllTags"));
        foreach (var tag in availableTags)
        {
            AddTagFilterButton(tag, tag);
        }
    }

    private void AddTagFilterButton(string? tag, string label)
    {
        var isSelected = string.IsNullOrWhiteSpace(tag)
            ? string.IsNullOrWhiteSpace(_selectedTag)
            : string.Equals(tag, _selectedTag, StringComparison.OrdinalIgnoreCase);

        var button = new Button
        {
            Content = label,
            Tag = tag,
            Style = (Style)FindResource(isSelected ? "PrimaryButtonStyle" : "SecondaryButtonStyle"),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 8)
        };
        button.Click += OnTagFilterClick;
        System.Windows.Automation.AutomationProperties.SetName(button, label);

        TagFilterWrapPanel.Children.Add(button);
    }

    private async Task OpenNoteAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            ClearCurrentNote();
            return;
        }

        if (string.Equals(_currentNotePath, filePath, StringComparison.OrdinalIgnoreCase) && !_isLoadingNote)
        {
            UpdateSelectionState();
            return;
        }

        await FlushPendingChangesAsync().ConfigureAwait(true);

        _isLoadingNote = true;
        try
        {
            _currentNotePath = filePath;
            var content = await _storage.LoadNoteAsync(filePath).ConfigureAwait(true);

            if (_useMilkdown)
            {
                MilkdownEditor.SetContent(content);
                MilkdownEditor.SetReadOnly(false);
            }
            else
            {
                Editor.Text = content;
                Editor.CaretOffset = 0;
            }

            _dirty = false;

            UpdateSelectedNoteHeader();
            UpdateSelectionState();
            SetStatus(string.Format(L("ToolNotesStatusOpened"), _storage.GetRelativePath(filePath)));
        }
        finally
        {
            _isLoadingNote = false;
        }
    }

    private void ClearCurrentNote()
    {
        _saveTimer.Stop();
        _currentNotePath = null;
        _dirty = false;

        _isLoadingNote = true;
        try
        {
            if (_useMilkdown)
            {
                MilkdownEditor.SetContent(string.Empty);
                MilkdownEditor.SetReadOnly(true);
            }
            else
            {
                Editor.Text = string.Empty;
            }
        }
        finally
        {
            _isLoadingNote = false;
        }

        UpdateSelectedNoteHeader();
        UpdateSelectionState();
    }

    private async Task CreateNoteAsync(NoteTemplateKind templateKind)
    {
        try
        {
            await FlushPendingChangesAsync().ConfigureAwait(true);
            var notePath = await _storage.CreateNoteAsync(templateKind, _context).ConfigureAwait(true);
            await ReloadNotesAsync(notePath).ConfigureAwait(true);

            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_useMilkdown)
                {
                    MilkdownEditor.FocusEditor();
                }
                else
                {
                    Editor.Focus();
                    Editor.SelectAll();
                }
            });
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
        }
    }

    private async Task DeleteCurrentNoteAsync()
    {
        if (_currentNotePath is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(L("ToolNotesDeleteConfirm"), SelectedNoteTitleText.Text),
            L("ConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var deletedPath = _currentNotePath;
            await _storage.DeleteNoteAsync(deletedPath).ConfigureAwait(true);
            _currentNotePath = null;
            await ReloadNotesAsync().ConfigureAwait(true);
            SetStatus(string.Format(L("ToolNotesStatusDeleted"), Path.GetFileNameWithoutExtension(deletedPath)));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
        }
    }

    private async Task RenameCurrentNoteAsync()
    {
        if (_currentNotePath is null)
        {
            return;
        }

        await FlushPendingChangesAsync().ConfigureAwait(true);

        var currentName = Path.GetFileNameWithoutExtension(_currentNotePath);
        var dialog = new Views.Dialogs.InputDialog(_localizer)
        {
            Title = L("ToolNotesRenameTitle"),
            Prompt = L("ToolNotesRenamePrompt"),
            InputText = currentName,
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.InputText))
        {
            return;
        }

        try
        {
            var newPath = await _storage.RenameNoteAsync(_currentNotePath, dialog.InputText).ConfigureAwait(true);
            _currentNotePath = newPath;
            await ReloadNotesAsync(newPath).ConfigureAwait(true);
            SetStatus(string.Format(L("ToolNotesStatusRenamed"), _storage.GetRelativePath(newPath)));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
        }
    }

    private async Task DuplicateCurrentNoteAsync()
    {
        if (_currentNotePath is null)
        {
            return;
        }

        await FlushPendingChangesAsync().ConfigureAwait(true);

        try
        {
            var copyPath = await _storage.DuplicateNoteAsync(_currentNotePath).ConfigureAwait(true);
            await ReloadNotesAsync(copyPath).ConfigureAwait(true);
            SetStatus(string.Format(L("ToolNotesStatusDuplicated"), _storage.GetRelativePath(copyPath)));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
        }
    }

    private async Task FlushPendingChangesAsync()
    {
        _saveTimer.Stop();

        if (!_dirty || _currentNotePath is null)
        {
            return;
        }

        SetStatus(L("ToolNotesStatusSaving"));
        var content = _useMilkdown ? await GetMilkdownContentAsync().ConfigureAwait(true) : Editor.Text;
        await _storage.SaveNoteAsync(_currentNotePath, content).ConfigureAwait(true);
        _dirty = false;
        SetStatus(string.Format(L("ToolNotesStatusSaved"), DateTime.Now.ToString("HH:mm:ss")));
    }

    private async void OnEditorPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            || _currentNotePath is null)
        {
            return;
        }

        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (position is null)
        {
            return;
        }

        var offset = Editor.Document.GetOffset(position.Value.Location);
        var line = Editor.Document.GetLineByOffset(offset);
        var lineText = Editor.Document.GetText(line);
        var col = offset - line.Offset;

        foreach (System.Text.RegularExpressions.Match match in
            SimpleMarkdownConverter.NoteLinkRegex().Matches(lineText))
        {
            if (col >= match.Index && col < match.Index + match.Length)
            {
                var noteRef = match.Groups[1].Value.Trim();
                e.Handled = true;
                try
                {
                    await OpenLinkedNoteAsync(noteRef).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
                }
                return;
            }
        }
    }

    private async Task OpenLinkedNoteAsync(string noteReference)
    {
        var notePath = await _storage.ResolveOrCreateNoteAsync(noteReference).ConfigureAwait(true);

        if (string.Equals(_currentNotePath, notePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_currentNotePath is not null)
        {
            _backStack.Push(_currentNotePath);
            _forwardStack.Clear();
            UpdateNavButtons();
        }

        await ReloadNotesAsync(notePath).ConfigureAwait(true);
    }

    private string _lastMilkdownContent = string.Empty;

    private Task<string> GetMilkdownContentAsync()
    {
        // Content is synced via ContentChanged events; use the last known value
        return Task.FromResult(_lastMilkdownContent);
    }

    private void ApplyLocalization()
    {
        HeaderTitle.Text = L("ToolNotesTitle");
        BtnOpenFolder.Content = L("ToolNotesBtnOpenFolder");
        BtnNewNote.ToolTip = L("ToolNotesBtnNew");
        BtnCopyConfluence.Content = L("ToolNotesBtnCopyConfluence");
        BtnExportConfluence.Content = L("ToolNotesBtnExportConfluence");
        BtnExportHtml.Content = L("ToolNotesBtnExportHtml");

        SearchTextBox.Tag = L("ToolNotesSearchPlaceholder");
        TagsLabelText.Text = L("ToolNotesTagsLabel");

        EditorEmptyTitleText.Text = L("ToolNotesEmptyTitle");
        EditorEmptyBodyText.Text = L("ToolNotesEmptyBody");

        BtnCollapseAll.ToolTip = L("ToolNotesCollapseAll");
        BtnExpandAll.ToolTip = L("ToolNotesExpandAll");
        BtnMarkdownHelp.Content = L("ToolNotesMarkdownHelpBtn");
        BtnHelp.ToolTip = L("ToolHelpTooltip");
        System.Windows.Automation.AutomationProperties.SetName(BtnCollapseAll, L("ToolNotesCollapseAll"));
        System.Windows.Automation.AutomationProperties.SetName(BtnExpandAll, L("ToolNotesExpandAll"));
        System.Windows.Automation.AutomationProperties.SetName(BtnMarkdownHelp, L("ToolNotesMarkdownHelpBtn"));
        System.Windows.Automation.AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        System.Windows.Automation.AutomationProperties.SetName(BtnOpenFolder, L("ToolNotesBtnOpenFolder"));
        System.Windows.Automation.AutomationProperties.SetName(BtnNewNote, L("ToolNotesBtnNew"));
        System.Windows.Automation.AutomationProperties.SetName(BtnCopyConfluence, L("ToolNotesBtnCopyConfluence"));
        System.Windows.Automation.AutomationProperties.SetName(BtnExportConfluence, L("ToolNotesBtnExportConfluence"));
        System.Windows.Automation.AutomationProperties.SetName(BtnExportHtml, L("ToolNotesBtnExportHtml"));
        System.Windows.Automation.AutomationProperties.SetName(SearchTextBox, L("ToolNotesSearchPlaceholder"));
        System.Windows.Automation.AutomationProperties.SetName(NotesTreeView, L("ToolNotesListTitle"));
        System.Windows.Automation.AutomationProperties.SetName(TagFilterWrapPanel, L("ToolNotesTagsLabel"));
        BtnNavBack.ToolTip = L("ToolNotesNavBack");
        BtnNavForward.ToolTip = L("ToolNotesNavForward");
        System.Windows.Automation.AutomationProperties.SetName(BtnNavBack, L("ToolNotesNavBack"));
        System.Windows.Automation.AutomationProperties.SetName(BtnNavForward, L("ToolNotesNavForward"));

        RebuildTagFilterButtons();
    }

    private void UpdateSelectionState()
    {
        var hasSelection = _currentNotePath is not null;

        if (_useMilkdown)
        {
            Editor.Visibility = Visibility.Collapsed;
            MilkdownEditor.Visibility = Visibility.Visible;
            MilkdownEditor.SetReadOnly(!hasSelection);
            EditorEmptyStatePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            MilkdownEditor.Visibility = Visibility.Collapsed;
            Editor.Visibility = Visibility.Visible;
            Editor.IsReadOnly = !hasSelection;
            EditorEmptyStatePanel.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        }
        BtnCopyConfluence.IsEnabled = hasSelection;
        BtnExportConfluence.IsEnabled = hasSelection;
        BtnExportHtml.IsEnabled = hasSelection;
    }

    private void UpdateSelectedNoteHeader()
    {
        if (_currentNotePath is null)
        {
            SelectedNoteTitleText.Text = L("ToolNotesNoSelection");
            SelectedNotePathText.Text = _storage.NotesRootPath;
            return;
        }

        var editorContent = _useMilkdown ? _lastMilkdownContent : Editor.Text;
        SelectedNoteTitleText.Text = ExtractHeaderTitle(editorContent, _currentNotePath);
        SelectedNotePathText.Text = _storage.GetRelativePath(_currentNotePath);
    }

    private static string ExtractHeaderTitle(string markdown, string filePath)
    {
        foreach (var rawLine in (markdown ?? string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }

        return Path.GetFileNameWithoutExtension(filePath);
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? GetBrush("ErrorBrush")
            : GetBrush("TextSecondaryBrush");
    }

    private Brush GetBrush(string resourceKey)
        => (Brush)(TryFindResource(resourceKey) ?? Brushes.LightGray);

    private string? ResolveRequestedPath(ToolContext? context)
    {
        if (string.IsNullOrWhiteSpace(context?.Argument))
        {
            return null;
        }

        var resolved = Path.IsPathRooted(context.Argument)
            ? context.Argument
            : Path.Combine(_storage.NotesRootPath, context.Argument);

        return _storage.IsUnderNotesRoot(resolved) ? resolved : null;
    }

    private static NotesStorageService CreateStorageService()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;

        // Attempt to read NotesDirectory from settings.json (lightweight, no DI)
        var settingsPath = Path.Combine(basePath, "config", "settings.json");
        if (File.Exists(settingsPath))
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (json.RootElement.TryGetProperty("NotesDirectory", out var prop)
                    && prop.GetString() is { Length: > 0 } customDir)
                {
                    var resolved = Path.IsPathRooted(customDir)
                        ? customDir
                        : Path.Combine(basePath, customDir);
                    return new NotesStorageService(resolved);
                }
            }
            catch
            {
                // Fall through to default
            }
        }

        return new NotesStorageService(Path.Combine(basePath, "config", "notes"));
    }

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private string L(string key) => _localizer?[key] ?? key;

    private async void OnNewNoteClick(object sender, RoutedEventArgs e)
        => await CreateNoteAsync(NoteTemplateKind.Blank).ConfigureAwait(true);

    private async void OnDailyNoteClick(object sender, RoutedEventArgs e)
        => await CreateNoteAsync(NoteTemplateKind.Daily).ConfigureAwait(true);

    private async void OnIncidentNoteClick(object sender, RoutedEventArgs e)
        => await CreateNoteAsync(NoteTemplateKind.Incident).ConfigureAwait(true);

    private async void OnProcedureNoteClick(object sender, RoutedEventArgs e)
        => await CreateNoteAsync(NoteTemplateKind.Procedure).ConfigureAwait(true);

    private async void OnRenameNoteClick(object sender, RoutedEventArgs e)
        => await RenameCurrentNoteAsync().ConfigureAwait(true);

    private async void OnDuplicateNoteClick(object sender, RoutedEventArgs e)
        => await DuplicateCurrentNoteAsync().ConfigureAwait(true);

    private async void OnDeleteNoteClick(object sender, RoutedEventArgs e)
        => await DeleteCurrentNoteAsync().ConfigureAwait(true);

    private async void OnNotesTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressSelectionChanged)
        {
            return;
        }

        if (e.NewValue is NoteTreeNode { FilePath: not null } node)
        {
            await OpenNoteAsync(node.FilePath).ConfigureAwait(true);
        }
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        => await ReloadNotesAsync(_currentNotePath).ConfigureAwait(true);

    private void OnTreeViewPreviewRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Prevent the session tab context menu from intercepting the right-click
        e.Handled = true;

        // Select the clicked tree item
        if (e.OriginalSource is DependencyObject source)
        {
            var treeViewItem = FindParent<TreeViewItem>(source);
            if (treeViewItem is not null)
            {
                treeViewItem.IsSelected = true;
            }
        }

        // Rebuild and show our context menu
        OnTreeViewContextMenuOpening(sender, null!);
        TreeContextMenu.IsOpen = true;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T target) return target;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnTreeViewContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        TreeContextMenu.Items.Clear();

        var treeItem = NotesTreeView.SelectedItem as NoteTreeNode;

        // ── Create ──
        var newBlank = new MenuItem { Header = L("ToolNotesBtnNew") };
        newBlank.Click += async (_, _) => await CreateNoteAsync(NoteTemplateKind.Blank).ConfigureAwait(true);
        TreeContextMenu.Items.Add(newBlank);

        var newDaily = new MenuItem { Header = L("ToolNotesBtnDaily") };
        newDaily.Click += async (_, _) => await CreateNoteAsync(NoteTemplateKind.Daily).ConfigureAwait(true);
        TreeContextMenu.Items.Add(newDaily);

        var newIncident = new MenuItem { Header = L("ToolNotesBtnIncident") };
        newIncident.Click += async (_, _) => await CreateNoteAsync(NoteTemplateKind.Incident).ConfigureAwait(true);
        TreeContextMenu.Items.Add(newIncident);

        var newProcedure = new MenuItem { Header = L("ToolNotesBtnProcedure") };
        newProcedure.Click += async (_, _) => await CreateNoteAsync(NoteTemplateKind.Procedure).ConfigureAwait(true);
        TreeContextMenu.Items.Add(newProcedure);

        TreeContextMenu.Items.Add(new Separator());

        // ── New Folder ──
        var newFolder = new MenuItem { Header = L("ToolNotesNewFolder") };
        newFolder.Click += (_, _) => CreateFolderFromContextMenu(treeItem);
        TreeContextMenu.Items.Add(newFolder);

        // ── File actions ──
        if (treeItem is { FilePath: not null })
        {
            TreeContextMenu.Items.Add(new Separator());

            var rename = new MenuItem { Header = L("ToolNotesBtnRename") };
            rename.Click += async (_, _) => await RenameCurrentNoteAsync().ConfigureAwait(true);
            TreeContextMenu.Items.Add(rename);

            var duplicate = new MenuItem { Header = L("ToolNotesBtnDuplicate") };
            duplicate.Click += async (_, _) => await DuplicateCurrentNoteAsync().ConfigureAwait(true);
            TreeContextMenu.Items.Add(duplicate);

            TreeContextMenu.Items.Add(new Separator());

            var openFolder = new MenuItem { Header = L("ToolNotesBtnOpenFolder") };
            openFolder.Click += (_, _) => OpenFolder(
                Path.GetDirectoryName(treeItem.FilePath) ?? _storage.NotesRootPath);
            TreeContextMenu.Items.Add(openFolder);

            TreeContextMenu.Items.Add(new Separator());

            var delete = new MenuItem
            {
                Header = L("ToolNotesBtnDelete"),
                Foreground = Application.Current.TryFindResource("ErrorBrush") as System.Windows.Media.Brush
                    ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red)
            };
            delete.Click += async (_, _) => await DeleteCurrentNoteAsync().ConfigureAwait(true);
            TreeContextMenu.Items.Add(delete);
        }

        // ── Folder actions ──
        if (treeItem is { IsFolder: true, FolderPath: not null })
        {
            TreeContextMenu.Items.Add(new Separator());

            var openInExplorer = new MenuItem { Header = L("ToolNotesBtnOpenFolder") };
            openInExplorer.Click += (_, _) => OpenFolder(treeItem.FolderPath);
            TreeContextMenu.Items.Add(openInExplorer);
        }
    }

    private async void CreateFolderFromContextMenu(NoteTreeNode? contextNode)
    {
        var parentFolder = contextNode?.FolderPath
            ?? (contextNode?.FilePath is not null ? Path.GetDirectoryName(contextNode.FilePath) : null)
            ?? _storage.NotesRootPath;

        var dialog = new Views.Dialogs.InputDialog(_localizer)
        {
            Title = L("ToolNotesNewFolder"),
            Prompt = L("ToolNotesNewFolderPrompt"),
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.InputText))
        {
            return;
        }

        try
        {
            _storage.CreateFolder(dialog.InputText, parentFolder);
            await ReloadNotesAsync(_currentNotePath).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
        }
    }

    private async void OnTagFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        _selectedTag = button.Tag as string;
        RebuildTagFilterButtons();
        await RefreshVisibleNotesAsync(_currentNotePath).ConfigureAwait(true);
    }

    // ── TreeView internal drag & drop ─────────────────────────────

    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;

    private void OnTreeViewPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(NotesTreeView);
    }

    private void OnTreeViewPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _isDragging)
        {
            return;
        }

        var currentPos = e.GetPosition(NotesTreeView);
        if (Math.Abs(currentPos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (NotesTreeView.SelectedItem is not NoteTreeNode { FilePath: not null } sourceNode)
        {
            return;
        }

        _isDragging = true;
        try
        {
            var data = new System.Windows.DataObject("NoteTreeNode", sourceNode);
            DragDrop.DoDragDrop(NotesTreeView, data, System.Windows.DragDropEffects.Move);
        }
        finally
        {
            _isDragging = false;
        }
    }

    private void OnTreeViewInternalDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = System.Windows.DragDropEffects.None;

        if (!e.Data.GetDataPresent("NoteTreeNode"))
        {
            return;
        }

        var target = GetTreeNodeAtPoint(e);
        if (target is { IsFolder: true })
        {
            e.Effects = System.Windows.DragDropEffects.Move;
        }

        e.Handled = true;
    }

    private async void OnTreeViewInternalDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("NoteTreeNode"))
        {
            return;
        }

        var sourceNode = e.Data.GetData("NoteTreeNode") as NoteTreeNode;
        var targetNode = GetTreeNodeAtPoint(e);

        if (sourceNode?.FilePath is null || targetNode?.FolderPath is null)
        {
            return;
        }

        if (string.Equals(
            Path.GetDirectoryName(sourceNode.FilePath),
            targetNode.FolderPath,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var newPath = await _storage.MoveNoteToFolderAsync(sourceNode.FilePath, targetNode.FolderPath)
                .ConfigureAwait(true);
            if (string.Equals(_currentNotePath, sourceNode.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                _currentNotePath = newPath;
            }

            await ReloadNotesAsync(_currentNotePath).ConfigureAwait(true);
            SetStatus(string.Format(L("ToolNotesStatusMoved"), Path.GetFileName(newPath)));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
        }

        e.Handled = true;
    }

    private NoteTreeNode? GetTreeNodeAtPoint(System.Windows.DragEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return null;
        }

        var treeViewItem = FindParent<TreeViewItem>(source);
        return treeViewItem?.DataContext as NoteTreeNode;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isLoadingNote || _currentNotePath is null)
        {
            return;
        }

        _dirty = true;
        UpdateSelectedNoteHeader();
        SetStatus(L("ToolNotesStatusModified"));

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();

        try
        {
            await FlushPendingChangesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(L("ToolNotesStatusError"), ex.Message), isError: true);
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var path = _currentNotePath is not null
            ? Path.GetDirectoryName(_currentNotePath) ?? _storage.NotesRootPath
            : _storage.NotesRootPath;
        OpenFolder(path);
    }

    private void OnCopyConfluenceClick(object sender, RoutedEventArgs e)
    {
        if (_currentNotePath is null)
        {
            return;
        }

        var content = _useMilkdown ? _lastMilkdownContent : (Editor.Text ?? string.Empty);
        Clipboard.SetText(ConfluenceStorageConverter.Convert(content));
        CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
    }

    private void OnExportConfluenceClick(object sender, RoutedEventArgs e)
    {
        if (_currentNotePath is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = L("ToolNotesExportConfluenceFilter"),
            FileName = $"{Path.GetFileNameWithoutExtension(_currentNotePath)}.storage.xml"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var content = _useMilkdown ? _lastMilkdownContent : (Editor.Text ?? string.Empty);
        File.WriteAllText(dialog.FileName, ConfluenceStorageConverter.Convert(content));
        CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
    }

    private void OnExportHtmlClick(object sender, RoutedEventArgs e)
    {
        if (_currentNotePath is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = L("ToolNotesExportHtmlFilter"),
            FileName = $"{Path.GetFileNameWithoutExtension(_currentNotePath)}.html"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var content = _useMilkdown ? _lastMilkdownContent : (Editor.Text ?? string.Empty);
        var html = MarkdownPreviewBuilder.BuildHtmlDocument(content, SelectedNoteTitleText.Text);
        File.WriteAllText(dialog.FileName, html);
        CopyFeedbackHelper.ShowCopyFeedback(sender as Button);
    }

    // ── Navigation history ──────────────────────────────────────────

    private async void OnNavBackClick(object sender, RoutedEventArgs e)
    {
        if (_backStack.Count == 0)
        {
            return;
        }

        await FlushPendingChangesAsync().ConfigureAwait(true);

        if (_currentNotePath is not null)
        {
            _forwardStack.Push(_currentNotePath);
        }

        var target = _backStack.Pop();
        UpdateNavButtons();
        await ReloadNotesAsync(target).ConfigureAwait(true);
    }

    private async void OnNavForwardClick(object sender, RoutedEventArgs e)
    {
        if (_forwardStack.Count == 0)
        {
            return;
        }

        await FlushPendingChangesAsync().ConfigureAwait(true);

        if (_currentNotePath is not null)
        {
            _backStack.Push(_currentNotePath);
        }

        var target = _forwardStack.Pop();
        UpdateNavButtons();
        await ReloadNotesAsync(target).ConfigureAwait(true);
    }

    private void UpdateNavButtons()
    {
        BtnNavBack.IsEnabled = _backStack.Count > 0;
        BtnNavForward.IsEnabled = _forwardStack.Count > 0;
    }

    // ── Drag & drop ─────────────────────────────────────────────────

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            && e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files
            && files.Any(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }

        e.Handled = true;
    }

    private async void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        var imported = 0;
        foreach (var source in files.Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var fileName = Path.GetFileName(source);
                var target = Path.Combine(_storage.NotesRootPath, fileName);
                if (File.Exists(target))
                {
                    var baseName = Path.GetFileNameWithoutExtension(fileName);
                    target = Path.Combine(_storage.NotesRootPath,
                        $"{baseName}-{DateTime.Now:HHmmss}.md");
                }

                File.Copy(source, target);
                imported++;
            }
            catch
            {
                // Skip files that fail to copy
            }
        }

        if (imported > 0)
        {
            await ReloadNotesAsync().ConfigureAwait(true);
            SetStatus(string.Format(L("ToolNotesStatusImported"), imported));
        }
    }

    // ── Auto-completion for [[ ──────────────────────────────────────

    private void OnTextAreaTextEntered(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (e.Text != "[" || _currentNotePath is null)
        {
            return;
        }

        var offset = Editor.CaretOffset;
        if (offset < 2 || Editor.Document.GetCharAt(offset - 2) != '[')
        {
            return;
        }

        ShowNoteLinkCompletion();
    }

    private void ShowNoteLinkCompletion()
    {
        if (_completionWindow is not null)
        {
            return;
        }

        var window = new CompletionWindow(Editor.TextArea);
        var data = window.CompletionList.CompletionData;

        foreach (var note in _allNotes)
        {
            if (!string.Equals(note.FilePath, _currentNotePath, StringComparison.OrdinalIgnoreCase))
            {
                data.Add(new NoteLinkCompletionData(note.Title));
            }
        }

        if (data.Count == 0)
        {
            return;
        }

        _completionWindow = window;
        window.Show();
        window.Closed += (_, _) => _completionWindow = null;
    }

    // ── TreeView collapse/expand ──────────────────────────────────

    private void OnCollapseAllClick(object sender, RoutedEventArgs e)
        => SetAllTreeItemsExpanded(NotesTreeView, false);

    private void OnExpandAllClick(object sender, RoutedEventArgs e)
        => SetAllTreeItemsExpanded(NotesTreeView, true);

    private static void SetAllTreeItemsExpanded(ItemsControl parent, bool expanded)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
            {
                tvi.IsExpanded = expanded;
                SetAllTreeItemsExpanded(tvi, expanded);
            }
        }
    }

    // ── Help ────────────────────────────────────────────────────────

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(L("ToolHelpNOTES"), L("ToolHelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnMarkdownHelpClick(object sender, RoutedEventArgs e)
    {
        var help = L("ToolNotesMarkdownHelp");
        MessageBox.Show(help, L("ToolNotesMarkdownHelpTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveTimer.Stop();
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();

        if (_dirty && _currentNotePath is not null)
        {
            try
            {
                var content = _useMilkdown ? _lastMilkdownContent : Editor.Text;
                _storage.SaveNoteAsync(_currentNotePath, content).GetAwaiter().GetResult();
                _dirty = false;
            }
            catch
            {
                // Best effort flush during disposal.
            }
        }

        if (_useMilkdown)
        {
            try { MilkdownEditor.Dispose(); }
            catch { /* Best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    private sealed class NoteLinkCompletionData : ICSharpCode.AvalonEdit.CodeCompletion.ICompletionData
    {
        public NoteLinkCompletionData(string title)
        {
            Text = title;
        }

        public System.Windows.Media.ImageSource? Image => null;
        public string Text { get; }
        public object Content => Text;
        public object Description => Text;
        public double Priority => 0;

        public void Complete(
            ICSharpCode.AvalonEdit.Editing.TextArea textArea,
            ICSharpCode.AvalonEdit.Document.ISegment completionSegment,
            EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, $"{Text}]]");
        }
    }
}
