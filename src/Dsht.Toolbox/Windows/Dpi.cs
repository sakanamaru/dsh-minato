using System;
using System.Runtime.InteropServices;

namespace Dsht.Toolbox.Windows
{
    /// <summary>
    /// Declares the process DPI aware.
    ///
    /// Without this the system virtualises coordinates: the window lands somewhere other than the
    /// requested position and glyphs are scaled twice on a 150 percent display. Per-monitor v2 is
    /// preferred because the overlay can sit on a monitor with a different scale factor.
    /// </summary>
    internal static class Dpi
    {
        static bool initialised;

        public static void Initialize()
        {
            if (initialised) return;
            initialised = true;

            // -4 is DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2; older builds fall back once.
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch (EntryPointNotFoundException)
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        /// <summary>Current scale factor of the primary monitor, in percent (100 = 96 dpi).</summary>
        public static int PrimaryScalePercent()
        {
            IntPtr monitor = MonitorFromPoint(new Point { X = 0, Y = 0 }, 1);
            if (monitor == IntPtr.Zero) return 100;

            uint dpiX, dpiY;
            if (GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) != 0) return 100;
            return (int)Math.Round(dpiX * 100.0 / 96.0);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Point { public int X; public int Y; }

        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);

        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    }
}
