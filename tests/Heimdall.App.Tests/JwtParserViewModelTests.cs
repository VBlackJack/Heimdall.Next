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
using System.Security.Cryptography;
using System.Text;
using Heimdall.App.Services;
using Heimdall.App.ViewModels.Tools;
using Heimdall.Core.Jwt;
using Heimdall.Core.Localization;

namespace Heimdall.App.Tests;

public sealed class JwtParserViewModelTests
{
    [Fact]
    public void PrefillInput_NonEmpty_SetsInput()
    {
        var vm = CreateViewModel();

        vm.PrefillInput("token");

        Assert.Equal("token", vm.InputText);
    }

    [Fact]
    public void Parse_EmptyInput_ShowsEmptyState()
    {
        var vm = CreateViewModel();
        vm.InputText = " ";

        vm.ParseCommand.Execute(null);

        Assert.True(vm.IsEmptyStateVisible);
        Assert.False(vm.IsErrorVisible);
        Assert.Equal(string.Empty, vm.HeaderText);
    }

    [Fact]
    public void Parse_InvalidFormat_ShowsLocalizedError()
    {
        var vm = CreateViewModel();
        vm.InputText = "abc";

        vm.ParseCommand.Execute(null);

        Assert.True(vm.IsErrorVisible);
        Assert.Equal("ToolJwtErrorInvalidFormat", vm.ErrorText);
        Assert.False(vm.IsVerifySectionVisible);
    }

    [Fact]
    public void Parse_DecodeFailure_ShowsLocalizedError()
    {
        var vm = CreateViewModel();
        vm.InputText = "%%%%.%%%%.%%%%";

        vm.ParseCommand.Execute(null);

        Assert.True(vm.IsErrorVisible);
        Assert.Equal("ToolJwtErrorDecodeFailed", vm.ErrorText);
    }

    [Fact]
    public void Parse_ValidHmacJwt_PopulatesOutputsAndVerifySection()
    {
        var vm = CreateViewModel();
        vm.InputText = CreateJwt("HS256", $$"""{"sub":"john","exp":{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}""", "secret");

        vm.ParseCommand.Execute(null);

        Assert.False(vm.IsErrorVisible);
        Assert.False(vm.IsEmptyStateVisible);
        Assert.NotEmpty(vm.HeaderText);
        Assert.NotEmpty(vm.PayloadText);
        Assert.NotEmpty(vm.SignatureHexText);
        Assert.True(vm.IsExpirationVisible);
        Assert.True(vm.IsVerifySectionVisible);
        Assert.True(vm.IsHmacVerifyVisible);
        Assert.False(vm.IsUnsupportedAlgVisible);
    }

    [Fact]
    public void Parse_NoExpiry_ShowsNoExpiryState()
    {
        var vm = CreateViewModel();
        vm.InputText = CreateJwt("HS256", """{"sub":"john"}""", "secret");

        vm.ParseCommand.Execute(null);

        Assert.True(vm.IsExpirationVisible);
        Assert.Equal(JwtExpirationStatus.NoExpiry, vm.ExpirationStatus);
        Assert.Equal("ToolJwtNoExpiry", vm.ExpirationText);
    }

    [Fact]
    public void Parse_InvalidExp_HidesExpiration()
    {
        var vm = CreateViewModel();
        vm.InputText = CreateJwt("HS256", """{"sub":"john","exp":"tomorrow"}""", "secret");

        vm.ParseCommand.Execute(null);

        Assert.False(vm.IsExpirationVisible);
        Assert.Equal(string.Empty, vm.ExpirationText);
    }

    [Fact]
    public void Parse_UnsupportedAsymmetricAlg_ShowsUnsupportedSection()
    {
        var vm = CreateViewModel();
        vm.InputText = $"{Encode(Encoding.UTF8.GetBytes("""{"alg":"RS256"}"""))}.{Encode(Encoding.UTF8.GetBytes("""{"sub":"john"}"""))}.";

        vm.ParseCommand.Execute(null);

        Assert.True(vm.IsVerifySectionVisible);
        Assert.False(vm.IsHmacVerifyVisible);
        Assert.True(vm.IsUnsupportedAlgVisible);
    }

    [Fact]
    public void Parse_UnknownAlg_HidesVerifySection()
    {
        var vm = CreateViewModel();
        vm.InputText = $"{Encode(Encoding.UTF8.GetBytes("""{"alg":"XYZ"}"""))}.{Encode(Encoding.UTF8.GetBytes("""{"sub":"john"}"""))}.";

        vm.ParseCommand.Execute(null);

        Assert.False(vm.IsVerifySectionVisible);
    }

    [Fact]
    public void Verify_ValidSecret_ShowsSuccess()
    {
        var vm = CreateViewModel();
        vm.InputText = CreateJwt("HS256", """{"sub":"john"}""", "secret");
        vm.ParseCommand.Execute(null);
        vm.SecretText = "secret";

        vm.VerifyCommand.Execute(null);

        Assert.True(vm.IsVerifyResultVisible);
        Assert.True(vm.IsVerifyResultValid);
        Assert.Contains("ToolJwtSignatureValid", vm.VerifyResultText, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_InvalidSecret_ShowsFailure()
    {
        var vm = CreateViewModel();
        vm.InputText = CreateJwt("HS256", """{"sub":"john"}""", "secret");
        vm.ParseCommand.Execute(null);
        vm.SecretText = "bad";

        vm.VerifyCommand.Execute(null);

        Assert.True(vm.IsVerifyResultVisible);
        Assert.False(vm.IsVerifyResultValid);
        Assert.Contains("ToolJwtSignatureInvalid", vm.VerifyResultText, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_WithoutSecret_CannotExecute()
    {
        var vm = CreateViewModel();
        vm.InputText = CreateJwt("HS256", """{"sub":"john"}""", "secret");
        vm.ParseCommand.Execute(null);

        Assert.False(vm.VerifyCommand.CanExecute(null));
    }

    [Fact]
    public void Verify_WithoutDecodedToken_CannotExecute()
    {
        var vm = CreateViewModel();
        vm.SecretText = "secret";

        Assert.False(vm.VerifyCommand.CanExecute(null));
    }

    [Fact]
    public void Parse_AfterVerification_ResetsVerifyResult()
    {
        var vm = CreateViewModel();
        vm.InputText = CreateJwt("HS256", """{"sub":"john"}""", "secret");
        vm.ParseCommand.Execute(null);
        vm.SecretText = "secret";
        vm.VerifyCommand.Execute(null);

        vm.ParseCommand.Execute(null);

        Assert.False(vm.IsVerifyResultVisible);
        Assert.Equal(string.Empty, vm.VerifyResultText);
    }

    [Fact]
    public async Task LocaleChanged_ReparsesAndRelocalizesExpirationAndVerifyResult()
    {
        var localizer = await CreateLocalizerAsync("en");
        var vm = CreateViewModel();
        vm.Initialize(localizer);
        vm.InputText = CreateJwt("HS256", $$"""{"sub":"john","exp":{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}""", "secret");
        vm.ParseCommand.Execute(null);
        vm.SecretText = "secret";
        vm.VerifyCommand.Execute(null);
        var englishExpiration = vm.ExpirationText;
        var englishVerify = vm.VerifyResultText;

        await localizer.SwitchLocaleAsync("fr");

        Assert.NotEqual(englishExpiration, vm.ExpirationText);
        Assert.NotEqual(englishVerify, vm.VerifyResultText);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var vm = CreateViewModel();

        vm.Dispose();
        vm.Dispose();
    }

    private static JwtParserViewModel CreateViewModel() => new(new JwtParserToolService());

    private static async Task<LocalizationManager> CreateLocalizerAsync(string locale)
    {
        var manager = new LocalizationManager();
        await manager.LoadAsync(Path.Combine(AppContext.BaseDirectory, "locales"), locale);
        return manager;
    }

    private static string CreateJwt(string alg, string payloadJson, string secret)
    {
        var headerJson = $$"""{"alg":"{{alg}}","typ":"JWT"}""";
        var headerRaw = Encode(Encoding.UTF8.GetBytes(headerJson));
        var payloadRaw = Encode(Encoding.UTF8.GetBytes(payloadJson));
        byte[] signatureBytes = alg switch
        {
            "HS256" => new HMACSHA256(Encoding.UTF8.GetBytes(secret)).ComputeHash(Encoding.UTF8.GetBytes($"{headerRaw}.{payloadRaw}")),
            "HS384" => new HMACSHA384(Encoding.UTF8.GetBytes(secret)).ComputeHash(Encoding.UTF8.GetBytes($"{headerRaw}.{payloadRaw}")),
            "HS512" => new HMACSHA512(Encoding.UTF8.GetBytes(secret)).ComputeHash(Encoding.UTF8.GetBytes($"{headerRaw}.{payloadRaw}")),
            _ => []
        };
        return $"{headerRaw}.{payloadRaw}.{Encode(signatureBytes)}";
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
