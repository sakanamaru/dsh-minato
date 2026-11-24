using System;
using System.IO;

namespace Dsht.Toolbox.Core.Hud
{
    /// <summary>
    /// Where the plugin writes the snapshot and the HUD reads it. One definition, so the two
    /// halves cannot disagree about the path.
    /// </summary>
    public static class HudSnapshotPath
    {
        public const string FileName = "snapshot.json";

        public static string DefaultDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "dsht");
        }

        public static string Default()
        {
            return Path.Combine(DefaultDirectory(), FileName);
        }

        /// <summary>Reads the snapshot, tolerating a missing or half-written file.</summary>
        public static Live.LiveSnapshot ReadOrNull(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return Live.LiveSnapshot.Parse(File.ReadAllText(path));
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
