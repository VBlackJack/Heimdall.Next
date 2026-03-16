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
using CommunityToolkit.Mvvm.ComponentModel;

namespace Heimdall.App.ViewModels;

/// <summary>
/// Represents a project node in the TreeView hierarchy.
/// Project → Group → Server.
/// </summary>
public partial class ServerProjectViewModel : ObservableObject
{
    [ObservableProperty]
    private string _projectId = "";

    [ObservableProperty]
    private string _projectName = "";

    [ObservableProperty]
    private string _projectColor = "";

    [ObservableProperty]
    private bool _isVirtualProject;

    [ObservableProperty]
    private ObservableCollection<ServerGroupViewModel> _groups = [];

    /// <summary>
    /// Total number of servers across all groups in this project.
    /// </summary>
    public int ServerCount => Groups.Sum(g => g.ServerCount);
}
