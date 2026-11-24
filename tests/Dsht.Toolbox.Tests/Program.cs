using System;
using System.Collections.Generic;
using Dsht.Toolbox.Core.Diag;

namespace Dsht.Toolbox.Tests
{
    /// <summary>零依赖断言运行器：每条断言都能失败（实现改坏即红）。</summary>
    internal static class Program
    {
        static int pass, fail;

        static void Check(string name, bool ok)
        {
            if (ok) { pass++; Console.WriteLine("  [PASS] " + name); }
            else { fail++; Console.WriteLine("  [FAIL] " + name); }
        }

        static int Main()
        {
            Console.WriteLine("== dsht tests ==");

            Console.WriteLine("-- VersionFacts");
            Check("解析 rc 版本", VersionFacts.Parse("deepseek-harness-0.2.2-rc.5-win-x64.exe") == "0.2.2-rc.5");
            Check("解析纯版本", VersionFacts.Parse("0.2.2") == "0.2.2");
            Check("无版本返回 null", VersionFacts.Parse("no-version-here") == null);
            Check("单段数字不算版本", VersionFacts.Parse("v5") == null);
            Check("比较：rc.5 > rc.4", VersionFacts.Compare("0.2.2-rc.5", "0.2.2-rc.4") == 1);
            Check("比较：0.2.2 > 0.2.2-rc.5", VersionFacts.Compare("0.2.2", "0.2.2-rc.5") == 1);
            Check("比较：相等", VersionFacts.Compare("0.2.1-rc.3", "0.2.1-rc.3") == 0);
            Check("比较：不可解析返回 null", VersionFacts.Compare("x", "0.2.2") == null);
            Check("规范化：0.2.2.0 == 0.2.2", VersionFacts.Compare("0.2.2.0", "0.2.2") == 0);
            Check("规范化：0.2.2.0-rc.5 解析", VersionFacts.Parse("0.2.2.0-rc.5") == "0.2.2-rc.5");
            Check("预发行数值比较：rc.10 > rc.9", VersionFacts.Compare("0.2.2-rc.10", "0.2.2-rc.9") == 1);
            Check("SameCore：0.2.2-rc.5 与 0.2.2.0", VersionFacts.SameCore("0.2.2-rc.5", "0.2.2.0"));
            Check("SameCore：0.2.1 与 0.2.2 为 false", !VersionFacts.SameCore("0.2.1", "0.2.2"));

            Console.WriteLine("-- RemedyPlanner: 安装缺失");
            var f1 = new DiagFacts { InstallDirExists = false };
            var p1 = RemedyPlanner.Plan(f1);
            Check("缺安装 ⇒ block", HasFinding(p1, "install-missing", FindingLevel.Block));
            Check("缺安装 ⇒ 健康=false", p1.Healthy == false);
            Check("缺安装 ⇒ 建议重装", HasAction(p1, ActionKind.Reinstall));

            Console.WriteLine("-- RemedyPlanner: 进程占用");
            var f2 = Healthy();
            f2.Holders.Add(new HolderFact { Pid = 1, Name = "DeepSeek Harness", Path = f2.InstallDir + "\\DeepSeek Harness.exe", InsideInstallDir = true });
            f2.Holders.Add(new HolderFact { Pid = 2, Name = "node", Path = f2.InstallDir + "\\resources\\runtime\\node.exe", InsideInstallDir = true, IsBackend = true });
            var p2 = RemedyPlanner.Plan(f2);
            Check("占用 ⇒ holders 发现", HasFinding(p2, "holders", FindingLevel.Warn));
            Check("占用 ⇒ 计划结束进程且标记危险", HasAction(p2, ActionKind.StopHolders) && DangerOf(p2, ActionKind.StopHolders));
            Check("占用 ⇒ 收集到 2 个 pid", PidsOf(p2, ActionKind.StopHolders).Count == 2);

            Console.WriteLine("-- RemedyPlanner: pending");
            var f3 = Healthy();
            f3.Updater = UpdaterState.PendingKnown;
            f3.PendingVersion = "0.2.2-rc.5";
            f3.ProductVersion = "0.2.1-rc.3";
            f3.PendingExeName = "deepseek-harness-0.2.2-rc.5-win-x64.exe";
            var p3 = RemedyPlanner.Plan(f3);
            Check("pending 新于已装 ⇒ 发现", HasFinding(p3, "updater-pending", FindingLevel.Warn));
            Check("pending ⇒ 计划清理缓存", HasAction(p3, ActionKind.ClearUpdaterPending));

            var f4 = Healthy();
            f4.Updater = UpdaterState.PendingKnown;
            f4.PendingVersion = "0.2.2-rc.5";
            f4.ProductVersion = "0.2.2-rc.5";
            Check("pending 等于已装 ⇒ 判为重复", HasFinding(RemedyPlanner.Plan(f4), "version-equal", FindingLevel.Info));

            var f4b = Healthy();
            f4b.Updater = UpdaterState.PendingKnown;
            f4b.PendingVersion = "0.2.1-rc.3";
            f4b.ProductVersion = "0.2.2";
            Check("pending 旧于已装 ⇒ version-older", HasFinding(RemedyPlanner.Plan(f4b), "version-older", FindingLevel.Info));

            var f4c = Healthy();
            f4c.Updater = UpdaterState.PendingKnown;
            f4c.PendingVersion = "0.2.2-rc.5";
            f4c.ProductVersion = "0.2.2.0";
            var p4c = RemedyPlanner.Plan(f4c);
            Check("同核心 ⇒ 不断言新旧，报 same-core", HasFinding(p4c, "version-same-core", FindingLevel.Info));
            Check("同核心 ⇒ 不得出现 version-older", !HasFinding(p4c, "version-older", FindingLevel.Info));

            var f5 = Healthy();
            f5.Updater = UpdaterState.Suspicious;
            Check("可疑残留 ⇒ 计划清理", HasAction(RemedyPlanner.Plan(f5), ActionKind.ClearUpdaterPending));

            Console.WriteLine("-- RemedyPlanner: 健康");
            var p6 = RemedyPlanner.Plan(Healthy());
            Check("健康 ⇒ healthy=true", p6.Healthy);
            Check("有告警 ⇒ healthy=false（不能把“该修”报成健康）", RemedyPlanner.Plan(f2).Healthy == false);
            Check("健康 ⇒ 无动作（Nothing）", p6.Actions.Count == 1 && p6.Actions[0].Kind == ActionKind.Nothing);

            Console.WriteLine("-- ReportRenderer");
            string human = ReportRenderer.Human(f2, p2);
            Check("人类文本含 holders", human.Contains("holders"));
            Check("人类文本含危险标记", human.Contains("--yes"));
            Check("有告警时结论为“待修”而不是“阻塞”", human.Contains("待修") && !human.Contains("阻塞"));
            Check("干净时结论为“干净”", ReportRenderer.Human(Healthy(), RemedyPlanner.Plan(Healthy())).Contains("结论：干净"));
            string json = ReportRenderer.Json(f2, p2);
            Check("JSON 可闭合", json.StartsWith("{") && json.EndsWith("}"));
            Check("JSON 含 healthy 字段", json.Contains("\"healthy\":"));
            Check("JSON 转义引号", ReportRenderer.Json(new DiagFacts { InstallDir = "a\"b" }, RemedyPlanner.Plan(new DiagFacts { InstallDir = "a\"b" })).Contains("\\\""));

            Console.WriteLine();
            Console.WriteLine("== " + pass + " passed, " + fail + " failed ==");
            return fail == 0 ? 0 : 1;
        }

        static DiagFacts Healthy()
        {
            var f = new DiagFacts();
            f.InstallDir = "C:\\Apps\\DeepSeek Harness";
            f.UpdaterDir = "C:\\Updater";
            f.InstallDirExists = true;
            f.MainExeExists = true;
            f.UninstallerExists = true;
            f.ProductVersion = "0.2.2-rc.5";
            f.Updater = UpdaterState.Clean;
            return f;
        }

        static bool HasFinding(DiagPlan p, string code, FindingLevel lvl)
        {
            foreach (var x in p.Findings) if (x.Code == code && x.Level == lvl) return true;
            return false;
        }

        static bool HasAction(DiagPlan p, ActionKind k)
        {
            foreach (var a in p.Actions) if (a.Kind == k) return true;
            return false;
        }

        static bool DangerOf(DiagPlan p, ActionKind k)
        {
            foreach (var a in p.Actions) if (a.Kind == k) return a.Dangerous;
            return false;
        }

        static List<int> PidsOf(DiagPlan p, ActionKind k)
        {
            foreach (var a in p.Actions) if (a.Kind == k) return a.Pids;
            return new List<int>();
        }
    }
}