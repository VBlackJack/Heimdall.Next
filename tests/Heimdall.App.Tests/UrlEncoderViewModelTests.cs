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

public sealed class UrlEncoderViewModelTests
{
    [Fact]
    public void DecodedText_BeforeInitialization_DoesNotPropagate()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = new UrlEncoderViewModel(service);

        vm.DecodedText = "abc";

        Assert.Equal(string.Empty, vm.EncodedText);
        Assert.Equal(0, service.EncodeCalls);
    }

    [Fact]
    public async Task PrefillDecoded_NonEmpty_EncodesImmediately()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = new UrlEncoderViewModel(service);
        vm.Initialize(await CreateLocalizerAsync("en"));

        vm.PrefillDecoded("abc");

        Assert.Equal("abc", vm.DecodedText);
        Assert.Equal("ENC(False):abc", vm.EncodedText);
        Assert.Equal(1, service.EncodeCalls);
    }

    [Fact]
    public void PrefillDecoded_Whitespace_OnlyMarksInitialized()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = new UrlEncoderViewModel(service);

        vm.PrefillDecoded("   ");
        vm.DecodedText = "later";

        Assert.Equal("ENC(False):later", vm.EncodedText);
        Assert.Equal(1, service.EncodeCalls);
    }

    [Fact]
    public void DecodedTextChange_EncodesValue()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = CreateInitializedViewModel(service);

        vm.DecodedText = "hello";

        Assert.Equal("ENC(False):hello", vm.EncodedText);
        Assert.False(vm.HasDecodeError);
    }

    [Fact]
    public void DecodedTextChange_Empty_ClearsEncodedAndError()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = CreateInitializedViewModel(service);
        vm.HasDecodeError = true;
        vm.EncodedText = "old";

        vm.DecodedText = string.Empty;

        Assert.Equal(string.Empty, vm.EncodedText);
        Assert.False(vm.HasDecodeError);
    }

    [Fact]
    public void EncodedTextChange_DecodesValue()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = CreateInitializedViewModel(service);

        vm.EncodedText = "abc";

        Assert.Equal("DEC:abc", vm.DecodedText);
        Assert.False(vm.HasDecodeError);
    }

    [Fact]
    public void EncodedTextChange_Empty_ClearsDecodedAndError()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = CreateInitializedViewModel(service);
        vm.HasDecodeError = true;
        vm.DecodedText = "value";

        vm.EncodedText = string.Empty;

        Assert.Equal(string.Empty, vm.DecodedText);
        Assert.False(vm.HasDecodeError);
    }

    [Fact]
    public void EncodedTextChange_Invalid_SetsErrorAndClearsDecoded()
    {
        var service = new FakeUrlEncoderToolService { ThrowOnDecode = true };
        var vm = CreateInitializedViewModel(service);
        vm.DecodedText = "old";

        vm.EncodedText = "%%%";

        Assert.Equal(string.Empty, vm.DecodedText);
        Assert.True(vm.HasDecodeError);
    }

    [Fact]
    public void EditingDecodedAfterDecodeError_ClearsErrorAndReencodes()
    {
        var service = new FakeUrlEncoderToolService { ThrowOnDecode = true };
        var vm = CreateInitializedViewModel(service);
        vm.EncodedText = "bad";
        service.ThrowOnDecode = false;

        vm.DecodedText = "fixed";

        Assert.Equal("ENC(False):fixed", vm.EncodedText);
        Assert.False(vm.HasDecodeError);
    }

    [Fact]
    public void ComponentModeChanged_ReencodesCurrentDecoded()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = CreateInitializedViewModel(service);
        vm.DecodedText = "value";

        vm.ComponentMode = true;

        Assert.Equal("ENC(True):value", vm.EncodedText);
        Assert.Equal(2, service.EncodeCalls);
    }

    [Fact]
    public void ComponentModeChanged_WithEmptyDecoded_DoesNotCallService()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = CreateInitializedViewModel(service);

        vm.ComponentMode = true;

        Assert.Equal(0, service.EncodeCalls);
        Assert.Equal(string.Empty, vm.EncodedText);
    }

    [Fact]
    public void DecodedPropagation_DoesNotReenterDecode()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = CreateInitializedViewModel(service);

        vm.DecodedText = "value";

        Assert.Equal(1, service.EncodeCalls);
        Assert.Equal(0, service.DecodeCalls);
    }

    [Fact]
    public void EncodedPropagation_DoesNotReenterEncode()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = CreateInitializedViewModel(service);

        vm.EncodedText = "value";

        Assert.Equal(0, service.EncodeCalls);
        Assert.Equal(1, service.DecodeCalls);
    }

    [Fact]
    public async Task Initialize_TwiceWithSameLocalizer_RemainsStable()
    {
        var service = new FakeUrlEncoderToolService();
        var vm = new UrlEncoderViewModel(service);
        var localizer = await CreateLocalizerAsync("en");

        vm.Initialize(localizer);
        vm.Initialize(localizer);
        vm.MarkInitialized();
        vm.DecodedText = "value";

        Assert.Equal("ENC(False):value", vm.EncodedText);
        Assert.Equal(1, service.EncodeCalls);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var vm = new UrlEncoderViewModel(new FakeUrlEncoderToolService());

        vm.Dispose();
        vm.Dispose();

        Assert.True(true);
    }

    [Fact]
    public void Reset_ClearsTextsAndError()
    {
        var vm = CreateInitializedViewModel(new FakeUrlEncoderToolService());
        vm.DecodedText = "decoded";
        vm.EncodedText = "encoded";
        vm.HasDecodeError = true;

        vm.Reset();
        vm.MarkInitialized();
        vm.DecodedText = "next";

        Assert.Equal("next", vm.DecodedText);
        Assert.Equal("ENC(False):next", vm.EncodedText);
        Assert.False(vm.HasDecodeError);
    }

    private static UrlEncoderViewModel CreateInitializedViewModel(FakeUrlEncoderToolService service)
    {
        var vm = new UrlEncoderViewModel(service);
        vm.MarkInitialized();
        return vm;
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    private sealed class FakeUrlEncoderToolService : IUrlEncoderToolService
    {
        public int EncodeCalls { get; private set; }

        public int DecodeCalls { get; private set; }

        public bool ThrowOnDecode { get; set; }

        public string Encode(string input, bool componentMode)
        {
            EncodeCalls++;
            return $"ENC({componentMode}):{input}";
        }

        public string Decode(string input)
        {
            DecodeCalls++;
            if (ThrowOnDecode)
            {
                throw new UriFormatException("invalid");
            }

            return $"DEC:{input}";
        }
    }
}
