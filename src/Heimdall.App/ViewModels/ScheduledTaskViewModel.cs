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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.App.Services;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels;

/// <summary>
/// ViewModel managing the scheduled tasks list: CRUD operations and enable/disable toggling.
/// </summary>
public partial class ScheduledTaskListViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;
    private readonly IDialogService _dialogService;
    private readonly LocalizationManager _localizer;

    [ObservableProperty]
    private ObservableCollection<ScheduledTaskItemViewModel> _tasks = [];

    [ObservableProperty]
    private bool _hasNoTasks = true;

    public ScheduledTaskListViewModel(
        ConfigManager configManager,
        IDialogService dialogService,
        LocalizationManager localizer)
    {
        _configManager = configManager;
        _dialogService = dialogService;
        _localizer = localizer;
    }

    /// <summary>
    /// Opens the scheduled task creation dialog and adds the result to the list.
    /// </summary>
    [RelayCommand]
    private async Task AddTaskAsync()
    {
        // Placeholder: will be wired to a ScheduledTaskDialog in a future phase
        await Task.CompletedTask;
    }

    /// <summary>
    /// Opens the scheduled task edit dialog for the selected task.
    /// </summary>
    /// <param name="task">The task item to edit.</param>
    [RelayCommand]
    private async Task EditTaskAsync(ScheduledTaskItemViewModel task)
    {
        ArgumentNullException.ThrowIfNull(task);

        // Placeholder: will be wired to a ScheduledTaskDialog in a future phase
        await Task.CompletedTask;
    }

    /// <summary>
    /// Deletes the selected task after user confirmation.
    /// </summary>
    /// <param name="task">The task item to delete.</param>
    [RelayCommand]
    private async Task DeleteTaskAsync(ScheduledTaskItemViewModel task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var confirmed = await _dialogService.ShowConfirmAsync(
            _localizer["ScheduledTaskDeleteTitle"],
            _localizer.Format("ScheduledTaskDeleteMessage", task.Name),
            "warning");

        if (confirmed)
        {
            Tasks.Remove(task);
            HasNoTasks = Tasks.Count == 0;
        }
    }

    /// <summary>
    /// Toggles the enabled state of a scheduled task.
    /// </summary>
    /// <param name="task">The task item to toggle.</param>
    [RelayCommand]
    private void ToggleTask(ScheduledTaskItemViewModel task)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.Enabled = !task.Enabled;
    }

    /// <summary>
    /// Loads scheduled tasks from the configuration.
    /// </summary>
    /// <param name="tasks">The task items to display.</param>
    public void LoadTasks(IEnumerable<ScheduledTaskItemViewModel> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        Tasks = new ObservableCollection<ScheduledTaskItemViewModel>(tasks);
        HasNoTasks = Tasks.Count == 0;
    }
}

/// <summary>
/// ViewModel for a single scheduled task item displayed in the task list.
/// </summary>
public partial class ScheduledTaskItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _serverId = "";

    [ObservableProperty]
    private string _serverName = "";

    /// <summary>
    /// Scheduled time in HH:mm format.
    /// </summary>
    [ObservableProperty]
    private string _scheduledTime = "";

    [ObservableProperty]
    private string _recurrence = "";

    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>
    /// Human-readable next trigger time (e.g., "Tomorrow at 09:00").
    /// </summary>
    [ObservableProperty]
    private string _nextTrigger = "";
}
