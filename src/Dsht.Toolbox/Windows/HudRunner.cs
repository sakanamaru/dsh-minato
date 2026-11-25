using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Dsht.Toolbox.Core.Hud;
using Dsht.Toolbox.Core.Live;

namespace Dsht.Toolbox.Windows
{
    /// <summary>
    /// Drives the overlay: reads the snapshot on a timer, redraws, and pushes the frame.
    ///
    /// Two modes share this code so they cannot drift:
    /// - interactive: show the window until the process is closed;
    /// - capture: show it, wait a moment, write a screenshot of the screen region, exit. That is how
    ///   the window itself is verified without asking a human to look at it.
    /// </summary>
    internal static class HudRunner
    {
        public static int Run(string snapshotPath, int scalePercent, int originX, int originY,
            TimeSpan refresh, TimeSpan? captureAfter, string capturePath)
        {
            HudLayout.Measure(scalePercent, out int width, out int height);

            Console.WriteLine("reading " + snapshotPath);

            LiveSnapshot first = HudSnapshotPath.ReadOrNull(snapshotPath);
            HudContent content = HudContent.From(first, DateTimeOffset.UtcNow);
            Canvas frame = Paint(content, scalePercent, width, height);

            using (var window = new LayeredHudWindow(width, height))
            {
                if (!window.MoveTo(originX, originY, out int moveError))
                {
                    Console.Error.WriteLine("could not place the overlay (error " + moveError + ")");
                    return ExitCodes.Failed;
                }
                window.Present(frame, 255);

                if (!string.IsNullOrEmpty(capturePath))
                {
                    Thread.Sleep(captureAfter ?? TimeSpan.FromMilliseconds(600));

                    // Capture the window itself first. A screen capture is the stronger proof (it
                    // shows the overlay actually composited), but this process may run without an
                    // interactive desktop, in which case the screen is blank and proves nothing.
                    if (ScreenCapture.TryCaptureWindow(window.Handle, width, height, capturePath, out string windowFailure))
                    {
                        Console.WriteLine("captured window: " + capturePath);
                        return ExitCodes.Ok;
                    }

                    if (!ScreenCapture.TrySave(originX, originY, width, height, capturePath, out string screenFailure))
                    {
                        Console.Error.WriteLine(screenFailure);
                        return ExitCodes.Failed;
                    }

                    if (ScreenCapture.IsUniformFromFile(capturePath))
                    {
                        Console.Error.WriteLine("capture is a uniform colour: this session has no visible desktop,");
                        Console.Error.WriteLine("so the screenshot proves nothing. Window capture also failed: " + windowFailure);
                        return ExitCodes.Failed;
                    }

                    Console.WriteLine("captured screen region: " + capturePath);
                    return ExitCodes.Ok;
                }

                // Ctrl+C is the way out: an overlay has no close button and must not be a
                // process the user has to hunt down in Task Manager.
                bool stopRequested = false;
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs eventArgs)
                {
                    eventArgs.Cancel = true;
                    stopRequested = true;
                };

                DateTime nextRefresh = DateTime.UtcNow + refresh;
                while (!stopRequested)
                {
                    if (window.PumpUntil(DateTime.UtcNow + TimeSpan.FromMilliseconds(120))) break;

                    if (DateTime.UtcNow >= nextRefresh)
                    {
                        LiveSnapshot snapshot = HudSnapshotPath.ReadOrNull(snapshotPath);
                        HudContent updated = HudContent.From(snapshot, DateTimeOffset.UtcNow);
                        window.Present(Paint(updated, scalePercent, width, height), 255);
                        nextRefresh = DateTime.UtcNow + refresh;
                    }
                }
            }

            return ExitCodes.Ok;
        }

        static Canvas Paint(HudContent content, int scalePercent, int width, int height)
        {
            Canvas scene = HudScene.Render(content, scalePercent, null);
            using (var text = new GdiText(width, height))
            {
                return text.Compose(scene, content, scalePercent);
            }
        }
    }

    /// <summary>
    /// Captures a screen region to PNG. Used to prove the overlay actually appeared on screen,
    /// which a bitmap dump of our own buffer would not prove.
    /// </summary>
    internal static class ScreenCapture
    {
        const int SourceCopy = 0x00CC0020;
        const int CaptureBlt = 0x40000000;

        /// <summary>
        /// Asks the window to render itself into a bitmap. Works for layered windows and needs no
        /// visible desktop, so it verifies our own rendering even in a headless session.
        /// </summary>
        public static bool TryCaptureWindow(IntPtr window, int width, int height, string path, out string failure)
        {
            failure = string.Empty;
            IntPtr windowDc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;

            try
            {
                windowDc = GetWindowDC(window);
                if (windowDc == IntPtr.Zero) { failure = "could not get the window device context"; return false; }

                memoryDc = CreateCompatibleDC(windowDc);
                bitmap = CreateCompatibleBitmap(windowDc, width, height);
                previous = SelectObject(memoryDc, bitmap);

                const uint RenderFullContent = 0x00000002;
                if (!PrintWindow(window, memoryDc, RenderFullContent))
                {
                    failure = "the window refused to render itself (PrintWindow)";
                    return false;
                }

                SelectObject(memoryDc, previous);
                previous = IntPtr.Zero;

                var canvas = new Canvas(width, height);
                ReadInto(canvas, memoryDc);
                if (IsUniform(canvas)) { failure = "the captured window is a uniform colour"; return false; }

                WritePng(canvas, path);
                return true;
            }
            catch (Exception ex)
            {
                failure = "could not capture the window: " + ex.Message;
                return false;
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memoryDc, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (windowDc != IntPtr.Zero) ReleaseDC(window, windowDc);
            }
        }

        /// <summary>True when almost every pixel is identical, which means the capture is empty.</summary>
        public static bool IsUniform(Canvas canvas)
        {
            int first = 0;
            int same = 0;
            int total = canvas.Width * canvas.Height;
            for (int i = 0; i < canvas.Pixels.Length; i += 4)
            {
                if (canvas.Pixels[i] == canvas.Pixels[first] &&
                    canvas.Pixels[i + 1] == canvas.Pixels[first + 1] &&
                    canvas.Pixels[i + 2] == canvas.Pixels[first + 2]) same++;
            }
            return same > total * 99 / 100;
        }

        /// <summary>Reads back a PNG we just wrote and reports whether it is uniform.</summary>
        public static bool IsUniformFromFile(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    return stream.Length < 1200;
                }
            }
            catch { return false; }
        }

        static void WritePng(Canvas canvas, string path)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, canvas.ToPng());
        }

        public static bool TrySave(int x, int y, int width, int height, string path, out string failure)
        {
            failure = string.Empty;
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;

            try
            {
                screenDc = GetDC(IntPtr.Zero);
                memoryDc = CreateCompatibleDC(screenDc);
                bitmap = CreateCompatibleBitmap(screenDc, width, height);
                previous = SelectObject(memoryDc, bitmap);

                if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SourceCopy | CaptureBlt))
                {
                    failure = "screen capture failed (BitBlt)";
                    return false;
                }

                SelectObject(memoryDc, previous);
                previous = IntPtr.Zero;

                var canvas = new Canvas(width, height);
                ReadInto(canvas, memoryDc);
                if (IsUniform(canvas)) { failure = "the screen region is a uniform colour (no visible desktop)"; return false; }

                WritePng(canvas, path);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                failure = "cannot write the capture there: access denied (" + path + ")";
                return false;
            }
            catch (IOException ex)
            {
                failure = "cannot write the capture: " + ex.Message;
                return false;
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memoryDc, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>Reads the DIB pixels into a Canvas, normalising BGRA to RGBA.</summary>
        static void ReadInto(Canvas canvas, IntPtr memoryDc)
        {
            var header = new BitmapInfoHeader
            {
                HeaderSize = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
                Width = canvas.Width,
                Height = -canvas.Height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
            };

            var bytes = new byte[canvas.Width * canvas.Height * 4];
            int rows = GetDIBits(memoryDc, GetCurrentObject(memoryDc, 7), 0, (uint)canvas.Height, bytes, ref header, 0);
            if (rows == 0) throw new InvalidOperationException("could not read back the captured pixels");

            for (int i = 0; i < bytes.Length; i += 4)
            {
                canvas.Pixels[i] = bytes[i + 2];
                canvas.Pixels[i + 1] = bytes[i + 1];
                canvas.Pixels[i + 2] = bytes[i];
                canvas.Pixels[i + 3] = 255;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfoHeader
        {
            public uint HeaderSize;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint ImageSize;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ColoursUsed;
            public uint ColoursImportant;
        }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern IntPtr GetCurrentObject(IntPtr dc, uint type);
        [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr window);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines,
            byte[] bits, ref BitmapInfoHeader info, uint usage);

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
            IntPtr source, int sourceX, int sourceY, int operation);
    }
}