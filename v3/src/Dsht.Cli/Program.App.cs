using System;
using System.Collections.Generic;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;
using Dsht.Domain.Services;
using Dsht.Domain.Targets;

namespace Dsht.Cli
{
    /// <summary>状态、启停、安装/卸载与自检相关命令 ✓。
    /// 架构审计（S1）：从 Program.cs 原样搬出来，逻辑一行没改 ✓
    /// 边界用括号配平找，逆序删除避免行号漂移 ✓</summary>
    public static partial class Program
    {
        /// <summary>服务三态 + detail 三行。逐条对齐 v2.x 的 StatusCli。</summary>
        private static int Status(ServiceRegistry reg, bool detail)
        {
            ServiceReport r = reg.Get<IServiceTarget>().Probe();
            Console.WriteLine(r.StatusMarker);
            // 官方桌面端（Electron）**不监听 3080** ✓（2026-09-30 真机实测：监听 19387 ✓）
            // → 它开着时上面那行是 STATUS_DOWN ✓ 准确但会让人以为"dsh 没在跑" ✗
            // 这里**只在真检测到那个进程时**才补一行 ✓ → 界面据此能如实显示"桌面端在跑" ✓✓
            try
            {
                if (reg.Get<IProcessQuery>().AnyProcessNamed("DeepSeek Harness"))
                {
                    // 带上 pid / 启动时间 / 已运行 ✓（用户要求："概览再更新下 desktop 的 pid 启动时间和已运行" ✓✓）
                    // 取不到就留空 ✓ 不猜 ✓（STATUS_DESKTOP <name> <pid> <start> <uptime>）
                    int dpid = 0; string dstart = ""; string dup = "";
                    try { dpid = reg.Get<IProcessQuery>().PidOfNamed("DeepSeek Harness"); } catch { }
                    if (dpid > 0)
                    {
                        try { System.DateTime? ds = reg.Get<IProcessQuery>().StartTime(dpid); if (ds.HasValue) { dstart = ds.Value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture); dup = UptimeFormatter.Format(System.DateTime.Now - ds.Value); } } catch { }
                    }
                    // 分成**独立标记** ✓（进程名与时长都带空格 ✗ → 挤在一行没法可靠解析 ✓）
                    Console.WriteLine("STATUS_DESKTOP DeepSeek Harness");
                    if (dpid > 0) Console.WriteLine("STATUS_DESKTOP_PID " + dpid);
                    if (!string.IsNullOrEmpty(dstart)) Console.WriteLine("STATUS_DESKTOP_START " + dstart);
                    if (!string.IsNullOrEmpty(dup)) Console.WriteLine("STATUS_DESKTOP_UPTIME " + dup);
                }
            }
            catch { }
            if (!detail) return 0;
            Console.WriteLine("STATUS_PID " + (r.Pid > 0 ? r.Pid.ToString() : "0"));
            // Honest diagnostic: a running service with no PID means the process probe could not read it
            // (iproute2/ss missing, or the listener belongs to another user) - say so instead of a bare 0.
            if (r.Pid <= 0 && !r.StatusMarker.EndsWith("_DOWN", StringComparison.Ordinal))
                Console.WriteLine("STATUS_PID_NOTE " + T("服务在运行但拿不到 PID：可能缺少 iproute2(ss) 或权限不足（uptime 也会为空）", "service is up but no PID could be read: iproute2 (ss) may be missing or permissions are insufficient (uptime will be empty too)"));
            bool haveStart = false;
            DateTime start = DateTime.MinValue;
            if (r.Pid > 0)
            {
                DateTime? s = reg.Get<IProcessQuery>().StartTime(r.Pid);
                if (s.HasValue) { start = s.Value; haveStart = true; }
            }
            Console.WriteLine("STATUS_START " + (haveStart ? start.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : ""));
            Console.WriteLine("STATUS_UPTIME " + (haveStart ? UptimeFormatter.Format(DateTime.Now - start) : ""));
            return 0;
        }
        private static int Describe(ServiceRegistry reg)
        {
            IServiceTarget target = reg.Get<IServiceTarget>();
            Console.WriteLine(target.Describe());
            Console.WriteLine("basis: " + target.Probe().Basis);
            return 0;
        }
        /// <summary>安装**可选的桥接插件** ✓✓（用户要求：「安装桥接插件有按钮吗」✓）。
        ///
        /// **它做什么**：把 dsh 的会话/token 状态写成一份**只读快照** ✓
        ///   让工具箱能显示「**运行中**」—— 这是**磁盘投影给不了的事实** ✓
        ///   也是这个插件存在的**唯一理由** ✓✓（不装 → 工具箱降级为磁盘投影 ✓ 功能不缺 ✓ 只是没有运行态 ✓）
        /// **它不做什么**：不发模型请求 ✓ 不改 dsh 状态 ✓ 不读会话正文 ✓ 不联网 ✓ 任何失败静默 ✓
        ///
        /// ★★ **实测踩过的三个坑**（2026-09-30 真机 ✓ 我都验证过 ✓）：
        ///   ① `dsh plugin add` **需要 pnpm** ✗ —— 而 dsh **不会**替你装它 ✓
        ///      → 没有 pnpm 就**明确告诉用户怎么装** ✓ **绝不假装成功** ✗
        ///   ② `add` 成功时会 link 进 profile ✓ **并把插件的 patch 合并进 `cordis.patch.yml`** ✓
        ///   ③ **必须验证 patch 里真的有 `shio-bridge` 行** ✗✗ ——
        ///      否则插件**根本不会加载** ✓ 而 dsh **不会报任何错** ✗（"0 处加载错误"是**假绿** ✓ 我踩过 ✓）
        ///
        /// 标记行：BRIDGE_PLAN / BRIDGE_NO_PNPM / BRIDGE_NO_DSH / BRIDGE_NO_PLUGIN / BRIDGE_OK
        ///         / BRIDGE_FAIL / BRIDGE_VERIFY / BRIDGE_NOTE</summary>
        private static int BridgeInstall(string[] args, ServiceRegistry reg)
        {
            string profile = Flag(args, "--profile");
            // N11 FIX (CLI audit MINOR): this value is pasted into a dsh command line, and the
            // plugin-patching command already validates its profile names - a name like "web&calc"
            // would otherwise chain a command on Windows. Same rule here.
            // F-E FIX (CLI final review): the character class allows dots, so "." and ".." passed - and the name
            // is joined into a path later, making the verification read a parent directory instead of a profile.
            if (!string.IsNullOrEmpty(profile) && (profile == "." || profile == ".."
                || !System.Text.RegularExpressions.Regex.IsMatch(profile, @"^[A-Za-z0-9._-]+$")))
            {
                Console.WriteLine("BRIDGE_FAIL " + T("profile 名字不合法（只允许字母数字与 . _ - ✓）：" + profile, "invalid profile name: " + profile));
                return 0;
            }
            if (string.IsNullOrEmpty(profile)) profile = "web";
            bool yes = Has(args, "--yes");

            string selfDir = SelfDir();
            string plugin = System.IO.Path.Combine(System.IO.Path.Combine(selfDir, "plugin"), "dsh-minato-bridge");

            Console.WriteLine("BRIDGE_PLAN " + T(
                "把可选的桥接插件装进 dsh 的 profile「" + profile + "」✓ 它只读 ✓ 不联网 ✓ 不发模型请求 ✓ 不改 dsh 状态 ✓",
                "install the optional bridge plugin into dsh profile '" + profile + "' - read-only, no network, no model calls, no writes to dsh state"));
            Console.WriteLine("BRIDGE_NOTE " + T(
                "装了它，工具箱才能显示「运行中」（运行态是进程内事实，磁盘投影给不了 ✗）；不装也能用 ✓ 只是那一位显示 unknown ✓",
                "with it, the toolkit can show which sessions are running; without it, that one field is unknown"));

            if (!System.IO.Directory.Exists(plugin))
            {
                Console.WriteLine("BRIDGE_NO_PLUGIN " + T(
                    "随包分发的插件目录不存在：" + plugin + " ✓（官方发布包会带 plugin/dsh-minato-bridge ✓ 源码编译请从仓库的 plugin/ 目录取 ✓）",
                    "bundled plugin folder not found: " + plugin));
                return 0;
            }

            string dsh = ResolveDsh(reg);
            if (string.IsNullOrEmpty(dsh))
            {
                Console.WriteLine("BRIDGE_NO_DSH " + T("没有找到 dsh ✓ 请先装 dsh 再装插件 ✓", "dsh not found; install dsh first"));
                return 0;
            }

            string pnpm = WhichOnPath("pnpm");
            if (string.IsNullOrEmpty(pnpm))
            {
                Console.WriteLine("BRIDGE_NO_PNPM " + T(
                    "没有找到 pnpm ✗ —— 而 `dsh plugin add` **依赖它** ✓ 请先装：npm i -g pnpm ✓ 然后重跑本命令 ✓"
                    + "（dsh 自己**不会**替你装 pnpm ✓ 这一步不能省 ✓）",
                    "pnpm not found - dsh's plugin command needs it. Install it with: npm i -g pnpm, then run this again."));
                return 0;
            }
            Console.WriteLine("BRIDGE_NOTE " + T("pnpm: " + pnpm + " ✓ · dsh: " + dsh + " ✓ · 插件: " + plugin + " ✓",
                                                "pnpm: " + pnpm + " / dsh: " + dsh + " / plugin: " + plugin));

            if (!yes)
            {
                Console.WriteLine("BRIDGE_PLAN " + T("这是写操作（会改 profile 的 package.json 与 cordis.patch.yml ✓ 装前 dsh 自己会保留原状 ✓）—— 确认请加 --yes ✓",
                                                     "this writes to the profile - add --yes to proceed"));
                return 0;
            }

            int rc = -1;
            string outp = RunExternal(dsh, "plugin --profile " + profile + " add \"" + plugin + "\"", out rc);
            Console.WriteLine("BRIDGE_FAIL_RAW " + rc + " " + (outp == null ? "" : outp.Trim().Replace("\r", "").Replace("\n", " | ")));

            // ③ **验证**：profile 的 patch 里必须真的有 shio-bridge ✓✓（否则装了也不加载 ✗ 且不报错 ✗）
            string profileDir = "";
            try
            {
                string dataRoot = reg.Get<IPaths>().DataRoot;
                profileDir = System.IO.Path.Combine(System.IO.Path.Combine(dataRoot, "profiles"), profile);
            }
            catch { }

            // ★★ 改进（用户实测后提出）：`desktop` profile **由 dsh 的桌面端独占管理** ✗
            //   → 命令行装不进去 ✓ 而原来只把 dsh 的英文报错原样丢出去 ✗ → 用户不知道该怎么办 ✓
            //   ✓ 现在：**打印该 profile 的实际路径 + 明确指引** ✓✓
            if (outp != null && outp.IndexOf("managed exclusively by the Electron application", StringComparison.Ordinal) >= 0)
            {
                Console.WriteLine("BRIDGE_MANAGED " + T(
                    "profile「" + profile + "」由 dsh 的**桌面端（Electron 应用）独占管理** ✗ → 命令行装不进去 ✓"
                    + "请到桌面端的「添加插件」对话框里，把下面那行**本地目录路径**粘进去 ✓"
                    + "（它接受 npm 包名 / GitHub 地址 / 本地目录路径 ✓）或者改装到 web：`bridge-install --profile web --yes` ✓",
                    "profile '" + profile + "' is managed by dsh's desktop application; paste the local folder path below into its add-plugin dialog, or use --profile web"));
                Console.WriteLine("BRIDGE_MANAGED_PATH " + (string.IsNullOrEmpty(profileDir) ? "(拿不到路径 ✓)" : profileDir));
                Console.WriteLine("BRIDGE_MANAGED_PLUGIN " + plugin + T("   ← **把这一行粘进桌面端的「添加插件」对话框** ✓", "   <- paste this into the desktop add-plugin dialog"));
                return 0;
            }

            bool linked = false, patched = false;
            if (!string.IsNullOrEmpty(profileDir))
            {
                // C1 FIX (audit MAJOR): an npm package is a directory (pnpm makes a junction), and
                // File.Exists is false for both - so `linked` was always false, BRIDGE_OK was
                // unreachable, and a successful install printed a false failure.
                // NOTE: the comment goes ABOVE the line, never inside it - a `//` inside a
                // single-line `try { ... } catch { }` comments out the closing brace and breaks
                // the whole file (this exact mistake cost an hour tonight).
                try { linked = System.IO.Directory.Exists(System.IO.Path.Combine(System.IO.Path.Combine(profileDir, "node_modules"), "dsh-minato-bridge")); } catch { }
                try
                {
                    string patch = System.IO.Path.Combine(profileDir, "cordis.patch.yml");
                    if (System.IO.File.Exists(patch))
                    {
                        string txt = System.IO.File.ReadAllText(patch);
                        // ★★ 改进（用户实测后提出，G4 类）：原来**只 grep `shio-bridge`** ✗
                        //   → **分不出"格式正确"和"格式坏掉"** ✗✗
                        //   （实测：把整个条目压平成同一缩进 ✗ → YAML 结构坏了 ✗ 而 grep 仍报 patched=1 ✗）
                        //   ✓ 现在：**结构校验** ✓ —— 条目后面每一行的缩进必须**严格大于**条目本身 ✓
                        //     缩进不对 → **不算挂上** ✗（宁可报失败，也不假报成功 ✓✓）
                        patched = PatchEntryWellFormed(txt);
                    }
                }
                catch { }
            }

            Console.WriteLine("BRIDGE_VERIFY linked=" + (linked ? "1" : "0") + " patched=" + (patched ? "1" : "0"));

            if (linked && patched)
            {
                Console.WriteLine("BRIDGE_OK " + T(
                    "插件已装好并**已注册进加载树** ✓✓ 重启 dsh 后生效 ✓ 届时工具箱的「运行中」会变成真实值 ✓",
                    "plugin installed and registered in the load tree; restart dsh to take effect"));
            }
            else if (linked && !patched)
            {
                // ★★ 用户实测反馈（外部审计也点了这条）：装完还要**手动**把 patch 行补上 ✗
                //   → 门槛太高 ✓ 而且**漏了 dsh 不会报错** ✗✗（插件静默不加载 ✓）
                // ✓ 现在：**自动合并** ✓✓ —— 备份 → 插入已有 insert 列表 → 结构校验 → 失败则逐字节还原 ✓
                Console.WriteLine("BRIDGE_PATCHING " + T(
                    "包里已 link ✓ 但 profile 的 cordis.patch.yml 缺 shio-bridge 行 → **正在自动补上** ✓（会先备份 ✓）",
                    "linked, but the profile patch file lacks the entry; adding it now (a backup is kept)"));
                string pdetail;
                bool merged = MergePatchEntry(profileDir, plugin, out pdetail);
                if (merged)
                {
                    Console.WriteLine("BRIDGE_PATCHED " + pdetail);
                    Console.WriteLine("BRIDGE_OK " + T(
                        "插件已装好并**已注册进加载树** ✓✓ 重启 dsh 后生效 ✓ 届时工具箱的「运行中」会变成真实值 ✓",
                        "plugin installed and registered in the load tree; restart dsh to take effect"));
                }
                else
                {
                    Console.WriteLine("BRIDGE_FAIL " + T(
                        "自动补 patch 行**没成功**：" + pdetail + " ✓ 原文件**未被改动** ✓（备份也在 ✓）"
                        + "请手动把插件自带的 cordis.patch.yml 追加到该 profile 的 cordis.patch.yml ✓ 然后重启 dsh ✓",
                        "could not add the patch entry automatically: " + pdetail + "; the file was left unchanged"));
                }
            }
            else
            {
                Console.WriteLine("BRIDGE_FAIL " + T(
                    "安装没有成功 ✓ 退出码 " + rc + " ✓ 上面 BRIDGE_FAIL_RAW 是原始输出 ✓（最常见原因：pnpm 不在 PATH ✓ 或 dsh 的 profile 名不对 ✓）",
                    "install did not succeed; exit code " + rc + "; see BRIDGE_FAIL_RAW above"));
            }
            return 0;
        }
        /// <summary>把插件自带的 `cordis.patch.yml` 里的 `- id: shio-bridge` 条目**合并进** profile 的 patch 文件 ✓。
        /// ★ 动机：装完还要用户**手动**补这一行 ✗ → 门槛太高 ✓ 而且**漏了 dsh 不报错**（插件静默不加载 ✗）。
        /// 做法（**我手工做过两遍并逐项验证过** ✓ 现在固化进代码 ✓）：
        ///   ① 先**备份** profile 的 patch 文件 ✓
        ///   ② 从插件 patch 里取出 `- id: shio-bridge` 那一段 ✓ —— **保留相对缩进** ✓ 基准归到 4 空格 ✓
        ///   ③ 找**已有的顶层 `- insert:` 列表** ✓ 把条目追加到该列表**末尾** ✓
        ///      ✗✗ **绝不新建第二个 `insert:`** —— 重复键会让 YAML 解析器只取一个 ✓ **会丢掉用户原有条目** ✓
        ///   ④ **结构校验** ✓（复用 PatchEntryWellFormed ✓）失败 → **逐字节还原备份** ✓ 并如实报告 ✓
        /// 返回 false 时**保证文件与调用前逐字节相同** ✓。</summary>
        private static bool MergePatchEntry(string profileDir, string pluginDir, out string detail)
        {
            detail = "";
            string profPatch = System.IO.Path.Combine(profileDir, "cordis.patch.yml");
            string plugPatch = System.IO.Path.Combine(pluginDir, "cordis.patch.yml");
            string backup = profPatch + ".bak-before-bridge-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try
            {
                if (!System.IO.File.Exists(profPatch)) { detail = "profile 的 cordis.patch.yml 不存在"; return false; }
                if (!System.IO.File.Exists(plugPatch)) { detail = "插件自带的 cordis.patch.yml 不存在"; return false; }
                string profText = System.IO.File.ReadAllText(profPatch);
                string plugText = System.IO.File.ReadAllText(plugPatch);
                System.IO.File.Copy(profPatch, backup, true);

                string[] pl = plugText.Replace("\r\n", "\n").Split('\n');
                int start = -1;
                for (int i = 0; i < pl.Length; i++)
                {
                    string t = pl[i].TrimStart();
                    if (t.StartsWith("-") && t.TrimStart('-').TrimStart().StartsWith("id:", StringComparison.Ordinal)
                        && t.IndexOf("shio-bridge", StringComparison.Ordinal) >= 0) { start = i; break; }
                }
                if (start < 0) { detail = "插件 patch 里找不到 shio-bridge 条目"; return false; }
                int baseIndent = pl[start].Length - pl[start].TrimStart().Length;
                System.Collections.Generic.List<string> entry = new System.Collections.Generic.List<string>();
                for (int i = start; i < pl.Length; i++)
                {
                    if (pl[i].Trim().Length == 0) continue;
                    int cur = pl[i].Length - pl[i].TrimStart().Length;
                    int rel = cur - baseIndent;
                    if (rel < 0) rel = 0;
                    entry.Add(new string(' ', 4 + rel) + pl[i].Trim());
                }

                string[] pf = profText.Replace("\r\n", "\n").Split('\n');
                System.Collections.Generic.List<string> outLines = new System.Collections.Generic.List<string>();
                for (int i = 0; i < pf.Length; i++) outLines.Add(pf[i]);
                int insIdx = -1;
                for (int i = 0; i < pf.Length; i++) { if (pf[i].TrimEnd() == "- insert:") { insIdx = i; break; } }
                if (insIdx < 0)
                {
                    outLines.Add("");
                    outLines.Add("- insert:");
                    outLines.AddRange(entry);
                }
                else
                {
                    int last = insIdx;
                    for (int i = insIdx + 1; i < pf.Length; i++)
                    {
                        if (pf[i].Trim().Length == 0) continue;
                        if (!pf[i].StartsWith(" ")) break;
                        last = i;
                    }
                    System.Collections.Generic.List<string> ins = new System.Collections.Generic.List<string>();
                    ins.Add("");
                    ins.Add("    # dsh-minato-bridge: read-only snapshot (merged into this insert list, not a new one)");
                    ins.AddRange(entry);
                    outLines.InsertRange(last + 1, ins);
                }
                string result = string.Join("\r\n", outLines.ToArray());
                if (!PatchEntryWellFormed(result))
                {
                    System.IO.File.Copy(backup, profPatch, true);
                    detail = "合并后**结构校验不通过**（已还原）";
                    return false;
                }
                System.IO.File.WriteAllText(profPatch, result);
                detail = "已写入 ✓ 备份：" + System.IO.Path.GetFileName(backup);
                return true;
            }
            catch (Exception ex)
            {
                try { if (System.IO.File.Exists(backup)) System.IO.File.Copy(backup, profPatch, true); } catch { }
                detail = ex.Message;
                return false;
            }
        }

        /// <summary>`cordis.patch.yml` 里的 `- id: shio-bridge` 条目是否**结构正确** ✓。
        /// ★ 动机（用户实测后提出，G4 类）：原来只 `IndexOf("shio-bridge")` ✗ ——
        ///   **字符串在 ≠ 条目可用** ✗：把整个条目压平成同一缩进后，YAML 结构是坏的 ✗
        ///   而 grep **照样报 patched=1** ✗✗（我自己就这么误报过一次 ✓）。
        /// ✓ 现在：找到条目行后，检查它**之后**每一行（直到下一个 `- ` 条目）的缩进
        ///   **严格大于**条目行 ✓ 且至少有一行子项 ✓ —— 否则返回 false ✓（fail-closed ✓）。</summary>
        private static bool PatchEntryWellFormed(string yaml)
        {
            if (string.IsNullOrEmpty(yaml)) return false;
            string[] lines = yaml.Replace("\r\n", "\n").Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].TrimStart();
                if (!t.StartsWith("-")) continue;
                string after = t.Substring(1).TrimStart();
                if (after.StartsWith("id:", StringComparison.Ordinal) && t.IndexOf("shio-bridge", StringComparison.Ordinal) >= 0) { start = i; break; }
            }
            if (start < 0) return false;
            int idIndent = lines[start].Length - lines[start].TrimStart().Length;
            bool sawChild = false;
            for (int i = start + 1; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.Trim().Length == 0) continue;
                string t = l.TrimStart();
                if (t.StartsWith("-")) break;                  // 下一个条目 → 本条目结束 ✓
                int cur = l.Length - t.Length;
                if (cur <= idIndent) return false;              // 同级或更浅 → 结构坏了 ✗
                sawChild = true;
            }
            return sawChild;                                    // 一个子项都没有 → 不算条目 ✓
        }

        /// <summary>verify-install（V3 独有）：核对本机文件与发布清单的 SHA-256 ✓ —— 经典版「验证此安装」的 CLI 对应物 ✓。
        /// 默认核对**正在运行的自身** ✓；清单默认取自身旁边的 hashes.txt ✓，或用 --manifest 指定，或用 --url 从发布页取 ✓。
        /// **清单拿不到时绝不说 OK** ✗✓：只报 VERIFY_MANIFEST_MISSING 并说明如何取得 ✓。
        /// 诚实边界 ✓：本命令只做 SHA-256 比对；清单本身的 GPG 签名校验仍在 Windows 的 verify.ps1 里 ✓（CLI 不引入第三方依赖 ✗）。
        /// 标记行：VERIFY_SELF / VERIFY_MANIFEST / VERIFY_MATCH / VERIFY_MISMATCH / VERIFY_NOT_IN_MANIFEST / VERIFY_OK / VERIFY_FAIL。</summary>
        private static int VerifyInstall(string[] args)
        {
            string target = FlagOf(args, "--file");
            if (target.Length == 0)
            {
                try { target = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; } catch { }
                if (string.IsNullOrEmpty(target)) { Console.WriteLine("VERIFY_FAIL " + T("拿不到自身路径，请用 --file 指定要核对的文件", "cannot determine own path; pass --file")); return 0; }
            }
            if (!System.IO.File.Exists(target)) { Console.WriteLine("VERIFY_FAIL " + T("文件不存在: ", "file does not exist: ") + target); return 0; }
            string manifestPath = FlagOf(args, "--manifest");
            string url = FlagOf(args, "--url");
            string text = null;
            if (url.Length > 0)
            {
                try
                {
                    // .NET Framework defaults to TLS 1.0, which GitHub refuses ("could not create SSL/TLS secure
                    // channel"); .NET 8 already negotiates 1.2+. The API is obsolete on .NET 8 but functional, so
                    // the whole assignment is guarded rather than conditional on the runtime.
                    try { System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls; } catch { }
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                    {
                        wc.Headers.Add("User-Agent", "dsh-minato-verify");
                        text = wc.DownloadString(url);
                    }
                    Console.WriteLine("VERIFY_MANIFEST " + url + " " + T("(已下载)", "(downloaded)"));
                }
                catch (Exception wex) { Console.WriteLine("VERIFY_MANIFEST_MISSING " + T("无法下载清单: ", "could not download the manifest: ") + wex.Message); Console.WriteLine("VERIFY_FAIL 0"); return 0; }
            }
            else
            {
                if (manifestPath.Length == 0)
                {
                    try { manifestPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(target)), "hashes.txt"); } catch { }
                }
                if (string.IsNullOrEmpty(manifestPath) || !System.IO.File.Exists(manifestPath))
                {
                    // 清单缺失 = 无法核对 ✗ → 明确说清，绝不给出 OK ✗✓
                    Console.WriteLine("VERIFY_MANIFEST_MISSING " + T("找不到清单文件（默认取同目录的 hashes.txt）: ", "manifest not found (defaults to hashes.txt next to the file): ") + (manifestPath == null ? "" : manifestPath));
                    Console.WriteLine("VERIFY_HINT " + T("用 --manifest 指定清单，或用 --url https://github.com/sakanamaru/dsh-minato/releases/latest/download/hashes.txt 联网取官方清单", "pass --manifest, or --url https://github.com/sakanamaru/dsh-minato/releases/latest/download/hashes.txt to fetch the official manifest"));
                    Console.WriteLine("VERIFY_FAIL 0");
                    return 0;
                }
                try { text = System.IO.File.ReadAllText(manifestPath); Console.WriteLine("VERIFY_MANIFEST " + manifestPath); }
                catch (Exception rex) { Console.WriteLine("VERIFY_MANIFEST_MISSING " + rex.Message); Console.WriteLine("VERIFY_FAIL 0"); return 0; }
            }
            string selfHash;
            try { selfHash = Sha256Of(target); }
            catch (Exception hex) { Console.WriteLine("VERIFY_FAIL " + T("无法计算哈希: ", "cannot hash: ") + hex.Message); return 0; }
            Console.WriteLine("VERIFY_SELF " + target + " " + selfHash);
            string leaf = System.IO.Path.GetFileName(target);
            string want = null;
            int entries = 0;
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.Length == 0 || l.IndexOf((char)35) == 0) continue;   // 跳过 # 注释行
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(l, "^([0-9a-fA-F]{64})[ \\t]+\\*?(.+)$");
                if (!m.Success) continue;
                entries++;
                string name = m.Groups[2].Value.Trim();
                if (string.Equals(name, leaf, StringComparison.OrdinalIgnoreCase)) want = m.Groups[1].Value.ToLowerInvariant();
            }
            if (entries == 0) { Console.WriteLine("VERIFY_MANIFEST_MISSING " + T("清单里没有可解析的 SHA-256 行", "the manifest has no parsable SHA-256 lines")); Console.WriteLine("VERIFY_FAIL 0"); return 0; }
            Console.WriteLine("VERIFY_ENTRIES " + entries);
            if (want == null)
            {
                Console.WriteLine("VERIFY_NOT_IN_MANIFEST " + leaf);
                Console.WriteLine("VERIFY_HINT " + T("清单里没有这个文件名 —— 它可能不是本项目的发布产物（发布清单只列发布资产）", "that filename is not in the manifest - it may not be a release artifact of this project"));
                Console.WriteLine("VERIFY_FAIL 1");
                return 0;
            }
            if (string.Equals(want, selfHash, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("VERIFY_MATCH " + leaf + " " + want);
                Console.WriteLine("VERIFY_OK 1");
            }
            else
            {
                Console.WriteLine("VERIFY_MISMATCH " + leaf + " " + T("清单=", "manifest=") + want + " " + T("本机=", "local=") + selfHash);
                Console.WriteLine("VERIFY_HINT " + T("不一致：本机文件与清单记录不符（可能被替换或篡改），请从官方 Release 重新下载", "mismatch: the local file does not match the manifest (it may have been replaced or tampered with); download it again from the official release"));
                Console.WriteLine("VERIFY_FAIL 1");
            }
            return 0;
        }
        /// <summary>wipe（V3 独有）：清除数据根内容 ✓ —— 卸载前的"干净清除" ✓（经典版有 ✓）。
        /// 破坏性操作 ✗ → 多重闸门 ✓：① 先打印计划 ② 必须 --yes ③ **必须先成功做出 -pre-wipe 备份**
        /// （没备份就不许清 ✗）④ 拒绝系统/用户级根目录 ✗ ⑤ **备份根在数据根内时拒绝** ✗（否则会连安全网一起删 ✗）。
        /// 备份目录本身**不动** ✓；标记行：WIPE_PLAN / WIPE_REFUSED / WIPE_PRE_BACKUP / WIPE_OK / WIPE_FAIL。</summary>
        private static int WipeCmd(string[] args, ServiceRegistry reg)
        {
            string data = reg.Get<IPaths>().DataRoot;
            string backups = reg.Get<IBackupSource>().BackupsRoot;
            string dataFull, bkFull;
            try
            {
                dataFull = System.IO.Path.GetFullPath(data).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                bkFull = System.IO.Path.GetFullPath(backups).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) { Console.WriteLine("WIPE_FAIL " + ex.Message); return 0; }
            // F1 FIX (CLI audit MAJOR): two refusals used to sit here - a drive/system-root check
            // and a check that the backup folder is not inside the data root - and both returned
            // before the output below. This command no longer deletes anything, so the refusals
            // were both unnecessary and harmful: they made the one thing the command still does
            // - print the exact path for the user to delete by hand - unreachable in precisely
            // the two configurations where it matters most. They are gone; the dead block below
            // keeps its own copies of the same checks for whoever restores the delete path.
            int files = 0, dirs = 0;
            // F2 FIX (CLI audit MINOR): a failed enumeration used to leave files and dirs at 0, and
            // the line below then said "the data root holds 0 files and 0 folders", which is a fake
            // zero where the truth is unknown. counted distinguishes that case.
            bool counted = false;
            try { files = System.IO.Directory.GetFiles(dataFull, "*", System.IO.SearchOption.AllDirectories).Length; dirs = System.IO.Directory.GetDirectories(dataFull, "*", System.IO.SearchOption.AllDirectories).Length; counted = true; } catch { }
            // ★★★ **用户要求（2026-09-30）**：「删除 CLI 和 GUI 备份里的清除数据操作按钮，点击只弹出手动删除路径」✓✓
            //   → **本命令永不删除任何东西** ✓✓ 无论有没有 `--yes` ✓
            //   → 只输出：① 数据根里有多少东西（信息 ✓）② **手动删除的确切路径** ✓
            //   → 保留 `WIPE_PLAN` / `WIPE_PLAN_NOTE` 标记 ✓（门槛与 GUI 的解析不用改 ✓）
            Console.WriteLine("WIPE_PLAN " + T("数据根内有 ", "the data root holds ") + (counted ? files.ToString() : T("unknown", "unknown")) + T(" 个文件、", " files and ") + (counted ? dirs.ToString() : T("unknown", "unknown")) + T(" 个目录", " folders") + " — " + dataFull);
            Console.WriteLine("WIPE_PLAN_NOTE " + T("**本工具不再执行清除** ✓ 请**手动**删除上面那个目录 ✓ 删前请先备份 ✓（备份目录：" + backups + " ✓）", "this tool no longer wipes data - delete the folder above yourself, after backing up"));
            Console.WriteLine("WIPE_MANUAL " + dataFull);
            return 0;
            // ↓↓↓ 以下是**旧实现**（保留在源码里但**不可达** ✗ 因为上面已经 return ✓）
            //     留着的唯一理由是：将来若要恢复清除能力，逻辑与五道闸门都在 ✓
            //     但**现在它永远不会执行** ✓✓
            if (!Has(args, "--yes"))
            {
                Console.WriteLine("WIPE_PLAN " + T("将删除数据根内的 ", "will delete ") + files + T(" 个文件、", " files and ") + dirs + T(" 个子目录：", " subdirectories in ") + dataFull);
                Console.WriteLine("WIPE_PLAN_NOTE " + T("执行前**必须**先成功做出 -pre-wipe 备份（做不出就不清 ✗）；备份目录本身不动 ✓。确认请加 --yes", "a -pre-wipe backup MUST succeed first (no backup, no wipe); the backups root itself is untouched. Add --yes to confirm"));
                return 0;
            }
            // 闸门 ③：先备份，且**必须成功** ✓✓
            try
            {
                Dsht.Domain.Model.BackupResult pb = reg.Get<IBackupSource>().Create(dataFull, Dsht.Domain.Model.BackupKind.PreWipe, _cfg == null ? 3 : _cfg.KeepBackups, WorkspaceRoot(reg));
                if (pb == null || string.IsNullOrEmpty(pb.Path)) { Console.WriteLine("WIPE_REFUSED " + T("清除前的安全备份未能创建，已拒绝执行（没备份就不清 ✗）", "the pre-wipe backup could not be created; refusing to wipe (no backup, no wipe)")); return 0; }
                AddContentHashToMarker(pb.Path);   // 回滚锚点也要能自证完整 ✓✓
            Console.WriteLine("WIPE_PRE_BACKUP " + pb.Path);   // 打印行必须保留 ✗（我第 53 轮把它替换成了哈希调用 ✗✗ → 用户看不到安全备份在哪 ✓）
            }
            catch (Exception bex) { Console.WriteLine("WIPE_REFUSED " + T("清除前的安全备份失败，已拒绝执行: ", "the pre-wipe backup failed; refusing to wipe: ") + bex.Message); return 0; }
            // 真清：只删数据根**内容** ✓
            int removed = 0;
            try
            {
                string[] fs = System.IO.Directory.GetFiles(dataFull);
                for (int i = 0; i < fs.Length; i++) { System.IO.File.Delete(fs[i]); removed++; }
                string[] ds = System.IO.Directory.GetDirectories(dataFull);
                for (int i = 0; i < ds.Length; i++) { System.IO.Directory.Delete(ds[i], true); removed++; }
                OpLog(reg, "WARN", "wipe OK " + dataFull + " (" + removed + " entries removed, backup " + files + " files)");
                Console.WriteLine("WIPE_OK " + removed);
                Console.WriteLine("WIPE_NOTE " + T("备份未被删除，可随时用 restore 恢复 ✓", "backups were kept; restore can bring the data back at any time"));
            }
            catch (Exception wex) { OpLog(reg, "ERROR", "wipe failed: " + wex.Message); Console.WriteLine("WIPE_FAIL " + wex.Message); }
            return 0;
        }
        /// <summary>import（V3 独有）：把**外部**备份包导入本机备份根 ✓ —— 跨机迁移的关键一环 ✓。
        /// 恢复侧刻意只接受备份根内的路径（outside → 拒绝 ✓），所以外部包必须先导入 ✓。
        /// 用法：import --path &lt;外部包&gt; [--yes]。加 --yes 时：先对当前数据根做一次 -pre-import 安全备份 ✓，
        /// 再**复制**外部包进备份根（源包不动 ✓），最后提示用 restore 恢复 ✓。导入本身不改动数据 ✓。</summary>
        private static int ImportCmd(string[] args, ServiceRegistry reg)
        {
            string src = FlagOf(args, "--path");
            if (src.Length == 0) { Console.WriteLine("IMPORT_FAIL usage: import --path <外部备份包> [--yes]"); return 0; }
            if (!System.IO.Directory.Exists(src)) { Console.WriteLine("IMPORT_FAIL " + T("源目录不存在: ", "source directory does not exist: ") + src); return 0; }
            IBackupSource bk = reg.Get<IBackupSource>();
            string root = bk.BackupsRoot;
            string srcFull, rootFull;
            try
            {
                srcFull = System.IO.Path.GetFullPath(src).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                rootFull = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            }
            catch (Exception ex) { Console.WriteLine("IMPORT_FAIL " + ex.Message); return 0; }
            if (srcFull == rootFull || srcFull.StartsWith(rootFull + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                Console.WriteLine("IMPORT_NOTE " + T("该备份已在本机备份根内，无需导入 —— 直接 restore 即可", "that backup is already inside the local backups root - just restore it"));
                Console.WriteLine("IMPORT_OK " + srcFull);
                return 0;
            }
            string[] entries = null;
            try { entries = System.IO.Directory.GetFileSystemEntries(srcFull); } catch { }
            if (entries == null || entries.Length == 0) { Console.WriteLine("IMPORT_FAIL " + T("无效备份目录（空目录）", "invalid backup directory (empty)")); return 0; }
            if (!Has(args, "--yes"))
            {
                Console.WriteLine("IMPORT_PLAN " + T("将把 ", "will copy ") + srcFull + T(" 复制进备份根 ", " into the backups root ") + rootFull + T("，并先对当前数据根做一次 -pre-import 安全备份（会写盘）—— 确认请加 --yes", ", taking a -pre-import safety backup of the current data root first (writes to disk) - add --yes to confirm"));
                return 0;
            }
            // 1) 安全网：导入前给当前数据根留一份 -pre-import 备份 ✓
            try
            {
                Dsht.Domain.Model.BackupResult pb = bk.Create(reg.Get<IPaths>().DataRoot, Dsht.Domain.Model.BackupKind.PreImport, _cfg == null ? 3 : _cfg.KeepBackups, WorkspaceRoot(reg));
                if (pb != null && !string.IsNullOrEmpty(pb.Path)) { Console.WriteLine("IMPORT_PRE_BACKUP " + pb.Path); AddContentHashToMarker(pb.Path); }   // 回滚锚点也要能自证完整 ✓✓
            }
            catch (Exception bex) { Console.WriteLine("IMPORT_PRE_BACKUP_FAILED " + bex.Message); }
            // 2) 复制外部包进备份根（源包不动 ✓）；名字带 -imported 便于识别（Classify 视作手动类 ✓ 不会被自动清理 ✓）
            string dest = System.IO.Path.Combine(rootFull, "dsh-data-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "-" + System.Diagnostics.Process.GetCurrentProcess().Id + "-imported");
            try
            {
                int files = CopyDirDeep(srcFull, dest, 0);
                // 把**同级旁挂文件**（完成标记 / 版本记录）一起导入 ✓✓ —— 否则迁移到新机器后无法核对完整性 ✓
                CopySiblingFile(srcFull, dest, ".manifest");
                CopySiblingFile(srcFull, dest, ".version");
                OpLog(reg, "INFO", "import OK " + srcFull + " -> " + dest + " (" + files + " files)");
                Console.WriteLine("IMPORT_OK " + dest + " " + files);
                Console.WriteLine("IMPORT_NEXT " + T("下一步：restore --path ", "next: restore --path ") + dest + T(" --dry-run 先预览，再去掉 --dry-run 执行", " --dry-run to preview, then drop --dry-run to apply"));
            }
            catch (Exception cex) { OpLog(reg, "ERROR", "import failed: " + cex.Message); Console.WriteLine("IMPORT_FAIL " + cex.Message); }
            return 0;
        }
        private static int AboutCmd()
        {
            // 图标署名 ✓✓（用户要求："README + 关于页署名原作者" ✓）
            Console.WriteLine("CREDITS " + T("鲸鱼娘（Whale-chan）形象来自 DeepSeek 社区同人创作；本项目图标为生成式 AI 产出（生成式 AI 工具），提示词由维护者编写。非官方、非商业、与 DeepSeek 官方无关。详见仓库 docs/ASSETS.md。", "Whale-chan is community fan art; this project's icon is AI-generated (生成式 AI 工具). Unofficial, non-commercial, not affiliated with DeepSeek. See ASSETS.md."));
            Console.WriteLine("dsh-minato " + ToolkitVersion);
            Console.WriteLine(T("社区版 DeepSeek Harness (dsh) 本机部署运维套件：安装 / 启动 / 监控 / 备份恢复 / 插件诊断与隔离",
                                "community deploy & ops kit for DeepSeek Harness (dsh): install, start, monitor, backup & restore, plugin diagnosis & quarantine"));
            Console.WriteLine(T("非官方工具，与 DeepSeek 官方无关。", "Unofficial tool; not affiliated with DeepSeek."));
            Console.WriteLine(T("仓库：", "Repository: ") + "https://github.com/sakanamaru/dsh-minato");
            Console.WriteLine(T("许可：MIT", "License: MIT"));
            // ★ 审查抓到：这句话有**两处不实** ✗✗
            //   ① 说"不联网（余额查询除外）" ✗ —— 但**根本没有余额查询** ✓ 而真联网的命令有 6 条 ✗
            //   ② 说"写操作一律先备份" ✗ —— 改设置/改 profile/改快捷方式都**不**备份 ✗
            // ✓ 现在：**逐条如实** ✓✓（联网命令点名 ✓ 备份范围说清 ✓）
            // ★★ 第 2 轮审查抓到：我上一版**仍然不实** ✗✗
            //   ① 漏了 `doctor` ✗ —— 它会对 npm registry 发一次 HTTP GET（Program.cs 的 Doctor → IHttpProbe.Responds ✓
            //      而 GUI 自己也写着"进这一页会自动跑一次 doctor（会检查网络…）" ✓ 项目里 compare_markers 还专门忽略它的 Network 行 ✓）
            //   ② 把 `wipe` 列进"会先备份" ✗ —— 而 wipe **现在根本不删任何东西** ✓（它只打印手动删除路径 ✓ 也从不备份 ✓）
            // ✓ 现在：**逐条对齐代码** ✓✓
            Console.WriteLine(T("本程序只读写本机。会联网的只有：check / update-info / update-center / doctor / install / update / verify-install --url / balance（仅当你在设置里填了 balance_key ✓）；其余命令不联网。",
                                "Works locally only. The only commands that use the network are: check, update-info, update-center, doctor, install, update, verify-install --url, and balance (only when you have set balance_key in settings). Everything else stays offline."));
            Console.WriteLine(T("restore 之前会先自动备份并打印位置（可回滚）；update / import 会尝试先备份，失败时仍继续。wipe 现在只打印手动删除路径、不删也不备份。改设置、改 profile、改快捷方式不备份。",
                                "restore always backs up first and prints where; update and import try to and continue if that fails. wipe only prints the path to delete by hand - it neither deletes nor backs up. Settings, profile and shortcut edits are not backed up."));
            return 0;
        }
        /// <summary>ui（V3 独有）：启动跨平台 GUI（Avalonia）。找不到就**如实说明**，不静默失败、不假装已启动。</summary>
        private static int UiCmd()
        {
            string gui = Environment.GetEnvironmentVariable("DSHT_GUI");
            if (!string.IsNullOrEmpty(gui) && !System.IO.File.Exists(gui)) { Console.WriteLine("UI_FAIL " + T("DSHT_GUI 指向的文件不存在：", "DSHT_GUI points at a missing file: ") + gui); return 0; }
            if (string.IsNullOrEmpty(gui))
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                // Avalonia 优先；找不到再回退 v2.x 的旧界面（并如实说明那是旧版）
                string[] names = PlatformIsWindows()
                    ? new string[] { "dsht-gui.exe", "Toolkit GUI Standalone.exe", "Toolkit GUI.exe", "DeepSeek Harness Toolkit.exe" }
                    : new string[] { "dsht-gui", "Toolkit GUI Standalone", "Toolkit GUI" };
                for (int i = 0; i < names.Length; i++)
                {
                    string cand = System.IO.Path.Combine(dir, names[i]);
                    if (System.IO.File.Exists(cand)) { gui = cand; break; }
                }
            }
            if (string.IsNullOrEmpty(gui))
            {
                Console.WriteLine("UI_FAIL " + T("没找到 GUI（把 dsht-gui 放在本程序同目录，或用环境变量 DSHT_GUI 指定）", "no GUI found (put dsht-gui next to this program, or set DSHT_GUI)"));
                return 0;
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(gui);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi);
                string legacy = (gui.IndexOf("dsht-gui", StringComparison.OrdinalIgnoreCase) < 0) ? T("（提示：这是 v2.x 的旧界面；跨平台新界面请用 dsht-gui）", " (note: this is the v2.x UI; the cross-platform one is dsht-gui)") : "";
                Console.WriteLine("UI_OK " + gui + (p != null ? " pid=" + p.Id : "") + legacy);
            }
            catch (Exception ex) { Console.WriteLine("UI_FAIL " + ex.Message); }
            return 0;
        }
        private static int InstallLike(string[] args, ServiceRegistry reg, bool update)
        {
            IToolchainQuery tc = reg.Get<IToolchainQuery>();
            string verb = update ? "UPDATE" : "INSTALL";
            string which = tc.WhichDsh();
            string installed = tc.DshVersion();
            bool isInstalled = !string.IsNullOrEmpty(which) || !string.IsNullOrEmpty(installed);

            if (isInstalled && !update)
            {
                Console.WriteLine(verb + "_SKIP " + T("已经装了 dsh ", "dsh is already installed ") + (installed.Length > 0 ? installed : "?") + T("（升级请用 update）", " (use update to upgrade)"));
                Console.WriteLine(verb + "_OBSERVED " + (installed.Length > 0 ? installed : "installed"));
                return 0;
            }

            // Linux 一键安装的前置：dsh 靠 npm 装，npm 靠 node。缺 node 时**不能假装一键** ✗
            string nodeNow = tc.NodeVersion();
            bool nodeMissing = string.IsNullOrEmpty(nodeNow);
            bool nodeOld = !nodeMissing && NodeTooOld(nodeNow, 22, 19);   // dsh 要求 >= 22.19.0（真机抓到的 ✗）
            // ★★ 架构审计抓到（v2 对等 + 诚实性）：**Windows 上这里被整个跳过** ✗
            //   → 而 WindowsToolchainQuery 的注释写着"安装器负责" ✗ —— **安装器里根本没有 Node 安装代码** ✗✗
            //   → 结果：Windows 缺 Node 时只报 INSTALL_FAIL「拿不到可信的最新版本（离线…）」✗ → **误导** ✓
            // ✓ 现在：**Windows 也能一键装** ✓✓（winget 是系统自带的 ✓ 零第三方依赖 ✓）
            //   并且**不假装成功** ✓ —— 装完**复核 `NodeVersion()`** ✓ 观察而不是信任退出码 ✓✓
            if (PlatformIsWindows() && (nodeMissing || nodeOld))
            {
                Console.WriteLine(verb + "_NEED_NODE " + (nodeMissing
                    ? T("未检测到 Node.js（dsh 通过 npm 安装，需要它）", "Node.js not found (dsh installs through npm and needs it)")
                    : T("Node.js 版本过旧（", "Node.js is too old (") + nodeNow + T("）—— dsh 要求 >= 22.19.0", ") - dsh requires >= 22.19.0")));
                Console.WriteLine(verb + "_NODE_HINT " + T("Windows 上可用 winget 自动装：加 --install-node --yes；或手动装（官网安装包 / winget install OpenJS.NodeJS.LTS）后**重开一个终端**再试 ✓",
                                                                    "on Windows: add --install-node --yes to install with winget, or install it yourself (official installer / winget install OpenJS.NodeJS.LTS) and reopen the terminal"));
                if (!Has(args, "--install-node") || !Has(args, "--yes"))
                {
                    Console.WriteLine(verb + "_DRYRUN " + T("（确认请加 --install-node --yes）", "(add --install-node --yes to confirm)"));
                    Console.WriteLine(verb + "_OBSERVED not-installed");
                    return 0;
                }
                Console.WriteLine(verb + "_NODE_INSTALLING " + T("正在用 winget 安装 Node.js LTS …", "installing Node.js LTS with winget ..."));
                int ncW = tc.InstallNodeRuntime();
                string nvW = tc.NodeVersion();
                if (string.IsNullOrEmpty(nvW))
                {
                    Console.WriteLine(verb + "_FAIL " + T("Node 引导失败（退出码 ", "Node bootstrap failed (exit code ") + ncW + T("）：", "): ") + Dsht.Platform.Windows.WindowsToolchainQuery.LastError);
                    Console.WriteLine(verb + "_NODE_HINT " + T("可手动安装：winget install OpenJS.NodeJS.LTS（或官网安装包），装完重开一个终端 ✓", "install by hand: winget install OpenJS.NodeJS.LTS (or the official installer), then reopen the terminal"));
                    Console.WriteLine(verb + "_OBSERVED not-installed");
                    return 0;
                }
                Console.WriteLine(verb + "_NODE_OK " + nvW + T("（由 winget 安装 ✓ 可能需要重开终端才能用）", " (installed by winget; a new terminal may be needed)"));
            }

            if (!PlatformIsWindows() && (nodeMissing || nodeOld))
            {
                Console.WriteLine(verb + "_NEED_NODE " + (nodeMissing
                    ? T("未检测到 Node.js（dsh 通过 npm 安装，需要它）", "Node.js not found (dsh installs through npm and needs it)")
                    : T("Node.js 版本过旧（", "Node.js is too old (") + nodeNow + T("）—— dsh 要求 >= 22.19.0", ") - dsh requires >= 22.19.0")));
                Console.WriteLine(verb + "_NODE_HINT " + T("免 sudo：加 --install-node 自动装到 ~/.local/node；或用系统包管理器：sudo apt install -y nodejs npm（Debian/Ubuntu）/ sudo dnf install -y nodejs npm（Fedora）",
                                                            "no sudo needed: add --install-node to install into ~/.local/node; or use your package manager: sudo apt install -y nodejs npm (Debian/Ubuntu) / sudo dnf install -y nodejs npm (Fedora)"));
                if (!Has(args, "--install-node") || !Has(args, "--yes"))
                {
                    Console.WriteLine(verb + "_DRYRUN " + T("（确认请加 --install-node --yes）", "(add --install-node --yes to confirm)"));
                    Console.WriteLine(verb + "_OBSERVED not-installed");
                    return 0;
                }
                Console.WriteLine(verb + "_NODE_INSTALLING " + T("正在下载官方 Node LTS 到 ~/.local/node …", "downloading the official Node LTS into ~/.local/node ..."));
                int nc = tc.InstallNodeRuntime();
                string nv = tc.NodeVersion();
                if (string.IsNullOrEmpty(nv))
                {
                    Console.WriteLine(verb + "_FAIL " + T("Node 引导失败（退出码 ", "Node bootstrap failed (exit code ") + nc + T("）：", "): ") + Dsht.Platform.Linux.LinuxToolchainQuery.LastError);
                    Console.WriteLine(verb + "_NODE_HINT " + T("可改用系统包管理器：sudo apt install -y nodejs npm", "or use your package manager: sudo apt install -y nodejs npm"));
                    Console.WriteLine(verb + "_OBSERVED not-installed");
                    return 0;
                }
                Console.WriteLine(verb + "_NODE_OK " + nv + T("（免 sudo，装在 ~/.local/node）", " (no sudo, installed under ~/.local/node)"));
            }
            // --list: print the available versions verbatim (the classic line could do this; V3 could not).
            if (Has(args, "--list"))
            {
                string vers = tc.NpmViewVersions();
                if (string.IsNullOrEmpty(vers)) Console.WriteLine(verb + "_LIST_FAIL " + T("拿不到版本列表（离线或 npm 不可用）", "could not list versions (offline or npm unavailable)"));
                else Console.WriteLine(verb + "_VERSIONS " + vers.Replace(Environment.NewLine, " ").Trim());
                return 0;
            }
            // --version: install one explicit version (still whitelisted - it is pasted into a command line).
            string wantVersion = NpmVersionGuard.Normalize(FlagOf(args, "--version"));
            if (wantVersion.Length > 0 && !NpmVersionGuard.IsSafe(wantVersion))
            {
                Console.WriteLine(verb + "_FAIL " + T("指定的版本号未通过白名单：", "the requested version failed the whitelist: ") + wantVersion);
                Console.WriteLine(verb + "_OBSERVED " + (installed.Length > 0 ? installed : "not-installed"));
                return 0;
            }
            string channel = _cfg == null ? "rc" : _cfg.UpdateChannel;
            string latest = wantVersion.Length > 0 ? wantVersion : VersionForChannel(tc, channel);
            if (wantVersion.Length == 0) Console.WriteLine(verb + "_CHANNEL " + (string.IsNullOrEmpty(channel) ? "rc" : channel) + " -> " + (latest.Length == 0 ? "unknown" : latest));
            if (!NpmVersionGuard.IsSafe(latest))
            {
                Console.WriteLine(verb + "_FAIL " + T("拿不到可信的最新版本（离线，或 npm 返回值未通过白名单）", "no trustworthy latest version (offline, or the npm value failed the whitelist)"));
                Console.WriteLine(verb + "_OBSERVED " + (installed.Length > 0 ? installed : "not-installed"));
                return 0;
            }
            string target = "@deepseek-ai/dsh@" + latest;
            string registry = tc.NpmRegistryConfig();
            Console.WriteLine(verb + "_PLAN " + T("将执行：npm install -g ", "will run: npm install -g ") + target + (string.IsNullOrEmpty(registry) ? "" : " --registry " + registry));
            if (!Has(args, "--yes"))
            {
                Console.WriteLine(verb + "_DRYRUN " + T("（确认请加 --yes；这会真的改动全局 npm 包）", "(add --yes to confirm; this really changes global npm packages)"));
                Console.WriteLine(verb + "_OBSERVED " + (installed.Length > 0 ? installed : "not-installed"));
                return 0;
            }
            // Safety net for updates: take a -pre-update data backup BEFORE touching npm, so a broken
            // new version cannot cost the user their data. The classic v2.x line had this and V3 did
            // not (BackupKind.PreUpdate existed but nothing ever created one).
            if (update && !string.IsNullOrEmpty(installed))
            {
                try
                {
                    Dsht.Domain.Abstractions.IBackupSource bks = reg.Get<Dsht.Domain.Abstractions.IBackupSource>();
                    Dsht.Domain.Model.BackupResult pb = bks.Create(reg.Get<Dsht.Domain.Abstractions.IPaths>().DataRoot, Dsht.Domain.Model.BackupKind.PreUpdate, _cfg == null ? 3 : _cfg.KeepBackups, WorkspaceRoot(reg));
                    if (pb != null && !string.IsNullOrEmpty(pb.Path)) { Console.WriteLine(verb + "_PRE_BACKUP " + pb.Path); AddContentHashToMarker(pb.Path); }   // 回滚锚点也要能自证完整 ✓✓
                    // Record the version we are about to replace, NEXT TO the package (a file inside it would
                    // be restored into the data root). This is what makes a rollback candidate knowable.
                    if (pb != null && !string.IsNullOrEmpty(pb.Path))
                    {
                        try { System.IO.File.WriteAllText(pb.Path + ".version", installed); } catch { }
                    }
                    else Console.WriteLine(verb + "_PRE_BACKUP_FAILED " + T("更新前备份未能创建（数据根不可读？）", "pre-update backup could not be created (data root unreadable?)"));
                }
                catch (Exception bex) { Console.WriteLine(verb + "_PRE_BACKUP_FAILED " + bex.Message); }
            }
            int code = tc.NpmInstallGlobal(target, registry);
            string after = reg.Get<IToolchainQuery>().DshVersion();
            // Success means the OBSERVED version is the one we asked for. "dsh is installed" is NOT enough:
            // when the install fails the old version is still there, so that test reported a false OK.
            bool reachedTarget = !string.IsNullOrEmpty(after) && (after == latest || after.IndexOf(latest, StringComparison.Ordinal) >= 0);
            if (reachedTarget)
            {
                Console.WriteLine(verb + "_OK " + after + T("（复检已观测到 dsh）", " (dsh observed after the run)"));
                Console.WriteLine(verb + "_OBSERVED " + after);
                return 0;
            }
            // Roll back to the version we recorded before the update, then re-check by observation.
            if (update && !string.IsNullOrEmpty(installed))
            {
                Console.WriteLine(verb + "_ROLLBACK_TRY " + installed);
                int rc = tc.NpmInstallGlobal("@deepseek-ai/dsh@" + installed, registry);
                string back = reg.Get<IToolchainQuery>().DshVersion();
                if (!string.IsNullOrEmpty(back) && back.IndexOf(installed, StringComparison.Ordinal) >= 0)
                {
                    Console.WriteLine(verb + "_ROLLBACK_OK " + back);
                    Console.WriteLine(verb + "_OBSERVED " + back);
                    return 0;
                }
                Console.WriteLine(verb + "_ROLLBACK_FAILED " + T("回滚后仍未观测到旧版本；请手动执行 npm install -g @deepseek-ai/dsh@", "old version still not observed after rollback; run npm install -g @deepseek-ai/dsh@") + installed + T("（npm 退出码 ", " (npm exit code ") + rc + "）");
            }
            Console.WriteLine(verb + "_FAIL " + T("npm 退出码 ", "npm exit code ") + code + T("，且复检仍未观测到 dsh", ", and dsh is still not observed"));
            Console.WriteLine(verb + "_OBSERVED not-installed");
            return 0;
        }
        /// <summary>uninstall（V3 独有）：卸载 dsh（**不动数据目录**）。同样：计划 → 闸门 → npm → 复检。</summary>
        private static int UninstallCmd(string[] args, ServiceRegistry reg)
        {
            IToolchainQuery tc = reg.Get<IToolchainQuery>();
            string which = tc.WhichDsh();
            string installed = tc.DshVersion();
            if (string.IsNullOrEmpty(which) && string.IsNullOrEmpty(installed))
            {
                Console.WriteLine("UNINSTALL_SKIP " + T("没有观测到已安装的 dsh", "no installed dsh observed"));
                Console.WriteLine("UNINSTALL_OBSERVED not-installed");
                return 0;
            }
            Console.WriteLine("UNINSTALL_PLAN " + T("将执行：npm uninstall -g @deepseek-ai/dsh（只移除 dsh 程序，**不动**你的数据目录与备份）", "will run: npm uninstall -g @deepseek-ai/dsh (removes the program only; your data root and backups are NOT touched)"));
            if (!Has(args, "--yes"))
            {
                Console.WriteLine("UNINSTALL_DRYRUN " + T("（确认请加 --yes）", "(add --yes to confirm)"));
                Console.WriteLine("UNINSTALL_OBSERVED " + (installed.Length > 0 ? installed : "installed"));
                return 0;
            }
            int code = tc.NpmUninstallGlobal();
            string after = reg.Get<IToolchainQuery>().DshVersion();
            if (string.IsNullOrEmpty(after) && string.IsNullOrEmpty(tc.WhichDsh()))
            {
                Console.WriteLine("UNINSTALL_OK");
            OpLog(reg, "INFO", "uninstall OK");
                Console.WriteLine("UNINSTALL_OBSERVED not-installed");
                return 0;
            }
            Console.WriteLine("UNINSTALL_FAIL " + T("npm 退出码 ", "npm exit code ") + code + T("（复检仍观测到 dsh）", " (dsh still observed)"));
            Console.WriteLine("UNINSTALL_OBSERVED " + (after.Length > 0 ? after : "installed"));
            // ★ A2（2026-10-07 备份链审查）：UNINSTALL_FAIL → 1 ✓（SKIP/DRYRUN 保持 0 ✓ 它们不是失败 ✓）
            return 1;
        }
        /// <summary>从**启动日志**里取出 dsh 打印的那个 URL ✓✓
        /// 为什么必须用它：dsh web 打印的是 `http://127.0.0.1:<port>/?token=…` ✓
        /// —— **裸端口会认证失败** ✗（2026-09-30 真机反馈："dsh web authentication required; reopen the URL
        /// printed by dsh web" ✓）。启动日志由平台层写（LinuxServiceControl.LastLogPath ✓）。
        /// 取不到就返回空串 ✓ —— 不猜、不拼一个可能错的 URL ✗</summary>
        private static string StartUrlFromLog()
        {
            try
            {
                // ✗ 原来写死 Linux 的 LastLogPath → **Windows 上取不到** ✗（真机：Windows 侧日志根本不存在 ✓）
                // 改成**平台中立**的同一路径 ✓✓ 两个平台的平台层都往这里写 ✓
                string logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-minato-start.log");
                if (string.IsNullOrEmpty(logPath)) return "";
                string text = "";
                try { if (System.IO.File.Exists(logPath)) text = System.IO.File.ReadAllText(logPath); } catch { return ""; }
                System.Text.RegularExpressions.Match m =
                    System.Text.RegularExpressions.Regex.Match(text, @"http://[^\s""']*\?token=[^\s""']+");
                return m.Success ? m.Value : "";
            }
            catch { return ""; }
        }
        private static int StartCmd(string[] args, ServiceRegistry reg)
        {
            IServiceTarget target = TargetForStart(args, reg);   // 先解析 --port 再选目标（顺序敏感）
            IServiceControl ctl = reg.Get<IServiceControl>();

            ServiceReport before = target.Probe();
            string st = before.State.ToString();
            if (ServiceControlPolicy.BeforeStart(st, before.Pid) == StartDecision.AlreadyRunning)
            {
                // 身份校验 ✓：端口上有人监听 ≠ 那就是 dsh ✗（实测被 python http.server 占用时曾报 START_OK ✗✗）。
                // PID 可得时必须确认是 dsh（与 stop 用同一道校验 ✓）；PID 不可得时如实说明未能确认 ✓，不默认它是 dsh ✗。
                if (before.Pid > 0 && !reg.Get<IProcessQuery>().IsDshCommandLine(before.Pid))
                {
                    Console.WriteLine("START_FAIL " + T("端口被非 dsh 进程占用（PID ", "that port is held by a non-dsh process (PID ") + before.Pid + T("）；请先停掉它或换一个端口", "); stop it first or choose another port"));
                    Console.WriteLine("START_OBSERVED " + st.ToLowerInvariant() + " " + T("（端口被占用，但不是 dsh）", "(port in use, but not by dsh)"));
                    OpLog(reg, "ERROR", "start refused: port held by non-dsh pid " + before.Pid);
                    return 0;
                }
                Console.WriteLine("START_OK " + (before.Pid > 0 ? before.Pid.ToString() : "0"));
                { string su2 = StartUrlFromLog(); if (su2 != "") Console.WriteLine("START_URL " + su2); else Console.WriteLine("START_URL_UNKNOWN " + T("已在运行，但读不到 dsh 打印的带 token 地址；请在启动它的终端里复制那条地址（裸端口会认证失败）", "already running, but the token URL could not be read; copy it from the terminal that started dsh (a bare port fails authentication)")); }
            OpLog(reg, "INFO", "start OK (already running)");
                Console.WriteLine("START_OBSERVED " + st.ToLowerInvariant() + (before.Pid > 0 ? T("（观测到已在运行，未重复启动）", "(already running; not started again)") : T("（观测到已在运行；PID 不可得，未能确认身份）", "(already running; no PID available, identity unconfirmed)")));
                return 0;
            }

            if (!Has(args, "--yes"))
            {
                Console.WriteLine("START_PLAN " + T("将启动：dsh（profile ", "will start dsh (profile ") + ArgOr(args, "--profile", "web") + T(" / 端口 ", " / port ") + ArgOr(args, "--port", WebPort.ToString()) + T("）—— 这会改变系统状态，需要显式确认。", ") - this changes system state and needs explicit confirmation."));
                Console.WriteLine("START_NOTE " + T("确认请加 --yes；可用 --port <n> 指定端口（测试时务必用非默认端口）。", "add --yes to confirm; --port <n> to pick a port (always use a non-default port when testing)."));
                Console.WriteLine("START_OBSERVED " + before.State.ToString().ToLowerInvariant());
                return 0;
            }
            int port = 3080;
            string profile = "web";
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--port") { int pp; if (int.TryParse(args[i + 1], out pp)) port = pp; }
                else if (args[i] == "--profile") profile = args[i + 1];
            }

            string file, cmdArgs;
            if (PlatformIsWindows())
            {
                // ★ 审查抓到：profile 直接来自参数 ✗ 未校验就拼进 cmd.exe ✗
                //   → `start --profile "web & <任意命令>"` 会执行它 ✓（同样的值在 bridge-install / profilepatch 里是**有白名单**的 ✓）
                // ✓ 现在：与那两处**同一条白名单** ✓✓
                if (!System.Text.RegularExpressions.Regex.IsMatch(profile ?? "", @"^[A-Za-z0-9._\-]+$"))
                {
                    Console.WriteLine("START_FAIL " + T("profile 名不合法（只允许字母数字 . _ -）：" + profile, "invalid profile name: " + profile));
                    return 0;
                }
                // ★★ 架构审查抓到（v2 对等）：这里用**裸 `dsh`** ✗ 而 v2 用的是**解析后的完整路径** ✓
                //   → winget/npm 刚装完、当前会话 PATH 未刷新时 ✗ → **start 会失败** ✗（v2 不会 ✓）
                //   → Linux 分支本来就用了 ResolveDsh ✓（两边不一致 ✗）
                // ✓ 现在：**Windows 也用解析后的路径** ✓✓（拿不到就退回裸 `dsh` ✓ 不猜 ✓）
                string dshExe = null;
                try { dshExe = ResolveDsh(reg); } catch { }
                file = "cmd.exe";                                   // npm 的 dsh 是 .cmd 垫片，必须经 cmd 包装
                if (!string.IsNullOrEmpty(dshExe))
                    cmdArgs = "/c \"" + dshExe + "\" --profile " + profile + " --port " + port;
                else
                    cmdArgs = "/c dsh --profile " + profile + " --port " + port;
            }
            else
            {
                file = ResolveDsh(reg);                              // 主动解析（免 sudo 引导的 node 其 bin 不在非交互 PATH 里 ✗）
                cmdArgs = "--profile " + profile + " --port " + port;
            }

            int pid; string err;
            bool launched = ctl.StartDetached(file, cmdArgs, null, out pid, out err);
            if (!launched)
            {
                Console.WriteLine("START_FAIL " + T("启动命令未能发出：", "could not launch: ") + err);
                Console.WriteLine("START_OBSERVED down");
                return 0;
            }
            Console.WriteLine("START_LAUNCHED " + pid + " " + T("（命令已发出，正在用可观测事实确认…）", "(launched; verifying by observation…)"));

            // 等最多 15 秒，用端口/HTTP 观测确认（不猜）
            for (int i = 0; i < 15; i++)
            {
                System.Threading.Thread.Sleep(1000);
                ServiceReport now = target.Probe();
                string s2 = now.State.ToString();
                if (ServiceControlPolicy.AfterLaunch(s2, now.Pid, pid) == StartOutcome.Started)
                {
                    Console.WriteLine("START_OK " + (now.Pid > 0 ? now.Pid.ToString() : pid.ToString()));
            OpLog(reg, "INFO", "start OK");
                    Console.WriteLine("START_OBSERVED " + s2.ToLowerInvariant());
                    string su = StartUrlFromLog();
                    if (su != "") Console.WriteLine("START_URL " + su);
                    return 0;
                }
            }
            ServiceReport last = target.Probe();
            Console.WriteLine("START_FAIL " + T("命令已发出但 30 秒内未观测到端口/HTTP 就绪；子进程输出见 ", "launched but not observed ready within 30s; child output: ") + System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-minato-start.log"));
            Console.WriteLine("START_OBSERVED " + last.State.ToString().ToLowerInvariant() + " " + last.Basis);
            return 0;
        }
        /// <summary>stop（V3 独有）：按**观测到的 PID** 结束 dsh，再用观测确认真的停了。
        /// 标记行：`STOP_OK <pid>` / `STOP_FAIL <原因>` + `STOP_OBSERVED <状态>`。</summary>
        private static int StopCmd(string[] args, ServiceRegistry reg)
        {
            IServiceTarget target = reg.Get<IServiceTarget>();
            // --port：把目标临时指向指定端口。**没有这个开关时 stop 只会认默认 3080（用户的实例）**，
            // 而 DSH_HOME 隔离不隔离端口 —— 这是踩过的坑，所以测试必须能指定端口。
            int portArg = 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--port") { int pp; if (int.TryParse(args[i + 1], out pp)) portArg = pp; }
            }
            if (portArg > 0)
            {
                target = PlatformComposition.WebFor(portArg, reg.Get<IPortProbe>(), reg.Get<IHttpProbe>(), reg.Get<IProcessQuery>());
            }
            // `--target web|desktop`：**两个都开着时可以选停一个** ✓✓（用户要求 ✓）
            // 默认 web ✓ = 原行为 ✓ 向后兼容 ✓（不加这个参数时一个字都不变 ✓）
            string stopTarget = ArgOr(args, "--target", "web").Trim().ToLowerInvariant();
            if (stopTarget == "desktop")
            {
                int dpid = 0;
                try { dpid = reg.Get<IProcessQuery>().PidOfNamed("DeepSeek Harness"); } catch { }
                if (dpid <= 0)
                {
                    Console.WriteLine("STOP_FAIL " + T("没有检测到官方桌面端进程（DeepSeek Harness）", "no desktop app process (DeepSeek Harness) found"));
                    Console.WriteLine("STOP_OBSERVED down");
                    return 0;
                }
                if (dpid == System.Diagnostics.Process.GetCurrentProcess().Id)
                {
                    Console.WriteLine("STOP_FAIL " + T("拒绝执行：那是本程序自己（安全保护）", "refused: that is this program itself (safety)"));
                    return 0;
                }
                if (!Has(args, "--yes"))
                {
                    Console.WriteLine("STOP_PLAN " + T("将停止**官方桌面端** PID ", "will stop the **desktop app** PID ") + dpid);
                    Console.WriteLine("STOP_NOTE " + T("确认请加 --yes。它和 web 是两个独立的东西：本命令只停桌面端，不动 3080 上的 web。", "add --yes. The desktop app and dsh web are separate; this only stops the desktop app."));
                    return 0;
                }
                IServiceControl dctl = reg.Get<IServiceControl>();
                string derr;
                // Electron 是**多进程** ✗ → 必须杀**整棵** ✓（StopTree ✓ 与 Windows 侧实现一致 ✓）
                bool dok = dctl.StopTree(dpid, out derr);
            // ★★ 第 3 轮审查抓到：Windows 的 StopTree **只看 taskkill 的退出码** ✗ 从未复检进程是否真的没了 ✗
            //   → 却照样打印 STOP_OBSERVED down ✓（**断言了一个没做过的观测** ✗）
            //   → 而 Linux 侧本来就复检 /proc ✓（两边不一致 ✓）
            // ✓ 现在：**这里真的复核一次** ✓✓（进程还在 → 不算 down ✓ 只报 unknown ✓ 不撒谎 ✓）
            if (dok)
            {
                try
                {
                    using (System.Diagnostics.Process stillThere = System.Diagnostics.Process.GetProcessById(dpid))
                    {
                        if (stillThere != null && !stillThere.HasExited) dok = false;
                    }
                }
                catch { }
            }
                Console.WriteLine(dok ? "STOP_OK " + dpid : "STOP_FAIL " + (string.IsNullOrEmpty(derr) ? T("停止失败", "stop failed") : derr));
                Console.WriteLine("STOP_OBSERVED " + (dok ? "down" : "unknown"));
                if (dok) OpLog(reg, "INFO", "stop desktop pid=" + dpid);
                return 0;
            }
            ServiceReport r = target.Probe();
            if (ServiceControlPolicy.BeforeStop(r.State.ToString(), r.Pid) == StopDecision.NothingToStop)
            {
                Console.WriteLine("STOP_FAIL " + T("没有观测到在运行的 dsh（端口未监听）", "no running dsh observed (port not listening)"));
                Console.WriteLine("STOP_OBSERVED down");
                return 0;
            }
            if (!Has(args, "--yes"))
            {
                Console.WriteLine("STOP_PLAN " + T("将停止 PID ", "will stop PID ") + r.Pid + T("（端口 ", " (port ") + (portArg > 0 ? portArg.ToString() : "3080") + T("）—— 这会中断正在运行的服务，需要显式确认。", ") - this interrupts a running service and needs explicit confirmation."));
                Console.WriteLine("STOP_NOTE " + T("确认请加 --yes。注意：DSH_HOME 隔离不隔离端口，测试时绝不要对默认端口执行本命令。", "add --yes to confirm. Note: isolating DSH_HOME does NOT isolate the port - never run this against the default port in a test."));
                return 0;
            }
            IServiceControl ctl = reg.Get<IServiceControl>();
            string err;
            // ---- 安全闸门（真机复盘后加的）：绝不对可疑 PID 下手 ----
            if (r.Pid <= 1)
            {
                Console.WriteLine("STOP_FAIL " + T("拒绝执行：观测到的 PID ", "refused: observed PID ") + r.Pid + T(" 不可能是 dsh（安全保护）", " cannot be dsh (safety guard)"));
                return 0;
            }
            if (r.Pid == System.Diagnostics.Process.GetCurrentProcess().Id)
            {
                Console.WriteLine("STOP_FAIL " + T("拒绝执行：那是本程序自己（安全保护）", "refused: that is this program itself (safety guard)"));
                return 0;
            }
            if (!reg.Get<IProcessQuery>().IsDshCommandLine(r.Pid) && Has(args, "--force")) OpLog(reg, "WARN", "stop used --force on a non-dsh listener pid " + r.Pid);
            if (!reg.Get<IProcessQuery>().IsDshCommandLine(r.Pid) && !Has(args, "--force"))
            {
                Console.WriteLine("STOP_FAIL " + T("监听该端口的进程不是 dsh（PID ", "the process on that port is not dsh (PID ") + r.Pid + T("）；如确认要停，请加 --force", "); add --force to stop it anyway"));
                return 0;
            }            bool ok = ctl.StopTree(r.Pid, out err);
            // 再观测要**重试**：进程刚收到信号还没死透 ✗，立刻复探会看到"仍在监听"→ 假失败 ✗（真机抓到的）
            string st = "";
            for (int i = 0; i < 12; i++)
            {
                System.Threading.Thread.Sleep(1000);
                st = target.Probe().State.ToString();
                if (ServiceControlPolicy.AfterStop(st) == StopOutcome.Stopped) break;
            }
            if (ServiceControlPolicy.AfterStop(st) == StopOutcome.Stopped)
            {
                Console.WriteLine("STOP_OK " + r.Pid);
            OpLog(reg, "INFO", "stop OK pid " + r.Pid);
                Console.WriteLine("STOP_OBSERVED down");
                return 0;
            }
            Console.WriteLine("STOP_FAIL " + (ok ? T("进程已结束但端口仍在监听（12 秒后仍未观测到停止）", "process gone but the port is still listening (still not stopped after 12s)") : (string.IsNullOrEmpty(err) ? T("结束进程失败（未给出原因）", "failed to stop the process (no reason given)") : err)));
            Console.WriteLine("STOP_OBSERVED " + st.ToLowerInvariant());
            return 0;
        }
        /// <summary>selftest：写自检报告并打印 report -> 路径。内容逐条对齐 v2.x 的 Selftest。</summary>
        private static int SelfTest(string[] args, ServiceRegistry reg)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("== DeepSeek Harness Toolkit selftest ==");   // 同上
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                System.Reflection.AssemblyTitleAttribute title = (System.Reflection.AssemblyTitleAttribute)System.Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyTitleAttribute));
                System.Reflection.AssemblyCompanyAttribute company = (System.Reflection.AssemblyCompanyAttribute)System.Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyCompanyAttribute));
                System.Reflection.AssemblyDescriptionAttribute desc = (System.Reflection.AssemblyDescriptionAttribute)System.Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyDescriptionAttribute));
                sb.AppendLine("title   : " + (title == null ? "(null)" : title.Title));
                sb.AppendLine("company : " + (company == null ? "(null)" : company.Company));
                sb.AppendLine("desc    : " + (desc == null ? "(null)" : desc.Description));
                sb.AppendLine("version : " + asm.GetName().Version);
                sb.AppendLine("ui lang : " + System.Globalization.CultureInfo.CurrentUICulture.Name);

                IToolchainQuery tc = reg.Get<IToolchainQuery>();
                IPortProbe ports = reg.Get<IPortProbe>();
                IPaths paths = reg.Get<IPaths>();
                string dshLoc = tc.WhichDsh();
                sb.AppendLine("dsh installed (live): " + (dshLoc != null));

                sb.AppendLine("port 1 (expect False): " + ports.IsOpen(1, 500));
                bool selfOpen;
                System.Net.Sockets.TcpListener l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                l.Start();
                int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
                selfOpen = ports.IsOpen(port, 500);
                l.Stop();
                sb.AppendLine("self-listener (expect True): " + selfOpen);

                sb.AppendLine("dsh loc  : " + (dshLoc == null ? "(null)" : dshLoc));
                string nodeVer = tc.NodeVersion();
                sb.AppendLine("node ver : " + (string.IsNullOrEmpty(nodeVer) ? "(empty)" : nodeVer));
                sb.AppendLine("state dir: " + paths.StateDir);
                sb.AppendLine("data root: " + paths.DataRoot);
            }
            catch (Exception ex) { sb.AppendLine("EXCEPTION: " + ex); }

            string report = args.Length > 1 ? args[1] : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh_selftest.txt");
            try
            {
                System.IO.File.WriteAllText(report, sb.ToString(), new System.Text.UTF8Encoding(true));
                Console.WriteLine("report -> " + report);
            }
            catch (Exception ex) { Console.WriteLine("write report failed: " + ex.Message); }
            return 0;
        }
    }
}
