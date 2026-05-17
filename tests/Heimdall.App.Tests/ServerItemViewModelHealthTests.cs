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

using System.ComponentModel;
using Heimdall.App.ViewModels;
using Heimdall.Core.SessionHealth;

namespace Heimdall.App.Tests;

/// <summary>
/// Verifies that <see cref="ServerItemViewModel.HealthState"/> assignments raise
/// <see cref="INotifyPropertyChanged"/> for both the backing property and the
/// computed <c>HealthTooltipText</c>, so the sidebar dot's tooltip updates as
/// soon as a new probe verdict arrives.
/// </summary>
public class ServerItemViewModelHealthTests
{
    [Fact]
    public void HealthState_DefaultsToInitial()
    {
        var vm = new ServerItemViewModel { Id = "srv-1" };

        Assert.Equal(HealthState.Initial, vm.HealthState);
    }

    [Fact]
    public void SettingHealthState_RaisesPropertyChangedForBackingFieldAndTooltip()
    {
        var vm = new ServerItemViewModel { Id = "srv-1" };
        var observed = new List<string?>();
        vm.PropertyChanged += (_, e) => observed.Add(e.PropertyName);

        vm.HealthState = new HealthState(HealthStatus.Up, DateTime.UtcNow, 12, null);

        Assert.Contains(nameof(ServerItemViewModel.HealthState), observed);
        Assert.Contains(nameof(ServerItemViewModel.HealthTooltipText), observed);
    }

    [Fact]
    public void HealthTooltipText_AlwaysReturnsNonEmptyString()
    {
        var vm = new ServerItemViewModel { Id = "srv-1" };

        Assert.False(string.IsNullOrEmpty(vm.HealthTooltipText));
    }
}
