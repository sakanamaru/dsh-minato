using System;
using System.IO;
using Dsht.Toolbox.Core.Hud;
using Dsht.Toolbox.Core.Live;
using Dsht.Toolbox.Windows;

namespace Dsht.Toolbox.Commands
{
    /// <summary>
    /// <c>dsht hud</c>: draw the overlay from the snapshot the plugin writes.
    ///
    /// <c>--preview &lt;file.png&gt;</c> renders the same scene to an image and exits, which is how the
    /// overlay is reviewed and asserted without a window on screen.
    /// </summary>
    internal static class HudCommand
    {
        public static int Run(string[] args)
        {
            if (!TryParse(args, out HudOptions options, out string error))
            {
                Console.Error.WriteLine(error);
                return ExitCodes.Usage;
            }

            if (!options.HasPreview)
            {
                Console.Error.WriteLine("dsht hud: the interactive overlay is not implemented yet; use --preview <file.png> (" + ExitCodes.Unsupported + ")");
                return ExitCodes.Unsupported;
            }

            string snapshotPath = options.SnapshotPath ?? HudSnapshotPath.Default();
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
            bool previewSeen = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--preview":
                        if (!TakeValue(args, ref i, "--preview", out string target)) { error = "--preview needs a file path"; return false; }
                        options.PreviewPath = target;
                        previewSeen = true;
                        break;

                    case "--snapshot":
                        if (!TakeValue(args, ref i, "--snapshot", out string snapshotPath)) { error = "--snapshot needs a file path"; return false; }
                        options.SnapshotPath = snapshotPath;
                        break;

                    case "--scale":
                        if (!TakeValue(args, ref i, "--scale", out string scale)) { error = "--scale needs a number"; return false; }
                        if (!int.TryParse(scale, out int scaleValue) || scaleValue < 100 || scaleValue > 300)
                        { error = "--scale must be between 100 and 300"; return false; }
                        options.ScalePercent = scaleValue;
                        break;

                    default:
                        error = "unknown option: " + args[i];
                        return false;
                }
            }

            options.HasPreview = previewSeen;

            return true;
        }

        static bool TakeValue(string[] args, ref int index, string name, out string value)
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
        public string SnapshotPath;
        public int ScalePercent = 100;
        public bool HasPreview;
    }
}