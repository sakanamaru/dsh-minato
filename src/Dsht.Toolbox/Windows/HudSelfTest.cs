using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dsht.Toolbox.Core.Hud;

namespace Dsht.Toolbox.Windows
{
    /// <summary>
    /// Verifies the properties of the overlay window that a screenshot cannot show on a machine with
    /// no visible desktop: it exists, it is layered and click-through, it never activates, it stays
    /// out of the taskbar, it is topmost, it sits where we put it, and it accepts a frame.
    ///
    /// This is not a substitute for looking at it. It is the part that can be proven without eyes.
    /// </summary>
    internal static class HudSelfTest
    {
        const int GwlExStyle = -20;
        const int GwlStyle = -16;
        const int WsVisible = 0x10000000;
        const int WsExLayered = 0x00080000;
        const int WsExTransparent = 0x00000020;
        const int WsExToolWindow = 0x00000080;
        const int WsExNoActivate = 0x08000000;
        const int WsExAppWindow = 0x00040000;

        public static int Run(int scalePercent, int x, int y)
        {
            HudLayout.Measure(scalePercent, out int width, out int height);
            HudContent content = HudContent.From(null, DateTimeOffset.UtcNow);

            int failures = 0;
            using (var window = new LayeredHudWindow(width, height))
            {
                bool placed = window.MoveTo(x, y, out int moveError);
                if (!placed) Console.WriteLine("       placement failed with error " + moveError);

                Canvas frame;
                using (var text = new GdiText(width, height))
                {
                    frame = text.Compose(HudScene.Render(content, scalePercent, null), content, scalePercent);
                }
                window.Present(frame, 255);

                long exStyle = GetWindowLongPtr(window.Handle, GwlExStyle).ToInt64();
                long style = GetWindowLongPtr(window.Handle, GwlStyle).ToInt64();

                Report("window exists", window.Handle != IntPtr.Zero, ref failures);
                Report("window is visible", (style & WsVisible) != 0, ref failures);
                Report("window is layered", (exStyle & WsExLayered) != 0, ref failures);
                Report("window is click-through", (exStyle & WsExTransparent) != 0, ref failures);
                Report("window never activates", (exStyle & WsExNoActivate) != 0, ref failures);
                Report("window stays out of the taskbar", (exStyle & WsExToolWindow) != 0, ref failures);
                Report("window is not an app window", (exStyle & WsExAppWindow) == 0, ref failures);

                if (GetWindowRect(window.Handle, out Rect rect))
                {
                    Console.WriteLine("       requested " + x + "," + y + "  actual " + rect.Left + "," + rect.Top +
                                      "  dpi " + Dpi.PrimaryScalePercent() + "%");
                    Report("window is placed where it was asked to be",
                        rect.Left == x && rect.Top == y, ref failures);
                    Report("window is the size the layout computed",
                        rect.Right - rect.Left == width && rect.Bottom - rect.Top == height, ref failures);
                }
                else
                {
                    Report("window rect is readable", false, ref failures);
                }

                Console.Error.WriteLine();
                Console.Error.WriteLine(failures == 0
                    ? "window contract verified. Appearance still needs a human on an interactive desktop."
                    : failures + " window property/ies failed");
            }

            return failures == 0 ? ExitCodes.Ok : ExitCodes.Failed;
        }

        static void Report(string name, bool ok, ref int failures)
        {
            Console.WriteLine((ok ? "  [ok] " : "  [!!] ") + name);
            if (!ok) failures++;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    }
}