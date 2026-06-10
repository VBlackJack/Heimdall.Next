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
using System.Xml.Linq;

namespace Heimdall.App.Tests;

/// <summary>
/// Guards the keyboard contract of modal dialogs: every dialog window that
/// defines a default (Enter) button must also define a cancel (Escape) button,
/// and neither role may be assigned to more than one button per dialog.
/// A single button carrying both IsDefault and IsCancel (safe-by-default
/// pattern, see PasteConfirmDialog) is legal.
/// </summary>
public sealed class DialogKeyboardContractTests
{
    private const string DialogsRelativePath = @"src\Heimdall.App\Views\Dialogs";

    /// <summary>
    /// Dialogs whose Escape handling is assigned dynamically in code-behind
    /// instead of statically in XAML. A file listed here is exempt from the
    /// "IsDefault requires IsCancel" rule. Keep this list minimal: every entry
    /// must name the code-behind member that performs the dynamic assignment.
    /// Currently empty — MessageDialog.xaml keeps a static IsCancel on
    /// BtnTertiary (used by ShowThreeWay) and therefore passes the static scan;
    /// its per-factory reassignment lives in MessageDialog.xaml.cs.
    /// </summary>
    private static readonly IReadOnlySet<string> DynamicCancelAllowlist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static TheoryData<string> DialogXamlFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in EnumerateDialogWindowXamlFiles())
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DialogXamlFiles))]
    public void DialogWindow_HasAtMostOneDefaultAndOneCancelButton(string fileName)
    {
        var document = LoadDialogXaml(fileName);

        int defaultCount = CountButtonsWithAttribute(document, "IsDefault");
        int cancelCount = CountButtonsWithAttribute(document, "IsCancel");

        Assert.True(defaultCount <= 1,
            $"{fileName}: {defaultCount} buttons declare IsDefault=\"True\" — at most one is allowed.");
        Assert.True(cancelCount <= 1,
            $"{fileName}: {cancelCount} buttons declare IsCancel=\"True\" — at most one is allowed.");
    }

    [Theory]
    [MemberData(nameof(DialogXamlFiles))]
    public void DialogWindow_WithDefaultButton_AlsoDefinesCancelButton(string fileName)
    {
        if (DynamicCancelAllowlist.Contains(fileName))
        {
            return;
        }

        var document = LoadDialogXaml(fileName);

        int defaultCount = CountButtonsWithAttribute(document, "IsDefault");
        if (defaultCount == 0)
        {
            return;
        }

        int cancelCount = CountButtonsWithAttribute(document, "IsCancel");

        Assert.True(cancelCount >= 1,
            $"{fileName}: declares IsDefault=\"True\" (Enter shortcut) but no IsCancel=\"True\" — "
            + "Escape would be inert. Add IsCancel to the cancel-role button, or register the "
            + "file in DynamicCancelAllowlist if Escape is wired in code-behind.");
    }

    private static XDocument LoadDialogXaml(string fileName)
    {
        string path = Path.Combine(FindRepoRoot(), DialogsRelativePath, fileName);
        Assert.True(File.Exists(path), $"Dialog XAML not found: {path}");
        return XDocument.Load(path);
    }

    private static int CountButtonsWithAttribute(XDocument document, string attributeName)
    {
        return document
            .Descendants()
            .Count(element =>
                element.Name.LocalName == "Button"
                && string.Equals(
                    element.Attribute(attributeName)?.Value,
                    "True",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateDialogWindowXamlFiles()
    {
        string dialogsDir = Path.Combine(FindRepoRoot(), DialogsRelativePath);
        if (!Directory.Exists(dialogsDir))
        {
            throw new DirectoryNotFoundException($"Dialogs directory not found: {dialogsDir}");
        }

        foreach (var file in Directory.EnumerateFiles(dialogsDir, "*.xaml", SearchOption.TopDirectoryOnly))
        {
            var root = XDocument.Load(file).Root;
            if (root is not null && root.Name.LocalName == "Window")
            {
                yield return file;
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Heimdall.slnx")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException(
            $"Cannot find repository root containing Heimdall.slnx from test binary directory: {AppContext.BaseDirectory}");
    }
}
