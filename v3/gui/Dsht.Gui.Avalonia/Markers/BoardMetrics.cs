using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>看板指标页纯计算（2026-10-11 看板体验修复 ✓✓）：
    /// ① 三档对比表的窗口合计 ② 输入 Top5 排行 ③ 图表重画签名。
    /// 全部是**纯函数**：不碰 IO ✗ 不读时钟 ✗（today 由调用方传入 ✓ 可测 ✓ LogicTests 直接钉死 ✓）。
    /// 未知值一律不进求和（✗ 不假装 0 ✓），「未提供 / 未知」文案归界面层（这里只给事实）。</summary>
    public static class BoardMetrics
    {
        /// <summary>一档口径的窗口合计（三档对比表的一行 ✓）。</summary>
        public sealed class LevelSum
        {
            public long Sessions;      // SESSWIN_META eligible（与筛选条「窗口内 N 个」同口径 ✓）
            public long Uncached;      // Σ bucket=own 的 uncached（未命中缓存 ✓）
            public long CacheRead;     // Σ bucket=own 的 cacheRead（命中缓存 ✓）
            public long CacheWrite;    // Σ bucket=own 的 cacheWrite
            public long Output;        // Σ bucket=own 的 output
            public long Turns;         // Σ bucket=own 的 turns（只加 HasStats 的行 ✓）
            public long TokUnknown;    // token 未知的会话数（未计入求和 ✓ 如实计数 ✗ 不假装 0 ✓）
            /// <summary>输入合计 = 未命中 + 缓存命中（与 SESSIONS_TOTAL 的 in 同口径 ✓）。</summary>
            public long In { get { return Uncached + CacheRead; } }
        }

        /// <summary>从一份 sessions 快照求窗口合计 ✓ **口径与总计卡窗口块逐字一致**（Shells.Pages.Overview 同源 ✓✓）：
        /// 只加 bucket=own 且 HasTokens 的行（token 未知 → 不进求和 ✓ 计入 TokUnknown 如实标注 ✓）；
        /// turns 只加 HasStats 的行 ✓；sub 行绝不进（口径 = 本会话自身 ✗ 不重复计子代理 ✓）。</summary>
        public static LevelSum SumOwnRows(SessionsSnapshot d)
        {
            LevelSum s = new LevelSum();
            if (d == null) return s;
            s.Sessions = d.WinEligible;
            for (int i = 0; i < d.AggRows.Count; i++)
            {
                SessAggRow r = d.AggRows[i];
                if (r == null || r.Bucket != "own") continue;
                if (r.HasTokens)
                {
                    s.Uncached += r.Uncached;
                    s.CacheRead += r.CacheRead;
                    s.CacheWrite += r.CacheWrite;
                    s.Output += r.Output;
                }
                else s.TokUnknown++;
                if (r.HasStats) s.Turns += r.Turns;
            }
            return s;
        }

        /// <summary>输入 Top5 榜一行（会话名 / 输入 / 输出 / 轮次 ✓）。</summary>
        public sealed class TopRow
        {
            public string Id = "";
            /// <summary>**显示名** —— 默认 =「项目名 · 短码」（来自 cwd ✓ 路径派生 ✓）；
            /// 只有开关开启（且投影确实有标题）才是会话标题 ✓ 提案 §11 红线 A ✓。</summary>
            public string Title = "";
            public long In;
            public long Output;
            public long Turns;
        }

        /// <summary>输入 Top5：bucket=own 里「数值完整」（HasTokens && HasStats ✓ token 或轮次未知的不进榜 ✗ 不猜 ✓）
        /// 的会话，按输入（uncached+cacheRead ✓ 与 SESSIONS_TOTAL 同口径 ✓）降序取前 5；
        /// 并列按会话 id 字典序（Ordinal ✓ 两次运行同序 ✓ 确定性 ✓）。
        /// ★ 2026-10-26 收尾 (b) ✓✓：**默认显示「项目名 · 短码」**（来自 `SESSION` 行的 `cwd=` ✓ 路径派生 ✓
        /// ✗ 不是对话内容 ✗）；<paramref name="showTitle"/> = 用户显式打开标题开关（默认 false ✓）
        /// ⇒ 只有它为 true 且投影真有标题时才用标题 ✓（✗ 默认态绝不出现标题 ✗ LogicTests 有断言 ✓）。
        /// cwd 也拿不到 ⇒ 退化成短 id ✓（✗ 不空白 ✗ 不编 ✗）。</summary>
        public static List<TopRow> Top5ByInput(SessionsSnapshot d) { return Top5ByInput(d, BoardHierarchy.TitleSwitchDefaultOn); }

        /// <summary>带标题开关的重载（界面走这一个 ✓ 纯函数 ✓ 可测 ✓）。</summary>
        public static List<TopRow> Top5ByInput(SessionsSnapshot d, bool showTitle)
        {
            List<TopRow> all = new List<TopRow>();
            if (d == null) return all;
            Dictionary<string, SessionRow> byId = new Dictionary<string, SessionRow>(StringComparer.Ordinal);
            for (int i = 0; i < d.Rows.Count; i++)
                if (d.Rows[i] != null && d.Rows[i].Id != null && !byId.ContainsKey(d.Rows[i].Id)) byId[d.Rows[i].Id] = d.Rows[i];
            for (int i = 0; i < d.AggRows.Count; i++)
            {
                SessAggRow r = d.AggRows[i];
                if (r == null || r.Bucket != "own") continue;
                if (!r.HasTokens || !r.HasStats) continue;   // ✗ 数值不完整不进榜 ✗ 不猜 ✓（脚注如实说明 ✓）
                all.Add(new TopRow { Id = r.Id ?? "", In = r.Uncached + r.CacheRead, Output = r.Output, Turns = r.Turns });
            }
            all.Sort(delegate (TopRow x, TopRow y)
            {
                int c = y.In.CompareTo(x.In);   // 输入降序 ✓
                return c != 0 ? c : string.CompareOrdinal(x.Id, y.Id);   // 并列 → id 字典序（确定性 ✓）
            });
            if (all.Count > 5) all.RemoveRange(5, all.Count - 5);
            for (int i = 0; i < all.Count; i++)
            {
                SessionRow row;
                if (byId.TryGetValue(all[i].Id, out row) && row != null)
                {
                    all[i].Title = DisplayName(row, showTitle);
                    if (!string.IsNullOrEmpty(all[i].Title)) continue;
                }
                all[i].Title = ShortId(all[i].Id);   // 短 id 兜底 ✓
            }
            return all;
        }

        // ================================================================ ★ 榜单显示名（收尾 (b) ✓ 唯一出处）

        /// <summary>★ 榜单显示名（2026-10-26 收尾 (b) ✓✓）：**默认** =「项目（会话工作目录）名 · 短码」✓
        /// —— 来自 `cwd` ✓ **路径派生** ✗ 不是对话内容 ✗；只有 <paramref name="showTitle"/> 打开
        /// **且**投影里确实有标题时才返回标题 ✓（✗ 默认态绝不出现标题 ✗ 提案 §11 红线 A ✓）。
        /// cwd 取不到 ⇒ 只给短码 ✓（✗ 不空白 ✗ 不编一个项目名 ✗）。</summary>
        public static string DisplayName(SessionRow row, bool showTitle)
        {
            if (row == null) return "";
            if (showTitle && !string.IsNullOrEmpty(row.Title)) return row.Title;   // 显式开启 + 真有标题 ✓
            string proj = ProjectName(row.Cwd);
            string shortId = ShortId(row.Id);
            if (proj.Length == 0) return shortId;                                  // 没有 cwd ⇒ 只给短码 ✓
            if (shortId.Length == 0) return proj;
            return proj + " · " + shortId;
        }

        /// <summary>项目名 = 会话工作目录的**末段**（两种分隔符都认 ✓；只有盘符 / 空 ⇒ 空串 ✗ 不编 ✓）。</summary>
        public static string ProjectName(string cwd)
        {
            if (string.IsNullOrEmpty(cwd)) return "";
            string t = cwd.Trim().TrimEnd('/', '\\');       // 末尾分隔符不算一段 ✓
            if (t.Length == 0) return "";
            int i = t.LastIndexOfAny(new char[] { '/', '\\' });
            string seg = i >= 0 ? t.Substring(i + 1) : t;
            if (seg.Length == 0) return "";                  // 只有盘符（`C:\`）⇒ 不编名字 ✓
            if (seg.Length == 2 && seg[1] == ':') return "";  // `C:` ⇒ 同上 ✓
            return seg;
        }

        /// <summary>会话短码 = id 前 8 位（不足 8 位就原样 ✓ ✗ 不补零 ✗ 不截断成空 ✓）。</summary>
        public static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            return id.Length > 8 ? id.Substring(0, 8) : id;
        }

        /// <summary>图表区重画签名（看板体验修复 ✓✓ **图表只在输入真的变了才重画** ✓✓）。
        /// 覆盖图表全部输入（宁可多收 ✓ 漏收 = 该重画没重画 ✗✗）：
        ///   · SESSION 行（图①新建=Created ✓ 图②命中率分布=HitPercent ✓ 图③活跃量=In ✓ 热力图=Last ✓ 解析序逐行 ✓）
        ///   · eligible 集（AggRows 的 id，排序后入签 ✓ winFilter 的输入 ✓）
        ///   · 窗口（WinDays/WinFrom/WinTo ✓）与 chartDays（图表范围档 ✓）
        ///   · TimeRows / CtxRows 全字段（耗时图 / Token 分类图输入 ✓）
        ///   · 体检 Ok/Pass/Warn/Error（体检分布图输入 ✓）
        ///   · today（按日分桶的「今天」✓ 跨天必须重画 ✓ **本函数不读时钟**，today 由调用方传 DateTime.Now.Date ✓ 可测 ✓）。
        /// null 快照 → 固定串 ✗ 不抛 ✓；失败快照把 FailReason 带进签名（失败原因变 = 该重画 ✓）。</summary>
        public static string ChartSignature(SessionsSnapshot d, int chartDays, DoctorSummary doc, DateTime today)
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append("v1|days=").Append(chartDays)
              .Append("|today=").Append(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sb.Append("|doc=");
            if (doc == null) sb.Append("null");
            else sb.Append(doc.Ok ? '1' : '0').Append(',').Append(doc.Pass).Append(',').Append(doc.Warn).Append(',').Append(doc.Error);
            if (d == null || !d.Ok)
            {
                sb.Append("|data=").Append(d == null ? "null" : "fail:" + (d.FailReason ?? ""));
                return sb.ToString();
            }
            sb.Append("|win=").Append(d.WinDays).Append(',').Append(d.WinFrom).Append(',').Append(d.WinTo);
            List<string> ids = new List<string>();
            for (int i = 0; i < d.AggRows.Count; i++)
                if (d.AggRows[i] != null && d.AggRows[i].Id != null) ids.Add(d.AggRows[i].Id);
            ids.Sort(StringComparer.Ordinal);
            sb.Append("|elig=").Append(string.Join(",", ids.ToArray()));
            for (int i = 0; i < d.Rows.Count; i++)
            {
                SessionRow r = d.Rows[i];
                if (r == null) { sb.Append("|row=null"); continue; }
                sb.Append("|row=").Append(r.Id).Append(',').Append(r.Created).Append(',').Append(r.Last)
                  .Append(',').Append(r.In).Append(',').Append(r.HitPercent.ToString("R", CultureInfo.InvariantCulture));
            }
            for (int i = 0; i < d.TimeRows.Count; i++)
            {
                SessTimeRow r = d.TimeRows[i];
                if (r == null) continue;
                sb.Append("|tm=").Append(r.Id).Append(',').Append(r.Has ? '1' : '0')
                  .Append(',').Append(r.LlmMs).Append(',').Append(r.ToolMs).Append(',').Append(r.TtftMs).Append(',').Append(r.DecodeMs);
            }
            for (int i = 0; i < d.CtxRows.Count; i++)
            {
                SessCtxRow r = d.CtxRows[i];
                if (r == null) continue;
                sb.Append("|cx=").Append(r.Id).Append(',').Append(r.Has ? '1' : '0')
                  .Append(',').Append(r.System).Append(',').Append(r.Tools).Append(',').Append(r.Message);
            }
            return sb.ToString();
        }
    }
}
