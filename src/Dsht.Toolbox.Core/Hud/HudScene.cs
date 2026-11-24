using System;
using Dsht.Toolbox.Core.Hud;

namespace Dsht.Toolbox.Core.Hud
{
    /// <summary>
    /// Builds the bitmap the overlay shows. Text is drawn by the host through a callback, so the
    /// geometry, the dot, the accent and the staleness treatment are all assertable and only glyph
    /// rasterisation is left to GDI.
    /// </summary>
    public static class HudScene
    {
        public const byte AccentR = 74, AccentG = 200, AccentB = 176;
        public const byte BackgroundR = 22, BackgroundG = 24, BackgroundB = 28;
        public const byte TextR = 236, TextG = 238, TextB = 242;
        public const byte MutedR = 138, MutedG = 144, MutedB = 154;
        public const byte WarningR = 226, WarningG = 168, WarningB = 74;
        public const byte UnknownR = 96, UnknownG = 100, UnknownB = 110;

        /// <summary>Paints the background and the dot, then hands every run of text to the host.</summary>
        public static Canvas Render(HudContent content, int scalePercent, Action<Canvas, HudText> drawText)
        {
            HudLayout.Measure(scalePercent, out int width, out int height);
            var canvas = new Canvas(width, height);
            canvas.Clear(BackgroundR, BackgroundG, BackgroundB, 255);

            HudLayout.DotRect(scalePercent, out int dotX, out int dotY, out int diameter);
            byte r, g, b;
            ColourFor(content, out r, out g, out b);
            canvas.FillCircle(dotX + diameter / 2, dotY + diameter / 2, Math.Max(1, diameter / 2), r, g, b, 255);

            foreach (HudText run in HudLayout.Compute(content, scalePercent))
            {
                if (drawText != null) drawText(canvas, run);
            }

            return canvas;
        }

        /// <summary>Colour of the status dot: teal working, amber waiting, grey idle or unknown.</summary>
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
                    r = UnknownR; g = UnknownG; b = UnknownB;
                    return;
            }
        }

        /// <summary>Text colour for a run: muted for labels, dimmer still when the snapshot is stale.</summary>
        public static void TextColour(HudText run, bool stale, out byte r, out byte g, out byte b)
        {
            if (run.Muted || stale) { r = MutedR; g = MutedG; b = MutedB; return; }
            r = TextR; g = TextG; b = TextB;
        }
    }
}
