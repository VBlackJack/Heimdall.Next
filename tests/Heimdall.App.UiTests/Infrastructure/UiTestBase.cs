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
using System.Windows.Interop;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Heimdall.Core.Models;
using WpfWindow = System.Windows.Window;

namespace Heimdall.App.UiTests.Infrastructure;

[CollectionDefinition(DesktopUiCollection.Name, DisableParallelization = true)]
public sealed class DesktopUiCollection : ICollectionFixture<DesktopUiFixture>
{
    public const string Name = "DesktopUiCollection";
}

/// <summary>
/// Base class for hosted-WPF UI smoke tests.
/// </summary>
public abstract class UiTestBase<TControl> where TControl : FrameworkElement, IToolView, new()
{
    protected HostedToolWindow<TControl> OpenTool(ToolContext? context = null)
    {
        WpfTestHost.ResetLocale();
        return HostedToolWindow<TControl>.Create(context);
    }

    protected static string ReadText(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.Patterns.Value.IsSupported)
        {
            return element.Patterns.Value.Pattern.Value.Value ?? string.Empty;
        }

        if (element.Patterns.Text.IsSupported)
        {
            return element.Patterns.Text.Pattern.DocumentRange.GetText(int.MaxValue).Trim('\0', '\r', '\n');
        }

        return element.Name ?? string.Empty;
    }

    protected static string ReadClipboardText()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return WpfTestHost.Invoke(System.Windows.Clipboard.GetText);
            }
            catch
            {
                Thread.Sleep(50);
            }
        }

        throw new InvalidOperationException("clipboard unavailable in test host");
    }
}

public sealed class HostedToolWindow<TControl> : IDisposable where TControl : FrameworkElement, IToolView, new()
{
    private readonly WpfWindow _hostWindow;
    private readonly UIA3Automation _automation;
    private readonly TControl _control;
    private bool _disposed;

    private HostedToolWindow(WpfWindow hostWindow, TControl control, UIA3Automation automation, AutomationElement root)
    {
        _hostWindow = hostWindow;
        _control = control;
        _automation = automation;
        Root = root;
    }

    public WpfWindow HostWindow => _hostWindow;
    public TControl Control => _control;
    public AutomationElement Root { get; }

    public static HostedToolWindow<TControl> Create(ToolContext? context)
    {
        WpfWindow? window = null;
        TControl? control = null;
        IntPtr handle = IntPtr.Zero;

        WpfTestHost.Invoke(() =>
        {
            control = new TControl();
            control.Initialize(context, WpfTestHost.Localizer);

            window = new WpfWindow
            {
                Content = control,
                Width = 1280,
                Height = 960,
                Left = 80,
                Top = 80,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowActivated = true
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            handle = new WindowInteropHelper(window).EnsureHandle();
        });

        var automation = new UIA3Automation();
        var root = automation.FromHandle(handle)
            ?? throw new InvalidOperationException($"Could not attach UIA3Automation to handle {handle}.");

        return new HostedToolWindow<TControl>(window!, control!, automation, root);
    }

    public AutomationElement FindByAutomationId(string automationId, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        return WaitHelpers.WaitFor(
            () => Root.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            $"automation id '{automationId}'",
            timeout);
    }

    public AutomationElement? TryFindByAutomationId(string automationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        return Root.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
    }

    public T InvokeOnUi<T>(Func<TControl, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return WpfTestHost.Invoke(() => action(_control));
    }

    public void InvokeOnUi(Action<TControl> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        WpfTestHost.Invoke(() => action(_control));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _automation.Dispose();
        }
        finally
        {
            WpfTestHost.Invoke(() =>
            {
                _control.Dispose();
                _hostWindow.Close();
            });
        }
    }
}
