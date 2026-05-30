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

using System.Runtime.Versioning;
using Heimdall.Core.Security;

namespace Heimdall.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class PrivilegeLauncherTests
{
    [Fact]
    public void EncodeDecodeLaunchPayload_RoundTripsArgumentsWithSpacesQuotesAndDashes()
    {
        string[] originalArgs =
        [
            "arg with spaces",
            "he said \"hello\"",
            "arg ",
            "--key=value",
            "-Force"
        ];

        string encoded = PrivilegeLauncher.EncodeLaunchPayload("notepad.exe", originalArgs);
        PrivilegeLauncher.PrivilegeLaunchPayload decoded = PrivilegeLauncher.DecodeLaunchPayload(encoded);

        Assert.Equal("notepad.exe", decoded.Exe);
        Assert.Equal(originalArgs, decoded.Args);
    }

    [Fact]
    public void EncodeDecodeLaunchPayload_RoundTripsEmptyArgsArray()
    {
        string encoded = PrivilegeLauncher.EncodeLaunchPayload("notepad.exe", []);
        PrivilegeLauncher.PrivilegeLaunchPayload decoded = PrivilegeLauncher.DecodeLaunchPayload(encoded);

        Assert.Equal("notepad.exe", decoded.Exe);
        Assert.Empty(decoded.Args);
    }

    [Fact]
    public void EncodeDecodeLaunchPayload_RoundTripsSingleArgument()
    {
        string encoded = PrivilegeLauncher.EncodeLaunchPayload("notepad.exe", ["single"]);
        PrivilegeLauncher.PrivilegeLaunchPayload decoded = PrivilegeLauncher.DecodeLaunchPayload(encoded);

        Assert.Equal("notepad.exe", decoded.Exe);
        Assert.Equal(["single"], decoded.Args);
    }

    [Fact]
    public void ParseArguments_PreservesQuotedArguments()
    {
        string[] parsed = PrivilegeLauncher.ParseArguments(
            "\"arg with spaces\" \"he said \\\"hello\\\"\" \"arg \" --key=value -Force");

        Assert.Equal(
            ["arg with spaces", "he said \"hello\"", "arg ", "--key=value", "-Force"],
            parsed);
    }

    [Fact]
    public void TryValidateLaunchPayload_RejectsNullExe()
    {
        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload(null!, []),
            out string errorMessage);

        Assert.False(isValid);
        Assert.Contains("Executable path is required", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateLaunchPayload_RejectsEmptyExe()
    {
        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload("", []),
            out string errorMessage);

        Assert.False(isValid);
        Assert.Contains("Executable path is required", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateLaunchPayload_RejectsExeContainingNullByte()
    {
        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload("bad\0exe.exe", []),
            out string errorMessage);

        Assert.False(isValid);
        Assert.Contains("null byte", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateLaunchPayload_RejectsMissingFullyQualifiedExe()
    {
        string missingExePath = Path.Combine(Path.GetTempPath(), $"heimdall-missing-{Guid.NewGuid():N}.exe");
        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload(missingExePath, []),
            out string errorMessage);

        Assert.False(isValid);
        Assert.Contains("File not found", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateLaunchPayload_AcceptsBareExeName()
    {
        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload("notepad.exe", ["arg with spaces"]),
            out string errorMessage);

        Assert.True(isValid);
        Assert.Equal(string.Empty, errorMessage);
    }

    [Fact]
    public void TryValidateLaunchPayload_System_RejectsBareExeName()
    {
        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload("notepad.exe", []),
            out string errorMessage,
            PrivilegeLevel.System);

        Assert.False(isValid);
        Assert.Contains("fully-qualified", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateLaunchPayload_TrustedInstaller_RejectsBareExeName()
    {
        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload("notepad.exe", []),
            out string errorMessage,
            PrivilegeLevel.TrustedInstaller);

        Assert.False(isValid);
        Assert.Contains("fully-qualified", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateLaunchPayload_System_AcceptsExistingFullyQualifiedExe()
    {
        string? comspec = Environment.GetEnvironmentVariable("COMSPEC");
        Assert.False(string.IsNullOrWhiteSpace(comspec));

        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload(comspec!, []),
            out string errorMessage,
            PrivilegeLevel.System);

        Assert.True(isValid);
        Assert.Equal(string.Empty, errorMessage);
    }

    [Fact]
    public void TryValidateLaunchPayload_System_RejectsMissingFullyQualifiedExe()
    {
        string missingExePath = Path.Combine(Path.GetTempPath(), $"heimdall-missing-{Guid.NewGuid():N}.exe");
        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload(missingExePath, []),
            out string errorMessage,
            PrivilegeLevel.System);

        Assert.False(isValid);
        Assert.Contains("File not found", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateLaunchPayload_CurrentUserElevated_StillAcceptsBareExeName()
    {
        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload("notepad.exe", []),
            out string errorMessage,
            PrivilegeLevel.CurrentUserElevated);

        Assert.True(isValid);
        Assert.Equal(string.Empty, errorMessage);
    }

    [Fact]
    public void TryValidateLaunchPayload_AcceptsExistingFullyQualifiedExe()
    {
        string? comspec = Environment.GetEnvironmentVariable("COMSPEC");
        Assert.False(string.IsNullOrWhiteSpace(comspec));

        bool isValid = PrivilegeLauncher.TryValidateLaunchPayload(
            new PrivilegeLauncher.PrivilegeLaunchPayload(comspec!, []),
            out string errorMessage);

        Assert.True(isValid);
        Assert.Equal(string.Empty, errorMessage);
    }
}
