using System;
using System.IO;
using System.Text;
using Dsht.Domain.Abstractions;

namespace Dsht.Platform.Linux
{
    /// <summary>配置读写（Linux）：状态目录/launcher.config，UTF-8 无 BOM。</summary>
    public sealed class LinuxConfigSource : IConfigSource
    {
        private readonly string _stateDir;
        public LinuxConfigSource(IPaths paths) { _stateDir = paths == null ? AppDomain.CurrentDomain.BaseDirectory : paths.StateDir; }
        public string ConfigPath { get { return Path.Combine(_stateDir, "launcher.config"); } }

        public string ReadConfig()
        {
            try { return File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, new UTF8Encoding(false)) : null; }
            catch { return null; }
        }

        public void WriteConfig(string text)
        {
            // ★★★ T2/D14 修复（2026-10-07 真机复现）：**状态目录不存在时，写盘静默失败** ✗✗
            //   · Linux 的 StateDir = `$XDG_DATA_HOME/DeepSeekHarnessLauncher`（默认 `~/.local/share/...`）
            //     —— `ResolveStateDir()` 只**算出路径**，**从不创建它** ✗
            //     （Windows 侧不同：`ResolveStateDir()` 会先做一次 `File.Create` 写探针 ✓ 顺带把目录建出来 ✓
            //      所以 Windows 上从来没有这个缺陷 ✓ 这也是它在 Windows 上测不出来的原因 ✓）
            //   · 于是全新机器/全新 HOME 上**第一次** `config-set` 时：
            //       `File.WriteAllText` 抛 `DirectoryNotFoundException` → 被下面的 catch **吞掉** ✗
            //       → 回读拿到 null → 解析成默认配置 → 与请求值不等 → 打印
            //         `CONFIGSET_FAIL 写入未生效（配置文件可能只读或被占用）` ✗（**归因是错的** ✓ 真因是目录不存在）
            //     真机实测（Ubuntu 26.04 · 全新 HOME + 全新 XDG_DATA_HOME · 目录完全可写）：
            //       `config-set ws /tmp/.../ws` → `CONFIGSET_FAIL 写入未生效`；手工 `mkdir -p` 同一个目录后再跑 → `CONFIGSET_OK ws` ✓
            //   ✓ 现在：写之前**先把状态目录建出来** ✓✓
            //     · 与同平台的 `LinuxLogSource.WriteLog` 同一手法（它一直 `Directory.CreateDirectory(dir)` ✓）
            //     · `CreateDirectory` 对已存在的目录是**幂等**的 ✓（不改变既有行为 ✓）
            //     · 仍然只在**真的写配置**时创建 ✓（只读命令不产生副作用 ✓）
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, text, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}