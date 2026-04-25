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

using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Heimdall.App.UiTests.Infrastructure;
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.App.ViewModels.Settings;
using Heimdall.App.Views.Dialogs;
using Heimdall.Core.Ssh;

namespace Heimdall.App.UiTests.Dialogs;

[Collection(DesktopUiCollection.Name)]
public sealed class TrustedHostKeyDetailsDialogSmokeTests
{
    [StaFact]
    [Trait("Category", "RequiresDesktop")]
    public void Dialog_LoadsReadOnlyFieldsWithoutBindingFailures()
    {
        WpfTestHost.ResetLocale();

        WpfTestHost.Invoke(() =>
        {
            TrustedHostKeyDetailsDialog? dialog = null;

            try
            {
                var entry = new HostKeyEntry(
                    "SHA256:jJ6rMm0o4x5r8g1m3WQ8m9r7X3k4fYwW9nP5sQm1KxA",
                    new DateTimeOffset(2026, 4, 24, 22, 10, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 4, 25, 8, 30, 0, TimeSpan.Zero),
                    "ssh-ed25519",
                    HostKeySource.UserConfirmed)
                {
                    PublicKeyBase64 = "AAAAC3NzaC1lZDI1NTE5AAAAIEhpbWRhbGxUZXN0S2V5"
                };

                var row = CreateRow(entry);
                var viewModel = new TrustedHostKeyDetailsDialogViewModel(row, WpfTestHost.Localizer);

                dialog = new TrustedHostKeyDetailsDialog
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 120,
                    Top = 120,
                    DataContext = viewModel
                };

                dialog.Show();
                dialog.UpdateLayout();
                dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.True(dialog.IsLoaded);
                Assert.Equal(entry.Fingerprint, viewModel.Fingerprint);
                Assert.Equal(entry.PublicKeyBase64, viewModel.PublicKeyBase64);
            }
            finally
            {
                dialog?.Close();
            }
        });
    }

    private static TrustedHostKeyRowViewModel CreateRow(HostKeyEntry entry)
    {
        var constructor = typeof(TrustedHostKeyRowViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(string),
                typeof(string),
                typeof(int),
                typeof(HostKeyEntry),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(string)
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        return (TrustedHostKeyRowViewModel)constructor.Invoke(
        [
            "localhost:2222",
            "localhost",
            2222,
            entry,
            "User confirmed",
            "24/04/2026 22:10",
            "25/04/2026 08:30",
            "(not available)"
        ]);
    }
}
