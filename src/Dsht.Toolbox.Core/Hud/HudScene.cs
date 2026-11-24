using System;
using System.Collections.Generic;
using Dsht.Toolbox.Core.Hud;

namespace Dsht.Toolbox.Core.Hud
{
    /// <summary>
    /// Builds the bitmap the HUD shows. Text is supplied by a callback so the scene stays free of
    /// GDI and can be produced in tests with a stub blitter: the geometry, the dot, the accent and
    /// the staleness treatment are all asserted, and only glyph rasterisation is left to the host.
    /// </summary>
    public static class HudScene
    {
        /// <summary>One accent colour, chosen once: a muted teal that survives both Windows themes.</summary>
        public const byte AccentR = 74, AccentG = 200, AccentB = 176;
        public const byte BackgroundR = 22, BackgroundG = 24, BackgroundB = 28;
        public const byte TextR = 236, TextG = 238, TextB = 242;
        public const byte MutedR = 150, MutedG = 156, MutedB = 166;
        public const byte WarningR = 226, WarningG = 168, WarningB = 74;

        /// <summary>
        /// Draws the scene. <paramref name="drawText"/> receives the canvas, the line and the role
        /// colour and is responsible for glyphs only.
        /// </summary>
        public static Canvas Render(HudContent content, int scalePercent, Action<Canvas, HudText, bool> drawText)
        {
            HudLayout.Measure(scalePercent, out int width, out int height);
            var canvas = new Canvas(width, height);
            canvas.Clear(BackgroundR, BackgroundG, BackgroundB, 235);

            HudLayout.DotRect(scalePercent, out int dotX, out int dotY, out int diameter);
            byte r, g, b;
            ColourFor(content, out r, out g, out b);
            canvas.FillCircle(dotX + diameter / 2, dotY + diameter / 2, diameter / 2, r, g, b, 255);

            foreach (HudText line in HudLayout.Compute(content, scalePercent))
            {
                bool muted = !line.Emphasis && content.Stale;
                if (drawText != null) drawText(canvas, line, muted);
            }

            return canvas;
        }

        /// <summary>Colour of the status dot: teal while working, amber when a question waits.</summary>
        public static void ColourFor(HudContent content, out byte r, out byte g, out byte b)
        {
            switch (content.State)
            {
                case HudState.Waiting:
                    r = WarningR; g = WarningG; b = WarningB;
                    return;
                case HudState.Working:
                    r = AccentR; g = AccentG; b = AccentB;
                    return;
                case HudState.Idle:
                    r = MutedR; g = MutedG; b = MutedB;
                    return;
                default:
                    r = 96; g = 100; b = 110;
                    return;
            }
        }

        public static void TextColour(bool muted, out byte r, out byte g, out byte b)
        {
            if (muted) { r = MutedR; g = MutedG; b = MutedB; return; }
            r = TextR; g = TextG; b = TextB;
        }
    }
}
