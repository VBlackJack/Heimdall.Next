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
using Heimdall.Core.Configuration;
using Heimdall.Core.SessionDiagnostics;

namespace Heimdall.App.Tests;

public sealed class SessionTabViewModelTests
{
    [Fact]
    public void HasFailureDetails_IsFalse_WhenFailureDetailsIsNull()
    {
        var vm = new SessionTabViewModel();

        Assert.Null(vm.FailureDetails);
        Assert.False(vm.HasFailureDetails);
    }

    [Fact]
    public void SettingFailureDetails_RaisesPropertyChanged_AndSetsHasFailureDetails()
    {
        var vm = new SessionTabViewModel();
        List<string> changes = [];
        vm.PropertyChanged += (_, args) => RecordChange(args, changes);

        vm.FailureDetails = new SessionDiagnostic(
            SessionFailureStage.SshAuth,
            "ErrorSshAuthRejected",
            7,
            "Access denied");

        Assert.True(vm.HasFailureDetails);
        Assert.Contains(nameof(SessionTabViewModel.FailureDetails), changes);
        Assert.Contains(nameof(SessionTabViewModel.HasFailureDetails), changes);
    }

    [Fact]
    public void ClearingFailureDetails_ResetsHasFailureDetails()
    {
        var vm = new SessionTabViewModel
        {
            FailureDetails = new SessionDiagnostic(
                SessionFailureStage.SshGateway,
                "ErrorConnectionFailed",
                null,
                "Tunnel failed")
        };

        List<string> changes = [];
        vm.PropertyChanged += (_, args) => RecordChange(args, changes);

        vm.FailureDetails = null;

        Assert.Null(vm.FailureDetails);
        Assert.False(vm.HasFailureDetails);
        Assert.Contains(nameof(SessionTabViewModel.FailureDetails), changes);
        Assert.Contains(nameof(SessionTabViewModel.HasFailureDetails), changes);
    }

    [Fact]
    public void MarkAsAdHoc_SetsFlagAndSnapshot()
    {
        var vm = new SessionTabViewModel();
        var dto = new ServerProfileDto
        {
            Id = "adhoc-rdp-10.0.0.5",
            DisplayName = "RDP to 10.0.0.5",
            RemoteServer = "10.0.0.5",
            ConnectionType = "RDP"
        };

        Assert.False(vm.IsAdHoc);
        Assert.Null(vm.AdHocProfileSnapshot);
        Assert.Throws<ArgumentNullException>(() => vm.MarkAsAdHoc(null!));

        vm.MarkAsAdHoc(dto);

        Assert.True(vm.IsAdHoc);
        Assert.Same(dto, vm.AdHocProfileSnapshot);
    }

    private static void RecordChange(PropertyChangedEventArgs args, ICollection<string> changes)
    {
        if (!string.IsNullOrWhiteSpace(args.PropertyName))
        {
            changes.Add(args.PropertyName);
        }
    }
}
