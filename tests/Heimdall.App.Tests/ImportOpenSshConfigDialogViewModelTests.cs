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
using Heimdall.App.Services.Import;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.Core.Configuration;
using Heimdall.Core.Ssh;

namespace Heimdall.App.Tests;

public sealed class ImportOpenSshConfigDialogViewModelTests
{
    [Fact]
    public async Task Initialize_SetsAllNewSelected_AndAllDuplicatesUnselected()
    {
        using var fixture = await OpenSshDialogFixture.CreateAsync();
        await fixture.ConfigManager.SaveServersAsync(
        [
            new ServerProfileDto
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = "prod",
                ConnectionType = "SSH",
                RemoteServer = "existing.example.com"
            }
        ]);

        await fixture.ViewModel.InitializeAsync(new OpenSshParseResult(
        [
            new OpenSshImportCandidate { Alias = "prod", HostName = "prod.example.com", SourceLineNumber = 1 },
            new OpenSshImportCandidate { Alias = "logs", HostName = "logs.example.com", SourceLineNumber = 2 }
        ],
        [
            new OpenSshImportDiagnostic(OpenSshDiagnosticLevel.Warning, 2, OpenSshDiagnosticCode.HostNameFallbackToAlias, "logs")
        ]));

        Assert.Equal(2, fixture.ViewModel.Items.Count);
        Assert.False(fixture.ViewModel.Items[0].IsSelected);
        Assert.True(fixture.ViewModel.Items[1].IsSelected);
        Assert.True(fixture.ViewModel.HasDiagnostics);
    }

    [Fact]
    public async Task ConfirmCommand_OnlyCallsImporterWithSelectedCandidates()
    {
        using var fixture = await OpenSshDialogFixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(new OpenSshParseResult(
        [
            new OpenSshImportCandidate { Alias = "prod", HostName = "prod.example.com", SourceLineNumber = 1 },
            new OpenSshImportCandidate { Alias = "logs", HostName = "logs.example.com", SourceLineNumber = 2 }
        ],
        []));

        fixture.ViewModel.Items[1].IsSelected = false;
        await fixture.ViewModel.ConfirmCommand.ExecuteAsync(null);

        var servers = await fixture.ConfigManager.LoadServersAsync();
        var imported = Assert.Single(servers);
        Assert.Equal("prod", imported.DisplayName);
        Assert.NotNull(fixture.ViewModel.Result);
        Assert.Equal(1, fixture.ViewModel.Result!.ImportedCount);
        Assert.Equal(0, fixture.ViewModel.Result!.SkippedDuplicates);
    }

    [Fact]
    public async Task Initialize_ProxyJumpRow_ShowsGatewayChain()
    {
        using var fixture = await OpenSshDialogFixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(new OpenSshParseResult(
        [
            new OpenSshImportCandidate
            {
                Alias = "prod",
                HostName = "prod.example.com",
                SourceLineNumber = 1,
                ProxyJumpChain =
                [
                    new OpenSshProxyJumpHop
                    {
                        Host = "bastion",
                        HostName = "bastion.example.com",
                        User = "ops",
                        Port = 2222,
                        SourceLineNumber = 2
                    }
                ]
            }
        ],
        []));

        var item = Assert.Single(fixture.ViewModel.Items);
        Assert.Equal("ops@bastion.example.com:2222", item.GatewayChain);
    }

    [Fact]
    public async Task Initialize_ProxyJumpDiagnostic_ShowsWarningMessage()
    {
        using var fixture = await OpenSshDialogFixture.CreateAsync();
        await fixture.ViewModel.InitializeAsync(new OpenSshParseResult(
        [
            new OpenSshImportCandidate { Alias = "prod", HostName = "prod.example.com", SourceLineNumber = 1 }
        ],
        [
            new OpenSshImportDiagnostic(
                OpenSshDiagnosticLevel.Warning,
                2,
                OpenSshDiagnosticCode.ProxyJumpTokenSubstitution,
                "%h")
        ]));

        var diagnostic = Assert.Single(fixture.ViewModel.Diagnostics);
        Assert.True(diagnostic.IsWarning);
        Assert.Contains("%h", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initialize_ReusedGateway_IsDistinguishedInGatewayChain()
    {
        using var fixture = await OpenSshDialogFixture.CreateAsync();
        var settings = await fixture.ConfigManager.LoadSettingsAsync();
        settings.SshGateways.Add(new SshGatewayDto
        {
            Id = "existing",
            Name = "Existing Bastion",
            Host = "bastion.example.com",
            Port = 22,
            User = "ops"
        });
        await fixture.ConfigManager.SaveSettingsAsync(settings);

        await fixture.ViewModel.InitializeAsync(new OpenSshParseResult(
        [
            new OpenSshImportCandidate
            {
                Alias = "prod",
                HostName = "prod.example.com",
                SourceLineNumber = 1,
                ProxyJumpChain =
                [
                    new OpenSshProxyJumpHop
                    {
                        Host = "bastion.example.com",
                        HostName = "bastion.example.com",
                        User = "ops",
                        Port = 22,
                        SourceLineNumber = 2
                    }
                ]
            }
        ],
        []));

        var item = Assert.Single(fixture.ViewModel.Items);
        Assert.Contains("Existing Bastion", item.GatewayChain, StringComparison.Ordinal);
    }

    private sealed class OpenSshDialogFixture : IDisposable
    {
        private OpenSshDialogFixture(string rootPath, ConfigManager configManager, ImportOpenSshConfigDialogViewModel viewModel)
        {
            RootPath = rootPath;
            ConfigManager = configManager;
            ViewModel = viewModel;
        }

        public string RootPath { get; }

        public ConfigManager ConfigManager { get; }

        public ImportOpenSshConfigDialogViewModel ViewModel { get; }

        public static async Task<OpenSshDialogFixture> CreateAsync()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "heimdall-b60-dialog-tests", Guid.NewGuid().ToString("N"));
            var configManager = new ConfigManager(rootPath);
            await configManager.InitializeAsync();
            var localizer = await CommandLibraryTestHelpers.CreateAppLocalizerAsync();
            var importer = new OpenSshConfigImporter(configManager);
            var viewModel = new ImportOpenSshConfigDialogViewModel(localizer, importer);
            return new OpenSshDialogFixture(rootPath, configManager, viewModel);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
