using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>看板层级重整（2026-10-26 · 提案《看板优化与置顶悬浮窗》§3 五条）—— **纯呈现层**的纯函数：
    /// ① 状态胶囊 ② 今日用量进度 ③ 最近活动流 ④ 24 小时分布。
    /// <para>硬约束（逐条守）：</para>
    /// <para>· **不改任何数据口径** —— 窗口 / 三档 / 未知 / 估算 / 四桶全部沿用现有标记行与现有纯函数
    ///   （<see cref="BoardMetrics.SumOwnRows"/> 的同源口径 ✓ 本文件不新增标记行、不新增 CLI 命令 ✓）。</para>
    /// <para>· **不读时钟**（时刻由调用方传入 ⇒ 可测 ✓）、**不碰 IO** ✗。</para>
    /// <para>· **未知一律「未知」，绝不写成 0** ✗✓ —— token 未知 ⇒ TokKnown=false ⇒ 文案「用量未知」；
    ///   没有桥接 ⇒ 状态「未知（未装桥接）」**不许猜成停驻** ✗。</para>
    /// <para>· **数据最小化**：活动流与分布只带 id 短码 + 时间 + 用量 ✗ **绝不带会话标题 / 对话正文 / 工具参数** ✗。</para>
    /// <para>· **24 小时分布的口径**：token 与缓存命中率都是「**整会话累计，非该小时增量**」——
    ///   按会话**最后活动小时**整会话归属 ✓；逐时真值须解原始 zstd 日志（本工具拿不到）⇒ 一律带脚注 ✗ 不许声称是增量 ✓。</para>
    /// </summary>
    public static class BoardHierarchy
    {
        // ================================================================ ① 状态胶囊

        /// <summary>三种状态文案（✗ 不含 ✓/✗ 记号 ✗ 不用施工语气 ✓）。</summary>
        public const string StateActive = "在动";
        public const string StateParked = "停驻";
        public const string StateIdle = "无活动";
        public const string StateUnknown = "未知";

        /// <summary>状态来源标注（★ 提案 §3① 要求「状态来源必须标清」✓ 逐字）。</summary>
        public const string SrcBridge = "来自桥接快照 · 3 秒拍";
        /// <summary>没有桥接快照时的来源标注 —— **必须如实说未装桥接，不许猜成停驻** ✗✓。</summary>
        public const string SrcNoBridge = "未装桥接";
        /// <summary>桥接在、但没有 `active` 字段（旧版插件）⇒ 「在动」这项事实读不到 ✗ 不猜 ✓。</summary>
        public const string SrcOldBridge = "桥接快照没有「在动」字段（插件版本较旧）";
        /// <summary>看板数据本身不可用（`SESSIONS_FAIL` / 没取到数）。</summary>
        public const string SrcNoData = "看板数据不可用";

        /// <summary>状态胶囊取值（纯函数 ✓ 不读时钟 ✓ 未知绝不落成 0/停驻 ✓）。</summary>
        public sealed class Capsule
        {
            public string State = StateUnknown;
            public string Source = SrcNoData;
            /// <summary>状态是否**已知**（false ⇒ 界面只能说「未知」✗ 不许猜 ✗）。</summary>
            public bool Known;
            /// <summary>桥接快照里相邻两拍 seq 变过的会话数（真在动 ✓）。</summary>
            public int ActiveCount;
            /// <summary>还挂在 dsh 进程里的会话数（只有桥接快照才有这个事实 ✓）。</summary>
            public int LiveCount;
            /// <summary>会话总数（该快照的行数 ✓ 只是如实计数 ✗ 不当状态用 ✓）。</summary>
            public int Sessions;
        }

        /// <summary>状态判定（**三态 + 未知**，判据全部可观察 ✓）：
        /// ① 没有桥接快照（`SESSIONS_SOURCE` = disk / aggregate）⇒ **未知（未装桥接）** ✗ 不许猜成停驻 ✗；
        /// ② 有桥接快照但所有行的 `active` 都是 unknown（旧版插件）⇒ **未知**（「在动」读不到 ✓ 不猜 ✓）；
        /// ③ 有桥接快照且 `active` 可用 ⇒ 有 active=1 →「在动」· 否则有 live=1 →「停驻」· 否则「无活动」✓。
        /// 「无活动」只在**桥接在**的前提下才给（✗ 无桥接时给「无活动」= 假装知道 ✗）。</summary>
        public static Capsule CapsuleOf(SessionsSnapshot d)
        {
            Capsule c = new Capsule();
            if (d == null || !d.Ok) { c.State = StateUnknown; c.Source = SrcNoData; c.Known = false; return c; }
            c.Sessions = d.Rows.Count;
            if (!d.IsLiveSource) { c.State = StateUnknown; c.Source = SrcNoBridge; c.Known = false; return c; }
            int activeKnown = 0, active = 0, live = 0;
            for (int i = 0; i < d.Rows.Count; i++)
            {
                SessionRow r = d.Rows[i];
                if (r == null) continue;
                if (r.ActiveKnown) { activeKnown++; if (r.Active) active++; }
                if (r.Live) live++;
            }
            c.ActiveCount = active;
            c.LiveCount = live;
            if (activeKnown == 0)
            {
                c.State = StateUnknown; c.Source = SrcOldBridge; c.Known = false; return c;
            }
            c.Known = true;
            c.Source = SrcBridge;
            c.State = active > 0 ? StateActive : (live > 0 ? StateParked : StateIdle);
            return c;
        }

        /// <summary>胶囊主文案：`状态（来源）` —— 有桥接 ⇒「在动（来自桥接快照 · 3 秒拍）」✓；
        /// 无桥接 ⇒「未知（未装桥接）」✓（★ 提案 §3① 逐字口径 ✓）。</summary>
        public static string CapsuleStateLine(SessionsSnapshot d)
        {
            Capsule c = CapsuleOf(d);
            return c.State + "（" + c.Source + "）";
        }

        /// <summary>胶囊数据时刻：`数据时刻 HH:mm:ss`；时刻未知 ⇒ `数据时刻 未知`（✗ 绝不显示 00:00:00 ✓）。</summary>
        public static string CapsuleClockLine(DateTime? dataAt)
        {
            return "数据时刻 " + ClockText(dataAt);
        }

        /// <summary>★ 看板顶部那行**纯文字**的数据时刻（2026-10-26 收尾 (a) ✓✓）——
        /// 状态胶囊已按维护者意见拆掉 ✗，但它承担的**数据时刻不能丢** ✗ ⇒ 保留成一行纯文字 ✓。
        /// <para>与 <see cref="CapsuleClockLine"/> 是**同一份实现**（✗ 不许两套并存 ✗）：这里只是给它一个
        /// 不再叫"胶囊"的名字 ✓ 界面只调这一个 ✓（`Capsule*` 系列仍被 LogicTests 的三态诚实性断言引用 ✓）。</para></summary>
        public static string DataClockLine(DateTime? dataAt)
        {
            return CapsuleClockLine(dataAt);
        }

        // ================================================================ ★ 收尾 (a)(b) 的界面文案（唯一出处 ✓）

        /// <summary>★ 会话标题开关的**默认值**（2026-10-26 收尾 (b) ✓✓ 提案 §11 红线 A ✓）：
        /// 会话标题是**模型从对话内容派生**的 ✗ 属提示词派生数据 ✗ ⇒ **默认关** ✓。
        /// 界面初始化与 LogicTests 的"默认关"断言**共用这一个出处** ✓（改成 true ⇒ 断言立刻红 ✓）。</summary>
        public const bool TitleSwitchDefaultOn = false;

        /// <summary>★ 标题开关**开启时**的明示（收尾 (b) ✓ 提案 §11 红线 A ✓ 逐字含「标题可能由你的对话内容派生」✓）。
        /// 关着时不显示这句 ✓；开启时**必须**显示 ✗ 不许只改开关不提示 ✓。
        /// <para>★ 接线统一（2026-10-26）：这句话现在**只有一份**（<see cref="OverlayLight.TitleNotice"/> ✓）——
        /// 看板 Top 5 卡与悬边状态灯面板显示的是**同一句** ✗ 不许两处各写一份（写歪一句即红 ✓ 有断言 ✓）。</para></summary>
        public const string TitleNotice = OverlayLight.TitleNotice;

        /// <summary>默认态（开关关）的名称口径说明 —— 名称来自**路径** ✗ 不含对话内容 ✓。</summary>
        public const string NamePolicyText = "会话名默认是「项目（会话工作目录）名 · 短码」，来自路径，不含对话内容。";

        /// <summary>HH:mm:ss（未知 ⇒「未知」✗ 不用 0 顶替 ✓）；本地时间 ✓。</summary>
        public static string ClockText(DateTime? at)
        {
            if (!at.HasValue) return StateUnknown;
            return at.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>胶囊细节行（如实说明判据与计数 ✓ 未知时**只说未知的原因** ✗ 不给 0 ✓）。</summary>
        public static string CapsuleDetailLine(SessionsSnapshot d)
        {
            Capsule c = CapsuleOf(d);
            if (d == null || !d.Ok) return "读不到会话投影，运行状态无从判断。";
            if (!d.IsLiveSource) return "磁盘投影里没有「在动 / 停驻」这项事实；装了桥接插件才有。";
            if (!c.Known) return "桥接快照里没有「在动」字段，判不出在动还是停驻。";
            return "会话 " + c.Sessions + " 个 · 在动 " + c.ActiveCount + " · dsh 内挂着 " + c.LiveCount
                + "（在动 = 桥接相邻两拍之间 seq 变过；挂着 = 还在 dsh 里但没动）";
        }

        // ================================================================ ② 今日用量进度

        /// <summary>今日用量相对**用户自填预算**的进度（`sessions_budget_usd_per_day` ✓ 已有键 ✓）。
        /// ✗ 不预置任何阈值 ✗ —— 没填预算 ⇒ `HasBudget=false` ⇒ **不画进度条** ✓ 只显示数字 ✓。</summary>
        public sealed class Gauge
        {
            /// <summary>用户填了正数预算（✗ 0 / 空 / 非法都不算 ✓）。</summary>
            public bool HasBudget;
            /// <summary>可以画进度条（有预算 ✓ 且今日金额可算 ✓）。</summary>
            public bool DrawBar;
            /// <summary>已用百分比（**未钳**，>100 如实反映超预算 ✓；画条时由界面钳满 ✓）。</summary>
            public double Percent;
            /// <summary>真超限（CLI 的 `over=1` ✓ 只提示 ✗ 不阻止 ✓）。</summary>
            public bool Over;
            public string LimitText = "";
            public string SpentText = "";
            public decimal Spent;
            public bool HasSpent;
        }

        /// <summary>预算进度取值（纯函数 ✓ 金额一律取 CLI 原串 ✗ GUI 不重算计价 ✓）。</summary>
        public static Gauge GaugeOf(CostSnapshot cs)
        {
            Gauge g = new Gauge();
            CostBudget b = cs != null ? cs.Budget : null;
            if (b == null) return g;
            g.Over = b.Over;
            decimal lim = 0m;
            bool hasLim = b.HasLimit && CostMarkers.TryMoney(b.LimitText, out lim) && lim > 0m;
            g.HasBudget = hasLim;
            if (hasLim) g.LimitText = CostMarkers.Usd(b.LimitText);
            decimal sp = 0m;
            g.HasSpent = CostMarkers.TryMoney(b.SpentText, out sp);
            if (g.HasSpent) { g.Spent = sp; g.SpentText = CostMarkers.Usd(b.SpentText); }
            if (!hasLim) return g;                       // ✗ 没有预算 ⇒ 不画条 ✗ 也绝不自己造一个上限 ✓
            if (!g.HasSpent || sp < 0m) return g;        // 今日金额不可算 ⇒ 不画条（✗ 不假装 0 ✓）
            g.DrawBar = true;
            g.Percent = (double)(sp / lim) * 100.0;
            return g;
        }

        /// <summary>未设预算时的如实说明（✗ 不画进度条 ✓ 数字照显 ✓ ✗ 不造上限 ✓）。</summary>
        public const string NoBudgetText = "未设置每日预算，只显示数字（设置页可填）";
        /// <summary>进度条口径脚注（**必须点明「相对你自填的预算」** ✗ 不是我们预设的阈值 ✓）。</summary>
        public const string GaugeFootnote = "进度 = 今日估算金额 / 你自填的每日预算（sessions_budget_usd_per_day）；金额一律是估算，不是 DeepSeek 的官方账单。";

        /// <summary>今日金额行：有预算 ⇒「今日估算 $x / 每日预算 $y（USD）· 已用 n%」✓；
        /// 没预算 ⇒ 金额照显 + 如实说未设预算 ✓；金额不可算 ⇒ 如实说（✗ 不显示 0 ✓）。</summary>
        public static string GaugeMoneyLine(CostSnapshot cs)
        {
            CostBlock today = cs != null ? cs.Today : null;
            if (today == null) return CostMarkers.TodayUnavailable;
            Gauge g = GaugeOf(cs);
            if (!g.HasSpent) return "今日" + CostMarkers.NotComputableText;
            string spent = g.Spent == 0m
                ? CostMarkers.ZeroAmountText
                : "今日" + CostMarkers.EstimateLabel + " " + g.SpentText;   // g.SpentText 已是显示串（✗ 不二次 Usd ✓）
            if (!g.HasBudget) return spent + "（" + NoBudgetText + "）";
            return spent + " / 每日预算 " + g.LimitText + "（" + CostMarkers.CurrencyLabel + "）· 已用 " + PercentText(g)
                + (g.Over ? "（已超预算，仅提示，不阻止任何操作）" : "");
        }

        /// <summary>已用百分比文案（**极小非零绝不说成 0** ✓ —— 与金额的 `&lt;$0.000001` 同一条纪律 ✓）。</summary>
        public static string PercentText(Gauge g)
        {
            if (g == null || !g.DrawBar) return StateUnknown;
            double p = g.Percent;
            if (p < 0) return StateUnknown;
            if (p > 0 && p < 0.005) return "<0.01%";
            if (p >= 10) return p.ToString("0", CultureInfo.InvariantCulture) + "%";
            if (p >= 1) return p.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            return p.ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        // ================================================================ ③ 最近活动流 / ④ 24 小时分布

        /// <summary>一条会话活动（★ **只有 id 短码 + 时间 + 用量** ✗ 绝无标题 / 正文 / 工具参数 ✗）。
        /// 时间来自 SESSION 行（唯一带时间的标记 ✓）；用量来自**同一份输出**的 `SESSAGG_SESSION bucket=own`
        /// 行（`HasTokens` 才可信 ✓ 未知 ⇒ TokKnown=false ⇒ 文案「用量未知」✗ 不假装 0 ✓）。</summary>
        public sealed class ActivityRow
        {
            public string Id = "";
            public string ShortId = "";
            /// <summary>最后活动时间原文（ISO ✓ 空 = 投影里没有这项事实 ✓）。</summary>
            public string LastIso = "";
            /// <summary>本地时间（解析不了 ⇒ null ⇒ 时间显示「未知」✗ 不猜 ✓）。</summary>
            public DateTime? LastLocal;
            public bool TokKnown;
            /// <summary>整会话累计输入 token（= 未命中 + 缓存命中 ✓ 与总计卡同一字段 ✓）。</summary>
            public long In;
            /// <summary>缓存命中率（% ✓ &lt;0 = 未知 ✓）。</summary>
            public double HitPercent = -1;
        }

        /// <summary>活动行集合 = **eligible 集（`SESSAGG_SESSION bucket=own`）∩ 有 id 的 SESSION 行**。
        /// 两边都来自**同一次 CLI 取数** ⇒ 与看板原数字同源（差 0 ✓）；
        /// 只有 eligible 集进（= 与总计卡 / 三档表 / Top5 同一集合 ✓ 口径不变 ✓）。</summary>
        public static List<ActivityRow> ActivityRows(SessionsSnapshot d)
        {
            List<ActivityRow> list = new List<ActivityRow>();
            if (d == null || !d.Ok) return list;
            Dictionary<string, SessionRow> byId = new Dictionary<string, SessionRow>(StringComparer.Ordinal);
            for (int i = 0; i < d.Rows.Count; i++)
            {
                SessionRow r = d.Rows[i];
                if (r == null || string.IsNullOrEmpty(r.Id)) continue;
                if (!byId.ContainsKey(r.Id)) byId[r.Id] = r;   // 重复 id 先赢 ✓（与 CLI 同口径 ✓）
            }
            for (int i = 0; i < d.AggRows.Count; i++)
            {
                SessAggRow a = d.AggRows[i];
                if (a == null || a.Bucket != "own" || string.IsNullOrEmpty(a.Id)) continue;
                SessionRow r;
                if (!byId.TryGetValue(a.Id, out r) || r == null) continue;
                ActivityRow ar = new ActivityRow();
                ar.Id = a.Id;
                ar.ShortId = a.Id.Length > 8 ? a.Id.Substring(0, 8) : a.Id;
                ar.LastIso = r.Last;
                ar.LastLocal = ParseLocal(r.Last);
                ar.TokKnown = a.HasTokens;
                ar.In = a.HasTokens ? a.In : 0;
                ar.HitPercent = r.HitPercent;
                list.Add(ar);
            }
            return list;
        }

        /// <summary>ISO → **本地时间**（空 / 解析失败 ⇒ null ✗ 不猜 ✓；与图 3 / 热力图同一归日口径的同一读法 ✓）。</summary>
        public static DateTime? ParseLocal(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            DateTime t;
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out t)) return null;
            return t.ToLocalTime();
        }

        /// <summary>最近 N 条活动（时间降序 ✓ 时间未知的排最后 ✗ 不猜时间 ✓ 但也绝不丢弃 ✓；
        /// 并列按 id 字典序 ⇒ 两次运行同序 ✓ 确定性 ✓）。</summary>
        public static List<ActivityRow> RecentActivity(SessionsSnapshot d, int max)
        {
            List<ActivityRow> all = ActivityRows(d);
            all.Sort(delegate (ActivityRow x, ActivityRow y)
            {
                bool xk = x.LastLocal.HasValue, yk = y.LastLocal.HasValue;
                if (xk != yk) return xk ? -1 : 1;
                if (xk)
                {
                    int c = y.LastLocal.Value.CompareTo(x.LastLocal.Value);
                    if (c != 0) return c;
                }
                return string.CompareOrdinal(x.Id, y.Id);
            });
            if (max >= 0 && all.Count > max) all.RemoveRange(max, all.Count - max);
            return all;
        }

        /// <summary>活动流一行：`id 短码　时间　用量`（✗ 无标题 ✗ 无正文 ✗ 无工具参数 ✓）。
        /// 时间未知 ⇒「时间未知」✓；用量未知 ⇒「用量未知」✓（✗ 绝不显示 0 ✓）。</summary>
        public static string ActivityLine(ActivityRow r)
        {
            if (r == null) return "";
            string t = r.LastLocal.HasValue
                ? r.LastLocal.Value.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "时间未知";
            string tok = r.TokKnown ? (SessionRow.Human(r.In) + " token") : "用量未知";
            return r.ShortId + "　" + t + "　" + tok;
        }

        /// <summary>活动流口径脚注（数据最小化 ✓ 逐字）。</summary>
        public const string ActivityFootnote = "只列会话 id 短码、最后活动时间与用量（整会话累计）；不含会话标题、对话正文与工具参数。";

        /// <summary>24 小时分布（每小时一格 ✓ 本地小时 ✓）。</summary>
        public sealed class HourCell
        {
            public int Hour;
            /// <summary>该小时的**会话活动数**（按会话最后提问时间归属 ✓ 这是逐时真值 ✓）。</summary>
            public long Sessions;
            /// <summary>该小时里 token 未知的会话数（**未计入求和** ✗ 不假装 0 ✓）。</summary>
            public long TokUnknown;
            /// <summary>该小时里 token **已知**的会话数（用来区分「真 0」与「全未知」✗ 不混 ✓）。</summary>
            public long TokKnownCount;
            /// <summary>整会话累计输入 token 的归属和（★ **不是该小时增量** ✗ 见脚注 ✓）。</summary>
            public long In;
            /// <summary>命中率加权的分子 / 分母（按输入量加权 ✓ 与 KPI 同一加权口径 ✓）。</summary>
            public double HitNum;
            public double HitDen;
            public bool HasHit { get { return HitDen > 0; } }
            public double HitPct { get { return HitDen > 0 ? HitNum / HitDen : -1; } }
        }

        /// <summary>24 小时分布整体（含无法归属的会话计数 ✓ 如实列出 ✗ 不猜小时 ✓）。</summary>
        public sealed class Hourly
        {
            public HourCell[] Hours = new HourCell[24];
            /// <summary>投影里没有最后活动时间 ⇒ **无法归属到任何小时**（✗ 不猜 ✓）。</summary>
            public long NoTime;
            /// <summary>该窗口里 token 未知的会话总数（未计入任何小时求和 ✓）。</summary>
            public long TokUnknown;
            /// <summary>该窗口里 token 已知的会话总数（区分「真 0」与「全未知」✓）。</summary>
            public long TokKnownCount;
            /// <summary>会话活动合计（= Σ 每小时 ✓ 不含 NoTime ✓）。</summary>
            public long Sessions;
            /// <summary>整会话累计输入 token 合计（= Σ 每小时 ✓ 只加已知的 ✓）。</summary>
            public long In;
            /// <summary>有活动的小时数。</summary>
            public int ActiveHours;
        }

        /// <summary>按**本地小时**分桶（24 格 ✓ 缺的格恒为 0 —— 那是"该小时确实没有会话活动"这一**真值**，
        /// 而"用量未知"由 `TokUnknown` 单列，两者在文案上分得很清 ✗ 不混 ✓）。
        /// 数据不可用（`!Ok`）⇒ 全 0 ⇒ **界面层必须显示「未知」而不是这张表**（见 <see cref="HourlyUnknownText"/> ✓）。</summary>
        public static Hourly HourlyDistribution(SessionsSnapshot d)
        {
            Hourly h = new Hourly();
            for (int i = 0; i < 24; i++) h.Hours[i] = new HourCell { Hour = i };
            if (d == null || !d.Ok) return h;
            List<ActivityRow> rows = ActivityRows(d);
            for (int i = 0; i < rows.Count; i++)
            {
                ActivityRow r = rows[i];
                if (!r.LastLocal.HasValue) { h.NoTime++; continue; }   // ✗ 不猜小时 ✓
                HourCell c = h.Hours[r.LastLocal.Value.Hour];
                c.Sessions++;
                h.Sessions++;
                if (r.TokKnown) { c.In += r.In; h.In += r.In; c.TokKnownCount++; h.TokKnownCount++; }
                else { c.TokUnknown++; h.TokUnknown++; }
                if (r.HitPercent >= 0 && r.In > 0) { c.HitNum += r.HitPercent * r.In; c.HitDen += r.In; }
            }
            for (int i = 0; i < 24; i++) if (h.Hours[i].Sessions > 0) h.ActiveHours++;   // 有活动的小时**数**（✗ 不是会话数 ✓）
            return h;
        }

        /// <summary>数据不可用时的如实文案（✗ 绝不画一张全 0 的表 ✓ 也绝不显示 0 ✓）。</summary>
        public const string HourlyUnknownText = "会话活动分布：未知（读不到会话投影）";

        /// <summary>★ 24 小时分布的**唯一脚注**（提案 §3④ 硬要求 ✓ 逐字含「整会话累计，非该小时增量」✓✓）：
        /// token 与缓存命中率都是整会话累计、按最后活动小时归属 ✗ **不许声称是每小时真实增量** ✗
        /// （逐时真值须解原始 zstd 日志，本工具拿不到 ✓）。</summary>
        public const string HourlyFootnote = "整会话累计，非该小时增量（token 与缓存命中率都按会话最后活动小时整会话归属；逐时真值须解原始日志，本工具拿不到）。";

        /// <summary>★ 24 小时分布的**集合口径**脚注（2026-10-26 收尾 (c) ✓✓ 维护者点名：M1 自曝未在文案点明 ✗）：
        /// 这张表的集合 = **eligible 集**（投影里有 `bucket=own` token 行 ✓ 且同时有 `SESSION` 行 ✓）**再交**
        /// "有最后活动时间" ✓；而**图表页的热力图**只要求 `SESSION` 行（不要求有 token 行 ✓ 也不要求同一 eligible 集 ✓）。
        /// ⇒ 两者**口径不同** ✓ 必须逐字点明 ✗ 不许含糊 ✓（LogicTests 逐字断言 ✓）。
        /// ★ 首句是**逐字**指定句 ✓ 不许改写 ✗ 也不许与 <see cref="HourlyFootnote"/> 合并成一句含糊话 ✗；
        /// 括号里只把"差在哪"说清楚（本表 = eligible 集再交"有最后活动时间"；热力图 = 全部会话行 ✓）。</summary>
        public const string HourlyEligibleNote = "仅统计有最后活动时间的会话，与下方热力图口径不同（本表只收投影里既有该会话 token 行、又有会话行的 eligible 集；热力图按全部会话行归日，不要求有 token 行）。";

        /// <summary>分布标题（**写明是"会话活动数 + 整会话累计用量"** ✗ 不写"每小时用量" ✗）。</summary>
        public const string HourlyTitle = "近 24 小时会话活动分布（本地小时）";

        /// <summary>一行汇总（合计口径与脚注一致 ✓）。</summary>
        public static string HourlySummaryText(Hourly h)
        {
            if (h == null) return HourlyUnknownText;
            StringBuilder sb = new StringBuilder();
            sb.Append("有活动的小时 ").Append(h.ActiveHours).Append(" 个 · 会话活动合计 ").Append(h.Sessions)
              .Append(" 个 · 输入合计 ");
            if (h.TokKnownCount == 0 && h.TokUnknown > 0) sb.Append("用量未知");
            else sb.Append(SessionRow.Human(h.In)).Append(" token");
            sb.Append("（整会话累计）");
            if (h.TokUnknown > 0) sb.Append(" · ").Append(h.TokUnknown).Append(" 个会话用量未知（未计入）");
            if (h.NoTime > 0) sb.Append(" · ").Append(h.NoTime).Append(" 个会话没有最后活动时间（无法归属小时）");
            return sb.ToString();
        }

        /// <summary>峰值小时文案（✗ 不用"最多"以外的断言 ✓；没有活动 ⇒ 如实说没有 ✓）。</summary>
        public static string HourlyPeakText(Hourly h)
        {
            if (h == null) return HourlyUnknownText;
            int best = -1;
            for (int i = 0; i < 24; i++)
            {
                if (h.Hours[i].Sessions == 0) continue;
                if (best < 0 || h.Hours[i].Sessions > h.Hours[best].Sessions) best = i;
                else if (h.Hours[i].Sessions == h.Hours[best].Sessions && i < best) best = i;   // 并列取更早的小时 ✓ 确定性 ✓
            }
            if (best < 0) return "该窗口内没有可归属小时的会话活动。";
            HourCell c = h.Hours[best];
            return "活动最多的时段：" + Two(best) + " 时（会话 " + c.Sessions + " 个 · 输入 "
                + (c.TokKnownCount == 0 && c.TokUnknown > 0 ? "用量未知" : SessionRow.Human(c.In) + " token") + "）";
        }

        /// <summary>某一小时的**输入 token 单元格**（界面直接用它 ✓ 同一函数被 LogicTests 钉死 ✓）：
        /// 空小时 ⇒「—」（✗ 不显 0 免得与"未知"混淆 ✓）；该小时 token 全未知 ⇒「未知」✗ 绝不显 0 ✓；
        /// 部分未知 ⇒ 主值 + 「（另有 N 个未知）」如实标注 ✓。</summary>
        public static string HourTokenText(HourCell c)
        {
            if (c == null || c.Sessions == 0) return "—";
            if (c.TokKnownCount == 0 && c.TokUnknown > 0) return StateUnknown;
            return SessionRow.Human(c.In) + (c.TokUnknown > 0 ? "（另有 " + c.TokUnknown + " 个未知）" : "");
        }

        /// <summary>某一小时的**缓存命中率单元格**（同一整会话累计口径 ✓ 没有可加权输入 ⇒「未知」✗ 不显 0 ✓）。</summary>
        public static string HourHitText(HourCell c)
        {
            if (c == null || c.Sessions == 0) return "—";
            if (!c.HasHit) return StateUnknown;
            return c.HitPct.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>两位小时（表格/峰值用 ✓ InvariantCulture ✓）。</summary>
        public static string Two(int h)
        {
            return h.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
