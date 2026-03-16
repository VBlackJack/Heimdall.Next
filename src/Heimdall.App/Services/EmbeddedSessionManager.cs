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
using System.Windows.Controls;
using System.Windows.Media;
using Heimdall.App.ViewModels;
using Heimdall.App.Views;
using Heimdall.Core.Configuration;

namespace Heimdall.App.Services;

/// <summary>
/// Creates visual hosts for connection sessions so the shell can render
/// embedded protocol surfaces without teaching the ViewModel layer about WPF.
/// </summary>
public sealed class EmbeddedSessionManager
{
    public object CreateHostControl(
        SessionTabViewModel sessionTab,
        string displayName,
        string connectionType,
        object session)
    {
        ArgumentNullException.ThrowIfNull(sessionTab);
        ArgumentNullException.ThrowIfNull(session);

        if (string.Equals(connectionType, "RDP", StringComparison.OrdinalIgnoreCase) &&
            session is RdpServerDto server)
        {
            var view = new EmbeddedRdpView();
            view.InitializeSession(server, sessionTab);
            return view;
        }

        if (session is UIElement element)
        {
            return element;
        }

        return new DisposablePlaceholderView(displayName, connectionType, session);
    }

    private static Brush GetBrush(string resourceKey, Brush fallback)
    {
        return Application.Current.TryFindResource(resourceKey) as Brush ?? fallback;
    }

    private sealed class DisposablePlaceholderView : Border, IDisposable
    {
        private readonly IDisposable? _session;
        private bool _disposed;

        public DisposablePlaceholderView(string displayName, string connectionType, object session)
        {
            _session = session as IDisposable;

            Background = GetBrush("BackgroundBrush", Brushes.Transparent);
            Child = BuildContent(displayName, connectionType);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _session?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by the session engine.
            }
        }

        private static FrameworkElement BuildContent(string displayName, string connectionType)
        {
            var message = string.Equals(connectionType, "SFTP", StringComparison.OrdinalIgnoreCase)
                ? "The SFTP session is connected, but the embedded browser view is not wired yet."
                : string.Format(
                    "The {0} session is connected, but no embedded view is available yet.",
                    connectionType);

            var outer = new Border
            {
                Margin = new Thickness(24),
                Padding = new Thickness(24),
                CornerRadius = new CornerRadius(16),
                Background = GetBrush("CardBrush", Brushes.Black),
                BorderBrush = GetBrush("BorderBrush", Brushes.DimGray),
                BorderThickness = new Thickness(1),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            var stack = new StackPanel
            {
                MaxWidth = 460
            };

            stack.Children.Add(new TextBlock
            {
                Text = displayName,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("TextPrimaryBrush", Brushes.White)
            });

            stack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = GetBrush("TextSecondaryBrush", Brushes.Gainsboro)
            });

            outer.Child = stack;
            return outer;
        }
    }
}
