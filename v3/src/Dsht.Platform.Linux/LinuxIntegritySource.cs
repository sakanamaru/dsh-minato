using System;
using System.IO;
using System.Reflection;
using System.Text;
using Dsht.Domain.Abstractions;

namespace Dsht.Platform.Linux
{
    /// <summary>自身完整性数据源（Linux）：与 Windows 同语义（自身 exe 的 SHA-256 + 同目录 hashes.txt）。</summary>
    public sealed class LinuxIntegritySource : IIntegritySource
    {
        public string SelfPath()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc) && File.Exists(loc)) return loc;
                // ✗✗ 单文件发布（.NET 5+）时 Location 是**空的** ✗
                //    → 实测：SelfHash/SelfFileName/ReadManifest **全为 null** → Judge 得 Unknown → 整条自检"跳过" ✓✓
                //    （与版本号那次是**同一个坑** ✓ 单文件下要用**进程主模块路径** ✓）
                try
                {
                    System.Diagnostics.Process proc = System.Diagnostics.Process.GetCurrentProcess();
                    if (proc != null && proc.MainModule != null && !string.IsNullOrEmpty(proc.MainModule.FileName))
                        return proc.MainModule.FileName;
                }
                catch { }
                return "";
            }
            catch { return ""; }
        }

        public string SelfFileName()
        {
            try { return Path.GetFileName(SelfPath()); } catch { return ""; }
        }

        /// <summary>自身哈希（小写 hex）✓。
        /// ★★★ **第 3 轮审查抓到的性能大头** ✓✓
        ///   ✗ 正式包里 `hashes.txt` **总是**列着本 exe ✗ → 每次调用都要**整份读 66 MB 再算 SHA-256** ✗✗
        ///     而 GUI 一次刷新要起 **5–6 个** CLI 进程 ✓ → 每次按钮 0.3–0.7 GB 无谓磁盘 IO ✓
        ///   ✓ 现在：**按 (路径, 大小, 修改时间) 做跨进程缓存** ✓✓
        ///     · 命中 → 直接返回上次算出的哈希 ✓（不读文件 ✓）
        ///     · 未命中 → 老实算一遍并写入缓存 ✓
        ///     · **任何异常都不影响正确性** ✓（缓存只是加速 ✓ 读不到/写不进就退化成原来的行为 ✓）
        ///   ⚠ **诚实边界**：缓存键含大小与 mtime ✓ 所以文件一变就会重算 ✓；
        ///     但**能改写安装目录的人本来也能改写 hashes.txt** ✓ —— 这道闸门防的是"意外损坏/半截下载" ✓
        ///     不是防有写权限的攻击者 ✓（缓存不改变这个事实 ✓ 这里如实写明 ✓）。
        /// </summary>
        public string SelfHash()
        {
            try
            {
                string path = SelfPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

                string key = null;
                string cacheFile = null;
                try
                {
                    System.IO.FileInfo fi = new System.IO.FileInfo(path);
                    key = fi.FullName + "|" + fi.Length.ToString() + "|" + fi.LastWriteTimeUtc.Ticks.ToString();
                    string cacheDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-minato-selfhash");
                    System.IO.Directory.CreateDirectory(cacheDir);
                    // ★★★ 备份链审查抓到（2026-10-07，D7）：缓存文件名原来只用 **长度-时间戳** ✗✗
                    //   → **路径根本没进文件名** ✗ → /tmp 是所有进程共享的 ✓ → 可预知、可**预置投毒** ✗✗
                    // ✓ 现在：文件名 = **SHA256(完整 key)**（含路径 ✓）→ 内容寻址 ✓✓
                    cacheFile = System.IO.Path.Combine(cacheDir, Sha256Hex(key) + ".txt");
                    if (System.IO.File.Exists(cacheFile))
                    {
                        string cached = System.IO.File.ReadAllText(cacheFile).Trim();
                        if (cached.Length == 64 && cached.IndexOf('|') < 0) return cached;
                    }
                }
                catch { }

                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] h = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in h) sb.Append(b.ToString("x2"));
                    string hex = sb.ToString();
                    try { if (cacheFile != null) System.IO.File.WriteAllText(cacheFile, hex); } catch { }
                    return hex;
                }
            }
            catch { return null; }
        }

        /// <summary>字符串的 SHA256（小写 hex）✓ —— 用于把自哈希缓存的 key 变成**定长、文件系统安全**的名字 ✓（D7 ✓）。</summary>
        private static string Sha256Hex(string s)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public string ReadManifest()
        {
            try
            {
                string exe = SelfPath();
                if (string.IsNullOrEmpty(exe)) return null;
                string dir = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(dir)) return null;
                string manifest = Path.Combine(dir, "hashes.txt");
                if (!File.Exists(manifest)) return null;
                return File.ReadAllText(manifest);
            }
            catch { return null; }
        }
    }
}