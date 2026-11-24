using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Dsht.Toolbox.Core.Diag;

namespace Dsht.Toolbox.Commands
{
    /// <summary>执行修复动作。每个动作打印做了什么；任何一步失败即停止并如实报错。</summary>
    internal static class RemedyRunner
    {
        public static int Execute(DiagFacts f, DiagPlan plan)
        {
            int done = 0;
            foreach (var a in plan.Actions)
            {
                if (a.Kind == ActionKind.Nothing) { Console.WriteLine("无需修复。"); return ExitCodes.Ok; }
                Console.WriteLine("[" + (done + 1) + "] " + a.Kind + " — " + a.Why);
                bool ok;
                switch (a.Kind)
                {
                    case ActionKind.StopHolders: ok = StopHolders(a.Pids); break;
                    case ActionKind.ClearUpdaterPending: ok = ClearPending(f.UpdaterDir); break;
                    case ActionKind.RunInstallerFromTemp: ok = RunInstaller(f, a.Argument); break;
                    case ActionKind.Reinstall: Console.WriteLine("    需要重装：安装目录不可修复。"); ok = false; break;
                    default: ok = true; break;
                }
                if (!ok)
                {
                    Console.Error.WriteLine("    失败，已停止（未完成后续动作）。");
                    return ExitCodes.Failed;
                }
                done++;
            }

            // 修完复检：重新采集事实，证明状态确实变了
            DiagFacts after = Windows.DesktopProbe.Collect(f.InstallDir, f.UpdaterDir);
            DiagPlan afterPlan = RemedyPlanner.Plan(after);
            Console.WriteLine();
            Console.WriteLine("复检：holders=" + after.Holders.Count + " updater=" + after.Updater + " healthy=" + (afterPlan.Healthy ? "true" : "false"));
            return afterPlan.Healthy ? ExitCodes.Ok : ExitCodes.Failed;
        }

        static bool StopHolders(List<int> pids)
        {
            bool all = true;
            foreach (int pid in pids)
            {
                try
                {
                    using (var p = Process.GetProcessById(pid))
                    {
                        // 先尝试优雅关闭，再强杀
                        bool closed = false;
                        try { closed = p.CloseMainWindow(); } catch { }
                        if (closed && p.WaitForExit(5000)) { Console.WriteLine("    pid " + pid + " 已优雅退出"); continue; }
                        p.Kill(true);
                        p.WaitForExit(10000);
                        Console.WriteLine("    pid " + pid + " 已结束");
                    }
                }
                catch (ArgumentException) { Console.WriteLine("    pid " + pid + " 已不存在"); }
                catch (Exception ex) { Console.WriteLine("    pid " + pid + " 结束失败: " + ex.GetType().Name); all = false; }
            }
            Thread.Sleep(1500); // 等句柄真正释放
            return all;
        }

        static bool ClearPending(string updaterDir)
        {
            string pending = Path.Combine(updaterDir, "pending");
            try
            {
                if (Directory.Exists(pending))
                {
                    Directory.Delete(pending, true);
                    Console.WriteLine("    已删除 pending 目录");
                }
                else Console.WriteLine("    pending 不存在，跳过");
                string blockmap = Path.Combine(updaterDir, "current.blockmap");
                if (File.Exists(blockmap)) File.Delete(blockmap);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("    清理失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>社区验证的绕法：安装器必须在 %TEMP% 作为工作目录运行，否则它会去重命名自己的 cwd。</summary>
        static bool RunInstaller(DiagFacts f, string exeName)
        {
            string pending = Path.Combine(f.UpdaterDir, "pending");
            string exe = Path.Combine(pending, exeName ?? "");
            if (!File.Exists(exe))
            {
                string[] cands = Directory.Exists(pending) ? Directory.GetFiles(pending, "*.exe") : new string[0];
                if (cands.Length == 0) { Console.Error.WriteLine("    找不到待装安装器"); return false; }
                exe = cands[0];
            }
            string work = Path.GetTempPath();
            Console.WriteLine("    installer: " + Path.GetFileName(exe));
            Console.WriteLine("    workdir  : " + work + "  （关键：不能是安装目录）");
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    WorkingDirectory = work,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("--updated");
                psi.ArgumentList.Add("--force-run");
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    Console.WriteLine("    安装器已启动（pid " + p.Id + "），等待退出…");
                    if (!p.WaitForExit(15 * 60 * 1000))
                    {
                        Console.Error.WriteLine("    安装器 15 分钟未结束；请人工检查。");
                        return false;
                    }
                    Console.WriteLine("    安装器退出码 " + p.ExitCode);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("    启动失败: " + ex.Message);
                return false;
            }
        }
    }
}
