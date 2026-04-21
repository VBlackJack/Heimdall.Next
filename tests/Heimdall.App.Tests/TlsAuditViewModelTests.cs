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

using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Security;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Heimdall.App.Tests;

public class TlsAuditViewModelTests
{
    [Fact]
    public async Task RunAudit_EmptyHost_ShowsError()
    {
        var vm = new TlsAuditViewModel();
        vm.Initialize(null);
        vm.Host = "   ";

        await vm.RunAuditCommand.ExecuteAsync(null);

        Assert.True(vm.ShowError);
        Assert.False(vm.ShowResults);
    }

    [Fact]
    public async Task RunAudit_InvalidHost_ShowsError()
    {
        var vm = new TlsAuditViewModel();
        vm.Initialize(null);
        vm.Host = "../../etc/passwd";
        vm.Port = "443";

        await vm.RunAuditCommand.ExecuteAsync(null);

        Assert.True(vm.ShowError);
        Assert.False(vm.ShowResults);
    }

    [Fact]
    public async Task RunAudit_InvalidPort_ShowsError()
    {
        var vm = new TlsAuditViewModel();
        vm.Initialize(null);
        vm.Host = "example.com";
        vm.Port = "99999";

        await vm.RunAuditCommand.ExecuteAsync(null);

        Assert.True(vm.ShowError);
        Assert.False(vm.ShowResults);
    }

    [Fact]
    public async Task RunAudit_WithFakeProber_Success_PopulatesResults()
    {
        using var cert = CreateCertificate();
        var prober = new FakeProber(
            protocolResults: new()
            {
                [SslProtocols.Tls12] = true,
                [SslProtocols.Tls13] = true
            },
            supportedCiphers:
            [
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384
            ],
            cert: new X509Certificate2(cert));

        var vm = new TlsAuditViewModel();
        vm.Initialize(null);
        vm.SetProber(prober);
        vm.Host = "example.com";
        vm.Port = "443";

        await vm.RunAuditCommand.ExecuteAsync(null);

        Assert.True(vm.ShowResults);
        Assert.Equal(TlsGrade.APlus, vm.Grade);
        Assert.Equal(TlsAuditEngine.ProtocolDefinitions.Count, vm.Protocols.Count);
        Assert.Single(vm.Ciphers);
        Assert.NotEmpty(vm.Findings);
        Assert.NotNull(vm.CertificateSummary);
        Assert.False(string.IsNullOrEmpty(vm.ReportText));
        Assert.False(vm.IsAuditing);
    }

    [Fact]
    public async Task RunAudit_WithFakeProber_WeakConfig_GradeF()
    {
        var prober = new FakeProber(
            protocolResults: new()
            {
#pragma warning disable CS0618, SYSLIB0039
                [SslProtocols.Ssl3] = true,
#pragma warning restore CS0618, SYSLIB0039
                [SslProtocols.Tls12] = true
            });

        var vm = new TlsAuditViewModel();
        vm.Initialize(null);
        vm.SetProber(prober);
        vm.Host = "vulnerable.example.com";
        vm.Port = "443";

        await vm.RunAuditCommand.ExecuteAsync(null);

        Assert.True(vm.ShowResults);
        Assert.Equal(TlsGrade.F, vm.Grade);
        Assert.Contains(vm.Findings, finding => finding.Severity == TlsFindingSeverity.Critical);
    }

    [Fact]
    public async Task Cancel_DuringAudit_StopsGracefully()
    {
        var taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingProber = new BlockingProber(taskCompletionSource.Task);

        var vm = new TlsAuditViewModel();
        vm.Initialize(null);
        vm.SetProber(blockingProber);
        vm.Host = "slow.example.com";
        vm.Port = "443";

        var auditTask = vm.RunAuditCommand.ExecuteAsync(null);

        await Task.Delay(50);
        Assert.True(vm.IsAuditing);

        vm.CancelCommand.Execute(null);
        taskCompletionSource.TrySetCanceled();

        await auditTask;

        Assert.False(vm.IsAuditing);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed class FakeProber : ITlsProber
    {
        private readonly Dictionary<SslProtocols, bool> _protocolResults;
        private readonly HashSet<TlsCipherSuite> _supportedCiphers;
        private readonly X509Certificate2? _cert;

        public FakeProber(
            Dictionary<SslProtocols, bool>? protocolResults = null,
            HashSet<TlsCipherSuite>? supportedCiphers = null,
            X509Certificate2? cert = null)
        {
            _protocolResults = protocolResults ?? new Dictionary<SslProtocols, bool>();
            _supportedCiphers = supportedCiphers ?? [];
            _cert = cert;
        }

        public Task<bool> TestProtocolAsync(string host, int port, SslProtocols protocol, CancellationToken ct)
            => Task.FromResult(_protocolResults.GetValueOrDefault(protocol));

        public Task<bool> TestCipherSuiteAsync(string host, int port, TlsCipherSuite suite, CancellationToken ct)
            => Task.FromResult(_supportedCiphers.Contains(suite));

        public Task<X509Certificate2?> RetrieveCertificateAsync(string host, int port, CancellationToken ct)
            => Task.FromResult<X509Certificate2?>(_cert is null ? null : new X509Certificate2(_cert));
    }

    private sealed class BlockingProber : ITlsProber
    {
        private readonly Task<bool> _blockingTask;

        public BlockingProber(Task<bool> blockingTask)
        {
            _blockingTask = blockingTask;
        }

        public async Task<bool> TestProtocolAsync(string host, int port, SslProtocols protocol, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return await _blockingTask.WaitAsync(ct);
        }

        public Task<bool> TestCipherSuiteAsync(string host, int port, TlsCipherSuite suite, CancellationToken ct)
            => Task.FromResult(false);

        public Task<X509Certificate2?> RetrieveCertificateAsync(string host, int port, CancellationToken ct)
            => Task.FromResult<X509Certificate2?>(null);
    }
}
