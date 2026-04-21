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

using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Configuration;
using Heimdall.Core.Localization;
using Heimdall.Core.Network;
using System.IO;

namespace Heimdall.App.Tests;

public class HttpHeaderAnalyzerViewModelTests
{
    [Fact]
    public void CheckCommand_EmptyUrl_SetsError()
    {
        var vm = new HttpHeaderAnalyzerViewModel(new FakeHttpHeaderService());

        vm.UrlInput = string.Empty;
        vm.CheckCommand.Execute(null);

        Assert.True(vm.ShowError);
        Assert.Equal("ToolHttpHeadersErrorUrlRequired", vm.ErrorText);
        Assert.False(vm.HasResults);
    }

    [Fact]
    public async Task CheckCommand_Success_PopulatesResults()
    {
        var service = new FakeHttpHeaderService
        {
            Response = HttpHeaderEvaluationEngine.ParseHttpResponse(
                "HTTP/1.1 200 OK\r\n" +
                "Strict-Transport-Security: max-age=31536000\r\n" +
                "Content-Security-Policy: default-src 'self'\r\n" +
                "X-Frame-Options: DENY\r\n" +
                "X-Content-Type-Options: nosniff\r\n" +
                "Referrer-Policy: strict-origin-when-cross-origin\r\n" +
                "Permissions-Policy: geolocation=()\r\n" +
                "Set-Cookie: sid=1; Secure; HttpOnly; SameSite=Lax\r\n\r\n")
        };
        var localizer = await CreateLocalizerAsync("en");
        var vm = new HttpHeaderAnalyzerViewModel(service)
        {
            UrlInput = "https://example.com"
        };
        vm.Initialize(localizer);

        vm.CheckCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.True(vm.HasResults);
        Assert.False(vm.ShowError);
        Assert.NotEmpty(vm.SecurityHeaders);
        Assert.NotEmpty(vm.DisclosureHeaders);
        Assert.Equal("A+", vm.GradeText);
        Assert.Contains("example.com", vm.ReportText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckCommand_ServiceThrows_SetsError()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = new HttpHeaderAnalyzerViewModel(new FakeHttpHeaderService
        {
            ExceptionToThrow = new InvalidOperationException("boom"),
        })
        {
            UrlInput = "https://example.com",
        };
        vm.Initialize(localizer);

        vm.CheckCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.True(vm.ShowError);
        Assert.Contains("boom", vm.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelCommand_CancelsInflightRequest()
    {
        var service = new BlockingHttpHeaderService();
        var vm = new HttpHeaderAnalyzerViewModel(service)
        {
            UrlInput = "https://example.com",
        };

        vm.CheckCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsBusy);
        vm.CancelCommand.Execute(null);
        service.Release();
        await WaitUntilAsync(() => !vm.IsBusy);

        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void SetGateway_DelegatesToService()
    {
        var service = new FakeHttpHeaderService();
        var gateway = new SshGatewayDto { Name = "gw", Host = "jump", Port = 22 };
        var vm = new HttpHeaderAnalyzerViewModel(service);

        vm.SetGateway(gateway);

        Assert.Same(gateway, service.LastGateway);
    }

    [Fact]
    public async Task UpdateLocalizer_ReprojectsDisplayStrings()
    {
        var service = new FakeHttpHeaderService
        {
            Response = HttpHeaderEvaluationEngine.ParseHttpResponse(
                "HTTP/1.1 200 OK\r\n" +
                "X-Powered-By: ASP.NET\r\n\r\n")
        };

        var en = new LocalizationManager();
        await en.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "en");
        var fr = new LocalizationManager();
        await fr.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), "fr");

        var vm = new HttpHeaderAnalyzerViewModel(service)
        {
            UrlInput = "https://example.com",
        };
        vm.Initialize(en);

        vm.CheckCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsBusy);
        Assert.Equal("Not present", vm.DisclosureHeaders[0].DisplayValue);

        vm.UpdateLocalizer(fr);

        Assert.Equal("Non présent", vm.DisclosureHeaders[0].DisplayValue);
    }

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    [Fact]
    public async Task CheckCommand_TogglesIsBusy()
    {
        var service = new BlockingHttpHeaderService();
        var vm = new HttpHeaderAnalyzerViewModel(service)
        {
            UrlInput = "https://example.com",
        };

        vm.CheckCommand.Execute(null);
        await WaitUntilAsync(() => vm.IsBusy);
        Assert.True(vm.IsBusy);

        service.Release();
        await WaitUntilAsync(() => !vm.IsBusy);
        Assert.False(vm.IsBusy);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 2000)
    {
        var timeoutAt = Environment.TickCount64 + timeoutMs;
        while (!predicate())
        {
            if (Environment.TickCount64 > timeoutAt)
            {
                throw new TimeoutException("Condition was not met before timeout.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FakeHttpHeaderService : IHttpHeaderService
    {
        public HttpResponseInfo Response { get; set; } = new(200, new Dictionary<string, string>(), string.Empty);
        public Exception? ExceptionToThrow { get; set; }
        public SshGatewayDto? LastGateway { get; private set; }

        public Task<HttpResponseInfo> FetchAsync(Uri uri, CancellationToken ct)
        {
            if (ExceptionToThrow is not null)
            {
                return Task.FromException<HttpResponseInfo>(ExceptionToThrow);
            }

            return Task.FromResult(Response);
        }

        public void SetGateway(SshGatewayDto? gateway)
        {
            LastGateway = gateway;
        }
    }

    private sealed class BlockingHttpHeaderService : IHttpHeaderService
    {
        private readonly TaskCompletionSource<object?> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<HttpResponseInfo> FetchAsync(Uri uri, CancellationToken ct)
        {
            using var registration = ct.Register(() => _gate.TrySetCanceled(ct));
            await _gate.Task;
            return new HttpResponseInfo(200, new Dictionary<string, string>(), "HTTP/1.1 200 OK");
        }

        public void SetGateway(SshGatewayDto? gateway)
        {
        }

        public void Release()
        {
            _gate.TrySetResult(null);
        }
    }
}
