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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Onboarding;

/// <summary>
/// View-model for the first-launch onboarding overlay. Owns the 3-step
/// state machine, resolves all localized labels, and persists completion
/// to <see cref="AppSettings.OnboardingCompleted"/>.
/// </summary>
/// <remarks>
/// <para>
/// The view-model is intentionally pure: it does not know about the host
/// window's tab system or sidebar. Instead it raises
/// <see cref="StepCompleted"/> with the index of the step that was just
/// finished so the host view can perform whatever navigation it owns
/// (switching tabs, toggling sidebars, etc.). A single <see cref="Completed"/>
/// event is raised once persistence succeeds.
/// </para>
/// <para>
/// Tab navigation will be factored behind a service in a later refactor
/// phase; do not pull it into this class.
/// </para>
/// </remarks>
public sealed partial class OnboardingFlowViewModel : ObservableObject
{
    /// <summary>Total number of steps in the onboarding flow.</summary>
    public const int StepCount = 3;

    private readonly LocalizationManager _localizer;
    private readonly ConfigManager _configManager;
    private AppSettings? _settings;

    /// <summary>
    /// Creates a new onboarding view-model. Call <see cref="Attach"/> with
    /// the live <see cref="AppSettings"/> instance before invoking
    /// <see cref="Start"/>.
    /// </summary>
    public OnboardingFlowViewModel(LocalizationManager localizer, ConfigManager configManager)
    {
        _localizer = localizer;
        _configManager = configManager;
    }

    /// <summary>Zero-based index of the currently displayed step.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepIndicatorStates))]
    private int _currentStep;

    /// <summary>Localized title of the current step.</summary>
    [ObservableProperty]
    private string _titleText = string.Empty;

    /// <summary>Localized description shown beneath the title.</summary>
    [ObservableProperty]
    private string _subtitleText = string.Empty;

    /// <summary>Localized label of the Skip button.</summary>
    [ObservableProperty]
    private string _skipLabel = string.Empty;

    /// <summary>Localized label of the Next/Get-Started button.</summary>
    [ObservableProperty]
    private string _nextLabel = string.Empty;

    /// <summary>Whether the overlay should currently be displayed.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>
    /// Step indicator states bound by the view's dot ItemsControl. The
    /// active step is <c>true</c>, all other entries are <c>false</c>.
    /// Recomputed automatically whenever <see cref="CurrentStep"/> changes.
    /// </summary>
    public IReadOnlyList<bool> StepIndicatorStates
    {
        get
        {
            var dots = new bool[StepCount];
            if (CurrentStep >= 0 && CurrentStep < StepCount)
            {
                dots[CurrentStep] = true;
            }

            return dots;
        }
    }

    /// <summary>
    /// Raised after a step is completed via Next, with the zero-based index
    /// of the step that was just finished. The view uses this to perform
    /// any navigation associated with the completed step.
    /// </summary>
    public event EventHandler<int>? StepCompleted;

    /// <summary>
    /// Raised once after the flow finishes (Next on the final step, Skip,
    /// or Escape) and persistence has succeeded.
    /// </summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Binds the view-model to the live application settings so completion
    /// can be persisted on the same in-memory instance the rest of the app
    /// observes.
    /// </summary>
    public void Attach(AppSettings? settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Resets the flow to step 0, refreshes localized labels, and shows the
    /// overlay. Call this once after attaching the live settings instance.
    /// </summary>
    public void Start()
    {
        CurrentStep = 0;
        IsVisible = true;
        RefreshLabels();
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        var completedStep = CurrentStep;
        StepCompleted?.Invoke(this, completedStep);

        if (completedStep < StepCount - 1)
        {
            CurrentStep = completedStep + 1;
        }
        else
        {
            await CompleteAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task SkipAsync() => CompleteAsync();

    [RelayCommand]
    private Task EscapeAsync() => CompleteAsync();

    private async Task CompleteAsync()
    {
        IsVisible = false;
        if (_settings is null)
        {
            return;
        }

        _settings.OnboardingCompleted = true;
        await _configManager.MergeSettingAsync(s => s.OnboardingCompleted = true).ConfigureAwait(true);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCurrentStepChanged(int value)
    {
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        switch (CurrentStep)
        {
            case 0:
                TitleText = _localizer["OnboardingStep1Title"];
                SubtitleText = _localizer["OnboardingStep1Desc"];
                break;
            case 1:
                TitleText = _localizer["OnboardingStep2Title"];
                SubtitleText = _localizer["OnboardingStep2Desc"];
                break;
            case 2:
                TitleText = _localizer["OnboardingStep3Title"];
                SubtitleText = _localizer["OnboardingStep3Desc"];
                break;
        }

        SkipLabel = _localizer["OnboardingBtnSkip"];
        NextLabel = CurrentStep < StepCount - 1
            ? _localizer["OnboardingBtnNext"]
            : _localizer["OnboardingBtnGetStarted"];
    }
}
