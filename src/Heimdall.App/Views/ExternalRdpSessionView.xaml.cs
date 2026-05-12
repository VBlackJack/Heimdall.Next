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
using Heimdall.App.Services;

namespace Heimdall.App.Views;

/// <summary>
/// Lightweight status surface for an externally launched mstsc.exe process.
/// </summary>
public partial class ExternalRdpSessionView : UserControl
{
    private readonly ExternalRdpSessionModel _session;

    public ExternalRdpSessionView(ExternalRdpSessionModel session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        InitializeComponent();

        DisplayNameTextBlock.Text = _session.DisplayName;
        EndpointTextBlock.Text = _session.Endpoint;
        ProcessTextBlock.Text = _session.ProcessId > 0
            ? _session.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "-";
        ApplyStatus();

        _session.StatusChanged += OnSessionStatusChanged;
        Unloaded += OnUnloaded;
    }

    private void OnSessionStatusChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyStatus();
        }
        else
        {
            Dispatcher.BeginInvoke(ApplyStatus);
        }
    }

    private void ApplyStatus()
    {
        StatusTextBlock.Text = _session.Status;
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _session.StatusChanged -= OnSessionStatusChanged;
        Unloaded -= OnUnloaded;
    }
}
