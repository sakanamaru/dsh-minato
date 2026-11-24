using System;
using System.IO;
using System.IO.Compression;

namespace Dsht.Toolbox.Core.Hud
{
    /// <summary>
    /// Minimal PNG encoder for the preview image. Written by hand for two reasons: the tool ships
    /// with no dependencies, and a deterministic encoder means the preview can be asserted
    /// (dimensions, header, payload size) instead of eyeballed.
    ///
    /// It writes 8-bit RGBA, no interlacing, filter type 0 on every scanline.
    /// </summary>
    public static class Png
    {
        static readonly byte[] Signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static byte[] Encode(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (rgba.Length != width * height * 4) throw new ArgumentException("rgba length does not match dimensions", nameof(rgba));

            using (var output = new MemoryStream())
            {
                output.Write(Signature, 0, Signature.Length);

                byte[] header = new byte[13];
                WriteBigEndian(header, 0, width);
                WriteBigEndian(header, 4, height);
                header[8] = 8;    // bit depth
                header[9] = 6;    // colour type: RGBA
                header[10] = 0;   // deflate
                header[11] = 0;   // adaptive filtering
                header[12] = 0;   // no interlace
                WriteChunk(output, "IHDR", header);

                byte[] raw = RawScanlines(width, height, rgba);
                using (var compressed = new MemoryStream())
                {
                    using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                        deflate.Write(raw, 0, raw.Length);
                    WriteChunk(output, "IDAT", compressed.ToArray());
                }

                WriteChunk(output, "IEND", new byte[0]);
                return output.ToArray();
            }
        }

        static byte[] RawScanlines(int width, int height, byte[] rgba)
        {
            int stride = width * 4;
            byte[] raw = new byte[(stride + 1) * height];
            for (int y = 0; y < height; y++)
            {
                int target = y * (stride + 1);
                raw[target] = 0;
                Buffer.BlockCopy(rgba, y * stride, raw, target + 1, stride);
            }
            return raw;
        }

        static void WriteChunk(Stream stream, string type, byte[] data)
        {
            byte[] length = new byte[4];
            WriteBigEndian(length, 0, data.Length);
            stream.Write(length, 0, 4);

            byte[] typeBytes = new byte[4];
            for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
            stream.Write(typeBytes, 0, 4);
            stream.Write(data, 0, data.Length);

            uint state = Crc32.Update(Crc32.Begin(), typeBytes, 0, typeBytes.Length);
            state = Crc32.Update(state, data, 0, data.Length);
            uint crc = Crc32.Finish(state);
            byte[] crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, unchecked((int)crc));
            stream.Write(crcBytes, 0, 4);
        }

        static void WriteBigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }

    /// <summary>
    /// CRC-32 as PNG defines it (polynomial 0xEDB88320, reflected).
    ///
    /// The state is exposed in three steps because a chunk CRC covers the type and the data in
    /// one pass; combining two finished CRCs by seeding the second with the first is wrong, and
    /// that mistake makes every chunk checksum fail while a lenient decoder still renders.
    /// </summary>
    public static class Crc32
    {
        static readonly uint[] Table = BuildTable();

        public static uint Begin()
        {
            return 0xFFFFFFFFu;
        }

        public static uint Update(uint state, byte[] data, int offset, int count)
        {
            uint crc = state;
            for (int i = 0; i < count; i++)
                crc = Table[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        public static uint Finish(uint state)
        {
            return state ^ 0xFFFFFFFFu;
        }

        /// <summary>Convenience wrapper for a single buffer.</summary>
        public static uint Compute(byte[] data, int offset, int count)
        {
            return Finish(Update(Begin(), data, offset, count));
        }

        static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }

    /// <summary>A tiny RGBA surface with the handful of operations the HUD needs.</summary>
    public sealed class Canvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Pixels;

        public Canvas(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public void Clear(byte r, byte g, byte b, byte a)
        {
            for (int i = 0; i < Pixels.Length; i += 4)
            {
                Pixels[i] = r;
                Pixels[i + 1] = g;
                Pixels[i + 2] = b;
                Pixels[i + 3] = a;
            }
        }

        public void FillRect(int x, int y, int width, int height, byte r, byte g, byte b, byte a)
        {
            for (int row = y; row < y + height; row++)
            {
                if (row < 0 || row >= Height) continue;
                for (int column = x; column < x + width; column++)
                {
                    if (column < 0 || column >= Width) continue;
                    int index = (row * Width + column) * 4;
                    Pixels[index] = r;
                    Pixels[index + 1] = g;
                    Pixels[index + 2] = b;
                    Pixels[index + 3] = a;
                }
            }
        }

        public void FillCircle(int centerX, int centerY, int radius, byte r, byte g, byte b, byte a)
        {
            int squared = radius * radius;
            for (int row = centerY - radius; row <= centerY + radius; row++)
            {
                if (row < 0 || row >= Height) continue;
                for (int column = centerX - radius; column <= centerX + radius; column++)
                {
                    if (column < 0 || column >= Width) continue;
                    int dy = row - centerY;
                    int dx = column - centerX;
                    if (dx * dx + dy * dy > squared) continue;
                    int index = (row * Width + column) * 4;
                    Pixels[index] = r;
                    Pixels[index + 1] = g;
                    Pixels[index + 2] = b;
                    Pixels[index + 3] = a;
                }
            }
        }

        public byte[] ToPng()
        {
            return Png.Encode(Width, Height, Pixels);
        }
    }
}