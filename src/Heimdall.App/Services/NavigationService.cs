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

namespace Heimdall.App.Services;

/// <summary>
/// Simple navigation service for switching between top-level views
/// (Servers, Tunnels, ScheduledTasks, Settings).
/// </summary>
public class NavigationService
{
    private string _currentView = "Servers";

    /// <summary>
    /// Raised when a navigation to a different view is requested.
    /// The parameter is the target view name.
    /// </summary>
    public event Action<string>? NavigationRequested;

    /// <summary>
    /// Gets the name of the currently active view.
    /// </summary>
    public string CurrentView => _currentView;

    /// <summary>
    /// Navigates to the specified view by name.
    /// Raises <see cref="NavigationRequested"/> if the view changes.
    /// </summary>
    /// <param name="viewName">
    /// Target view name (e.g., "Servers", "Tunnels", "ScheduledTasks", "Settings").
    /// </param>
    public void NavigateTo(string viewName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);

        if (string.Equals(_currentView, viewName, StringComparison.Ordinal))
        {
            return;
        }

        _currentView = viewName;
        NavigationRequested?.Invoke(viewName);
    }
}
