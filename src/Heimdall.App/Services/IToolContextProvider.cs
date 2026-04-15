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
using Heimdall.App.ViewModels;

namespace Heimdall.App.Services;

/// <summary>
/// Provides the currently inherited tool target host and localized labels
/// displayed by the Tools tab and sidebar Tools panel.
/// </summary>
public interface IToolContextProvider : INotifyPropertyChanged, IDisposable
{
    /// <summary>Currently selected server host, trimmed, or <c>null</c> when no host is selected.</summary>
    string? TargetHost { get; }

    /// <summary>True when <see cref="TargetHost"/> has a non-empty value.</summary>
    bool HasTarget { get; }

    /// <summary>Localized context label displayed in tool entry points.</summary>
    string ContextLabel { get; }

    /// <summary>Tooltip text mirroring <see cref="ContextLabel"/> for truncated labels.</summary>
    string ContextTooltip { get; }

    /// <summary>Theme brush resource key used by the full Tools tab context label.</summary>
    string ContextBrushKey { get; }

    /// <summary>
    /// Updates the provider from the selected server. Passing <c>null</c>
    /// clears the inherited target host.
    /// </summary>
    void SetSelectedServer(ServerItemViewModel? server);
}
