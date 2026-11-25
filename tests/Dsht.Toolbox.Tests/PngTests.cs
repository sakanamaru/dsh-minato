using System;
using System.IO;
using System.IO.Compression;
using Dsht.Toolbox.Core.Hud;

namespace Dsht.Toolbox.Tests
{
    /// <summary>Assertions about the deterministic PNG encoder.</summary>
    internal static partial class Program
    {
        static void PngTests()
        {
            Console.WriteLine("-- Png");
            var canvas = new Canvas(3, 2);
            canvas.Clear(10, 20, 30, 255);
            canvas.FillRect(1, 1, 1, 1, 200, 100, 50, 255);
            byte[] png = canvas.ToPng();

            Check("starts with the png signature", png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71);
            Check("declares an IHDR chunk", png[12] == (byte)'I' && png[13] == (byte)'H' && png[14] == (byte)'D' && png[15] == (byte)'R');

            int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            Check("encodes the requested width", width == 3);
            Check("encodes the requested height", height == 2);

            Check("ends with an IEND marker", png[png.Length - 8] == (byte)'I' && png[png.Length - 7] == (byte)'E');

            byte[] decoded = DecodeIdat(png);
            Check("the payload decodes to (stride+1)*height", decoded.Length == (3 * 4 + 1) * 2);
            Check("every scanline uses filter 0", decoded[0] == 0 && decoded[13] == 0);

            int pixel = 1 + (1 * (3 * 4 + 1)) + 1 * 4;
            Check("pixels round-trip through the deflate stream",
                decoded[pixel] == 200 && decoded[pixel + 1] == 100 && decoded[pixel + 2] == 50);

            // The empty IEND chunk has a well-known CRC; if this fails, every chunk checksum is wrong.
            byte[] iendType = new byte[] { (byte)'I', (byte)'E', (byte)'N', (byte)'D' };
            Check("the crc of IEND matches the published constant",
                Crc32.Compute(iendType, 0, 4) == 0xAE426082u);
            Check("crc state can be chained without double inversion",
                Crc32.Finish(Crc32.Update(Crc32.Update(Crc32.Begin(), iendType, 0, 2), iendType, 2, 2)) == 0xAE426082u);

            Check("every chunk crc in the encoded png verifies",
                AllChunkCrcsValid(png));

            Check("a mismatched buffer is rejected",
                Throws(() => Png.Encode(2, 2, new byte[4])));
            Check("an empty canvas is rejected", Throws(() => Png.Encode(0, 1, new byte[0])));
        }

        static bool AllChunkCrcsValid(byte[] png)
        {
            int position = 8;
            while (position + 8 <= png.Length)
            {
                int length = (png[position] << 24) | (png[position + 1] << 16) | (png[position + 2] << 8) | png[position + 3];

                uint state = Crc32.Update(Crc32.Begin(), png, position + 4, 4);
                state = Crc32.Update(state, png, position + 8, length);
                uint expected = Crc32.Finish(state);

                int crcOffset = position + 8 + length;
                uint stored = ((uint)png[crcOffset] << 24) | ((uint)png[crcOffset + 1] << 16) |
                              ((uint)png[crcOffset + 2] << 8) | png[crcOffset + 3];

                if (stored != expected) return false;
                position = crcOffset + 4;
            }
            return true;
        }

        static byte[] DecodeIdat(byte[] png)
        {
            using (var idat = new MemoryStream())
            {
                int position = 8;
                while (position < png.Length)
                {
                    int length = (png[position] << 24) | (png[position + 1] << 16) | (png[position + 2] << 8) | png[position + 3];
                    string type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
                    if (type == "IDAT") idat.Write(png, position + 8, length);
                    position += 8 + length + 4;
                }

                idat.Position = 0;
                using (var zlib = new ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    zlib.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        static bool Throws(Action action)
        {
            try { action(); return false; }
            catch (ArgumentException) { return true; }
        }
    }
}
