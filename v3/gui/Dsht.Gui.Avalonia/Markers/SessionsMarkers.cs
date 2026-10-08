using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>一个会话行（来自 CLI `sessions` 的 `SESSION` 标记行）。缺失/未知的数值用 -1 表示，**不假装 0**。</summary>
    public sealed class SessionRow
    {
        public string Id = "";
        public string Title = "";          // dsh 自己生成的会话标题（可能为空）
        public string Created = "";        // "unknown" → 空串
        public string Last = "";           // 同上
        public long Turns;
        public long Steps;
        public long In;                    // 输入侧总量（未命中缓存 + 命中缓存）
        public long Out;
        public long CacheRead;
        public double HitPercent = -1;     // 缓存命中率（%）
        public double DecodeTps = -1;      // 解码速度（tok/s）
        public long TtftMs = -1;           // 首 token（dsh 投影原值，多步累计；未知 = -1）
        public double CtxPercent = -1;     // 上下文压力（%）
        public bool Blank;
        /// <summary>父会话 id（空 = 它是根会话 ✓）。来自 CLI 的 `SESSION_CHILD <父> <子>` ✓✓
        /// —— dsh 把子会话 id 记在父会话文件的 "childId" 字段里 ✓（2026-09-30 实测 98 组 ✓）</summary>
        public string ParentId = "";
        /// <summary>它的子会话 id 列表（子代理 ✓）。</summary>
        public List<string> ChildIds = new List<string>();
        public bool IsSubAgent { get { return ParentId.Length > 0; } }
        public bool Live;
        /// <summary>是否**知道**运行态（只有桥接插件快照才提供 live；磁盘投影没有这个事实）。</summary>
        public bool LiveKnown;
        // ★ 2026-10-02：`live` ≠ "在动"（桌面端开着时 store 里**全是 live** ✗ 用户实测"没在运行显示 运行6" ✓）
        //   `active` = 插件相邻两拍之间 seq 变过 = 真在动 ✓；存在性判定（unknown ≠ false ✓）
        public bool Active;
        public bool ActiveKnown;
        public string LastActive = "";

        /// <summary>相对最大输入量的条形长度（0–100，由 SessionsView.AttachBars 计算）</summary>
        public int TokenBar;

        public string ShortId { get { return Id.Length > 8 ? Id.Substring(0, 8) : Id; } }

        /// <summary>标题（空则给一个人话兜底，不留空白让人猜）。</summary>
        public string TitleText { get { return string.IsNullOrEmpty(Title) ? "（未命名会话）" : Title; } }

        /// <summary>状态语义：0=运行中(dsh 内) 1=已结束 2=空会话（颜色由界面层决定）。</summary>
        public int StatusKind { get { return Live ? 0 : (Blank ? 2 : 1); } }

        /// <summary>★ 2026-10-02 诚实化：`live` 只表示"还挂在 dsh 进程里"✗ 不是"在跑"✗✓
        /// 桌面端开着 → 它 store 里的会话全是 live（哪怕几天没碰 ✓）→ 原来一律显示"运行中"是**过度断言** ✗。
        /// 三档判据（越靠前越精确 ✓）：
        ///   ① active 已知（插件 0.2.0）→ 运行中 / 挂着（dsh 内未动）✓✓
        ///   ② active 未知（插件还是 0.1.0 ✓ **绝大多数现有用户**）→ 用**最后活动时间**兜底，
        ///      且**明说依据**（"按最后活动判断" ✓）✗ 不冒充精确 ✗
        ///   ③ 连最后活动也没有 → "活动未知" ✗ 不猜 ✗</summary>
        private const int RecentMinutes = 15;   // 15 分钟内有活动 → 按"在跑"判断 ✓（长生成中途 lastPromptAt 不更新 ✗ 所以给得宽 ✓）

        public string LiveText
        {
            get
            {
                if (!LiveKnown) return Blank ? "空会话" : "运行态未知";
                if (!Live) return Blank ? "空会话" : "已结束";
                if (ActiveKnown) return Active ? "运行中" : "挂着（dsh 内未动）";
                int age = LastAgeMinutes;
                if (age >= 0 && age <= RecentMinutes) return "运行中（按最后活动判断）";
                if (age >= 0) return "挂着 · 最后活动 " + LastAgeText;
                return "dsh 内挂着（活动未知）";
            }
        }

        /// <summary>最后活动距今多少分钟（-1 = 没有该字段 / 解析不了 → **未知** ✗ 不猜 ✗）。</summary>
        public int LastAgeMinutes
        {
            get
            {
                if (string.IsNullOrEmpty(Last)) return -1;
                DateTime t;
                if (!DateTime.TryParse(Last, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return -1;
                double m = (DateTime.UtcNow - t).TotalMinutes;
                return m < 0 ? 0 : (int)m;   // 时钟回拨 → 当作"刚刚" ✓（绝不显示负数 ✗）
            }
        }

        /// <summary>人话相对时间（"5 分钟前" / "3 小时前" / "2 天前"）；取不到 → 空串 ✓。</summary>
        public string LastAgeText
        {
            get
            {
                int m = LastAgeMinutes;
                if (m < 0) return "";
                if (m < 60) return m + " 分钟前";
                if (m < 60 * 24) return (m / 60) + " 小时前";
                return (m / (60 * 24)) + " 天前";
            }
        }

        public string HitText { get { return HitPercent < 0 ? "unknown" : HitPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%"; } }
        public string DecodeText { get { return DecodeTps < 0 ? "unknown" : DecodeTps.ToString("0.0", CultureInfo.InvariantCulture) + " tok/s"; } }
        public string CtxText { get { return CtxPercent < 0 ? "unknown" : CtxPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%"; } }

        /// <summary>首 token：超过 1 秒改用秒显示（dsh 投影里是多步累计的毫秒数，这里只换算单位，不改语义）。</summary>
        public string TtftText
        {
            get
            {
                if (TtftMs < 0) return "unknown";
                if (TtftMs < 1000) return TtftMs.ToString(CultureInfo.InvariantCulture) + " ms";
                return (TtftMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s";
            }
        }

        public string InText { get { return Human(In); } }
        public string OutText { get { return Human(Out); } }
        public string CacheReadText { get { return Human(CacheRead); } }
        public string TurnsText { get { return Turns + " 轮 / " + Steps + " 步"; } }

        public string LastShort { get { return ShortTime(Last); } }
        public string CreatedShort { get { return ShortTime(Created); } }

        /// <summary>缓存命中率等级：0=未知 1=低 2=中 3=高（越高越省钱）。</summary>
        public int HitLevel { get { return Level(HitPercent, 70, 90, true); } }

        /// <summary>上下文压力等级：0=未知 1=低 2=中 3=高（越低越安全）。</summary>
        public int CtxLevel { get { return Level(CtxPercent, 50, 80, false); } }

        public int HitBar { get { return Clamp(HitPercent); } }
        public int CtxBar { get { return Clamp(CtxPercent); } }

        /// <summary>人读数字：1.2K / 45.6M / 1.2B（固定 InvariantCulture）。</summary>
        public static string Human(long v)
        {
            if (v < 0) return "unknown";
            double d = v;
            if (v < 1000) return v.ToString(CultureInfo.InvariantCulture);
            if (v < 1000000) return (d / 1000).ToString("0.0", CultureInfo.InvariantCulture) + "K";
            if (v < 1000000000) return (d / 1000000).ToString("0.0", CultureInfo.InvariantCulture) + "M";
            return (d / 1000000000).ToString("0.0", CultureInfo.InvariantCulture) + "B";
        }

        private static int Clamp(double v)
        {
            if (v < 0) return 0;
            if (v > 100) return 100;
            return (int)Math.Round(v);
        }

        private static int Level(double v, double low, double high, bool higherIsBetter)
        {
            if (v < 0) return 0;
            if (higherIsBetter)
            {
                if (v >= high) return 3;
                if (v >= low) return 2;
                return 1;
            }
            if (v >= high) return 3;
            if (v >= low) return 2;
            return 1;
        }

        /// <summary>"2026-09-23T01:48:28Z" → "09-23 01:48"（人眼扫读；解析不了就原样返回）。</summary>
        private static string ShortTime(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "unknown";
            if (iso.Length >= 16 && iso[10] == 'T') return iso.Substring(5, 5) + " " + iso.Substring(11, 5);
            return iso;
        }
    }

    /// <summary>SESSAGG_SESSION 行（看板第一批 · 2026-10-08 ✓✓ 规格 §11.1：bucket=own 唯一实现 ✓
    /// 未知字段 → HasX=false ✗ 绝不假装 0 ✓；children/depth 第一批恒 unknown → 不建模 ✓ 血缘后移第二批 ✓）。</summary>
    public sealed class SessAggRow
    {
        public string Id = "";
        public string Bucket = "";
        public long Turns;
        public long Steps;
        public long Uncached;
        public long CacheRead;
        public long CacheWrite;
        public long Output;
        public bool HasStats;   // turns/steps 任一 unknown → false ✓
        public bool HasTokens;  // 四个 token 位任一 unknown → false ✓
        /// <summary>这批会话的输入合计（uncached + cacheRead ✓ 与 SESSIONS_TOTAL 的 in 同口径 ✓）。</summary>
        public long In { get { return Uncached + CacheRead; } }
    }

    /// <summary>SESSTIME_SESSION 行（看板第二批 · 2026-10-09 ✓✓ 规格 §11.6-B：逐会话计时四件 ✓
    /// 任一位 unknown → Has=false ✗ 绝不假装 0（零值是真实值 ✓ 照常进图 ✓））。</summary>
    public sealed class SessTimeRow
    {
        public string Id = "";
        public long LlmMs;
        public long ToolMs;
        public long TtftMs;
        public long DecodeMs;
        public bool Has;    // 四位任一 unknown → false ✓
    }

    /// <summary>SESSCTX_SESSION 行（看板第二批 · 2026-10-09 ✓✓ 规格 §11.6-B：上下文构成三桶 ✓
    /// messageTokens **一桶不拆**（决策 D3 ✗✓）；任一位 unknown → Has=false ✗ 绝不假装 0 ✓）。</summary>
    public sealed class SessCtxRow
    {
        public string Id = "";
        public long System;
        public long Tools;
        public long Message;
        public bool Has;    // 三位任一 unknown → false ✓
    }

    /// <summary>`sessions` 命令的完整结果（含汇总与数据来源）。</summary>
    public sealed class SessionsSnapshot
    {
        public bool Ok;
        public string FailReason = "";
        public int Count;
        public int NonBlank;
        public int Live;
        public string Source = "";         // snapshot | disk | aggregate
        public string Root = "";
        public long TotalIn;
        public long TotalOut;
        public long TotalCacheRead;
        public double TotalHitPercent = -1;
        public double TotalDecodeTps = -1;
        public List<SessionRow> Rows = new List<SessionRow>();
        /// <summary>子代理（有父会话的）数量 ✓；RootCount = 其余（根会话/普通会话 ✓）。</summary>
        public int SubAgentCount;
        public int RootCount;

        // —— 看板第一批（2026-10-08 ✓✓ 规格 §11.1 ✓ truth=0 ✓✓）——
        // SESSWIN_META：窗口元信息（HasWinMeta=false → 老 CLI 没打这行 → GUI 走「不过滤」回退 ✓）
        public int WinDays = -1;            // -1 = 总计（CLI 打 unknown ✓ GUI 内存哨兵 ✓）
        public string WinFrom = "-";
        public string WinTo = "-";
        public string WinTz = "";
        public bool WinTruth0;              // 第一批恒 true ✓（→ C.4 脚注两处常显 ✓）
        public int WinScanned;
        public int WinEligible;
        public int WinUnknownLast;
        public bool HasWinMeta;
        // SESSAGG_TOTAL scope=store：**无视窗口过滤**的固定总计卡 ✓
        public long AggSessions;
        public long AggNonBlank;
        public long AggUncached;
        public long AggCacheRead;
        public long AggCacheWrite;
        public long AggOutput;
        public string AggFirstDay = "";     // 本地日 yyyy-MM-dd；CLI 全无数据时打 unknown → 这里留 "unknown" 原文 ✓
        public string AggLastDay = "";
        public bool HasAggTotal;
        // SESSAGG_SESSION（bucket=own ✓ 只含 eligible ✓ 窗口卡数据源 + KPI/图表过滤基准 ✓）
        public List<SessAggRow> AggRows = new List<SessAggRow>();
        // —— 看板第二批（2026-10-09 ✓✓ 规格 §11.6-B ✓）：与 SESSAGG_SESSION 同一 eligible 集 ✓
        //   旧 CLI 没有这两行 → 列表为空 → 对应卡片如实显示「当前数据源未提供」✗ 不画空图 ✗ 不猜 ✓ ——
        /// <summary>SESSTIME_SESSION 行集（耗时分解卡数据源）。</summary>
        public List<SessTimeRow> TimeRows = new List<SessTimeRow>();
        /// <summary>SESSCTX_SESSION 行集（Token 分类卡数据源）。</summary>
        public List<SessCtxRow> CtxRows = new List<SessCtxRow>();

        /// <summary>窗口内会话 id 集（SESSAGG_SESSION 行就是 eligible 集合 ✓ 规格 §4.1 ✓）。
        /// HasWinMeta=false（老 CLI）→ 返回 null → 调用方走「不过滤」回退 ✓✓</summary>
        public HashSet<string> AggIdSet()
        {
            if (!HasWinMeta) return null;
            HashSet<string> h = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < AggRows.Count; i++) if (AggRows[i] != null && AggRows[i].Id != null) h.Add(AggRows[i].Id);
            return h;
        }

        /// <summary>数据来源的中文说明（给界面用；插件缺失时如实说明少了什么）。</summary>
        public string SourceText
        {
            get
            {
                // ★★★ **真机 GUI 冒烟抓到的回归（截图里写着「数据来源：未知」）** ✓✓
                //   ✗ CLI 的合并修复引入了新值 **`snapshot+disk`**（快照 + 磁盘补齐 ✓）✗
                //     → 而这里只认 snapshot / disk / aggregate ✗ → **落到"未知"** ✗✗
                //     → 它恰好是**最常见的真实情况**（dsh 在跑 + 有历史会话 ✓）
                //   ✓ 现在：**如实说明两者都在用** ✓✓（并说清历史来自磁盘 ✓）
                if (Source == "snapshot+disk") return "数据来源：桥接插件快照（实时）+ 磁盘投影（历史）";
                if (Source == "snapshot") return "数据来源：桥接插件快照（含实时「运行中」标记）";
                if (Source == "disk") return "数据来源：磁盘投影（未装桥接插件 —— 因此没有「正在运行」这一项）";
                if (Source == "aggregate") return "数据来源：投影总表（每会话投影文件缺失时的兜底）";
                return "数据来源：未知（CLI 报告的是「" + (Source ?? "") + "」）";
            }
        }

        public bool IsLiveSource { get { return Source == "snapshot" || Source == "snapshot+disk"; } }
    }

    /// <summary>列表的过滤与排序（纯函数，供界面调用；也可单测）。</summary>
    public static class SessionsView
    {
        public const int FilterAll = 0;
        public const int FilterNonBlank = 1;
        public const int FilterLive = 2;

        public static List<SessionRow> Filter(List<SessionRow> rows, int mode)
        {
            List<SessionRow> r = new List<SessionRow>();
            if (rows == null) return r;
            for (int i = 0; i < rows.Count; i++)
            {
                SessionRow s = rows[i];
                if (s == null) continue;
                if (mode == FilterNonBlank && s.Blank) continue;
                if (mode == FilterLive && !s.Live) continue;
                r.Add(s);
            }
            return r;
        }

        /// <summary>排序：0=最后活动（新→旧） 1=输入 token（多→少） 2=缓存命中率（低→高，最该看的排前面） 3=上下文压力（高→低） 4=解码速度（快→慢）。</summary>
        public static List<SessionRow> Sort(List<SessionRow> rows, int mode)
        {
            List<SessionRow> r = rows == null ? new List<SessionRow>() : new List<SessionRow>(rows);
            r.Sort(delegate(SessionRow a, SessionRow b)
            {
                int c;
                switch (mode)
                {
                    case 1: c = b.In.CompareTo(a.In); break;
                    case 2: c = Rank(a.HitPercent).CompareTo(Rank(b.HitPercent)); break;
                    case 3: c = b.CtxPercent.CompareTo(a.CtxPercent); break;
                    case 4: c = RankDesc(b.DecodeTps).CompareTo(RankDesc(a.DecodeTps)); break;
                    default: c = string.CompareOrdinal(b.Last, a.Last); break;
                }
                if (c != 0) return c;
                return string.CompareOrdinal(a.Id, b.Id);
            });
            return r;
        }

        /// <summary>未知值（-1）排在最后。</summary>
        private static double Rank(double v) { return v < 0 ? double.MaxValue : v; }

        /// <summary>降序时未知值（-1）排在最后。</summary>
        private static double RankDesc(double v) { return v < 0 ? double.MinValue : v; }

        /// <summary>给每行算出相对最大输入量的条形长度（0–100），用于横向对比。</summary>
        public static void AttachBars(List<SessionRow> rows)
        {
            if (rows == null) return;
            long max = 0;
            for (int i = 0; i < rows.Count; i++) if (rows[i] != null && rows[i].In > max) max = rows[i].In;
            for (int i = 0; i < rows.Count; i++)
            {
                SessionRow s = rows[i];
                if (s == null) continue;
                s.TokenBar = max <= 0 ? 0 : (int)Math.Round(s.In * 100.0 / max);
                if (s.TokenBar < 2 && s.In > 0) s.TokenBar = 2;
            }
        }

        // —— 看板第二批（2026-10-09 ✓✓ 规格 §11.6 ✓ 纯函数 ✓ LogicTests 可断言 ✓✓）——

        /// <summary>耗时分解四桶合计（等首 token / 流式解码 / llm 其他 / 工具执行；只计入 Has 行 ✓）。
        /// llm 其他 = llmMs − ttftMs − decodeMs（dsh 源码恒等式：ttft/decode 是 llmMs 的子集 ⇒ ≥ 0 ✓ 勘察 §5 ✓；
        /// 防御性钳 0 —— 真出现负数说明口径被上游改了，钳 0 并把该行按原值计前三桶，✗ 不反向伪造 ✓）。
        /// 返回 long[4]；known = 计入的会话数。</summary>
        public static long[] TimeSplit(List<SessTimeRow> rows, out int known)
        {
            long[] t = new long[4];
            known = 0;
            if (rows == null) return t;
            for (int i = 0; i < rows.Count; i++)
            {
                SessTimeRow r = rows[i];
                if (r == null || !r.Has) continue;
                known++;
                t[0] += r.TtftMs;
                t[1] += r.DecodeMs;
                long other = r.LlmMs - r.TtftMs - r.DecodeMs;
                t[2] += other > 0 ? other : 0;
                t[3] += r.ToolMs;
            }
            return t;
        }

        /// <summary>上下文构成三桶合计（system / tools / message **一桶不拆** D3 ✓；只计入 Has 行 ✓）。
        /// 返回 long[3]；known = 计入的会话数。</summary>
        public static long[] CtxTotals(List<SessCtxRow> rows, out int known)
        {
            long[] t = new long[3];
            known = 0;
            if (rows == null) return t;
            for (int i = 0; i < rows.Count; i++)
            {
                SessCtxRow r = rows[i];
                if (r == null || !r.Has) continue;
                known++;
                t[0] += r.System;
                t[1] += r.Tools;
                t[2] += r.Message;
            }
            return t;
        }

        /// <summary>ISO 时间戳 → **本地日** yyyy-MM-dd（空/解析失败 → null ✗ 不猜日期 ✓ 与图 3 同一归日口径 ✓）。</summary>
        public static string LocalDayOfIso(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            System.DateTime dt;
            if (!System.DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out dt)) return null;
            return dt.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>解析 CLI `sessions` 的标记行（纯函数，**绝不抛**：无法解析的行跳过）。
    /// 契约见 `v3/README.md` 的命令表；这里的解析器与 CLI 的打印格式是同一份约定的两端。</summary>
    public static class SessionsMarkers
    {
        /// <summary>C.4 脚注（规格 §4.2 :342 ✓ **一字不许改** ✓ LogicTests 逐字符断言 ✓）——
        /// truth=0 期间：筛选条下方 + 窗口总计卡内**两处常显** ✓✓ 此文本是唯一能改它的地方。</summary>
        public const string TruthFootnote = "本口径按会话最后活动时间筛选，用量为整会话累计，非窗口内增量。";

        // —— 看板第二批（2026-10-09 ✓✓ 规格 §11.6-C ✓ **一字不许改** ✓ LogicTests 逐字符断言 ✓✓）——
        /// <summary>耗时分解卡：标题。</summary>
        public const string TimeTitle = "耗时分解（窗口内会话 · 整会话累计）";
        /// <summary>耗时分解卡：口径脚注（C.4 之外**追加**的口径说明 ✓ 不是替代 ✓）。</summary>
        public const string TimeFootnote = "llmMs = 等首 token + 流式解码 + llm 其他（dsh 源码口径：ttft/decode 是 llmMs 的子集）；工具执行与 llmMs 不相交。缺 sessionStats 的会话不计入，不猜。";
        /// <summary>Token 分类卡：标题。</summary>
        public const string CtxTitle = "上下文构成 · Token 分类（窗口内会话 · 当前快照合计）";
        /// <summary>Token 分类卡：口径脚注（启发式折算必标「估算」✓ D3 不拆 ✓）。</summary>
        public const string CtxFootnote = "上下文构成是 dsh 启发式折算（估算）口径（4 字符 ≈ 1 token），取各会话当前上下文快照，非提供方计费数字，也非整会话累计；messageTokens 一桶不拆（决策 D3）。缺 contextBreakdown 的会话不计入，不猜。";
        /// <summary>热力图：标题固定模板（{0} = ChartDays）。</summary>
        public const string HeatTitle = "近 {0} 天会话最后活动日 · 会话数（本地日）";
        /// <summary>热力图：脚注固定模板（{0} = 缺 lastPromptAt 的会话数）。</summary>
        public const string HeatFootnote = "按会话最后活动时间归本地日；投影只含最后活动时间，这不是逐日活跃轨迹（逐日真值须读原始日志 → 第三批）；缺失 {0} 个不计入，不猜。";

        public static SessionsSnapshot Parse(string output)
        {
            SessionsSnapshot s = new SessionsSnapshot();
            if (string.IsNullOrEmpty(output)) return s;
            Dictionary<string, string> links = new Dictionary<string, string>();   // 子 id → 父 id ✓
            string[] lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? "" : lines[i].Trim();
                if (line.Length == 0) continue;
                try
                {
                    if (line.StartsWith("SESSIONS_FAIL", StringComparison.Ordinal))
                    {
                        s.FailReason = line.Substring("SESSIONS_FAIL".Length).Trim();
                        continue;
                    }
                    if (line.StartsWith("SESSIONS_OK", StringComparison.Ordinal)) { s.Count = (int)Num(Tail(line, "SESSIONS_OK"), 0); s.Ok = true; continue; }
                    if (line.StartsWith("SESSIONS_NONBLANK", StringComparison.Ordinal)) { s.NonBlank = (int)Num(Tail(line, "SESSIONS_NONBLANK"), 0); continue; }
                    if (line.StartsWith("SESSIONS_LIVE", StringComparison.Ordinal)) { s.Live = (int)Num(Tail(line, "SESSIONS_LIVE"), 0); continue; }
                    if (line.StartsWith("SESSIONS_SOURCE", StringComparison.Ordinal)) { s.Source = Tail(line, "SESSIONS_SOURCE").Trim(); continue; }
                    if (line.StartsWith("SESSIONS_ROOT", StringComparison.Ordinal)) { s.Root = Tail(line, "SESSIONS_ROOT").Trim(); continue; }
                    if (line.StartsWith("SESSIONS_TOTAL", StringComparison.Ordinal))
                    {
                        Dictionary<string, string> kv = Pairs(Tail(line, "SESSIONS_TOTAL"));
                        s.TotalIn = Num(Get(kv, "in"), 0);
                        s.TotalOut = Num(Get(kv, "out"), 0);
                        s.TotalCacheRead = Num(Get(kv, "cacheRead"), 0);
                        s.TotalHitPercent = Pct(Get(kv, "hit"));
                        s.TotalDecodeTps = Pct(Get(kv, "decode"));
                        continue;
                    }
                    // —— 看板第一批（2026-10-08 ✓✓ 规格 §11.1 ✓）——
                    if (line.StartsWith("SESSWIN_META ", StringComparison.Ordinal))
                    {
                        Dictionary<string, string> kv = Pairs(Tail(line, "SESSWIN_META"));
                        string d = Get(kv, "days");
                        s.WinDays = (d == null || d == "unknown") ? -1 : (int)Num(d, -1);
                        s.WinFrom = Get(kv, "from") ?? "-";
                        s.WinTo = Get(kv, "to") ?? "-";
                        s.WinTz = Decode(Get(kv, "tz") ?? "");
                        s.WinTruth0 = Get(kv, "truth") == "0";
                        s.WinScanned = (int)Num(Get(kv, "scanned"), 0);
                        s.WinEligible = (int)Num(Get(kv, "eligible"), 0);
                        s.WinUnknownLast = (int)Num(Get(kv, "unknown_last"), 0);
                        s.HasWinMeta = true;
                        continue;
                    }
                    if (line.StartsWith("SESSAGG_TOTAL ", StringComparison.Ordinal))
                    {
                        Dictionary<string, string> kv = Pairs(Tail(line, "SESSAGG_TOTAL"));
                        s.AggSessions = Num(Get(kv, "sessions"), 0);
                        s.AggNonBlank = Num(Get(kv, "nonblank"), 0);
                        s.AggUncached = Num(Get(kv, "uncached"), 0);
                        s.AggCacheRead = Num(Get(kv, "cacheRead"), 0);
                        s.AggCacheWrite = Num(Get(kv, "cacheWrite"), 0);
                        s.AggOutput = Num(Get(kv, "output"), 0);
                        s.AggFirstDay = Get(kv, "first_day") ?? "";
                        s.AggLastDay = Get(kv, "last_day") ?? "";
                        s.HasAggTotal = true;
                        continue;
                    }
                    if (line.StartsWith("SESSAGG_SESSION ", StringComparison.Ordinal))
                    {
                        string rest = Tail(line, "SESSAGG_SESSION").Trim();
                        int sp = rest.IndexOf(' ');
                        SessAggRow ar = new SessAggRow();
                        if (sp < 0) { ar.Id = rest; s.AggRows.Add(ar); continue; }
                        ar.Id = rest.Substring(0, sp);
                        Dictionary<string, string> kv = Pairs(rest.Substring(sp + 1));
                        ar.Bucket = Get(kv, "bucket") ?? "";
                        string turns = Get(kv, "turns");
                        string steps = Get(kv, "steps");
                        ar.HasStats = !string.IsNullOrEmpty(turns) && !string.IsNullOrEmpty(steps) && turns != "unknown" && steps != "unknown";
                        if (ar.HasStats) { ar.Turns = Num(turns, 0); ar.Steps = Num(steps, 0); }
                        string un = Get(kv, "uncached");
                        string cr = Get(kv, "cacheRead");
                        string cw = Get(kv, "cacheWrite");
                        string op = Get(kv, "output");
                        ar.HasTokens = !string.IsNullOrEmpty(un) && un != "unknown"
                            && !string.IsNullOrEmpty(cr) && cr != "unknown"
                            && !string.IsNullOrEmpty(cw) && cw != "unknown"
                            && !string.IsNullOrEmpty(op) && op != "unknown";
                        if (ar.HasTokens) { ar.Uncached = Num(un, 0); ar.CacheRead = Num(cr, 0); ar.CacheWrite = Num(cw, 0); ar.Output = Num(op, 0); }
                        s.AggRows.Add(ar);
                        continue;
                    }
                    // —— 看板第二批（2026-10-09 ✓✓ 规格 §11.6-B ✓ 与 SESSAGG_SESSION 同模式：id 在前、键=值在后、unknown → Has=false ✓）——
                    if (line.StartsWith("SESSTIME_SESSION ", StringComparison.Ordinal))
                    {
                        string rest = Tail(line, "SESSTIME_SESSION").Trim();
                        int sp = rest.IndexOf(' ');
                        SessTimeRow tr = new SessTimeRow();
                        if (sp < 0) { tr.Id = rest; s.TimeRows.Add(tr); continue; }
                        tr.Id = rest.Substring(0, sp);
                        Dictionary<string, string> kv = Pairs(rest.Substring(sp + 1));
                        string llm = Get(kv, "llmMs");
                        string tool = Get(kv, "toolMs");
                        string ttft = Get(kv, "ttftMs");
                        string dec = Get(kv, "decodeMs");
                        tr.Has = !string.IsNullOrEmpty(llm) && llm != "unknown"
                            && !string.IsNullOrEmpty(tool) && tool != "unknown"
                            && !string.IsNullOrEmpty(ttft) && ttft != "unknown"
                            && !string.IsNullOrEmpty(dec) && dec != "unknown";
                        if (tr.Has) { tr.LlmMs = Num(llm, 0); tr.ToolMs = Num(tool, 0); tr.TtftMs = Num(ttft, 0); tr.DecodeMs = Num(dec, 0); }
                        s.TimeRows.Add(tr);
                        continue;
                    }
                    if (line.StartsWith("SESSCTX_SESSION ", StringComparison.Ordinal))
                    {
                        string rest = Tail(line, "SESSCTX_SESSION").Trim();
                        int sp = rest.IndexOf(' ');
                        SessCtxRow cr = new SessCtxRow();
                        if (sp < 0) { cr.Id = rest; s.CtxRows.Add(cr); continue; }
                        cr.Id = rest.Substring(0, sp);
                        Dictionary<string, string> kv = Pairs(rest.Substring(sp + 1));
                        string sys = Get(kv, "system");
                        string tls = Get(kv, "tools");
                        string msg = Get(kv, "message");
                        cr.Has = !string.IsNullOrEmpty(sys) && sys != "unknown"
                            && !string.IsNullOrEmpty(tls) && tls != "unknown"
                            && !string.IsNullOrEmpty(msg) && msg != "unknown";
                        if (cr.Has) { cr.System = Num(sys, 0); cr.Tools = Num(tls, 0); cr.Message = Num(msg, 0); }
                        s.CtxRows.Add(cr);
                        continue;
                    }
                    if (line.StartsWith("SESSION ", StringComparison.Ordinal)) s.Rows.Add(ParseRow(line));
                    // 父子链接 ✓：CLI 扫会话文件的 childId 得到 ✓（先收集 ✓ 循环后统一归类 ✓）
                    if (line.StartsWith("SESSION_CHILD ", StringComparison.Ordinal))
                    {
                        string[] pc = line.Substring("SESSION_CHILD ".Length).Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (pc.Length >= 2) links[pc[1]] = pc[0];   // 子 → 父 ✓
                        continue;
                    }
                }
                catch
                {
                    /* 单行解析失败不影响其它行 —— 界面绝不能因为一行脏数据而崩 */
                }
            }
            // 运行态只有桥接插件快照才知道：磁盘投影没有这个事实（unknown 不等于"已结束"）
            bool liveKnown = s.Source == "snapshot";
            for (int i = 0; i < s.Rows.Count; i++) s.Rows[i].LiveKnown = liveKnown;
            // 归类：子代理挂到父会话下 ✓（98/98 子 id 都有 SESSION 行 ✓ 所以都能归类 ✓）
            // ★★ 审查抓到：这里对**每个子代理**都线性扫一遍全部行去找父 ✗ → O(子 × 行) ✗
            //   而且 Parse 是在 **UI 线程**上跑的 ✓（await 之后回到 UI 上下文 ✓）→ 会话多就卡界面 ✓
            // ✓ 现在：**先建 id → 行 的字典** ✓✓ 父查找 O(1) ✓
            System.Collections.Generic.Dictionary<string, SessionRow> byId2 =
                new System.Collections.Generic.Dictionary<string, SessionRow>(StringComparer.Ordinal);
            for (int rk = 0; rk < s.Rows.Count; rk++)
            {
                SessionRow rr = s.Rows[rk];
                if (rr != null && rr.Id != null && !byId2.ContainsKey(rr.Id)) byId2[rr.Id] = rr;
            }
            for (int ri = 0; ri < s.Rows.Count; ri++)
            {
                string cid2 = s.Rows[ri].Id;
                string pid3;
                if (cid2 != null && links.TryGetValue(cid2, out pid3))
                {
                    s.Rows[ri].ParentId = pid3;
                    SessionRow parentRow;
                    if (byId2.TryGetValue(pid3, out parentRow) && parentRow != null) parentRow.ChildIds.Add(cid2);
                }
            }
            int subCount = 0;
            for (int ri = 0; ri < s.Rows.Count; ri++) if (s.Rows[ri].IsSubAgent) subCount++;
            s.SubAgentCount = subCount;
            s.RootCount = s.Rows.Count - subCount;
            return s;
        }

        private static SessionRow ParseRow(string line)
        {
            string rest = line.Substring("SESSION ".Length).Trim();
            int sp = rest.IndexOf(' ');
            SessionRow r = new SessionRow();
            if (sp < 0) { r.Id = rest; return r; }
            r.Id = rest.Substring(0, sp);
            Dictionary<string, string> kv = Pairs(rest.Substring(sp + 1));
            r.Title = Decode(Clean(Get(kv, "title")));
            r.Created = Clean(Get(kv, "created"));
            r.Last = Clean(Get(kv, "last"));
            r.Turns = Num(Get(kv, "turns"), 0);
            r.Steps = Num(Get(kv, "steps"), 0);
            r.In = Num(Get(kv, "in"), 0);
            r.Out = Num(Get(kv, "out"), 0);
            r.CacheRead = Num(Get(kv, "cacheRead"), 0);
            r.HitPercent = Pct(Get(kv, "hit"));
            r.DecodeTps = Pct(Get(kv, "decode"));
            r.TtftMs = Num(Get(kv, "ttft"), -1);
            r.CtxPercent = Pct(Get(kv, "ctx"));
            r.Blank = Get(kv, "blank") == "1";
            r.Live = Get(kv, "live") == "1";
            // ★ 2026-10-02：真在动（插件相邻两拍 seq 变过 ✓）；unknown → ActiveKnown=false ✗ 不假装 ✗
            string act = Get(kv, "active");
            r.Active = act == "1";
            r.ActiveKnown = act == "1" || act == "0";
            r.LastActive = Decode(Clean(Get(kv, "lastActive")));
            return r;
        }

        private static string Tail(string line, string marker)
        {
            return line.Length > marker.Length ? line.Substring(marker.Length) : "";
        }

        /// <summary>标记行自由文本解码 ✓（与 CLI 侧对称 ✓）。
        /// ★★ 架构审计抓到（S4）：这里原来是**逐字复制**领域层 MarkerText.Decode ✗
        ///   → 两边靠"人记得同步" ✗ → 改一边就悄悄不一致 ✗
        /// ✓ 现在：**直接调用同一份源码** ✓✓（csproj 用 Compile Include 链接进来 ✓
        ///   仍然没有 ProjectReference ✓ 架构约定不破 ✓）</summary>
        public static string Decode(string text)
        {
            return Dsht.Domain.Services.MarkerText.Decode(text);
        }

        /// <summary>"unknown" → 空串（界面显示 unknown 由文本属性负责）。</summary>
        private static string Clean(string v) { return v == null || v == "unknown" ? "" : v; }

        private static Dictionary<string, string> Pairs(string text)
        {
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return kv;
            string[] parts = text.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length == 0) continue;
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                kv[p.Substring(0, eq)] = p.Substring(eq + 1);
            }
            return kv;
        }

        private static string Get(Dictionary<string, string> kv, string key)
        {
            string v;
            return kv.TryGetValue(key, out v) ? v : null;
        }

        private static long Num(string v, long fallback)
        {
            if (string.IsNullOrEmpty(v) || v == "unknown") return fallback;
            long n;
            return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : fallback;
        }

        /// <summary>百分比/速率：`unknown` 或非法 → -1（未知），绝不当作 0。</summary>
        private static double Pct(string v)
        {
            if (string.IsNullOrEmpty(v) || v == "unknown") return -1;
            double d;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : -1;
        }
    }
}
