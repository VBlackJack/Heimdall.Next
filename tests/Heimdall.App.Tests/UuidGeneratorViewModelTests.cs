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
using Heimdall.Core.Identifiers;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class UuidGeneratorViewModelTests
{
    [Fact]
    public void Ctor_DoesNotGenerate()
    {
        var service = new CountingFakeService();
        _ = new UuidGeneratorViewModel(service);

        Assert.Equal(0, service.GenerateCallCount);
    }

    [Fact]
    public void Initialize_SetsDefaults_AndGeneratesInitialSingle()
    {
        var first = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var service = new CountingFakeService(first);
        var vm = new UuidGeneratorViewModel(service);

        vm.Initialize(null);

        Assert.Equal("10", vm.BatchCountText);
        Assert.Equal(UuidGenerator.Format(first, UuidFormat.Default), vm.SingleResult);
        Assert.Equal(1, service.GenerateCallCount);
    }

    [Fact]
    public async Task Initialize_SetsResultLabelForV4()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new UuidGeneratorViewModel(new CountingFakeService(Guid.NewGuid()));

        vm.Initialize(localizer);

        Assert.Equal(localizer["ToolUuidResultLabel"], vm.ResultLabelText);
    }

    [Fact]
    public async Task VersionChange_ToV7_RebuildsResultLabelAndRegenerates()
    {
        var localizer = await CreateLocalizerAsync("en");
        var first = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var second = Guid.Parse("22222222-2222-7222-8222-222222222222");
        var service = new CountingFakeService(first, second);
        var vm = new UuidGeneratorViewModel(service);
        vm.Initialize(localizer);
        vm.MarkInitialized();

        vm.SelectedVersion = UuidVersion.V7;

        Assert.Equal(localizer["ToolUuidResultLabelV7"], vm.ResultLabelText);
        Assert.Equal(UuidGenerator.Format(second, UuidFormat.Default), vm.SingleResult);
        Assert.Equal(2, service.GenerateCallCount);
    }

    [Fact]
    public void UppercaseToggle_ReformatsStoredWithoutRegenerating()
    {
        var guid = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var service = new CountingFakeService(guid);
        var vm = new UuidGeneratorViewModel(service);
        vm.Initialize(null);
        vm.MarkInitialized();
        var before = service.GenerateCallCount;

        vm.Uppercase = true;

        Assert.Equal(before, service.GenerateCallCount);
        Assert.Equal(UuidGenerator.Format(guid, new UuidFormat(true, true)), vm.SingleResult);
    }

    [Fact]
    public void HyphensToggle_ReformatsStoredWithoutRegenerating()
    {
        var guid = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var service = new CountingFakeService(guid);
        var vm = new UuidGeneratorViewModel(service);
        vm.Initialize(null);
        vm.MarkInitialized();
        var before = service.GenerateCallCount;

        vm.WithHyphens = false;

        Assert.Equal(before, service.GenerateCallCount);
        Assert.Equal(UuidGenerator.Format(guid, new UuidFormat(false, false)), vm.SingleResult);
    }

    [Fact]
    public void GenerateCommand_ProducesNewGuid_AndRecordsIt()
    {
        var first = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var second = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var service = new CountingFakeService(first, second);
        var vm = new UuidGeneratorViewModel(service);
        vm.Initialize(null);
        vm.MarkInitialized();

        vm.GenerateCommand.Execute(null);

        Assert.Equal(UuidGenerator.Format(second, UuidFormat.Default), vm.SingleResult);
        Assert.Equal(2, service.GenerateCallCount);
    }

    [Fact]
    public void GenerateBatchCommand_ClampsCountBelowMin_ToMin()
    {
        var vm = CreateInitializedViewModel(new CountingFakeService(Guid.NewGuid(), Guid.NewGuid()));
        vm.BatchCountText = "0";

        vm.GenerateBatchCommand.Execute(null);

        Assert.Equal("1", vm.BatchCountText);
        Assert.Single(SplitLines(vm.BatchResults));
    }

    [Fact]
    public void GenerateBatchCommand_ClampsCountAboveMax_ToMax()
    {
        var vm = CreateInitializedViewModel(new CountingFakeService(Guid.NewGuid()));
        vm.BatchCountText = "500";

        vm.GenerateBatchCommand.Execute(null);

        Assert.Equal("100", vm.BatchCountText);
        Assert.Equal(100, SplitLines(vm.BatchResults).Length);
    }

    [Fact]
    public void GenerateBatchCommand_ParseFailure_FallsBackToMin()
    {
        var vm = CreateInitializedViewModel(new CountingFakeService(Guid.NewGuid(), Guid.NewGuid()));
        vm.BatchCountText = "abc";

        vm.GenerateBatchCommand.Execute(null);

        Assert.Equal("1", vm.BatchCountText);
        Assert.Single(SplitLines(vm.BatchResults));
    }

    [Fact]
    public void GenerateBatchCommand_RecordsBatch_AndFormatRespected()
    {
        var first = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var second = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var third = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var service = new CountingFakeService(first, second, third);
        var vm = CreateInitializedViewModel(service);
        vm.Uppercase = true;
        vm.WithHyphens = false;
        vm.BatchCountText = "2";

        vm.GenerateBatchCommand.Execute(null);

        Assert.Equal(
            string.Join(Environment.NewLine, UuidGenerator.Format(second, new UuidFormat(true, false)), UuidGenerator.Format(third, new UuidFormat(true, false))),
            vm.BatchResults);
    }

    [Fact]
    public void VersionChange_WithNonEmptyBatch_RegeneratesBatchWithSameCount()
    {
        var seeds = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var service = new CountingFakeService(seeds);
        var vm = CreateInitializedViewModel(service);
        vm.BatchCountText = "3";
        vm.GenerateBatchCommand.Execute(null);

        vm.SelectedVersion = UuidVersion.V7;

        Assert.Equal(8, service.GenerateCallCount);
        Assert.Equal(3, SplitLines(vm.BatchResults).Length);
        Assert.Equal(UuidVersion.V7, service.RequestedVersions.Last());
    }

    [Fact]
    public async Task OnLocaleChanged_RebuildsResultLabel_WithoutGeneratingNewGuid()
    {
        var localizer = await CreateLocalizerAsync("en");
        var service = new CountingFakeService(Guid.NewGuid());
        var vm = new UuidGeneratorViewModel(service);
        vm.Initialize(localizer);
        vm.MarkInitialized();
        var before = service.GenerateCallCount;

        await localizer.SwitchLocaleAsync("fr");

        Assert.Equal(before, service.GenerateCallCount);
        Assert.Equal(localizer["ToolUuidResultLabel"], vm.ResultLabelText);
    }

    [Fact]
    public void BatchSeparator_IsNewlineBetweenItems_NoTrailingNewline()
    {
        var service = new CountingFakeService(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var vm = CreateInitializedViewModel(service);
        vm.BatchCountText = "2";

        vm.GenerateBatchCommand.Execute(null);

        Assert.Contains(Environment.NewLine, vm.BatchResults, StringComparison.Ordinal);
        Assert.False(vm.BatchResults.EndsWith(Environment.NewLine, StringComparison.Ordinal));
    }

    private static UuidGeneratorViewModel CreateInitializedViewModel(IUuidGeneratorToolService service)
    {
        var vm = new UuidGeneratorViewModel(service);
        vm.Initialize(null);
        vm.MarkInitialized();
        return vm;
    }

    private static string[] SplitLines(string value)
        => value.Split([Environment.NewLine], StringSplitOptions.None);

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    private sealed class CountingFakeService(params Guid[] seeded) : IUuidGeneratorToolService
    {
        private readonly Queue<Guid> _seeded = new(seeded);

        public int GenerateCallCount { get; private set; }

        public int FormatCallCount { get; private set; }

        public List<UuidVersion> RequestedVersions { get; } = [];

        public Guid Generate(UuidVersion version)
        {
            GenerateCallCount++;
            RequestedVersions.Add(version);
            return _seeded.Count > 0 ? _seeded.Dequeue() : Guid.NewGuid();
        }

        public string Format(Guid guid, UuidFormat format)
        {
            FormatCallCount++;
            return UuidGenerator.Format(guid, format);
        }
    }
}
