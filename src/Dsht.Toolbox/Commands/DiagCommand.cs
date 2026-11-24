using System;
using Dsht.Toolbox.Core.Diag;
using Dsht.Toolbox.Windows;

namespace Dsht.Toolbox.Commands
{
    /// <summary>diag：诊断桌面端安装/更新故障。默认只读；--fix 才写。</summary>
    internal static class DiagCommand
    {
        public static int Run(string[] args)
        {
            bool json = Has(args, "--json");
            bool fix = Has(args, "--fix");
            bool yes = Has(args, "--yes");
            string installDir = Arg(args, "--dir") ?? DesktopProbe.DefaultInstallDir();
            string updaterDir = Arg(args, "--updater") ?? DesktopProbe.DefaultUpdaterDir();

            DiagFacts f = DesktopProbe.Collect(installDir, updaterDir);
            DiagPlan plan = RemedyPlanner.Plan(f);

            if (json) Console.WriteLine(ReportRenderer.Json(f, plan));
            else Console.Write(ReportRenderer.Human(f, plan));

            // 只读模式：干净=0，有问题=1（可用脚本判断），有阻塞也=1
            if (!fix) return plan.Healthy ? ExitCodes.Ok : ExitCodes.Failed;

            if (HasDangerous(plan) && !yes)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("有需要确认的动作（会结束进程/重跑安装器）。确认请加 --yes：dsht diag --fix --yes");
                return ExitCodes.Usage;
            }

            Console.WriteLine();
            return RemedyRunner.Execute(f, plan);
        }

        static bool HasDangerous(DiagPlan p)
        {
            foreach (var a in p.Actions) if (a.Dangerous) return true;
            return false;
        }

        internal static bool Has(string[] a, string k)
        {
            foreach (var x in a) if (string.Equals(x, k, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string Arg(string[] a, string k)
        {
            for (int i = 0; i < a.Length - 1; i++) if (string.Equals(a[i], k, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return null;
        }
    }
}