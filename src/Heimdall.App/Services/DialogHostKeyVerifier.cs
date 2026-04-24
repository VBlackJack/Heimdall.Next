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
using Heimdall.App.ViewModels.Dialogs;
using Heimdall.App.Views.Dialogs;
using Heimdall.Core.Localization;
using Heimdall.Core.Logging;
using Heimdall.Core.Ssh;

namespace Heimdall.App.Services;

/// <summary>
/// WPF host key verifier that marshals onto the UI dispatcher and shows a modal dialog.
/// </summary>
internal sealed class DialogHostKeyVerifier(LocalizationManager localizer) : IHostKeyVerifier
{
    private readonly LocalizationManager _localizer = localizer;

    public async Task<HostKeyDecision> VerifyAsync(
        string host,
        int port,
        string algorithm,
        string presentedFingerprint,
        string? storedFingerprint,
        CancellationToken ct = default)
    {
        var app = Application.Current;
        if (app is null)
        {
            FileLogger.Warn(
                $"DialogHostKeyVerifier invoked without Application.Current for {host}:{port}; rejecting host key.");
            return HostKeyDecision.Reject;
        }

        if (ct.IsCancellationRequested)
        {
            return HostKeyDecision.Reject;
        }

        try
        {
            return await app.Dispatcher.InvokeAsync(
                () => ShowDialog(
                    app,
                    host,
                    port,
                    algorithm,
                    presentedFingerprint,
                    storedFingerprint,
                    ct),
                System.Windows.Threading.DispatcherPriority.Normal,
                ct);
        }
        catch (TaskCanceledException)
        {
            return HostKeyDecision.Reject;
        }
    }

    private HostKeyDecision ShowDialog(
        Application app,
        string host,
        int port,
        string algorithm,
        string presentedFingerprint,
        string? storedFingerprint,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return HostKeyDecision.Reject;
        }

        var viewModel = new HostKeyPromptDialogViewModel(
            _localizer,
            host,
            port,
            algorithm,
            presentedFingerprint,
            storedFingerprint);
        var dialog = new HostKeyPromptDialog(_localizer)
        {
            DataContext = viewModel
        };

        if (app.MainWindow is Window { IsLoaded: true } owner)
        {
            dialog.Owner = owner;
        }

        using var registration = ct.CanBeCanceled
            ? ct.Register(() =>
            {
                if (dialog.Dispatcher.HasShutdownStarted)
                {
                    return;
                }

                _ = dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (dialog.IsVisible)
                    {
                        dialog.Close();
                    }
                });
            })
            : default;

        _ = dialog.ShowDialog();
        return viewModel.Decision ?? HostKeyDecision.Reject;
    }
}
