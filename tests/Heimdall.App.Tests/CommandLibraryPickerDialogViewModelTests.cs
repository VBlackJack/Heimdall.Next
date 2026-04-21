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

using Heimdall.App.ViewModels.Dialogs;
using Heimdall.App.Services.PostConnect;
using Microsoft.Extensions.DependencyInjection;
using TwinShell.Core.Enums;
using TwinShell.Core.Models;
using ActionModel = TwinShell.Core.Models.Action;

namespace Heimdall.App.Tests;

public sealed class CommandLibraryPickerDialogViewModelTests
{
    [Fact]
    public async Task InitializeAsync_LoadsAndSortsActions()
    {
        var vm = await CreateViewModelAsync(
            CommandLibraryTestHelpers.CreateLinuxAction("b", "Zulu", "echo {value}", CommandLibraryTestHelpers.RequiredParameter("value", "Value")),
            CommandLibraryTestHelpers.CreateLinuxAction("a", "Alpha", "echo {value}", CommandLibraryTestHelpers.RequiredParameter("value", "Value")));

        Assert.Equal(2, vm.Actions.Count);
        Assert.Equal("Alpha", vm.Actions[0].Title);
        Assert.Equal("Zulu", vm.Actions[1].Title);
        Assert.Equal(2, vm.ActionsView.Cast<object>().Count());
    }

    [Fact]
    public async Task PlatformFilter_HidesNonMatchingEntries()
    {
        var windowsOnly = new ActionModel
        {
            Id = "win",
            Title = "Win action",
            Category = "Ops",
            Platform = Platform.Windows,
            WindowsCommandTemplate = new CommandTemplate
            {
                Id = "win-template",
                Name = "Win",
                Platform = Platform.Windows,
                CommandPattern = "Write-Host ok"
            }
        };
        var vm = await CreateViewModelAsync(
            windowsOnly,
            CommandLibraryTestHelpers.CreateLinuxAction("lin", "Linux action", "echo ok"));

        vm.PlatformFilter = "Linux";

        var filtered = vm.ActionsView.Cast<CommandLibraryPickerItem>().ToList();
        Assert.Single(filtered);
        Assert.Equal("lin", filtered[0].ActionId);
    }

    [Fact]
    public async Task SearchText_FiltersByTitleAndCategory()
    {
        var categoryMatch = CommandLibraryTestHelpers.CreateLinuxAction("svc", "Restart", "systemctl restart nginx");
        categoryMatch.Category = "Services";
        var vm = await CreateViewModelAsync(
            CommandLibraryTestHelpers.CreateLinuxAction("tail", "Tail logs", "tail -f /tmp/app.log"),
            categoryMatch);

        vm.SearchText = "serv";

        var filtered = vm.ActionsView.Cast<CommandLibraryPickerItem>().ToList();
        Assert.Single(filtered);
        Assert.Equal("svc", filtered[0].ActionId);
    }

    [Fact]
    public async Task SelectingWindowsOnlyAction_ShowsLinuxTemplateError()
    {
        var windowsOnly = new ActionModel
        {
            Id = "win",
            Title = "Windows only",
            Category = "Ops",
            Platform = Platform.Windows,
            WindowsCommandTemplate = new CommandTemplate
            {
                Id = "win-template",
                Name = "Win",
                Platform = Platform.Windows,
                CommandPattern = "Write-Host ok"
            }
        };
        var vm = await CreateViewModelAsync(windowsOnly);
        vm.SelectedAction = vm.Actions.Single();

        Assert.False(vm.CanConfirm);
        Assert.Equal("This action does not provide a Linux command template.", vm.ErrorMessage);
        Assert.Empty(vm.Parameters);
    }

    [Fact]
    public async Task Confirm_RequiresAllRequiredParameters()
    {
        var vm = await CreateViewModelAsync(
            CommandLibraryTestHelpers.CreateLinuxAction(
                "action-1",
                "Tail log",
                "tail -f {path}",
                CommandLibraryTestHelpers.RequiredParameter("path", "Path")));
        vm.SelectedAction = vm.Actions.Single();

        Assert.False(vm.CanConfirm);

        vm.ConfirmCommand.Execute(null);

        Assert.Equal("Parameter 'Path' is required.", vm.ErrorMessage);
        Assert.Null(vm.ResultActionId);
    }

    [Fact]
    public async Task Confirm_ReturnsTechnicalParameterNames()
    {
        var vm = await CreateViewModelAsync(
            CommandLibraryTestHelpers.CreateLinuxAction(
                "action-1",
                "Tail log",
                "tail -f {path} --lines {lines}",
                CommandLibraryTestHelpers.RequiredParameter("path", "Path"),
                CommandLibraryTestHelpers.OptionalParameter("lines", "Lines", "50", type: "int")));
        vm.SelectedAction = vm.Actions.Single();
        vm.Parameters[0].Value = "/var/log/auth.log";

        vm.ConfirmCommand.Execute(null);

        Assert.Equal("action-1", vm.ResultActionId);
        Assert.Equal("Tail log", vm.ResultActionTitle);
        Assert.NotNull(vm.ResultParams);
        Assert.Equal("/var/log/auth.log", vm.ResultParams!["path"]);
        Assert.Equal("50", vm.ResultParams["lines"]);
        Assert.DoesNotContain("Path", vm.ResultParams.Keys);
    }

    [Fact]
    public async Task Cancel_ClearsResult()
    {
        var vm = await CreateViewModelAsync(CommandLibraryTestHelpers.CreateLinuxAction("action-1", "Tail log", "tail -f /tmp/app.log"));
        vm.SelectedAction = vm.Actions.Single();
        vm.CancelCommand.Execute(null);

        Assert.Null(vm.ResultActionId);
        Assert.Null(vm.ResultActionTitle);
        Assert.Null(vm.ResultParams);
    }

    [Fact]
    public async Task SelectingLinuxAction_CreatesParameterViewModels()
    {
        var vm = await CreateViewModelAsync(
            CommandLibraryTestHelpers.CreateLinuxAction(
                "action-1",
                "Tail log",
                "tail -f {path}",
                CommandLibraryTestHelpers.RequiredParameter("path", "Path"),
                CommandLibraryTestHelpers.OptionalParameter("lines", "Lines", "25", type: "int")));

        vm.SelectedAction = vm.Actions.Single();

        Assert.Equal(2, vm.Parameters.Count);
        Assert.Equal("path", vm.Parameters[0].Name);
        Assert.Equal(string.Empty, vm.Parameters[0].Value);
        Assert.Equal("25", vm.Parameters[1].Value);
    }

    [Fact]
    public async Task SelectedAction_WithContext_AppliesPrefillAndMarksAsAutoPrefilled()
    {
        var vm = await CreateViewModelAsync(
            new AutoPrefillContext("server.example.com", 22, "alice", "SSH"),
            CommandLibraryTestHelpers.CreateLinuxAction(
                "action-1",
                "Tail log",
                "ssh {user}@{host} -p {port}",
                CommandLibraryTestHelpers.RequiredParameter("host", "Host"),
                CommandLibraryTestHelpers.OptionalParameter("user", "User"),
                CommandLibraryTestHelpers.OptionalParameter("port", "Port")));

        vm.SelectedAction = vm.Actions.Single();

        Assert.Equal("server.example.com", vm.Parameters[0].Value);
        Assert.True(vm.Parameters[0].IsAutoPrefilled);
        Assert.Equal("alice", vm.Parameters[1].Value);
        Assert.True(vm.Parameters[1].IsAutoPrefilled);
        Assert.Equal("22", vm.Parameters[2].Value);
        Assert.True(vm.Parameters[2].IsAutoPrefilled);
    }

    [Fact]
    public async Task EditingPrefilledValue_ClearsIsAutoPrefilled()
    {
        var vm = await CreateViewModelAsync(
            new AutoPrefillContext("server.example.com", 22, "alice", "SSH"),
            CommandLibraryTestHelpers.CreateLinuxAction(
                "action-1",
                "Tail log",
                "ssh {host}",
                CommandLibraryTestHelpers.RequiredParameter("host", "Host")));

        vm.SelectedAction = vm.Actions.Single();
        vm.Parameters[0].Value = "other.example.com";

        Assert.False(vm.Parameters[0].IsAutoPrefilled);
    }

    [Fact]
    public async Task InitializeForChangeAsync_PreselectsActionAndPreservesExistingValues()
    {
        var localizer = await CommandLibraryTestHelpers.CreateAppLocalizerAsync();
        var provider = CommandLibraryTestHelpers.CreateResolverServiceProvider(
            CommandLibraryTestHelpers.CreateLinuxAction(
                "action-1",
                "Tail log",
                "ssh {host} -p {port}",
                CommandLibraryTestHelpers.RequiredParameter("host", "Host"),
                CommandLibraryTestHelpers.OptionalParameter("port", "Port", "22")));
        var vm = new CommandLibraryPickerDialogViewModel(localizer, provider.GetRequiredService<IServiceScopeFactory>());

        await vm.InitializeForChangeAsync(
            new AutoPrefillContext("server.example.com", 2222, "alice", "SSH"),
            "action-1",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["host"] = "userTyped.example.com",
                ["port"] = string.Empty
            });

        Assert.NotNull(vm.SelectedAction);
        Assert.Equal("action-1", vm.SelectedAction!.ActionId);
        Assert.Equal("userTyped.example.com", vm.Parameters.Single(p => p.Name == "host").Value);
        Assert.False(vm.Parameters.Single(p => p.Name == "host").IsAutoPrefilled);
        Assert.Equal(string.Empty, vm.Parameters.Single(p => p.Name == "port").Value);
        Assert.False(vm.Parameters.Single(p => p.Name == "port").IsAutoPrefilled);
    }

    [Fact]
    public async Task InitializeForChangeAsync_DeletedAction_SetsErrorMessage()
    {
        var localizer = await CommandLibraryTestHelpers.CreateAppLocalizerAsync();
        var provider = CommandLibraryTestHelpers.CreateResolverServiceProvider(
            CommandLibraryTestHelpers.CreateLinuxAction("action-1", "Tail log", "tail -f /tmp/app.log"));
        var vm = new CommandLibraryPickerDialogViewModel(localizer, provider.GetRequiredService<IServiceScopeFactory>());

        await vm.InitializeForChangeAsync(
            new AutoPrefillContext("server.example.com", 22, "alice", "SSH"),
            "missing",
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Null(vm.SelectedAction);
        Assert.Equal("The previously linked action is no longer available. Select another or cancel.", vm.ErrorMessage);
    }

    private static async Task<CommandLibraryPickerDialogViewModel> CreateViewModelAsync(params ActionModel[] actions)
        => await CreateViewModelAsync(null, actions);

    private static async Task<CommandLibraryPickerDialogViewModel> CreateViewModelAsync(
        AutoPrefillContext? prefillContext,
        params ActionModel[] actions)
    {
        var localizer = await CommandLibraryTestHelpers.CreateAppLocalizerAsync();
        var provider = CommandLibraryTestHelpers.CreateResolverServiceProvider(actions);
        var vm = new CommandLibraryPickerDialogViewModel(localizer, provider.GetRequiredService<IServiceScopeFactory>());
        await vm.InitializeAsync(prefillContext);
        return vm;
    }
}
