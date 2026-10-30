using System;
using System.Collections.Generic;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;
using Dsht.Domain.Services;

namespace Dsht.Cli
{
    /// <summary>基础设施与辅助：交互菜单、参数解析、本地化、日志、配置、平台判定、
    /// 可执行文件解析、完整性闸门 ✓。
    /// 架构审计（S1）：从 Program.cs 原样搬出来，逻辑一行没改 ✓
    /// 留在 Program.cs 的是组合根（Main + Compose + 命令分发）✓</summary>
    public static partial class Program
    {
        /// <summary>在 PATH 里找一个可执行文件 ✓（找不到返回空串 ✓ 不猜 ✓）。</summary>
        private static string WhichOnPath(string name)
        {
            try
            {
                string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                string[] dirs = pathVar.Split(System.IO.Path.PathSeparator);
                string[] exts = new string[] { ".exe", ".cmd", ".bat", "" };
                for (int i = 0; i < dirs.Length; i++)
                {
                    string d = dirs[i];
                    if (string.IsNullOrEmpty(d)) continue;
                    for (int j = 0; j < exts.Length; j++)
                    {
                        try
                        {
                            string f = System.IO.Path.Combine(d.Trim(), name + exts[j]);
                            if (System.IO.File.Exists(f)) return f;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return "";
        }
        /// <summary>按 --port 选择 start 的目标：默认端口用组合目标；指定端口时临时构造 Web 目标。
        /// **单独成方法**是为了让"先解析参数、再选目标"的顺序不会被后续改动打乱 ——
        /// 之前就是因为顺序反了，`start --port 3999` 去探了默认 3080（用户的实例）。
        /// 注意：DSH_HOME 隔离数据根、**不隔离端口**，所以测试必须能指定端口。</summary>
        /// <summary>解析 dsh 可执行文件路径：先问工具链，再查免 sudo 引导后的常见目录（非交互 PATH 里通常没有 ✗），最后交给 PATH。</summary>
        private static string ResolveDsh(ServiceRegistry reg)
        {
            string w = reg.Get<IToolchainQuery>().WhichDsh();
            if (!string.IsNullOrEmpty(w)) return w;
            string home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                string[] cands = new string[]
                {
                    System.IO.Path.Combine(home, ".local/node/bin/dsh"),
                    System.IO.Path.Combine(home, ".npm-global/bin/dsh"),
                    System.IO.Path.Combine(home, ".local/bin/dsh")
                };
                for (int i = 0; i < cands.Length; i++) { if (System.IO.File.Exists(cands[i])) return cands[i]; }
            }
            return "dsh";
        }
        private static int Menu(CommandContext ctx)
        {
            ServiceRegistry reg = ctx.Reg;
            string[] args = ctx.Args;
            ToolkitConfig cfg = ctx.Cfg;
            while (true)
            {
                Console.WriteLine();
                // 与经典版（v2.x）同风格：青色框 + 白色条目 + ▍小标题 + 彩色提示 ✓
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  ==============================================");
                Console.WriteLine("  dsh-minato " + ToolkitVersion);
                Console.WriteLine("  ==============================================");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(T("  ▍ 常用操作", "  ▍ common"));
                Console.ResetColor();
                Console.WriteLine("    1  安装/升级 dsh        2  启动 dsh           3  停止 dsh");
                Console.WriteLine("    4  状态                 5  会话与 token       6  形态与插件");
                Console.WriteLine("    7  立即备份             8  备份清单           9  恢复预览（dry-run）");
                Console.WriteLine("   10  体检                11  备份目录          12  全部命令");
                Console.WriteLine("    q  退出");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(T("  请输入数字：", "  choice: "));
                Console.ResetColor();
                string line = Console.ReadLine();
                if (line == null) return 0;
                line = line.Trim().ToLowerInvariant();
                if (line == "" || line == "q" || line == "quit" || line == "0") return 0;
                // W3：菜单与命令行共用同一批 handler；只换 args（reg/cfg 仍来自同一个 ctx ✓）
                if (line == "1") { if (Confirm("install/update dsh")) { InstallLike(ctx.WithArgs(new string[] { "--yes" }), false); InstallLike(ctx.WithArgs(new string[] { "--yes" }), true); } continue; }
                if (line == "2") { if (Confirm("start dsh")) StartCmd(ctx.WithArgs(new string[] { "--yes" })); continue; }
                if (line == "3") { if (Confirm("stop dsh")) StopCmd(ctx.WithArgs(new string[] { "--yes" })); continue; }
                if (line == "4") { StatusImpl(ctx, true); Describe(ctx); continue; }
                if (line == "5") { Sessions(ctx.WithArgs(new string[0])); continue; }
                if (line == "6") { Profiles(ctx); continue; }
                if (line == "7") { if (Confirm("backup")) Backup(ctx.WithArgs(new string[0])); continue; }
                if (line == "8") { BackupList(ctx.WithArgs(new string[] { "--detail" })); continue; }
                if (line == "9") { Restore(ctx.WithArgs(new string[] { "--dry-run" })); continue; }
                if (line == "10") { Doctor(ctx.WithArgs(new string[0])); continue; }
                if (line == "11") { Console.WriteLine(T("备份目录：", "backup folder: ") + reg.Get<IBackupSource>().BackupsRoot); continue; }
                if (line == "12") { Usage(); continue; }
                Console.WriteLine(T("没有这个选项。", "no such option."));
            }
        }
        /// <summary>**启动闸门** ✓✓：自身 SHA-256 vs 随包 hashes.txt。
        /// 返回 0 = 放行 ✓；非 0 = 拒绝运行（并已打印原因 ✓）。
        /// **绝不因为"读不到清单"就拒绝** ✗ —— 那会把源码编译和单独复制 exe 全挡掉 ✓（Unknown 放行 ✓）。
        /// **也绝不因为"读不到清单"就说"校验通过"** ✗ —— 明确说"跳过" ✓✓（与 doctor 的措辞一致 ✓）。</summary>
        private static int StartupIntegrityGate(ServiceRegistry reg)
        {
            try
            {
                // 用**真实的领域 API** ✓（ManifestParser.ParseHash + IIntegritySource 三成员 ✓）
                IIntegritySource integ = reg.Get<IIntegritySource>();
                string expected = Dsht.Domain.Services.ManifestParser.ParseHash(integ.ReadManifest(), integ.SelfFileName());
                // ★★★ **第 2 轮审查抓到（性能最大头 ✗✗）**：这里**先无条件算了整个 66 MB exe 的 SHA-256** ✗
                //   而 expected == null 时（源码编译 / 单独复制 exe / 没有清单 ✓）结果**必然是 Unknown** ✓ → 算了再丢 ✗
                //   → GUI 一次按钮要起 5–11 个 CLI 进程 ✓ → **每次白算 66 MB** ✗✗（正是"按钮还是有延迟"的根因 ✓）
                // ✓ 现在：**没有清单条目就根本不算** ✓✓（Unknown 放行语义完全不变 ✓）
                IntegrityVerdict v = (expected == null)
                    ? IntegrityVerdict.Unknown
                    : IntegrityJudge.Judge(expected, integ.SelfHash());
                if (v == IntegrityVerdict.Match) return 0;   // 一致 → 静默放行 ✓（不刷屏 ✓）
                if (v == IntegrityVerdict.Unknown)
                {
                    // 明说"跳过" ✓ —— **不假报"已验证"** ✓✓（与 doctor 的措辞一致 ✓）
                    // ★★★ **假绿修复（实测发现 —— 这一行把整条门槛链拖死过）** ✓✓
                    //   · 它是**诊断提示**（"源码编译属正常" ✓）→ 走 **stderr** 是对的 ✓（不是命令输出 ✓）
                    //   · 但门槛脚本用 `& powershell … 2>&1` + `$ErrorActionPreference = "Stop"` ✓
                    //     → PS 5.1 把子进程 stderr 变成 **NativeCommandError** → **Stop 终止** ✗✗
                    //     → **脚本在第一条子门禁就死掉、从不打印结果表** ✗ → **看输出像全绿** ✗✗
                    //   ✓ 已在**门槛脚本**里修（只在调外部命令时放宽 ✓）✓
                    //   ✓ 并在 `compare_markers` 里**全局忽略这一行** ✓✓（两边 exe 身份不同 → 天然不对称 ✓）
                    Console.Error.WriteLine("INTEGRITY_SKIPPED " + T("旁无 hashes.txt（或清单里没有本文件）→ 跳过自身校验。源码编译、单独复制 exe 属正常；官方发布包会带清单。", "no hashes.txt beside this exe (or it does not list this file) - self-check skipped; official releases ship a manifest."));
                    return 0;
                }
                // Mismatch → **拒绝运行** ✓✓（银狐静态感染后文件必然变 ✓）
                // —— 被改动时的提示 ✓✓（用户要求："被感染检测后，提示，文件被感染之类的提示" ✓）
                // 措辞纪律：说**事实**（"已经被改动" ✓ 指纹对不上是确定的 ✓）
                //           说**可能**（"可能被木马感染" ✓ 也可能是别的原因 ✗ 不把可能说成一定 ✗）
                //           给**能照着做的步骤** ✓✓（比一句"校验失败"有用得多 ✓）
                Console.WriteLine("");
                Console.WriteLine("  ╔══════════════════════════════════════════════════════════════╗");
                Console.WriteLine("  ║   警告：这个文件**已经被改动**，可能被木马感染              ║");
                Console.WriteLine("  ╚══════════════════════════════════════════════════════════════╝");
                Console.WriteLine("");
                Console.WriteLine(T("  本工具的每个文件都有官方指纹（SHA-256）。现在这个文件的指纹和官方清单**对不上** ——", "  Every file in this tool has an official SHA-256 fingerprint, and this one does not match."));
                Console.WriteLine(T("  说明它**被改过**。银狐一类木马正是这样干的：给正常程序打补丁，让它在你运行时同时干别的事。", "  SilverFox-class trojans work exactly this way: they patch a legitimate program."));
                Console.WriteLine("");
                Console.WriteLine(T("  **已拒绝运行**（刻意的：宁可你打不开，也不让你在不知情的情况下运行被改过的程序）", "  **Refused to run** - deliberately: better that it will not open than that it runs modified."));
                Console.WriteLine("");
                Console.WriteLine(T("  请这样做：", "  What to do:"));
                Console.WriteLine(T("    1. **不要**继续使用这个文件", "    1. Do not keep using this file"));
                Console.WriteLine(T("    2. 把它**删掉**（或先移到隔离目录）", "    2. Delete it (or move it somewhere isolated first)"));
                Console.WriteLine(T("    3. 从**官方 Releases 重新下载**：", "    3. Download again from the official releases:"));
                Console.WriteLine("       https://github.com/sakanamaru/dsh-minato/releases");
                Console.WriteLine(T("    4. 建议用杀毒软件**全盘扫描**一次（木马通常不止感染一个文件）", "    4. Run a full antivirus scan - trojans rarely infect only one file"));
                Console.WriteLine("");
                Console.WriteLine("  ---- " + T("技术细节（给排查用）", "technical details for diagnosis") + " ----");
                Console.WriteLine("  INTEGRITY_FAIL " + T("自身指纹与官方清单不一致", "self fingerprint does not match the official manifest"));
                Console.WriteLine("  INTEGRITY_EXPECTED " + (expected == null ? "unknown" : expected));
                Console.WriteLine("  INTEGRITY_ACTUAL " + (string.IsNullOrEmpty(integ.SelfHash()) ? "unknown" : integ.SelfHash()));
                Console.WriteLine("  INTEGRITY_HINT " + T("从官方 Releases 重新下载；本工具拒绝在被改动的情况下运行。", "download again from the official releases; this tool refuses to run when modified."));
                return 3;
            }
            catch
            {
                return 0;   // 自检本身出错 → 放行 ✓（不能让自检把工具变成砖 ✓）
            }
        }
        /// <summary>log（V3 独有）：查看/筛选/导出操作日志 —— 经典版「日志中心」的 CLI 对应物 ✓。
        /// 用法：log [--lines &lt;n&gt;] [--level info|warn|error] [--grep &lt;text&gt;] [--export &lt;file&gt; [--yes]]
        /// 标记行：LOG_OK &lt;n&gt; / LOG_LINE &lt;原文&gt; / LOG_EMPTY / LOG_EXPORT &lt;路径&gt; &lt;n&gt; / LOG_FAIL &lt;原因&gt;。</summary>
        /// <summary>记一条操作日志（尽力而为 ✓）。读取侧见 LogCmd ✓。</summary>
        private static void OpLog(ServiceRegistry reg, string level, string message)
        {
            try { reg.Get<ILogSource>().Append(level, message); } catch { }
        }
        /// <summary>Node 版本是否低于要求（真机抓到：dsh 要求 >= 22.19.0，太旧时它会**静默退出** ✗，极难诊断）。
        /// 纯函数，便于单测；无法解析时保守返回 false（不误伤）。</summary>
        private static bool NodeTooOld(string version, int needMajor, int needMinor)
        {
            if (string.IsNullOrEmpty(version)) return false;
            string v = version.Trim().TrimStart('v', 'V');
            int dot = v.IndexOf('.');
            if (dot <= 0) return false;
            int major;
            if (!int.TryParse(v.Substring(0, dot), out major)) return false;
            string rest = v.Substring(dot + 1);
            int dot2 = rest.IndexOf('.');
            int minor;
            if (!int.TryParse(dot2 > 0 ? rest.Substring(0, dot2) : rest, out minor)) return false;
            if (major != needMajor) return major < needMajor;
            return minor < needMinor;
        }
        /// <summary>是否 Windows（平台判断只用于选择启动方式，不用于猜形态）。</summary>
        private static bool PlatformIsWindows()
        {
            return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
        }
        /// <summary>取 `--name value` 形式的值；缺省返回空串。</summary>
        private static string FlagOf(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++) { if (args[i] == name) return args[i + 1]; }
            return "";
        }
        private static bool Has(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++) if (args[i] == name) return true;
            return false;
        }
        /// <summary>最小本地化：dryrun/restore 的失败与说明文案在 v2.x 里走 T()，必须同语言才能比对。</summary>
        private static string T(string zh, string en) { return IsEn() ? en : zh; }
        /// <summary>当前是否英文（与 v2.x 的 T() 同一判据）。</summary>
        private static bool IsEn() { return _isEn; }   // W3：由 CommandContext 的配置派生（Main/ConfigSet 更新），不再是 static ToolkitConfig
        /// <summary>高风险操作闸门（对齐 v2.x 的 IntegrityGate）：自身与随包 hashes.txt 不匹配即拒绝；旁无 manifest 时放行。</summary>
        private static bool IntegrityGate(ServiceRegistry reg)
        {
            IIntegritySource integ = reg.Get<IIntegritySource>();
            string expected = ManifestParser.ParseHash(integ.ReadManifest(), integ.SelfFileName());
            if (!IntegrityJudge.ShouldBlock(IntegrityJudge.Judge(expected, integ.SelfHash()))) return true;
            Console.WriteLine("RESTORE_FAIL " + T("自身完整性校验失败：当前程序与随包 hashes.txt 不匹配（可能被篡改）。已拒绝执行恢复，请从官方 Releases 重新下载。",
                "self-integrity FAILED: this executable does not match the shipped hashes.txt (possible tampering); restore refused. Re-download from the official Releases."));
            return false;
        }
        private static ToolkitConfig LoadConfig(ServiceRegistry reg)
        {
            try { return ConfigCodec.Parse(reg.Get<IConfigSource>().ReadConfig(), CanonPath); }
            catch { return new ToolkitConfig(); }
        }
    }
}
