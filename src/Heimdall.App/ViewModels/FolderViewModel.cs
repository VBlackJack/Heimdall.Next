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

using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Heimdall.App.ViewModels;

/// <summary>
/// Represents a folder node in the TreeView. Folders can contain sub-folders
/// and servers with unlimited nesting depth. Replaces the old Project + Group model.
/// </summary>
public partial class FolderViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    /// <summary>Full path from root (e.g., "ADSEC/Gateway/Linux").</summary>
    [ObservableProperty]
    private string _fullPath = "";

    [ObservableProperty]
    private string _color = "";

    /// <summary>
    /// Whether this folder is expanded in the TreeView.
    /// Bound TwoWay to TreeViewItem.IsExpanded for state persistence.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Stable key for expand/collapse state persistence.
    /// Uses FullPath for named folders, sentinel for the root "no group" folder.
    /// </summary>
    public string ExpansionKey =>
        string.IsNullOrEmpty(FullPath) ? "::nogroup" : FullPath;

    [ObservableProperty]
    private ObservableCollection<FolderViewModel> _subFolders = [];

    [ObservableProperty]
    private ObservableCollection<ServerItemViewModel> _servers = [];

    private ArrayList? _childrenCache;

    /// <summary>
    /// Combined collection for the TreeView: sub-folders first, then servers.
    /// WPF resolves the correct DataTemplate by type.
    /// Cached to avoid re-allocation on each access (perf with 768+ items).
    /// </summary>
    public IList Children
    {
        get
        {
            if (_childrenCache is null || _childrenCache.Count != SubFolders.Count + Servers.Count)
            {
                _childrenCache = new ArrayList(SubFolders.Count + Servers.Count);
                foreach (var f in SubFolders) _childrenCache.Add(f);
                foreach (var s in Servers) _childrenCache.Add(s);
            }

            return _childrenCache;
        }
    }

    /// <summary>Invalidate the Children cache when sub-collections change.</summary>
    public void InvalidateChildren() => _childrenCache = null;

    /// <summary>Total server count (direct + recursive).</summary>
    public int ServerCount =>
        Servers.Count + SubFolders.Sum(f => f.ServerCount);
}
