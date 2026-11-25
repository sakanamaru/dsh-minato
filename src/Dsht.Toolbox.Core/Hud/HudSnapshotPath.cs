using System;
using System.IO;

namespace Dsht.Toolbox.Core.Hud
{
    /// <summary>
    /// Where the snapshot is read from.
    ///
    /// The producer is the existing dsh-minato-bridge plugin, which writes to
    /// <c>&lt;DSH_HOME&gt;/shio-bridge/sessions.json</c>. DSV_HOME defaults to <c>%USERPROFILE%\.dsh</c>
    /// (the desktop app honours the same layout), so both sides agree without configuration.
    /// </summary>
    public static class HudSnapshotPath
    {
        public const string FileName = "sessions.json";
        public const string DirectoryName = "shio-bridge";

        public static string DshHome()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(profile, ".dsh");
        }

        public static string Default()
        {
            return Path.Combine(DshHome(), DirectoryName, FileName);
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
