using System;
using System.Collections.Generic;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;
using Dsht.Domain.Services;

namespace Dsht.Cli
{
    /// <summary>会话统计相关（`sessions` 命令 + 它的辅助函数）✓。
    /// ★★ 架构审计（S1）：从 `Program.cs` 里**原样搬出来**的 ✓✓（一行逻辑都没改 ✓）
    ///   · 动机：那个文件曾经 3500+ 行 / 94 个方法 ✗ → 任何改动都碰同一个文件 ✓
    ///   · 做法：`partial class` ✓ 同程序集 ✓ **零调用点变更** ✓ 纯机械 ✓
    ///   · 纪律：搬完立刻编译 + 跑门槛 ✓（编译器 + 11 项门槛双重把关 ✓）</summary>
    public static partial class Program
    {
        /// <summary>快照的年龄（秒）✓。**解析不出来 → 返回 0** ✓（当作新鲜 ✓ —— 绝不能因为解析失败就把 live 清掉 ✗）。</summary>
        private static long SnapshotAgeSeconds(string snap)
        {
            try
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                    snap, "\"generatedAt\"\\s*:\\s*\"([^\"]+)\"");
                if (!m.Success) return 0;
                System.DateTime t;
                if (!System.DateTime.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t))
                    return 0;
                double age = (System.DateTime.UtcNow - t).TotalSeconds;
                return age < 0 ? 0 : (long)age;
            }
            catch { return 0; }
        }
        private static int Sessions(ServiceRegistry reg)
        {
            ISessionStatsSource src = reg.Get<ISessionStatsSource>();
            List<SessionStat> list = new List<SessionStat>();
            string source = "disk";
            // ★★★ **插件 N6 补全（插件复审 —— 快照非空就**独占**了历史）** ✓✓
            //   ✗ 原来：快照里只要有**一条**（插件在 dsh 运行时必然有活会话 ✓）
            //     → 整个磁盘投影**被跳过** ✗✗ → **只有历史记录、没有活会话的那些会话从面板消失** ✓
            //       （N6 之前它们至少还在 ✓ 只是 turns=0 ✓ —— 所以"回退到磁盘"只在不跑 dsh 时成立 ✗）
            //   ✓ 现在：**合并** ✓✓ —— 快照优先（活会话的实时数字 ✓），磁盘**补齐**快照没覆盖的会话 ✓
            System.Collections.Generic.HashSet<string> have =
                new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            string snap = src.ReadText(src.SnapshotPath);
            if (snap != null)
            {
                SessionStat[] fromSnap = SessionStats.ParseSnapshot(snap);
                if (fromSnap.Length > 0)
                {
                    // ★★★ **F8 真机修复（2026-10-01 真机实测抓到）** ✓✓
                    //   ✗ 插件的 dispose 钩子**在真 dsh 里根本不会跑** ✗ —— dsh 跑完任务直接退出 ✓
                    //     → 它写下的 `live:true` **永久留在盘上** ✗✗
                    //     → **面板永远显示"运行中"** ✗（正是 F8 要防的那件事 ✓ 而它只防了"正常卸载"）
                    //   · 实测证据：快照的 `generatedAt` 停在 dsh 退出前最后一次 tick ✓
                    //     之后没有任何更晚的写入 ✓ → 说明 dispose 没跑 ✓
                    //   ✓ 现在：**不信"永久的 live"** ✓✓ 用 `generatedAt` 判**新鲜度** ✓
                    //     · 插件活着时每 3 秒刷新一次 ✓ → 30 秒没刷新 = **它已经不在了** ✓
                    //     · 那条快照的其余数据**照常保留** ✓ 只把会撒谎的 `live` 置 false ✓
                    //   ✓ 放在 CLI 层而不是 Domain ✓ —— 领域层纯净度门槛禁止时钟耦合 ✓✓
                    long snapAgeSec = SnapshotAgeSeconds(snap);
            // ★ 第 2 轮审查抓到：**写死 30 秒** ✗ → intervalMs 配到 30 秒以上时，活着的 dsh 会被判成已结束 ✗✗
            // ✓ 现在：**按快照自己声明的间隔推** ✓✓（阈值 = max(30 秒, 3 × interval) ✓ 拿不到就退回 30 秒 ✓）
            long snapThreshold = 30;
            try
            {
                System.Text.RegularExpressions.Match ivm = System.Text.RegularExpressions.Regex.Match(
                    snap, "\"intervalMs\"\\s*:\\s*(\\d+)");
                if (ivm.Success)
                {
                    long iv;
                    if (long.TryParse(ivm.Groups[1].Value, out iv) && iv > 0)
                    {
                        long t3 = (iv / 1000L) * 3L;
                        if (t3 > snapThreshold) snapThreshold = t3;
                    }
                }
            }
            catch { }
            bool stale = snapAgeSec > snapThreshold;
                    if (stale)
                    {
                        int cleared = 0;
                        for (int i = 0; i < fromSnap.Length; i++)
                            if (fromSnap[i].Live) { fromSnap[i].Live = false; cleared++; }
                        if (cleared > 0)
                            Console.Error.WriteLine("SESSIONS_SNAPSHOT_STALE 快照已 " + snapAgeSec + " 秒未刷新 → 插件已不在 → 已把 " + cleared + " 条「运行中」如实置为 false（数据保留 ✓）");
                    }
                    list.AddRange(fromSnap);
                    for (int i = 0; i < fromSnap.Length; i++) if (fromSnap[i].Id != null) have.Add(fromSnap[i].Id);
                    source = "snapshot";
                }
            }
            // 磁盘投影：**总是扫** ✓ 只补快照里没有的 id ✓（有快照的那条用快照的数字 ✓ 更实时 ✓）
            int fromDisk = 0;
            string[] files = src.ListSessionFiles();
            // ★★ 第 2 轮审查抓到：下面那段 childId 扫描**又把每个文件读了一遍** ✗✗
            //   → N 个会话文件 = 2N 次全文件读 ✓（197 个会话时第二遍约 200 ms ✓ 线性增长 ✓）
            // ✓ 现在：**第一遍读到的文本缓存下来，第二遍直接复用** ✓✓（只读一遍 ✓）
            var textCache = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string id = System.IO.Path.GetFileNameWithoutExtension(files[i]);
                if (id != null && id.StartsWith("session-", StringComparison.Ordinal)) id = id.Substring("session-".Length);
                if (id != null && have.Contains(id)) continue;
                string txt1 = src.ReadText(files[i]);
                if (!string.IsNullOrEmpty(txt1)) textCache[files[i]] = txt1;
                SessionStat s = SessionStats.ParseSessionProjection(txt1, id);
                if (s != null) { list.Add(s); if (id != null) have.Add(id); fromDisk++; }
            }
            if (fromDisk > 0) source = (source == "snapshot") ? "snapshot+disk" : "disk";
            if (list.Count == 0)
            {
                string agg = src.ReadText(src.AggregatePath);
                if (agg != null)
                {
                    SessionStat[] a = SessionStats.ParseAggregate(agg);
                    if (a.Length > 0) { list.AddRange(a); source = "aggregate"; }
                }
            }
            if (list.Count == 0)
            {
                Console.WriteLine("SESSIONS_FAIL " + T("没有可读的会话投影（dsh 未初始化，或该 dsh 版本的投影格式不认）",
                    "no readable session projection (dsh not initialized, or an unrecognized projection format)"));
                return 0;
            }
            // 父子关系：dsh 把**子会话 id** 记在父会话文件的 "childId" 字段里 ✓✓
            // （2026-09-30 实测：取样 8 个会话，**7 个 id 出现在别的会话文件的 childId 里** ✓）
            // 快照里**没有**这个信息 ✗ → 所以必须扫盘 ✓；用**流式字符串扫描**（不解析 JSON ✓ 快 ✓）
            try
            {
                if (_cfg == null || !_cfg.ScanChildren) throw new InvalidOperationException("scan_children=off");   // 排障开关真的接线 ✓
                string[] cfiles = src.ListSessionFiles();
                for (int ci = 0; ci < cfiles.Length; ci++)
                {
                    string pid2 = System.IO.Path.GetFileNameWithoutExtension(cfiles[ci]);
                    if (pid2 != null && pid2.StartsWith("session-", StringComparison.Ordinal)) pid2 = pid2.Substring("session-".Length);
                    string t2;
                    if (!textCache.TryGetValue(cfiles[ci], out t2)) t2 = src.ReadText(cfiles[ci]);   // ★ 第二遍复用第一遍读到的 ✓✓
                    if (string.IsNullOrEmpty(t2)) continue;
                    int at = 0;
                    while (true)
                    {
                        int k = t2.IndexOf("\"childId\"", at, StringComparison.Ordinal);
                        if (k < 0) break;
                        int q1 = t2.IndexOf('"', k + 9);
                        if (q1 < 0) break;
                        int q2 = t2.IndexOf('"', q1 + 1);
                        if (q2 < 0) break;
                        string cid = t2.Substring(q1 + 1, q2 - q1 - 1);
                        if (cid.Length > 0) Console.WriteLine("SESSION_CHILD " + MarkerText.Encode(pid2) + " " + MarkerText.Encode(cid));   // N10 FIX: both fields came straight from file names/content
                        at = q2 + 1;
                    }
                }
            }
            catch { }
            SessionTotals tot = SessionStats.Aggregate(list);
            int liveCount = 0;
            for (int i = 0; i < list.Count; i++) if (list[i].Live) liveCount++;
            Console.WriteLine("SESSIONS_OK " + list.Count);
            Console.WriteLine("SESSIONS_NONBLANK " + tot.NonBlankCount);
            Console.WriteLine("SESSIONS_LIVE " + liveCount);   // 只在有插件快照时可能 > 0（磁盘投影没有"在跑"这个事实）
            Console.WriteLine("SESSIONS_SOURCE " + source);
            Console.WriteLine("SESSIONS_ROOT " + src.SessionsDir);
            for (int i = 0; i < list.Count; i++)
            {
                SessionStat s = list[i];
                // I5 FIX (CLI audit MINOR): the title was escaped but the id was not, so a session
                // projection whose file name contained a newline (legal on Linux) could inject a
                // fake marker line into the output the GUI parses.
                Console.WriteLine("SESSION " + MarkerText.Encode(s.Id)
                    + " title=" + MarkerText.Encode(s.Title)
                    // F-D FIX (CLI final review): the id and title were escaped but these two were not, and a
                    // timestamp read from a session file is external text - a newline in it injected a fake
                    // marker line into the output the GUI parses (the reviewer reproduced SESSIONS_OK 9999).
                    + " created=" + (string.IsNullOrEmpty(s.CreatedAt) ? "unknown" : MarkerText.Encode(s.CreatedAt))
                    + " last=" + (string.IsNullOrEmpty(s.LastPromptAt) ? "unknown" : MarkerText.Encode(s.LastPromptAt))
                    + " turns=" + s.Turns
                    + " steps=" + s.Steps
                    + " in=" + s.TotalInputTokens
                    + " out=" + s.OutputTokens
                    + " cacheRead=" + s.CacheReadTokens
                    + " hit=" + Num1(SessionStats.CacheHitPercent(s))
                    + " decode=" + Num1(SessionStats.DecodeTokensPerSec(s))
                    + " ttft=" + (s.TtftMs > 0 ? s.TtftMs.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown")
                    + " ctx=" + Num1(SessionStats.ContextPressurePercent(s))
                    + " blank=" + (s.Blank ? "1" : "0")
                    // ★ 2026-10-02：`live` 只表示"dsh 进程内还挂着"（桌面端开着时 store 里全是 live ✗）
                    //   `active` 才是"真在动"（插件相邻两拍之间 seq 变过 ✓）；未知 → unknown ✗ 不假装 0 ✓
                    + " live=" + (s.Live ? "1" : "0")
                    + " active=" + (s.HasActive ? (s.Active ? "1" : "0") : "unknown")
                    + " lastActive=" + (string.IsNullOrEmpty(s.LastActiveAt) ? "unknown" : MarkerText.Encode(s.LastActiveAt)));
            }
            Console.WriteLine("SESSIONS_TOTAL in=" + (tot.UncachedInputTokens + tot.CacheReadTokens)
                + " out=" + tot.OutputTokens
                + " cacheRead=" + tot.CacheReadTokens
                + " uncached=" + tot.UncachedInputTokens   // ★ 口径拆开：in = uncached + cacheRead（用户反馈"400 亿太恐怖"→ 让数字自我解释 ✓）
                + " hit=" + Num1(tot.CacheHitPercent)
                + " decode=" + Num1(tot.DecodeTokensPerSec));
            return 0;
        }
    }
}
