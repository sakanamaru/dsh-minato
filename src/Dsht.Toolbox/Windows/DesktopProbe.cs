using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Dsht.Toolbox.Core.Diag;

namespace Dsht.Toolbox.Windows
{
    /// <summary>
    /// Reads facts from the machine. Every failure becomes a fact (unknown), never a silent
    /// false: a permission problem must not be reported as a missing installation.
    /// </summary>
    internal static class DesktopProbe
    {
        const string MainExecutable = "DeepSeek Harness.exe";
        const string UninstallerExecutable = "Uninstall DeepSeek Harness.exe";
        const string VersionFile = "version";
        const string AppArchive = "resources" + "\\" + "app.asar";
        const string BackendFragment = "resources" + "\\" + "runtime" + "\\";

        public static string DefaultInstallDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "DeepSeek Harness");
        }

        public static string DefaultUpdaterDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "@deepseek-aidsh-desktop-updater");
        }

        public static DiagFacts Collect(string installDir, string updaterDir)
        {
            var facts = new DiagFacts { InstallDir = installDir, UpdaterDir = updaterDir };

            facts.InstallDirPresence = PresenceOf(installDir);
            if (facts.InstallDirPresence != Presence.Present) return facts;

            string mainExe = Path.Combine(installDir, MainExecutable);
            facts.MainExePresence = PresenceOf(mainExe);
            facts.UninstallerPresence = PresenceOf(Path.Combine(installDir, UninstallerExecutable));
            facts.ElectronVersion = ReadTextOrNull(Path.Combine(installDir, VersionFile));
            facts.ProductVersion = FileVersionOf(mainExe);
            facts.AppVersion = AppVersionFromArchive(Path.Combine(installDir, AppArchive));

            foreach (string required in new string[] { "resources", AppArchive, "resources" + "\\" + "runtime" })
                if (PresenceOf(Path.Combine(installDir, required)) != Presence.Present) facts.PayloadGaps.Add(required);

            CollectUpdater(facts, updaterDir);
            CollectHolders(facts, installDir);
            return facts;
        }

        static void CollectUpdater(DiagFacts facts, string updaterDir)
        {
            facts.InstallerInRoot = PresenceOf(Path.Combine(updaterDir, "installer.exe")) == Presence.Present;

            Presence updaterPresence = PresenceOf(updaterDir);
            if (updaterPresence == Presence.Absent) { facts.Updater = UpdaterState.None; return; }
            if (updaterPresence == Presence.Unreadable) { facts.Updater = UpdaterState.Unreadable; return; }

            facts.Updater = UpdaterState.Clean;
            string pending = Path.Combine(updaterDir, "pending");
            Presence pendingPresence = PresenceOf(pending);
            if (pendingPresence == Presence.Unreadable) { facts.Updater = UpdaterState.Unreadable; return; }
            if (pendingPresence == Presence.Absent) return;

            string[] staged;
            try { staged = Directory.GetFiles(pending, "*.exe"); }
            catch { facts.Updater = UpdaterState.Unreadable; return; }

            if (staged.Length == 0) { facts.Updater = UpdaterState.Clean; return; }
            if (staged.Length > 1) { facts.Updater = UpdaterState.Ambiguous; facts.PendingExeName = Path.GetFileName(staged[0]); return; }

            string package = staged[0];
            facts.PendingExeName = Path.GetFileName(package);
            try { facts.PendingBytes = new FileInfo(package).Length; } catch { facts.PendingBytes = 0; }

            string info = ReadTextOrNull(Path.Combine(pending, "update-info.json"));
            facts.PendingSha512 = JsonField(info, "sha512");
            string recordedName = JsonField(info, "fileName");

            bool nameMatches = !string.IsNullOrEmpty(recordedName) &&
                               string.Equals(recordedName, facts.PendingExeName, StringComparison.OrdinalIgnoreCase);
            facts.PendingVersion = VersionFacts.Parse(facts.PendingExeName);

            facts.Updater = nameMatches && !string.IsNullOrEmpty(facts.PendingSha512)
                ? UpdaterState.Pending
                : UpdaterState.PendingUnverified;
        }

        static void CollectHolders(DiagFacts facts, string installDir)
        {
            string root = Normalize(installDir);
            string rootWithSeparator = root + Path.DirectorySeparatorChar;

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                facts.ProcessEnumerationFailed = true;
                return;
            }

            if (processes == null || processes.Length == 0)
            {
                facts.ProcessEnumerationFailed = true;
                return;
            }

            int candidates = 0;
            foreach (Process process in processes)
            {
                try
                {
                    string path = TryGetPath(process);
                    if (path == null) continue;

                    string normalized = Normalize(path);
                    if (!normalized.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)) continue;

                    candidates++;
                    long startTicks = 0;
                    try
                    {
                        startTicks = process.StartTime.ToUniversalTime().Ticks;
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited between enumeration and this read. Identity becomes
                        // unverifiable, which the executor would treat as a refusal, not a target.
                        startTicks = 0;
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        startTicks = 0;
                    }

                    facts.Holders.Add(new HolderFact
                    {
                        Pid = process.Id,
                        Name = process.ProcessName,
                        Path = path,
                        StartTimeUtcTicks = startTicks,
                        InsideInstallDir = true,
                        IsBackend = normalized.IndexOf(BackendFragment, StringComparison.OrdinalIgnoreCase) >= 0,
                    });
                }
                catch
                {
                    // A process that exits mid-enumeration is not a failure; one whose path cannot be
                    // read is simply not evidence either way.
                }
                finally
                {
                    process.Dispose();
                }
            }

            // Enumeration succeeded but yielded nothing at all: treat as unknown, not as "no processes".
            if (candidates == 0 && facts.Holders.Count == 0) { /* app may legitimately be stopped */ }
        }

        static string TryGetPath(Process process)
        {
            try { return process.MainModule != null ? process.MainModule.FileName : null; }
            catch { return null; }
        }

        /// <summary>Absolute, separator-normalised, trimmed of trailing separators.</summary>
        static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string full;
            try { full = Path.GetFullPath(path); } catch { full = path; }
            return full.TrimEnd('\\', '/');
        }

        static Presence PresenceOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return Presence.Absent;
            try
            {
                if (Directory.Exists(path) || File.Exists(path)) return Presence.Present;

                // Exists() returns false for both missing and denied; distinguish them.
                string parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && (Directory.Exists(parent) || File.Exists(parent)))
                {
                    try
                    {
                        using (var enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator()) { enumerator.MoveNext(); }
                        return Presence.Present;
                    }
                    catch (UnauthorizedAccessException) { return Presence.Unreadable; }
                    catch (DirectoryNotFoundException) { return Presence.Absent; }
                    catch (IOException) { return Presence.Unreadable; }
                }
                return Presence.Absent;
            }
            catch (UnauthorizedAccessException) { return Presence.Unreadable; }
            catch { return Presence.Unreadable; }
        }

        static string ReadTextOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; } catch { return null; }
        }

        static string FileVersionOf(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                return string.IsNullOrEmpty(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
            }
            catch { return null; }
        }

        static readonly Regex InfoField = new Regex("\\\"(?<key>[A-Za-z0-9_]+)\\\"\\s*:\\s*\\\"(?<value>[^\\\"]*)\\\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Reads one string field from update-info.json without taking a JSON dependency.</summary>
        static string JsonField(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            foreach (Match match in InfoField.Matches(json))
                if (string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
                    return match.Groups["value"].Value;
            return null;
        }

        /// <summary>
        /// The desktop app's own version lives in the asar header's package.json. Reading it avoids
        /// guessing from a four-part assembly version that has already lost its pre-release tag.
        /// </summary>
        static string AppVersionFromArchive(string asarPath)
        {
            try
            {
                if (!File.Exists(asarPath)) return null;
                using (FileStream stream = File.OpenRead(asarPath))
                {
                    byte[] prefix = new byte[16];
                    if (stream.Read(prefix, 0, 16) != 16) return null;

                    int jsonLength = BitConverter.ToInt32(prefix, 12);
                    if (jsonLength <= 0 || jsonLength > 32 * 1024 * 1024) return null;

                    byte[] header = new byte[jsonLength];
                    if (stream.Read(header, 0, jsonLength) != jsonLength) return null;

                    string text = Encoding.UTF8.GetString(header);
                    Match name = Regex.Match(text, "\\\"name\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                    if (!name.Success || name.Groups[1].Value.IndexOf("dsh-desktop", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        // Fall back to any package name that looks like the desktop app.
                        if (!name.Success) return null;
                    }

                    Match version = Regex.Match(text, "\\\"version\\\"\\s*:\\s*\\\"([0-9][^\\\"]*)\\\"");
                    return version.Success ? VersionFacts.Parse(version.Groups[1].Value) : null;
                }
            }
            catch { return null; }
        }
    }
}