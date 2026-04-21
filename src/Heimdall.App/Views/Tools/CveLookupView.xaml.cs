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

using System.Windows.Controls;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;
using Heimdall.Core.Models;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Thin view shell for the offline CVE lookup tool.
/// </summary>
public partial class CveLookupView : UserControl, IToolView
{
    private readonly CveLookupViewModel _viewModel = new();
    private bool _disposed;

    public CveLookupView()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    /// <inheritdoc />
    public void Initialize(ToolContext? context, LocalizationManager? localizer)
    {
        _viewModel.Initialize(localizer);

        if (!string.IsNullOrWhiteSpace(context?.Argument))
        {
            _viewModel.SearchWith(context.Argument);
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            TxtInput.Focus();
        });
    }

    public bool CanClose() => true;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}
