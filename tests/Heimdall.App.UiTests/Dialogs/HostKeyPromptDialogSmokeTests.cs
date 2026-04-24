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

using System.Windows;
using Heimdall.App.UiTests.Infrastructure;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.App.Views.Dialogs;

namespace Heimdall.App.UiTests.Dialogs;

[Collection(DesktopUiCollection.Name)]
public sealed class HostKeyPromptDialogSmokeTests
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void FirstUseDialog_LoadsWithoutBindingFailures()
    {
        OpenAndCloseDialog(
            presentedFingerprint: "SHA256:jJ6rMm0o4x5r8g1m3WQ8m9r7X3k4fYwW9nP5sQm1KxA",
            storedFingerprint: null);
    }

    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void MismatchDialog_LoadsWithoutBindingFailures()
    {
        OpenAndCloseDialog(
            presentedFingerprint: "SHA256:8b4A2QkQnM0y2z6v4d9kB7uE1mP3tR5yH2cN6wX9pLs",
            storedFingerprint: "SHA256:jJ6rMm0o4x5r8g1m3WQ8m9r7X3k4fYwW9nP5sQm1KxA");
    }

    private static void OpenAndCloseDialog(string presentedFingerprint, string? storedFingerprint)
    {
        WpfTestHost.ResetLocale();

        WpfTestHost.Invoke(() =>
        {
            HostKeyPromptDialog? dialog = null;

            try
            {
                dialog = new HostKeyPromptDialog(WpfTestHost.Localizer)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 120,
                    Top = 120,
                    DataContext = new HostKeyPromptDialogViewModel(
                        WpfTestHost.Localizer,
                        "secure.example.com",
                        22,
                        "ssh-ed25519",
                        presentedFingerprint,
                        storedFingerprint)
                };

                dialog.Show();
                dialog.UpdateLayout();

                var viewModel = Assert.IsType<HostKeyPromptDialogViewModel>(dialog.DataContext);
                Assert.True(dialog.IsLoaded);
                Assert.False(string.IsNullOrWhiteSpace(dialog.Title));
                Assert.Equal(storedFingerprint is not null, viewModel.IsMismatch);
                Assert.Equal(presentedFingerprint, viewModel.PresentedFingerprint);
                Assert.Equal(storedFingerprint, viewModel.StoredFingerprint);
            }
            finally
            {
                dialog?.Close();
            }
        });
    }
}
