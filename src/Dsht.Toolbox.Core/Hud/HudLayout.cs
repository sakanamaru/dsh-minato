using System;
using System.Collections.Generic;

namespace Dsht.Toolbox.Core.Hud
{
    /// <summary>One drawable line of text: what, where, how big, which colour role.</summary>
    public sealed class HudText
    {
        public string Text = string.Empty;
        public int X;
        public int Y;
        public int Size;
        public bool Emphasis;
    }

    /// <summary>
    /// Layout only: no drawing, no fonts, no window. Being pure is what lets the four lines be
    /// asserted (positions, sizes, canvas) instead of judged by eye - the mistake that made the
    /// previous UI unfixable.
    ///
    /// Rules the layout enforces: one accent, three sizes (11 headline, 13 body scaled by DPI,
    /// and 1 dot), spacing on a 4-unit grid, four rows, no decoration.
    /// </summary>
    public static class HudLayout
    {
        public const int Grid = 4;
        public const int Padding = 12;
        public const int DotDiameter = 8;
        public const int RowGap = 18;

        public const int HeadlineSize = 12;
        public const int BodySize = 11;

        /// <summary>Canvas size in device pixels for the given DPI scale (1.0 = 96 dpi).</summary>
        public static void Measure(int scalePercent, out int width, out int height)
        {
            int scale = Math.Max(100, scalePercent);
            width = Scale(232, scale);
            height = Scale(Padding * 2 + RowGap * 3 + HeadlineSize + BodySize * 3, scale);
        }

        public static List<HudText> Compute(HudContent content, int scalePercent)
        {
            int scale = Math.Max(100, scalePercent);
            var lines = new List<HudText>();

            int x = Scale(Padding + DotDiameter + Grid * 2, scale);
            int y = Scale(Padding, scale);

            lines.Add(new HudText
            {
                Text = content.Headline,
                X = x,
                Y = y,
                Size = Scale(HeadlineSize, scale),
                Emphasis = true,
            });

            y += Scale(RowGap, scale);
            foreach (string line in new string[] { content.ContextLine, content.TokensLine, content.WaitingLine })
            {
                lines.Add(new HudText { Text = line, X = x, Y = y, Size = Scale(BodySize, scale), Emphasis = false });
                y += Scale(RowGap, scale);
            }

            return lines;
        }

        /// <summary>Where the status dot goes, relative to the canvas.</summary>
        public static void DotRect(int scalePercent, out int x, out int y, out int diameter)
        {
            int scale = Math.Max(100, scalePercent);
            x = Scale(Padding, scale);
            y = Scale(Padding + 3, scale);
            diameter = Scale(DotDiameter, scale);
        }

        static int Scale(int value, int scalePercent)
        {
            return (int)Math.Round(value * (scalePercent / 100.0));
        }
    }
}
