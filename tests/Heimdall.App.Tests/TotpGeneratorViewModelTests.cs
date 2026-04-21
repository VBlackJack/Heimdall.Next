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
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class TotpGeneratorViewModelTests
{
    private const string SeedBase32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Fact]
    public async Task Initialize_SetsRemainingSeconds_ToDefault()
    {
        var vm = new TotpGeneratorViewModel();
        vm.Initialize(await CreateLocalizerAsync("en"));

        Assert.Equal(30, vm.RemainingSeconds);
        Assert.Equal("30s remaining", vm.TimeRemainingText);
    }

    [Fact]
    public async Task Start_EmptySecret_ShowsRequiredError()
    {
        var vm = new TotpGeneratorViewModel();
        vm.Initialize(await CreateLocalizerAsync("en"));

        vm.StartCommand.Execute(null);

        Assert.Equal("Please enter a secret key.", vm.ErrorMessage);
        Assert.True(vm.IsErrorVisible);
        Assert.False(vm.IsCodePanelVisible);
    }

    [Fact]
    public async Task Start_InvalidBase32_ShowsInvalidError()
    {
        var vm = new TotpGeneratorViewModel();
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.SecretInput = "invalid!";

        vm.StartCommand.Execute(null);

        Assert.Equal("Invalid Base32 encoding. Use characters A-Z and 2-7 only.", vm.ErrorMessage);
        Assert.True(vm.IsErrorVisible);
    }

    [Fact]
    public async Task Start_ValidSecret_MakesCodePanelVisible()
    {
        var vm = new TotpGeneratorViewModel();
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.SecretInput = SeedBase32;

        vm.StartCommand.Execute(null);

        Assert.True(vm.IsCodePanelVisible);
        Assert.False(vm.IsErrorVisible);
    }

    [Fact]
    public async Task Start_ValidSecret_SanitizesInput()
    {
        var vm = new TotpGeneratorViewModel();
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.SecretInput = " gezd gnbv gy3t qojq gezd gnbv gy3t qojq ";

        vm.StartCommand.Execute(null);
        vm.RefreshCode(59L);

        Assert.Equal("287082", vm.CurrentCode);
        Assert.False(vm.IsErrorVisible);
    }

    [Fact]
    public async Task RefreshCode_WithoutSecret_IsNoop()
    {
        var vm = new TotpGeneratorViewModel();
        vm.Initialize(await CreateLocalizerAsync("en"));

        vm.RefreshCode(59L);

        Assert.Equal("------", vm.CurrentCode);
    }

    [Fact]
    public async Task RefreshCode_WithSecret_UpdatesCode()
    {
        var vm = new TotpGeneratorViewModel();
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.SecretInput = SeedBase32;
        vm.StartCommand.Execute(null);

        vm.RefreshCode(59L);

        Assert.Equal("287082", vm.CurrentCode);
        Assert.Equal(1, vm.RemainingSeconds);
        Assert.Equal("1s remaining", vm.TimeRemainingText);
    }

    [Fact]
    public async Task CanCopy_PlaceholderCode_ReturnsFalse()
    {
        var vm = new TotpGeneratorViewModel();
        vm.Initialize(await CreateLocalizerAsync("en"));

        Assert.False(vm.CanCopy());
    }

    [Fact]
    public async Task CanCopy_RealCode_ReturnsTrue()
    {
        var vm = new TotpGeneratorViewModel();
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.SecretInput = SeedBase32;
        vm.StartCommand.Execute(null);
        vm.RefreshCode(59L);

        Assert.True(vm.CanCopy());
    }

    [Fact]
    public async Task Dispose_UnsubscribesLocaleChangedAndRefreshBecomesNoop()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new TotpGeneratorViewModel();
        vm.Initialize(localizer);
        vm.SecretInput = SeedBase32;
        vm.StartCommand.Execute(null);
        vm.RefreshCode(59L);
        var code = vm.CurrentCode;
        var timeText = vm.TimeRemainingText;

        vm.Dispose();
        vm.RefreshCode(1111111109L);
        await localizer.SwitchLocaleAsync("fr");

        Assert.Equal(code, vm.CurrentCode);
        Assert.Equal(timeText, vm.TimeRemainingText);
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }
}
