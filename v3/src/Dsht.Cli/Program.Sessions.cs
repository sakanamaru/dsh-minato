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
            return Sessions(reg, new string[0]);
        }
        /// <summary>sessions [--days 7|14|30 | --from YYYY-MM-DD --to YYYY-MM-DD] [--level global|parents|parents_sub]
        /// （看板第一批 2026-10-08 ✓✓ → 第三批血缘桥 2026-10-09 ✓✓ 规格 §11.7 ✓ 决策 D2a/D4 ✓）。
        /// ★ --days 省略 = 总计（SESSWIN_META days=unknown、from/to=「-」✓）；值只认 7/14/30 ✗ 其它一律 SESSIONS_FAIL + stderr（rc 仍 0 ✓ 与既有 FAIL 路径一致 ✓）
        /// ★ --from/--to（D4 ✓ 第三批）：本地日**含端点**闭区间；必须成对 ✓ 格式 yyyy-MM-dd ✓ from&lt;=to ✓ 与 --days 互斥 ✓ —— 违反任一 ⇒ SESSIONS_FAIL + stderr ✗ 不猜 ✓；
        ///   生效时 SESSWIN_META 打 days=unknown from/to=实际区间 ✓（§4.2 原文允许 ✓）
        /// ★ --level 省略 = 配置 sessions_default_level（默认 global ✓）；第三批三档全解锁 ✓✓ ——
        ///   血缘来自桥插件快照 lineage 段（路线①/D2a ✓ 桥插件只解原始日志**首帧** header ✓ 正文帧一帧不解 ✓）；
        ///   非 global 且 lineage=none ⇒ SESSIONS_FAIL + stderr 明说（降级文案 ✓ 规格 §11.7-D ✓）✗ 不拿不全的投影血缘冒充 ✓
        /// ★ 窗口口径（规格 §4.2 / C.4）：按会话**最后活动时间**筛选 ✓；用量为**整会话累计** ✗ 不是窗口内增量 ✓（GUI 随 truth=0 常显 C.4 脚注 ✓）
        /// ★ 时区纪律：本地日 00:00 边界的换算**在这里**做（TimeZoneInfo 逐日取偏移 = 夏令时正确 ✓）——Domain 只做毫秒区间比较 ✓（领域层不碰时钟/时区 ✓✓）</summary>
        private static int Sessions(ServiceRegistry reg, string[] args)
        {
            // —— 参数（先于任何 IO ✓；无效参数 = SESSWIN_META(source=unknown 计数全 0) + SESSIONS_FAIL + stderr，rc=0 ✓ 规格 §11.1 ✓）——
            int days = 0;   // 0 = 总计（marker 里写 unknown ✓ 窗口不过滤 → 窗口卡 == 总计卡 ✓ 硬要求 ✓）
            string daysRaw = FlagOf(args, "--days");
            if (daysRaw.Length > 0 && daysRaw != "7" && daysRaw != "14" && daysRaw != "30")
            {
                PrintWinMeta(0, "-", "-", "unknown", 0, 0, 0, "unknown", false, -1);
                Console.Error.WriteLine("SESSAGG_ARG --days 只认 7 / 14 / 30（收到：" + daysRaw + "）");
                Console.WriteLine("SESSIONS_FAIL " + T("--days 只认 7 / 14 / 30（收到：" + daysRaw + "）",
                    "--days accepts only 7 / 14 / 30 (got: " + daysRaw + ")"));
                return 0;
            }
            if (daysRaw.Length > 0) days = int.Parse(daysRaw, System.Globalization.CultureInfo.InvariantCulture);
            // —— D4 自定义日期范围（第三批 · 规格 §11.7-D ✓✓）：--from/--to 成对 · yyyy-MM-dd · from<=to · 与 --days 互斥 ——
            string fromRaw = FlagOf(args, "--from");
            string toRaw = FlagOf(args, "--to");
            bool customRange = fromRaw.Length > 0 || toRaw.Length > 0;
            DateTime customFrom = DateTime.MinValue;
            DateTime customTo = DateTime.MinValue;
            if (customRange)
            {
                string customErr = null;
                if (days > 0) customErr = "--from/--to 与 --days 互斥（选一个）";
                else if (fromRaw.Length == 0 || toRaw.Length == 0) customErr = "--from 与 --to 必须成对给出（不默认补端点 ✗ 不猜 ✓）";
                else
                {
                    DateTime pf;
                    DateTime pt;
                    bool okF = DateTime.TryParseExact(fromRaw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out pf);
                    bool okT = DateTime.TryParseExact(toRaw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out pt);
                    if (!okF || !okT) customErr = "--from/--to 只认 yyyy-MM-dd（收到：" + fromRaw + " / " + toRaw + "）";
                    else if (pf > pt) customErr = "--from 不能晚于 --to（收到：" + fromRaw + " > " + toRaw + "）";
                    else { customFrom = pf; customTo = pt; }
                }
                if (customErr != null)
                {
                    PrintWinMeta(0, "-", "-", "unknown", 0, 0, 0, "unknown", false, -1);
                    Console.Error.WriteLine("SESSAGG_ARG " + customErr);
                    Console.WriteLine("SESSIONS_FAIL " + T(customErr, "bad custom range: " + customErr));
                    return 0;
                }
            }
            // —— 窗口阈值：本地日 00:00 → 次日 00:00，**左闭右开** ✓（夏令时逐日正确 ✓）；days=0 且无自定义 → 不过滤 ✓ ——
            long fromMs = 0;
            long toMsEx = 0;
            string fromText = "-";
            string toText = "-";
            bool windowed = false;   // ★ 第三批：days 档与自定义档共用同一过滤路径 ✓（§11.7-D：窗口语义不变 ✓ C.1/C.4 ✓）
            if (days > 0)
            {
                DateTime today = DateTime.Now.Date;
                DateTime fromDate = today.AddDays(-(days - 1));
                fromMs = LocalMidnightUtcMs(fromDate);
                toMsEx = LocalMidnightUtcMs(today.AddDays(1));
                fromText = fromDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                toText = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                windowed = true;
            }
            else if (customRange)
            {
                fromMs = LocalMidnightUtcMs(customFrom);
                toMsEx = LocalMidnightUtcMs(customTo.AddDays(1));   // 含端点 ⇒ 右端 = to 的次日 00:00 ✓
                fromText = customFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                toText = customTo.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                windowed = true;
            }
            string level = FlagOf(args, "--level");
            if (level.Length == 0) level = (_cfg != null && _cfg.SessionsDefaultLevel != null) ? _cfg.SessionsDefaultLevel : "global";
            level = level.Trim().ToLowerInvariant();
            if (level != "global" && level != "parents" && level != "parents_sub")
            {
                PrintWinMeta(days, fromText, toText, "unknown", 0, 0, 0, "unknown", false, -1);
                Console.Error.WriteLine("SESSAGG_LEVEL --level 只认 global / parents / parents_sub（收到：" + level + "）");
                Console.WriteLine("SESSIONS_FAIL " + T("--level 只认 global / parents / parents_sub（收到：" + level + "）",
                    "--level accepts only global / parents / parents_sub (got: " + level + ")"));
                return 0;
            }
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
            RowGateReport rep = new RowGateReport();   // ★ R5/R6 丢弃计数 → stderr SESSAGG_ROWDROP ✓（2026-10-08 ✓ 只进 stderr ✗ 不污染 stdout 标记面 ✓）
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
                SessionStat s = SessionStats.ParseSessionProjection(txt1, id, rep);
                if (s != null) { list.Add(s); if (id != null) have.Add(id); fromDisk++; }
            }
            if (fromDisk > 0) source = (source == "snapshot") ? "snapshot+disk" : "disk";
            if (list.Count == 0)
            {
                string agg = src.ReadText(src.AggregatePath);
                if (agg != null)
                {
                    SessionStat[] a = SessionStats.ParseAggregate(agg, rep);
                    if (a.Length > 0) { list.AddRange(a); source = "aggregate"; }
                }
            }
            if (list.Count == 0)
            {
                // ★ 规格 §11.1：FAIL 路径也照打 SESSWIN_META（source=unknown、计数全 0 ✓ 在 SESSIONS_FAIL **之前** ✓）但不打 SESSAGG_TOTAL ✓
                PrintWinMeta(days, fromText, toText, "unknown", 0, 0, 0, level, false, -1);
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
            // —— 看板第三批（2026-10-09 ✓✓ 规格 §11.7 ✓ 决策 D2a 路线①）：血缘来自桥插件快照的 lineage 段 ——
            //   ★ 桥插件进程内解原始日志**首帧** header（正文帧一帧不解 ✓ 三条红线不动 ✓）；CLI 自己不碰 zstd ✓
            //   ★ 快照陈旧（dsh 已退出）**不影响**血缘可用性：历史会话首帧不会变 ✓；快照死后新建的会话 → 无血缘 → NOHEADER 如实计数 ✓
            //   ★ lineage=none（旧插件/段缺失）⇒ 非 global 口径如实拒绝（降级文案 ✓）✗ 不拿不全的投影血缘冒充（§11.2 实测否决 ✓）
            SnapshotLineage lin = (snap != null) ? SessionStats.ParseSnapshotLineage(snap) : null;
            bool lineageOk = lin != null && lin.Present;
            if (level != "global" && !lineageOk)
            {
                PrintWinMeta(days, fromText, toText, source, list.Count, 0, 0, level, false, -1);
                Console.Error.WriteLine("SESSAGG_LINEAGE 血缘不可用：未检测到桥插件快照的 lineage 段（第三批 · 路线①/D2a：dsh-minato-bridge 只解原始日志首帧 header，正文帧一帧不解）；口径 " + level + " 需要血缘 ⇒ 请安装/升级桥插件后重试");
                Console.WriteLine("SESSIONS_FAIL " + T("血缘不可用：口径 " + level + " 需要桥插件血缘段（第三批 · dsh-minato-bridge 直读原始日志首帧 header，正文帧一帧不解）；未检测到 ⇒ 请安装/升级桥插件后重试",
                    "lineage unavailable: scope " + level + " needs the bridge plugin lineage section (first-frame headers only); install/upgrade dsh-minato-bridge and retry"));
                return 0;
            }
            // 建图（纯函数 ✓ 领域层 ✓ 零 IO/零时钟 ✓✓）；knownIds = 面板会话 ∪ lineage 键（孤儿判定边界 ✓ §11.7-B ✓）
            LineageGraph graph = null;
            int noHeader = 0;   // 面板会话中无血缘边（无原始日志/解码失败/快照陈旧期新建）⇒ 全局照常 ✓ 血缘档排除 ✓ children/depth=unknown ✓
            if (lineageOk)
            {
                List<LineageEdge> edges = new List<LineageEdge>(lin.Edges.Values);
                System.Collections.Generic.HashSet<string> knownIds = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < edges.Count; i++) knownIds.Add(edges[i].Id);
                for (int i = 0; i < list.Count; i++) if (list[i].Id != null) knownIds.Add(list[i].Id);
                graph = SessionLineage.Build(edges, knownIds);
                for (int i = 0; i < list.Count; i++) if (list[i].Id == null || !lin.Edges.ContainsKey(list[i].Id)) noHeader++;
            }
            SessionTotals tot = SessionStats.Aggregate(list);
            int liveCount = 0;
            for (int i = 0; i < list.Count; i++) if (list[i].Live) liveCount++;
            Console.WriteLine("SESSIONS_OK " + list.Count);
            Console.WriteLine("SESSIONS_NONBLANK " + tot.NonBlankCount);
            Console.WriteLine("SESSIONS_LIVE " + liveCount);   // 只在有插件快照时可能 > 0（磁盘投影没有"在跑"这个事实）
            Console.WriteLine("SESSIONS_SOURCE " + source);
            Console.WriteLine("SESSIONS_ROOT " + src.SessionsDir);
            // —— 看板第一批（2026-10-08 ✓✓）：窗口元信息 + 固定总计卡 + 窗口内逐会话聚合（规格 §11.1 ✓ truth=0 ✓✓）——
            // eligible/unknown_last：days=0（总计）→ eligible = 全部 scanned ✓✓（窗口卡 == 总计卡 ✓ 硬要求 ✓）；unknown_last 只**报告**不排除 ✓
            int unknownLast = 0;
            int eligibleCount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                long lp = SessionWindow.EffectiveLastPromptMs(list[i]);
                if (lp <= 0) unknownLast++;
                if (windowed && !SessionWindow.Accept(lp, fromMs, toMsEx)) continue;
                if (!LevelOf(graph, lin, level, list[i].Id)) continue;   // 第三批：血缘档过滤（global ⇒ 恒过 ✓）
                eligibleCount++;
            }
            PrintWinMeta(days, fromText, toText, source, list.Count, eligibleCount, unknownLast,
                level, lineageOk, graph != null ? graph.ForkCount : -1);
            // 固定总计卡：**无视窗口过滤** ✓（scope=store ✓）；first_day = min(createdAt) 本地日，last_day = max(lastPromptAt) 本地日（全无 → unknown ✗ 不猜 ✓）
            long aggUncached = 0;
            long aggCacheRead = 0;
            long aggCacheWrite = 0;
            long aggOutput = 0;
            long firstDayMs = -1;
            long lastDayMs = -1;
            for (int i = 0; i < list.Count; i++)
            {
                SessionStat s = list[i];
                if (s.HasTokens)
                {
                    aggUncached += s.UncachedInputTokens;
                    aggCacheRead += s.CacheReadTokens;
                    aggCacheWrite += s.CacheWriteTokens;
                    aggOutput += s.OutputTokens;
                }
                if (s.CreatedAtEpochMs > 0 && (firstDayMs < 0 || s.CreatedAtEpochMs < firstDayMs)) firstDayMs = s.CreatedAtEpochMs;
                long lp = SessionWindow.EffectiveLastPromptMs(s);
                if (lp > lastDayMs) lastDayMs = lp;
            }
            Console.WriteLine("SESSAGG_TOTAL scope=store sessions=" + list.Count
                + " nonblank=" + tot.NonBlankCount
                + " uncached=" + aggUncached
                + " cacheRead=" + aggCacheRead
                + " cacheWrite=" + aggCacheWrite
                + " output=" + aggOutput
                + " first_day=" + (firstDayMs > 0 ? LocalDayOfEpochMs(firstDayMs) : "unknown")
                + " last_day=" + (lastDayMs > 0 ? LocalDayOfEpochMs(lastDayMs) : "unknown")
                + " truth=0");
            // —— 诚实行（只进 stderr ✓✓ stdout 标记面保持干净 ✓ verify_command_matrix 抓不到 stderr → 用 stdout 标记验证主体 ✓）——
            if (unknownLast > 0) Console.Error.WriteLine("SESSAGG_UNKNOWN_LAST n=" + unknownLast);
            EmitRowDrop(rep);
            // —— 第三批血缘诚实行（规格 §11.7-C ✓ 一律 stderr ✓ 有才打 ✓）——
            if (graph != null)
            {
                foreach (KeyValuePair<string, LineageEdge> kv in lin.Edges)
                    if (kv.Value.Multiparent)
                        Console.Error.WriteLine("SESSAGG_GUARD multiparent=" + MarkerText.Encode(kv.Value.Id));   // §B.3 ✓ 拒绝归类（该会话只进全局 ✓）
                for (int i = 0; i < graph.CycleIds.Count; i++)
                    Console.Error.WriteLine("SESSAGG_GUARD cycle=" + MarkerText.Encode(graph.CycleIds[i]));
                for (int i = 0; i < graph.Orphans.Count; i++)
                    Console.Error.WriteLine("SESSAGG_ORPHAN child=" + MarkerText.Encode(graph.Orphans[i].Key) + " parent=" + MarkerText.Encode(graph.Orphans[i].Value));
                if (noHeader > 0) Console.Error.WriteLine("SESSAGG_NOHEADER n=" + noHeader);   // 无血缘的面板会话数（lineage=none 时由 meta 全权说明 ⇒ 不打 ✓）
                if (lin.Errors > 0) Console.Error.WriteLine("SESSAGG_HDRERR n=" + lin.Errors);   // 桥侧首帧解码失败总数（§11.7-B ✓）
            }
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
            // —— 窗口内逐会话聚合（第三批 ✓✓ 规格 §11.7-C ✓）：每会话**两行** ✓ bucket=own + bucket=sub（血缘已知才打 sub ✓）
            //   ★ own 行：children=**直接子数**（血缘未知 ⇒ unknown ✓）depth=自走链深度（根=0 ✓ 环上 ⇒ unknown ✓）
            //   ★ sub 行 = own + 血缘后代闭包（**已知成员求和** ✓ + sub_unknown 计数未知成员 ✗ 不静默漏加 ✗ 不整行标 unknown ✓）
            //   ★ B.4 硬要求：children=0 ⇒ sub 行六字段与 own **逐字节相等**（⇒ 直接复用 own 的字段文本 ✓✓ 契约测试断言 ✓）
            //   ★ 只打 eligible（窗口 × 层级 ✓）；未知 → literal unknown ✗ 不假装 0 ✓✓ ——
            System.Collections.Generic.Dictionary<string, SessionStat> byId = null;
            if (graph != null)
            {
                byId = new System.Collections.Generic.Dictionary<string, SessionStat>(StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++) if (list[i].Id != null && !byId.ContainsKey(list[i].Id)) byId.Add(list[i].Id, list[i]);
            }
            for (int i = 0; i < list.Count; i++)
            {
                SessionStat s = list[i];
                if (windowed && !SessionWindow.Accept(SessionWindow.EffectiveLastPromptMs(s), fromMs, toMsEx)) continue;
                LineageEdge le = null;
                LineageInfo li = null;
                if (graph != null && s.Id != null)
                {
                    lin.Edges.TryGetValue(s.Id, out le);
                    graph.Nodes.TryGetValue(s.Id, out li);
                }
                if (!SessionLineage.LevelAccept(level, le, li)) continue;
                System.Globalization.CultureInfo i0c = System.Globalization.CultureInfo.InvariantCulture;
                string fTurns = s.HasStats ? s.Turns.ToString(i0c) : "unknown";
                string fSteps = s.HasStats ? s.Steps.ToString(i0c) : "unknown";
                string fUn = s.HasTokens ? s.UncachedInputTokens.ToString(i0c) : "unknown";
                string fCr = s.HasTokens ? s.CacheReadTokens.ToString(i0c) : "unknown";
                string fCw = s.HasTokens ? s.CacheWriteTokens.ToString(i0c) : "unknown";
                string fOp = s.HasTokens ? s.OutputTokens.ToString(i0c) : "unknown";
                string childrenText = li != null ? li.Children.Count.ToString(i0c) : "unknown";
                string depthText = (li != null && li.HasDepth) ? li.Depth.ToString(i0c) : "unknown";
                string encId = MarkerText.Encode(s.Id);
                Console.WriteLine("SESSAGG_SESSION " + encId
                    + " bucket=own"
                    + " turns=" + fTurns
                    + " steps=" + fSteps
                    + " uncached=" + fUn
                    + " cacheRead=" + fCr
                    + " cacheWrite=" + fCw
                    + " output=" + fOp
                    + " children=" + childrenText + " depth=" + depthText);
                if (li != null)
                {
                    if (li.Children.Count == 0)
                    {
                        // ★ B.4：无子会话 ⇒ sub == own（逐字节相等 ⇒ 复用 own 字段文本 ✓✓）
                        Console.WriteLine("SESSAGG_SESSION " + encId
                            + " bucket=sub"
                            + " turns=" + fTurns
                            + " steps=" + fSteps
                            + " uncached=" + fUn
                            + " cacheRead=" + fCr
                            + " cacheWrite=" + fCw
                            + " output=" + fOp
                            + " children=0 depth=" + depthText
                            + " members=1 sub_unknown=0 sub_unknown_stats=0");
                    }
                    else
                    {
                        // 闭包累加：self + 血缘后代（任意深度 ✓）；已知成员求和 ✓ 未知成员计欠账 ✓
                        long vTurns = s.HasStats ? s.Turns : 0;
                        long vSteps = s.HasStats ? s.Steps : 0;
                        long vUn = s.HasTokens ? s.UncachedInputTokens : 0;
                        long vCr = s.HasTokens ? s.CacheReadTokens : 0;
                        long vCw = s.HasTokens ? s.CacheWriteTokens : 0;
                        long vOp = s.HasTokens ? s.OutputTokens : 0;
                        int unkTok = s.HasTokens ? 0 : 1;
                        int unkStats = s.HasStats ? 0 : 1;
                        foreach (string d in li.Descendants)
                        {
                            SessionStat ds;
                            if (byId.TryGetValue(d, out ds))
                            {
                                if (ds.HasTokens) { vUn += ds.UncachedInputTokens; vCr += ds.CacheReadTokens; vCw += ds.CacheWriteTokens; vOp += ds.OutputTokens; }
                                else unkTok++;
                                if (ds.HasStats) { vTurns += ds.Turns; vSteps += ds.Steps; }
                                else unkStats++;
                            }
                            else { unkTok++; unkStats++; }   // 闭包成员不在聚合宇宙（无投影/无快照条目）⇒ 用量不可知 ⇒ 计欠账 ✗ 不静默漏加 ✓
                        }
                        Console.WriteLine("SESSAGG_SESSION " + encId
                            + " bucket=sub"
                            + " turns=" + vTurns.ToString(i0c)
                            + " steps=" + vSteps.ToString(i0c)
                            + " uncached=" + vUn.ToString(i0c)
                            + " cacheRead=" + vCr.ToString(i0c)
                            + " cacheWrite=" + vCw.ToString(i0c)
                            + " output=" + vOp.ToString(i0c)
                            + " children=" + childrenText + " depth=" + depthText
                            + " members=" + (1 + li.Descendants.Count).ToString(i0c)
                            + " sub_unknown=" + unkTok.ToString(i0c)
                            + " sub_unknown_stats=" + unkStats.ToString(i0c));
                    }
                }
                // —— 看板第二批（2026-10-09 ✓✓ 规格 §11.6-B ✓）：逐会话**计时四件** + **上下文构成三桶** ——
                //   与 SESSAGG_SESSION 同一 eligible 集 ✓ 同一循环 ✓；
                //   行缺失/被 R5 门拒 → 全位 literal unknown ✗ 绝不假装 0（零值是真实值 ✓ 照打数字 ✓）——
                Console.WriteLine("SESSTIME_SESSION " + MarkerText.Encode(s.Id)
                    + " llmMs=" + (s.HasStats ? s.LlmMs.ToString(i0c) : "unknown")
                    + " toolMs=" + (s.HasStats ? s.ToolMs.ToString(i0c) : "unknown")
                    + " ttftMs=" + (s.HasStats ? s.TtftMs.ToString(i0c) : "unknown")
                    + " decodeMs=" + (s.HasStats ? s.DecodeMs.ToString(i0c) : "unknown"));
                Console.WriteLine("SESSCTX_SESSION " + MarkerText.Encode(s.Id)
                    + " system=" + (s.HasBreakdown ? s.SystemTokens.ToString(i0c) : "unknown")
                    + " tools=" + (s.HasBreakdown ? s.ToolsTokens.ToString(i0c) : "unknown")
                    + " message=" + (s.HasBreakdown ? s.MessageTokens.ToString(i0c) : "unknown"));
            }
            return 0;
        }
        /// <summary>SESSWIN_META 发射（OK 与 FAIL 路径共用 ✓ 规格 §11.1 ✓）。
        /// days=0 → days=unknown ✓（总计与**自定义区间**都是 ✓ —— 自定义时 from/to=实际区间 ✓ §4.2 原文允许 ✓）。
        /// 第三批追加键（§11.7-C ✓）：level=实际生效口径 ✓ lineage=ok|none ✓ fork_sessions=n|unknown ✓（FAIL 早退 ⇒ level=unknown lineage=none fork_sessions=unknown ✓）。
        /// tz：TimeZoneInfo.Local.Id —— Linux 上是 IANA 名，Windows 上是 Windows 时区 ID（.NET 4.x 无 IANA 映射 API ✓ 如实上报平台名 ✗ 不维护对照表 ✗ 规格 §11.1 偏差登记 ✓）。</summary>
        private static void PrintWinMeta(int days, string fromText, string toText, string source, int scanned, int eligible, int unknownLast,
            string level, bool lineageOk, long forkSessions)
        {
            Console.WriteLine("SESSWIN_META days=" + (days > 0 ? days.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown")
                + " from=" + fromText
                + " to=" + toText
                + " tz=" + MarkerText.Encode(TimeZoneInfo.Local.Id)
                + " truth=0 level=" + level + " source=" + source
                + " scanned=" + scanned
                + " eligible=" + eligible
                + " unknown_last=" + unknownLast
                + " lineage=" + (lineageOk ? "ok" : "none")
                + " fork_sessions=" + (forkSessions >= 0 ? forkSessions.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown"));
        }
        /// <summary>层级过滤的 CLI 侧封装（第三批 ✓）：查血缘边+节点 → 领域层 <see cref="SessionLineage.LevelAccept"/> 判定（口径只在领域层写一遍 ✓✓）。
        /// lineage 不可用（graph=null）⇒ global 恒过 ✓ 其余档在到达这里**之前**已被 SESSIONS_FAIL 拒绝 ✓（双保险 ✓）。</summary>
        private static bool LevelOf(LineageGraph graph, SnapshotLineage lin, string level, string id)
        {
            if (graph == null || lin == null) return level == "global";
            LineageEdge le = null;
            LineageInfo li = null;
            if (id != null)
            {
                lin.Edges.TryGetValue(id, out le);
                graph.Nodes.TryGetValue(id, out li);
            }
            return SessionLineage.LevelAccept(level, le, li);
        }
        /// <summary>R5/R6 丢弃计数 → stderr SESSAGG_ROWDROP（每条非零桶一行 ✓ key=<名> ver=<期望版本> n=<条数> ✓）。</summary>
        private static void EmitRowDrop(RowGateReport rep)
        {
            if (rep == null) return;
            if (rep.DocRejected > 0) Console.Error.WriteLine("SESSAGG_ROWDROP key=document ver=" + SessionStats.ProjectionDocCompatMin + "-" + SessionStats.ProjectionDocVersion + " n=" + rep.DocRejected);
            if (rep.SessionStats > 0) Console.Error.WriteLine("SESSAGG_ROWDROP key=sessionStats ver=" + SessionStats.RowVerSessionStats + " n=" + rep.SessionStats);
            if (rep.TokenUsage > 0) Console.Error.WriteLine("SESSAGG_ROWDROP key=tokenUsage ver=" + SessionStats.RowVerTokenUsage + " n=" + rep.TokenUsage);
            if (rep.ContextPressure > 0) Console.Error.WriteLine("SESSAGG_ROWDROP key=contextPressure ver=" + SessionStats.RowVerContextPressure + " n=" + rep.ContextPressure);
            if (rep.ContextBreakdown > 0) Console.Error.WriteLine("SESSAGG_ROWDROP key=contextBreakdown ver=2/4/5 n=" + rep.ContextBreakdown);   // 接受集合（第二批 §11.6-A ✓ 单版本文案已过时 ✓）
            if (rep.SessionListMetadata > 0) Console.Error.WriteLine("SESSAGG_ROWDROP key=sessionListMetadata ver=" + SessionStats.RowVerSessionListMetadata + " n=" + rep.SessionListMetadata);
            if (rep.Title > 0) Console.Error.WriteLine("SESSAGG_ROWDROP key=title ver=" + SessionStats.RowVerTitle + " n=" + rep.Title);
        }
        /// <summary>本地日 00:00 → UTC 毫秒（窗口边界 ✓ 左闭右开 ✓ 逐日取偏移 = 夏令时正确 ✓）。</summary>
        private static long LocalMidnightUtcMs(DateTime localDate)
        {
            TimeSpan off = TimeZoneInfo.Local.GetUtcOffset(localDate);
            DateTime utcTicks = new DateTime(localDate.Ticks, DateTimeKind.Unspecified).Subtract(off);
            return (long)(utcTicks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }
        /// <summary>epoch 毫秒 → **本地日** yyyy-MM-dd（CLI 层 ✓ 时区在这里碰 ✓ 逐时刻取偏移 = 夏令时正确 ✓）。≤0/异常 → "unknown" ✗ 不猜 ✓。</summary>
        private static string LocalDayOfEpochMs(long ms)
        {
            if (ms <= 0) return "unknown";
            try
            {
                DateTime utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
                DateTime local = utc + TimeZoneInfo.Local.GetUtcOffset(utc);
                return local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return "unknown"; }
        }
    }
}
