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

using System.Runtime.InteropServices;

namespace TwinShell.Infrastructure.Interop;

/// <summary>
/// Desktop Window Manager API for Windows 11 Mica/Acrylic backdrop effects.
/// Provides P/Invoke declarations for DwmSetWindowAttribute and related functions.
/// </summary>
public static class DwmApi
{
    private const string DwmApiDll = "dwmapi.dll";

    /// <summary>
    /// Sets the value of Desktop Window Manager (DWM) non-client rendering attributes.
    /// </summary>
    /// <param name="hwnd">Handle to the window.</param>
    /// <param name="attr">The attribute to set.</param>
    /// <param name="attrValue">Pointer to the attribute value.</param>
    /// <param name="cbAttribute">Size of the attribute value.</param>
    /// <returns>S_OK (0) on success, or an HRESULT error code.</returns>
    [DllImport(DwmApiDll, SetLastError = true)]
    public static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int cbAttribute);

    /// <summary>
    /// Gets the value of Desktop Window Manager (DWM) non-client rendering attributes.
    /// </summary>
    [DllImport(DwmApiDll, SetLastError = true)]
    public static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int attr,
        out int attrValue,
        int cbAttribute);

    /// <summary>
    /// Extends the window frame into the client area.
    /// </summary>
    [DllImport(DwmApiDll, SetLastError = true)]
    public static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd,
        ref MARGINS pMarInset);

    // ============================================================
    // DWMWINDOWATTRIBUTE enumeration values
    // ============================================================

    /// <summary>
    /// Use with DwmSetWindowAttribute. Enables or disables dark mode for the window.
    /// Set to 1 for dark mode, 0 for light mode.
    /// Available on Windows 10 1809 and later.
    /// </summary>
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>
    /// Use with DwmSetWindowAttribute. Sets the system-drawn backdrop material.
    /// Requires Windows 11 Build 22000 or later.
    /// </summary>
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>
    /// Use with DwmSetWindowAttribute. Enables Mica effect.
    /// Legacy attribute for Windows 11 21H2-22H1. Use DWMWA_SYSTEMBACKDROP_TYPE instead.
    /// </summary>
    public const int DWMWA_MICA_EFFECT = 1029;

    /// <summary>
    /// Use with DwmSetWindowAttribute. Sets window corner preference.
    /// </summary>
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    // ============================================================
    // DWM_SYSTEMBACKDROP_TYPE enumeration values
    // ============================================================

    /// <summary>
    /// Let the Desktop Window Manager automatically decide the backdrop material.
    /// </summary>
    public const int DWMSBT_AUTO = 0;

    /// <summary>
    /// Do not apply any system backdrop material.
    /// </summary>
    public const int DWMSBT_DISABLE = 1;

    /// <summary>
    /// Apply Mica material to the window background.
    /// Best for main application windows. Samples wallpaper once for performance.
    /// </summary>
    public const int DWMSBT_MAINWINDOW = 2;

    /// <summary>
    /// Apply Acrylic material to the window background.
    /// Best for transient/popup windows. Continuous blur effect.
    /// </summary>
    public const int DWMSBT_TRANSIENTWINDOW = 3;

    /// <summary>
    /// Apply Mica Alt material to the window background.
    /// Similar to Mica but optimized for tabbed interfaces.
    /// </summary>
    public const int DWMSBT_TABBEDWINDOW = 4;

    // ============================================================
    // DWM_WINDOW_CORNER_PREFERENCE enumeration values
    // ============================================================

    /// <summary>
    /// Let the system decide whether to round window corners.
    /// </summary>
    public const int DWMWCP_DEFAULT = 0;

    /// <summary>
    /// Never round window corners.
    /// </summary>
    public const int DWMWCP_DONOTROUND = 1;

    /// <summary>
    /// Round the corners if appropriate.
    /// </summary>
    public const int DWMWCP_ROUND = 2;

    /// <summary>
    /// Round the corners with a small radius.
    /// </summary>
    public const int DWMWCP_ROUNDSMALL = 3;

    // ============================================================
    // Helper structures
    // ============================================================

    /// <summary>
    /// Margins structure for DwmExtendFrameIntoClientArea.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;

        /// <summary>
        /// Creates margins that extend into all client area (-1 for all sides).
        /// </summary>
        public static MARGINS ExtendAll => new() { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
    }
}
