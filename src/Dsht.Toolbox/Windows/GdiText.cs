using System;
using System.Runtime.InteropServices;
using Dsht.Toolbox.Core.Hud;

namespace Dsht.Toolbox.Windows
{
    /// <summary>
    /// Glyph rasterisation for the HUD, through GDI directly.
    ///
    /// No NuGet dependency: System.Drawing.Common would be the obvious choice, but this tool ships
    /// as one file with zero packages, so the handful of GDI calls needed are declared here. The
    /// division of labour is deliberate: Core decides geometry and colour (and is tested), this
    /// class only turns strings into pixels.
    /// </summary>
    internal sealed class GdiText : IDisposable
    {
        const uint DibRgbColors = 0;
        const uint UseFontDefault = 0;
        const uint FontAntialiased = 4;
        const int TransparentBackground = 1;
        const uint DefaultCharset = 1;

        readonly int width;
        readonly int height;
        readonly IntPtr memoryDc;
        readonly IntPtr dib;
        readonly IntPtr previousObject;
        readonly IntPtr bits;

        public GdiText(int width, int height)
        {
            this.width = width;
            this.height = height;

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
            var info = new BitmapInfo { Header = header };

            dib = CreateDIBSection(memoryDc, ref info, DibRgbColors, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new InvalidOperationException("could not create a DIB section");

            previousObject = SelectObject(memoryDc, dib);
            SetBkMode(memoryDc, TransparentBackground);
        }

        /// <summary>Copies the scene into the bitmap, draws every line, and reads the result back.</summary>
        public Canvas Compose(Canvas canvas, HudContent content, int scalePercent)
        {
            Marshal.Copy(canvas.Pixels, 0, bits, canvas.Pixels.Length);

            foreach (HudText line in HudLayout.Compute(content, scalePercent))
            {
                byte r, g, b;
                HudScene.TextColour(!line.Emphasis && content.Stale, out r, out g, out b);
                DrawLine(line, r, g, b);
            }

            var result = new Canvas(canvas.Width, canvas.Height);
            Marshal.Copy(bits, result.Pixels, 0, result.Pixels.Length);

            // GDI does not maintain an alpha channel; the overlay is fully opaque where it draws.
            for (int i = 3; i < result.Pixels.Length; i += 4) result.Pixels[i] = 255;
            return result;
        }

        void DrawLine(HudText line, byte r, byte g, byte b)
        {
            IntPtr font = CreateFontW(
                -line.Size, 0, 0, 0, line.Emphasis ? 600 : 400,
                UseFontDefault, UseFontDefault, UseFontDefault,
                DefaultCharset, UseFontDefault, UseFontDefault, FontAntialiased, UseFontDefault,
                "Segoe UI");

            IntPtr previousFont = IntPtr.Zero;
            try
            {
                if (font != IntPtr.Zero) previousFont = SelectObject(memoryDc, font);
                SetTextColor(memoryDc, ColourRef(r, g, b));
                TextOutW(memoryDc, line.X, line.Y, line.Text, line.Text.Length);
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

        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public uint Colours;
        }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr dc, uint colour);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOutW(IntPtr dc, int x, int y, string text, int count);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontW(
            int height, int width, int escapement, int orientation, int weight,
            uint italic, uint underline, uint strike, uint charset,
            uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    }
}