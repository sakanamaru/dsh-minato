using System;
using System.Runtime.InteropServices;
using Dsht.Toolbox.Core.Hud;

namespace Dsht.Toolbox.Windows
{
    /// <summary>
    /// The overlay window: always on top, click-through, no taskbar entry, no focus stealing.
    ///
    /// A layered window is used because the pixel content comes from Core (which is tested) and is
    /// pushed with UpdateLayeredWindow, so the window never re-renders on WM_PAINT and never flickers.
    /// Click-through is WS_EX_TRANSPARENT plus WS_EX_NOACTIVATE: the user must be able to work under
    /// it, and a status display that steals focus would be worse than useless.
    /// </summary>
    internal sealed class LayeredHudWindow : IDisposable
    {
        const int WsPopup = unchecked((int)0x80000000);
        const int WsExLayered = 0x00080000;
        const int WsExTransparent = 0x00000020;
        const int WsExToolWindow = 0x00000080;
        const int WsExNoActivate = 0x08000000;
        const int WsExTopmost = 0x00000008;

        const uint SwpNoActivate = 0x0010;
        const uint SwpShowWindow = 0x0040;
        const int UlwAlpha = 0x00000002;
        const byte AcSrcOver = 0x00;
        const byte AcSrcAlpha = 0x01;

        // The window procedure must stay reachable from managed code for the window's lifetime;
        // GetFunctionPointerForDelegate does not keep the delegate alive by itself.
        static readonly WndProcDelegate Procedure = DefWindowProc;

        readonly IntPtr handle;

        // UpdateLayeredWindow both paints and repositions: its destination point is the new window
        // origin. Forgetting this makes every frame snap the overlay back to (0,0).
        int originX;
        int originY;
        readonly int width;
        readonly int height;
        readonly IntPtr screenDc;
        readonly IntPtr memoryDc;
        readonly IntPtr bitmap;
        readonly IntPtr previousObject;

        public LayeredHudWindow(int width, int height)
        {
            this.width = width;
            this.height = height;

            IntPtr module = GetModuleHandle(null);
            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf(typeof(WindowClassEx)),
                Style = 0,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = module,
                ClassName = "dsht.hud",
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                int classError = Marshal.GetLastWin32Error();
                // 1410 means the class already exists from an earlier run in this process.
                if (classError != 1410)
                    throw new InvalidOperationException("could not register the overlay window class (error " + classError + ")");
            }

            handle = CreateWindowEx(
                WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopmost,
                "dsht.hud", string.Empty, WsPopup,
                0, 0, width, height,
                IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);

            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("could not create the overlay window (error " + Marshal.GetLastWin32Error() + ")");

            screenDc = GetDC(IntPtr.Zero);
            memoryDc = CreateCompatibleDC(screenDc);

            var header = new BitmapInfoHeader
            {
                HeaderSize = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
            };

            IntPtr bits;
            bitmap = CreateDIBSection(memoryDc, ref header, 0, out bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) throw new InvalidOperationException("could not create the overlay bitmap");
            previousObject = SelectObject(memoryDc, bitmap);
        }

        /// <summary>Handle of the overlay window, for capturing it directly.</summary>
        public IntPtr Handle { get { return handle; } }

        /// <summary>Places and shows the overlay. Returns false with the win32 error when it fails.</summary>
        public bool MoveTo(int x, int y, out int error)
        {
            originX = x;
            originY = y;
            error = 0;
            if (SetWindowPos(handle, IntPtr.Zero, x, y, width, height, SwpNoActivate | SwpShowWindow)) return true;
            error = Marshal.GetLastWin32Error();

            // A layered window can refuse SetWindowPos before it has content; retry via MoveWindow.
            if (MoveWindow(handle, x, y, width, height, true)) { error = 0; return true; }
            error = Marshal.GetLastWin32Error();
            return false;
        }

        /// <summary>Pushes a rendered frame to the screen. Alpha is premultiplied, as the API requires.</summary>
        public void Present(Canvas canvas, byte opacity)
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

            IntPtr bits;
            IntPtr dib = CreateDIBSection(screenDc, ref header, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) return;

            try
            {
                byte[] premultiplied = Premultiply(canvas, opacity);
                Marshal.Copy(premultiplied, 0, bits, premultiplied.Length);

                IntPtr dc = CreateCompatibleDC(screenDc);
                try
                {
                    IntPtr previous = SelectObject(dc, dib);
                    var destination = new Point { X = originX, Y = originY };
                    var size = new Size { Width = canvas.Width, Height = canvas.Height };
                    var source = new Point { X = 0, Y = 0 };
                    var blending = new BlendFunction
                    {
                        BlendOp = AcSrcOver,
                        SourceConstantAlpha = 255,
                        AlphaFormat = AcSrcAlpha,
                    };

                    UpdateLayeredWindow(handle, screenDc, ref destination, ref size,
                        dc, ref source, 0, ref blending, UlwAlpha);

                    SelectObject(dc, previous);
                }
                finally
                {
                    DeleteDC(dc);
                }
            }
            finally
            {
                DeleteObject(dib);
            }
        }

        /// <summary>Windows wants premultiplied BGRA for a layered window.</summary>
        static byte[] Premultiply(Canvas canvas, byte opacity)
        {
            byte[] output = new byte[canvas.Pixels.Length];
            int alpha = opacity > 255 ? 255 : opacity;
            for (int i = 0; i < canvas.Pixels.Length; i += 4)
            {
                int a = canvas.Pixels[i + 3] * alpha / 255;
                output[i] = (byte)(canvas.Pixels[i + 2] * a / 255);      // blue
                output[i + 1] = (byte)(canvas.Pixels[i + 1] * a / 255);  // green
                output[i + 2] = (byte)(canvas.Pixels[i] * a / 255);      // red
                output[i + 3] = (byte)a;
            }
            return output;
        }

        /// <summary>Runs the message loop until <paramref name="stopAfter"/> elapses or the process is closed.</summary>
        public bool PumpUntil(DateTime deadline)
        {
            while (DateTime.UtcNow < deadline)
            {
                while (PeekMessage(out Message message, IntPtr.Zero, 0, 0, 1))
                {
                    if (message.Value == 0x0012) return true;   // WM_QUIT
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                System.Threading.Thread.Sleep(15);
            }
            return false;
        }

        public void Dispose()
        {
            if (previousObject != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, previousObject);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            if (handle != IntPtr.Zero) DestroyWindow(handle);
            UnregisterClass("dsht.hud", GetModuleHandle(null));
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WindowClassEx
        {
            public uint Size;
            public uint Style;
            public IntPtr WindowProcedure;
            public int ClassExtra;
            public int WindowExtra;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr Background;
            [MarshalAs(UnmanagedType.LPWStr)] public string MenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
            public IntPtr IconSmall;
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
        struct Point { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct Size { public int Width; public int Height; }

        [StructLayout(LayoutKind.Sequential)]
        struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Message
        {
            public IntPtr Handle;
            public uint Value;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public Point Cursor;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

        delegate IntPtr WndProcDelegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc, ref Point destination, ref Size size,
            IntPtr sourceDc, ref Point source, uint colourKey, ref BlendFunction blending, int flags);

        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    }
}