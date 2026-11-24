using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Dsht.Toolbox.Core.Diag;

namespace Dsht.Toolbox.Windows
{
    /// <summary>
    /// Reads facts from the machine. The only place that touches the filesystem and the
    /// process table for <c>diag</c>; every failure is recorded as a fact rather than thrown,
    /// because half a diagnosis is still useful when permissions are partial.
    /// </summary>
    internal static class DesktopProbe
    {
        const string MainExecutable = "DeepSeek Harness.exe";
        const string UninstallerExecutable = "Uninstall DeepSeek Harness.exe";
        const string VersionFile = "version";
        const string BackendRuntimeFragment = "\\resources\\runtime\\";

        public static string DefaultInstallDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Programs", "DeepSeek Harness");
        }

        public static string DefaultUpdaterDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "@deepseek-aidsh-desktop-updater");
        }

        public static DiagFacts Collect(string installDir, string updaterDir)
        {
            var facts = new DiagFacts
            {
                InstallDir = installDir,
                UpdaterDir = updaterDir,
                InstallDirExists = DirectoryExists(installDir),
            };

            string mainExe = Path.Combine(installDir, MainExecutable);
            facts.MainExeExists = FileExists(mainExe);
            facts.UninstallerExists = FileExists(Path.Combine(installDir, UninstallerExecutable));
            facts.ElectronVersion = ReadTextOrNull(Path.Combine(installDir, VersionFile));
            facts.ProductVersion = FileVersionOf(mainExe);

            CollectUpdater(facts, updaterDir);
            CollectHolders(facts, installDir);
            return facts;
        }

        static void CollectUpdater(DiagFacts facts, string updaterDir)
        {
            facts.InstallerInRoot = FileExists(Path.Combine(updaterDir, "installer.exe"));

            if (!DirectoryExists(updaterDir))
            {
                facts.Updater = UpdaterState.None;
                return;
            }

            facts.Updater = UpdaterState.Clean;
            string pending = Path.Combine(updaterDir, "pending");
            if (!DirectoryExists(pending))
            {
                if (facts.InstallerInRoot) facts.Updater = UpdaterState.Suspicious;
                return;
            }

            string[] staged;
            try
            {
                staged = Directory.GetFiles(pending, "*.exe");
            }
            catch
            {
                facts.Updater = UpdaterState.Suspicious;
                return;
            }

            if (staged.Length == 0)
            {
                // A staging area with no installer is only suspicious when it still carries
                // metadata; an empty directory is normal between updates.
                facts.Updater = FileExists(Path.Combine(pending, "update-info.json"))
                    ? UpdaterState.Suspicious
                    : UpdaterState.Clean;
                return;
            }

            if (staged.Length > 1)
            {
                facts.Updater = UpdaterState.Suspicious;
                return;
            }

            facts.PendingExeName = Path.GetFileName(staged[0]);
            try { facts.PendingBytes = new FileInfo(staged[0]).Length; } catch { facts.PendingBytes = 0; }
            facts.PendingVersion = VersionFacts.Parse(facts.PendingExeName)
                ?? VersionFromInfo(ReadTextOrNull(Path.Combine(pending, "update-info.json")));
            facts.Updater = facts.PendingVersion == null ? UpdaterState.PendingUnknown : UpdaterState.PendingKnown;
        }

        static void CollectHolders(DiagFacts facts, string installDir)
        {
            string root = NormalizePath(installDir);

            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch { return; }

            foreach (Process process in processes)
            {
                try
                {
                    string path = TryGetPath(process);
                    if (path == null)
                    {
                        facts.UnreadableProcesses++;
                        continue;
                    }

                    string normalized = NormalizePath(path);
                    bool insideInstall = normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase);
                    bool sameName = string.Equals(process.ProcessName, "DeepSeek Harness", StringComparison.OrdinalIgnoreCase);
                    if (!insideInstall && !sameName) continue;

                    facts.Holders.Add(new HolderFact
                    {
                        Pid = process.Id,
                        Name = process.ProcessName,
                        Path = path,
                        InsideInstallDir = insideInstall,
                        IsBackend = insideInstall &&
                            normalized.IndexOf(BackendRuntimeFragment, StringComparison.OrdinalIgnoreCase) >= 0,
                    });
                }
                catch
                {
                    facts.UnreadableProcesses++;
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        static string TryGetPath(Process process)
        {
            try { return process.MainModule != null ? process.MainModule.FileName : null; }
            catch { return null; }
        }

        static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            try { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
            catch { return path.TrimEnd('\\', '/'); }
        }

        static bool DirectoryExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && Directory.Exists(path); } catch { return false; }
        }

        static bool FileExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path); } catch { return false; }
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

        static readonly Regex InfoVersionPattern = new Regex(
            "\"(?:version|tag|name)\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        static string VersionFromInfo(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Match match = InfoVersionPattern.Match(json);
            return match.Success ? VersionFacts.Parse(match.Groups[1].Value) : null;
        }
    }
}
