using System;
using System.IO;
using System.Threading;
using Dsht.Toolbox.Core.Hud;
using Dsht.Toolbox.Core.Live;
using Dsht.Toolbox.Windows;

namespace Dsht.Toolbox.Commands
{
    /// <summary>
    /// <c>dsht hud</c>: the always-on-top status overlay.
    ///
    /// Three ways to run it, all sharing the same rendering path:
    /// - default: show the overlay and keep it updated from the snapshot file;
    /// - <c>--preview file.png</c>: render the same scene to an image and exit (no window);
    /// - <c>--capture file.png</c>: show the window, screenshot the screen region, exit. This is how
    ///   the window is verified without a human watching it.
    /// </summary>
    internal static class HudCommand
    {
        static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

        public static int Run(string[] args)
        {
            if (!TryParse(args, out HudOptions options, out string error))
            {
                Console.Error.WriteLine(error);
                return ExitCodes.Usage;
            }

            string snapshotPath = options.SnapshotPath ?? HudSnapshotPath.Default();

            if (options.Dump)
            {
                LiveSnapshot snapshot = HudSnapshotPath.ReadOrNull(snapshotPath);
                HudContent content = HudContent.From(snapshot, DateTimeOffset.UtcNow);
                Console.WriteLine("source : " + snapshotPath);
                Console.WriteLine("stale  : " + (content.Stale ? "yes" : "no"));
                Console.WriteLine("state  : " + content.State);
                Console.WriteLine("  " + content.Headline);
                foreach (HudRow row in content.Rows)
                    Console.WriteLine("  " + row.Label.PadRight(8) + row.Value);
                return ExitCodes.Ok;
            }

            if (options.SelfTest)
            {
                return HudSelfTest.Run(options.ScalePercent, options.OriginX, options.OriginY);
            }

            if (!string.IsNullOrEmpty(options.ProbeArea))
            {
                string[] area = options.ProbeArea.Split(',');
                if (area.Length != 4 ||
                    !int.TryParse(area[0], out int ax) || !int.TryParse(area[1], out int ay) ||
                    !int.TryParse(area[2], out int aw) || !int.TryParse(area[3], out int ah))
                {
                    Console.Error.WriteLine("--probe needs X,Y,W,H");
                    return ExitCodes.Usage;
                }

                if (!ScreenCapture.TrySave(ax, ay, aw, ah, options.ProbeAreaPath, out string probeFailure))
                {
                    Console.Error.WriteLine(probeFailure);
                    return ExitCodes.Failed;
                }
                Console.WriteLine("probe written: " + options.ProbeAreaPath);
                return ExitCodes.Ok;
            }

            if (!string.IsNullOrEmpty(options.PreviewPath))
            {
                LiveSnapshot snapshot = HudSnapshotPath.ReadOrNull(snapshotPath);
                HudContent content = HudContent.From(snapshot, DateTimeOffset.UtcNow);
                if (!TryRenderPreview(content, options.ScalePercent, options.PreviewPath, out string failure))
                {
                    Console.Error.WriteLine(failure);
                    return ExitCodes.Failed;
                }
                Console.WriteLine("preview written: " + options.PreviewPath);
                return ExitCodes.Ok;
            }

            return HudRunner.Run(
                snapshotPath,
                options.ScalePercent,
                options.OriginX,
                options.OriginY,
                RefreshInterval,
                string.IsNullOrEmpty(options.CapturePath) ? (TimeSpan?)null : TimeSpan.FromMilliseconds(700),
                options.CapturePath);
        }

        /// <summary>Renders and writes the PNG. A failure is reported, never thrown at the user.</summary>
        static bool TryRenderPreview(HudContent content, int scalePercent, string path, out string failure)
        {
            failure = string.Empty;
            HudLayout.Measure(scalePercent, out int width, out int height);

            Canvas scene = HudScene.Render(content, scalePercent, null);
            Canvas canvas;
            using (var text = new GdiText(width, height))
            {
                canvas = text.Compose(scene, content, scalePercent);
            }

            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, canvas.ToPng());
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                failure = "cannot write the preview there: access denied (" + path + ")";
                return false;
            }
            catch (IOException ex)
            {
                failure = "cannot write the preview: " + ex.Message;
                return false;
            }
        }

        static bool TryParse(string[] args, out HudOptions options, out string error)
        {
            options = new HudOptions();
            error = string.Empty;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--preview":
                        if (!TakeValue(args, ref i, out string preview)) { error = "--preview needs a file path"; return false; }
                        options.PreviewPath = preview;
                        break;

                    case "--capture":
                        if (!TakeValue(args, ref i, out string capture)) { error = "--capture needs a file path"; return false; }
                        options.CapturePath = capture;
                        break;

                    case "--probe":
                        if (!TakeValue(args, ref i, out string probe)) { error = "--probe needs X,Y,W,H"; return false; }
                        options.ProbeArea = probe;
                        if (!TakeValue(args, ref i, out string probePath)) { error = "--probe needs X,Y,W,H and a file path"; return false; }
                        options.ProbeAreaPath = probePath;
                        break;

                    case "--dump":
                        options.Dump = true;
                        break;

                    case "--selftest":
                        options.SelfTest = true;
                        break;

                    case "--snapshot":
                        if (!TakeValue(args, ref i, out string snapshotPath)) { error = "--snapshot needs a file path"; return false; }
                        options.SnapshotPath = snapshotPath;
                        break;

                    case "--scale":
                        if (!TakeValue(args, ref i, out string scale)) { error = "--scale needs a number"; return false; }
                        if (!int.TryParse(scale, out int scaleValue) || scaleValue < 100 || scaleValue > 300)
                        { error = "--scale must be between 100 and 300"; return false; }
                        options.ScalePercent = scaleValue;
                        break;

                    case "--position":
                        if (!TakeValue(args, ref i, out string position)) { error = "--position needs X,Y"; return false; }
                        string[] parts = position.Split(',');
                        if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
                        { error = "--position needs X,Y (for example 24,24)"; return false; }
                        options.OriginX = x;
                        options.OriginY = y;
                        break;

                    default:
                        error = "unknown option: " + args[i];
                        return false;
                }
            }

            return true;
        }

        static bool TakeValue(string[] args, ref int index, out string value)
        {
            value = null;
            int next = index + 1;
            if (next >= args.Length || args[next].StartsWith("--", StringComparison.Ordinal)) return false;
            value = args[next];
            index = next;
            return true;
        }
    }

    internal sealed class HudOptions
    {
        public string PreviewPath;
        public string CapturePath;
        public string SnapshotPath;
        public int ScalePercent = 100;
        public int OriginX = 24;
        public int OriginY = 24;
        public string ProbeArea;
        public string ProbeAreaPath;
        public bool SelfTest;
        public bool Dump;
    }
}