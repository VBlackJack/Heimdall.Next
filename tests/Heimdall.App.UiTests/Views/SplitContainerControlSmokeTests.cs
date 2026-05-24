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
using System.Windows.Threading;
using Heimdall.App.UiTests.Infrastructure;
using Heimdall.App.Views;
using Heimdall.Core.Models;

namespace Heimdall.App.UiTests.Views;

[Collection(DesktopUiCollection.Name)]
public sealed class SplitContainerControlSmokeTests
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void ReloadAfterUnload_RebindsLifecycleHandlersAndModel()
    {
        WpfTestHost.Invoke(() =>
        {
            var model = new SplitContainerModel
            {
                First = new SessionPaneModel { PaneId = "left" },
                Second = new SessionPaneModel { PaneId = "right" },
                Orientation = SplitOrientation.Vertical
            };
            var control = new SplitContainerControl
            {
                DataContext = model,
                Width = 400,
                Height = 240
            };
            var host = new Grid();
            var window = new Window
            {
                Content = host,
                Width = 480,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 120,
                Top = 120,
                ShowInTaskbar = false
            };

            try
            {
                host.Children.Add(control);
                window.Show();
                window.UpdateLayout();

                var rootGrid = Assert.IsType<Grid>(control.FindName("RootGrid"));
                AssertVerticalLayout(rootGrid);

                host.Children.Remove(control);
                window.UpdateLayout();
                FlushDispatcher();
                Assert.False(control.IsLoaded);

                model.Orientation = SplitOrientation.Horizontal;

                host.Children.Add(control);
                window.UpdateLayout();
                FlushDispatcher();

                Assert.True(control.IsLoaded);
                AssertHorizontalLayout(rootGrid);

                model.Orientation = SplitOrientation.Vertical;
                FlushDispatcher();

                AssertVerticalLayout(rootGrid);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void AssertVerticalLayout(Grid rootGrid)
    {
        Assert.Single(rootGrid.RowDefinitions);
        Assert.Equal(3, rootGrid.ColumnDefinitions.Count);
    }

    private static void AssertHorizontalLayout(Grid rootGrid)
    {
        Assert.Equal(3, rootGrid.RowDefinitions.Count);
        Assert.Single(rootGrid.ColumnDefinitions);
    }

    private static void FlushDispatcher()
        => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
