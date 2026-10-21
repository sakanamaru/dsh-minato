using System;
using System.IO;
using Dsht.Domain.Abstractions;

namespace Dsht.Platform.Linux
{
    /// <summary>工具链版本查询（Linux）：直接执行 node/npm/dsh（无 cmd.exe 包装）。
    /// WhichDsh = 扫 PATH 找可执行文件 dsh；NpmRegistryConfig = npm config get registry。</summary>
    public sealed class LinuxToolchainQuery : IToolchainQuery
    {
        public string NodeVersion() { return Capture(NodeExe(), "--version"); }

        public string NpmVersion() { return CaptureNpm("--version"); }

        /// <summary>dsh 版本：**不能用 `dsh --version`** ✗ —— 真机实测它对 --version/-v/version 全部零输出，
        /// 于是"是否已安装"判断永远为假 ✗。改用 `npm ls -g` 的输出解析（可靠 ✓），失败再退回 PATH 查找。</summary>
        public string DshVersion()
        {
            string raw = CaptureNpm("ls -g @deepseek-ai/dsh --depth=0 --registry https://registry.npmmirror.com");   // 本地查询也带上镜像，避免联网卡住 ✗
            if (!string.IsNullOrEmpty(raw))
            {
                int at = raw.LastIndexOf("@deepseek-ai/dsh@", StringComparison.Ordinal);
                if (at >= 0)
                {
                    string rest = raw.Substring(at + "@deepseek-ai/dsh@".Length).Trim();
                    int cut = rest.IndexOfAny(new char[] { ' ', '\n', '\r', '\t' });
                    if (cut > 0) rest = rest.Substring(0, cut);
                    if (rest.Length > 0) return rest;
                }
            }
            // 兜底：文件在就算装了，版本未知（不谎报版本 ✓）
            string w = WhichDsh();
            return string.IsNullOrEmpty(w) ? "" : "unknown";
        }

        public string WhichDsh()
        {
            // Check known install locations first: the no-sudo bootstrap puts the global bin in
            // ~/.local/node/bin, which is not on this process PATH, so PATH alone lies.
            string[] known = NodeBinDirs();
            for (int i = 0; i < known.Length; i++)
            {
                try { string kf = System.IO.Path.Combine(known[i], "dsh"); if (System.IO.File.Exists(kf)) return kf; } catch { }
            }
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string d in path.Split(':'))
                {
                    string dir = d.Trim();
                    if (dir.Length == 0) continue;
                    try
                    {
                        string f = Path.Combine(dir, "dsh");
                        if (File.Exists(f)) return f;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        public string NpmRegistryConfig() { return CaptureNpm("config get registry"); }

        /// <summary>全局安装（Linux 直接执行 npm；registry 为空则用默认源）。返回退出码，-1 = 未能执行。</summary>
        /// <summary>全局安装：先用配置的 registry；失败且不是镜像源时**自动回退到 npmmirror** ✓（国内网络现实）。
        /// 全程把 npm 的输出留在 LastError 里，失败时 CLI 会如实展示（之前只报退出码，无法诊断 ✗）。</summary>
        public int NpmInstallGlobal(string pkg, string registry)
        {
            int code = RunNpm("install -g " + (string.IsNullOrEmpty(registry) ? "" : "--registry " + registry + " ") + pkg, out LastError);
            if (code == 0) return 0;
            if (string.IsNullOrEmpty(registry) || registry.IndexOf("npmmirror", StringComparison.OrdinalIgnoreCase) < 0)
            {
                string mirror = "https://registry.npmmirror.com";
                string second;
                int code2 = RunNpm("install -g --registry " + mirror + " " + pkg, out second);
                LastError = LastError + "\n--- 回退 " + mirror + " ---\n" + second;
                if (code2 == 0) return 0;
                return code2;
            }
            return code;
        }


        /// <summary>全局卸载 dsh。返回退出码，-1 = 未能执行。</summary>
        public int NpmUninstallGlobal() { return RunExit(NpmExe(), "uninstall -g @deepseek-ai/dsh"); }

        /// <summary>跑一个命令并返回退出码（-1 = 未能执行/超时）。</summary>
        private static int RunExit(string file, string args)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(file, args);
                InjectPath(psi);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    System.Threading.Tasks.Task<string> so = System.Threading.Tasks.Task.Run(delegate { return p.StandardOutput.ReadToEnd(); });
                    System.Threading.Tasks.Task<string> se = System.Threading.Tasks.Task.Run(delegate { return p.StandardError.ReadToEnd(); });
                    if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } return -1; }
                    System.Threading.Tasks.Task.WaitAll(so, se);
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
        /// <summary>免 sudo 引导 Node：下载官方 LTS 预编译包解到 ~/.local/node（一次性，之后 npm/dsh 都在里面）。
        /// 版本固定为常量，便于审计与复现；失败一律返回 -1，由调用方如实报告。</summary>
        /// <summary>npm 可执行文件：优先用免 sudo 引导到 ~/.local/node 的那个（非交互 PATH 里没有 ✗），否则交给 PATH。</summary>
        /// <summary>node 可执行文件：优先用免 sudo 引导到 ~/.local/node 的那个（非交互 PATH 里没有 ✗），否则交给 PATH。</summary>
        /// <summary>把免 sudo 引导出来的目录前置到子进程 PATH。
        /// 为什么必须这么做：官方 Node 包里的 `npm` 是符号链接到 npm-cli.js，其 shebang 是
        /// `#!/usr/bin/env node` ✗ —— 只要 node 不在 PATH 上，npm 就报 "env: 'node': No such file" ✗。
        /// 注入后 node / npm / dsh 一次全部可用 ✓（npm 的全局 bin 就是 ~/.local/node/bin ✓）。</summary>
        private static void InjectPath(System.Diagnostics.ProcessStartInfo psi)
        {
            string home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home)) return;
            string prefix = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(home, ".local"), "node"), "bin")
                          + ":" + System.IO.Path.Combine(System.IO.Path.Combine(home, ".npm-global"), "bin")
                          + ":" + System.IO.Path.Combine(home, ".local/bin");
            try
            {
                string cur = psi.EnvironmentVariables["PATH"];
                psi.EnvironmentVariables["PATH"] = prefix + (string.IsNullOrEmpty(cur) ? "" : ":" + cur);
            }
            catch { }
        }
        private static string NodeExe()
        {
            string home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                string p = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(home, ".local"), "node"), "bin");
                p = System.IO.Path.Combine(p, "node");
                if (System.IO.File.Exists(p)) return p;
            }
            return "node";
        }
        private static string NpmExe()
        {
            string home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                string p = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(home, ".local"), "node"), "bin");
                p = System.IO.Path.Combine(p, "npm");
                if (System.IO.File.Exists(p)) return p;
                string cli = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(home, ".local"), "node"), "lib"), "node_modules/npm/bin/npm-cli.js");
                if (System.IO.File.Exists(cli)) return cli;   // 兜底：直接调 npm-cli.js（需配合 node）
            }
            return "npm";
        }
        /// <summary>最近一次引导失败的原因（供 CLI 如实展示）。</summary>
        public static string LastError = "";

        /// <summary>跑命令并**带回输出**（诊断用；之前只返回退出码，出错时看不到原因 ✗）。</summary>
        private static int RunExitCapture(string file, string args, out string output)
        {
            output = "";
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(file, args);
                InjectPath(psi);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    System.Threading.Tasks.Task<string> so = System.Threading.Tasks.Task.Run(delegate { return p.StandardOutput.ReadToEnd(); });
                    System.Threading.Tasks.Task<string> se = System.Threading.Tasks.Task.Run(delegate { return p.StandardError.ReadToEnd(); });
                    if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } output = "（超时）"; return -1; }
                    System.Threading.Tasks.Task.WaitAll(so, se);
                    string o = (so.Result ?? "") + (se.Result ?? "");
                    output = o.Length > 1200 ? o.Substring(o.Length - 1200) : o;
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { output = ex.Message; return -1; }
        }
        /// <summary>npm-cli.js 的绝对路径（官方 Node 包内）。用它配合 NodeExe() 调用，**绕开 npm 垫片的
        /// `#!/usr/bin/env node` shebang** ✗ —— 真机上正是它导致 npm 在子进程里报 "env: 'node': No such file" ✗。</summary>
        private static string NpmCliJs()
        {
            string home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                string p = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(home, ".local"), "node"), "lib"), "node_modules/npm/bin/npm-cli.js");
                if (System.IO.File.Exists(p)) return p;
            }
            return "";
        }

        /// <summary>跑 npm 并取输出：优先 `<node> <npm-cli.js> args` ✓；没有引导版 Node 时才退回 PATH 上的 npm。</summary>
        private static string CaptureNpm(string args)
        {
            string cli = NpmCliJs();
            if (cli.Length > 0) return Capture2(NodeExe(), cli + " " + args);
            return Capture(NpmExe(), args);
        }

        /// <summary>跑 npm 并取退出码与输出（同上策略 ✓）。</summary>
        private static int RunNpm(string args, out string output)
        {
            string cli = NpmCliJs();
            if (cli.Length > 0) return RunExitCapture(NodeExe(), cli + " " + args, out output);
            return RunExitCapture(NpmExe(), args, out output);
        }
        /// <summary>Node archive architecture suffix (RuntimeInformation works on both build paths).</summary>
        private static string NodeArch()
        {
            try
            {
                System.Runtime.InteropServices.Architecture a = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
                if (a == System.Runtime.InteropServices.Architecture.Arm64) return "arm64";
                if (a == System.Runtime.InteropServices.Architecture.Arm) return "armv7l";
                if (a == System.Runtime.InteropServices.Architecture.X86) return "x86";
                return "x64";
            }
            catch { return "x64"; }
        }

        public int InstallNodeRuntime()
        {
            // dsh 要求 Node >= 22.19.0（真机 npm warn EBADENGINE 抓到的 ✗）→ 按候选列表逐个试 ✓
            string[] versions = new string[] { "v24.10.0", "v22.20.0", "v22.19.0" };
            string home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home)) return -1;
            string dir = System.IO.Path.Combine(System.IO.Path.Combine(home, ".local"), "node");
            string tar = System.IO.Path.Combine(dir, "node.tar.xz");
            try
            {
                // **先清空目标目录再解包** ✗：就地覆盖升级会把 npm 搞坏（真机报 "Class extends value undefined" ✗）。
                // 代价是全局包（含 dsh）需要重装 —— 紧接着的 npm install 会补上 ✓。
                if (System.IO.Directory.Exists(dir))
                {
                    try { System.IO.Directory.Delete(dir, true); }
                    catch (Exception dex)
                    {
                        // **删不掉就不能继续** ✗：覆盖解包会把 npm 弄坏（真机报 "Class extends value undefined" ✗）
                        LastError = "无法清空 " + dir + "（可能有进程在用）：" + dex.Message;
                        return -1;
                    }
                    if (System.IO.Directory.Exists(dir)) { LastError = "清空 " + dir + " 后目录仍存在"; return -1; }
                }
                System.IO.Directory.CreateDirectory(dir);
                for (int vi = 0; vi < versions.Length; vi++)
                {
                string ver = versions[vi];
                string url = "https://nodejs.org/dist/" + ver + "/node-" + ver + "-linux-" + NodeArch() + ".tar.xz";
                // 三级兜底：curl → wget → python3（真机上 curl 常常没装 ✗）
                if (RunExit("curl", "-fsSL --max-time 600 " + url + " -o " + tar) != 0
                    && RunExit("wget", "-q --timeout=60 -O " + tar + " " + url) != 0
                    && RunExit("python3", "-c \"import urllib.request,sys; urllib.request.urlretrieve(sys.argv[1], sys.argv[2])\" " + url + " " + tar) != 0)
                {
                    LastError = "没有可用的下载方式（curl/wget/python3 都失败或不存在）—— 可先 sudo apt install -y curl";
                    return -1;
                }
                if (!System.IO.File.Exists(tar) || new System.IO.FileInfo(tar).Length < 1000000)
                {
                    LastError = "下载的文件不完整（" + ver + "）";
                    continue;   // 换下一个候选版本 ✓
                // ★★★ 架构审计抓到（安全 MAJOR）：下载的 Node tarball **没有任何校验** ✗✗
                //   → 解压后**放进 PATH** ✓ 之后每次 node/npm/dsh 都在跑它 ✗ → 被换掉的包就是**任意代码执行** ✗
                //   → 而项目自己的发布物是**用 SHA-256 校验**的 ✓（install.sh / verify-linux.sh ✓）唯独这里漏了 ✓
                // ✓ 现在：**必须与官方 SHASUMS256.txt 一致** ✓✓ 否则**拒绝解压** ✓（fail-closed ✓）
                string tarName = System.IO.Path.GetFileName(tar);
                string tarDir = System.IO.Path.GetDirectoryName(tar);
                string wantSha = FetchExpectedSha(ver, tarName, tarDir);
                string gotSha = Sha256File(tar);
                if (wantSha == null || gotSha == null || !string.Equals(wantSha, gotSha, StringComparison.OrdinalIgnoreCase))
                {
                    LastError = "Node " + ver + " 哈希校验未通过（拿不到官方 SHASUMS256.txt 或对不上）—— 已拒绝解压";
                    try { System.IO.File.Delete(tar); } catch { }
                    continue;   // 换下一个候选版本 ✓
                }
                }
                try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); } catch (Exception dex) { LastError = "cannot clear " + dir + ": " + dex.Message; return -1; }
                System.IO.Directory.CreateDirectory(dir);
                if (RunExit("tar", "-xf " + tar + " --strip-components=1 -C " + dir) != 0) return -1;
                try { System.IO.File.Delete(tar); } catch { }
                LastError = "";
                return 0;
                }
                if (string.IsNullOrEmpty(LastError)) LastError = "所有候选版本都下载失败";
                return -1;
            }
            catch { return -1; }
        }

        /// <summary>下载并解析 SHASUMS256.txt 里某个文件的期望哈希 ✓；**拿不到就返回 null** ✓（调用方按"不能校验"处理 ✓ 不猜 ✓）。</summary>
        private static string FetchExpectedSha(string ver, string fileName, string tmpDir)
        {
            string sumsUrl = "https://nodejs.org/dist/" + ver + "/SHASUMS256.txt";
            string sums = System.IO.Path.Combine(string.IsNullOrEmpty(tmpDir) ? System.IO.Path.GetTempPath() : tmpDir, "SHASUMS256-" + ver + ".txt");
            try { System.IO.File.Delete(sums); } catch { }
            if (RunExit("curl", "-fsSL --max-time 120 " + sumsUrl + " -o " + sums) != 0
                && RunExit("wget", "-q --timeout=60 -O " + sums + " " + sumsUrl) != 0
                && RunExit("python3", "-c \"import urllib.request,sys; urllib.request.urlretrieve(sys.argv[1], sys.argv[2])\" " + sumsUrl + " " + sums) != 0)
                return null;
            try
            {
                if (!System.IO.File.Exists(sums)) return null;
                string[] ls = System.IO.File.ReadAllLines(sums);
                for (int i = 0; i < ls.Length; i++)
                {
                    string t = ls[i] == null ? "" : ls[i].Trim();
                    if (t.Length == 0) continue;
                    int sp = t.IndexOf(' ');
                    if (sp <= 0) continue;
                    string h = t.Substring(0, sp).Trim();
                    string nm = t.Substring(sp + 1).Trim().TrimStart('*');
                    if (nm.EndsWith(fileName, StringComparison.Ordinal) && h.Length == 64) return h.ToLowerInvariant();
                }
            }
            catch { }
            finally { try { System.IO.File.Delete(sums); } catch { } }
            return null;
        }

        /// <summary>算文件 SHA-256（小写 hex）✓；**失败返回 null** ✓（调用方按"不能校验"处理 ✓）。</summary>
        private static string Sha256File(string path)
        {
            try
            {
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                using (System.IO.FileStream fs = System.IO.File.OpenRead(path))
                {
                    byte[] h = sha.ComputeHash(fs);
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return null; }
        }

        /// <summary>Node 运行时的 bin 目录候选（免 sudo 引导后 dsh 就在这里，非交互 PATH 里通常没有 ✗）。</summary>
        private static string[] NodeBinDirs()
        {
            string home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home)) return new string[0];
            return new string[]
            {
                System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(home, ".local"), "node"), "bin"),
                System.IO.Path.Combine(System.IO.Path.Combine(home, ".npm-global"), "bin"),
                System.IO.Path.Combine(home, ".local/bin"),
                "/usr/local/bin", "/usr/bin"
            };
        }

        /// <summary>查最新版本：先按配置的 registry，失败再回退 npmmirror（与安装对称 ✓ —— 真机上出现过"引导完 Node 后查不到版本" ✗）。</summary>
        /// <summary>列出可用版本（原样返回 npm 输出 ✓；失败回退镜像 ✓）。</summary>
        public string NpmViewVersions()
        {
            string v = CaptureNpm("view @deepseek-ai/dsh versions");
            if (!string.IsNullOrEmpty(v)) return v;
            return CaptureNpm("view @deepseek-ai/dsh versions --registry https://registry.npmmirror.com");
        }

        public string NpmViewLatest()
        {
            // 只取"最后一行"：npm 会把 EBADENGINE/deprecated 等警告混进输出 ✗，
            // 整段拿去白名单会被拒 → 表现为"拿不到可信版本"（真机抓到的 ✗）
            // 先走镜像 ✓（国内实测 26 秒 vs npmjs 2 分钟 ✗），再退回默认源
            string v = LastVersionLine(CaptureNpm("view @deepseek-ai/dsh version --registry https://registry.npmmirror.com"));
            if (v.Length > 0) return v;
            return LastVersionLine(CaptureNpm("view @deepseek-ai/dsh version"));
        }

        /// <summary>从 npm 输出里取最后一行"看起来像版本号"的内容（去掉警告噪音 ✓）。</summary>
        private static string LastVersionLine(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string s = lines[i].Trim();
                if (s.Length == 0) continue;
                if (s.StartsWith("npm ", StringComparison.OrdinalIgnoreCase)) continue;
                if (s.IndexOf(' ') >= 0) continue;
                return s;
            }
            return "";
        }

        private static string Capture2(string exe, string args) { return Capture(exe, args); }

        private static string Capture(string exe, string args)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(exe, args);
                InjectPath(psi);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    if (p == null) return "";
                    System.Threading.Tasks.Task<string> soT2 = System.Threading.Tasks.Task.Run(delegate { return p.StandardOutput.ReadToEnd(); });
                    System.Threading.Tasks.Task<string> seT2 = System.Threading.Tasks.Task.Run(delegate { return p.StandardError.ReadToEnd(); });
                    string outp = soT2.Result;
                    seT2.Wait(5000);
                    p.WaitForExit(120000);
                    return outp;
                }
            }
            catch { return ""; }
        }
    }
}
