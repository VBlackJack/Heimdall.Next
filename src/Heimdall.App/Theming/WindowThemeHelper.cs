using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Heimdall.App.Theming;

internal static class WindowThemeHelper
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int attrSize);

    public static void ApplyCurrentTheme(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        void OnSourceInitialized(object? sender, EventArgs args)
        {
            window.SourceInitialized -= OnSourceInitialized;
            Apply(window);
        }

        if (PresentationSource.FromVisual(window) is null)
        {
            window.SourceInitialized += OnSourceInitialized;
            return;
        }

        Apply(window);
    }

    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var useDarkMode = ShouldUseDarkTitleBar(window) ? 1 : 0;
        var size = Marshal.SizeOf<int>();

        _ = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref useDarkMode, size);
        _ = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeLegacy, ref useDarkMode, size);
    }

    private static bool ShouldUseDarkTitleBar(FrameworkElement element)
    {
        var resource = element.TryFindResource("BackgroundBrush")
            ?? Application.Current.TryFindResource("BackgroundBrush");

        if (resource is SolidColorBrush brush)
        {
            var luminance = ((0.299 * brush.Color.R)
                + (0.587 * brush.Color.G)
                + (0.114 * brush.Color.B)) / 255.0;

            return luminance < 0.5;
        }

        return true;
    }
}
