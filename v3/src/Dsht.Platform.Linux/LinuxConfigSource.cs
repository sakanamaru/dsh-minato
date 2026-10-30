using System;
using System.IO;
using System.Text;
using Dsht.Domain.Abstractions;
using Dsht.Platform.Shared;

namespace Dsht.Platform.Linux
{
    /// <summary>配置读写（Linux）：状态目录/launcher.config，UTF-8 无 BOM。
    /// B-10：写盘改**原子写**（同目录临时文件 + File.Replace/Move），失败原因落 launcher.log；
    /// 写成功后把权限收到 0600（launcher.config 里有 balance_key 明文）。</summary>
    public sealed class LinuxConfigSource : IConfigSource
    {
        private readonly string _stateDir;
        public LinuxConfigSource(IPaths paths) { _stateDir = paths == null ? AppDomain.CurrentDomain.BaseDirectory : paths.StateDir; }
        public string ConfigPath { get { return Path.Combine(_stateDir, "launcher.config"); } }

        public string ReadConfig()
        {
            try { return File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, new UTF8Encoding(false)) : null; }
            catch (Exception ex) { AtomicConfigFile.LogReadFailure(_stateDir, ConfigPath, ex); return null; }
        }

        public void WriteConfig(string text)
        {
            // ★★★ T2/D14 修复（2026-10-07 真机复现）：状态目录不会被算出来时自动存在 ⇒ 写之前先建出来
            //   （CreateDirectory 幂等；只在真的写配置时执行 ⇒ 只读命令不产生副作用）。
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            catch { }   // 建目录失败不在这里吞掉原因：下面的原子写会失败并把原因记进 launcher.log ✓
            if (AtomicConfigFile.WriteAllText(ConfigPath, text, new UTF8Encoding(false), _stateDir)) RestrictPermissions(ConfigPath);
        }

        /// <summary>B-10：launcher.config 含 balance_key 明文 ⇒ 至少 0600（仅属主可读写）。
        /// ⚠ 本源码还要被 csc(.NET Framework 4.x / C#5) 编译（B-17）⇒ File.SetUnixFileMode 是 .NET 6+ API，
        ///   用编译期条件只编进 net8 构建；.NET Framework 构建里是空实现（那套 exe 只在 Windows 门槛里跑）。</summary>
        private void RestrictPermissions(string path)
        {
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsLinux()) return;   // 防御：Linux 实现在 Windows 测试里被直接实例化时不要调用（会抛 PlatformNotSupportedException）；用 IsLinux() 让 CA1416 分析器认出这是平台守卫
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (Exception ex) { AtomicConfigFile.TryLog(_stateDir, "WARN", "config-chmod-0600-failed path=" + path + " => " + ex.GetType().Name + ": " + ex.Message); }
#else
            // csc 构建（.NET Framework 4.x / C#5）没有 File.SetUnixFileMode；该构建不跑 Linux 真机，权限设置不适用。
            if (path == null) return;
#endif
        }
    }
}
