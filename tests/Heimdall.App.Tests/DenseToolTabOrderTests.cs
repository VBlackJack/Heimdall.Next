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
using System.Text.RegularExpressions;

namespace Heimdall.App.Tests;

/// <summary>
/// Guards explicit tab navigation on the densest tool layouts.
/// </summary>
public class DenseToolTabOrderTests
{
    [Fact]
    public void DenseToolViews_KeepExplicitTabOrderOnKeyControls()
    {
        var toolsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Heimdall.App", "Views", "Tools"));

        var expectedControls = new Dictionary<string, string[]>
        {
            ["PortScannerView.xaml"] =
            [
                "TxtHost", "TxtPorts", "CmbRouteVia", "BtnScan", "ResultsGrid", "BtnCopy", "BtnExportCsv", "BtnHelp"
            ],
            ["NetworkCartographyView.xaml"] =
            [
                "TxtSubnet", "CmbDepth", "BtnStart", "BtnStop", "CmbRouteVia", "ResultsGrid", "CmbHistory", "BtnExportCsv", "BtnHelp"
            ],
            ["NotesToolView.xaml"] =
            [
                "BtnToggleSidebar", "SearchTextBox", "NotesTreeView", "BtnNavBack", "BtnNoteActions", "MilkdownEditor", "Editor", "BtnOpenFolder", "BtnHelp"
            ],
            ["LogViewerView.xaml"] =
            [
                "FilePathInput", "BtnBrowse", "BtnTail", "FilterInput", "EncodingCombo", "LogViewer", "BtnCopyLog", "BtnHelp"
            ],
        };

        var failures = new List<string>();

        foreach (var (fileName, controlNames) in expectedControls)
        {
            var xamlPath = Path.Combine(toolsDir, fileName);
            var content = File.ReadAllText(xamlPath);

            foreach (var controlName in controlNames)
            {
                var pattern = $@"<[\w:]+[^>]*\bx:Name=""{Regex.Escape(controlName)}""[^>]*\bTabIndex=""-?\d+""";
                if (!Regex.IsMatch(content, pattern, RegexOptions.CultureInvariant))
                {
                    failures.Add($"{fileName} -> {controlName}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Missing explicit TabIndex on dense tool controls:\n" + string.Join("\n", failures));
    }
}
