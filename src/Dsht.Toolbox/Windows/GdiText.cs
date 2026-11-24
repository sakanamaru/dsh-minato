using System;
using System.Runtime.InteropServices;
using Dsht.Toolbox.Core.Hud;

namespace Dsht.Toolbox.Windows
{
    /// <summary>
    /// Glyph rasterisation for the overlay, through GDI directly.
    ///
    /// System.Drawing.Common would be the obvious choice, but this tool ships as one file with zero
    /// packages, so the handful of GDI calls needed are declared here. The division of labour is
    /// deliberate: Core decides geometry and colour (and is tested), this class only turns strings
    /// into pixels.
    /// </summary>
    internal sealed class GdiText : IDisposable
    {
        const uint DibRgbColors = 0;
        const uint FontAntialiased = 4;
        const int TransparentBackground = 1;
        const uint DefaultCharset = 1;
        const string Face = "Segoe UI";

        readonly IntPtr memoryDc;
        readonly IntPtr dib;
        readonly IntPtr previousObject;
        readonly IntPtr bits;

        public GdiText(int width, int height)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            try
            {
                memoryDc = CreateCompatibleDC(screenDc);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }

            var header = new BitmapInfoHeader
            {
                HeaderSize = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
                Width = width,
                Height = -height,   // negative height selects a top-down bitmap
                Planes = 1,
                BitCount = 32,
                Compression = 0,
            };

            dib = CreateDIBSection(memoryDc, ref header, DibRgbColors, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new InvalidOperationException("could not create a DIB section");

            previousObject = SelectObject(memoryDc, dib);
            SetBkMode(memoryDc, TransparentBackground);
        }

        /// <summary>Copies the scene in, draws every run of text, and reads the result back.</summary>
        public Canvas Compose(Canvas canvas, HudContent content, int scalePercent)
        {
            Marshal.Copy(canvas.Pixels, 0, bits, canvas.Pixels.Length);

            foreach (HudText run in HudLayout.Compute(content, scalePercent))
            {
                byte r, g, b;
                HudScene.TextColour(run, content.Stale, out r, out g, out b);
                if (!string.IsNullOrEmpty(run.Text)) DrawRun(run, r, g, b);
            }

            var result = new Canvas(canvas.Width, canvas.Height);
            Marshal.Copy(bits, result.Pixels, 0, result.Pixels.Length);

            // GDI does not maintain an alpha channel; the overlay is opaque where it draws.
            for (int i = 3; i < result.Pixels.Length; i += 4) result.Pixels[i] = 255;
            return result;
        }

        void DrawRun(HudText run, byte r, byte g, byte b)
        {
            IntPtr font = CreateFontW(
                -run.Size, 0, 0, 0, run.Emphasis ? 600 : 400,
                0, 0, 0, DefaultCharset, 0, 0, FontAntialiased, 0, Face);

            IntPtr previousFont = IntPtr.Zero;
            try
            {
                if (font != IntPtr.Zero) previousFont = SelectObject(memoryDc, font);
                SetTextColor(memoryDc, ColourRef(r, g, b));
                TextOutW(memoryDc, run.X, run.Y, run.Text, run.Text.Length);
            }
            finally
            {
                if (previousFont != IntPtr.Zero) SelectObject(memoryDc, previousFont);
                if (font != IntPtr.Zero) DeleteObject(font);
            }
        }

        static uint ColourRef(byte r, byte g, byte b)
        {
            return (uint)(r | (g << 8) | (b << 16));
        }

        public void Dispose()
        {
            if (previousObject != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, previousObject);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
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
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr dc, uint colour);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOutW(IntPtr dc, int x, int y, string text, int count);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontW(
            int height, int width, int escapement, int orientation, int weight,
            uint italic, uint underline, uint strike, uint charset,
            uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    }
}
