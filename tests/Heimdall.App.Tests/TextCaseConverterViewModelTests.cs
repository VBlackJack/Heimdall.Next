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

public sealed class TextCaseConverterViewModelTests
{
    [Fact]
    public void ConvertCommand_Camel_SetsSelectedStyleOutputAndHasResult()
    {
        var vm = CreateViewModel();
        vm.InputText = "hello world";

        vm.ConvertCommand.Execute(TextCaseStyle.Camel);

        Assert.Equal(TextCaseStyle.Camel, vm.SelectedStyle);
        Assert.Equal("CAMEL:hello world", vm.OutputText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public void ConvertCommand_Pascal_SetsSelectedStyleOutputAndHasResult()
    {
        var vm = CreateViewModel();
        vm.InputText = "hello world";

        vm.ConvertCommand.Execute(TextCaseStyle.Pascal);

        Assert.Equal(TextCaseStyle.Pascal, vm.SelectedStyle);
        Assert.Equal("PASCAL:hello world", vm.OutputText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public void ConvertCommand_Snake_SetsSelectedStyleOutputAndHasResult()
    {
        var vm = CreateViewModel();
        vm.InputText = "hello world";

        vm.ConvertCommand.Execute(TextCaseStyle.Snake);

        Assert.Equal(TextCaseStyle.Snake, vm.SelectedStyle);
        Assert.Equal("SNAKE:hello world", vm.OutputText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public void ConvertCommand_Kebab_SetsSelectedStyleOutputAndHasResult()
    {
        var vm = CreateViewModel();
        vm.InputText = "hello world";

        vm.ConvertCommand.Execute(TextCaseStyle.Kebab);

        Assert.Equal(TextCaseStyle.Kebab, vm.SelectedStyle);
        Assert.Equal("KEBAB:hello world", vm.OutputText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public void ConvertCommand_Upper_SetsSelectedStyleOutputAndHasResult()
    {
        var vm = CreateViewModel();
        vm.InputText = "hello world";

        vm.ConvertCommand.Execute(TextCaseStyle.Upper);

        Assert.Equal(TextCaseStyle.Upper, vm.SelectedStyle);
        Assert.Equal("UPPER:hello world", vm.OutputText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public void ConvertCommand_Lower_SetsSelectedStyleOutputAndHasResult()
    {
        var vm = CreateViewModel();
        vm.InputText = "hello world";

        vm.ConvertCommand.Execute(TextCaseStyle.Lower);

        Assert.Equal(TextCaseStyle.Lower, vm.SelectedStyle);
        Assert.Equal("LOWER:hello world", vm.OutputText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public void ConvertCommand_Title_SetsSelectedStyleOutputAndHasResult()
    {
        var vm = CreateViewModel();
        vm.InputText = "hello world";

        vm.ConvertCommand.Execute(TextCaseStyle.Title);

        Assert.Equal(TextCaseStyle.Title, vm.SelectedStyle);
        Assert.Equal("TITLE:hello world", vm.OutputText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public void ConvertCommand_Constant_SetsSelectedStyleOutputAndHasResult()
    {
        var vm = CreateViewModel();
        vm.InputText = "hello world";

        vm.ConvertCommand.Execute(TextCaseStyle.Constant);

        Assert.Equal(TextCaseStyle.Constant, vm.SelectedStyle);
        Assert.Equal("CONSTANT:hello world", vm.OutputText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public void InputTextChanged_ReappliesLastConversion()
    {
        var service = new FakeTextCaseConverterService();
        var vm = new TextCaseConverterViewModel(service);
        vm.InputText = "first";
        vm.ConvertCommand.Execute(TextCaseStyle.Camel);

        vm.InputText = "second";

        Assert.Equal("CAMEL:second", vm.OutputText);
        Assert.Equal(2, service.ConvertCalls);
    }

    [Fact]
    public void InputTextChanged_WithoutSelectedStyle_DoesNothing()
    {
        var service = new FakeTextCaseConverterService();
        var vm = new TextCaseConverterViewModel(service);

        vm.InputText = "value";

        Assert.Equal(string.Empty, vm.OutputText);
        Assert.Equal(0, service.ConvertCalls);
        Assert.False(vm.HasResult);
    }

    [Fact]
    public void ClearingInput_AfterConversion_KeepsHasResultTrue()
    {
        var vm = CreateViewModel();
        vm.InputText = "value";
        vm.ConvertCommand.Execute(TextCaseStyle.Title);

        vm.InputText = string.Empty;

        Assert.True(vm.HasResult);
        Assert.Equal("TITLE:", vm.OutputText);
    }

    [Fact]
    public async Task Initialize_TwiceWithSameLocalizer_RemainsStable()
    {
        var vm = CreateViewModel();
        var localizer = await CreateLocalizerAsync("en");

        vm.Initialize(localizer);
        vm.Initialize(localizer);
        vm.InputText = "value";
        vm.ConvertCommand.Execute(TextCaseStyle.Snake);

        Assert.Equal("SNAKE:value", vm.OutputText);
    }

    [Fact]
    public void ContextArgument_IsIgnored_ByVmSurface()
    {
        var vm = CreateViewModel();

        Assert.Equal(string.Empty, vm.InputText);
        Assert.False(vm.HasResult);
        Assert.Null(vm.SelectedStyle);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var vm = CreateViewModel();

        vm.Dispose();
        vm.Dispose();

        Assert.True(true);
    }

    [Fact]
    public void ConvertCommand_ReplacesSelectedStyle()
    {
        var vm = CreateViewModel();
        vm.InputText = "value";
        vm.ConvertCommand.Execute(TextCaseStyle.Camel);

        vm.ConvertCommand.Execute(TextCaseStyle.Constant);

        Assert.Equal(TextCaseStyle.Constant, vm.SelectedStyle);
        Assert.Equal("CONSTANT:value", vm.OutputText);
    }

    private static TextCaseConverterViewModel CreateViewModel()
        => new(new FakeTextCaseConverterService());

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    private sealed class FakeTextCaseConverterService : ITextCaseConverterService
    {
        public int ConvertCalls { get; private set; }

        public string Convert(string input, TextCaseStyle style)
        {
            ConvertCalls++;
            return $"{style.ToString().ToUpperInvariant()}:{input}";
        }
    }
}
