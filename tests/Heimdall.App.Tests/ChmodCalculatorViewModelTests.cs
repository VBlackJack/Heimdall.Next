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

using System.IO;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class ChmodCalculatorViewModelTests
{
    [Fact]
    public void ApplyPrefill_ValidOctal_UsesArgument()
    {
        var vm = CreateViewModel();

        vm.ApplyPrefill("644");

        Assert.Equal("644", vm.OctalText);
        Assert.Equal("rw-r--r--", vm.SymbolicText);
        Assert.Equal("chmod 644 filename", vm.CommandPreviewText);
    }

    [Fact]
    public void ApplyPrefill_InvalidOctal_FallsBackTo755()
    {
        var vm = CreateViewModel();

        vm.ApplyPrefill("999");

        Assert.Equal("755", vm.OctalText);
    }

    [Fact]
    public void BitToggle_RecomputesDerivedValues()
    {
        var vm = CreateInitializedViewModel();
        vm.ApplyPresetCommand.Execute("000");

        vm.OwnerR = true;
        vm.OwnerW = true;
        vm.OwnerX = true;
        vm.GroupR = true;
        vm.GroupX = true;
        vm.OthersR = true;

        Assert.Equal("754", vm.OctalText);
        Assert.Equal("rwxr-xr--", vm.SymbolicText);
        Assert.Equal("chmod 754 filename", vm.CommandPreviewText);
    }

    [Fact]
    public void OctalText_ValidInput_UpdatesBitsAndClearsSymbolicPreview()
    {
        var vm = CreateInitializedViewModel();
        vm.ApplySymbolicCommand.Execute(null);
        vm.SymbolicInputText = "u+rwx";
        vm.ApplySymbolicCommand.Execute(null);

        vm.OctalText = "600";

        Assert.True(vm.OwnerR);
        Assert.True(vm.OwnerW);
        Assert.False(vm.OwnerX);
        Assert.Equal("chmod 600 filename", vm.CommandPreviewText);
    }

    [Fact]
    public void OctalText_InvalidInput_IsSilentlyRejected()
    {
        var vm = CreateInitializedViewModel();
        vm.OctalText = "755";

        vm.OctalText = "999";

        Assert.Equal("999", vm.OctalText);
        Assert.Equal("rwxr-xr-x", vm.SymbolicText);
        Assert.Equal("chmod 755 filename", vm.CommandPreviewText);
    }

    [Fact]
    public void ApplyPreset_UsesSingleParameterizedCommand()
    {
        var vm = CreateInitializedViewModel();

        vm.ApplyPresetCommand.Execute("600");

        Assert.Equal("600", vm.OctalText);
        Assert.Equal("rw-------", vm.SymbolicText);
    }

    [Fact]
    public void ApplySymbolic_ValidInput_UpdatesModeAndPreview()
    {
        var vm = CreateInitializedViewModel();
        vm.SymbolicInputText = "u+rwx,g+rx,o+r";

        vm.ApplySymbolicCommand.Execute(null);

        Assert.False(vm.HasSymbolicInputError);
        Assert.Equal("754", vm.OctalText);
        Assert.Equal("chmod u+rwx,g+rx,o+r filename", vm.CommandPreviewText);
    }

    [Fact]
    public void ApplySymbolic_InvalidInput_ShowsLocalizedError_AndPreservesMode()
    {
        var vm = CreateInitializedViewModel();
        vm.OctalText = "755";
        vm.SymbolicInputText = "invalid";

        vm.ApplySymbolicCommand.Execute(null);

        Assert.True(vm.HasSymbolicInputError);
        Assert.Equal("ToolChmodErrorInvalidSymbolic", vm.SymbolicInputErrorText);
        Assert.Equal("755", vm.OctalText);
    }

    [Fact]
    public void ApplySymbolic_EmptyInput_ClearsErrorOnly()
    {
        var vm = CreateInitializedViewModel();
        vm.HasSymbolicInputError = true;
        vm.SymbolicInputErrorText = "old";
        vm.SymbolicInputText = " ";

        vm.ApplySymbolicCommand.Execute(null);

        Assert.False(vm.HasSymbolicInputError);
        Assert.Equal(string.Empty, vm.SymbolicInputErrorText);
    }

    [Fact]
    public async Task LocaleChanged_ReprojectsHelpAndErrorText()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = CreateViewModel();
        vm.Initialize(localizer);
        vm.MarkInitialized();
        vm.SymbolicInputText = "bad";
        vm.ApplySymbolicCommand.Execute(null);
        var english = vm.SymbolicInputErrorText;

        await localizer.SwitchLocaleAsync("fr");

        Assert.NotEqual(english, vm.SymbolicInputErrorText);
        Assert.Contains('\n', vm.HelpText);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var vm = CreateViewModel();

        vm.Dispose();
        vm.Dispose();
    }

    private static ChmodCalculatorViewModel CreateInitializedViewModel()
    {
        var vm = CreateViewModel();
        vm.ApplyPrefill(null);
        vm.MarkInitialized();
        return vm;
    }

    private static ChmodCalculatorViewModel CreateViewModel()
        => new(new ChmodCalculatorToolService());

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }
}
