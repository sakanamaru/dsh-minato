using System;
using System.Collections.Generic;

namespace Dsht.Toolbox.Core.Diag
{
    public enum FindingLevel { Ok, Info, Warn, Block }

    /// <summary>一条发现（纯数据）。</summary>
    public sealed class Finding
    {
        public FindingLevel Level;
        public string Code = "";     // 稳定机器码，用于测试与文档
        public string Title = "";
        public string Detail = "";
    }

    public enum ActionKind
    {
        StopHolders = 0,
        ClearUpdaterPending = 1,
        RunInstallerFromTemp = 2,
        Reinstall = 3,
        Nothing = 4,
    }

    /// <summary>一条修复动作（有序；纯数据，由 --fix 执行）。</summary>
    public sealed class RemedyAction
    {
        public ActionKind Kind;
        public string Why = "";
        public bool Dangerous;   // true ⇒ 必须额外确认
        public List<int> Pids = new List<int>();
        public string Argument = "";
    }

    public sealed class DiagPlan
    {
        public List<Finding> Findings = new List<Finding>();
        public List<RemedyAction> Actions = new List<RemedyAction>();
        public bool Healthy;
    }

    /// <summary>事实 → 发现 + 修复计划。纯函数：同输入同输出，无 IO 无时钟。</summary>
    public static class RemedyPlanner
    {
        public static DiagPlan Plan(DiagFacts f)
        {
            var plan = new DiagPlan();
            if (f == null) { plan.Healthy = false; Add(plan, FindingLevel.Block, "no-facts", "没有读到任何事实", "请以管理员身份重试"); return plan; }

            // 1) 安装目录
            if (!f.InstallDirExists)
            {
                Add(plan, FindingLevel.Block, "install-missing", "安装目录不存在", f.InstallDir);
                plan.Actions.Add(new RemedyAction { Kind = ActionKind.Reinstall, Why = "没有安装可修，只能重装" });
                plan.Healthy = false;
                return plan;
            }
            if (!f.MainExeExists) Add(plan, FindingLevel.Block, "main-exe-missing", "主程序缺失", "DeepSeek Harness.exe 不在安装目录");
            else Add(plan, FindingLevel.Ok, "main-exe", "主程序存在", null);

            if (!f.UninstallerExists) Add(plan, FindingLevel.Warn, "uninstaller-missing", "卸载器缺失", "可能被部分覆盖安装破坏");

            // 2) 进程占用（error 32/5 的直接原因）
            int inside = 0, backend = 0;
            var pids = new List<int>();
            foreach (var h in f.Holders)
            {
                if (!h.InsideInstallDir) continue;
                inside++;
                pids.Add(h.Pid);
                if (h.IsBackend) backend++;
            }
            if (inside > 0)
            {
                Add(plan, FindingLevel.Warn, "holders", "有 " + inside + " 个进程占用安装目录",
                    "这是更新/覆盖安装失败（Windows error 32/5）的直接原因；其中后端 node 进程 " + backend + " 个");
                plan.Actions.Add(new RemedyAction { Kind = ActionKind.StopHolders, Why = "释放安装目录上的文件句柄", Dangerous = true, Pids = pids });
            }
            else Add(plan, FindingLevel.Ok, "no-holders", "没有进程占用安装目录", null);

            if (f.UnreadableProcesses > 0)
                Add(plan, FindingLevel.Info, "unreadable-procs", "有 " + f.UnreadableProcesses + " 个进程路径读不到", "权限不足；不代表没占用，管理员身份更准");

            // 3) updater 状态
            switch (f.Updater)
            {
                case UpdaterState.None:
                    Add(plan, FindingLevel.Info, "updater-none", "没有 updater 缓存（从未检查更新）", null);
                    break;
                case UpdaterState.Clean:
                    Add(plan, FindingLevel.Ok, "updater-clean", "updater 缓存干净", null);
                    break;
                case UpdaterState.PendingKnown:
                case UpdaterState.PendingUnknown:
                    {
                        string detail = "待装: " + (string.IsNullOrEmpty(f.PendingExeName) ? "(未知)" : f.PendingExeName);
                        int? cmp = VersionFacts.Compare(f.PendingVersion, f.ProductVersion);
                        if (cmp.HasValue && cmp.Value > 0)
                            detail += "；比已装版本新（已装 " + (f.ProductVersion ?? "未知") + "，待装 " + f.PendingVersion + "）";
                        else if (VersionFacts.SameCore(f.PendingVersion, f.ProductVersion))
                            detail += "；与已装版本同核心（已装读数为 " + (f.ProductVersion ?? "未知") + "），属重复/残留";
                        else if (cmp.HasValue && cmp.Value == 0)
                            detail += "；与已装版本相同（已装 " + (f.ProductVersion ?? "未知") + "），属重复/残留";
                        else if (!cmp.HasValue)
                            detail += "；版本无法比较（已装 " + (f.ProductVersion ?? "未知") + "）";
                        else
                            detail += "；不新于已装版本，属残留";
                        Add(plan, FindingLevel.Warn, "updater-pending", "updater 里有待装安装包", detail);
                        plan.Actions.Add(new RemedyAction { Kind = ActionKind.ClearUpdaterPending, Why = "残留 pending 会让下次更新反复失败", Dangerous = false });
                        if (f.InstallerInRoot)
                            plan.Actions.Add(new RemedyAction { Kind = ActionKind.RunInstallerFromTemp, Why = "用 %TEMP% 作为工作目录静默重跑已下载的安装器（社区验证的绕法）", Dangerous = true, Argument = f.PendingExeName ?? "" });
                        break;
                    }
                case UpdaterState.Suspicious:
                    Add(plan, FindingLevel.Warn, "updater-suspicious", "updater 缓存有可疑残留", "pending 不完整或根目录残留 installer.exe");
                    plan.Actions.Add(new RemedyAction { Kind = ActionKind.ClearUpdaterPending, Why = "清掉不完整缓存，避免更新反复卡住", Dangerous = false });
                    break;
            }

            // 4) 版本一致性
            if (!string.IsNullOrEmpty(f.ProductVersion) && !string.IsNullOrEmpty(f.PendingVersion))
            {
                int? cmp2 = VersionFacts.Compare(f.PendingVersion, f.ProductVersion);
                if (cmp2.HasValue && cmp2.Value == 0)
                    Add(plan, FindingLevel.Info, "version-equal", "待装版本等于已装版本", "这次更新是重复的，可直接清 pending");
                else if (VersionFacts.SameCore(f.PendingVersion, f.ProductVersion))
                {
                    Add(plan, FindingLevel.Info, "version-same-core", "待装与已装是同一版本核心",
                        "已装版本只能读到数字部分（" + f.ProductVersion + "），预发行后缀不可知 ⇒ 不做新旧断言；pending 可安全清理");
                }
                else if (false)
                    Add(plan, FindingLevel.Info, "version-equal", "待装版本等于已装版本", "这次更新是重复的，可直接清 pending");
                else if (cmp2.HasValue && cmp2.Value < 0)
                    Add(plan, FindingLevel.Info, "version-older", "待装版本旧于已装版本", "pending 是旧残留，清理后不会再触发它");
            }

            plan.Healthy = noProblems(plan);
            if (plan.Healthy && plan.Actions.Count == 0) plan.Actions.Add(new RemedyAction { Kind = ActionKind.Nothing, Why = "没有需要修的东西" });
            return plan;
        }

        /// <summary>“健康” = 没有 Block 也没有 Warn（即：没有需要动手的东西）。</summary>
        static bool noProblems(DiagPlan p)
        {
            foreach (var x in p.Findings)
                if (x.Level == FindingLevel.Block || x.Level == FindingLevel.Warn) return false;
            return true;
        }

        static void Add(DiagPlan p, FindingLevel lvl, string code, string title, string detail)
        {
            p.Findings.Add(new Finding { Level = lvl, Code = code, Title = title, Detail = detail ?? "" });
        }
    }
}