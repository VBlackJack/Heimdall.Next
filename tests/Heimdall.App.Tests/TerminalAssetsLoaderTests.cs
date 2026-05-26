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

namespace Heimdall.App.Tests;

public sealed class TerminalAssetsLoaderTests
{
    [Fact]
    public void AssetProperties_Return_NonEmpty_Content()
    {
        Assert.False(string.IsNullOrWhiteSpace(TerminalAssetsLoader.TerminalHtml));
        Assert.False(string.IsNullOrWhiteSpace(TerminalAssetsLoader.XtermCss));
        Assert.False(string.IsNullOrWhiteSpace(TerminalAssetsLoader.XtermJs));
        Assert.False(string.IsNullOrWhiteSpace(TerminalAssetsLoader.AddonFitJs));
        Assert.False(string.IsNullOrWhiteSpace(TerminalAssetsLoader.AddonWebglJs));
    }

    [Fact]
    public void AssetProperties_Return_Same_Reference_On_Subsequent_Access()
    {
        Assert.Same(TerminalAssetsLoader.TerminalHtml, TerminalAssetsLoader.TerminalHtml);
        Assert.Same(TerminalAssetsLoader.XtermCss, TerminalAssetsLoader.XtermCss);
        Assert.Same(TerminalAssetsLoader.XtermJs, TerminalAssetsLoader.XtermJs);
        Assert.Same(TerminalAssetsLoader.AddonFitJs, TerminalAssetsLoader.AddonFitJs);
        Assert.Same(TerminalAssetsLoader.AddonWebglJs, TerminalAssetsLoader.AddonWebglJs);
    }

    [Fact]
    public async Task CreateLazyAsset_Concurrent_First_Access_Returns_Consistent_Instance()
    {
        Lazy<string> lazyAsset = TerminalAssetsLoader.CreateLazyAsset(Path.Combine("Terminal", "xterm.min.js"));

        string[] results = await Task.WhenAll(
            Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => lazyAsset.Value)));

        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.All(results, result => Assert.Same(results[0], result));
    }

    [Fact]
    public void CreateLazyAsset_RootedPath_ThrowsArgumentException()
    {
        string rootedPath = Path.GetFullPath("terminal.html");

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => TerminalAssetsLoader.CreateLazyAsset(rootedPath));

        Assert.Equal("relativePath", exception.ParamName);
    }

    [Fact]
    public void CreateLazyAsset_ParentTraversal_ThrowsArgumentException()
    {
        string traversalPath = Path.Combine("Terminal", "..", "terminal.html");

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => TerminalAssetsLoader.CreateLazyAsset(traversalPath));

        Assert.Equal("relativePath", exception.ParamName);
    }
}
