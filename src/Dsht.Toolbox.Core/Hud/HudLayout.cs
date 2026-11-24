using System;
using System.Collections.Generic;

namespace Dsht.Toolbox.Core.Hud
{
    /// <summary>One drawable run of text: what, where, how big, which weight.</summary>
    public sealed class HudText
    {
        public string Text = string.Empty;
        public int X;
        public int Y;
        public int Size;
        public bool Emphasis;
        public bool Muted;
    }

    /// <summary>
    /// Layout only: no drawing, no fonts, no window. Being pure is what lets the overlay be
    /// asserted (positions, sizes, canvas) instead of judged by eye - the failure that made the
    /// previous UI unfixable.
    ///
    /// Rules enforced here: a 4-unit grid, two font sizes, one value column so numbers line up,
    /// and a canvas that is exactly as tall as the content.
    /// </summary>
    public static class HudLayout
    {
        public const int Grid = 4;
        public const int Padding = 12;
        public const int DotDiameter = 8;
        public const int RowGap = 16;
        public const int HeadlineSize = 12;
        public const int BodySize = 11;

        /// <summary>Canvas width. Wide enough for the longest headline and value, nothing more.</summary>
        public const int Width = 176;

        /// <summary>Left column of the values, so they align regardless of label length.</summary>
        public const int ValueColumn = 88;

        /// <summary>Canvas size in device pixels for the given DPI scale (100 = 96 dpi).</summary>
        public static void Measure(int scalePercent, out int width, out int height)
        {
            int scale = Math.Max(100, scalePercent);
            width = Scale(Width, scale);

            // Headline, three body rows, and padding above and below. The previous version added a
            // whole extra body line, which left a dead band at the bottom of the bitmap.
            int content = Padding + RowGap * 3 + BodySize + Padding;
            height = Scale(content, scale);
        }

        public static List<HudText> Compute(HudContent content, int scalePercent)
        {
            int scale = Math.Max(100, scalePercent);
            var runs = new List<HudText>();

            int labelX = Scale(Padding + DotDiameter + Grid * 2, scale);
            int valueX = Scale(ValueColumn, scale);
            int y = Scale(Padding, scale);

            runs.Add(new HudText { Text = content.Headline, X = labelX, Y = y, Size = Scale(HeadlineSize, scale), Emphasis = true });
            y += Scale(RowGap, scale);

            foreach (HudRow row in content.Rows)
            {
                runs.Add(new HudText { Text = row.Label, X = labelX, Y = y, Size = Scale(BodySize, scale), Muted = true });
                runs.Add(new HudText { Text = row.Value, X = valueX, Y = y, Size = Scale(BodySize, scale) });
                y += Scale(RowGap, scale);
            }

            return runs;
        }

        /// <summary>Where the status dot goes, relative to the canvas.</summary>
        public static void DotRect(int scalePercent, out int x, out int y, out int diameter)
        {
            int scale = Math.Max(100, scalePercent);
            x = Scale(Padding, scale);
            y = Scale(Padding + 2, scale);
            diameter = Scale(DotDiameter, scale);
        }

        static int Scale(int value, int scalePercent)
        {
            return (int)Math.Round(value * (scalePercent / 100.0));
        }
    }
}