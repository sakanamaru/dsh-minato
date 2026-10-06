using System;
using System.Collections.Generic;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;
using Dsht.Domain.Services;

namespace Dsht.Cli
{
    /// <summary>体检与 profile 相关（doctor / bootdiag / profiles / profilecheck / profilepatch）✓。
    /// 架构审计（S1）：从 Program.cs 原样搬出来，逻辑一行没改 ✓
    /// 边界用括号配平找，逆序删除避免行号漂移 ✓</summary>
    public static partial class Program
    {
        private static int ProfileCheck(string[] args, ServiceRegistry reg)
        {
            string dir = Flag(args, "--dir");
            string one = Flag(args, "--file");
            bool vendor = Has(args, "--vendor");
            bool abs = Has(args, "--abs");
            IProfileSource src = reg.Get<IProfileSource>();
            List<ProfileFinding> fs = new List<ProfileFinding>();
            int files = 0, skipped = 0, readErrors = 0;
            if (!string.IsNullOrEmpty(one))
            {
                ProfileFile pf = src.ReadSingle(one, abs);
                if (pf != null) { files = 1; fs.AddRange(ProfileScanner.Scan(pf.Text, pf.Label, FileExists)); }
            }
            else
            {
                ProfileCollection col = src.CollectDirectory(dir, vendor, abs);
                skipped = col.SkippedVendor;
                readErrors = col.ReadErrors;
                foreach (ProfileFile pf in col.Files)
                {
                    files++;
                    fs.AddRange(ProfileScanner.Scan(pf.Text, pf.Label, FileExists));
                }
            }
            foreach (ProfileFinding f in fs)
                Console.WriteLine("PROFILECHK_WARN " + f.File + " " + f.Line + " " + f.Id + " " + f.Missing + " " + f.Hint);
            Console.WriteLine("PROFILECHK_TOTAL " + fs.Count + " " + files);
            if (skipped > 0) Console.WriteLine("PROFILECHK_SKIPPED_VENDOR " + skipped);
            if (readErrors > 0 && Has(args, "--diag")) Console.WriteLine("PROFILECHK_READ_ERRORS " + readErrors);
            if (abs)
            {
                foreach (ProfileFinding f in fs)
                    if (f.Missing == "maxDepth") Console.WriteLine("PROFILECHK_FIX " + f.File + "|" + f.Line + "|" + f.Id + "|" + f.Missing);
            }
            // 只有"确实扫过且没有任何发现"才说 OK ✗：读不到目录时结果不完整，必须如实说明 ✓
            // OK 的抑制是**无条件**的 ✓：只要读错误 > 0，就不许说"没有问题" ✗（这跟 --diag 无关 —— 我一度把它一起 gated 了，回归测试立刻抓到 ✗）。
            if (fs.Count == 0 && readErrors == 0) Console.WriteLine("PROFILECHK_OK");
            else if (readErrors > 0 && Has(args, "--diag")) Console.WriteLine("PROFILECHK_INCOMPLETE " + T("有目录读不到，本次结果不完整 —— 不要当作「没有问题」", "some directories could not be read; this result is incomplete - do not read it as no problems"));
            return 0;
        }
        /// <summary>profiles（V3 独有）：列出 profile、它们的**配置形态**与插件清单。
        /// 数据来源：`<数据根>/profiles/<name>/package.json` 里的 `dsh.profile.bundles`（明文小 JSON，只读零注入）。
        /// 标记行：
        ///   `PROFILES_OK <n>` / `PROFILE <name> form=<web|headless|acp|unknown|unparsed> bundles=<n> thirdparty=<m>`
        ///   / `BUNDLE <profile> <bundle-id> <official|thirdparty>` / `PROFILES_FAIL <原因>`
        /// **诚实边界**：这是**配置形态**（manifest 里启用了哪个 app bundle），**不是运行形态**——
        /// "dsh 在跑"仍必须由端口/进程等运行时事实判断（见 describe/status）。</summary>
        private static int Profiles(ServiceRegistry reg)
        {
            IProfileManifestSource src = reg.Get<IProfileManifestSource>();
            if (!reg.Get<IFileSystemQuery>().DirectoryExists(src.ProfilesRoot))
            {
                Console.WriteLine("PROFILES_FAIL " + T("找不到 profiles 目录", "profiles directory not found"));
                return 0;
            }
            string[] names = src.ListProfiles();
            List<string[]> rows = new List<string[]>();
            List<string[]> bundles = new List<string[]>();
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase)) continue;   // 插件安装目录，不是 profile
                string manifestText = src.ReadManifest(name);
                if (manifestText == null) continue;                    // 没有 package.json → 不是 profile（诚实跳过，不报噪声）
                ProfileManifestInfo info = ProfileManifest.Parse(manifestText);
                string form = info.Parsed ? ProfileManifest.FormName(info.ConfiguredForm) : "unparsed";
                rows.Add(new string[] { name, form, info.Bundles.Length.ToString(), info.ThirdPartyPlugins.Length.ToString() });
                for (int b = 0; b < info.Bundles.Length; b++)
                {
                    string id = info.Bundles[b];
                    bool official = id != null && id.StartsWith("@deepseek-ai/", StringComparison.Ordinal);
                    string ver = BundleVersion(src, name, id);
                    bundles.Add(new string[] { name, id, official ? "official" : "thirdparty", ver });
                }
            }
            Console.WriteLine("PROFILES_OK " + rows.Count);
            for (int i = 0; i < rows.Count; i++)
                Console.WriteLine("PROFILE " + rows[i][0] + " form=" + rows[i][1] + " bundles=" + rows[i][2] + " thirdparty=" + rows[i][3]);
            for (int i = 0; i < bundles.Count; i++)
                Console.WriteLine("BUNDLE " + bundles[i][0] + " " + bundles[i][1] + " " + bundles[i][2] + (bundles[i][3] == "" ? "" : " version=" + bundles[i][3]));
            // 被隔离的条目：profile 的 cordis.patch.yml 里 disabled: true（按行扫描，向前找最近的 id:；不猜 YAML 结构）
            for (int i = 0; i < rows.Count; i++)
            {
                string[] dis = DisabledEntries(src.ReadPatch(rows[i][0]));
                for (int k = 0; k < dis.Length; k++) Console.WriteLine("DISABLED " + rows[i][0] + " " + dis[k]);
            }
            return 0;
        }
        /// <summary>带"逐条发射"回调的 DocItem 容器 ✓（`doctor --stream` 专用 ✓）。
        ///   ★ 技巧：DoctorCollect 里的 `items.Add(...)` **一行都不用改** ✓✓
        ///     —— `Collection&lt;T&gt;.Add` 走 `InsertItem` 虚调用 ✓ 在这里拦下发一条打一条 ✓。</summary>
        private sealed class EmitDocList : System.Collections.ObjectModel.Collection<DocItem>
        {
            private readonly System.Action<DocItem> _emit;
            public EmitDocList(System.Action<DocItem> emit) { _emit = emit; }
            protected override void InsertItem(int index, DocItem item)
            {
                base.InsertItem(index, item);
                if (_emit != null) { try { _emit(item); } catch { } }
            }
        }

        private static int Doctor(string[] args, ServiceRegistry reg)
        {
            // ★★ --stream（2026-10-02，GUI 专用 ✓）：**边算边打印**条目行，汇总行挪到**最后** ✓✓
            //   —— 杀软式"列表先出来、逐行出结果"（用户建议 ✓）。慢项本来就在队尾：
            //     System/Harness/Service/Workspace/Backup 先出（亚秒级 ✓），npm registry 探测（最长 4s）倒数第二 ✓
            //   ★ 默认输出**逐字不变** ✓（汇总仍在最前 ✓）→ v2.x 标记契约 / compare_markers / 契约测试全不动 ✓✓
            //   两边条目行格式**完全一致**（`[OK|WARN|ERROR] 类别 描述` ✓）→ GUI 的解析器按前缀扫行、
            //     不挑位置（SummaryMarkers.ParseDoctor 对 DOCTOR_OK/WARN/ERROR 只认前缀 ✓）→ 流式文本可直接喂 ✓
            bool stream = Has(args, "--stream");
            if (stream) Console.WriteLine("DOCTOR_BEGIN");
            EmitDocList items = new EmitDocList(stream
                ? (System.Action<DocItem>)delegate(DocItem it) { Console.WriteLine("[" + DoctorSummary.Level(it.Level) + "] " + it.Cat + " " + it.Text); }
                : null);
            DoctorCollect(reg, items);   // 收集（流式时**逐条**随算随发 ✓）
            List<DocItem> list = new List<DocItem>(items);
            string summary = DoctorSummary.Summary(list);
            Console.WriteLine(summary);   // 流式：汇总在**末尾**；默认：在**最前** ✓
            if (!stream)
            {
                foreach (DocItem it in list)
                    Console.WriteLine("[" + DoctorSummary.Level(it.Level) + "] " + it.Cat + " " + it.Text);
            }

            string report = Flag(args, "--report");
            if (report == null) report = Flag(args, "-report");
            if (report != null)
            {
                string text = DoctorReport.Build(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), ToolkitVersion,
                    Environment.OSVersion.VersionString, list,
                    ConfigSummaryBuilder.Build(reg.Get<IConfigSource>().ReadConfig()),
                    LogSummaryBuilder.Build(reg.Get<ILogSource>().ReadLog()), summary);
                try
                {
                    System.IO.File.WriteAllText(report, text, new System.Text.UTF8Encoding(true));
                    Console.WriteLine("DOCTOR_REPORT " + report);
                }
                catch (Exception ex) { Console.WriteLine("DOCTOR_WRITE_FAIL " + ex.Message); }
            }
            return 0;
        }
        private static void DoctorCollect(ServiceRegistry reg, System.Collections.Generic.IList<DocItem> items)
        {
            IToolchainQuery tc = reg.Get<IToolchainQuery>();
            IFileSystemQuery fs = reg.Get<IFileSystemQuery>();
            IBackupSource bk = reg.Get<IBackupSource>();
            IHttpProbe http = reg.Get<IHttpProbe>();
            IProcessQuery proc = reg.Get<IProcessQuery>();
            IIntegritySource integ = reg.Get<IIntegritySource>();
            IPaths paths = reg.Get<IPaths>();
            ServiceReport sr = reg.Get<IServiceTarget>().Probe();

            items.Add(new DocItem("System", 0, OsName() + ": " + Environment.OSVersion.VersionString + " (" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + ")"));
            string node = tc.NodeVersion();
            if (string.IsNullOrWhiteSpace(node)) items.Add(new DocItem("System", 1, "Node.js 未找到（dsh 依赖 npm 安装）"));
            else items.Add(new DocItem("System", 0, "Node.js: " + node.Trim()));
            string npm = tc.NpmVersion();
            items.Add(new DocItem("System", string.IsNullOrWhiteSpace(npm) ? 1 : 0, string.IsNullOrWhiteSpace(npm) ? "npm 不可用" : "npm: " + npm.Trim()));

            string dsh = tc.WhichDsh();
            if (dsh == null) items.Add(new DocItem("Harness", 2, "dsh 未安装（交互菜单按 1 安装）"));
            else
            {
                items.Add(new DocItem("Harness", 0, "dsh 已安装: " + ReportSanitizer.Sanitize(dsh)));
                string dv = tc.DshVersion();
                if (string.IsNullOrWhiteSpace(dv)) items.Add(new DocItem("Harness", 1, "dsh --version 无输出"));
                else items.Add(new DocItem("Harness", 0, "dsh 版本: " + ReportSanitizer.Sanitize(dv.Trim().Replace("\r", " ").Replace("\n", " "))));
            }

            if (sr.State == ServiceState.Down)
            {
                // 桌面端在跑时**跳过 3080 那条** ✓（用户要求："体检里检测到 desktop 就跳过 3080 端口监测" ✓✓）
                // 理由：桌面端**不监听 3080**（实测走 19387 ✓）→ 那条 ERROR 说的是"web 服务没起" ✓
                //       但用户在用桌面端时它**必然**是 ERROR ✓ 会误导 ✗
                bool desktopRunning = false;
                try { desktopRunning = reg.Get<IProcessQuery>().AnyProcessNamed("DeepSeek Harness"); } catch { }
                if (!desktopRunning)
                    items.Add(new DocItem("Service", 2, "端口 " + WebPort + " 未监听（服务未运行；菜单按 2 启动）"));
                // 官方桌面端（Electron）**不监听 3080**（2026-09-29 在真机上实测：它监听 19387）✓
                // → 它开着时上面那条 ERROR 会误导用户，甚至让他"按 2 启动"再起一个 web 实例 ✗
                // 这里**只在真的检测到那个进程时**才补一句说明 ✓
                // ✗ 更正（2026-09-30）：我原先注释写"比对环境里没有它 → gate1 零影响"，**这是错的** ✓
                //   实测：本机桌面端开着时 gate1 从 22/22 掉到 19/21（doctor 与 --report 各失败一次）✗
                //   → 该行**已加进两个 doctor 用例的 ignore** ✓（环境相关 → 属于必须 ignore 的那类 ✓）
                try
                {
                    if (reg.Get<IProcessQuery>().AnyProcessNamed("DeepSeek Harness"))
                        items.Add(new DocItem("Service", 1, T("另检测到官方桌面端进程（DeepSeek Harness）：它不走 3080 端口，本项检测不到它——这是检测范围不同，不是故障", "an official desktop app process (DeepSeek Harness) is also running: it does not use port 3080, so this check cannot see it - a difference in scope, not a fault")));
                }
                catch { }
            }
            else
            {
                int pid = sr.Pid;
                items.Add(new DocItem("Service", 0, "端口 " + WebPort + " 监听中" + (pid > 0 ? "（PID " + pid + "）" : "")));
                bool isDsh = pid > 0 && proc.IsDshCommandLine(pid);
                string who = isDsh ? "监听进程确为 dsh" : (pid > 0 ? "监听进程不是 dsh！命令行: " + ReportSanitizer.Sanitize(proc.CommandLine(pid)) : "无法确认监听进程身份");
                items.Add(new DocItem("Service", isDsh ? 0 : 2, who));
                bool httpOk = http.Responds(PlatformComposition.WebUrl, 800);
                items.Add(new DocItem("Service", 0, "HTTP: " + (httpOk ? "有应答（dsh 未授权统一 401 属正常门控）" : "无应答")));
                items.Add(new DocItem("Service", sr.State == ServiceState.Ready ? 0 : 1, "服务状态: " + (sr.State == ServiceState.Ready ? "运行中" : (sr.State == ServiceState.Listening ? "启动中" : "已停止"))));
            }

            string data = paths.DataRoot;
            if (string.IsNullOrEmpty(data) || !fs.DirectoryExists(data))
            {
                items.Add(new DocItem("Workspace", 2, "数据目录不存在: " + data + "（dsh 尚未初始化）"));
            }
            else
            {
                bool enumerable = fs.CanEnumerate(data);
                items.Add(new DocItem("Workspace", enumerable ? 0 : 2, "数据目录: " + ReportSanitizer.Sanitize(data) + (enumerable ? "" : "（无读取权限）")));
                long size = fs.DirSize(data);
                items.Add(new DocItem("Workspace", size > 1024L * 1024 * 1024 ? 1 : 0, "数据大小: " + SizeFormatter.Human(size) + (size > 1024L * 1024 * 1024 ? "（较大，备份耗时会增加）" : "")));
            }

            string bkRoot = bk.BackupsRoot;
            if (!fs.DirectoryExists(bkRoot))
            {
                items.Add(new DocItem("Backup", 1, "备份目录不存在（尚未备份过；建议定期备份）"));
            }
            else
            {
                items.Add(new DocItem("Backup", 0, "备份目录: " + ReportSanitizer.Sanitize(bkRoot)));
                List<BackupEntry> all = bk.ListRaw();
                string latest = null;
                for (int i = all.Count - 1; i >= 0; i--) { if (BackupPackage.IsValidPackage(all[i].Snapshot)) { latest = all[i].Name; break; } }
                if (latest == null) items.Add(new DocItem("Backup", 1, "无有效备份（全部无效或为空）"));
                else
                {
                    items.Add(new DocItem("Backup", 0, "最新备份: " + ReportSanitizer.Sanitize(latest)));
                    int? days = BackupAge.DaysSince(latest, DateTime.Now);
                    if (days.HasValue) items.Add(new DocItem("Backup", days.Value > 7 ? 1 : 0, "距上次备份: " + days.Value + " 天" + (days.Value > 7 ? "（建议更新备份）" : "")));
                }
            }

            string cfgReg = tc.NpmRegistryConfig();
            string registry = string.IsNullOrWhiteSpace(cfgReg) ? NpmOfficial : cfgReg.Trim();
            bool reach = http.Responds(registry, 4000);
            items.Add(new DocItem("Network", reach ? 0 : 1, "npm registry " + ReportSanitizer.Sanitize(registry) + (reach ? " 可达" : " 不可达（离线或网络受限；不影响本地功能）")));

            string expected = ManifestParser.ParseHash(integ.ReadManifest(), integ.SelfFileName());
            IntegrityVerdict verdict = IntegrityJudge.Judge(expected, integ.SelfHash());
            if (verdict == IntegrityVerdict.Match) items.Add(new DocItem("Integrity", 0, "自身 exe 与随包 hashes.txt 一致（未被改动）"));
            else if (verdict == IntegrityVerdict.Mismatch) items.Add(new DocItem("Integrity", 2, "自身 exe 与随包 hashes.txt 不一致！（可能被篡改或替换，请从官方 Release 重新下载）"));
            else items.Add(new DocItem("Integrity", 0, "旁无 hashes.txt，跳过自身校验（单独复制 exe 或源码编译属正常；如需校验请使用官方发布包）"));
        }
        /// <summary>profilepatch（V3 独有）：给 profile 的 cordis.patch.yml 追加/修改"禁用某个条目"的顶层行。
        /// 纪律与 v2.x 一致：**备份 → 写盘 → 复检 → 失败回滚**；不带 --yes 只打印计划（DRYRUN）。
        /// 用法：`profilepatch --profile <name> --id <entry> [--enable] [--yes]`
        /// 标记行：`PROFILEPATCH_PLAN` / `_DRYRUN` / `_BACKUP` / `_OK` / `_NOOP` / `_ROLLBACK` / `_FAIL <原因>`。</summary>
        private static int ProfilePatch(string[] args, ServiceRegistry reg)
        {
            IProfileManifestSource src = reg.Get<IProfileManifestSource>();
            string profile = FlagOf(args, "--profile");
            string id = FlagOf(args, "--id");
            bool enable = Has(args, "--enable");
            bool yes = Has(args, "--yes");

            if (string.IsNullOrEmpty(profile))
            {
                Console.WriteLine("PROFILEPATCH_FAIL usage: profilepatch --profile <name> --id <entry> [--enable] [--yes]");
                return 0;
            }
            // I4 FIX (CLI audit MINOR): the profile name is joined into a path, so a value like
            // "..\..\x" could rewrite any file named cordis.patch.yml outside the data root (the
            // fixed file name limits the blast radius, and --yes is required, but it is still a
            // path traversal). Names are restricted to what a profile name can actually be.
            if (!string.IsNullOrEmpty(profile) && !System.Text.RegularExpressions.Regex.IsMatch(profile, @"^[A-Za-z0-9._-]+$") || profile == "." || profile == "..")   // N7 FIX: dots alone escaped the profiles dir
            {
                Console.WriteLine("PROFILEPATCH_FAIL " + T("profile 名字不合法（只允许字母数字与 . _ - ✓）：" + profile, "invalid profile name: " + profile));
                return 0;
            }
            string text = src.ReadPatch(profile);
            if (text == null) { Console.WriteLine("PROFILEPATCH_FAIL file-not-found " + profile); return 0; }

            PatchPlan plan = enable ? PatchPlanner.PlanEnable(text, id) : PatchPlanner.PlanDisable(text, id);
            if (plan.Noop) { Console.WriteLine("PROFILEPATCH_NOOP " + plan.Reason); return 0; }
            if (!plan.Valid) { Console.WriteLine("PROFILEPATCH_FAIL " + plan.Reason); return 0; }

            Console.WriteLine("PROFILEPATCH_PLAN " + profile + "/cordis.patch.yml:" + plan.Line + " " + (enable ? "disabled: false" : "disabled: true"));
            if (!yes)
            {
                Console.WriteLine("PROFILEPATCH_DRYRUN " + T("（确认请加 --yes；只改该 profile 的补丁文件，且会先备份）", "(add --yes to confirm; only that profile's patch file is touched, and it is backed up first)"));
                return 0;
            }
            string backup; string err;
            bool ok = src.ApplyPatch(profile, plan.NewText, out backup, out err);
            if (!string.IsNullOrEmpty(backup)) Console.WriteLine("PROFILEPATCH_BACKUP " + backup);
            if (!ok)
            {
                if (err == "verify-failed") Console.WriteLine("PROFILEPATCH_ROLLBACK " + backup);
                Console.WriteLine("PROFILEPATCH_FAIL " + err);
                return 0;
            }
            Console.WriteLine("PROFILEPATCH_OK " + profile + "/cordis.patch.yml:" + plan.Line);
            OpLog(reg, "INFO", "profilepatch OK " + profile + " line " + plan.Line);
            return 0;
        }
        /// <summary>bootdiag：解析启动失败输出。标记逐条对齐 v2.x 的 BootDiagCli。</summary>
        private static int BootDiag(string[] args, ServiceRegistry reg)
        {
            string from = Flag(args, "--from");
            if (string.IsNullOrEmpty(from)) { Console.WriteLine("BOOTDIAG_FAIL no-input"); return 0; }
            string text = null;
            try { if (System.IO.File.Exists(from)) text = System.IO.File.ReadAllText(from, new System.Text.UTF8Encoding(false)); } catch { }
            if (text == null) { Console.WriteLine("BOOTDIAG_FAIL cannot-read " + ReportSanitizer.Sanitize(from)); return 0; }
            BootDiagResult r = BootDiagParser.Parse(text, LocateEntry(reg), FileExists);
            if (!r.Recognized)
            {
                Console.WriteLine("BOOTDIAG_FAIL");
                Console.WriteLine("BOOTDIAG_KIND unknown");
                Console.WriteLine("BOOTDIAG_FIRST " + TextClipper.Clip(r.FirstError, 200));
                return 0;
            }
            Console.WriteLine("BOOTDIAG_OK");
            Console.WriteLine("BOOTDIAG_KIND " + r.Kind);
            Console.WriteLine("BOOTDIAG_PLUGIN " + r.Plugin);
            Console.WriteLine("BOOTDIAG_ENTRY " + r.Entry);
            Console.WriteLine("BOOTDIAG_FILE " + ReportSanitizer.Sanitize(r.File));
            Console.WriteLine("BOOTDIAG_LINE " + r.Line);
            Console.WriteLine("BOOTDIAG_HINT " + r.Hint);
            return 0;
        }
    }
}
