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

using System.Text.RegularExpressions;
using Heimdall.Rdp;

namespace Heimdall.Rdp.Tests;

public sealed class CredentialAutofillTests
{
    [Theory]
    [InlineData("Windows Security")]
    [InlineData("S\u00e9curit\u00e9 de Windows")]
    [InlineData("Securite Windows")]
    [InlineData("Windows-Sicherheit")]
    [InlineData("Seguridad de Windows")]
    [InlineData("Sicurezza di Windows")]
    [InlineData("Seguran\u00e7a do Windows")]
    [InlineData("Windows-beveiliging")]
    [InlineData("Zabezpieczenia systemu Windows")]
    [InlineData("Credential")]
    [InlineData("Credenziale")]
    [InlineData("Credencial")]
    [InlineData("Anmeldeinformation")]
    [InlineData("mstsc.exe")]
    public void TitlePattern_MatchesKnownCredentialDialogTitles(string title)
    {
        Assert.Matches(CredentialAutofill.TitlePattern, title);
    }

    [Theory]
    [InlineData("Notepad")]
    [InlineData("File Explorer")]
    [InlineData("")]
    public void TitlePattern_DoesNotMatchUnrelatedTitles(string title)
    {
        Assert.DoesNotMatch(CredentialAutofill.TitlePattern, title);
    }

    [Fact]
    public void SelectCredentialDialogTarget_ReturnsNull_ForUnmatchedBrokerWindows()
    {
        var windows = new List<CredentialAutofill.WindowInfo>
        {
            new(new IntPtr(0x1001), "Windows Security", "Credential Dialog Xaml Host", 2001, "CredentialUIBroker"),
            new(new IntPtr(0x1002), "Windows Security", "Credential Dialog Xaml Host", 2002, "CredentialUIBroker")
        };
        var hostHintPattern = new Regex("server01\\.corp\\.local", RegexOptions.IgnoreCase);

        var result = CredentialAutofill.SelectCredentialDialogTarget(
            mstscProcessId: 9999,
            hostHintPattern,
            windows,
            scan: 1);

        Assert.Null(result);
    }
}
