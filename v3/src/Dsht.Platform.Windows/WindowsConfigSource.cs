using System;
using System.IO;
using System.Text;
using Dsht.Domain.Abstractions;
using Dsht.Platform.Shared;

namespace Dsht.Platform.Windows
{
    /// <summary>配置读写（Windows）：状态目录/launcher.config，UTF-8 无 BOM（与 v2.x SaveConfig 一致）。
    /// B-10：写盘改**原子写**（同目录临时文件 + File.Replace/Move），失败原因落 launcher.log（不再 catch { } 静默吞）；
    /// 读失败同样落日志。IConfigSource 的签名（string ReadConfig / void WriteConfig）保持不变。</summary>
    public sealed class WindowsConfigSource : IConfigSource
    {
        private readonly string _stateDir;
        public WindowsConfigSource(IPaths paths) { _stateDir = paths == null ? AppDomain.CurrentDomain.BaseDirectory : paths.StateDir; }
        public string ConfigPath { get { return Path.Combine(_stateDir, "launcher.config"); } }

        public string ReadConfig()
        {
            try { return File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, new UTF8Encoding(false)) : null; }
            catch (Exception ex) { AtomicConfigFile.LogReadFailure(_stateDir, ConfigPath, ex); return null; }
        }

        public void WriteConfig(string text)
        {
            // 不在本类建状态目录：Windows 侧 StateDir 由 WindowsPaths.ResolveStateDir() 的写探针建出（既有语义）。
            AtomicConfigFile.WriteAllText(ConfigPath, text, new UTF8Encoding(false), _stateDir);
        }
    }
}
