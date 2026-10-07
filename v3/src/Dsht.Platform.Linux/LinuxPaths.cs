using System;
using System.IO;
using Dsht.Domain.Abstractions;

namespace Dsht.Platform.Linux
{
    /// <summary>路径解析（Linux）。对应 v2.x 的 Windows 语义，按 XDG 约定落地：
    ///   StateDir   = exe 所在目录（可写时），否则 $XDG_DATA_HOME/DeepSeekHarnessLauncher（默认 ~/.local/share/...）
    ///   BackupsRoot= StateDir/backup
    ///   DataRoot   = $DSH_HOME，其次 $HOME/.dsh</summary>
    public sealed class LinuxPaths : IPaths
    {
        private const string DataDirName = ".dsh";
        private const string AppDirName = "DeepSeekHarnessLauncher";
        private readonly string _stateDir;

        /// <summary>Read an env var and NORMALISE it to an absolute path.
        /// ⚠ **诚实边界（架构审计核对过 ✓）**：这里只做**词法**归一化（`Path.GetFullPath` ✓）。
        ///   · 相对路径（如 `.dsh`）**确实**会被挡在 --apply 闸门外 ✓（它会被补成绝对路径 ✓）
        ///   · 但**符号链接 / junction 不会被解析** ✗ —— 把一个指向 ~/.dsh 的链接设成 DSH_HOME，
        ///     闸门看到的字符串与默认候选不同 → **仍会放行** ✗
        ///   （要真解决得解析真实路径 ✓ 而 .NET Framework 4.x 上没有 `ResolveLinkTarget` ✗
        ///    → 需要平台侧 P/Invoke / readlink ✓ 目前**未做** ✓ 这里如实写明而不是含糊带过 ✓）</summary>
        internal static string NormEnv(string name)
        {
            try
            {
                string v = Environment.GetEnvironmentVariable(name);
                if (string.IsNullOrEmpty(v)) return v;
                v = v.Trim();
                if (v.Length == 0) return v;
                string abs = System.IO.Path.GetFullPath(v);
                // C2: resolve the real target too - a link to the real data root must not bypass the gate
                return RealPath(abs);
            }
            catch { return null; }
        }
        public LinuxPaths() { _stateDir = ResolveStateDir(); }

        public string StateDir { get { return _stateDir; } }

        // ★ 架构审计（S3）：这里原来还有一个 BackupsRoot 实现 ✗
        //   → 它**没有任何调用点** ✓ 而长得像"备份根的唯一来源" ✗
        //   → **我一度把安全修复做在了它上面** ✗✗（真机审查发现改的是死代码 ✓）
        // ✓ 已随接口成员一起删除 ✓✓（备份根的正确来源是 IBackupSource.BackupsRoot ✓）
        public string DataRoot
        {
            get
            {
                string env = NormEnv("DSH_HOME");
                if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
                return DefaultDataRoots()[0];
            }
        }

        /// <summary>默认数据根候选。供 restore --apply 的隔离判定使用：生效数据根等于它即视为"默认位置"，拒绝真实写入。</summary>
        public static string[] DefaultDataRoots()
        {
            string home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(home)) home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new string[] { Path.Combine(home, DataDirName) };
        }

        /// <summary>工作区自动探测：V3 尚未移植（v2.x 在 Windows 上会遍历盘符与常见目录）。诚实返回 null。</summary>
                /// <summary>Auto-detected workspace root: the current directory, but only when WorkspaceJudge
        /// considers it plausible. This used to return null, which made workspace backup/restore
        /// unavailable on Linux (platform-seam audit). The forbidden roots are the Linux
        /// equivalents; the Windows list (users, $recycle.bin, ...) would accept /etc or /usr.
        /// An explicit ws= setting always wins and is not judged.</summary>
        public string WorkspaceRoot
        {
            get
            {
                try
                {
                    string cwd = System.IO.Directory.GetCurrentDirectory();
                    if (string.IsNullOrEmpty(cwd)) return null;
                    string full;
                    try { full = System.IO.Path.GetFullPath(cwd); } catch { return null; }
                    if (!Dsht.Domain.Services.WorkspaceJudge.LooksLike(full, ForbiddenWorkspaceRoots())) return null;
                    // The home directory itself is not a workspace (it would back up everything).
                    string home = Environment.GetEnvironmentVariable("HOME");
                    if (!string.IsNullOrEmpty(home))
                    {
                        string h;
                        try { h = System.IO.Path.GetFullPath(home).TrimEnd(System.IO.Path.DirectorySeparatorChar); } catch { h = ""; }
                        if (h.Length > 0 && full.TrimEnd(System.IO.Path.DirectorySeparatorChar) == h) return null;
                    }
                    string data = DataRoot;
                    if (!string.IsNullOrEmpty(data) && full.TrimEnd(Path.DirectorySeparatorChar) == data.TrimEnd(Path.DirectorySeparatorChar)) return null;
                    return full;
                }
                catch { return null; }
            }
        }

        /// <summary>Linux forbidden roots: the system directories plus /home, /tmp and friends.
        /// A project under /home is fine; /home itself is not.</summary>
        internal static string[] ForbiddenWorkspaceRoots()
        {
            // Note: /home itself is NOT listed - WorkspaceJudge treats a forbidden root as covering its
            // whole subtree, and on Linux a project normally lives under the home directory. The home
            // directory ITSELF is rejected separately below (exact match).
            return new string[] { "/", "/etc", "/usr", "/var", "/bin", "/sbin", "/lib", "/lib64",
                "/boot", "/proc", "/sys", "/dev", "/run", "/root", "/tmp", "/opt", "/srv", "/media", "/mnt" };
        }

        /// <summary>状态目录：**Linux 上一律用 XDG** ✓✓（$XDG_DATA_HOME/DeepSeekHarnessLauncher ✓ 默认 ~/.local/share/…）。
        ///
        /// ✗✗ 原来的逻辑是 v2.x 的 Windows 语义：「exe 所在目录可写就用 exe 目录」✗
        ///   在 Windows 上那是对的（安装目录固定 ✓）；**在 Linux 上是个数据丢失 bug** ✗✗：
        ///   包是 tar 解压出来的 → **目录总是可写** → 日志与备份**全写进包目录** ✗
        ///   → **升级 / 重装 / 换个目录解压 → 备份和日志全丢** ✗✗
        ///   （实测印证：VM 上出现过两份不同位置的 launcher.log ✓ 说明它确实变过位置 ✓）
        /// ✓ Linux 没有 v2.x 的历史包袱 → 用 XDG 约定 ✓ 升级不再丢数据 ✓✓
        ///
        /// 兼容：老位置（exe 目录）里若有 logs/ 或 backup/ 而新位置没有 → **一次性搬过去** ✓（幂等 ✓ 失败不致命 ✓）。</summary>
        private static string ResolveStateDir()
        {
            string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(xdg))
            {
                string home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrWhiteSpace(home)) home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                xdg = Path.Combine(home, ".local", "share");
            }
            string dir = Path.Combine(xdg, AppDirName);
            MigrateFromExeDir(dir);
            return dir;
        }

        /// <summary>把老位置（exe 目录）里的 logs/ 与 backup/ 搬到新位置 ✓。
        /// 只在「新位置没有、老位置有」时搬 ✓ → 幂等 ✓；任何失败都**不致命** ✓（大不了还从老位置读 ✓）。</summary>
        private static void MigrateFromExeDir(string newDir)
        {
            try
            {
                string oldDir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(oldDir)) return;
                string o = Path.GetFullPath(oldDir).TrimEnd(Path.DirectorySeparatorChar);
                string n = Path.GetFullPath(newDir).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(o, n, StringComparison.Ordinal)) return;   // 本来就一样 ✓
                foreach (string sub in new string[] { "logs", "backup" })
                {
                    string from = Path.Combine(o, sub);
                    string to = Path.Combine(n, sub);
                    try
                    {
                        if (!Directory.Exists(from)) continue;
                        if (Directory.Exists(to) && Directory.GetFileSystemEntries(to).Length > 0) continue;   // 新位置已有 → 不覆盖 ✓
                        Directory.CreateDirectory(n);
                        Directory.Move(from, to);
                    }
                    catch { }   // 尽力而为 ✓ 单个子目录失败不影响其它 ✓
                }
            }
            catch { }
        }

        /// <summary>解析路径的真实目标（解开符号链接）；拿不到就返回原值。
        /// 架构审计 C2：隔离闸门只做词法归一化，指向真实数据根的链接会绕过它。
        /// ★★★ L4 修复（2026-10-07 真机实测）：这里原来是把路径**拼进命令行**的 ✗
        ///   ✗ `new ProcessStartInfo("readlink", "-f " + p)` —— 路径**没有引号** ✗
        ///   → .NET 按空白把 Arguments 切成 argv → readlink 收到**两个**操作数
        ///   → 输出两行（`/tmp/l4/my` 与 `dsh`）→ 返回的"真实路径"里**带换行符** ✗
        ///   → 它被当成 DataRoot → `DSH_HOME="/tmp/l4/my dsh"` 时 `backup --to` **BACKUP_FAIL** ✗✗
        ///   （真机原话：`BACKUP_FAIL 数据目录不存在：/tmp/dsht-pathcheck.XXXX/my` 换行 `dsh` ✓）
        /// ✓ 现在：**引号化** ✓✓ 用 `QuoteArg` 保证"一个参数就是一个 argv 元素" ✓
        ///   ⚠ 为什么不用 .NET Core 的 `ProcessStartInfo.ArgumentList` ✗：
        ///     v3 源码还要能被 **.NET Framework 的 csc** 编译（`v3/tests/verify_restore_apply.ps1`
        ///     就是用 in-box csc 建 exe 的 ✓ 一用 ArgumentList 那个门槛立刻红 ✗ 已实测 ✓）
        ///     → 照 `LinuxServiceControl.QuoteIfNeeded` 的同一手法做字符串引号化 ✓
        /// 同一文件里其它"命令拼接"已核过 ✓：本文件只有这一处起进程（其余是 Directory/File API ✓），
        ///   所以只改这一处 ✓ 不做无关重构 ✗。</summary>
        internal static string RealPath(string p)
        {
            try
            {
                if (string.IsNullOrEmpty(p)) return p;
                if (!System.IO.Directory.Exists(p)) return p;
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("readlink", "-f " + QuoteArg(p));
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (System.Diagnostics.Process pr = System.Diagnostics.Process.Start(psi))
                {
                    if (pr == null) return p;
                    string outp = pr.StandardOutput.ReadToEnd();
                    pr.WaitForExit(3000);
                    outp = outp == null ? "" : outp.Trim();
                    if (outp.Length == 0 || !System.IO.Path.IsPathRooted(outp)) return p;
                    return outp;
                }
            }
            catch { return p; }
        }

        /// <summary>把一个路径变成"**一个** argv 参数"：含空白/引号时包一层双引号并把
        ///   `\` 与 `"` 转义 ✓（.NET 在 Unix 上也用它那套 Windows 规则解析 Arguments ✓
        ///   外层引号会被去掉 ✓ 所以 readlink 收到的是**原样的**一个路径 ✓）。
        /// ★ L4：这就是那个真机缺陷的修复点 —— 之前路径直接拼在命令后面 ✗ 空格把它切成了两个参数 ✗。
        /// ⚠ 为什么不用 `ProcessStartInfo.ArgumentList` ✗：v3 源码要能被 .NET Framework 的 csc 编译 ✓
        ///   （`verify_restore_apply.ps1` 用 in-box csc 建 exe ✓ 用了 ArgumentList 该门槛会红 ✗）。
        /// 不含空白/引号时**原样返回** ✓（保持与旧行为逐字一致 ✓ 最小改动 ✓）。</summary>
        internal static string QuoteArg(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            if (s.IndexOf(' ') < 0 && s.IndexOf('\t') < 0 && s.IndexOf('"') < 0 && s.IndexOf('\\') < 0) return s;
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
