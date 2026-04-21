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
using System.Text;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class Base64ToolViewModelTests
{
    [Fact]
    public async Task PrefillInput_EncodesImmediately()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService());
        vm.Initialize(await CreateLocalizerAsync("en"));

        await vm.PrefillInput("abc");

        Assert.Equal("YWJj", vm.OutputText);
        Assert.True(vm.IsResultsPanelVisible);
        Assert.Equal("Encoded 3 bytes", vm.StatusText);
    }

    [Fact]
    public async Task EncodeCommand_UsesUtf8Input()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService());
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.MarkInitialized();
        vm.InputText = "hé";

        await vm.EncodeCommand.ExecuteAsync(null);

        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("hé"), Base64FormattingOptions.InsertLineBreaks), vm.OutputText);
    }

    [Fact]
    public async Task DecodeCommand_DecodesTextOutput()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService());
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.MarkInitialized();
        vm.InputText = "YWJj";

        await vm.DecodeCommand.ExecuteAsync(null);

        Assert.Equal("abc", vm.OutputText);
        Assert.Equal("Decoded 3 bytes", vm.StatusText);
        Assert.Equal(new byte[] { 97, 98, 99 }, vm.TryGetLastDecodedBytes());
    }

    [Fact]
    public async Task DecodeCommand_InvalidInput_ShowsLocalizedError()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService { DecodeException = new FormatException() });
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.MarkInitialized();
        vm.InputText = "%%%";

        await vm.DecodeCommand.ExecuteAsync(null);

        Assert.Equal("Invalid Base64 input", vm.StatusText);
        Assert.Equal("ErrorTextBrush", vm.StatusForegroundBrushKey);
    }

    [Fact]
    public async Task EncodeCommand_FileMode_UsesLoadedBytes()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService
        {
            LoadOutcome = new FileLoadOutcome(true, [251, 255, 255], "data.bin", FileLoadError.None),
        });
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.MarkInitialized();
        vm.IsFileMode = true;
        vm.InputText = "text";
        await vm.LoadFileAsync("ignored", CancellationToken.None);
        vm.IsUrlSafe = true;

        await vm.EncodeCommand.ExecuteAsync(null);

        Assert.Equal("-___", vm.OutputText);
    }

    [Fact]
    public async Task DecodeCommand_FileMode_CachesBytesWithoutChangingOutput()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService());
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.MarkInitialized();
        vm.IsFileMode = true;
        vm.OutputText = "previous";
        vm.InputText = "YWJj";

        await vm.DecodeCommand.ExecuteAsync(null);

        Assert.Equal("previous", vm.OutputText);
        Assert.Equal(new byte[] { 97, 98, 99 }, vm.TryGetLastDecodedBytes());
        Assert.Equal(string.Empty, vm.StatusText);
    }

    [Fact]
    public void OnInputTextChangedFromView_AfterInitialization_ClearsOutputAndStatus()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService());
        vm.MarkInitialized();
        vm.OutputText = "value";
        vm.StatusText = "status";
        vm.IsResultsPanelVisible = true;
        vm.IsEmptyStateVisible = false;

        vm.OnInputTextChangedFromView();

        Assert.Equal(string.Empty, vm.OutputText);
        Assert.Equal(string.Empty, vm.StatusText);
        Assert.False(vm.IsResultsPanelVisible);
        Assert.True(vm.IsEmptyStateVisible);
    }

    [Fact]
    public async Task LoadFileAsync_Success_SetsReadOnlyInput()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService
        {
            LoadOutcome = new FileLoadOutcome(true, [1, 2, 3], "data.bin", FileLoadError.None),
        });
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.MarkInitialized();

        await vm.LoadFileAsync("ignored", CancellationToken.None);

        Assert.True(vm.IsInputReadOnly);
        Assert.Contains("data.bin", vm.InputText, StringComparison.Ordinal);
        Assert.Null(vm.TryGetLastDecodedBytes());
    }

    [Fact]
    public async Task LoadFileAsync_FileTooLarge_ShowsLocalizedError()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService
        {
            LoadOutcome = new FileLoadOutcome(false, null, null, FileLoadError.FileTooLarge),
        });
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.MarkInitialized();

        await vm.LoadFileAsync("ignored", CancellationToken.None);

        Assert.Equal("File exceeds the 5 MB size limit.", vm.StatusText);
        Assert.Equal("ErrorTextBrush", vm.StatusForegroundBrushKey);
    }

    [Fact]
    public async Task LoadFileAsync_IoFailure_ShowsFormattedError()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService
        {
            LoadOutcome = new FileLoadOutcome(false, null, null, FileLoadError.IoFailure, "disk error"),
        });
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.MarkInitialized();

        await vm.LoadFileAsync("ignored", CancellationToken.None);

        Assert.Equal("Error: disk error", vm.StatusText);
    }

    [Fact]
    public async Task SaveFileAsync_WithDecodedBytes_ReportsSavedStatus()
    {
        var service = new FakeBase64ToolService();
        var vm = new Base64ToolViewModel(service);
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.MarkInitialized();
        vm.InputText = "YWJj";
        await vm.DecodeCommand.ExecuteAsync(null);

        await vm.SaveFileAsync("C:\\temp\\saved.bin", CancellationToken.None);

        Assert.Equal("Saved to C:\\temp\\saved.bin", vm.StatusText);
        Assert.Equal(new byte[] { 97, 98, 99 }, service.SavedBytes);
    }

    [Fact]
    public async Task SaveFileAsync_ServiceThrows_ShowsFormattedError()
    {
        var service = new FakeBase64ToolService { SaveException = new IOException("write failed") };
        var vm = new Base64ToolViewModel(service);
        vm.Initialize(await CreateLocalizerAsync("en"));
        vm.MarkInitialized();
        vm.InputText = "YWJj";
        await vm.DecodeCommand.ExecuteAsync(null);

        await vm.SaveFileAsync("C:\\temp\\saved.bin", CancellationToken.None);

        Assert.Equal("Error: write failed", vm.StatusText);
        Assert.Equal("ErrorTextBrush", vm.StatusForegroundBrushKey);
    }

    [Fact]
    public void IsFileMode_TurnedOff_ResetsState()
    {
        var vm = new Base64ToolViewModel(new FakeBase64ToolService());
        vm.IsFileMode = true;
        vm.IsInputReadOnly = true;
        vm.InputText = "loaded";
        vm.OutputText = "result";
        vm.StatusText = "status";
        vm.IsResultsPanelVisible = true;
        vm.IsEmptyStateVisible = false;

        vm.IsFileMode = false;

        Assert.False(vm.IsBrowseFileButtonVisible);
        Assert.False(vm.IsInputReadOnly);
        Assert.Equal(string.Empty, vm.InputText);
        Assert.Equal(string.Empty, vm.OutputText);
        Assert.Equal(string.Empty, vm.StatusText);
        Assert.True(vm.IsEmptyStateVisible);
    }

    [Fact]
    public async Task LocaleChanged_ReprojectsStatus()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new Base64ToolViewModel(new FakeBase64ToolService());
        vm.Initialize(localizer);
        await vm.PrefillInput("abc");
        var english = vm.StatusText;

        await localizer.SwitchLocaleAsync("fr");

        Assert.NotEqual(english, vm.StatusText);
        Assert.Equal("3 octets encodés", vm.StatusText);
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    private sealed class FakeBase64ToolService : IBase64ToolService
    {
        public Exception? DecodeException { get; set; }
        public Exception? SaveException { get; set; }

        public FileLoadOutcome LoadOutcome { get; set; } =
            new(true, [1, 2, 3], "data.bin", FileLoadError.None);

        public byte[]? SavedBytes { get; private set; }

        public Task<string> EncodeAsync(byte[] data, bool urlSafe, CancellationToken ct)
            => Task.FromResult(Heimdall.Core.Codecs.Base64Codec.Encode(data, urlSafe));

        public Task<byte[]> DecodeAsync(string base64, bool urlSafe, CancellationToken ct)
        {
            if (DecodeException is not null)
            {
                return Task.FromException<byte[]>(DecodeException);
            }

            return Task.FromResult(Heimdall.Core.Codecs.Base64Codec.Decode(base64, urlSafe));
        }

        public Task<FileLoadOutcome> LoadFileAsync(string path, long maxBytes, CancellationToken ct)
            => Task.FromResult(LoadOutcome);

        public Task SaveFileAsync(string path, byte[] data, CancellationToken ct)
        {
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            SavedBytes = data;
            return Task.CompletedTask;
        }
    }
}
