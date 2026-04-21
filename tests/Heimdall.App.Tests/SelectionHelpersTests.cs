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
using Heimdall.App.ViewModels;

namespace Heimdall.App.Tests;

public sealed class SelectionHelpersTests
{
    [Fact]
    public void EnumerateVisibleLeaves_ReturnsDepthFirstVisibleLeafOrder()
    {
        var leafA = new ServerItemViewModel { Id = "a", DisplayName = "A" };
        var leafB = new ServerItemViewModel { Id = "b", DisplayName = "B" };
        var leafC = new ServerItemViewModel { Id = "c", DisplayName = "C" };
        var child = new FolderViewModel
        {
            Name = "Child",
            IsExpanded = true,
            Servers = new ObservableCollection<ServerItemViewModel> { leafB }
        };
        var root = new FolderViewModel
        {
            Name = "Root",
            IsExpanded = true,
            SubFolders = new ObservableCollection<FolderViewModel> { child },
            Servers = new ObservableCollection<ServerItemViewModel> { leafA }
        };
        var secondRoot = new FolderViewModel
        {
            Name = "Second",
            IsExpanded = true,
            Servers = new ObservableCollection<ServerItemViewModel> { leafC }
        };

        var result = SelectionHelpers.EnumerateVisibleLeaves([root, secondRoot])
            .Select(server => server.Id)
            .ToArray();

        Assert.Equal(["b", "a", "c"], result);
    }

    [Fact]
    public void EnumerateVisibleLeaves_SkipsCollapsedFolders()
    {
        var leaf = new ServerItemViewModel { Id = "hidden", DisplayName = "Hidden" };
        var root = new FolderViewModel
        {
            Name = "Root",
            IsExpanded = false,
            Servers = new ObservableCollection<ServerItemViewModel> { leaf }
        };

        var result = SelectionHelpers.EnumerateVisibleLeaves([root]).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void EnumerateVisibleLeaves_SkipsLeavesInCollapsedDescendants()
    {
        var leaf = new ServerItemViewModel { Id = "hidden", DisplayName = "Hidden" };
        var collapsedChild = new FolderViewModel
        {
            Name = "Child",
            IsExpanded = false,
            Servers = new ObservableCollection<ServerItemViewModel> { leaf }
        };
        var root = new FolderViewModel
        {
            Name = "Root",
            IsExpanded = true,
            SubFolders = new ObservableCollection<FolderViewModel> { collapsedChild }
        };

        var result = SelectionHelpers.EnumerateVisibleLeaves([root]).ToList();

        Assert.Empty(result);
    }
}
