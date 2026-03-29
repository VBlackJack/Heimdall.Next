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

using System.Windows;
using WpfControl = System.Windows.Controls.Control;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Small controller that keeps async tool shell states consistent:
/// loading, inline error, empty state, results visibility, and busy button state.
/// </summary>
internal sealed class ToolAsyncStateController
{
    private readonly Action<bool>? _setBusy;
    private readonly WpfProgressBar? _loadingBar;
    private readonly WpfTextBlock? _errorText;
    private readonly UIElement? _emptyState;
    private readonly UIElement? _results;
    private readonly WpfTextBlock? _statusText;
    private readonly WpfControl[] _controlsToDisable;

    public ToolAsyncStateController(
        Action<bool>? setBusy,
        WpfProgressBar? loadingBar,
        WpfTextBlock? errorText,
        UIElement? emptyState,
        UIElement? results,
        WpfTextBlock? statusText,
        params WpfControl[] controlsToDisable)
    {
        _setBusy = setBusy;
        _loadingBar = loadingBar;
        _errorText = errorText;
        _emptyState = emptyState;
        _results = results;
        _statusText = statusText;
        _controlsToDisable = controlsToDisable ?? [];
    }

    public void Reset(bool showEmptyState = true)
    {
        if (_errorText is not null)
        {
            _errorText.Text = string.Empty;
            _errorText.Visibility = Visibility.Collapsed;
        }

        if (_loadingBar is not null)
        {
            _loadingBar.Visibility = Visibility.Collapsed;
        }

        if (_results is not null)
        {
            _results.Visibility = Visibility.Collapsed;
        }

        if (_emptyState is not null)
        {
            _emptyState.Visibility = showEmptyState ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_statusText is not null)
        {
            _statusText.Text = string.Empty;
        }
    }

    public void Begin(string? status = null)
    {
        _setBusy?.Invoke(true);
        SetControlsEnabled(false);

        if (_errorText is not null)
        {
            _errorText.Text = string.Empty;
            _errorText.Visibility = Visibility.Collapsed;
        }

        if (_loadingBar is not null)
        {
            _loadingBar.Visibility = Visibility.Visible;
        }

        if (_statusText is not null && status is not null)
        {
            _statusText.Text = status;
        }
    }

    public void ShowError(
        string message,
        string? status = null,
        bool showEmptyState = true,
        bool keepResultsVisible = false)
    {
        if (_results is not null)
        {
            _results.Visibility = keepResultsVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_emptyState is not null)
        {
            _emptyState.Visibility = keepResultsVisible
                ? Visibility.Collapsed
                : (showEmptyState ? Visibility.Visible : Visibility.Collapsed);
        }

        if (_errorText is not null)
        {
            _errorText.Text = message;
            _errorText.Visibility = Visibility.Visible;
        }

        if (_statusText is not null && status is not null)
        {
            _statusText.Text = status;
        }
    }

    public void ShowResults(string? status = null)
    {
        if (_emptyState is not null)
        {
            _emptyState.Visibility = Visibility.Collapsed;
        }

        if (_results is not null)
        {
            _results.Visibility = Visibility.Visible;
        }

        if (_statusText is not null && status is not null)
        {
            _statusText.Text = status;
        }
    }

    public void End()
    {
        _setBusy?.Invoke(false);
        SetControlsEnabled(true);

        if (_loadingBar is not null)
        {
            _loadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (var control in _controlsToDisable)
        {
            control.IsEnabled = enabled;
        }
    }
}
