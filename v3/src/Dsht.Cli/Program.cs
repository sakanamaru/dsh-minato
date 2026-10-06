using System;
using System.Collections.Generic;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;
using Dsht.Domain.Services;
using Dsht.Domain.Targets;
using Dsht.Platform.Windows;

using System.Reflection;

[assembly: AssemblyTitle("dsh-minato V3")]
[assembly: AssemblyDescription("DeepSeek Harness(dsh) 安装/启动/卸载/备份恢复工具箱。v1: SOGR-Momono Dango(QwenPaw/DeepseekAPI-V4-Flash-0731)；v2: DeepSeek DSH(DSH/DeepseekAPI-V4-Flash-0731)；GitHub @sakanamaru")]
[assembly: AssemblyCompany("SOGR-Momono Dango / DeepSeek DSH / @sakanamaru")]
[assembly: AssemblyProduct("dsh-minato")]
[assembly: AssemblyVersion("3.0.5.0")]
[assembly: AssemblyFileVersion("3.0.5.0")]
namespace Dsht.Cli
{
    /// <summary>V3 CLI 组合根 + 命令面。每个命令的标记行都要与 v2.x 逐字一致（见 v3/tests/compare_markers.ps1）。</summary>
    public static partial class Program
    {
        private const int WebPort = 3080;

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); } catch { }
            // host 接线（2026-10-06）：配置键 host 现在**真的生效** —— Compose 之前预读一次配置，
            // 让 WebUrl 用配置的 loopback 名（默认 127.0.0.1 行为不变 ✓）。
            try
            {
                string preText = PlatformComposition.IsWindows()
                    ? new Dsht.Platform.Windows.WindowsConfigSource(new Dsht.Platform.Windows.WindowsPaths()).ReadConfig()
                    : new Dsht.Platform.Linux.LinuxConfigSource(new Dsht.Platform.Linux.LinuxPaths()).ReadConfig();
                PlatformComposition.ApplyWebHost(ConfigCodec.Parse(preText, null).Host);
            }
            catch { }
            ServiceRegistry reg = Compose();
            _cfg = LoadConfig(reg);
            string cmd = args.Length > 0 ? args[0] : "";

            // —— **反银狐启动闸门** ✓✓（用户要求："反银狐木马感染/伪造的机制，至少被感染无法运行" ✓）
            // 银狐（SilverFox）的主要手法是**静态感染**：给正常 exe 打补丁/追加代码 → **文件变了** ✓
            // → 自身 SHA-256 与随包 hashes.txt 不一致就**直接拒绝运行** ✓✓
            // 纪律（沿用 IntegrityJudge 的语义 ✓）：
            //   · **Mismatch → 拒绝**（被改过 ✓ 一律不跑 ✓ 不给绕过参数 ✗ —— 绕过参数本身就是洞 ✗✓）
            //   · **Unknown → 放行**（无清单/源码编译/单独复制 exe ✓ 否则开发者寸步难行 ✓✓）
            //     但会**明说"跳过校验"** ✓ **不假报"已验证"** ✗✓
            //   · 例外：`verify-install` 与 `selftest` **放行** ✓✓（被改过的包也要能自证/诊断 ✓）
            if (cmd != "verify-install" && cmd != "selftest")
            {
                int gate = StartupIntegrityGate(reg);
                if (gate != 0) return gate;
            }

            if (cmd == "") return Menu(reg);
            if (cmd == "status") return Status(reg, Has(args, "--detail"));
            if (cmd == "overview") return Overview(reg);   // 聚合只读：status --detail + sessions + backup-list + profiles（GUI 一次调用 ✓）
            if (cmd == "describe") return Describe(reg);
            if (cmd == "bridge-install") return BridgeInstall(args, reg);   // ✓ 可选的桥接插件 ✓（用户要求"安装桥接插件有按钮吗" ✓）
            if (cmd == "profilecheck") return ProfileCheck(args, reg);
            if (cmd == "profilepatch") return ProfilePatch(args, reg);
            if (cmd == "profiles") return Profiles(reg);
            if (cmd == "verify-install") return VerifyInstall(args);
            if (cmd == "wipe") return WipeCmd(args, reg);
            if (cmd == "import") return ImportCmd(args, reg);
            if (cmd == "update-info") return UpdateInfo(reg);
            if (cmd == "update-center") return UpdateCenter(reg, args);   // args: --stream（GUI 流式 ✓ 默认输出不变 ✓）
            if (cmd == "balance") return Balance(reg);   // ★ DeepSeek 余额检测（只读 ✓ key 来自配置 ✗ 不进命令行 ✓）
            if (cmd == "log") return LogCmd(args, reg);
            if (cmd == "about") return AboutCmd();
            if (cmd == "shortcut") return ShortcutCmd(args);
            if (cmd == "ui") return UiCmd();
            if (cmd == "install") return InstallLike(args, reg, false);
            if (cmd == "update") return InstallLike(args, reg, true);
            if (cmd == "uninstall") return UninstallCmd(args, reg);
            if (cmd == "start") return StartCmd(args, reg);
            if (cmd == "stop") return StopCmd(args, reg);
            if (cmd == "sessions") return Sessions(reg);
            if (cmd == "backup-list") return BackupList(args, reg);
            if (cmd == "backup-dir") return BackupDirCmd(args, reg);   // ✓ 备份位置查看/设置 ✓（用户要求"备份路径在备份页面里设置并且显示" ✓）
            if (cmd == "doctor") return Doctor(args, reg);
            if (cmd == "version") { Console.WriteLine("DSHT_VERSION " + ToolkitVersion); return 0; }
            if (cmd == "config-get") return ConfigGet();
            if (cmd == "config-set") return ConfigSet(args, reg);
            if (cmd == "bootdiag") return BootDiag(args, reg);
            if (cmd == "restore") return Restore(args, reg);
            if (cmd == "selftest") return SelfTest(args, reg);
            if (cmd == "check") return Check(reg);
            if (cmd == "backup") return Backup(args, reg);
            if (cmd == "backup-export") return BackupExport(args, reg);
            if (cmd == "backup-delete") return BackupDelete(args, reg);
            if (cmd == "autostart") return AutoStartCmd(args, reg);
            Usage();
            return 2;
        }



        /// <summary>profilecheck：标记行逐条对齐 v2.x 的 ProfileCheckCli。</summary>
        // ================================================================ 桥接插件（可选）


        /// <summary>工具箱自己所在的目录 ✓（单文件发布下 `Assembly.Location` 是空的 ✗ → 用 MainModule ✓）。</summary>
        private static string SelfDir()
        {
            string exe = "";
            try { exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; } catch { }
            if (!string.IsNullOrEmpty(exe))
            {
                try { string d = System.IO.Path.GetDirectoryName(exe); if (!string.IsNullOrEmpty(d)) return d; } catch { }
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>跑一个外部命令并拿回 stdout+stderr ✓（超时 120 秒 ✓ 超时如实说 ✓）。</summary>
        private static string RunExternal(string file, string argLine, out int exitCode)
        {
            exitCode = -1;
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(file, argLine);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = new System.Text.UTF8Encoding(false);
                psi.StandardErrorEncoding = new System.Text.UTF8Encoding(false);
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    System.Threading.Tasks.Task<string> so = System.Threading.Tasks.Task.Run(delegate { return p.StandardOutput.ReadToEnd(); });
                    System.Threading.Tasks.Task<string> se = System.Threading.Tasks.Task.Run(delegate { return p.StandardError.ReadToEnd(); });
                    if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } exitCode = -2; return "（超时 120 秒，已结束该进程）"; }
                    exitCode = p.ExitCode;
                    sb.Append(so.Result);
                    string err = se.Result;
                    if (!string.IsNullOrEmpty(err)) sb.Append(Environment.NewLine).Append("[stderr] ").Append(err);
                    return sb.ToString();
                }
            }
            catch (Exception ex) { return "（无法启动 " + file + "：" + ex.Message + "）"; }
        }





        /// <summary>组合包版本（读该 profile 里它自己的 package.json 的 version；读不到 → 空串）。</summary>
        private static string BundleVersion(IProfileManifestSource src, string profileName, string bundleId)
        {
            JNode root = JsonLite.Parse(src.ReadBundleManifest(profileName, bundleId));
            return root == null ? "" : root.Get("version").AsString("");
        }

        /// <summary>从 cordis.patch.yml 里找出 `disabled: true` 的条目 id。
        /// **按行扫描**（不是 YAML 解析器）：遇到 disabled: true 就向前找最近的 `id:` 行；找不到就跳过（诚实降级，不猜）。</summary>
        private static string[] DisabledEntries(string yaml)
        {
            List<string> found = new List<string>();
            if (string.IsNullOrEmpty(yaml)) return found.ToArray();
            string[] lines = yaml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("disabled:", StringComparison.Ordinal)) continue;
                if (line.IndexOf("true", StringComparison.OrdinalIgnoreCase) < 0) continue;
                for (int k = i - 1; k >= 0 && k > i - 40; k--)
                {
                    string prev = lines[k].Trim();
                    if (prev.StartsWith("disabled:", StringComparison.Ordinal)) continue;
                    if (prev.StartsWith("- id:", StringComparison.Ordinal)) { found.Add(prev.Substring("- id:".Length).Trim().Trim('\'', '"')); break; }
                    if (prev.StartsWith("id:", StringComparison.Ordinal)) { found.Add(prev.Substring("id:".Length).Trim().Trim('\'', '"')); break; }
                }
            }
            return found.ToArray();
        }


        /// <summary>派生指标格式化：未知（-1）→ `unknown`，否则一位小数；**固定 InvariantCulture**（标记行必须机器可读，不受区域设置影响）。</summary>
        private static string Num1(double v)
        {
            return v < 0 ? "unknown" : v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>start（V3 独有）：启动 dsh，然后**用可观测事实确认**是否真的起来 —— 绝不因为"命令发出去了"就报成功。
        /// 标记行：`START_OK <pid>` / `START_FAIL <原因>`，两者之后都会补一行 `START_OBSERVED <状态>`（Ready/Listening/Down）。
        /// 用法：`start [--port <n>] [--profile <name>]`（默认 3080 / web）。</summary>
        /// <summary>取参数值（形如 `--port 3999`）；缺省或非法时返回 fallback。仅用于文案展示，判定逻辑在 TargetForStart 里。</summary>
        private static string ArgOr(string[] args, string name, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name && !string.IsNullOrEmpty(args[i + 1])) return args[i + 1];
            }
            return fallback;
        }
        private static IServiceTarget TargetForStart(string[] args, ServiceRegistry reg)
        {
            int port = 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--port") { int pp; if (int.TryParse(args[i + 1], out pp)) port = pp; }
            }
            if (port <= 0 || port == WebPort) return reg.Get<IServiceTarget>();
            return PlatformComposition.WebFor(port, reg.Get<IPortProbe>(), reg.Get<IHttpProbe>(), reg.Get<IProcessQuery>());
        }
        /// <summary>install / update（V3 独有）：装或升级 dsh。**只用可观测事实判定结果**：
        /// 先看 WhichDsh/DshVersion（是否已装）→ 打印计划 → `--yes` 闸门 → npm → **再复检**。
        /// 标记行：`INSTALL_PLAN/_DRYRUN/_SKIP/_OK/_FAIL` 或 `UPDATE_*`，并始终补一行 `*_OBSERVED <版本|not-installed>`。</summary>
        /// <summary>无参数时的数字菜单（沿用 v2.x 的习惯，条目按 V3 的命令重排）。
        /// **纪律：写操作在菜单里二次确认后，才带 --yes 调用同一个命令实现** —— 闸门不绕过；
        /// 输入 EOF（管道/重定向）视为退出，绝不空转。</summary>
        /// <summary>用法行（未知命令与菜单"全部命令"共用）。</summary>
        /// <summary>用法速查（菜单"全部命令"与未知命令共用）。
        /// 注意：这里**绝不能调用自己** ✗ —— 之前用正则抽取内联那行时把函数体写成了 `Usage();`，
        /// 变成无限递归 → 菜单项 12 直接栈溢出（真机扫描抓到的 ✗）。</summary>
        /// <summary>overview（V3 独有，**只读**）：一个进程给出概览页所需的**全部**标记行 ——
        /// status --detail + sessions + backup-list + profiles 的并集。GUI 每拍从 4 个进程降到 1 个 ✓
        /// （0.5 秒实时档的刷新成本大头是进程启动 ✓）。各子命令输出逐字不变 ✓ 解析方按前缀取行 ✓
        /// 只读聚合页：任一子块失败不打断后面的块（能显示多少显示多少 ✓）。</summary>
        private static int Overview(ServiceRegistry reg)
        {
            Status(reg, true);
            Sessions(reg);
            BackupList(new string[] { "backup-list" }, reg);
            Profiles(reg);
            return 0;
        }

        private static void Usage()
        {
            Console.WriteLine(T("dsh-minato 命令速查：", "dsh-minato commands:"));
            Console.WriteLine("  status [--detail] | overview | describe | doctor | bootdiag | check | selftest | version | about");
            Console.WriteLine("  profiles | profilecheck [--dir <d>] [--file <yaml>] [--diag] | profilepatch --profile <name> --id <entry> [--enable] [--yes]");
            Console.WriteLine("  sessions | log [--lines <n>] [--level info|warn|error] [--grep <text>] [--export <file> [--yes]]");
            Console.WriteLine("  update-info | update-center | update [--yes]");
            Console.WriteLine("  balance（DeepSeek 余额检测 ✓ 只读 ✓ 需先在设置里填 balance_key ✓）");
Console.WriteLine("  config-get | config-set <key> <value>");
            Console.WriteLine("  install [--install-node] [--version <v>] [--list] [--yes] | update [--version <v>] [--list] [--yes] | uninstall [--yes]");
            Console.WriteLine("  update-info（只读：当前/最新/来源/状态/回滚候选 ✓）");
            Console.WriteLine("  start [--port <n>] [--profile <name>] [--yes] | stop [--port <n>] [--target web|desktop] [--force] [--yes]");
            Console.WriteLine("  backup | backup-list [--detail] [--verify] | backup-export --path <备份> --to <目标> [--yes] | backup-delete --path <备份> [--yes] [--yes]");
            Console.WriteLine("  restore --path <备份> [--dry-run] [--apply] [--yes]");
            Console.WriteLine("  import --path <外部备份包> [--yes] | wipe [--yes] | verify-install [--manifest <f>] [--file <f>] [--url <u>]");
            Console.WriteLine("  shortcut [--yes] | ui | 无参数 = 数字菜单");
            Console.WriteLine(T("写操作一律先打印计划，加 --yes 才执行；涉及数据根的真实恢复还要求先设置 DSH_HOME。",
                                "Every write prints its plan first; add --yes to execute. A real restore writes into the effective data root, which is DSH_HOME when set and the default data root when not; only the --apply path is refused when that root is a default location."));
        }


        /// <summary>菜单里的二次确认：只有明确输入 y 才继续（写操作的闸门不绕过）。</summary>
        private static bool Confirm(string what)
        {
            Console.Write(T("将执行：", "will run: ") + what + T("　确认？(y/N) ", "  confirm? (y/N) "));
            string a = Console.ReadLine();
            return a != null && a.Trim().ToLowerInvariant() == "y";
        }

        /// <summary>文件的 SHA-256（小写十六进制 ✓）。</summary>
        private static string Sha256Of(string path)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(path))
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(fs);
                System.Text.StringBuilder sb = new System.Text.StringBuilder(h.Length * 2);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>递归复制目录（导入用 ✓；深度上限兜底，避免符号链接环 ✗）。返回复制文件数。</summary>
        /// <summary>复制备份包的**同级旁挂文件**（存在才复制 ✓ 尽力而为 ✓）。</summary>
        private static void CopySiblingFile(string srcPkg, string dstPkg, string suffix)
        {
            try
            {
                string from = srcPkg.TrimEnd('\\', '/') + suffix;
                if (!System.IO.File.Exists(from)) return;
                System.IO.File.Copy(from, dstPkg.TrimEnd('\\', '/') + suffix, true);
            }
            catch { }
        }
        private static int CopyDirDeep(string from, string to, int depth)
        {
            // ★★★ **第 3 轮审查抓到（MAJOR）**：这里**会跟随符号链接/junction** ✗✗
            //   而项目里其它所有拷贝路径都**显式跳过** reparse point ✓
            //   （WindowsBackupSource.CopyTree / LinuxBackupSource.CopyTree / WindowsFileSystemQuery.Walk / SkipRules ✓）
            //   → 导入一个包、或打包一个含符号链接的工作区时 ✗
            //     → **被链接到的整棵树会被复制进来** ✗（~/.ssh、/etc、%APPDATA% … ✓）
            //     → 信息泄露 + 磁盘被塞满 ✓ 而且 pnpm 工作区**本来就有符号链接** ✓
            // ✓ 现在：**与其它拷贝路径同一策略：跳过 reparse point** ✓✓
            if (depth > 32) return 0;
            System.IO.Directory.CreateDirectory(to);
            int n = 0;
            string[] files = System.IO.Directory.GetFiles(from);
            for (int i = 0; i < files.Length; i++)
            {
                try { if ((System.IO.File.GetAttributes(files[i]) & System.IO.FileAttributes.ReparsePoint) != 0) continue; } catch { }
                System.IO.File.Copy(files[i], System.IO.Path.Combine(to, System.IO.Path.GetFileName(files[i])), true);
                n++;
            }
            string[] dirs = System.IO.Directory.GetDirectories(from);
            for (int i = 0; i < dirs.Length; i++)
            {
                try { if ((System.IO.File.GetAttributes(dirs[i]) & System.IO.FileAttributes.ReparsePoint) != 0) continue; } catch { }
                n += CopyDirDeep(dirs[i], System.IO.Path.Combine(to, System.IO.Path.GetFileName(dirs[i])), depth + 1);
            }
            return n;
        }

        /// <summary>从 JSON 文本里取一个**字符串字段**（够用即可 ✓ 不引 JSON 库 ✓ 保持零依赖 ✓）。
        /// 找不到返回空串 ✓ 不猜 ✗。</summary>
        private static string JsonStr(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return "";
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return "";
            int c = json.IndexOf(':', k + key.Length + 2);
            if (c < 0) return "";
            int q1 = json.IndexOf('"', c + 1);
            if (q1 < 0) return "";
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        /// <summary>取某个仓库最新 Release 的**一行摘要**（best-effort ✓ 超时 6 秒 ✓ 失败返回空 ✓）。</summary>
        private static string FetchLatestRelease(string owner, string repo)
        {
            try
            {
                // ✗✗ 真机实测（2026-09-30）：CLI 用 csc / .NET 4.0 编译 ✓ 默认**只有 TLS 1.0** ✗
                //    → GitHub 要求 TLS 1.2 → 请求抛异常 → 被 catch 吞掉 → "取不到更新日志" ✓✓
                //    （PowerShell 测试却成功 ✓ 因为它跑在 4.x 上、默认开了 TLS 1.2 ✓ 这个差异把人骗了 ✓）
                // .NET 4.0 **没有** SecurityProtocolType.Tls12 枚举 ✗ → 用数值 3072 ✓
                try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; } catch { }
                string url = "https://api.github.com/repos/" + owner + "/" + repo + "/releases?per_page=1";
                System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.UserAgent = "dsh-minato";
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (System.IO.StreamReader sr = new System.IO.StreamReader(resp.GetResponseStream()))
                {
                    string body = sr.ReadToEnd();
                    string tag = JsonStr(body, "tag_name");
                    if (string.IsNullOrEmpty(tag)) return "";
                    string name = JsonStr(body, "name");
                    string one = string.IsNullOrEmpty(name) ? tag : (tag + " " + name);
                    one = one.Replace("\r", " ").Replace("\n", " ");
                    if (one.Length > 160) one = one.Substring(0, 160) + "…";
                    return one;
                }
            }
            catch { }
            // 兜底：**atom 源**（GitHub 的 RSS ✓ 走 www.github.com ✓ 有些网络下比 api 更通 ✓）
            try
            {
                string url2 = "https://github.com/" + owner + "/" + repo + "/releases.atom";
                System.Net.HttpWebRequest r2 = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url2);
                r2.UserAgent = "dsh-minato";
                r2.Timeout = 6000;
                r2.ReadWriteTimeout = 6000;
                using (System.Net.HttpWebResponse resp2 = (System.Net.HttpWebResponse)r2.GetResponse())
                using (System.IO.StreamReader sr2 = new System.IO.StreamReader(resp2.GetResponseStream()))
                {
                    string body2 = sr2.ReadToEnd();
                    int ti = body2.IndexOf("<title", StringComparison.Ordinal);
                    if (ti >= 0)
                    {
                        int t1 = body2.IndexOf('>', ti);
                        int t2 = t1 < 0 ? -1 : body2.IndexOf("</title>", t1, StringComparison.Ordinal);
                        if (t1 > 0 && t2 > t1)
                        {
                            string one2 = body2.Substring(t1 + 1, t2 - t1 - 1).Trim().Replace("\r", " ").Replace("\n", " ");
                            if (one2.Length > 160) one2 = one2.Substring(0, 160) + "…";
                            return one2;
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        /// <summary>本工具自己的版本串 ✓（取不到就 unknown ✓ 不猜 ✗）。</summary>
        private static string DshtVersionString()
        {
            // ✗✗ 原来用 FileVersionInfo.GetVersionInfo(asm.Location) → **单文件发布时 Location 是空的** ✗✗
            //    （.NET 5+ 已知行为 ✓）→ 实测 Linux 上显示 unknown ✓
            // ✓ 更新中心本来就在 CLI 里 → **直接用编译期的 ToolkitVersion** ✓ 连反射都不用 ✓
            if (!string.IsNullOrEmpty(ToolkitVersion)) return ToolkitVersion;
            return "unknown";
        }





        /// <summary>跑一条命令并丢弃输出（systemctl 之类 ✓ 失败不影响主流程 ✓）。</summary>
        private static void RunQuiet(string exe, string args)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(exe, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    if (p != null) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(8000); }
                }
            }
            catch { }
        }





        /// <summary>从标记里取内容哈希（没有则 null ✓）。</summary>
        private static string MarkerHash(string markerPath)
        {
            try
            {
                string[] ls = System.IO.File.ReadAllLines(markerPath);
                for (int i = 0; i < ls.Length; i++)
                    if (ls[i].StartsWith("sha256=", StringComparison.Ordinal)) return ls[i].Substring(7).Trim();
            }
            catch { }
            return null;
        }




        /// <summary>doctor：七类体检。首行 DOCTOR_OK|WARN|ERROR n，其后每行 [级别] 类别 描述。逐条对齐 v2.x。
        /// 可选 `--report <file>`：写完整诊断报告（含配置/日志摘要，全部脱敏）→ `DOCTOR_REPORT <路径>`；
        /// 写失败 → `DOCTOR_WRITE_FAIL <原因>`。全程只读（与 v2.x 一致，报告用 UTF-8 **带 BOM** 写）。</summary>
        /// <summary>当前操作系统名（跨平台：Linux 上不能写 "Windows" —— 真机测试抓到的 bug）。</summary>
        /// <summary>I7 FIX: the running executable path. Assembly.Location is empty for single-file
        /// builds, so autostart entries were written with an empty command while reporting success.</summary>
        private static string SelfExePath()
        {
            try
            {
                string p = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch { }
            return "dsh-minato";
        }

        private static string OsName()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return "Windows";
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)) return "macOS";
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)) return "Linux";
            return "未知系统";
        }



        /// <summary>工具箱版本（源码常量：保证 csc 与 dotnet 两种构建报告一致）。</summary>
        internal const string ToolkitVersion = "3.0.5";

        private const string NpmOfficial = "https://registry.npmjs.org";

        private static bool FileExists(string p) { try { return System.IO.File.Exists(p); } catch { return false; } }

        private static string Flag(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }



        /// <summary>工作区解析（对齐 v2.x 的 WorkspaceRoot）：`ws=` 配置优先——配置了但目录不存在 → null
        /// （**不回退自动探测**，避免误备份/误恢复）；未配置 → 用平台自动探测（Windows：exe 上两级 + 合理性判定；
        /// Linux：**当前工作目录**（B2 修复后的事实 ✓；此处原写"诚实返回 null" ✗ 已过时 ✓ —— 那是修 cwd 基准之前的行为 ✗）。
        /// dry-run 与真实恢复都走这里，保证两处目标一致。</summary>
        /// <summary>解析 `ws=` 里的**多个工作区**（`;` 分隔 ✓ —— 不用 `:` 因为 Windows 路径含 `:` ✗✓）。
        /// 单个路径（无 `;`）→ 返回一个元素 ✓ 完全向后兼容 ✓✓。</summary>
        private static string[] ConfiguredWorkspaces()
        {
            string raw = _cfg == null || _cfg.Workspace == null ? "" : _cfg.Workspace;
            if (raw.Trim().Length == 0) return new string[0];
            string[] parts = raw.Split(';');
            List<string> outp = new List<string>();
            for (int i = 0; i < parts.Length; i++) { string s = parts[i].Trim().Trim('"'); if (s.Length > 0) outp.Add(s); }
            return outp.ToArray();
        }








        /// <summary>有效备份目录判定（注入给领域校验器）。</summary>
        private static Func<string, bool> IsValidBackupDirFn(ServiceRegistry reg)
        {
            IBackupSource bk = reg.Get<IBackupSource>();
            return delegate(string dir) { return BackupPackage.IsValidPackage(bk.Snapshot(dir)); };
        }

        /// <summary>备份导出：校验路径后**复制到目标目录**（会写盘 → 需要 --yes 闸门 ✓）。</summary>
        // ★★★ **用户反馈（2026-09-30）**：「gui 删除备份好像也有问题」✓✓ → 追出**两个**真 bug ✗✗
        //   ✗ 这里（export）与下面的 delete **是同一个 bug** ✗：
        //     校验走 `PathValidator`（内部 `ResolveBackupPath` → **绝对路径** ✓）
        //     但**实际操作**用的却是**原始的 --path 字符串** ✗（GUI 传的是裸名字 ✓）
        //     → 于是操作的是**相对于当前工作目录**的同名目录 ✗ 而不是备份根里的那份 ✗
        //     → delete 那边还会因此**绕过**「删除后仍存在」检查 ✓ → **假报 BKDEL_OK** ✗
        //     → 用户看到"点了两次但没删" ✓✓ **完全解释通了** ✓
        //     → **而且危险** ✗：CWD 里恰好有同名目录会被误删 ✓
        //   ✓ 现在：**校验和操作都用同一个解析后的绝对路径** ✓✓


        /// <summary>backup：非交互备份（手动类）。标记逐条对齐 v2.x 的 NIBackup。</summary>
        /// <summary>备份 ✓。**用户要求（2026-09-30）**：「第一次备份必须手动设置目录，避免卸载时删掉备份」✓✓
        /// 背景：备份根默认是 `StateDir/backup` ✓ 而 **Windows 上 StateDir 就是安装目录** ✗
        ///   → 备份**物理上躺在安装目录里** ✓ 卸载**理论上**会连它一起清 ✗
        ///   → 安装器侧已加保险（**卸载显式跳过 backup/ 与 logs/** ✓✓）
        ///   → 这里再**明确警告一次** ✓ 并告诉用户怎么把备份移出去 ✓
        ///   → 完整做法（让备份根本身可配置 ✓ 第一次强制指定 ✓）见下方 TODO ✓</summary>
        private static void WarnIfBackupsInsideInstall(ServiceRegistry reg)
        {
            try
            {
                string bk = reg.Get<IBackupSource>().BackupsRoot;
                string st = reg.Get<IPaths>().StateDir;
                if (string.IsNullOrEmpty(bk) || string.IsNullOrEmpty(st)) return;
                string b = System.IO.Path.GetFullPath(bk).TrimEnd('\\', '/');
                string s = System.IO.Path.GetFullPath(st).TrimEnd('\\', '/');
                bool inside = b.StartsWith(s + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(b, s, StringComparison.OrdinalIgnoreCase);
                if (!inside) return;
                // ★★ **用户反馈（2026-09-30）**：「GUI 备份报错」✗✗ —— 命令其实是**成功**的 ✓
                //   真因：这里原来输出**4 行长警告** ✓ 而 GUI 的 toast 把 CLI 输出**原样弹出** ✗
                //     → 满屏 `✗` 与「可能被一起清掉」→ **读起来就是报错** ✓✓
                //   ✓ 现在：**缩成一句** ✓✓（安装器侧已加保险：卸载显式跳过 backup/ ✓
                //     所以这句只是**提示** ✓ 不是告警 ✓ 也不再吓人 ✓）
                Console.WriteLine("BACKUP_WARN " + T(
                    "备份在安装目录内 ✓ 建议用 --to <目录> 或环境变量 DSH_MINATO_BACKUP_DIR 放到外面 ✓",
                    "backups live inside the install folder; use --to <dir> or DSH_MINATO_BACKUP_DIR to move them out"));
            }
            catch { }
        }

        private static int Backup(string[] args, ServiceRegistry reg)
        {
            // ★★ **用户要求（2026-09-30）**：「第一次备份必须手动设置目录」✓✓
            //   ✓ `--to <目录>` → **本次运行把备份根指定到那里** ✓✓（放在安装目录之外才稳妥 ✓）
            //   ✓ 没给 `--to` → 走默认根 ✓ 但**若默认根在安装/状态目录内 → 明确警告** ✓
            string toDir = (Flag(args, "--to") ?? "").Trim().Trim('"');
            if (!string.IsNullOrEmpty(toDir))
            {
                try
                {
                    string full = System.IO.Path.GetFullPath(toDir);
                    System.IO.Directory.CreateDirectory(full);
                    Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", full);
                    // D2 FIX (CLI audit MAJOR): --to works through an environment variable, but the
                    // settings file takes priority over it, and the write below swallowed its own
                    // failure. When the file could not be written the variable was ignored, so the
                    // tool announced a destination and then backed up somewhere else.
                    // ✓ **记住这个选择** ✓✓（用户要求"第一次弹窗选择" ✓ 但**不该每次都问** ✗）
                    //   → 写进 `<StateDir>/.backup-dir` ✓ 之后所有命令都用它 ✓✓
                    // ★★★ **真机 CI 修复（2026-10-01）—— 写设置文件前必须先建目录** ✓✓
                    //   ✗ 原来直接 `WriteAllText("<StateDir>/.backup-dir", …)` ✗
                    //     → **全新安装上 StateDir 还不存在** ✗（它由日志/备份首次写入时才建 ✓）
                    //     → `Could not find a part of the path …` ✗ → **位置记不住** ✗✗
                    //     → 后续 `backup-list` / `--verify` **回落到默认根** ✗ → 找不到刚建的包 ✓
                    //   · 真机复现（Ubuntu）：干净 XDG 下第一次 `backup --to <dir>`
                    //     → `BACKUP_WARN 记不住这个位置` ✓ → 4-5 条连锁失败 ✓
                    //     第二次跑就好 ✓ —— 因为第一次的日志**顺手把那个目录建出来了** ✗✗
                    //   ✓ 现在：**先 `CreateDirectory` 再写** ✓✓（真用户也受益 ✓ 不只是测试 ✓）
                    try
                    {
                        string selDir = System.IO.Path.Combine(reg.Get<IPaths>().StateDir, ".backup-dir");
                        try { string parent = System.IO.Path.GetDirectoryName(selDir); if (!string.IsNullOrEmpty(parent)) System.IO.Directory.CreateDirectory(parent); } catch { }
                        System.IO.File.WriteAllText(selDir, full, new System.Text.UTF8Encoding(false));
                    }
                    catch (Exception wex) { Console.WriteLine("BACKUP_WARN " + T("记不住这个位置（设置文件写不进去 ✓）：" + wex.Message, "could not remember the location: " + wex.Message)); }
                    string effective = reg.Get<IBackupSource>().BackupsRoot;
                    if (!string.Equals((effective ?? "").TrimEnd('\\', '/'), full.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("BACKUP_FAIL " + T("指定的目录**没有生效** ✓ 本次备份会写到：" + effective + " ✗（设置文件可能只读 ✓）", "the --to folder did not take effect; this backup would go to: " + effective));
                        return 0;
                    }
                    Console.WriteLine("BACKUP_TO " + full + " " + T("本次备份写到这个目录 ✓（放在安装目录之外才稳妥 ✓）", "this backup goes here"));
                }
                catch (Exception ex) { Console.WriteLine("BACKUP_FAIL " + T("无法使用 --to 指定的目录：" + ex.Message, "cannot use --to dir: " + ex.Message)); return 0; }
            }
            // ★★★ **用户要求（2026-09-30）**：「第一次备份弹窗选择」✓✓
            //   → **第一次备份**（备份根里还没有任何有效备份 ✓）**必须指定目录** ✓✓
            //   → 没给 `--to` 就**拒绝** ✓ 并说清怎么给 ✓（放在安装目录之外才稳妥 ✓）
            //   → 已有备份 → **沿用上次的根** ✓ 不再每次追问 ✓✓
            bool hasAny = false;   // ✓ 提到 if 外面 ✓ 后面 WarnIfBackupsInsideInstall 要用 ✓✓
            if (string.IsNullOrEmpty(toDir))
            {
                try
                {
                    string br = reg.Get<IBackupSource>().BackupsRoot;   // ✓ 这里 `bk` 还没声明 ✓ 直接取 ✓
                    // E1 FIX (CLI audit MINOR): this counted any dsh-data-* folder, and an interrupted
                    // first backup leaves exactly such an empty folder behind (the destination is
                    // created before anything is copied). The next run then skipped the guard and wrote
                    // a package inside the install folder - what that guard exists to prevent.
                    if (System.IO.Directory.Exists(br))
                    {
                        string[] cands = System.IO.Directory.GetDirectories(br, "dsh-data-*");
                        for (int ci = 0; ci < cands.Length; ci++)
                        {
                            try { if (BackupPackage.IsValidPackage(reg.Get<IBackupSource>().Snapshot(cands[ci]))) { hasAny = true; break; } }
                            catch { }
                        }
                    }
                }
                catch { }
                if (!hasAny)
                {
                    // D4 FIX (CLI audit MINOR): when the configured backup folder no longer exists the
                    // message said "the first backup needs a folder", which is wrong and misleading -
                    // the user HAS backups, in a folder that is gone. Say so, and only then apply the
                    // first-backup rule.
                    string br2 = "";
                    try { br2 = reg.Get<IBackupSource>().BackupsRoot; } catch { }
                    // ★★★ **N6 修复（复审 MINOR —— D4 的回归）** ✓✓
                    //   ✗ 原来只要"根不存在"就报"配置的备份目录不存在" ✗
                    //     → 而**全新安装**时默认根 `<StateDir>\backup` **本来就不存在** ✗
                    //     → **第一次备份**看到的是"你可能移动或删除了它" ✗✗ **完全误导** ✓
                    //   ✓ 现在：**只有**显式配置过（`.backup-dir` 或环境变量）**而且**那个目录没了
                    //     才走 D4 分支 ✓ 否则照常走"第一次备份"提示 ✓✓
                    bool explicitlySet = false;
                    try
                    {
                        // F-H FIX (CLI final review): an empty or whitespace-only file, or a whitespace-only
                        // environment variable, counted as "explicitly configured" even though the source ignores
                        // it - so a fresh install could still show the misleading "configured folder is missing".
                        string selFile = System.IO.Path.Combine(reg.Get<IPaths>().StateDir, ".backup-dir");
                        bool selSet = false;
                        try { selSet = System.IO.File.Exists(selFile) && System.IO.File.ReadAllText(selFile).Trim().Length > 0; } catch { }
                        string envSel = Environment.GetEnvironmentVariable("DSH_MINATO_BACKUP_DIR");
                        explicitlySet = selSet || !string.IsNullOrEmpty((envSel == null ? "" : envSel).Trim());
                    }
                    catch { }
                    if (explicitlySet && !string.IsNullOrEmpty(br2) && !System.IO.Directory.Exists(br2))
                    {
                        Console.WriteLine("BACKUP_FAIL " + T("**配置的备份目录不存在** ✗：" + br2 + " ✓（你可能移动或删除了它 ✓）请用 `backup-dir --reset` 恢复默认 ✓ 或用 `--to <目录>` 指定新的 ✓", "the configured backups folder does not exist: " + br2));
                        return 0;
                    }
                    // ★★★ **N9 修复（复审 MAJOR —— GUI 把**所有** BACKUP_FAIL 都当成"要选目录"）** ✓✓
                    //   ✗ GUI 只认 `BACKUP_FAIL` ✗ → 而 CLI 对**每一种失败**都打它 ✓
                    //     （配置目录不存在 ✓ `--to` 没生效 ✓ 数据目录不存在 ✓ 备份真的失败 ✓）
                    //     → 那些**真正的错误**会被 GUI 静默地变成"弹文件夹选择器" ✗✗
                    //   ✓ 现在：**打一个专用标记** ✓✓ GUI 只认它 ✓ 其余错误照常显示 ✓
                    Console.WriteLine("BACKUP_NEEDS_DIR 第一次备份需要先选一个目录");
                    Console.WriteLine("BACKUP_FAIL " + T(
                        "**第一次备份必须指定目录** ✓ 请加 `--to <目录>` ✓（建议放在安装目录之外 ✓ 例如 D:\\dsh-backups ✓）"
                        + "；GUI 里第一次点「立即备份」也会弹窗让你选 ✓✓",
                        "the first backup needs an explicit folder - add --to <dir>, preferably outside the install folder"));
                    return 0;
                }
            }
            // ✓✓ **用户反馈（2026-10-01）**：「现在备份还是受阻」✗
            //   真因之一：这条 `BACKUP_WARN` **每次备份都弹** ✗
            //     → toast 第一行永远是「建议用 --to …」✓ → **读起来像"你得先做什么"** ✓✓
            //   ✓ 现在：**只在第一次备份时提示一次** ✓✓（之后就安静 ✓ 备份本来就成功 ✓）
            if (!hasAny) WarnIfBackupsInsideInstall(reg);
            IPaths paths = reg.Get<IPaths>();
            IBackupSource bk = reg.Get<IBackupSource>();
            string src = paths.DataRoot;
            if (!reg.Get<IFileSystemQuery>().DirectoryExists(src))
            {
                Console.WriteLine("BACKUP_FAIL " + T("数据目录不存在：" + src, "data dir not found: " + src));
                return 0;
            }
            BackupResult r = bk.Create(src, BackupKind.Manual, _cfg == null ? 3 : _cfg.KeepBackups, WorkspaceRoot(reg));
            if (r == null)
            {
                // ★ 架构审计抓到（S7）：原来是"见 launcher.log" ✗ —— 而原因**就在手边** ✓
                //   → 现在：**把平台层记下的真实原因打印出来** ✓✓（仍同时指路日志 ✓）
                string why = PlatformIsWindows() ? Dsht.Platform.Windows.WindowsBackupSource.LastError : Dsht.Platform.Linux.LinuxBackupSource.LastError;
                Console.WriteLine("BACKUP_FAIL " + T("备份失败：" + (string.IsNullOrEmpty(why) ? "（平台层没有给出原因 ✓ 见 launcher.log）" : why),
                                                          "backup failed: " + (string.IsNullOrEmpty(why) ? "(the platform layer gave no reason; see launcher.log)" : why)));
                return 0;
            }
            if (r.SkippedNested > 0)
                Console.WriteLine(T("已跳过 " + r.SkippedNested + " 个嵌套备份目录（dsh-data-*），不复制进本次备份。",
                                    "Skipped " + r.SkippedNested + " nested backup folder(s) (dsh-data-*), not copied into this backup."));
            // ★★ 第 3 轮审查抓到：**先打印 BACKUP_OK、之后才看 FailedCopies** ✗✗
            //   → 一个部分失败的备份同时打印"成功"和"不完整" ✓ 而 GUI 只看 BACKUP_OK ✓ → 报成成功 ✗
            // ✓ 现在：**不完整就不报 OK** ✓✓（GUI 因此如实显示未完成 ✓）
            if (r.FailedCopies > 0)
                Console.WriteLine("BACKUP_INCOMPLETE " + r.Path + " " + T("有 " + r.FailedCopies + " 项没能备份 ✓ 这个包**不完整** ✓（restore 会要求 --force）", "the package is incomplete: " + r.FailedCopies + " items could not be backed up"));
            else
                Console.WriteLine("BACKUP_OK " + r.Path);
            int _wsDone = PackageAllWorkspaces(r.Path, reg);
            if (_wsDone > 0) Console.WriteLine("BACKUP_WORKSPACES " + _wsDone + T(" 个工作区已打包（新格式 _workspace/<名称>/.dshws ✓）", " workspaces packaged (new layout _workspace/<name>/.dshws)"));
            AddContentHashToMarker(r.Path);   // 标记补内容哈希 ✓（能发现"计数对但内容残" ✗✓）
            // ★★ 审查修复（2026-10-06，C1）：S9 修复把 if 的方法体连同花括号一起删了 ✗
            //   → 裸 `if (r.FailedCopies > 0)` 吞掉了 OpLog ✗✗ 完整备份**不写日志**、不完整反而写 "backup OK"（与事实相反）
            // ✓ 现在：**无条件写** ✓ 不完整用 WARN + INCOMPLETE 文案（与上面控制台的 BACKUP_INCOMPLETE 行一致 ✓）
            OpLog(reg, r.FailedCopies > 0 ? "WARN" : "INFO",
                  (r.FailedCopies > 0 ? "backup INCOMPLETE " : "backup OK ") + r.Path);
            return 0;
        }

        private const string GithubHandle = "github.com/sakanamaru";

        /// <summary>命令输出净化：去首尾空白，空串 → null（v2.x 的捕获不带尾换行）。</summary>
        private static string TrimOrNull(string s)
        {
            if (s == null) return null;
            string t = s.Trim();
            return t.Length == 0 ? null : t;
        }

        /// <summary>横幅：与 v2.x 同构（版本行按产品版本不同，比对时忽略）。</summary>
        private static void Banner()
        {
            Console.WriteLine("==============================================");
            Console.WriteLine("  DeepSeek Harness Toolkit V" + ToolkitVersion);   // 契约比对会忽略这行，但保持原文最省事
            Console.WriteLine("==============================================");
            Console.WriteLine("  v1 脚本协助 : SOGR-Momono Dango（QwenPaw/DeepseekAPI-V4-Flash-0731）");
            Console.WriteLine("  v2 重构封装 : DeepSeek DSH （DSH/DeepseekAPI-V4-Flash-0731）");
            Console.WriteLine("  GitHub    : @sakanamaru  https://" + GithubHandle);
            Console.WriteLine("----------------------------------------------");
            Console.WriteLine("  " + T("⚠ 非官方工具，由社区独立开发，与 DeepSeek 官方无关。", "⚠ Unofficial community tool, not affiliated with DeepSeek."));
        }

        private static void Pause()
        {
            Console.WriteLine();
            Console.WriteLine(T("  按任意键继续...", "  Press any key to continue..."));
            try { Console.ReadKey(true); } catch { }
            Console.WriteLine();
        }

        /// <summary>check：安装/版本/更新/服务/语言一览（GUI 检查页数据源）。逐条对齐 v2.x 的 Check()。</summary>
        private static int Check(ServiceRegistry reg)
        {
            IToolchainQuery tc = reg.Get<IToolchainQuery>();
            Banner();
            string node = TrimOrNull(tc.NodeVersion());
            Console.WriteLine("  Node.js    : " + (string.IsNullOrWhiteSpace(node) ? T("未检测到", "not found") : node));
            string npm = TrimOrNull(tc.NpmVersion());
            Console.WriteLine("  npm        : " + (string.IsNullOrWhiteSpace(npm) ? T("未检测到", "not found") : npm));
            string dsh = tc.WhichDsh();
            Console.WriteLine("  dsh        : " + (dsh == null ? T("未安装", "not installed") : dsh + " ✓"));
            if (dsh != null)
            {
                string v = TrimOrNull(tc.DshVersion());
                Console.WriteLine("  dsh 版本   : " + (string.IsNullOrWhiteSpace(v) ? T("（读取失败）", "(read failed)") : v));
                if (_cfg.CheckDshUpdate)
                {
                    string latest = VersionComparer.SanitizeLatest(tc.NpmViewLatest());
                    string line;
                    if (string.IsNullOrEmpty(latest)) line = T("（离线，未获取）", "(offline, n/a)");
                    else if (string.IsNullOrWhiteSpace(v) || VersionComparer.Compare(v, latest) < 0)
                        line = latest + (string.IsNullOrWhiteSpace(v) ? "" : T("（当前 " + v + "，有更新）", " (current " + v + ", update available)"));
                    else line = latest + T("（已是最新）", " (up to date)");
                    Console.WriteLine("  dsh 最新   : " + line);
                }
            }
            ServiceReport sr = reg.Get<IServiceTarget>().Probe();
            string ws = sr.State == ServiceState.Ready ? PlatformComposition.WebUrl + " " + T("已在运行", "running")
                : (sr.State == ServiceState.Listening ? T("启动中（端口已开，服务未就绪）", "starting (port open, not ready)") : T("未启动", "not started"));
            Console.WriteLine("  Web 服务   : " + ws);
            Console.WriteLine("  UI 语言    : " + (_cfg.Lang == "auto" ? T("跟随系统", "follow system") : (_cfg.Lang == "zh" ? "简体中文" : "English")));
            Pause();
            return 0;
        }




        private static void PrintPlan(long[] p)
        {
            Console.WriteLine("DRYRUN_NEW " + p[0]);
            Console.WriteLine("DRYRUN_OVERWRITE " + p[1]);
            Console.WriteLine("DRYRUN_KEEP " + p[2]);
            Console.WriteLine("DRYRUN_BYTES " + p[3]);
        }

        private static long[] PlanMerge(ServiceRegistry reg, string src, string dst, string skipTopDir, string skipTopFile, bool topRules)
        {
            IFileSystemQuery fs = reg.Get<IFileSystemQuery>();
            return MergePlanner.Plan(fs.WalkSource(src, skipTopDir, skipTopFile, topRules), fs.WalkDestination(dst));
        }

        /// <summary>restore：真实合并恢复。顺序与 v2.x 的 NIRestoreCore 一致：
        ///   定位/校验备份 → （--apply 准入）→ 运行中拒绝 → 恢复前自动备份 → 自身完整性闸门 → 恢复。
        /// V3 独有：`--apply` 显式开关，且只允许写入**隔离数据根**（见 RestoreApplyPolicy）——
        /// 生效数据根等于默认位置时直接拒绝，因此 V3 的真实恢复永远不会写进用户默认数据根。
        /// 与 v2.x 的有意差异（更诚实）：只有真正成功才打印 RESTORE_OK（v2.x 在恢复失败时也会打印 RESTORE_OK）。</summary>
        private static int Restore(string[] args, ServiceRegistry reg)
        {
            if (Has(args, "--dry-run") || Has(args, "-dry-run")) return DryRun(args, reg);
            bool apply = Has(args, "--apply");
            IBackupSource bk = reg.Get<IBackupSource>();
            IPaths paths = reg.Get<IPaths>();
            IFileSystemQuery fs = reg.Get<IFileSystemQuery>();
            string bkDir;
            string pathArg = Flag(args, "--path");
            if (pathArg != null)
            {
                string reason = PathValidator.ValidateRestorePath(pathArg, bk.BackupsRoot, IsValidBackupDirFn(reg));
                if (reason != null)
                {
                    if (reason == "no-path") Console.WriteLine("RESTORE_FAIL " + T("未指定备份目录", "no backup specified"));
                    else if (reason == "outside") Console.WriteLine("RESTORE_FAIL " + T("备份目录不在备份根内", "backup dir is outside the backups root"));
                    else Console.WriteLine("RESTORE_FAIL " + T("无效备份目录", "invalid backup directory"));
                    return 0;
                }
                // ★ 审查抓到：上面校验的是 ResolveBackupPath 的结果 ✗，而这里用的是**原始参数** ✗
                //   → 只给名字时（GUI 就是这样）校验看的是 &lt;备份根&gt;/&lt;名字&gt;，实际读的却是 &lt;当前目录&gt;/&lt;名字&gt; ✗✗
                //   → 可能失败，也可能把当前目录里同名的 dsh-data-* 目录恢复进数据根 ✓
                // ✓ 现在：**校验与操作同一个值** ✓✓
                bkDir = Dsht.Domain.Services.PathValidator.ResolveBackupPath(pathArg.Trim().Trim('"'), bk.BackupsRoot);
                string trunc = BackupTruncatedReason(bkDir);
                if (!string.IsNullOrEmpty(trunc) && !Has(args, "--force"))
                {
                    Console.WriteLine("RESTORE_FAIL " + trunc + T("；确认要用它恢复请加 --force", "; add --force to restore from it anyway"));
                    return 0;
                }
                if (!string.IsNullOrEmpty(trunc)) { Console.WriteLine("RESTORE_WARN " + trunc); OpLog(reg, "WARN", "restore used --force on a truncated backup: " + bkDir); }
            }
            else
            {
                if (!fs.DirectoryExists(bk.BackupsRoot)) { Console.WriteLine("RESTORE_FAIL " + T("没有备份", "no backups")); return 0; }
                List<BackupEntry> all = bk.ListRaw();
                string latest = null;
                for (int i = all.Count - 1; i >= 0; i--) { if (BackupPackage.IsValidPackage(all[i].Snapshot)) { latest = all[i].Path; break; } }
                if (latest == null) { Console.WriteLine("RESTORE_FAIL " + T("无有效备份", "no valid backup")); return 0; }
                // ★★ 第 2 轮审查抓到：**自动选出的包也要过完整性闸门** ✓✓（我上一版只堵了 --path 分支 ✗）
                //   → 否则被中断的包只要是最新的一个，就仍会被恢复并打印 RESTORE_OK ✗
                {
                    string autoTrunc = BackupTruncatedReason(latest);
                    if (!string.IsNullOrEmpty(autoTrunc) && !Has(args, "--force"))
                    {
                        Console.WriteLine("RESTORE_FAIL " + autoTrunc + T("；确认要用它恢复请加 --force", "; add --force to restore from it anyway"));
                        return 0;
                    }
                    if (!string.IsNullOrEmpty(autoTrunc)) Console.WriteLine("RESTORE_WARN " + autoTrunc);
                }
                bkDir = latest;
            }

            // --apply 准入：真实写盘只允许发生在隔离数据根上（默认数据根永不被 V3 恢复写入）
            string applyReason = RestoreApplyPolicy.Judge(apply, Environment.GetEnvironmentVariable("DSH_HOME"),
                paths.DataRoot, PlatformComposition.DefaultDataRoots());
            if (applyReason != null)
            {
                Console.WriteLine("RESTORE_FAIL " + RestoreApplyPolicy.Message(applyReason, !IsEn()));
                return 0;
            }

            // 安全闸门（逐条对齐 v2.x 的 NIRestoreCore）：运行中拒绝 → 恢复前自动备份
            ServiceReport sr = reg.Get<IServiceTarget>().Probe();
            if (sr.State != ServiceState.Down && !apply)
            {
                Console.WriteLine("RESTORE_FAIL " + T("dsh 正在运行，无法恢复", "dsh is running; cannot restore"));
                return 0;
            }
            if (apply)
            {
                // 跳过"运行中"闸门是人类的显式断言，但观测到的事实必须原样打出来（证据链，不静默）
                OpLog(reg, "WARN", "restore --apply skipped the running-service gate (observed: " + sr.State + ")");
                Console.WriteLine("RESTORE_APPLY_ACK " + T("已按 --apply 跳过「运行中」闸门；观测到的服务状态：",
                    "running-service gate skipped by --apply; observed service state: ") + sr.State + (sr.Pid > 0 ? " pid=" + sr.Pid : ""));
                Console.WriteLine("RESTORE_APPLY_ROOT " + paths.DataRoot);
            }

            string preRollbackPath = null;   // ★ C1：回滚锚点要在失败分支里也能用 ✓✓
            string dstRoot = paths.DataRoot;
            if (fs.DirectoryExists(dstRoot))
            {
                BackupResult pre = bk.Create(dstRoot, BackupKind.PreRestore, _cfg == null ? 3 : _cfg.KeepBackups, WorkspaceRoot(reg));
                if (pre == null) { Console.WriteLine("RESTORE_FAIL " + T("恢复前自动备份失败", "pre-restore backup failed")); return 0; }
                AddContentHashToMarker(pre.Path);   // 回滚锚点也要能自证完整 ✓✓（恢复前自动备份 ✓）
            preRollbackPath = pre.Path;   // ★ C1：记下来 ✓ 恢复失败时要自动回滚 ✓
            Console.WriteLine("RESTORE_PRE_BACKUP " + pre.Path);   // 回滚锚点必须**总是**告诉用户 ✗（我曾在一次编辑中误删此行 ✗）
            }

            if (!IntegrityGate(reg)) return 0;   // 对齐 v2.x：完整性不匹配时在写盘前拒绝

            RestoreOutcome o = bk.Restore(bkDir, dstRoot, WorkspaceRoot(reg));
            if (!o.Ok)
            {
                Console.WriteLine("RESTORE_FAIL " + T("恢复失败：" + (o.Error ?? ""), "restore failed: " + (o.Error ?? "")));
                // ★★★ 架构审计抓到（CRITICAL）：恢复没有回滚 ✗✗
                //   → 顶层目录是逐个拷贝的 ✓ 中途失败会留下半合并的数据根 ✗
                //   → 回滚锚点只打印了位置 ✓ 从不自动使用 ✗ · 而且这里还返回 0（假成功 ✗）
                // ✓ 现在：失败就自动把恢复前那份打回去 ✓✓ 并如实报告 ✓ 退出码非 0 ✓
                if (!string.IsNullOrEmpty(preRollbackPath))
                {
                    RestoreOutcome rb = bk.Restore(preRollbackPath, dstRoot, WorkspaceRoot(reg));
                    if (rb != null && rb.Ok)
                        Console.WriteLine("RESTORE_ROLLED_BACK " + preRollbackPath + " " + T("已自动回滚到恢复前的状态 ✓", "rolled back to the pre-restore state"));
                    else
                        Console.WriteLine("RESTORE_ROLLBACK_FAILED " + preRollbackPath + " " + T("自动回滚也失败了 ✗ 数据根可能是半合并状态 ✓ 请手动用上面那个包恢复 ✓", "automatic rollback ALSO failed; the data root may be half-merged - restore it by hand from the package above"));
                }
                else
                    Console.WriteLine("RESTORE_NO_ROLLBACK " + T("没有可用的回滚锚点（数据根原先不存在 ✓ 所以没建）✓", "no rollback anchor was available (the data root did not exist before)"));
                return 1;   // ★ 恢复失败不能返回 0 ✓✓（自动化会以为成功了 ✗）
            }
            Console.WriteLine("RESTORE_OK " + bkDir);
            OpLog(reg, "INFO", "restore OK " + bkDir);
            if (o.WorkspacesRestored > 0) Console.WriteLine("RESTORE_WS_RESTORED " + o.WorkspacesRestored);
            if (o.WorkspacesSkipped > 0) Console.WriteLine("RESTORE_WS_SKIPPED " + o.WorkspacesSkipped);
            if (o.WorkspacesUnrecognized > 0) Console.WriteLine("RESTORE_WS_UNRECOGNIZED " + o.WorkspacesUnrecognized);
            return 0;
        }


        /// <summary>dryrun：只读合并计划。标记与文案逐条对齐 v2.x 的 NIRestoreDryRun。</summary>
        private static int DryRun(string[] args, ServiceRegistry reg)
        {
            string pathArg = Flag(args, "--path");
            IBackupSource bk = reg.Get<IBackupSource>();
            IPaths paths = reg.Get<IPaths>();
            IFileSystemQuery fs = reg.Get<IFileSystemQuery>();
            string bkDir = null;

            if (string.IsNullOrWhiteSpace(pathArg))
            {
                List<BackupEntry> all = bk.ListRaw();
                for (int i = all.Count - 1; i >= 0; i--) { if (BackupPackage.IsValidPackage(all[i].Snapshot)) { bkDir = all[i].Path; break; } }
                if (bkDir == null) { Console.WriteLine("DRYRUN_FAIL " + T("无有效备份", "no valid backup")); return 0; }
                // ★ 第 3 轮抓到：**dry-run 的自动分支也没有闸门** ✗ → 会对真实 restore 会拒的包打印 DRYRUN_OK ✗
                //   → 同一个包两套结论 ✓（正是本文件反复强调不能有的那种不一致 ✓）
                {
                    string autoTrunc2 = BackupTruncatedReason(bkDir);
                    if (!string.IsNullOrEmpty(autoTrunc2) && !Has(args, "--force"))
                    {
                        Console.WriteLine("DRYRUN_FAIL " + autoTrunc2 + T("；确认要预览它请加 --force", "; add --force to preview it anyway"));
                        return 0;
                    }
                    if (!string.IsNullOrEmpty(autoTrunc2)) Console.WriteLine("DRYRUN_WARN " + autoTrunc2);
                }
            }
            else
            {
                string p = PathValidator.ResolveBackupPath(pathArg.Trim().Trim('"'), bk.BackupsRoot);   // 裸备份名解析到备份根内 ✓
                if (PathUtil.IsSubPath(bk.BackupsRoot, p))
                {
                    if (!BackupPackage.IsValidPackage(bk.Snapshot(p))) { Console.WriteLine("DRYRUN_FAIL " + T("无效备份目录", "invalid backup directory")); return 0; }
                    bkDir = p;
                    // 完成性闸门 ✓✓：放在**路径校验通过之后** —— 不存在的路径与根外路径都到不了这里 ✓（这正是上一版放错位置的原因 ✗）。
                    // 条件用 !IsNullOrEmpty ✓ 且只在"原因非空"时拦 ✓ → **最多少报，绝不误拦** ✓✓。
                    // ★★ 第 2 轮审查抓到：这道闸门**只在显式 --path 分支** ✗✗（我上一版的注释还声称覆盖了自动分支 ✗）
            //   → 不带 --path 时，中断的包只要是最新的就仍会被恢复并打印 RESTORE_OK ✗
            string truncReason = BackupTruncatedReason(p);
                    if (!string.IsNullOrEmpty(truncReason) && !Has(args, "--force"))
                    {
                        Console.WriteLine("DRYRUN_FAIL " + truncReason + T("；确认要预览它请加 --force", "; add --force to preview it anyway"));
                        OpLog(reg, "WARN", "restore --dry-run refused a truncated backup: " + p);
                        return 0;
                    }
                    if (!string.IsNullOrEmpty(truncReason)) Console.WriteLine("DRYRUN_WARN " + truncReason);
                }
                else
                {
                    string resolved = bk.Resolve(p);
                    if (resolved == null) { Console.WriteLine("DRYRUN_FAIL " + T("不是有效备份包", "not a valid backup package")); return 0; }
                    bkDir = resolved;
                }
            }

            Console.WriteLine("DRYRUN_OK");
            Console.WriteLine("DRYRUN_SRC " + bkDir);
            long tn = 0, to = 0, tk = 0, tb = 0;
            string dst = paths.DataRoot;
            long[] dp = PlanMerge(reg, bkDir, dst, "_workspace", null, false);
            Console.WriteLine("DRYRUN_SCOPE data " + dst);
            PrintPlan(dp);
            tn += dp[0]; to += dp[1]; tk += dp[2]; tb += dp[3];

            string wsSrc = System.IO.Path.Combine(bkDir, "_workspace");
            if (fs.DirectoryExists(wsSrc))
            {
                string[] subs = fs.ListDirectories(wsSrc);
                bool anyNew = false;
                for (int i = 0; i < subs.Length; i++) { if (fs.FileExists(System.IO.Path.Combine(subs[i], ".dshws"))) { anyNew = true; break; } }
                if (anyNew)
                {
                    for (int i = 0; i < subs.Length; i++)
                    {
                        if (!fs.FileExists(System.IO.Path.Combine(subs[i], ".dshws"))) continue;
                        string name = System.IO.Path.GetFileName(subs[i]);
                        string wsRoot = WorkspaceRoot(reg);
                        if (wsRoot == null) { Console.WriteLine("DRYRUN_SCOPE workspace " + name + " skipped (unknown workspace root)"); continue; }
                        string target = System.IO.Path.Combine(wsRoot, name);
                        long[] wp = PlanMerge(reg, subs[i], target, null, ".dshws", false);
                        Console.WriteLine("DRYRUN_SCOPE workspace " + name + " " + target);
                        PrintPlan(wp);
                        tn += wp[0]; to += wp[1]; tk += wp[2]; tb += wp[3];
                    }
                }
                else
                {
                    string wsRoot2 = WorkspaceRoot(reg);
                    string target = wsRoot2 == null ? "" : wsRoot2;
                    if (wsRoot2 == null) Console.WriteLine("DRYRUN_SCOPE workspace-legacy skipped (unknown workspace root)");
                    else Console.WriteLine("DRYRUN_SCOPE workspace-legacy " + target);
                    long[] wp = PlanMerge(reg, wsSrc, target, null, null, true);
                    PrintPlan(wp);
                    tn += wp[0]; to += wp[1]; tk += wp[2]; tb += wp[3];
                }
            }
            Console.WriteLine("DRYRUN_TOTAL " + tn + " " + to + " " + tk + " " + tb);
            Console.WriteLine("DRYRUN_NOTE " + T("合并语义：仅目标端存在的文件不会被删除；交互恢复时每个工作区可自定义目标或跳过。", "Merge semantics: destination-only files are NOT deleted; interactive restore allows per-workspace custom target or skip."));
            return 0;
        }

        /// <summary>v2.x 的 LocateEntryLine：给定文件 → 该文件；给定目录 → 目录下 yml/yaml；都不是 → 整个 profiles 目录。</summary>
        private static Func<string, string, EntryLocation> LocateEntry(ServiceRegistry reg)
        {
            return delegate(string fileOrDir, string entry)
            {
                try
                {
                    if (System.IO.File.Exists(fileOrDir))
                    {
                        int ln = EntryLocator.FindLine(System.IO.File.ReadAllText(fileOrDir, new System.Text.UTF8Encoding(false)), entry);
                        if (ln > 0) return new EntryLocation(fileOrDir, ln);
                        return null;
                    }
                    if (System.IO.Directory.Exists(fileOrDir))
                    {
                        EntryLocation d = ScanDir(fileOrDir, entry);
                        if (d != null) return d;
                        return null;
                    }
                    IProfileSource src = reg.Get<IProfileSource>();
                    ProfileCollection col = src.CollectDirectory(null, true, true);
                    foreach (ProfileFile pf in col.Files)
                    {
                        int ln = EntryLocator.FindLine(pf.Text, entry);
                        if (ln > 0) return new EntryLocation(pf.Path, ln);
                    }
                }
                catch { }
                return null;
            };
        }

        private static EntryLocation ScanDir(string dir, string entry)
        {
            try
            {
                string[] files = System.IO.Directory.GetFiles(dir, "*.yml", System.IO.SearchOption.AllDirectories);
                string[] files2 = System.IO.Directory.GetFiles(dir, "*.yaml", System.IO.SearchOption.AllDirectories);
                for (int pass = 0; pass < 2; pass++)
                {
                    string[] cur = pass == 0 ? files : files2;
                    foreach (string f in cur)
                    {
                        int ln = EntryLocator.FindLine(System.IO.File.ReadAllText(f, new System.Text.UTF8Encoding(false)), entry);
                        if (ln > 0) return new EntryLocation(f, ln);
                    }
                }
            }
            catch { }
            return null;
        }

        private static ToolkitConfig _cfg = new ToolkitConfig();

        /// <summary>ws 规范化（平台侧真实路径校验，供领域层注入）。</summary>
        private static string CanonPath(string p) { return System.IO.Path.GetFullPath(p); }




        /// <summary>组合根：交给 PlatformComposition 按平台装配（单 exe，运行时判定）。</summary>
        private static ServiceRegistry Compose()
        {
            return PlatformComposition.Compose();
        }    }
}
