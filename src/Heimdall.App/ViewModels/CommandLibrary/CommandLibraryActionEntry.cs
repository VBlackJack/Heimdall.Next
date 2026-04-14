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

using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TwinShell.Core.Enums;
using ActionModel = TwinShell.Core.Models.Action;

namespace Heimdall.App.ViewModels.CommandLibrary;

/// <summary>
/// Display-side wrapper around a TwinShell <see cref="ActionModel"/>.
/// Holds a back-reference to the owning <see cref="CommandLibraryViewModel"/>
/// so that derived display properties (favorite icon, search rank, localized
/// risk/platform labels) can be computed from current VM state at bind time.
/// </summary>
/// <remarks>
/// Phase A keeps the imperative <c>ICollectionView.Refresh()</c> trigger that
/// re-evaluates getters; the type only inherits <see cref="ObservableObject"/>
/// to make Phase B's binding migration trivial.
/// </remarks>
public sealed partial class CommandLibraryActionEntry : ObservableObject
{
    private readonly CommandLibraryViewModel _viewModel;

    /// <summary>
    /// Creates a new display entry bound to the given action and owning VM.
    /// </summary>
    /// <param name="source">Underlying TwinShell action model.</param>
    /// <param name="viewModel">VM providing favorite/search/locale state.</param>
    public CommandLibraryActionEntry(ActionModel source, CommandLibraryViewModel viewModel)
    {
        Source = source;
        _viewModel = viewModel;
    }

    /// <summary>The underlying TwinShell action model.</summary>
    public ActionModel Source { get; }

    /// <summary>Action title (passthrough).</summary>
    public string Title => Source.Title;

    /// <summary>Action description; empty string if none.</summary>
    public string Description => Source.Description ?? string.Empty;

    /// <summary>Action category, used for grouping and filtering.</summary>
    public string Category => Source.Category;

    /// <summary>Filled or hollow star glyph reflecting the current favorite state.</summary>
    public string FavoriteIcon => _viewModel.IsFavorite(Source.Id) ? "\u2605" : "\u2606";

    /// <summary>Localized tooltip describing the favorite-toggle action.</summary>
    public string FavoriteTooltip => _viewModel.IsFavorite(Source.Id)
        ? _viewModel.LocalizeKey("ToolCmdLibFavoriteRemove")
        : _viewModel.LocalizeKey("ToolCmdLibFavoriteAdd");

    /// <summary>
    /// Zero-based search rank, or <see cref="int.MaxValue"/> when no search is
    /// active so the default category sort wins.
    /// </summary>
    public int SearchRank => _viewModel.GetSearchRank(Source.Id);

    /// <summary>Localized platform label (Windows / Linux / Both).</summary>
    public string PlatformLabel => Source.Platform switch
    {
        Platform.Windows => _viewModel.LocalizeKey("ToolCmdLibPlatformLabelWin"),
        Platform.Linux => _viewModel.LocalizeKey("ToolCmdLibPlatformLabelLin"),
        _ => _viewModel.LocalizeKey("ToolCmdLibPlatformLabelBoth")
    };

    /// <summary>Brush representing the action's risk level.</summary>
    public Brush RiskBrush => Source.Level switch
    {
        CriticalityLevel.Info => ResolveBrush("TextSecondaryBrush", Brushes.Gray),
        CriticalityLevel.Run => ResolveBrush("WarningBrush", Brushes.Orange),
        CriticalityLevel.Dangerous => ResolveBrush("ErrorBrush", Brushes.Red),
        _ => ResolveBrush("TextSecondaryBrush", Brushes.Gray)
    };

    /// <summary>Localized long-form risk label, used for tooltips.</summary>
    public string RiskLabel => Source.Level switch
    {
        CriticalityLevel.Info => _viewModel.LocalizeKey("ToolCmdLibRiskInfo"),
        CriticalityLevel.Run => _viewModel.LocalizeKey("ToolCmdLibRiskRun"),
        CriticalityLevel.Dangerous => _viewModel.LocalizeKey("ToolCmdLibRiskDangerous"),
        _ => string.Empty
    };

    /// <summary>Short risk badge text shown in the action list.</summary>
    public string RiskBadge => Source.Level switch
    {
        CriticalityLevel.Info => _viewModel.LocalizeKey("ToolCmdLibRiskBadgeInfo"),
        CriticalityLevel.Run => _viewModel.LocalizeKey("ToolCmdLibRiskBadgeRun"),
        CriticalityLevel.Dangerous => _viewModel.LocalizeKey("ToolCmdLibRiskBadgeDanger"),
        _ => string.Empty
    };

    private static Brush ResolveBrush(string resourceKey, Brush fallback)
        => System.Windows.Application.Current?.TryFindResource(resourceKey) as Brush ?? fallback;
}
