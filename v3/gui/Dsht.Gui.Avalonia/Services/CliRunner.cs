using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Dsht.Gui.Avalonia.Services
{
    /// <summary>一条 CLI 命令的**结构化结果**（W2 · F-11 / F-12 / F-13）。
    /// <para><b>语义边界（重要）</b>：Ok 只表示「进程真的启动、在预算内跑完、没有抛异常」——
    /// <b>不等于 CLI 退出码 0</b>。本仓库 CLI 有大量失败路径仍然 rc=0（成败判据是标记行），
    /// 而 wipe 的计划视图按设计 rc=1；把 rc 折进 Ok 会让这两类都判错。
    /// 动作级别的成败仍由 ClassifyLevel（标记行 + Ok）判。</para>
    /// <para>只有 Ok=false 的两种形态是「普通输出伪装不了」的失败：
    /// TimedOut=true（超时被杀）与 Started=false（进程起不来）。
    /// 它们带上人类可读的 Text，但 MainWindow 的动作判定必须把它判成 error（F-11）。</para></summary>
    public sealed class CliResult
    {
        /// <summary>命令输出：stdout + stderr（stderr 逐行带 [stderr] 前缀）。</summary>
        public string Text { get; set; }
        /// <summary>进程启动成功、且在超时预算内跑完（✗ 不代表退出码为 0）。</summary>
        public bool Ok { get; set; }
        /// <summary>超时：已 Kill(true) 杀进程树并做了有界等待。</summary>
        public bool TimedOut { get; set; }
        /// <summary>进程是否真的启动（false = 起不来 / 路径错 / 权限不足）。</summary>
        public bool Started { get; set; }
        /// <summary>进程退出码；没启动 = -1。</summary>
        public int ExitCode { get; set; }
        /// <summary>失败原因（人类可读；成功时为 null）。</summary>
        public string Error { get; set; }

        private CliResult() { }

        /// <summary>正常跑完（不论退出码）。</summary>
        public static CliResult Success(string text, int exitCode)
        {
            CliResult r = new CliResult();
            r.Text = text;
            r.Ok = true;
            r.Started = true;
            r.ExitCode = exitCode;
            return r;
        }

        /// <summary>超时（已杀进程树；text 里带着已拿到的部分输出 + 一条超时说明）。</summary>
        public static CliResult Timeout(string text, string error)
        {
            CliResult r = new CliResult();
            r.Text = text;
            r.Ok = false;
            r.TimedOut = true;
            r.Started = true;
            r.ExitCode = -1;
            r.Error = error;
            return r;
        }

        /// <summary>起不来 / 执行中抛异常（进程没跑完）。</summary>
        public static CliResult Failure(string text, string error)
        {
            CliResult r = new CliResult();
            r.Text = text;
            r.Ok = false;
            r.TimedOut = false;
            r.Started = false;
            r.ExitCode = -1;
            r.Error = error;
            return r;
        }
    }

    /// <summary>GUI 调 CLI 的**唯一进程执行实现**（W2 / RUN-02 / F-10 / F-12 / F-13）。
    /// <para>纪律：本文件<b>只引用 System / System.Diagnostics / System.Text</b>（✗ 不许出现 Avalonia 类型 ✗）——
    /// 这样 Dsht.Gui.LogicTests 能把它当纯逻辑源码直接编译、在无图形环境里断言
    /// 「stderr 逐行前缀 / 超时分档 / 失败分类 / 缓存键与容量」（LogicTests 的 csproj 里显式 Include）。</para>
    /// <para>三份旧实现（MainWindow 的 Run / RunQuick / RunStreaming）与两份 stderr 拼装的差异就出在这里：
    /// 旧 Run 只给 stderr 第一行加前缀（F-10）⇒ 多行诊断的第 2 行起是裸行，会被标记行解析器当条目；
    /// 旧三份超时分别 30s/8s/30s 且都只 Kill() 不杀树（F-13）；旧 RunStreaming 硬编码 30 秒。</para></summary>
    public static class CliRunner
    {
        /// <summary>stderr 拼进输出时**每一行**都加 [stderr] 前缀（F-10 的唯一实现）。
        /// 空行跳过（不追加无意义的空行）。返回文本以 CRLF 结尾；没有 stderr → 空串。</summary>
        public static string PrefixStderr(string err)
        {
            if (string.IsNullOrEmpty(err)) return "";
            StringBuilder o = new StringBuilder();
            string[] ls = err.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int i = 0; i < ls.Length; i++)
            {
                if (ls[i].Length == 0) continue;
                o.Append("[stderr] ").Append(ls[i]).Append("\r\n");
            }
            return o.ToString();
        }

        /// <summary>按命令给超时上限（搬运自 MainWindow.TimeoutForMs 的原有语义，2026-10-06 优化 #4）：
        /// 写盘/装包这类长命令 10 分钟封顶，其余 30 秒 —— 挂住的只读命令快速反馈更重要。
        /// 三份运行器现在共用这一份（✗ 改一处必须记得改三处的日子结束了 ✗）。</summary>
        public static int TimeoutForMs(string args)
        {
            if (string.IsNullOrEmpty(args)) return 30000;
            string a = args.TrimStart();
            string[] longCmds = new string[] { "update ", "install ", "uninstall ", "backup ", "backup-export", "backup-delete", "restore ", "import ", "bridge-install" };
            for (int i = 0; i < longCmds.Length; i++) if (a.StartsWith(longCmds[i], StringComparison.Ordinal)) return 600000;
            if (a == "update" || a == "install" || a == "uninstall" || a == "backup" || a == "restore" || a == "import") return 600000;
            return 30000;
        }

        /// <summary>流式命令的超时预算（F-12）：doctor --stream / update-center --stream 是只读、
        /// 但会联网/探测 npm registry 与 GitHub —— 30 秒在网络慢时会**把已经流出的结果杀掉**。
        /// 给它们 120 秒；其余仍按 TimeoutForMs（✗ 不再硬编码 30 秒 ✗）。</summary>
        public static int StreamTimeoutForMs(string args)
        {
            string a = (args ?? "").TrimStart();
            if (a.StartsWith("doctor", StringComparison.Ordinal) || a.StartsWith("update-center", StringComparison.Ordinal)) return 120000;
            return TimeoutForMs(args);
        }

        /// <summary>动作结果的级别（U9：toast 边框色 error=红 / warn=橙 / info=中性）。
        /// ★ F-11：超时 / 起不来（!Ok）一律 error —— 旧实现只看输出里有没有 _FAIL/WARN，
        /// 而超时文案是裸文本（"（超时 N 秒…）"）⇒ 被当成 info 成功色。</summary>
        public static string ClassifyLevel(CliResult result)
        {
            if (result == null || !result.Ok) return "error";
            string text = result.Text ?? "";
            if (text.IndexOf("_FAIL", StringComparison.Ordinal) >= 0) return "error";
            if (text.IndexOf("WARN", StringComparison.Ordinal) >= 0
                || text.IndexOf("未生效", StringComparison.Ordinal) >= 0
                || text.IndexOf("不完整", StringComparison.Ordinal) >= 0
                || text.IndexOf("拒绝", StringComparison.Ordinal) >= 0) return "warn";
            return "info";
        }

        /// <summary>跑一条命令并拿回**结构化结果**（stdout + 逐行前缀后的 stderr）。
        /// 读流在等待前就并行起好（✗ 不先 ReadToEnd 再等 ✗ 防管道写满互等）；超时 → Kill(true) 杀整棵树 + 有界等待（3 秒）。</summary>
        public static CliResult Run(string cli, string args)
        {
            if (string.IsNullOrEmpty(cli)) return CliResult.Failure("未找到工具箱 CLI。", "cli path is empty");
            Process p = null;
            try
            {
                p = Process.Start(NewStartInfo(cli, args));
                if (p == null) return CliResult.Failure("运行 CLI 失败: 进程未启动", "process not started");
                System.Threading.Tasks.Task<string> soT = System.Threading.Tasks.Task.Run(delegate { return ReadAll(p.StandardOutput); });
                System.Threading.Tasks.Task<string> seT = System.Threading.Tasks.Task.Run(delegate { return ReadAll(p.StandardError); });
                int limitMs = TimeoutForMs(args);
                if (!p.WaitForExit(limitMs))
                {
                    KillTree(p);
                    return CliResult.Timeout("（超时 " + (limitMs / 1000) + " 秒，已结束该进程）", "timeout after " + limitMs + " ms");
                }
                StringBuilder sb = new StringBuilder();
                sb.Append(ReadTask(soT));
                string err = ReadTask(seT);
                if (!string.IsNullOrEmpty(err)) sb.Append(PrefixStderr(err));
                return CliResult.Success(sb.Length == 0 ? "（无输出）" : sb.ToString(), p.ExitCode);
            }
            catch (Exception ex)
            {
                return CliResult.Failure("运行 CLI 失败: " + ex.Message, ex.Message);
            }
            finally
            {
                if (p != null) { try { p.Dispose(); } catch { } }
            }
        }

        /// <summary>流式跑一条命令：stdout **每读到一行就回调**（回调发生在**后台线程**，GUI 侧自己包 Dispatcher.Post）。
        /// ★ F-12：超时不再丢弃已经流出的行 —— 先 Kill(true) 杀树，再把「已收行 + 一条超时说明」作为结果返回，
        /// 调用方覆盖 _rawOutput 时不会再把整页结果换成一行超时文案。</summary>
        public static CliResult RunStreaming(string cli, string args, Action<string> onLine)
        {
            if (string.IsNullOrEmpty(cli)) return CliResult.Failure("未找到工具箱 CLI。", "cli path is empty");
            Process p = null;
            try
            {
                p = Process.Start(NewStartInfo(cli, args));
                if (p == null) return CliResult.Failure("运行 CLI 失败: 进程未启动", "process not started");
                StringBuilder sb = new StringBuilder();
                object gate = new object();
                System.Threading.Tasks.Task reader = System.Threading.Tasks.Task.Run(delegate
                {
                    try
                    {
                        string ln;
                        while ((ln = p.StandardOutput.ReadLine()) != null)
                        {
                            lock (gate) { sb.Append(ln).Append("\r\n"); }
                            if (onLine != null) { try { onLine(ln); } catch { } }   // 一行画挂了不许断流
                        }
                    }
                    catch { }
                });
                System.Threading.Tasks.Task<string> seT = System.Threading.Tasks.Task.Run(delegate { return ReadAll(p.StandardError); });
                int limitMs = StreamTimeoutForMs(args);
                if (!p.WaitForExit(limitMs))
                {
                    KillTree(p);
                    try { reader.Wait(5000); } catch { }   // 有界等待读流任务，尽量把已出的行收回来
                    string errT = ReadTask(seT);
                    string textT;
                    lock (gate)
                    {
                        if (!string.IsNullOrEmpty(errT)) sb.Append(PrefixStderr(errT));
                        sb.Append("（超时 " + (limitMs / 1000) + " 秒，已结束该进程）").Append("\r\n");
                        textT = sb.ToString();
                    }
                    return CliResult.Timeout(textT, "stream timeout after " + limitMs + " ms");
                }
                try { reader.Wait(5000); } catch { }
                string err = ReadTask(seT);
                string text;
                lock (gate)
                {
                    if (!string.IsNullOrEmpty(err)) sb.Append(PrefixStderr(err));
                    text = sb.Length == 0 ? "（无输出）" : sb.ToString();
                }
                return CliResult.Success(text, p.ExitCode);
            }
            catch (Exception ex)
            {
                return CliResult.Failure("运行 CLI 失败: " + ex.Message, ex.Message);
            }
            finally
            {
                if (p != null) { try { p.Dispose(); } catch { } }
            }
        }

        /// <summary>只读命令的**短缓存**（F-08）：key = CLI 路径 + "|" + 参数（✗ 旧实现只有 args ⇒ 换 DSHT_CLI 后 4 秒内还显示旧 CLI 的数据 ✗）；
        /// 失败/超时**不入缓存**（✗ 旧实现把超时文案缓存 4 秒 ✗）；容量 ≤128 条（FIFO 淘汰，✗ 旧实现只增不减 ✗）；TTL 4 秒不变。
        /// 纪律沿用：只缓存**只读**命令（写操作后调用方会 Clear）。</summary>
        public static class CliCache
        {
            /// <summary>TTL（秒）—— 与旧实现一致，4 秒。</summary>
            public const double TtlSeconds = 4.0;
            /// <summary>容量上限（条）—— FIFO 淘汰。</summary>
            public const int MaxEntries = 128;

            private sealed class Entry { public string Text; public DateTime AtUtc; }
            private static readonly object Sync = new object();
            private static readonly Dictionary<string, Entry> Map = new Dictionary<string, Entry>(StringComparer.Ordinal);
            private static readonly List<string> Order = new List<string>();

            /// <summary>缓存键：**必须**含 CLI 路径（F-08）。纯函数，便于单测断言。</summary>
            public static string KeyOf(string cli, string args)
            {
                return (cli ?? "") + "|" + (args ?? "");
            }

            /// <summary>当前缓存条数（测试/诊断用）。</summary>
            public static int Count { get { lock (Sync) return Map.Count; } }

            /// <summary>清空缓存（**任何写操作之后都要调**；旧 InvalidateCliCache 的对应实现）。</summary>
            public static void Clear()
            {
                lock (Sync) { Map.Clear(); Order.Clear(); }
            }

            /// <summary>命中（且未过期）→ 直接返回缓存的文本；否则跑 run。
            /// 命令在锁外执行（✗ 三路并行刷新不许被缓存锁串行化 ✗）；**失败/超时不写缓存**。</summary>
            public static CliResult GetOrRun(string cli, string args, Func<CliResult> run)
            {
                if (run == null) return CliResult.Failure("（没有运行器）", "runner is null");
                if (string.IsNullOrEmpty(cli) || string.IsNullOrEmpty(args)) return run();   // 参数不全 ⇒ 不缓存（与旧行为一致）
                string key = KeyOf(cli, args);
                lock (Sync)
                {
                    Entry hit;
                    if (Map.TryGetValue(key, out hit) && (DateTime.UtcNow - hit.AtUtc).TotalSeconds < TtlSeconds)
                        return CliResult.Success(hit.Text, 0);
                }
                CliResult result = run();
                if (result == null) return CliResult.Failure("（运行器没有返回结果）", "runner returned null");
                if (!result.Ok) return result;   // F-08：失败/超时结果不入缓存
                lock (Sync)
                {
                    Map[key] = new Entry { Text = result.Text, AtUtc = DateTime.UtcNow };
                    Order.Remove(key);
                    Order.Add(key);
                    while (Map.Count > MaxEntries && Order.Count > 0)
                    {
                        Map.Remove(Order[0]);
                        Order.RemoveAt(0);
                    }
                }
                return result;
            }
        }

        // —— 内部小工具（编码与防死锁姿势与旧三份实现一致）——

        private static ProcessStartInfo NewStartInfo(string cli, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(cli, args ?? "");
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            return psi;
        }

        /// <summary>读干一个流（读失败当作空串 ✓ 不抛进上层）。</summary>
        private static string ReadAll(System.IO.TextReader reader)
        {
            try { return reader == null ? "" : reader.ReadToEnd(); } catch { return ""; }
        }

        /// <summary>有界等待读流任务（超时/被杀时它可能还在读）；拿不到就返回空串。</summary>
        private static string ReadTask(System.Threading.Tasks.Task<string> t)
        {
            try { if (t != null && t.Wait(5000)) return t.Result; } catch { }
            return "";
        }

        /// <summary>杀整棵进程树（F-13：CLI 会派生 npm/node 子进程，只 Kill 父进程会留下"看不见还在跑"的残留）。</summary>
        private static void KillTree(Process p)
        {
            if (p == null) return;
            try { p.Kill(true); } catch { try { p.Kill(); } catch { } }
            try { p.WaitForExit(3000); } catch { }   // 有界等待：杀掉后最多再等 3 秒
        }
    }
}
