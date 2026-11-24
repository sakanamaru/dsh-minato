using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Dsht.Toolbox.Core.Diag;

namespace Dsht.Toolbox.Windows
{
    /// <summary>IO 层：只负责读事实，不做推理。所有异常都转成事实（不抛）。</summary>
    internal static class DesktopProbe
    {
        public static string DefaultInstallDir()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Programs", "DeepSeek Harness");
        }

        public static string DefaultUpdaterDir()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "@deepseek-aidsh-desktop-updater");
        }

        public static DiagFacts Collect(string installDir, string updaterDir)
        {
            var f = new DiagFacts();
            f.InstallDir = installDir;
            f.UpdaterDir = updaterDir;
            f.InstallDirExists = SafeDirExists(installDir);
            string mainExe = Path.Combine(installDir, "DeepSeek Harness.exe");
            f.MainExeExists = SafeFileExists(mainExe);
            f.UninstallerExists = SafeFileExists(Path.Combine(installDir, "Uninstall DeepSeek Harness.exe"));
            f.ElectronVersion = ReadAllTextOrNull(Path.Combine(installDir, "version"));
            f.ProductVersion = FileVersionOrNull(mainExe);

            // updater
            string pending = Path.Combine(updaterDir, "pending");
            f.InstallerInRoot = SafeFileExists(Path.Combine(updaterDir, "installer.exe"));
            if (!SafeDirExists(updaterDir)) f.Updater = UpdaterState.None;
            else
            {
                f.Updater = UpdaterState.Clean;
                string pendingExe = null;
                if (SafeDirExists(pending))
                {
                    try
                    {
                        var cands = Directory.GetFiles(pending, "*.exe");
                        if (cands.Length > 0) { pendingExe = cands[0]; f.PendingBytes = new FileInfo(pendingExe).Length; }
                        if (cands.Length > 1) f.Updater = UpdaterState.Suspicious;
                    }
                    catch { f.Updater = UpdaterState.Suspicious; }
                    string info = Path.Combine(pending, "update-info.json");
                    string infoText = ReadAllTextOrNull(info);
                    if (pendingExe != null)
                    {
                        f.PendingExeName = Path.GetFileName(pendingExe);
                        f.PendingVersion = VersionFacts.Parse(f.PendingExeName);
                        if (f.PendingVersion == null) f.PendingVersion = VersionFromInfo(infoText);
                        f.Updater = f.PendingVersion == null ? UpdaterState.PendingUnknown : UpdaterState.PendingKnown;
                    }
                    else if (infoText != null) f.Updater = UpdaterState.Suspicious;
                }
                else if (f.InstallerInRoot) f.Updater = UpdaterState.Suspicious;
            }

            // 进程
            CollectHolders(installDir, f);
            return f;
        }

        static void CollectHolders(string installDir, DiagFacts f)
        {
            string full = Normalize(installDir);
            Process[] all;
            try { all = Process.GetProcesses(); } catch { return; }
            foreach (var p in all)
            {
                try
                {
                    string path = null;
                    try { path = p.MainModule != null ? p.MainModule.FileName : null; } catch { path = null; }
                    if (path == null) { f.UnreadableProcesses++; continue; }
                    string norm = Normalize(path);
                    bool inside = norm.StartsWith(full, StringComparison.OrdinalIgnoreCase);
                    bool sameName = string.Equals(p.ProcessName, "DeepSeek Harness", StringComparison.OrdinalIgnoreCase);
                    if (!inside && !sameName) continue;
                    f.Holders.Add(new HolderFact
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        Path = path,
                        InsideInstallDir = inside,
                        IsBackend = inside && norm.IndexOf("\\resources\\runtime\\", StringComparison.OrdinalIgnoreCase) >= 0,
                    });
                }
                catch { f.UnreadableProcesses++; }
                finally { try { p.Dispose(); } catch { } }
            }
        }

        static string Normalize(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { return Path.GetFullPath(p).TrimEnd('\\', '/'); } catch { return p.TrimEnd('\\', '/'); }
        }

        static bool SafeDirExists(string p) { try { return !string.IsNullOrEmpty(p) && Directory.Exists(p); } catch { return false; } }
        static bool SafeFileExists(string p) { try { return !string.IsNullOrEmpty(p) && File.Exists(p); } catch { return false; } }

        static string ReadAllTextOrNull(string p)
        {
            try { return File.Exists(p) ? File.ReadAllText(p).Trim() : null; } catch { return null; }
        }

        static string FileVersionOrNull(string p)
        {
            try
            {
                if (!File.Exists(p)) return null;
                var v = FileVersionInfo.GetVersionInfo(p);
                return string.IsNullOrEmpty(v.ProductVersion) ? v.FileVersion : v.ProductVersion;
            }
            catch { return null; }
        }

        static readonly Regex InfoVer = new Regex("\"(?:version|tag|name)\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        static string VersionFromInfo(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var m = InfoVer.Match(json);
            if (!m.Success) return null;
            return VersionFacts.Parse(m.Groups[1].Value);
        }
    }
}
