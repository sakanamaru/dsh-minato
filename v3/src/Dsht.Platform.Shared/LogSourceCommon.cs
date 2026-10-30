using System;
using System.IO;
using Dsht.Domain.Abstractions;

namespace Dsht.Platform.Shared
{
    /// <summary>日志来源（Windows/Linux 共用实现 · B-05 平台层去重）：状态目录/logs/launcher.log（与 v2.x 同路径）。只读；
    /// 文件不存在或读不到返回 null（摘要格式由领域层的 LogSummaryBuilder 负责）。</summary>
    public abstract class LogSourceCommon : ILogSource
    {
        private readonly string _stateDir;

        protected LogSourceCommon(IPaths paths) { _stateDir = paths == null ? AppDomain.CurrentDomain.BaseDirectory : paths.StateDir; }

        public string LogPath { get { return Path.Combine(Path.Combine(_stateDir, "logs"), "launcher.log"); } }

        public string ReadLog()
        {
            try { return File.Exists(LogPath) ? File.ReadAllText(LogPath) : null; }
            catch { return null; }
        }
        /// <summary>追加一条操作日志（尽力而为 ✓：日志失败不影响主操作 —— 这是唯一合理的静默点 ✓）。</summary>
        public void Append(string level, string message)
        {
            try
            {
                string dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " " + level + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
