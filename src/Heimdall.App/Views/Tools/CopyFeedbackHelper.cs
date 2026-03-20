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

using System.Windows.Controls;
using System.Windows.Threading;

namespace Heimdall.App.Views.Tools;

/// <summary>
/// Provides visual feedback when content is copied to the clipboard.
/// Temporarily replaces a button's content with a checkmark for one second.
/// </summary>
internal static class CopyFeedbackHelper
{
    private static readonly TimeSpan FeedbackDuration = TimeSpan.FromSeconds(1);
    private const string CheckMark = "\u2713";

    /// <summary>
    /// Briefly replaces the button content with a checkmark, then restores the original content.
    /// </summary>
    public static void ShowCopyFeedback(Button? btn)
    {
        if (btn is null) return;

        var original = btn.Content;
        btn.Content = CheckMark;
        btn.IsEnabled = false;

        var timer = new DispatcherTimer { Interval = FeedbackDuration };
        timer.Tick += (_, _) =>
        {
            btn.Content = original;
            btn.IsEnabled = true;
            timer.Stop();
        };
        timer.Start();
    }
}
