using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Dsht.Toolbox.Core.Remedy;

namespace Dsht.Toolbox.Windows
{
    /// <summary>
    /// The real implementation of <see cref="IRemedyExecutor"/>. Every method reports what it
    /// did to the log and returns false on the first unrecoverable failure, so the caller can
    /// stop instead of piling more changes onto a broken state.
    /// </summary>
    internal sealed class WindowsRemedyExecutor : IRemedyExecutor
    {
        const int GracefulExitTimeoutMs = 8000;
        const int ForcedExitTimeoutMs = 10000;
        const int HandleReleaseSettleMs = 1200;
        const int InstallerTimeoutMs = 15 * 60 * 1000;

        public bool StopHolders(IReadOnlyList<int> pids, TextWriter log)
        {
            bool allClosed = true;

            foreach (int pid in pids)
            {
                try
                {
                    using (Process process = Process.GetProcessById(pid))
                    {
                        if (TryCloseGracefully(process, log)) continue;
                        if (TryKill(process, log)) continue;
                        allClosed = false;
                    }
                }
                catch (ArgumentException)
                {
                    log.WriteLine("    pid " + pid + " already gone");
                }
                catch (Exception ex)
                {
                    log.WriteLine("    pid " + pid + " could not be closed: " + ex.GetType().Name);
                    allClosed = false;
                }
            }

            // Windows releases file handles a moment after process exit; a rename attempted
            // immediately afterwards still fails with sharing violation 32.
            if (allClosed) Thread.Sleep(HandleReleaseSettleMs);
            return allClosed;
        }

        static bool TryCloseGracefully(Process process, TextWriter log)
        {
            try
            {
                if (!process.CloseMainWindow()) return false;
                if (!process.WaitForExit(GracefulExitTimeoutMs)) return false;
                log.WriteLine("    pid " + process.Id + " closed gracefully");
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool TryKill(Process process, TextWriter log)
        {
            try
            {
                process.Kill(true);
                if (!process.WaitForExit(ForcedExitTimeoutMs))
                {
                    log.WriteLine("    pid " + process.Id + " did not exit after kill");
                    return false;
                }
                log.WriteLine("    pid " + process.Id + " terminated");
                return true;
            }
            catch (Exception ex)
            {
                log.WriteLine("    pid " + process.Id + " kill failed: " + ex.GetType().Name);
                return false;
            }
        }

        public bool ClearUpdaterPending(string updaterDir, TextWriter log)
        {
            string pending = Path.Combine(updaterDir, "pending");
            try
            {
                if (Directory.Exists(pending))
                {
                    Directory.Delete(pending, true);
                    log.WriteLine("    removed " + pending);
                }
                else
                {
                    log.WriteLine("    nothing staged");
                }

                string blockmap = Path.Combine(updaterDir, "current.blockmap");
                if (File.Exists(blockmap)) File.Delete(blockmap);
                return true;
            }
            catch (Exception ex)
            {
                log.WriteLine("    could not clear the staging area: " + ex.Message);
                return false;
            }
        }

        public bool RunInstallerFromTemp(string updaterDir, string exeName, TextWriter log)
        {
            string installer = ResolveInstaller(updaterDir, exeName);
            if (installer == null)
            {
                log.WriteLine("    no staged installer found");
                return false;
            }

            string workingDirectory = Path.GetTempPath();
            log.WriteLine("    installer: " + Path.GetFileName(installer));
            log.WriteLine("    workdir  : " + workingDirectory + " (must not be the install directory)");

            try
            {
                var startInfo = new ProcessStartInfo(installer)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add("--updated");
                startInfo.ArgumentList.Add("--force-run");

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null) return false;
                    log.WriteLine("    installer started (pid " + process.Id + "), waiting...");
                    if (!process.WaitForExit(InstallerTimeoutMs))
                    {
                        log.WriteLine("    installer did not finish within 15 minutes");
                        return false;
                    }
                    log.WriteLine("    installer exited with " + process.ExitCode);
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                log.WriteLine("    could not start the installer: " + ex.Message);
                return false;
            }
        }

        static string ResolveInstaller(string updaterDir, string exeName)
        {
            string pending = Path.Combine(updaterDir, "pending");
            if (!Directory.Exists(pending)) return null;

            if (!string.IsNullOrEmpty(exeName))
            {
                string named = Path.Combine(pending, exeName);
                if (File.Exists(named)) return named;
            }

            string[] candidates = Directory.GetFiles(pending, "*.exe");
            return candidates.Length > 0 ? candidates[0] : null;
        }
    }
}
