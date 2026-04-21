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

using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Thin WPF shell for the cron job manager.
/// </summary>
public partial class CronJobManagerView : UserControl, IToolView
{
    private readonly CronJobViewModel _vm;
    private LocalizationManager? _localizer;
    private Action<bool>? _setBusy;
    private bool _disposed;

    public CronJobManagerView()
    {
        _vm = new CronJobViewModel();
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.CopyResultsRequested += OnCopyResultsRequested;
        _vm.CronEntries.CollectionChanged += OnCronEntriesChanged;
        _vm.TaskEntries.CollectionChanged += OnTaskEntriesChanged;
        TxtCrontabInput.PreviewKeyDown += OnCrontabInputPreviewKeyDown;
    }

    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
        }

        _localizer = localizer;
        if (_localizer is not null)
        {
            _localizer.LocaleChanged += OnLocaleChanged;
        }

        _setBusy = context?.SetBusyAction;
        _vm.Initialize(localizer);
        ApplyLocalization();
        UpdateResultsSurface();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            TxtCrontabInput.Focus();
            TxtCrontabInput.SelectAll();
        });
    }

    public bool CanClose() => !_vm.IsLoadingTasks;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _setBusy?.Invoke(false);

        if (_localizer is not null)
        {
            _localizer.LocaleChanged -= OnLocaleChanged;
            _localizer = null;
        }

        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.CopyResultsRequested -= OnCopyResultsRequested;
        _vm.CronEntries.CollectionChanged -= OnCronEntriesChanged;
        _vm.TaskEntries.CollectionChanged -= OnTaskEntriesChanged;
        TxtCrontabInput.PreviewKeyDown -= OnCrontabInputPreviewKeyDown;
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ApplyLocalization()
    {
        TxtEmptyState.Text = L("ToolCronJobEmptyState");
        BtnCopyAll.ToolTip = L("ToolBtnCopyToClipboard");
        BtnHelp.ToolTip = L("ToolHelpTooltip");

        AutomationProperties.SetName(TxtCrontabInput, L("ToolCronJobPasteInstructions"));
        AutomationProperties.SetName(BtnParse, L("ToolCronJobBtnParse"));
        AutomationProperties.SetName(BtnClearPaste, L("ToolCronJobBtnClear"));
        AutomationProperties.SetName(BtnRefreshTasks, L("ToolCronJobBtnRefresh"));
        AutomationProperties.SetName(BtnCopyAll, L("ToolCronJobBtnCopy"));
        AutomationProperties.SetName(BtnHelp, L("ToolHelpTooltip"));
        AutomationProperties.SetName(BtnCloseHelp, L("BtnClose"));
        AutomationProperties.SetName(CronResultsGrid, L("ToolCronJobTabPaste"));
        AutomationProperties.SetName(TasksResultsGrid, L("ToolCronJobTabWindows"));
        AutomationProperties.SetName(LoadingBar, L("ToolCronJobA11yLoading"));
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CronJobViewModel.IsBusy):
                _setBusy?.Invoke(_vm.IsBusy);
                UpdateResultsSurface();
                break;
            case nameof(CronJobViewModel.HasCronError):
            case nameof(CronJobViewModel.HasTasksError):
            case nameof(CronJobViewModel.CrontabInputText):
            case nameof(CronJobViewModel.ModeIndex):
                UpdateResultsSurface();
                break;
        }
    }

    private void OnCronEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateResultsSurface();
    }

    private void OnTaskEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateResultsSurface();
    }

    private void UpdateResultsSurface()
    {
        LoadingBar.Visibility = _vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;

        var hasCronResults = _vm.CronEntries.Count > 0;
        var hasTaskResults = _vm.TaskEntries.Count > 0;
        var hasDraftInput = !string.IsNullOrWhiteSpace(_vm.CrontabInputText);

        CronResultsPanel.Visibility = _vm.Mode == CronJobMode.Paste && hasCronResults
            ? Visibility.Visible
            : Visibility.Collapsed;
        TasksResultsPanel.Visibility = _vm.Mode == CronJobMode.Tasks && hasTaskResults
            ? Visibility.Visible
            : Visibility.Collapsed;
        TxtTasksLoading.Visibility = _vm.IsBusy && _vm.Mode == CronJobMode.Tasks
            ? Visibility.Visible
            : Visibility.Collapsed;

        EmptyStatePanel.Visibility = hasCronResults || hasTaskResults || hasDraftInput || _vm.IsBusy
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnCrontabInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_vm.ParseCommand.CanExecute(null))
            {
                _vm.ParseCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnCopyResultsRequested(object? sender, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            CopyFeedbackHelper.ShowCopyFeedback(BtnCopyAll);
        }
        catch (ExternalException)
        {
            // Clipboard locked by another process.
        }
    }

    private void OnLocaleChanged(string _)
    {
        ApplyLocalization();
    }

    private string L(string key) => _localizer?[key] ?? key;
}
