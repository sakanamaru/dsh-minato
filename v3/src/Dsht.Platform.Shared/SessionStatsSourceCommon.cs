using System;
using System.Collections.Generic;
using System.IO;
using Dsht.Domain.Abstractions;

namespace Dsht.Platform.Shared
{
    /// <summary>会话投影来源（Windows/Linux 共用实现 · B-05 平台层去重）：`&lt;数据根&gt;/storages/…`，只读。</summary>
    public abstract class SessionStatsSourceCommon : ISessionStatsSource
    {
        private readonly string _dataRoot;

        protected SessionStatsSourceCommon(IPaths paths) { _dataRoot = paths == null ? "" : (paths.DataRoot ?? ""); }

        public string SessionsDir { get { return Path.Combine(Path.Combine(Path.Combine(Path.Combine(_dataRoot, "storages"), "session_projcache"), "sessions")); } }

        public string AggregatePath { get { return Path.Combine(Path.Combine(_dataRoot, "storages"), "session_projcache.json"); } }

        // ★★ **必须与桥接插件的写入位置一致** ✓✓（曾经不一致 ✗：
        //   CLI 读 `toolkit-bridge/` ✗ 而插件写 `shio-bridge/` ✗✗ → **CLI 永远读不到快照** ✓
        //   而 `shio-bridge` 是插件的 cordis id（`name = "shio-bridge"` ✓）→ **以它为准** ✓✓）
        public string SnapshotPath { get { return Path.Combine(Path.Combine(_dataRoot, "shio-bridge"), "sessions.json"); } }

        public string[] ListSessionFiles()
        {
            try
            {
                if (!Directory.Exists(SessionsDir)) return new string[0];
                string[] files = Directory.GetFiles(SessionsDir, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files;
            }
            catch { return new string[0]; }
        }

        public string ReadText(string path)
        {
            try { return (string.IsNullOrEmpty(path) || !File.Exists(path)) ? null : File.ReadAllText(path); }
            catch { return null; }
        }
    }
}
