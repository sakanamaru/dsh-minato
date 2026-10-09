using System;
using System.Collections.Generic;
using Dsht.Domain.Model;

namespace Dsht.Domain.Services
{
    /// <summary>会话投影解析与派生指标（纯函数）。
    /// 数据来源（dsh 0.1.5-rc.2 本机实测，明文 JSON、只读、持续更新）：
    ///   · 单会话：`&lt;数据根&gt;/storages/session_projcache/sessions/&lt;id&gt;.json` = `{version, record:{identity, rows:[…]}}`
    ///   · 投影总表：`&lt;数据根&gt;/storages/session_projcache.json` = `{unit, global, tables:{sessions:{&lt;key&gt;:{identity, rows:[…]}}}}`
    ///   · 插件快照（可选，由我们的桥接插件写）：见 <see cref="SnapshotFormatVersion"/> 的说明
    /// **诚实边界**：字段缺失 → Has*=false、派生指标返回 -1（未知），**绝不假装 0**；
    /// 格式/版本不认 → 返回 null / 空数组，由调用方诚实降级。
    /// **隐私边界**：只读计数、时间与元数据（id/标题/cwd）；对话正文在 zstd 压缩的 `session.jsonl.zstd` 里，本工具不读。</summary>
    /// <summary>R5/R6 版本门的**可见丢弃计数**（看板第一批 · 验收③的证据载体 ✓✓）。
    /// 行 `ver` 不匹配 → 该行丢弃 + 对应桶 +1；文档 `version` 不认 → 整份拒收 + DocRejected。
    /// 纯数据容器（零逻辑 ✓）；CLI 汇总后打到 stderr（SESSAGG_ROWDROP）✓ GUI 只吃 stdout ✗ 不受影响 ✓。</summary>
    public sealed class RowGateReport
    {
        /// <summary>R6：文档版本不认而整份拒收的投影数。</summary>
        public long DocRejected;
        /// <summary>R5：sessionStats 行版本不匹配丢弃数。</summary>
        public long SessionStats;
        /// <summary>R5：tokenUsage 行版本不匹配丢弃数。</summary>
        public long TokenUsage;
        /// <summary>R5：contextPressure 行版本不匹配丢弃数。</summary>
        public long ContextPressure;
        /// <summary>R5：contextBreakdown 行版本不匹配丢弃数。</summary>
        public long ContextBreakdown;
        /// <summary>R5：sessionListMetadata 行版本不匹配丢弃数。</summary>
        public long SessionListMetadata;
        /// <summary>R5：title 行版本不匹配丢弃数。</summary>
        public long Title;
        /// <summary>R5 行丢弃合计（不含整文档拒收）。</summary>
        public long RowDroppedTotal { get { return SessionStats + TokenUsage + ContextPressure + ContextBreakdown + SessionListMetadata + Title; } }
    }

    public static class SessionStats
    {
        /// <summary>插件快照格式版本（我们的桥接插件与 CLI 之间的约定）。</summary>
        public const int SnapshotFormatVersion = 2;

        /// <summary>仍接受的最小版本（v1 无 live 字段）。</summary>
        public const int SnapshotFormatVersionMin = 1;

        /// <summary>未知值（派生指标的分母为 0 时返回它，而不是 0）。</summary>
        public const double Unknown = -1;

        // ---------------- R5/R6 版本门（看板第一批 · 施工规格 §11.4 ✓✓）----------------
        // 版本表 = **线上写入器**（桌面端 app.asar 内 dsh-api-session-controller）的 stateVersions 实测（2026-10-08 提取 ✓）；
        //   npm 0.1.5-rc.2 包里的同名拷贝是**旧值**（contextBreakdown=4 等）✗ 已弃用 ✗。
        // 行 `ver` ≠ 下表 → 该行丢弃并计数（语义可能已变 ✗ 绝不猜 ✓）；**只接受精确相等**（更高的未来版本同样丢弃 ✓）。

        /// <summary>R5：sessionStats 行的当前写入器版本。</summary>
        public const long RowVerSessionStats = 1;
        /// <summary>R5：tokenUsage 行的当前写入器版本。</summary>
        public const long RowVerTokenUsage = 2;
        /// <summary>R5：contextPressure 行的当前写入器版本。</summary>
        public const long RowVerContextPressure = 5;
        /// <summary>R5：contextBreakdown 行的当前写入器版本。</summary>
        public const long RowVerContextBreakdown = 5;
        /// <summary>R5：contextBreakdown 行的**接受集合**（看板第二批 · 2026-10-09 实测 283/283 ✓ 规格 §11.6-A）：
        /// ver2 ×51 = **扁平** `val.{systemTokens,toolsTokens,messageTokens}`；ver4 ×114 / ver5 ×118 = **嵌套** `val.breakdown.{同名三桶}`。
        /// 三桶键名/语义三个版本一致（同为 dsh 启发式折算输出；ver4/5 的 `nodes[]` 明细本工具不读）⇒ 按实测同义证据接纳三版；
        /// 其余 ver（ver3、≥6、缺失/畸形）照拒 —— 语义可能已变 ✗ 绝不猜 ✓。</summary>
        public static readonly long[] RowVerContextBreakdownAccepted = new long[] { 2, 4, 5 };
        /// <summary>R5：sessionListMetadata 行的当前写入器版本。</summary>
        public const long RowVerSessionListMetadata = 1;
        /// <summary>R5：title 行的当前写入器版本。</summary>
        public const long RowVerTitle = 1;

        /// <summary>R6：单会话投影文档的当前版本（实测 283/283 份均为 7）。</summary>
        public const long ProjectionDocVersion = 7;
        /// <summary>R6：写入器声明的文档兼容带下界（compatibleVersions [3..6] 实测）——接受区间 = [3..7]。</summary>
        public const long ProjectionDocCompatMin = 3;

        // ---------------- 解析 ----------------

        /// <summary>解析单个会话投影文件 → SessionStat；格式不认 → null。</summary>
        public static SessionStat ParseSessionProjection(string json, string id)
        {
            return ParseSessionProjection(json, id, null);
        }

        /// <summary>带 R6 文档版本门 + R5 行版本门的解析（rep 可空：不需要计数时传 null）。
        /// R6：根 `version` 必须落在 [ProjectionDocCompatMin..ProjectionDocVersion]（= 写入器兼容带 [3..6] ∪ 当前 7 ✓ 实测）；
        ///   缺失/畸形/越界 → **整份拒收**（null）并 rep.DocRejected++ —— 文档版本不认 = 整体语义可能已变 ✗ 绝不猜 ✓。</summary>
        public static SessionStat ParseSessionProjection(string json, string id, RowGateReport rep)
        {
            JNode root = JsonLite.Parse(json);
            if (root == null || !root.IsObject) return null;
            JNode verNode = root.Get("version");
            long docVer = verNode == null ? -1 : (long)verNode.AsNumber(-1);
            if (docVer < ProjectionDocCompatMin || docVer > ProjectionDocVersion)
            {
                if (rep != null) rep.DocRejected++;
                return null;
            }
            JNode record = root.Get("record");
            if (record == null || !record.IsObject) return null;
            JNode row = RowOf(record.Get("rows"));
            if (row == null) return null;
            SessionStat s = FromRow(row, id, rep);
            if (s == null) return null;
            FillIdentity(s, record.Get("identity"));
            return s;
        }

        /// <summary>解析投影总表 → 全部会话（表键即 id，去掉 `session-` 前缀）。格式不认 → 空数组。</summary>
        public static SessionStat[] ParseAggregate(string json)
        {
            return ParseAggregate(json, null);
        }

        /// <summary>带 R5 行版本门的总表解析（rep 可空）。
        /// ★ R6 文档门**不适用**于总表（§11.4 ✓）：总表的每会话条目**没有** `version` 字段（实测形状 ✓）——
        ///   总表整体形状由 `tables.sessions` 的存在性担保，行级语义仍由 R5 行门把关 ✓。</summary>
        public static SessionStat[] ParseAggregate(string json, RowGateReport rep)
        {
            List<SessionStat> list = new List<SessionStat>();
            JNode root = JsonLite.Parse(json);
            if (root == null) return list.ToArray();
            JNode sessions = root.Path("tables", "sessions");
            if (sessions == null || !sessions.IsObject || sessions.Members == null) return list.ToArray();
            foreach (KeyValuePair<string, JNode> kv in sessions.Members)
            {
                JNode entry = kv.Value;
                if (entry == null || !entry.IsObject) continue;
                JNode row = RowOf(entry.Get("rows"));
                if (row == null) continue;
                string id = kv.Key == null ? "" : kv.Key;
                if (id.StartsWith("session-", StringComparison.Ordinal)) id = id.Substring("session-".Length);
                SessionStat s = FromRow(row, id, rep);
                if (s == null) continue;
                FillIdentity(s, entry.Get("identity"));
                list.Add(s);
            }
            return list.ToArray();
        }

        /// <summary>解析插件快照（格式 v1，见 <see cref="SnapshotFormatVersion"/>）；版本不认/格式不认 → 空数组。
        /// 快照形状：`{ "formatVersion":1, "generatedAt":"…", "sessions":[ { "id":"…", "title":"…", "turns":1, "steps":2,
        /// "uncachedInputTokens":3, "outputTokens":4, "cacheReadTokens":5, "cacheWriteTokens":6, "decodeMs":7,
        /// "decodeTokens":8, "ttftMs":9, "contextWindow":10, "pressureTokens":11, "lastPromptAt":"…", "blank":false } ] }`</summary>
        public static SessionStat[] ParseSnapshot(string json)
        {
            List<SessionStat> list = new List<SessionStat>();
            JNode root = JsonLite.Parse(json);
            if (root == null || !root.IsObject) return list.ToArray();
            JNode ver = root.Get("formatVersion");
            long v = ver == null ? -1 : (long)ver.AsNumber(-1);
            if (v != SnapshotFormatVersionMin && v != SnapshotFormatVersion) return list.ToArray();   // v1（无 live）与 v2（含 live）都认
            JNode arr = root.Get("sessions");
            if (arr == null || !arr.IsArray) return list.ToArray();
            for (int i = 0; i < arr.Items.Count; i++)
            {
                JNode n = arr.Items[i];
                if (n == null || !n.IsObject) continue;
                SessionStat s = new SessionStat();
                s.Id = Str(n, "id");
                s.Title = Str(n, "title");
                s.Cwd = Str(n, "cwd");
                s.CreatedAt = Str(n, "createdAt");
                s.LastPromptAt = Str(n, "lastPromptAt");
                s.Blank = n.Get("blank") != null && n.Get("blank").AsBool(false);
                s.Live = n.Get("live") != null && n.Get("live").AsBool(false);
                // ★ v2 追加（2026-10-02，可选字段 ✓ 老插件没有 → HasActive=false → 未知 ✗ 不假装 ✗）
                s.HasActive = n.Get("active") != null;
                s.Active = s.HasActive && n.Get("active").AsBool(false);
                s.LastActiveAt = Str(n, "lastActiveAt");
                s.Turns = Num(n, "turns");
                s.Steps = Num(n, "steps");
                s.LlmMs = Num(n, "llmMs");
                s.ToolMs = Num(n, "toolMs");
                s.TtftMs = Num(n, "ttftMs");
                s.DecodeMs = Num(n, "decodeMs");
                s.DecodeTokens = Num(n, "decodeTokens");
                s.UncachedInputTokens = Num(n, "uncachedInputTokens");
                s.OutputTokens = Num(n, "outputTokens");
                s.CacheReadTokens = Num(n, "cacheReadTokens");
                s.CacheWriteTokens = Num(n, "cacheWriteTokens");
                s.ContextWindow = Num(n, "contextWindow");
                s.PressureTokens = Num(n, "pressureTokens");
                s.SurfaceTokens = Num(n, "surfaceTokens");
                s.HasStats = n.Get("turns") != null;
                s.HasTokens = n.Get("uncachedInputTokens") != null || n.Get("outputTokens") != null;
                s.HasPressure = n.Get("contextWindow") != null;
                list.Add(s);
            }
            return list.ToArray();
        }

        /// <summary>解析插件快照的**血缘段**（看板第三批 · 规格 §11.7-A.5 ✓✓）。
        /// 快照顶层追加键 `lineage`（**追加不动版本号** ✓ 旧 CLI/GUI 只读已知键 ⇒ 向后兼容 ✓）：
        /// `{ "lineage": { "errors": &lt;解码失败数&gt;, "sessions": { "&lt;id&gt;": { "origin":"subagent"?,
        /// "parent":"&lt;id&gt;"?, "createdAt":&lt;epochMs&gt;? } } } }`（字段缺失就不写 ✓ F5 纪律 ✓）。
        /// ★ 格式不认 / 段缺失 ⇒ Present=false（=「血缘不可用」✓ 调用方据此拒绝非 global 口径 ✗ 不猜 ✓）。
        /// ★ `parent` 防御**数组形态**（§B.3 多父 ✓）：&gt;1 个 id ⇒ Multiparent=true ⇒ 该会话拒绝归类。
        /// ★ id/parent 一律去 `session-` 前缀（与 ParseAggregate 同口径 ✓）。</summary>
        public static SnapshotLineage ParseSnapshotLineage(string json)
        {
            SnapshotLineage r = new SnapshotLineage();
            JNode root = JsonLite.Parse(json);
            if (root == null || !root.IsObject) return r;
            JNode lin = root.Get("lineage");
            if (lin == null || !lin.IsObject) return r;
            JNode errs = lin.Get("errors");
            r.Errors = errs == null ? 0 : (long)errs.AsNumber(0);
            if (r.Errors < 0) r.Errors = 0;
            JNode sessions = lin.Get("sessions");
            if (sessions == null || !sessions.IsObject || sessions.Members == null) return r;
            r.Present = true;   // 段存在且 sessions 是对象 ⇒ 血缘可用（哪怕 0 条边 ✓ 0 条边 ≠ 不可用 ✓）
            foreach (KeyValuePair<string, JNode> kv in sessions.Members)
            {
                JNode n = kv.Value;
                if (n == null || !n.IsObject) continue;
                string id = kv.Key == null ? "" : kv.Key;
                if (id.StartsWith("session-", StringComparison.Ordinal)) id = id.Substring("session-".Length);
                if (id.Length == 0) continue;
                LineageEdge e = new LineageEdge();
                e.Id = id;
                e.Origin = Str(n, "origin") ?? "";
                JNode p = n.Get("parent");
                if (p != null)
                {
                    if (p.IsArray)
                    {
                        // 多父防御（§B.3 ✓）：&gt;1 个 id ⇒ 拒绝归类（=1 ⇒ 按单值收 ✓ =0 ⇒ 无父 ✓）
                        List<string> ids = new List<string>();
                        for (int i = 0; i < p.Items.Count; i++)
                        {
                            string one = p.Items[i] == null ? null : p.Items[i].AsString(null);
                            if (one == null) continue;
                            if (one.StartsWith("session-", StringComparison.Ordinal)) one = one.Substring("session-".Length);
                            if (one.Length > 0 && !ids.Contains(one)) ids.Add(one);
                        }
                        if (ids.Count > 1) { e.Multiparent = true; e.Parent = ids[0]; }
                        else if (ids.Count == 1) e.Parent = ids[0];
                    }
                    else
                    {
                        string one = p.AsString(null);
                        if (one != null)
                        {
                            if (one.StartsWith("session-", StringComparison.Ordinal)) one = one.Substring("session-".Length);
                            e.Parent = one;
                        }
                    }
                }
                if (r.Edges.ContainsKey(e.Id)) continue;   // 同 id 双写 = 数据已坏 ✗ 不猜 ✓ 先到为准（与建图纪律一致 ✓）
                r.Edges[e.Id] = e;
            }
            return r;
        }

        // ---------------- 派生指标（纯函数） ----------------

        /// <summary>缓存命中率（%）= cacheRead / (cacheRead + uncachedInput)；分母 0 → Unknown。</summary>
        public static double CacheHitPercent(SessionStat s)
        {
            if (s == null) return Unknown;
            long denom = s.CacheReadTokens + s.UncachedInputTokens;
            if (denom <= 0) return Unknown;
            return s.CacheReadTokens * 100.0 / denom;
        }

        /// <summary>解码速度（tokens/s）= decodeTokens / decodeMs × 1000；decodeMs 0 → Unknown。</summary>
        public static double DecodeTokensPerSec(SessionStat s)
        {
            if (s == null || s.DecodeMs <= 0) return Unknown;
            return s.DecodeTokens * 1000.0 / s.DecodeMs;
        }

        /// <summary>上下文压力（%）= pressureTokens / contextWindow × 100；窗口 0 → Unknown。</summary>
        public static double ContextPressurePercent(SessionStat s)
        {
            if (s == null || s.ContextWindow <= 0) return Unknown;
            return s.PressureTokens * 100.0 / s.ContextWindow;
        }

        /// <summary>多会话汇总：合计 token/解码量，并按合计值算加权命中率与速度（避免对百分比求平均）。</summary>
        public static SessionTotals Aggregate(IList<SessionStat> list)
        {
            SessionTotals t = new SessionTotals();
            if (list == null) return t;
            for (int i = 0; i < list.Count; i++)
            {
                SessionStat s = list[i];
                if (s == null) continue;
                t.Count++;
                if (!s.Blank) t.NonBlankCount++;
                t.UncachedInputTokens += s.UncachedInputTokens;
                t.OutputTokens += s.OutputTokens;
                t.CacheReadTokens += s.CacheReadTokens;
                t.CacheWriteTokens += s.CacheWriteTokens;
                t.DecodeMs += s.DecodeMs;
                t.DecodeTokens += s.DecodeTokens;
            }
            long denom = t.CacheReadTokens + t.UncachedInputTokens;
            t.CacheHitPercent = denom <= 0 ? Unknown : t.CacheReadTokens * 100.0 / denom;
            t.DecodeTokensPerSec = t.DecodeMs <= 0 ? Unknown : t.DecodeTokens * 1000.0 / t.DecodeMs;
            return t;
        }

        // ---------------- 内部 ----------------

        /// <summary>取投影行：dsh 的 `rows` 是**对象**（投影名 → `{ver,seq,val}`，本机实测确认），
        /// 同时兼容数组形态（防御 dsh 版本差异）。</summary>
        private static JNode RowOf(JNode rows)
        {
            if (rows == null) return null;
            if (rows.IsObject) return rows;
            if (rows.IsArray && rows.Items.Count > 0) return rows.Items[0];
            return null;
        }

        /// <summary>R5 行版本门：该键的行**存在**但 `ver` ≠ 当前写入器版本（ver 缺失/畸形也按不匹配算 ✓）→ true。
        /// 行**不存在** → false：让调用方按"字段缺失"的旧逻辑走（Has*=false ✓ 缺失 ≠ 丢弃 ✓✓ 不计数 ✓）。</summary>
        private static bool RowMismatch(JNode row, string key, long wantVer)
        {
            JNode entry = row == null ? null : row.Get(key);
            if (entry == null) return false;
            JNode ver = entry.Get("ver");
            long v = ver == null ? -1 : (long)ver.AsNumber(-1);
            return v != wantVer;
        }

        /// <summary>R5 行版本门（**接受集合**版 —— 看板第二批 contextBreakdown 用 ✓ 规格 §11.6-A）：
        /// 行**存在**但 `ver` ∉ vers（ver 缺失/畸形也按不匹配算 ✓）→ true；行**不存在** → false（同单版本版语义 ✓）。</summary>
        private static bool RowMismatchAny(JNode row, string key, long[] vers)
        {
            JNode entry = row == null ? null : row.Get(key);
            if (entry == null) return false;
            JNode ver = entry.Get("ver");
            long v = ver == null ? -1 : (long)ver.AsNumber(-1);
            for (int i = 0; i < vers.Length; i++) if (v == vers[i]) return false;
            return true;
        }

        private static SessionStat FromRow(JNode row, string id, RowGateReport rep)
        {
            if (row == null || !row.IsObject) return null;
            SessionStat s = new SessionStat();
            s.Id = id == null ? "" : id;

            JNode st = row.Path("sessionStats", "val");
            if (RowMismatch(row, "sessionStats", RowVerSessionStats)) { if (rep != null) rep.SessionStats++; st = null; }
            if (st != null && st.IsObject)
            {
                s.HasStats = true;
                s.Turns = Num(st, "turns");
                s.Steps = Num(st, "steps");
                s.LlmMs = Num(st, "llmMs");
                s.ToolMs = Num(st, "toolMs");
                s.TtftMs = Num(st, "ttftMs");
                s.TtftSteps = Num(st, "ttftSteps");
                s.DecodeMs = Num(st, "decodeMs");
                s.DecodeTokens = Num(st, "decodeTokens");
            }
            JNode tu = row.Path("tokenUsage", "val", "totals");
            if (RowMismatch(row, "tokenUsage", RowVerTokenUsage)) { if (rep != null) rep.TokenUsage++; tu = null; }
            if (tu != null && tu.IsObject)
            {
                s.HasTokens = true;
                s.UncachedInputTokens = Num(tu, "uncachedInputTokens");
                s.OutputTokens = Num(tu, "outputTokens");
                s.CacheReadTokens = Num(tu, "cacheReadTokens");
                s.CacheWriteTokens = Num(tu, "cacheWriteTokens");
            }
            JNode cp = row.Path("contextPressure", "val");
            if (RowMismatch(row, "contextPressure", RowVerContextPressure)) { if (rep != null) rep.ContextPressure++; cp = null; }
            if (cp != null && cp.IsObject)
            {
                s.HasPressure = true;
                s.SurfaceTokens = Num(cp, "surfaceTokens");
                s.ContextWindow = Num(cp, "contextWindow");
                s.PressureTokens = Num(cp, "pressureTokens");
            }
            JNode cb = row.Path("contextBreakdown", "val");
            if (RowMismatchAny(row, "contextBreakdown", RowVerContextBreakdownAccepted)) { if (rep != null) rep.ContextBreakdown++; cb = null; }
            if (cb != null && cb.IsObject)
            {
                s.HasBreakdown = true;
                // ★ 看板第二批（2026-10-09 实测 ✓ 规格 §11.6-A）：ver2 三桶**扁平**在 val 顶层；ver4/ver5 **嵌套**在 val.breakdown —— 双读 ✓
                JNode bk = cb.Get("breakdown");
                JNode buckets = (bk != null && bk.IsObject) ? bk : cb;
                s.SystemTokens = Num(buckets, "systemTokens");
                s.ToolsTokens = Num(buckets, "toolsTokens");
                s.MessageTokens = Num(buckets, "messageTokens");
            }
            JNode lm = row.Path("sessionListMetadata", "val");
            if (RowMismatch(row, "sessionListMetadata", RowVerSessionListMetadata)) { if (rep != null) rep.SessionListMetadata++; lm = null; }
            if (lm != null && lm.IsObject)
            {
                s.Blank = lm.Get("blank") != null && lm.Get("blank").AsBool(false);
                s.LastPromptEpochMs = Num(lm, "lastPromptAt");
                s.LastPromptAt = s.LastPromptEpochMs > 0 ? EpochMsToIso(s.LastPromptEpochMs) : Str(lm, "lastPromptAt");
            }
            JNode title = row.Path("title", "val");
            if (RowMismatch(row, "title", RowVerTitle)) { if (rep != null) rep.Title++; title = null; }
            if (title != null && title.IsString) s.Title = title.StringValue;
            return s;
        }

        private static void FillIdentity(SessionStat s, JNode identity)
        {
            if (s == null || identity == null || !identity.IsObject) return;
            s.Cwd = Str(identity, "cwd");
            s.CreatedAtEpochMs = Num(identity, "createdAt");
            s.CreatedAt = s.CreatedAtEpochMs > 0 ? EpochMsToIso(s.CreatedAtEpochMs) : Str(identity, "createdAt");
        }

        /// <summary>epoch 毫秒 → UTC ISO-8601（纯函数：固定纪元，**不读时钟、不带本机时区**）。
        /// dsh 投影里的 `createdAt` / `lastPromptAt` 是 Int64 epoch 毫秒（本机实测）。</summary>
        public static string EpochMsToIso(long ms)
        {
            if (ms <= 0) return "";
            try
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms)
                    .ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return ""; }
        }

        private static long Num(JNode obj, string name)
        {
            JNode n = obj == null ? null : obj.Get(name);
            if (n == null) return 0;
            if (n.NodeKind == JNode.Kind.Number) return (long)n.NumberValue;
            if (n.NodeKind == JNode.Kind.String)
            {
                double d;
                if (double.TryParse(n.StringValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return (long)d;
            }
            return 0;
        }

        private static string Str(JNode obj, string name)
        {
            JNode n = obj == null ? null : obj.Get(name);
            return (n != null && n.IsString) ? n.StringValue : "";
        }
    }

    /// <summary>快照血缘段的解析结果（看板第三批 · 规格 §11.7-A.5 ✓✓ 纯数据 ✓）。
    /// Present=false ⇒ 血缘不可用（旧插件快照/段缺失/形状不认 ✓）⇒ 非 global 口径必须如实拒绝 ✗ 不猜 ✓。</summary>
    public sealed class SnapshotLineage
    {
        /// <summary>lineage 段存在且 sessions 是对象 ⇒ true（0 条边也是「可用」✓ 空血缘 ≠ 缺血缘 ✓）。</summary>
        public bool Present;
        /// <summary>桥侧首帧解码失败总数（&gt;0 ⇒ CLI 打 SESSAGG_HDRERR 诚实行 ✓ 缺字段 ⇒ 0 ✓）。</summary>
        public long Errors;
        /// <summary>血缘边集（key = 会话 id，已去 session- 前缀 ✓ 多父 ⇒ edge.Multiparent=true ✓）。</summary>
        public Dictionary<string, LineageEdge> Edges = new Dictionary<string, LineageEdge>(StringComparer.Ordinal);
    }
}
