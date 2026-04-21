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
using Heimdall.Core.Codecs;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class IpConverterViewModelTests
{
    [Fact]
    public void InputText_Empty_SetsEmptyState()
    {
        var vm = CreateViewModel();

        vm.InputText = string.Empty;

        Assert.False(vm.HasError);
        Assert.False(vm.HasResult);
        Assert.True(vm.IsEmptyState);
    }

    [Fact]
    public void InputText_Whitespace_SetsEmptyState()
    {
        var vm = CreateViewModel();

        vm.InputText = "   ";

        Assert.False(vm.HasError);
        Assert.False(vm.HasResult);
        Assert.True(vm.IsEmptyState);
    }

    [Fact]
    public void InputText_Invalid_SetsErrorState()
    {
        var vm = CreateViewModel(new FakeIpConverterToolService { ShouldSucceed = false });

        vm.InputText = "invalid";

        Assert.True(vm.HasError);
        Assert.False(vm.HasResult);
        Assert.False(vm.IsEmptyState);
        Assert.Equal(string.Empty, vm.DottedText);
    }

    [Fact]
    public void InputText_Valid_SetsSuccessStateAndFields()
    {
        var vm = CreateViewModel();

        vm.InputText = "192.168.1.1";

        Assert.False(vm.HasError);
        Assert.True(vm.HasResult);
        Assert.False(vm.IsEmptyState);
        Assert.Equal("192.168.1.1", vm.DottedText);
        Assert.Equal("3232235777", vm.DecimalText);
        Assert.Equal("0xC0A80101", vm.HexText);
        Assert.Equal("11000000.10101000.00000001.00000001", vm.BinaryText);
        Assert.Equal("::ffff:c0a8:0101", vm.MappedIpv6Text);
    }

    [Fact]
    public void PrefillInput_Null_DoesNothing()
    {
        var service = new FakeIpConverterToolService();
        var vm = CreateViewModel(service);

        vm.PrefillInput(null);

        Assert.Equal(string.Empty, vm.InputText);
        Assert.Equal(0, service.TryConvertCalls);
    }

    [Fact]
    public void PrefillInput_UsesTargetHostAndTriggersConversion()
    {
        var vm = CreateViewModel();

        vm.PrefillInput("192.168.1.1");

        Assert.Equal("192.168.1.1", vm.InputText);
        Assert.True(vm.HasResult);
        Assert.Equal("3232235777", vm.DecimalText);
    }

    [Fact]
    public void Reset_ClearsFieldsAndState()
    {
        var vm = CreateViewModel();
        vm.InputText = "192.168.1.1";

        vm.Reset();

        Assert.Equal(string.Empty, vm.InputText);
        Assert.Equal(string.Empty, vm.DottedText);
        Assert.False(vm.HasError);
        Assert.False(vm.HasResult);
        Assert.True(vm.IsEmptyState);
    }

    [Fact]
    public async Task Initialize_TwiceWithSameLocalizer_RemainsStable()
    {
        var vm = CreateViewModel();
        var localizer = await CreateLocalizerAsync("en");

        vm.Initialize(localizer);
        vm.Initialize(localizer);
        vm.InputText = "192.168.1.1";

        Assert.True(vm.HasResult);
        Assert.Equal("192.168.1.1", vm.DottedText);
    }

    [Fact]
    public void InputText_Trimmed_BeforeCallingService()
    {
        var service = new FakeIpConverterToolService();
        var vm = CreateViewModel(service);

        vm.InputText = " 192.168.1.1 ";

        Assert.Equal("192.168.1.1", service.LastInput);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var vm = CreateViewModel();

        vm.Dispose();
        vm.Dispose();

        Assert.True(true);
    }

    private static IpConverterViewModel CreateViewModel(IIpConverterToolService? service = null)
        => new(service ?? new FakeIpConverterToolService());

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    private sealed class FakeIpConverterToolService : IIpConverterToolService
    {
        public bool ShouldSucceed { get; set; } = true;

        public int TryConvertCalls { get; private set; }

        public string LastInput { get; private set; } = string.Empty;

        public bool TryConvert(string input, out IpConversionResult result)
        {
            TryConvertCalls++;
            LastInput = input;

            if (!ShouldSucceed)
            {
                result = default;
                return false;
            }

            result = new IpConversionResult(
                "192.168.1.1",
                "3232235777",
                "0xC0A80101",
                "11000000.10101000.00000001.00000001",
                "::ffff:c0a8:0101");
            return true;
        }
    }
}
