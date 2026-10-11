using System;
using System.Collections.Generic;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;
using Dsht.Domain.Services;

namespace Dsht.Cli
{
    /// <summary>更新中心、日志、配置与自启/快捷方式相关命令 ✓。
    /// 架构审计（S1）：从 Program.cs 原样搬出来，逻辑一行没改 ✓
    /// 边界用括号配平找，逆序删除避免行号漂移 ✓</summary>
    public static partial class Program
    {
        /// <summary>按更新通道选版本 ✓（配置 update_channel，经典版同名设置项 ✓）：
        /// stable = 最新**非预发布**版 ✓；rc = 最新版（含预发布 ✓，即 npm 的 latest 标签）。
        /// npm view versions 的输出是**升序**的 ✓，所以取最后一个匹配项即最新 ✓（不自己比版本号 ✗，避免 0.1.7 vs 0.1.10 这类字典序陷阱 ✗）。
        /// 列表取不到时回退到 latest ✓；调用方须如实说明来源 ✓。</summary>
        private static string VersionForChannel(IToolchainQuery tc, string channel)
        {
            string latest = NpmVersionGuard.Normalize(tc.NpmViewLatest());
            if (string.IsNullOrEmpty(channel) || channel != "stable") return latest;
            string list = tc.NpmViewVersions();
            if (string.IsNullOrEmpty(list)) return latest;
            System.Text.RegularExpressions.MatchCollection ms = System.Text.RegularExpressions.Regex.Matches(list, "[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.]+)?");
            string best = "";
            for (int i = 0; i < ms.Count; i++)
            {
                string v = ms[i].Value;
                if (v.IndexOf((char)45) >= 0) continue;   // 预发布跳过 ✓
                if (!NpmVersionGuard.IsSafe(v)) continue;
                best = v;                                  // npm 输出升序 → 最后一个即最新 ✓
            }
            if (best.Length > 0) return best;
            // 没有任何非预发布版（dsh 至今全是 -rc.x ✓）→ 如实说明这次回退 ✓，不静默把 rc 当 stable 交付 ✗
            Console.WriteLine("CHANNEL_NOTE " + T("stable 通道下没有任何非预发布版，已回退到最新预发布版 ", "no non-prerelease version exists on the stable channel; falling back to the newest prerelease ") + latest);
            return latest;
        }
        /// <summary>update-info（V3 独有，只读）：经典版「更新中心」的 CLI 对应物 ✓。
        /// 标记行：UPDATEINFO_INSTALLED/LATEST/REGISTRY/STATE/PRE_BACKUP/PRE_BACKUP_VERSION/ROLLBACK。</summary>
        /// <summary>update-center（V3 独有，**只读** ✓）：四个组件的更新一览 ✓✓
        /// （用户要求："检查 webui / desktop / dsh-minato / 已安装插件的更新列表和版本，如果能获取更新日志那最好了" ✓）
        /// 标记行：
        ///   `UPDATECENTER_OK <n>`
        ///   `UPDATECENTER_ITEM <id> kind=<webui|desktop|minato|plugin> installed=<v|unknown> latest=<v|unknown> state=<up-to-date|update-available|unknown|external>`
        ///     ★ 插件的 state 另有一组（2026-10-11 ✓ 基准 = **随包那一份** ✓ 离线 ✓）：
        ///       `not-installed` ✓ / `up-to-date`✓（版本相同**且**内容一致）/ `update-available`（版本低于随包）/
        ///       `content-mismatch`（**版本相同但 snapshot.js/index.js 内容哈希不同** ✓ 半旧 ✓）/
        ///       `newer-than-bundled`（版本高于随包 ✓ 如实说）/ `unknown`（随包缺 plugin 目录 / 哈希比不了 ✓ 见 DIAG）
        ///     ✗ ITEM 的字段名与 kind **一个都没动** ✗（只把 latest/state 填成真值 ✓）
        ///   `UPDATECENTER_URL <id> <地址>`       ← GitHub / 官方安装页 ✓
        ///   `UPDATECENTER_NOTE <id> <说明>`      ← 更新前风险与确认要求 ✓
        ///   `UPDATECENTER_DIAG <id> <说明>`      ← **取不到最新版时的原始原因** ✓（状态码 / 超时 ✓ 2026-10-11 加 ✓
        ///                                          只在真没取到时出现 ✓ ✗ 不塞进 NOTE ✗ GUI 不认识 → 忽略 ✓）
        ///   `UPDATECENTER_LOG <id> <一行日志>`   ← 更新日志（best-effort ✓ 取不到就说取不到 ✓）
        /// 纪律：**只读** ✓ 不改任何东西 ✓；取不到的字段写 `unknown` ✓ **不猜** ✗</summary>
        private static int UpdateCenter(ServiceRegistry reg, string[] args)
        {
            IToolchainQuery tc = reg.Get<IToolchainQuery>();
            List<string> ids = new List<string>();
            List<string> lines = new List<string>();
            // ★★ --stream（2026-10-02，GUI 专用 ✓）：边算边打，`UPDATECENTER_OK <n>` 挪到**最后** ✓✓
            //   —— 杀软式"列表先出来、逐行出结果"（用户建议 ✓）。GUI 的 UpdParse 按前缀扫行、
            //   位置无关 ✓；`UPDATECENTER_BEGIN` 不在它的分支里 → 自动忽略 ✓。
            //   ★ 默认输出**逐字不变** ✓（OK 行仍在最前、行序不变 ✓）→ 契约与测试全不动 ✓✓
            bool stream = Has(args, "--stream");
            if (stream) Console.WriteLine("UPDATECENTER_BEGIN");
            System.Action<string> emit = stream ? (System.Action<string>)delegate(string l) { Console.WriteLine(l); } : null;

            // —— ① webui = dsh 本体（官方 npm 包 ✓）——
            // ★ 慢的网络查询**先起跑**（后台 ✓），快项先算、最后再等它 ✓✓
            //   --stream 时：desktop/minato/插件 秒出，webui 最后出 ✓（杀软式 ✓）
            //   默认行序不变 ✓：webui 的行仍排**最前** → 末尾 InsertRange(0) ✓✓
            //   线程安全 ✓：tc 只被这个后台任务用（②③④⑤ 都不碰 tc ✓）→ 无并发访问 ✓
            ids.Add("webui");
            System.Threading.Tasks.Task<List<string>> webuiT = System.Threading.Tasks.Task.Run(delegate
            {
                List<string> part = new List<string>();
                string dshInstalled = tc.DshVersion();
                if (string.IsNullOrEmpty(dshInstalled)) dshInstalled = "unknown";
                string dshLatest = VersionForChannel(tc, _cfg == null ? "rc" : _cfg.UpdateChannel);
                if (string.IsNullOrEmpty(dshLatest)) dshLatest = "unknown";
                string dshState = "unknown";
                if (dshInstalled != "unknown" && dshLatest != "unknown")
                    dshState = dshInstalled == dshLatest ? "up-to-date" : (Dsht.Domain.Services.VersionComparer.Compare(dshInstalled, dshLatest) < 0 ? "update-available" : "newer-than-latest");   // ★ 审查抓到：原来用字典序 ✗ → 1.9.0 会被判成比 1.10.0 新 ✗✓
                part.Add("UPDATECENTER_ITEM webui kind=webui installed=" + dshInstalled + " latest=" + dshLatest + " state=" + dshState);
                part.Add("UPDATECENTER_URL webui https://github.com/deepseek-ai/deepseek-harness");
                part.Add("UPDATECENTER_NOTE webui " + T("更新前会自动备份数据根并保留回滚点；需要你确认后才执行",
                    "updating backs up the data root first and keeps a rollback point; it runs only after you confirm"));
                return part;
            });

            // —— ② desktop = 官方桌面端（**只能去官方安装页** ✓ 用户指定 ✓）——
            string deskPath = null;
            try
            {
                string cand = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DeepSeek Harness", "DeepSeek Harness.exe");
                if (System.IO.File.Exists(cand)) deskPath = cand;
            }
            catch { }
            string deskVer = "unknown";
            if (deskPath != null)
            {
                try { System.Diagnostics.FileVersionInfo vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(deskPath); if (vi != null && !string.IsNullOrEmpty(vi.FileVersion)) deskVer = vi.FileVersion; } catch { }
            }
            ids.Add("desktop");
            List<string> deskPart = new List<string>();
            deskPart.Add("UPDATECENTER_ITEM desktop kind=desktop installed=" + deskVer + " latest=unknown state=" + (deskPath == null ? "unknown" : "external"));
            deskPart.Add("UPDATECENTER_URL desktop https://www.deepseek.com/en/harness/");
            deskPart.Add("UPDATECENTER_NOTE desktop " + T("官方桌面端不在本工具里更新 —— 去官方安装页自己下（本工具不重打包、也不改它）",
                "the desktop app is not updated here; download it from the official page"));
            AddPart(lines, emit, deskPart);

            // —— ③ minato = 本工具自己（**不影响数据** ✓ 用户要求 ✓）——
            string selfVer = "unknown";
            try { selfVer = DshtVersionString(); } catch { }
            ids.Add("minato");
            List<string> minatoPart = new List<string>();
            // ★★★ 2026-10-11 **本体自查更新** —— 修维护者那句「本体检查不了本体的更新」 ✓✓
            //   ✗ 原来这一行是写死的 `latest=unknown state=unknown` ✗ → 界面只能说
            //     「CLI 不查最新版」✓ 而 CLI 明明**能**查（FetchLatestRelease 早就在，只是没给它用 ✗）
            //   ✓ 现在：查 GitHub 的 **`/releases/latest`** ✓✓
            //     · 该端点**天然排除预发布** ✓ 正合"stable 只提示正式版" ✓（✗ 不自己排 tag 列表 ✗）
            //     · 版本号从**编译期常量**来（DshtVersionString ✓）→ 判定与 `version` 命令同源 ✓
            //       ✗ 不反射、不硬编码第二份版本号 ✗
            //   · 失败**绝不回落成 unknown 就完事** ✗：把超时/状态码原文写进 `UPDATECENTER_DIAG` ✓（各自独立 ✓
            //     本体查询失败**不影响** ①webui / ②desktop / ④插件 / ⑤更新日志 任何一行 ✓）
            //   · **只报告** ✓：不下载、不替换文件 ✗；下载入口仍由下面的 UPDATECENTER_URL 给 ✓
            string minatoTag = null, minatoWhy = null;
            FetchLatestStableTag("sakanamaru", "dsh-minato", out minatoTag, out minatoWhy);
            string minatoLatest = "unknown", minatoState = "unknown";
            if (!string.IsNullOrEmpty(minatoTag))
            {
                minatoLatest = minatoTag;
                minatoState = selfVer != "unknown"
                    ? (selfVer == minatoTag ? "up-to-date"
                        : (Dsht.Domain.Services.VersionComparer.Compare(selfVer, minatoTag) < 0 ? "update-available" : "newer-than-latest"))
                    : "unknown";
            }
            minatoPart.Add("UPDATECENTER_ITEM minato kind=minato installed=" + selfVer + " latest=" + minatoLatest + " state=" + minatoState);
            minatoPart.Add("UPDATECENTER_URL minato https://github.com/sakanamaru/dsh-minato");
            minatoPart.Add("UPDATECENTER_NOTE minato " + T("本工具的更新不碰你的数据（不动 ~/.dsh、不动备份、不动配置）；只报告、不自动更新（下载入口见上面的地址），需要你确认",
                "updating this tool does not touch your data; report only, never auto-updates; runs only after you confirm"));
            // ★ 失败/异常**单独一行说原因** ✓（✗ 不塞进 NOTE ✗ —— NOTE 是"更新前风险" ✓ 语义不同 ✓）
            //   · GUI 只解析 ITEM/URL/NOTE/LOG ✓ 这一行它**不认识 → 安全忽略** ✓（不破坏解析 ✓）
            //   · 只在"本该有 latest 却没有"时才打 ✓（绝不噪声 ✓ 也**绝不**拿它假装成功 ✗✓）
            string minatoDiag = null;
            if (!string.IsNullOrEmpty(minatoWhy))
            {
                minatoDiag = T("没取到最新版（只报告，因此无法判断是否有更新）：", "could not fetch the latest version (report only, so it cannot tell whether an update exists): ") + minatoWhy;
            }
            else if (string.IsNullOrEmpty(minatoTag) && selfVer == "unknown")
            {
                minatoDiag = T("连本工具自己的版本都没读出来，无法比对是否有更新", "this tool's own version could not be read, so there is nothing to compare against");
            }
            if (minatoDiag != null) minatoPart.Add("UPDATECENTER_DIAG minato " + minatoDiag);
            AddPart(lines, emit, minatoPart);

            // —— ④ 已安装插件（从各 profile 的 node_modules 扫 ✓ 取 GitHub 地址 ✓✓）——
            try
            {
                // I2 FIX (CLI audit MINOR): this hardcoded the real home directory, so under an isolated
            // DSH_HOME - the project's own testing mode - it listed plugins from the real profile.
            string dshHome = reg.Get<IPaths>().DataRoot;
                string profiles = System.IO.Path.Combine(dshHome, "profiles");   // N5 FIX: the extra segment made the plugin list always empty
                // ★★★ 2026-10-11 **桥接插件的基准**（维护者选定方案① ✓ 离线 ✓ 零新渠道 ✓）：
                //   基准 = **工具箱随包的那一份插件** = <exe 目录>\plugin\dsh-minato-bridge\ ✓
                //   （官方发布包带它 ✓；**开发构建常常没有** ✗ → 那时必须**如实降级** ✓ 不编 latest ✗）
                //   只算一次 ✓ **不联网** ✓ 不碰 ~\.dsh 的写 ✓（只读探测 ✓）
                BridgeBaseline bbl = LoadBundledBridge(SelfDir());
                if (System.IO.Directory.Exists(profiles))
                {
                    string[] profDirs = System.IO.Directory.GetDirectories(profiles);
                    for (int i = 0; i < profDirs.Length; i++)
                    {
                        string profName = System.IO.Path.GetFileName(profDirs[i]);
                        string nm = System.IO.Path.Combine(profDirs[i], "node_modules");
                        // ★ 桥接插件那一行**单独算** ✓；其余插件**逐字保持不变** ✓（既有行不动 ✓）
                        bool bridgeDone = false;
                        string[] pkgs = System.IO.Directory.Exists(nm) ? System.IO.Directory.GetDirectories(nm) : new string[0];
                        for (int k = 0; k < pkgs.Length; k++)
                        {
                            string pj = System.IO.Path.Combine(pkgs[k], "package.json");
                            if (!System.IO.File.Exists(pj)) continue;
                            string txt = null;
                            try { txt = System.IO.File.ReadAllText(pj); } catch { }
                            if (string.IsNullOrEmpty(txt)) continue;
                            string name = JsonStr(txt, "name");
                            string ver = JsonStr(txt, "version");
                            string repo = JsonStr(txt, "repository");
                            if (string.IsNullOrEmpty(name)) name = System.IO.Path.GetFileName(pkgs[k]);
                            if (string.IsNullOrEmpty(ver)) ver = "unknown";
                            string pid = "plugin:" + name;
                            ids.Add(pid);
                            // ★★★ 2026-10-11：**桥接插件**走真比对 ✓（基准 = 随包那一份 ✓ 双比对 = 版本号 + 内容哈希 ✓）
                            //   其余插件**逐字保持原样** ✓（它们确实没有独立版本渠道 ✓ 那条诚实的 NOTE 不动 ✓）
                            if (name == "dsh-minato-bridge")
                            {
                                bridgeDone = true;
                                if (string.IsNullOrEmpty(repo)) repo = NpmRepoOf(name);
                                AddPart(lines, emit, BridgeRow(bbl, profName, ver, CodeDirOf(pkgs[k], txt), repo));
                                continue;
                            }
                            List<string> plugPart = new List<string>();
                            plugPart.Add("UPDATECENTER_ITEM " + pid + " kind=plugin installed=" + ver + " latest=unknown state=unknown profile=" + System.IO.Path.GetFileName(profDirs[i]));
                            // package.json 没有 repository 时 **去问 npm** ✓✓（用户要求："插件尝试获取 GitHub 地址" ✓）
                            // 只在缺字段时才问 ✓（npm view 每次要 1~2 秒 ✗ 不能对每个插件都问 ✓）
                            if (string.IsNullOrEmpty(repo)) repo = NpmRepoOf(name);
                            plugPart.Add("UPDATECENTER_URL " + pid + " " + (string.IsNullOrEmpty(repo) ? "unknown" : repo));
                            plugPart.Add("UPDATECENTER_NOTE " + pid + " " + T("没有独立的版本渠道：插件随工具箱发布，它的版本号（package.json 的 version）与工具箱 tag（如 v3.0.7）不在一个序列上，因此没有可比的 latest —— 这里 `latest=unknown` 是事实，不是「还没查」；本工具也不会拿工具箱 tag 当插件的 latest。插件更新先描述风险再确认（版本变化可能改行为）；更新前自动备份。插件由各自作者维护，本工具不替它担保。",
                                "no independent release channel: this plugin ships with the toolkit, so its version (package.json) and the toolkit tag (e.g. v3.0.7) are not the same series - there is no comparable latest, so latest=unknown is a FACT, not a pending lookup, and this tool will not pass the toolkit tag off as the plugin's latest. plugin updates describe the risk and ask first; the data root is backed up"));
                            AddPart(lines, emit, plugPart);
                        }
                        // ★ 该 profile **是真 profile** 但没装桥接插件 → 也**如实报一行「未安装」** ✓
                        //   （✗ 不编一个已装版本 ✗ 不假报「已是最新」✗ 也不给杂物目录报 ✗ 见 LooksLikeProfile ✓）
                        if (!bridgeDone && LooksLikeProfile(profDirs[i]))
                        {
                            ids.Add("plugin:dsh-minato-bridge");
                            AddPart(lines, emit, BridgeRow(bbl, profName, "", "", ""));
                        }
                    }
                }
            }
            catch { }

            // —— ⑤ 更新日志（best-effort ✓ 从 GitHub Releases 取 ✓ 取不到就明说 ✓）——
            List<string> logPart = new List<string>();
            try
            {
                string log = FetchLatestRelease("deepseek-ai", "deepseek-harness");
                if (!string.IsNullOrEmpty(log)) logPart.Add("UPDATECENTER_LOG webui " + log);
                else logPart.Add("UPDATECENTER_LOG webui " + T("取不到更新日志（网络不可达或仓库没有 Releases）", "no changelog available"));
            }
            catch { logPart.Add("UPDATECENTER_LOG webui " + T("取不到更新日志", "no changelog available")); }
            AddPart(lines, emit, logPart);

            // —— ① 的结果现在才等（后台早就在跑 ✓）—— 快项已全部先出 ✓ webui 最后出 ✓
            List<string> webuiPart;
            try { webuiPart = webuiT.Result; }
            catch
            {
                webuiPart = new List<string>();
                webuiPart.Add("UPDATECENTER_ITEM webui kind=webui installed=unknown latest=unknown state=unknown");
                webuiPart.Add("UPDATECENTER_URL webui https://github.com/deepseek-ai/deepseek-harness");
                webuiPart.Add("UPDATECENTER_NOTE webui " + T("更新前会自动备份数据根并保留回滚点；需要你确认后才执行",
                    "updating backs up the data root first and keeps a rollback point; it runs only after you confirm"));
            }
            lines.InsertRange(0, webuiPart);   // ★ 默认行序：webui 仍在**最前** ✓ 逐字不变 ✓✓
            if (stream)
            {
                for (int w = 0; w < webuiPart.Count; w++) emit(webuiPart[w]);   // 流式：webui 最后出 ✓
            }
            Console.WriteLine("UPDATECENTER_OK " + ids.Count);   // 默认：汇总在**最前** ✓；流式：在**最后** ✓
            if (!stream)
            {
                for (int i = 0; i < lines.Count; i++) Console.WriteLine(lines[i]);
            }
            return 0;
        }

        /// <summary>把一段组件行并进总清单 ✓；流式时同时发射 ✓（--stream 用 ✓）。</summary>
        private static void AddPart(List<string> lines, System.Action<string> emit, List<string> part)
        {
            for (int i = 0; i < part.Count; i++) { lines.Add(part[i]); if (emit != null) emit(part[i]); }
        }
        /// <summary>问 npm 要某个包的仓库地址 ✓（package.json 缺 repository 时的兜底 ✓）。
        /// 找不到 npm / 查不到 → 返回空串 ✓ **不猜** ✗（上层会写 unknown ✓）。</summary>
        private static string NpmRepoOf(string pkg)
        {
            if (string.IsNullOrEmpty(pkg)) return "";
            // ★★★ 审查抓到：包名来自**插件自己的 package.json** ✗ → 未校验就拼进 cmd.exe ✗✗
            //   → `{"name":"x & calc"}` 这种名字会**执行任意命令** ✓（打开 GUI「更新」页即触发 ✓）
            //   → 项目对 registry 和 npm 版本都做了白名单 ✓ 唯独漏了包名 ✓
            // ✓ 现在：**npm 合法包名白名单** ✓✓（作用域名 + 包名 ✓ 不合规直接不问 npm ✓）
            if (!System.Text.RegularExpressions.Regex.IsMatch(pkg,
                    @"^(@[a-z0-9\-~][a-z0-9\-._~]*/)?[a-z0-9\-~][a-z0-9\-._~]*$"))
                return "";
            string[] tries = PlatformIsWindows()
                ? new string[] { "cmd.exe|/c npm view " + pkg + " repository.url", "npm.cmd|view " + pkg + " repository.url" }
                : new string[] { "npm|view " + pkg + " repository.url", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/node/bin/npm") + "|view " + pkg + " repository.url" };
            for (int i = 0; i < tries.Length; i++)
            {
                int bar = tries[i].IndexOf('|');
                if (bar <= 0) continue;
                string exe = tries[i].Substring(0, bar);
                string arg = tries[i].Substring(bar + 1);
                try
                {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(exe, arg);
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                    {
                        if (p == null) continue;
                        string outp = p.StandardOutput.ReadToEnd();
                        p.StandardError.ReadToEnd();
                        p.WaitForExit(8000);
                        if (string.IsNullOrEmpty(outp)) continue;
                        string[] ls = outp.Replace("\r\n", "\n").Split('\n');
                        for (int k = 0; k < ls.Length; k++)
                        {
                            string s = ls[k].Trim();
                            if (s.Length == 0) continue;
                            // npm 有时输出 git+https://…git → 去掉前缀后缀 ✓ 便于直接点开 ✓
                            if (s.StartsWith("git+", StringComparison.Ordinal)) s = s.Substring(4);
                            if (s.EndsWith(".git", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 4);
                            if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return s;
                        }
                    }
                }
                catch { }
            }
            return "";
        }

        // ================================================================ 桥接插件的「随包基准」（2026-10-11 ✓）
        // 维护者选定方案① ✓：**基准 = 工具箱随包的那一份插件** ✓（`<exe 目录>\plugin\dsh-minato-bridge\` ✓）
        //   ✓ 离线 ✓ 零新渠道 ✓ **不联网** ✓（本段代码里没有任何 HTTP / npm 调用 ✓ 只读本地文件 ✓）
        //   ✗ 不拿工具箱 tag 当插件的 latest ✗（那是编 ✗）✗ 不写 ~\.dsh ✗（只读 ✓）✗ 不改 ITEM 的字段名/kind ✗
        //   ✗ 不新增命令 ✗ 不新增标记前缀 ✗（GUI 已在按 kind=plugin 渲染 ✓）

        /// <summary>随包那一份桥接插件 = **基准** ✓。语义 =「**你这份工具箱所带的插件版本**」✓
        /// ✗ 不是网上的最新版 ✗（插件没有独立版本渠道 ✓ 本工具不拿工具箱 tag 顶替 ✗）。</summary>
        private sealed class BridgeBaseline
        {
            public bool DirExists;             // 随包目录在不在（源码编译/dev 构建常常没有 ✗）
            public string Dir = "";            // 随包目录（不管在不在都要有值 ✓ 报错文案要贴原文 ✓）
            public string Version = "unknown"; // 随包 package.json 的 version ✓ 读不到就 unknown ✓ 不编 ✗
            public bool LatestOk;              // 目录在 **且** version 读到了 → latest 可取 ✓
            public string CodeDir = "";        // 随包代码目录（snapshot.js / index.js 所在那一层 ✓）
            public string ShaSnapshot = "";    // 随包 snapshot.js 的 SHA-256（空 = 不可得 ✓）
            public string ShaIndex = "";       // 随包 index.js 的 SHA-256（空 = 不可得 ✓）
            public string Why = "";            // **取不到 latest** 的原始原因 ✓（→ UPDATECENTER_DIAG ✓ 不编 ✗）
            public string HashWhy = "";        // 随包**内容哈希**取不到的原始原因 ✓（→ 该行 NOTE ✓）
        }

        /// <summary>只读地读随包那一份（✗ 不联网 ✗ 不写盘 ✓）。
        /// 缺目录 / package.json 读不到 / 没有 version → `LatestOk=false` ✓ 并把**原始原因**记进 Why ✓。</summary>
        private static BridgeBaseline LoadBundledBridge(string selfDir)
        {
            BridgeBaseline b = new BridgeBaseline();
            try
            {
                b.Dir = System.IO.Path.Combine(System.IO.Path.Combine(selfDir, "plugin"), "dsh-minato-bridge");
                b.DirExists = System.IO.Directory.Exists(b.Dir);
                if (!b.DirExists)
                {
                    // ★ 这是**常见**情形（dotnet build / csc 全量编译 / 单独复制 exe ✓）→ 如实说 ✓ 不编 ✗
                    b.Why = T("工具箱自带的一份缺 plugin 目录（源码构建常见）：", "the bundled plugin folder is missing (normal for a source build): ") + b.Dir;
                    return b;
                }
                string pj = System.IO.Path.Combine(b.Dir, "package.json");
                string txt = null;
                try { if (System.IO.File.Exists(pj)) txt = System.IO.File.ReadAllText(pj); } catch { }
                if (string.IsNullOrEmpty(txt))
                {
                    b.Why = T("工具箱自带插件的 package.json 读不到：", "the bundled plugin's package.json cannot be read: ") + pj;
                    return b;
                }
                string v = JsonStr(txt, "version");
                if (string.IsNullOrEmpty(v))
                {
                    b.Why = T("工具箱自带插件的 package.json 里没有 version 字段：", "the bundled plugin's package.json has no version field: ") + pj;
                    return b;
                }
                b.Version = v;
                b.LatestOk = true;
                b.CodeDir = CodeDirOf(b.Dir, txt);
                b.ShaSnapshot = Sha256Hex(System.IO.Path.Combine(b.CodeDir, "snapshot.js"));
                b.ShaIndex = Sha256Hex(System.IO.Path.Combine(b.CodeDir, "index.js"));
                if (b.ShaSnapshot.Length == 0 || b.ShaIndex.Length == 0)
                    b.HashWhy = T("工具箱自带插件的内容文件读不到（只比 snapshot.js 与 index.js 两个文件）：",
                                  "the bundled plugin's content files cannot be read (only snapshot.js and index.js are compared): ") + b.CodeDir;
                return b;
            }
            catch (Exception ex)
            {
                b.Why = T("读工具箱自带插件时出错：" + ex.Message, "error while reading the bundled plugin: " + ex.Message);
                return b;
            }
        }

        /// <summary>插件的**代码目录**（snapshot.js / index.js 就在这一层 ✓）。依据 package.json 的 `main` ✓
        /// —— 实测两种真实布局都有 ✓（✗ 只看包根就会把旧布局误判成"文件不存在" ✗）：
        ///   ① 新版：`main=index.js` → 包根 ✓（web profile 实测就是 ✓）
        ///   ② 旧版：`main=plugin/dsh-minato-bridge/index.js` → 嵌套子目录 ✓（desktop profile 实测就是 ✓ 0.1.0 那份）
        /// main 缺失/不可信 → 依次退到 包根\index.js → 包根\plugin\dsh-minato-bridge\ ✓；都找不到按包根算 ✓
        /// （那时哈希算成"文件不存在" → 上层如实报**不一致** ✓ ✗ 不当作一致 ✗）。</summary>
        private static string CodeDirOf(string pkgDir, string pkgJson)
        {
            string main = JsonStr(pkgJson, "main");
            if (!string.IsNullOrEmpty(main))
            {
                string rel = main.Replace('/', '\\');
                int at = rel.LastIndexOf('\\');
                rel = at > 0 ? rel.Substring(0, at) : "";
                string cand = rel.Length > 0 ? System.IO.Path.Combine(pkgDir, rel) : pkgDir;
                try { if (System.IO.File.Exists(System.IO.Path.Combine(cand, "index.js"))) return cand; } catch { }
            }
            if (System.IO.File.Exists(System.IO.Path.Combine(pkgDir, "index.js"))) return pkgDir;
            string nested = System.IO.Path.Combine(System.IO.Path.Combine(pkgDir, "plugin"), "dsh-minato-bridge");
            if (System.IO.File.Exists(System.IO.Path.Combine(nested, "index.js"))) return nested;
            return pkgDir;
        }

        /// <summary>文件 SHA-256（**小写**十六进制 ✓ 与 PowerShell 的 `Get-FileHash -Algorithm SHA256` 同算法同编码 ✓）。
        /// 文件不存在 / 读不到 → 空串 ✓（✗ 不猜 ✗）；调用方必须把空串当「不可得」处理 ✓（见 CompareBridgeContent ✓）。</summary>
        private static string Sha256Hex(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return "";
                using (System.IO.FileStream fs = System.IO.File.OpenRead(path))
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(fs);
                    System.Text.StringBuilder sb = new System.Text.StringBuilder(h.Length * 2);
                    for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }

        /// <summary>哈希前缀（**显示用** ✓ 8 位 ✓ 只为让人一眼看出一致/不一致 ✗ 不是可校验的完整值 ✗）。</summary>
        private static string ShortHash(string h)
        {
            if (string.IsNullOrEmpty(h)) return T("（无）", "(none)");
            return h.Length <= 8 ? h : h.Substring(0, 8);
        }

        /// <summary>内容比对 = **双比对**的第二半 ✓：只比 `snapshot.js` 与 `index.js` 两个文件 ✓
        /// （✗ 不比 README / LICENSE / .bak / pdb 这些无关文件 ✗ —— 实测 desktop 那份里就躺着
        ///  `.bak-board3` / `.bak-pre307` ✗ 比它们只会误报 ✓）。
        /// 返回 1 = 两边一致 ✓；0 = **不一致** ✓（原因写进 why ✓）；-1 = **比不了** ✓（读不到 ✓ 原因写进 why ✓）。
        /// ★「内容一致」= **两个文件都相同** ✓ —— 少一个文件、或哈希不同，都**不算**一致 ✓。</summary>
        private static int CompareBridgeContent(string installedCodeDir, BridgeBaseline b, out string why)
        {
            why = "";
            if (!string.IsNullOrEmpty(b.HashWhy)) { why = b.HashWhy; return -1; }
            if (string.IsNullOrEmpty(installedCodeDir)) { why = T("拿不到已装插件的代码目录", "the installed plugin's code folder is unknown"); return -1; }
            string[] files = new string[] { "snapshot.js", "index.js" };
            for (int i = 0; i < files.Length; i++)
            {
                string ip = System.IO.Path.Combine(installedCodeDir, files[i]);
                string bp = System.IO.Path.Combine(b.CodeDir, files[i]);
                bool ie = false, be = false;
                try { ie = System.IO.File.Exists(ip); be = System.IO.File.Exists(bp); } catch { }
                if (!ie && !be) continue;   // 两边都没有这个文件 → 这一项一致 ✓（没什么可比 ✓ 也不因此误报 ✗）
                if (ie != be)
                {
                    why = T("文件集不同：" + files[i] + "（工具箱自带 " + (be ? "有" : "没有") + "，已装 " + (ie ? "有" : "没有") + "）",
                            "different file sets: " + files[i] + " (bundled " + (be ? "present" : "absent") + ", installed " + (ie ? "present" : "absent") + ")");
                    return 0;
                }
                string hs = Sha256Hex(ip), hb = Sha256Hex(bp);
                if (hs.Length == 0 || hb.Length == 0)
                {
                    why = T("内容读不到（文件在但 IO 出错）：" + ip, "content unreadable (the file exists but IO failed): " + ip);
                    return -1;
                }
                if (!string.Equals(hs, hb, StringComparison.OrdinalIgnoreCase))
                {
                    why = T("内容哈希不同：" + files[i] + "（工具箱自带 " + ShortHash(hb) + " ≠ 已装 " + ShortHash(hs) + "，两边都是 SHA-256）",
                            "content hash differs: " + files[i] + " (bundled " + ShortHash(hb) + " != installed " + ShortHash(hs) + ", both SHA-256)");
                    return 0;
                }
            }
            return 1;
        }

        /// <summary>桥接插件的**状态机**（各态触发条件 ✓ 2026-10-11）：
        ///   `unknown`            —— 随包缺 plugin 目录 / 随包 package.json 读不到 / 版本相同但内容哈希比不了 ✓（原因见 why ✓）
        ///   `not-installed`      —— 该 profile 里**找不到**这个插件 ✓
        ///   `update-available`   —— 已装版本 **低于** 随包 ✓
        ///   `newer-than-bundled` —— 已装版本 **高于** 随包 ✓（如实说 ✓ ✗ 不硬说成"有更新" ✗）
        ///   `up-to-date`         —— 版本相同 **且** 内容一致 ✓（snapshot.js + index.js 两个都相同 ✓）
        ///   `content-mismatch`   —— **版本相同但哈希不同** ✓（半旧 ✓ 只看版本号抓不住 ✓ 必须单独一态 ✓）
        /// why = 为什么不是"一致"的**原文** ✓（DIAG / NOTE 用 ✓ 不编 ✗）。</summary>
        private static string BridgeStateOf(string installedVer, string installedCodeDir, BridgeBaseline b, out string why)
        {
            why = "";
            if (!b.LatestOk) { why = b.Why; return "unknown"; }   // 随包基准不可用 → latest=unknown + state=unknown ✓
            if (string.IsNullOrEmpty(installedVer) || installedVer == "unknown") return "not-installed";
            int cmp = Dsht.Domain.Services.VersionComparer.Compare(installedVer, b.Version);
            if (cmp < 0) return "update-available";
            if (cmp > 0) return "newer-than-bundled";
            int c = CompareBridgeContent(installedCodeDir, b, out why);
            if (c == 0) return "content-mismatch";
            if (c < 0) return "unknown";
            return "up-to-date";
        }

        /// <summary>桥接插件**一行**：ITEM（字段名与 kind **逐字不变** ✓ 只把 latest/state 填成真值 ✓）
        /// + URL + NOTE（含**按 profile 分开**的修复指引 ✓）+ 必要时 DIAG ✓。</summary>
        private static List<string> BridgeRow(BridgeBaseline b, string profile, string installedVer, string installedCodeDir, string repo)
        {
            string why = "";
            string state = BridgeStateOf(installedVer, installedCodeDir, b, out why);
            string latest = b.LatestOk ? b.Version : "unknown";
            if (string.IsNullOrEmpty(installedVer)) installedVer = "unknown";
            List<string> part = new List<string>();
            part.Add("UPDATECENTER_ITEM plugin:dsh-minato-bridge kind=plugin installed=" + installedVer + " latest=" + latest + " state=" + state + " profile=" + profile);
            part.Add("UPDATECENTER_URL plugin:dsh-minato-bridge " + (string.IsNullOrEmpty(repo) ? "unknown" : repo));
            part.Add("UPDATECENTER_NOTE plugin:dsh-minato-bridge " + BridgeNote(b, profile, state, installedVer, latest, why));
            // ★ DIAG 的语义**保持不变** ✓：「本该有 latest 却取不到」时的**原始原因** ✓
            //   —— 于是只有**随包基准不可用**才打 ✓；内容哈希比不了的原因进 NOTE ✓（那时 latest 是有的 ✓ 不该报"取不到最新版"✗）
            if (state == "unknown" && !b.LatestOk && !string.IsNullOrEmpty(why))
                part.Add("UPDATECENTER_DIAG plugin:dsh-minato-bridge " + why);
            return part;
        }

        /// <summary>桥接插件那一行的 NOTE ✓：① 基准是什么 + 为什么仍然"没有独立版本渠道"（诚实口径**保留** ✓）
        /// ② 本次**实测结论**（逐态说清 ✓）③ **按 profile 分开**的修复指引 ✓（实测：desktop 由桌面端独占管理 ✗）。</summary>
        private static string BridgeNote(BridgeBaseline b, string profile, string state, string installedVer, string latest, string why)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(T("基准 = 你这份工具箱自带的插件（", "baseline = the plugin bundled with THIS toolkit ("));
            sb.Append(b.DirExists ? b.Dir : T("工具箱自带的目录缺失，见诊断行", "bundled folder missing - see the diagnostic line"));
            sb.Append(T("；离线可判，零新渠道）：latest 取的是工具箱自带那一份的版本，不是网上的最新版；插件仍然没有独立的版本渠道，它的版本号与工具箱 tag（如 v3.0.7）不在一个序列上，因此本工具不会拿工具箱 tag 当插件的 latest。判定 = 双比对：版本号（VersionComparer）+ 内容哈希（snapshot.js 与 index.js 各算 SHA-256，两个都相同才算内容一致；版本相同但哈希不同 = 半旧，单独一态，只看版本号抓不住）。",
                        " offline, no new channel): latest is the version of the copy bundled with this toolkit - NOT the newest version online. The plugin still has no independent release channel, so the toolkit tag is never passed off as the plugin's latest. The verdict uses a double comparison: version (VersionComparer) plus content hashes (SHA-256 of snapshot.js and index.js; both must match for the content to count as identical, and same-version-different-hash is reported as a state of its own). "));
            if (state == "not-installed")
                sb.Append(T("这台机器上：profile「" + profile + "」里找不到这个插件 → 未安装。",
                            "measured: the plugin is not present in profile '" + profile + "' -> not installed. "));
            else if (state == "up-to-date")
                sb.Append(T("这台机器上：已装 " + installedVer + " 与工具箱自带的一致（版本相同，snapshot.js 与 index.js 的内容哈希也相同）→ 已是最新。",
                            "measured: installed " + installedVer + " matches the bundled copy (same version and identical content hashes) -> up to date. "));
            else if (state == "update-available")
                sb.Append(T("这台机器上：已装 " + installedVer + " 低于工具箱自带的 " + latest + " → 有更新。",
                            "measured: installed " + installedVer + " is older than the bundled " + latest + " -> update available. "));
            else if (state == "newer-than-bundled")
                sb.Append(T("这台机器上：已装 " + installedVer + " 高于工具箱自带的 " + latest + " → 比工具箱自带还新（不当作「有更新」：你装的可能是更新的一份，或自己改过）。",
                            "measured: installed " + installedVer + " is newer than the bundled " + latest + " -> newer than the bundled copy (stated as fact, not spun as an update). "));
            else if (state == "content-mismatch")
                sb.Append(T("这台机器上：版本号与工具箱自带相同（都是 " + latest + "），但内容不一致 → 内容与工具箱自带不一致（半旧或被改过，版本号看不出来，所以本工具比内容）：" + why + "。",
                            "measured: the version equals the bundled one (" + latest + ") but the content differs -> content differs from the bundled copy: " + why + ". "));
            else
                sb.Append(T("这台机器上：比不了（" + why + "）→ 本工具不编 latest，也不报「已是最新」。",
                            "measured: the comparison is not possible (" + why + ") -> no invented latest and no false up-to-date. "));
            // ★ 修复指引**按 profile 分开** ✓（实测过：desktop **由桌面端独占管理** ✗ 命令行装不进也卸不掉 ✗）
            if (profile == "web")
                sb.Append(T("更新/安装方式：`bridge-install --profile web --yes`（写操作，会改该 profile 的 package.json 与 cordis.patch.yml，装前 dsh 自己会保留原状）；装完重启 dsh 才生效。",
                            "how to update/install: bridge-install --profile web --yes (a write; restart dsh afterwards). "));
            else if (profile == "desktop")
                sb.Append(T("更新/安装方式：该 profile 由 dsh 桌面端独占管理，命令行装不进也卸不掉；请通过桌面端 / 它的插件机制更新（桌面端接受本地目录路径）。",
                            "how to update/install: this profile is managed exclusively by the dsh desktop app - update it through the desktop app and its plugin mechanism. "));
            else
                sb.Append(T("更新/安装方式：本工具只报状态，更新请在对应客户端 / 插件机制里做。",
                            "how to update/install: this tool only reports the state; update it through the matching client or plugin mechanism. "));
            sb.Append(T("插件更新先描述风险再确认；更新前自动备份。插件由各自作者维护，本工具不替它担保。",
                        "plugin updates describe the risk and ask first; the data root is backed up before updating; plugins are maintained by their authors and this tool does not vouch for them."));
            return sb.ToString();
        }

        /// <summary>这个目录像不像**真的 profile** ✓：真 profile 都有 `package.json` 或 `cordis.patch.yml` ✓
        /// （实测：`~\.dsh\profiles\` 下还躺着 `node_modules` / `profiles` 这种杂物目录 ✗ 两个文件都没有 ✗
        ///   → **不给它们报「未安装」** ✓ 不制造噪声 ✗ 也不假装它们是 profile ✗）。</summary>
        private static bool LooksLikeProfile(string dir)
        {
            try
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "package.json"))) return true;
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "cordis.patch.yml"))) return true;
            }
            catch { }
            return false;
        }

        private static int UpdateInfo(ServiceRegistry reg)
        {
            IToolchainQuery tc = reg.Get<IToolchainQuery>();
            string installed = tc.DshVersion();
            if (string.IsNullOrEmpty(installed))
            {
                // 未安装时明确说"未安装"，不伪装成"最新" ✗
                if (string.IsNullOrEmpty(tc.WhichDsh())) { Console.WriteLine("UPDATEINFO_INSTALLED not-installed"); Console.WriteLine("UPDATEINFO_LATEST unknown"); Console.WriteLine("UPDATEINFO_STATE unknown"); return 0; }
            }
            Console.WriteLine("UPDATEINFO_INSTALLED " + (string.IsNullOrEmpty(installed) ? "unknown" : installed));
            Console.WriteLine("UPDATEINFO_CHANNEL " + (string.IsNullOrEmpty(_cfg == null ? null : _cfg.UpdateChannel) ? "rc" : _cfg.UpdateChannel));
            string latest = VersionForChannel(tc, _cfg == null ? "rc" : _cfg.UpdateChannel);
            Console.WriteLine("UPDATEINFO_LATEST " + (latest.Length == 0 ? "unknown" : latest));
            string reg2 = tc.NpmRegistryConfig();
            Console.WriteLine("UPDATEINFO_REGISTRY " + (string.IsNullOrEmpty(reg2) ? "default" : reg2.Trim()));
            string state = "unknown";
            if (installed != null && installed.Length > 0 && latest.Length > 0)
                state = installed == latest ? "up-to-date" : (Dsht.Domain.Services.VersionComparer.Compare(installed, latest) < 0 ? "update-available" : "newer-installed");   // ★ 审查抓到：字典序 ✗ → 改用正确比较器 ✓✓
            Console.WriteLine("UPDATEINFO_STATE " + state);
            // 回滚候选：最新的 -pre-update 备份，以及它旁挂文件里记录的当时版本 ✓
            try
            {
                string root = reg.Get<IBackupSource>().BackupsRoot;
                string best = null;
                if (!string.IsNullOrEmpty(root) && System.IO.Directory.Exists(root))
                {
                    string[] dirs = System.IO.Directory.GetDirectories(root, "*-pre-update");
                    System.Array.Sort(dirs, StringComparer.Ordinal);
                    if (dirs.Length > 0) best = dirs[dirs.Length - 1];
                }
                Console.WriteLine("UPDATEINFO_PRE_BACKUP " + (best == null ? "none" : best));
                string wasVersion = "unknown";
                if (best != null) { try { if (System.IO.File.Exists(best + ".version")) wasVersion = System.IO.File.ReadAllText(best + ".version").Trim(); } catch { } }
                Console.WriteLine("UPDATEINFO_PRE_BACKUP_VERSION " + wasVersion);
                Console.WriteLine("UPDATEINFO_ROLLBACK " + (best == null ? "none" : wasVersion));
            }
            catch (Exception ex) { Console.WriteLine("UPDATEINFO_PRE_BACKUP none"); Console.WriteLine("UPDATEINFO_ROLLBACK none"); Console.WriteLine("UPDATEINFO_NOTE " + ex.Message); }
            return 0;
        }
        private static int LogCmd(string[] args, ServiceRegistry reg)
        {
            string text = reg.Get<ILogSource>().ReadLog();
            if (text == null)
            {
                Console.WriteLine("LOG_EMPTY " + T("还没有日志（状态目录/logs/launcher.log 不存在）", "no log yet (state dir/logs/launcher.log does not exist)"));
                return 0;
            }
            string level = FlagOf(args, "--level").Trim().ToLowerInvariant();
            string grep = FlagOf(args, "--grep");
            int maxLines = 0;
            string linesArg = FlagOf(args, "--lines");
            if (linesArg.Length > 0) int.TryParse(linesArg, out maxLines);
            List<string> picked = new List<string>();
            string[] all = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < all.Length; i++)
            {
                string l = all[i];
                if (level.Length > 0 && l.IndexOf(level, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (grep.Length > 0 && l.IndexOf(grep, StringComparison.OrdinalIgnoreCase) < 0) continue;
                picked.Add(l);
            }
            while (picked.Count > 0 && picked[picked.Count - 1].Trim().Length == 0) picked.RemoveAt(picked.Count - 1);   // 去掉末尾空行（日志文件常以换行结尾）
            if (maxLines > 0 && picked.Count > maxLines) picked.RemoveRange(0, picked.Count - maxLines);
            string export = FlagOf(args, "--export");
            if (export.Length > 0)
            {
                if (!Has(args, "--yes"))
                {
                    Console.WriteLine("LOG_EXPORT_PLAN " + T("将把筛选结果写入 ", "will write the filtered result to ") + export + T("（会写盘）—— 确认请加 --yes", " (writes to disk) - add --yes to confirm"));
                    return 0;
                }
                try
                {
                    System.IO.File.WriteAllText(export, string.Join(Environment.NewLine, picked.ToArray()));
                    Console.WriteLine("LOG_EXPORT " + export + " " + picked.Count);
                }
                catch (Exception ex) { Console.WriteLine("LOG_FAIL " + ex.Message); }
                return 0;
            }
            Console.WriteLine("LOG_OK " + picked.Count);
            for (int i = 0; i < picked.Count; i++) Console.WriteLine("LOG_LINE " + picked[i]);
            return 0;
        }
        /// <summary>about（V3 独有）：版本、定位、许可与"非官方"声明。纯文本，不联网。</summary>
        /// <summary>shortcut（V3 独有）：创建桌面/应用菜单入口。
        /// Windows 用 PowerShell 的 WScript.Shell 建 .lnk（与 v2.x 同思路）；Linux 写 XDG 的 .desktop 文件。
        /// 写操作 → 计划 → `--yes` 闸门；失败一律如实报原因（不静默）。</summary>
        private static int ShortcutCmd(string[] args)
        {
            bool win = PlatformIsWindows();
            // 单文件发布下 Assembly.Location 是空的 ✗（真机测试抓到的）→ 用 MainModule，两条构建路径都可用 ✓
            string exe = "";
            try { exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; } catch { }
            if (string.IsNullOrEmpty(exe)) exe = System.IO.Path.Combine(AppContext.BaseDirectory, "dsh-minato");
            if (string.IsNullOrEmpty(exe)) { Console.WriteLine("SHORTCUT_FAIL " + T("拿不到自身路径", "cannot resolve own path")); return 0; }
            string dir = System.IO.Path.GetDirectoryName(exe);
            string target = win ? System.IO.Path.Combine(dir, "dsht-minato.exe") : System.IO.Path.Combine(dir, "dsht-minato");
            if (!System.IO.File.Exists(target)) target = exe;   // 还没改名时就用当前可执行文件

            string where;
            if (win)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                where = System.IO.Path.Combine(desktop, "dsh-minato.lnk");
            }
            else
            {
                string home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrEmpty(home)) { Console.WriteLine("SHORTCUT_FAIL " + T("没有 HOME，无法确定位置", "no HOME, cannot decide a location")); return 0; }
                where = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(home, ".local"), "share"), "applications");
                where = System.IO.Path.Combine(where, "dsh-minato.desktop");
            }
            Console.WriteLine("SHORTCUT_PLAN " + (win ? T("将创建快捷方式：", "will create a shortcut: ") : T("将创建应用入口：", "will create a desktop entry: ")) + where + T(" → ", " -> ") + target);
            if (!Has(args, "--yes"))
            {
                Console.WriteLine("SHORTCUT_DRYRUN " + T("（确认请加 --yes）", "(add --yes to confirm)"));
                return 0;
            }
            try
            {
                if (win)
                {
                    string ps = "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + where.Replace("'", "''") + "');" +
                                "$s.TargetPath='" + target.Replace("'", "''") + "';" +
                                "$s.WorkingDirectory='" + dir.Replace("'", "''") + "';$s.Save()";
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("powershell", "-NoProfile -Command \"" + ps.Replace("\"", "\\\"") + "\"");
                    psi.UseShellExecute = false; psi.CreateNoWindow = true;
                    using (System.Diagnostics.Process pr = System.Diagnostics.Process.Start(psi)) { pr.WaitForExit(30000); if (pr.ExitCode != 0) { Console.WriteLine("SHORTCUT_FAIL powershell 退出码 " + pr.ExitCode); return 0; } }
                }
                else
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(where));
                    string body = "[Desktop Entry]\nType=Application\nName=dsh-minato\nComment=" + T("dsh 部署运维套件", "deploy & ops kit for dsh") + "\nExec=" + target + "\nTerminal=true\nCategories=Utility;\n";
                    System.IO.File.WriteAllText(where, body, new System.Text.UTF8Encoding(false));
                }
                Console.WriteLine(System.IO.File.Exists(where) ? "SHORTCUT_OK " + where : "SHORTCUT_FAIL " + T("写入后未观测到文件", "file not observed after writing"));
            }
            catch (Exception ex) { Console.WriteLine("SHORTCUT_FAIL " + ex.Message); }
            return 0;
        }
        /// <summary>开机自启（用户要求："开启自启服务功能还在吗" + "按平台选 + 做成设置项让你选" ✓✓）
        /// · 目标：`auto_start_target` = auto（**按平台** ✓ Win/Mac→官方桌面端 · Linux→dsh web）/ desktop / web
        /// · 实现：**不需要管理员/root** ✓
        ///     Windows → 启动目录放一个 .cmd ✓（%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup）
        ///     Linux   → systemd **user** unit ✓（~/.config/systemd/user/ + systemctl --user enable ✓）
        /// · 全部**可回退** ✓（--disable 删掉 ✓；也会打印文件路径让你自己看 ✓）
        /// 标记：`AUTOSTART_STATUS <on|off> target=<desktop|web|auto>` / `AUTOSTART_OK <动作>` / `AUTOSTART_FAIL <原因>`</summary>
        private static int AutoStartCmd(string[] args, ServiceRegistry reg)
        {
            bool win = PlatformIsWindows();
            string cfgTarget = _cfg == null || string.IsNullOrEmpty(_cfg.AutoStartTarget) ? "auto" : _cfg.AutoStartTarget;
            string effective = cfgTarget;
            if (effective == "auto") effective = win ? "desktop" : "web";
            string label = effective == "desktop" ? T("官方桌面端", "the official desktop app") : T("dsh web", "dsh web");

            string path = win
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "Startup", "dsh-minato-autostart.cmd")
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "systemd", "user", "dsh-minato-autostart.service");

            bool enabled = FileExists(path);
            Console.WriteLine("AUTOSTART_STATUS " + (enabled ? "on" : "off") + " target=" + effective + " configured=" + cfgTarget);

            if (!Has(args, "--enable") && !Has(args, "--disable"))
            {
                Console.WriteLine("AUTOSTART_PATH " + path);
                Console.WriteLine("AUTOSTART_WOULD " + T("将启动：", "would start: ") + label + (effective == "desktop" && !win ? T("（注意：Linux 上官方桌面端暂未发行 ✓ 请把 auto_start_target 改成 web ✓）", " (note: no Linux desktop app yet; set auto_start_target=web)") : ""));
                return 0;
            }

            if (Has(args, "--disable"))
            {
                try
                {
                    if (enabled) System.IO.File.Delete(path);
                    if (!win) RunQuiet("systemctl", "--user disable dsh-minato-autostart");
                    Console.WriteLine("AUTOSTART_OK disable");
                    OpLog(reg, "INFO", "autostart disabled");
                }
                catch (Exception ex) { Console.WriteLine("AUTOSTART_FAIL " + ex.Message); }
                return 0;
            }

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                if (win)
                {
                    string body;
                    if (effective == "desktop")
                    {
                        string exe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DeepSeek Harness", "DeepSeek Harness.exe");
                        body = "@echo off\r\nrem dsh-minato 开机自启（设置里可关：dsh-minato autostart --disable）\r\nstart \"\" \"" + exe + "\"\r\n";
                    }
                    else
                    {
                        // I7 FIX (CLI audit MAJOR): Assembly.Location is an empty STRING (not null) for a
            // single-file build, which is how the tool ships, so the null check passed and the
            // autostart entry was written with an empty command path while still reporting success.
            // The running executable path is the reliable source.
            string cli = SelfExePath();
                        body = "@echo off\r\nrem dsh-minato 开机自启（设置里可关：dsh-minato autostart --disable）\r\n\"" + cli + "\" start --yes\r\n";
                    }
                    System.IO.File.WriteAllText(path, body, new System.Text.UTF8Encoding(false));
                }
                else
                {
                    string cli2 = SelfExePath();   // I7 FIX: see above
                    string exec = effective == "web" ? "\"" + cli2 + "\" start --yes" : "echo 'desktop app not available on Linux'";
                    string unit = "[Unit]\nDescription=dsh-minato autostart (dsh)\nAfter=network.target\n\n[Service]\nType=oneshot\nExecStart=" + exec + "\n\n[Install]\nWantedBy=default.target\n";
                    System.IO.File.WriteAllText(path, unit, new System.Text.UTF8Encoding(false));
                    RunQuiet("systemctl", "--user daemon-reload");
                    RunQuiet("systemctl", "--user enable dsh-minato-autostart");
                }
                // I6 FIX (CLI audit MINOR): success was printed without checking that anything was
            // written, unlike the shortcut path which verifies the file exists.
            bool wrote = false;
            try
            {
                // ★★★ **F-A 修复（CLI 复审 HIGH —— 检查的路径不是写入的那个）** ✓✓
                //   ✗ 我修 N1 时改了**检查**里的文件名 ✗ 但**真正写入的是 `path`** ✗✗
                //     → Windows 上写的是 `…\Startup\dsh-minato-autostart.cmd` ✓
                //       而检查找 `…\Startup\dsh-minato.cmd` ✗ → **永远 FAIL** ✗✗
                //     → 用户看到"写入后没有找到自启文件" ✓ 而**文件其实写好了** ✗（假报失败 ✓）
                //   ✓ 现在：**直接检查 `path`** ✓✓（那是真正写下去的那个 ✓ 平台无关 ✓）
                wrote = System.IO.File.Exists(path);
            }
            catch { }
            if (!wrote) { Console.WriteLine("AUTOSTART_FAIL " + T("写入后**没有找到自启文件** ✓ 请检查权限 ✓", "no autostart file was found after writing")); return 0; }
            Console.WriteLine("AUTOSTART_OK enable");
                Console.WriteLine("AUTOSTART_PATH " + path);
                OpLog(reg, "INFO", "autostart enabled target=" + effective);
            }
            catch (Exception ex) { Console.WriteLine("AUTOSTART_FAIL " + ex.Message); }
            return 0;
        }
        /// <summary>config-get：CONFIGGET_OK + 每行 CONFIG <key> <value>（顺序与 v2.x 一致）。</summary>
        private static int ConfigGet()
        {
            Console.WriteLine("CONFIGGET_OK");
            Console.WriteLine("CONFIG lang " + _cfg.Lang);
            Console.WriteLine("CONFIG host " + _cfg.Host);
            Console.WriteLine("CONFIG ws " + (_cfg.Workspace == null ? "" : _cfg.Workspace));
            Console.WriteLine("CONFIG keep_backups " + _cfg.KeepBackups);
            Console.WriteLine("CONFIG check_update " + (_cfg.CheckUpdate ? "on" : "off"));
            Console.WriteLine("CONFIG check_dsh_update " + (_cfg.CheckDshUpdate ? "on" : "off"));
            Console.WriteLine("CONFIG update_channel " + _cfg.UpdateChannel);
            Console.WriteLine("CONFIG close_action " + _cfg.CloseAction);
            Console.WriteLine("CONFIG auto_start " + (_cfg.AutoStart ? "on" : "off"));
            Console.WriteLine("CONFIG auto_start_target " + _cfg.AutoStartTarget);   // 审查修复：漏了这行 → GUI 设置页永远渲染不出这一项 ✗
            Console.WriteLine("CONFIG dsh_versions " + _cfg.DshVersions);
            // —— 排障开关 ✓（用户要求：""给可能会发生可能不会发生的问题提供解决的选项"" + ""备注一下发生什么问题可以尝试启用和禁用"" ✓✓）——
            Console.WriteLine("CONFIG browser_mode " + _cfg.BrowserMode);
            Console.WriteLine("CONFIG ui_parallel " + (_cfg.UiParallel ? "on" : "off"));
            Console.WriteLine("CONFIG scan_children " + (_cfg.ScanChildren ? "on" : "off"));
            Console.WriteLine("CONFIG gui_start_page " + _cfg.GuiStartPage);   // ★ 界面偏好（2026-10-02 用户要求"启动默认打开页面可自选" ✓）
            Console.WriteLine("CONFIG gui_auto_refresh " + _cfg.GuiAutoRefresh);   // ★ 概览自动刷新（2026-10-02 ✓：off/0.5/1/3/5/自定义秒 ✓）
            Console.WriteLine("CONFIG gui_shell " + _cfg.GuiShell);   // ★ 布局偏好（2026-10-06：顶栏切换器的选择被记住 ✓ 仅 GUI 用 ✓）
            Console.WriteLine("CONFIG gui_style " + _cfg.GuiStyle);   // ★ 风格偏好（同上 ✓ A/B/C/D → 0..3 ✓）
            Console.WriteLine("CONFIG balance_key " + ((_cfg.BalanceKey == null ? "" : _cfg.BalanceKey).Trim().Length > 0 ? "set" : ""));   // ★ 审查 M1 修复：不回显明文（终端回滚缓冲留底 ✗）只报 set/unset ✓ GUI 按"留空=保持"工作 ✓
            // —— 看板（2026-10-08 · 第一批 ✓ 第三批口径全解锁 ✓）：口径默认 + 单价键（费用显示前置=D2b 多帧解码 ✓ 决策 D5 ✓ 现在只有存储与校验 ✓）——
            Console.WriteLine("CONFIG sessions_default_level " + _cfg.SessionsDefaultLevel);   // ★ 第三批起三档全生效 ✓（parents/parents_sub 需要桥插件血缘段；缺血缘 ⇒ sessions 如实拒绝 ✓）
            Console.WriteLine("CONFIG sessions_price_in_per_mtok " + _cfg.SessionsPriceInPerMTok.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine("CONFIG sessions_price_cache_read_per_mtok " + _cfg.SessionsPriceCacheReadPerMTok.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine("CONFIG sessions_price_cache_write_per_mtok " + _cfg.SessionsPriceCacheWritePerMTok.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine("CONFIG sessions_price_out_per_mtok " + _cfg.SessionsPriceOutPerMTok.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // 备注行 ✓✓：GUI 原样显示在对应设置项下面 ✓（"出现什么问题时试哪个" ✓）
            Console.WriteLine("CONFIGNOTE browser_mode " + T("【浏览器打不开时改这个】auto=自动（先 snap run firefox → 再直开 → 最后 xdg-open）/ snap=只走 snap（Ubuntu 的 snap 版 firefox 必须这样 ✓）/ direct=只直开 firefox / xdg=只交给系统默认", "when the browser will not open"));
            Console.WriteLine("CONFIGNOTE ui_parallel " + T("【切页卡顿时改这个】on=概览页一次聚合调用 overview（快 ✓ 默认）/ off=四个命令分开跑（老行为，排障用）", "when switching pages feels slow"));
            Console.WriteLine("CONFIGNOTE scan_children " + T("【会话页想更快时关掉】on=扫描会话文件得出主/子代理归类（默认 ✓ 197 个会话约 200ms）/ off=不扫（会话页更快，但子代理统计会为空）", "to make the sessions page faster"));
            Console.WriteLine("CONFIGNOTE auto_start " + T("【不想让 GUI 自动起 dsh 时关掉】on=GUI 启动时自动启动（默认）/ off=不自动", "if you do not want the GUI to auto-start dsh"));
            Console.WriteLine("CONFIGNOTE auto_start_target " + T("【开机自启启动什么】auto=按平台（Windows/macOS 启动官方桌面端，Linux 启动 dsh web）/ desktop=官方桌面端 / web=dsh web", "what to start at login"));
            Console.WriteLine("CONFIGNOTE gui_start_page " + T("【GUI 启动先开哪页】0=概览 1=看板 2=会话与Token 3=形态与插件 4=备份 5=体检 6=设置 7=说明 8=更新 9=日志（默认 1）", "which page the GUI opens first"));
            Console.WriteLine("CONFIGNOTE gui_auto_refresh " + T("【概览页自动刷新间隔】off=暂停（默认）/ 0.5=实时 / 1=快 / 3=中 / 5=慢 / 或自定义秒数（0.5–3600）——只在概览/看板页生效", "overview auto-refresh interval"));
            Console.WriteLine("CONFIGNOTE gui_shell " + T("【侧栏布局】0=侧栏式 1=顶部标签 2=卡片网格 3=主从式 4=混合式（默认 4）——顶栏切换器会记住你的选择", "sidebar layout 0-4; the top-bar switcher remembers your choice"));
            Console.WriteLine("CONFIGNOTE gui_style " + T("【视觉风格】0=A 浅色卡片 1=B 深色卡片 2=C 深色紧凑 3=D 浅色仪表盘（默认 0）——顶栏 A-D 会记住你的选择", "visual style A-D; the top-bar switcher remembers your choice"));
            Console.WriteLine("CONFIGNOTE balance_key " + T("【DeepSeek 平台 API key，用于余额检测】留空=未绑定（概览页不显示余额卡）。⚠ key 以明文保存在本机配置文件里，只在你自己的机器上，不上传", "DeepSeek platform API key for the balance card; empty = unbound; stored in plain text in the local config only"));
            Console.WriteLine("CONFIGNOTE sessions_default_level " + T("【看板口径默认】global=全局 / parents=仅父会话 / parents_sub=父会话+子代理（第三批起三档全生效 ✓）——后两档需要桥插件血缘段（dsh-minato-bridge 只解原始日志首帧 header，正文帧一帧不解）；缺血缘时看板会如实提示降级", "default sessions scope; all three tiers live since batch 3 (parents/parents_sub need the bridge lineage section)"));
            Console.WriteLine("CONFIGNOTE sessions_price_in_per_mtok " + T("【输入单价】美元/百万 token；0=未填 → 不显示费用（费用显示前置=D2b 多帧解码 ✓ 当前只存不算）", "input price per million tokens; 0 = unset"));
            Console.WriteLine("CONFIGNOTE sessions_price_cache_read_per_mtok " + T("【缓存读单价】美元/百万 token；0=未填", "cache-read price per million tokens; 0 = unset"));
            Console.WriteLine("CONFIGNOTE sessions_price_cache_write_per_mtok " + T("【缓存写单价】美元/百万 token；0=未填", "cache-write price per million tokens; 0 = unset"));
            Console.WriteLine("CONFIGNOTE sessions_price_out_per_mtok " + T("【输出单价】美元/百万 token；0=未填", "output price per million tokens; 0 = unset"));
            Console.WriteLine("CONFIGNOTE close_action " + T("【关闭窗口时】exit=直接退出（默认）/ ask=先弹确认 / tray=最小化到系统托盘（托盘图标右键可显示或退出）", "close behavior: exit / ask / tray (system tray)"));
            Console.WriteLine("CONFIGNOTE host " + T("【dsh 监听地址】127.0.0.1 或 localhost（探测与显示用的回环名，功能等价）", "loopback name used for probing/display: 127.0.0.1 or localhost"));
            return 0;
        }
        /// <summary>config-set <key> <value>：白名单内才写盘，否则 CONFIGSET_FAIL 原因。</summary>
        private static int ConfigSet(string[] args, ServiceRegistry reg)
        {
            string key = args.Length > 1 ? args[1] : "";
            string val = args.Length > 2 ? args[2] : "";
            string reason = ConfigValidator.Validate(key, val, CanonPath);
            if (reason != null) { Console.WriteLine("CONFIGSET_FAIL " + reason); return 0; }
            _cfg = ConfigValidator.ApplyTo(_cfg, key, val, CanonPath);
            reg.Get<IConfigSource>().WriteConfig(ConfigCodec.Serialize(_cfg));
            // 回读校验 ✓：写盘可能静默失败（配置文件只读/被占用 ✗），实测曾报 CONFIGSET_OK 而文件根本没变 ✗✗。
            // 判据是"配置里这个键真的等于请求值" ✓，不是"我调用过写盘" ✗。
            string wantText = ConfigCodec.Serialize(_cfg);
            string gotText = ConfigCodec.Serialize(ConfigCodec.Parse(reg.Get<IConfigSource>().ReadConfig(), CanonPath));
            if (gotText != wantText)
            {
                Console.WriteLine("CONFIGSET_FAIL " + T("写入未生效（配置文件可能只读或被占用）—— 已回读核对，值未改变", "the write did not take effect (the config file may be read-only or locked) - read back and the value is unchanged"));
                _cfg = ConfigCodec.Parse(gotText, CanonPath);
                return 0;
            }
            _cfg = ConfigCodec.Parse(gotText, CanonPath);
            Console.WriteLine("CONFIGSET_OK " + key.Trim().ToLowerInvariant());
            return 0;
        }
    }
}
