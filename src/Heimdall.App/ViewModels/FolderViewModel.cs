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

    [ObservableProperty]
    private ObservableCollection<FolderViewModel> _subFolders = [];

    [ObservableProperty]
    private ObservableCollection<ServerItemViewModel> _servers = [];

    /// <summary>
    /// Combined collection for the TreeView: sub-folders first, then servers.
    /// WPF resolves the correct DataTemplate by type.
    /// </summary>
    public IList Children
    {
        get
        {
            var list = new ArrayList();
            foreach (var f in SubFolders) list.Add(f);
            foreach (var s in Servers) list.Add(s);
            return list;
        }
    }

    /// <summary>Total server count (direct + recursive).</summary>
    public int ServerCount =>
        Servers.Count + SubFolders.Sum(f => f.ServerCount);
}
