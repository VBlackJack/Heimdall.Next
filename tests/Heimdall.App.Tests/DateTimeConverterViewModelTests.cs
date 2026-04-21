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

using System.Globalization;
using System.IO;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Localization;
using Heimdall.Core.Temporal;

namespace Heimdall.App.Tests;

public sealed class DateTimeConverterViewModelTests
{
    [Fact]
    public void Initialize_LoadsTimezones_AndDefaultsToUtc()
    {
        var vm = CreateViewModel();

        vm.Initialize(null);

        Assert.NotEmpty(vm.Timezones);
        Assert.Equal("UTC", vm.SelectedTimezoneId);
    }

    [Fact]
    public void PrefillInput_Value_SetsInputWithoutClearingState()
    {
        var vm = CreateViewModel();
        vm.Initialize(null);

        vm.PrefillInput("1712345678");

        Assert.Equal("1712345678", vm.InputText);
    }

    [Fact]
    public async Task ConvertCurrentInput_ValidUnixSeconds_PopulatesAllOutputs()
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);
        var localizer = await CreateLocalizerAsync("en");
        var vm = CreateViewModel();
        vm.Initialize(localizer);
        vm.MarkInitialized();
        vm.InputText = "1712345678";

        vm.ConvertCurrentInput();

        Assert.True(vm.HasResult);
        Assert.False(vm.ShowEmptyState);
        Assert.Equal("1712345678", vm.UnixTimestampText);
        Assert.Equal("2024-04-05T19:34:38.0000000Z", vm.IsoUtcText);
        Assert.Contains("Detected:", vm.DetectedFormatText, StringComparison.Ordinal);
        Assert.Contains("ago", vm.RelativeTimeText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertCurrentInput_ValidIso_ReformatsTimezoneWithoutReparse()
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);
        var service = new CountingDateTimeConverterToolService();
        var vm = new DateTimeConverterViewModel(service, CreateTimezones);
        vm.Initialize(null);
        vm.MarkInitialized();
        vm.InputText = "2024-12-25T10:30:45Z";

        vm.ConvertCurrentInput();
        var parseCallsAfterConvert = service.ParseCallCount;

        vm.SelectedTimezoneId = "Custom/Plus2";

        Assert.Equal(parseCallsAfterConvert, service.ParseCallCount);
        Assert.Equal("Wednesday, 25 December 2024 12:30:45", vm.TimezoneTimeText);
    }

    [Fact]
    public void ConvertCurrentInput_InvalidInput_ShowsErrorAndHidesPanels()
    {
        var vm = CreateInitializedViewModel();
        vm.InputText = "not-a-date";

        vm.ConvertCurrentInput();

        Assert.False(vm.HasResult);
        Assert.False(vm.ShowEmptyState);
        Assert.Equal(string.Empty, vm.UnixTimestampText);
        Assert.Equal("ToolDateTimeErrorInvalid", vm.DetectedFormatText);
    }

    [Fact]
    public void ConvertCurrentInput_EmptyInput_ClearsToEmptyState()
    {
        var vm = CreateInitializedViewModel();
        vm.InputText = "1712345678";
        vm.ConvertCurrentInput();
        vm.InputText = "   ";

        vm.ConvertCurrentInput();

        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.HasResult);
        Assert.Equal(string.Empty, vm.DetectedFormatText);
    }

    [Fact]
    public void InputText_ChangedAfterInitialization_ClearsOutputsAndShowsEmptyState()
    {
        var vm = CreateInitializedViewModel();
        vm.UnixTimestampText = "123";
        vm.DetectedFormatText = "Detected";
        vm.HasResult = true;
        vm.ShowEmptyState = false;

        vm.InputText = "1712345678";

        Assert.Equal(string.Empty, vm.UnixTimestampText);
        Assert.Equal(string.Empty, vm.DetectedFormatText);
        Assert.False(vm.HasResult);
        Assert.True(vm.ShowEmptyState);
    }

    [Fact]
    public void UseNowCommand_SetsUnixSecondsInput()
    {
        var vm = CreateInitializedViewModel();

        vm.UseNowCommand.Execute(null);

        Assert.True(long.TryParse(vm.InputText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _));
    }

    [Fact]
    public async Task LocaleChanged_RebuildsDetectedAndErrorTexts()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = CreateViewModel();
        vm.Initialize(localizer);
        vm.MarkInitialized();
        vm.InputText = "1712345678";
        vm.ConvertCurrentInput();
        var englishSuccess = vm.DetectedFormatText;

        await localizer.SwitchLocaleAsync("fr");
        var frenchSuccess = vm.DetectedFormatText;

        vm.InputText = "invalid";
        vm.ConvertCurrentInput();
        var frenchError = vm.DetectedFormatText;

        await localizer.SwitchLocaleAsync("en");
        var englishError = vm.DetectedFormatText;

        Assert.NotEqual(englishSuccess, frenchSuccess);
        Assert.NotEqual(frenchError, englishError);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var vm = CreateViewModel();

        vm.Dispose();
        vm.Dispose();
    }

    private static DateTimeConverterViewModel CreateInitializedViewModel()
    {
        var vm = CreateViewModel();
        vm.Initialize(null);
        vm.MarkInitialized();
        return vm;
    }

    private static DateTimeConverterViewModel CreateViewModel()
        => new(new DateTimeConverterToolService(), CreateTimezones);

    private static IEnumerable<TimeZoneInfo> CreateTimezones()
        => [
            TimeZoneInfo.Utc,
            TimeZoneInfo.CreateCustomTimeZone("Custom/Plus2", TimeSpan.FromHours(2), "Custom/Plus2", "Custom/Plus2"),
        ];

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    private sealed class CountingDateTimeConverterToolService : IDateTimeConverterToolService
    {
        private readonly DateTimeConverterToolService _inner = new();

        public int ParseCallCount { get; private set; }

        public DateTimeParseOutcome Parse(string? input)
        {
            ParseCallCount++;
            return _inner.Parse(input);
        }

        public RelativeDuration ComputeRelative(DateTimeOffset input, DateTimeOffset now)
            => _inner.ComputeRelative(input, now);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(CultureInfo culture)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}
