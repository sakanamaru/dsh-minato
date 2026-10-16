using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>一个会话的成本行（CLI 的 `COST_SESSION scope=window <id> …` 行 · 规格 §3 ✓）。
    /// 未知一律不假装：token 缺失 → HasTokens=false；模型缺失 → HasModel=false；
    /// 金额缺失（cost=none）→ HasCost=false ✗ 绝不当 0 ✓。</summary>
    public sealed class CostSessionRow
    {
        public string Id = "";
        /// <summary>`model=unknown` → 空串（界面显示 unknown 由文本属性负责 ✓）。</summary>
        public string Model = "";
        public bool HasModel;
        public long Uncached;
        public long CacheRead;
        public long CacheWrite;
        public long Output;
        public bool HasTokens;          // 四桶任一 unknown → false ✓
        /// <summary>CLI 给的**十进制字符串**（固定 6 位小数 ✓ InvariantCulture ✓）✗ GUI 不重算 ✓。</summary>
        public string CostText = "";
        public bool HasCost;            // cost=none / 缺键 → false ✓
        public long Tokens { get { return Uncached + CacheRead + CacheWrite + Output; } }
    }

    /// <summary>`COST_TOTAL scope=…` 行：该块的四桶合计 + 金额（规格 §3 ✓）。</summary>
    public sealed class CostTotal
    {
        public long Uncached;
        public long CacheRead;
        public long CacheWrite;
        public long Output;
        public long Tokens;             // CLI 直接给的 tokens=（✗ 不用自己再求和冒充 ✓ 缺省才回退求和 ✓）
        public bool HasTokens;
        public string CostText = "";
        public bool HasCost;
    }

    /// <summary>`COST_BUDGET` 行（规格 §3 ✓ 归 today 块 ✓ 恒「今日 · level=global」✓ §3.1-③）：
    /// limit/spent 各自可为 none ✗ 不装 0 ✓；over 只在真超限时为 1 ✓。</summary>
    public sealed class CostBudget
    {
        public string LimitText = "";
        public bool HasLimit;
        public string SpentText = "";
        public bool HasSpent;
        public bool Over;
    }

    /// <summary>一个成本块（规格 §3 ✓ 靠 `scope=` 区分 ✗ 不靠出现顺序 ✓）：
    /// `scope=today` = 今日（含 COST_BUDGET ✓）；`scope=window` = 当前窗口（含 COST_SESSION ✓）。</summary>
    public sealed class CostBlock
    {
        public string Scope = "";             // "today" / "window" / ""（认不出 ✗ 不硬猜 ✓）
        public bool HasMeta;
        public int Days = -1;                 // days=unknown → -1（内存哨兵 ✓）
        public string From = "-";
        public string To = "-";
        public string Level = "";
        public string Currency = "";
        public string Tz = "";
        public bool Priced;                   // priced=1 ✓（0 = 四键全未填 → 不显示金额 ✓）
        public bool Truth0;
        /// <summary>无锚点被排除的会话数 ✓（脚注三要素之一的**唯一来源** ✗ GUI 不许自己数 ✓ §3.1-⑥）。</summary>
        public long UnknownLast = -1;
        /// <summary>模型未知的会话数 ✓（同上 ✓）。</summary>
        public long ModelUnknown = -1;
        public CostTotal Total = new CostTotal();
        public List<CostSessionRow> Sessions = new List<CostSessionRow>();
        /// <summary>预算（全局唯一 ✓ 归 today 块 ✓ §3.1-③）——同一对象也挂在快照上 ✓。</summary>
        public CostBudget Budget = new CostBudget();
        public List<string> Notes = new List<string>();
    }

    /// <summary>`cost` / `overview` 输出里的成本事实（规格 §3 + §3.1 ✓ 只解析标记行 ✗ 不引用核心库 ✓）。
    /// 一个进程吐**两块**：`scope=today` + `scope=window` ✓ 顺序不保证 ✓ ⇒ **按 scope 分组** ✗ 不按首次出现锁死 ✓。
    /// ✗ 一块缺了就把另一块的数当它显示 ✗（最危险的串档 ✓）——缺的那块如实说「该块不可用」✓。</summary>
    public sealed class CostSnapshot
    {
        /// <summary>至少见到一行 COST_*（老 CLI / 未接线 → false → 卡片如实说不提供 ✓）。</summary>
        public bool HasCost;
        public bool Ok;                       // 见到 COST_META ✓
        public string FailReason = "";        // COST_FAIL 原文 ✓
        public List<CostBlock> Blocks = new List<CostBlock>();
        /// <summary>全部 COST_NOTE（多值 ✓ 出现即累加 ✓ 去重保序 ✓）。</summary>
        public List<string> Notes = new List<string>();
        /// <summary>预算（`COST_BUDGET` 恒全局今日 ✓ 不分块 ✓）。</summary>
        public CostBudget Budget = new CostBudget();

        /// <summary>`scope=today` 块（✗ 没有就是 null ✓ 绝不退回 window 块 ✗）。</summary>
        public CostBlock Today { get { return ByScope("today"); } }
        /// <summary>`scope=window` 块（同上 ✓）。</summary>
        public CostBlock Window { get { return ByScope("window"); } }
        /// <summary>认不出 scope 的块（诊断/降级显示用 ✓ 不当任何一块用 ✗）。</summary>
        public CostBlock Unscoped { get { return ByScope(""); } }

        public CostBlock ByScope(string scope)
        {
            for (int i = 0; i < Blocks.Count; i++) if (Blocks[i] != null && Blocks[i].Scope == scope) return Blocks[i];
            return null;
        }

        /// <summary>有没有某条 COST_NOTE（**多值** ✓ 按首词比对 —— `session-cost-unavailable n=3` 也算命中 ✓）。</summary>
        public bool HasNote(string note)
        {
            for (int i = 0; i < Notes.Count; i++) if (NoteKey(Notes[i]) == note) return true;
            return false;
        }

        /// <summary>取某条 COST_NOTE 的 `n=` 计数（`session-cost-unavailable n=3` → 3 ✓）；
        /// 没有这条 / 没有 n= / 非法 → -1（未知 ✗ 不假装 0 ✓）。</summary>
        public int NoteCount(string note)
        {
            for (int i = 0; i < Notes.Count; i++)
            {
                if (NoteKey(Notes[i]) != note) continue;
                string[] parts = Notes[i].Split(' ');
                for (int k = 0; k < parts.Length; k++)
                {
                    if (!parts[k].StartsWith("n=", StringComparison.Ordinal)) continue;
                    int v;
                    if (int.TryParse(parts[k].Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
                }
                return -1;
            }
            return -1;
        }

        /// <summary>COST_NOTE 的首词（键）✓（`session-cost-unavailable n=3` → `session-cost-unavailable` ✓）。</summary>
        public static string NoteKey(string note)
        {
            if (string.IsNullOrEmpty(note)) return "";
            int sp = note.IndexOf(' ');
            return sp < 0 ? note : note.Substring(0, sp);
        }
    }

    /// <summary>成本标记行解析 + 展示文案（纯函数 · **绝不抛** ✓ LogicTests 直接钉死 ✓）。
    /// 契约 = 规格 §3 + §3.1（scope 两块制 ✓）；字段名逐字解析 ✗ GUI 不重算金额、不改名、不加字段 ✓。
    /// 纪律：未配价 → 任何金额文案都不出现 `$` ✗（只报 token ✓）；✗ 不把窗口数当今日数显示 ✓。</summary>
    public static class CostMarkers
    {
        // —— 用户可见文案（唯一出处 ✓ LogicTests 逐字断言 ✓ ✗ 不用 ✓/✗ 记号 ✗ 不写施工语气 ✓）——
        /// <summary>未配价时的整句提示（交付物 3 ✓ 逐字）。</summary>
        public const string UnpricedHint = "未配置单价 → 不显示金额（设置页可填）";
        /// <summary>今日卡脚注模板（规格 §2 末行 ✓ 三要素 ✓ 逐字；{0} = 无锚点被排除的会话数 = COST_META unknown_last ✓）。</summary>
        public const string TodayFootnoteTemplate = "整会话累计，非今日增量 · 按最后提问归属（昨天提问、今天仍在用的会话不计入）· {0} 个无锚点会话被排除";
        /// <summary>今日卡脚注三要素（测试用：逐条必须在 ✓）。</summary>
        public const string FootnotePart1 = "整会话累计，非今日增量";
        public const string FootnotePart2 = "按最后提问归属（昨天提问、今天仍在用的会话不计入）";
        public const string FootnotePart3 = "个无锚点会话被排除";
        /// <summary>金额一律带这一枚标签（交付物 4 ✓）。</summary>
        public const string EstimateLabel = "估算";
        /// <summary>界面只有 USD ✗ 不做人民币换算 ✓（维护者已确认 ✓）。</summary>
        public const string CurrencyLabel = "USD";
        /// <summary>老 CLI / 未接线时的如实说明（✗ 不装作 0 ✓）。</summary>
        public const string NotProvided = "当前数据源未提供成本标记行（CLI 版本较旧或未接线）";
        /// <summary>成本命令失败时的如实前缀。</summary>
        public const string FailPrefix = "成本数据读取失败：";
        /// <summary>某一块缺失时的如实说明（✗ 绝不拿另一块的数顶上 ✓ §3.1-②）。</summary>
        public const string TodayUnavailable = "今日：该块不可用（本命令输出里没有 scope=today 成本块）";
        public const string WindowUnavailable = "当前窗口：该块不可用（本命令输出里没有 scope=window 成本块）";
        /// <summary>预算口径句（非 global 档必标 ✓ §3.1-③）。</summary>
        public const string BudgetScopeText = "预算按全局计（不受口径档影响）";

        /// <summary>今日卡脚注（N 未知 → unknown ✗ 不装 0 ✓；N 只用 COST_META 的 unknown_last ✓）。</summary>
        public static string TodayFootnote(long unknownLast)
        {
            string n = unknownLast < 0 ? "unknown" : unknownLast.ToString(CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture, TodayFootnoteTemplate, n);
        }

        /// <summary>CLI 的十进制金额串 → 显示串（`$0.14` ✓ 只裁尾零 ✗ 不重算 ✗ 不四舍五入改值 ✓）。
        /// 非数字 / none / 空 → 空串（✗ 不显示假金额 ✓）。</summary>
        public static string Usd(string costText)
        {
            string t = costText == null ? "" : costText.Trim();
            if (t.Length == 0 || t == "none") return "";
            decimal d;
            if (!decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return "";
            return "$" + TrimZeros(t);
        }

        /// <summary>裁掉小数尾零但至少留两位（`0.140000` → `0.14`；`1.200000` → `1.20`；`3.000000` → `3.00`）。</summary>
        public static string TrimZeros(string dec)
        {
            string t = dec == null ? "" : dec.Trim();
            int dot = t.IndexOf('.');
            if (dot < 0) return t;
            int end = t.Length;
            while (end - 1 > dot + 2 && t[end - 1] == '0') end--;
            return t.Substring(0, end);
        }

        // —— 展示文案（纯函数 ✓ LogicTests 断言「未配价无 $ / 配价有估算」✓✓）——

        /// <summary>今日 token 行（**未配价也照常显示** ✓ 这一行永远不含金额 ✓ ✗ 不用窗口数冒充 ✓）。</summary>
        public static string TodayTokensLine(CostBlock today)
        {
            if (today == null) return TodayUnavailable;
            return TotalLine("今日", DayName(today), today.Total);
        }

        /// <summary>当前窗口 token 行（✗ 窗口块缺失 → 如实说不可用 ✓ 不拿今日数冒充 ✓）。</summary>
        public static string WindowTokensLine(CostBlock win)
        {
            if (win == null) return WindowUnavailable;
            return TotalLine("当前窗口", WindowName(win), win.Total);
        }

        /// <summary>合计行（today / window 共用 ✓ 文案一处 ✓）：用量未知 → 如实说 ✗ 不显示 0 ✓。</summary>
        private static string TotalLine(string who, string what, CostTotal t)
        {
            if (t == null || !t.HasTokens) return who + "（" + what + "）：用量未知（不猜）";
            return who + "（" + what + "）：输入 " + SessionRow.Human(t.Uncached + t.CacheRead) + " token"
                + "（其中缓存读 " + SessionRow.Human(t.CacheRead) + " · 缓存写 " + SessionRow.Human(t.CacheWrite) + "）"
                + "　输出 " + SessionRow.Human(t.Output) + " token"
                + "　合计 " + SessionRow.Human(t.Tokens) + " token";
        }

        /// <summary>今日金额行：配价 → 「估算金额 $…（USD · 估算，非官方账单）」（必含「估算」✓）；
        /// 未配价 → 逐字提示句，**不含任何金额 / $ / USD 数字** ✓✓；块缺失 → 空串（由 TodayUnavailable 行负责 ✓）。</summary>
        public static string TodayMoneyLine(CostBlock today)
        {
            if (today == null) return "";
            if (!today.Priced) return UnpricedHint;
            string usd = Usd(today.Total.CostText);
            if (usd.Length == 0) return "今日金额不可算（该块 token 未知，不假装 0）";
            return "估算金额 " + usd + "（" + Cur(today) + " · " + EstimateLabel + "，非官方账单）";
        }

        /// <summary>当前窗口金额行（同今日：未配价 → 只给提示句 ✓）。</summary>
        public static string WindowMoneyLine(CostBlock win)
        {
            if (win == null) return "";
            if (!win.Priced) return UnpricedHint;
            string usd = Usd(win.Total.CostText);
            if (usd.Length == 0) return "窗口金额不可算（该块 token 未知，不假装 0）";
            return "估算金额 " + usd + "（" + Cur(win) + " · " + EstimateLabel + "，非官方账单）";
        }

        private static string Cur(CostBlock b) { return b != null && !string.IsNullOrEmpty(b.Currency) ? b.Currency : CurrencyLabel; }

        /// <summary>今日日期名（today 块的 from 就是今日本地日 ✓ 取不到 → 人话兜底 ✗ 不猜日期 ✓）。</summary>
        public static string DayName(CostBlock today)
        {
            if (today == null) return "今天";
            if (!string.IsNullOrEmpty(today.From) && today.From != "-" && today.From != "unknown") return today.From;
            return "今天（本地日）";
        }

        /// <summary>窗口名（近 N 天 / 总计 / 自定义区间 from~to ✓ 与看板总计卡同一读法 ✓）。</summary>
        public static string WindowName(CostBlock b)
        {
            if (b == null) return "未知窗口";
            if (b.Days > 0) return "近 " + b.Days.ToString(CultureInfo.InvariantCulture) + " 天";
            if (b.From != "-" && b.To != "-" && !string.IsNullOrEmpty(b.From)) return "自定义区间 " + b.From + "~" + b.To;
            return "总计";
        }

        /// <summary>预算行（未设上限 → 空串 ✗ 不占版面 ✓；设了 → 预算 + 今日估算 ✗ 不把 none 当 0 ✓）。</summary>
        public static string BudgetLine(CostSnapshot s)
        {
            CostBudget b = s != null ? s.Budget : null;
            if (b == null || !b.HasLimit) return "";
            string limit = Usd(b.LimitText);
            if (limit.Length == 0) return "";
            string spent = Usd(b.SpentText);
            return "每日预算 " + limit + "（" + CurrencyLabel + "）· 今日" + EstimateLabel
                + (spent.Length > 0 ? " " + spent : " 金额不可算（未配价或该笔用量未知）");
        }

        /// <summary>超限提示条文案（**只在真超限时给非空** ✓ ✗ 不阻止任何操作 ✓ 我们不是收费系统 ✓）。</summary>
        public static string OverBudgetText(CostSnapshot s)
        {
            CostBudget b = s != null ? s.Budget : null;
            if (b == null || !b.Over) return "";
            string limit = Usd(b.LimitText);
            string spent = Usd(b.SpentText);
            if (limit.Length == 0) return "";
            string head = spent.Length > 0
                ? "今日" + EstimateLabel + "金额 " + spent + " 已超过每日预算 " + limit
                : "今日" + EstimateLabel + "金额已超过每日预算 " + limit;
            return head + "（" + CurrencyLabel + "）—— 仅提示，不阻止任何操作。";
        }

        /// <summary>预算口径句：`budget-scope-global` 出现，或（today/window 块的）level 不是 global → 给出 ✓ 否则空串 ✓。</summary>
        public static string BudgetScopeLine(CostSnapshot s, CostBlock block)
        {
            bool byNote = s != null && s.HasNote("budget-scope-global");
            string lv = block != null && block.Level != null ? block.Level : "";
            bool nonGlobal = lv.Length > 0 && lv != "unknown" && lv != "global";
            if (!byNote && !nonGlobal) return "";
            return BudgetScopeText;
        }

        /// <summary>`priced=1` 但部分会话 `cost=none` 时的如实说明（§3.1-⑤ ✓ cost=none ≠ 0 ✓）。
        /// 计数取 `COST_NOTE session-cost-unavailable n=N` ✓（取不到 → 空串 ✗ 不自己数 ✓）；未配价 → 空串 ✓。</summary>
        public static string SessionUnavailableLine(CostSnapshot s, CostBlock win)
        {
            if (win == null || !win.Priced || s == null) return "";
            int n = s.NoteCount("session-cost-unavailable");
            if (n <= 0) return "";
            return "其中 " + n.ToString(CultureInfo.InvariantCulture) + " 笔金额不可算（token 未知，不假装 0）";
        }

        /// <summary>会话明细一行（**唯一出处** ✓ LogicTests 钉死 ✓）：短 id · 最后使用模型（近似 ✓）· token
        /// · 金额（**仅配价且该笔可算时** ✓ 未配价一律不出现 $ ✓）· 用量未知如实标注 ✓。</summary>
        public static string SessionLine(CostSessionRow r, CostBlock win)
        {
            if (r == null) return "";
            string id = r.Id != null && r.Id.Length > 8 ? r.Id.Substring(0, 8) : (r.Id ?? "");
            StringBuilder sb = new StringBuilder(96);
            sb.Append(id);
            sb.Append("　模型 ").Append(r.HasModel ? r.Model : "unknown");
            if (r.HasTokens)
            {
                sb.Append("　输入 ").Append(SessionRow.Human(r.Uncached + r.CacheRead)).Append(" token")
                  .Append("　输出 ").Append(SessionRow.Human(r.Output)).Append(" token");
            }
            else sb.Append("　用量未知（不计入求和）");
            if (win != null && win.Priced && r.HasCost)
            {
                string usd = Usd(r.CostText);
                if (usd.Length > 0) sb.Append("　").Append(EstimateLabel).Append(" ").Append(usd);
            }
            return sb.ToString();
        }

        /// <summary>模型近似说明行（近似 ✓ + unknown 计数 ✓ 逐字一处 ✓ 计数用 COST_META model_unknown ✓）。</summary>
        public static string ModelNoteLine(CostBlock win)
        {
            if (win == null) return "";
            string core = "模型为「最后使用」近似（dsh 投影只保留每个会话最后一次请求的模型，会话内换过模型会失真）";
            if (win.ModelUnknown > 0) core += "；其中 " + win.ModelUnknown.ToString(CultureInfo.InvariantCulture) + " 个会话的模型未知（快照里没有这项，不猜模型名）";
            return core;
        }

        /// <summary>COST_NOTE 的人话（未知取值 → 原文照转 ✓ 不编 ✓）。</summary>
        public static string NoteText(string note)
        {
            switch (CostSnapshot.NoteKey(note))
            {
                case "price-missing": return "未配置单价（设置页可填四个单价键）";
                case "official-billing-not-compared": return "未与官方账单对账（余额接口只有当下值，没有历史）";
                case "model-approximate": return "模型为「最后使用」近似（不是逐次精确归属）";
                case "budget-scope-global": return BudgetScopeText;
                case "session-cost-unavailable": return "有会话的金额不可算（token 未知，不假装 0）";
                default: return note ?? "";
            }
        }

        /// <summary>把 COST_NOTE 列表合成一行（去重 ✓ 空 → 空串 ✓）；
        /// 有专属呈现的两条（预算口径 / 不可算笔数）**不在这里重复** ✓。</summary>
        public static string NotesLine(List<string> notes)
        {
            if (notes == null || notes.Count == 0) return "";
            List<string> seen = new List<string>();
            StringBuilder sb = new StringBuilder(160);
            for (int i = 0; i < notes.Count; i++)
            {
                string n = notes[i];
                string k = CostSnapshot.NoteKey(n);
                if (string.IsNullOrEmpty(k) || seen.Contains(k)) continue;
                if (k == "budget-scope-global" || k == "session-cost-unavailable") continue;   // 专属行已呈现 ✓
                seen.Add(k);
                sb.Append(seen.Count > 1 ? " · " : "").Append(NoteText(n));
            }
            return sb.ToString();
        }

        /// <summary>会话明细排序取前 n（**确定性** ✓）：配价 → 金额降序 ✓；否则用量降序 ✓；
        /// 并列按会话 id 字典序（Ordinal ✓ 两次运行同序 ✓）。more = 未列出的条数 ✓。</summary>
        public static List<CostSessionRow> TopSessions(CostBlock win, int n, out int more)
        {
            more = 0;
            List<CostSessionRow> all = new List<CostSessionRow>();
            if (win == null) return all;
            for (int i = 0; i < win.Sessions.Count; i++) if (win.Sessions[i] != null) all.Add(win.Sessions[i]);
            bool byCost = win.Priced;
            all.Sort(delegate (CostSessionRow x, CostSessionRow y)
            {
                int c;
                if (byCost) c = Money(x.CostText).CompareTo(Money(y.CostText));
                else c = (x.Uncached + x.CacheRead + x.CacheWrite + x.Output).CompareTo(y.Uncached + y.CacheRead + y.CacheWrite + y.Output);
                c = -c;   // 降序 ✓
                if (c != 0) return c;
                c = (x.Uncached + x.CacheRead + x.CacheWrite + x.Output).CompareTo(y.Uncached + y.CacheRead + y.CacheWrite + y.Output);
                if (c != 0) return -c;
                return string.CompareOrdinal(x.Id ?? "", y.Id ?? "");
            });
            if (n > 0 && all.Count > n) { more = all.Count - n; all.RemoveRange(n, all.Count - n); }
            return all;
        }

        /// <summary>金额比较用的十进制（解析不了 → 0 ✓ 排序稳定 ✗ 不抛 ✓）。</summary>
        private static decimal Money(string costText)
        {
            decimal d;
            if (string.IsNullOrEmpty(costText) || costText == "none") return 0m;
            return decimal.TryParse(costText, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0m;
        }

        /// <summary>解析 `cost` / `overview` 输出里的 COST_* 行（**绝不抛**：单行解析失败跳过 ✓）。
        /// **按 `scope=` 分组** ✓（两块都出现 ✓ 顺序不保证 ✓）✗ 不按首次出现锁死 ✓。</summary>
        public static CostSnapshot Parse(string output)
        {
            CostSnapshot s = new CostSnapshot();
            if (string.IsNullOrEmpty(output)) return s;
            string[] lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? "" : lines[i].Trim();
                if (line.Length == 0) continue;
                try
                {
                    if (line.StartsWith("COST_FAIL", StringComparison.Ordinal))
                    {
                        s.HasCost = true;
                        s.FailReason = line.Length > "COST_FAIL".Length ? line.Substring("COST_FAIL".Length).Trim() : "";
                        continue;
                    }
                    if (line.StartsWith("COST_META ", StringComparison.Ordinal))
                    {
                        s.HasCost = true; s.Ok = true;
                        Dictionary<string, string> kv = Pairs(Tail(line, "COST_META"));
                        CostBlock b = Block(s, Scope(kv));
                        b.HasMeta = true;
                        string d = Get(kv, "days");
                        b.Days = (d == null || d == "unknown") ? -1 : (int)Num(d, -1);
                        b.From = Get(kv, "from") ?? "-";
                        b.To = Get(kv, "to") ?? "-";
                        b.Level = Get(kv, "level") ?? "";
                        b.Currency = Decode(Get(kv, "currency") ?? "");
                        b.Tz = Decode(Get(kv, "tz") ?? "");
                        b.Priced = Get(kv, "priced") == "1";
                        b.Truth0 = Get(kv, "truth") == "0";
                        b.UnknownLast = Sent(kv, "unknown_last");     // ★ 脚注 N 的唯一来源 ✓
                        b.ModelUnknown = Sent(kv, "model_unknown");   // ★ 模型未知计数的唯一来源 ✓
                        continue;
                    }
                    if (line.StartsWith("COST_TOTAL ", StringComparison.Ordinal))
                    {
                        s.HasCost = true;
                        Dictionary<string, string> kv = Pairs(Tail(line, "COST_TOTAL"));
                        CostBlock b = Block(s, Scope(kv));
                        string un = Get(kv, "uncached");
                        string cr = Get(kv, "cacheRead");
                        string cw = Get(kv, "cacheWrite");
                        string op = Get(kv, "output");
                        b.Total.HasTokens = !string.IsNullOrEmpty(un) && un != "unknown"
                            && !string.IsNullOrEmpty(cr) && cr != "unknown"
                            && !string.IsNullOrEmpty(cw) && cw != "unknown"
                            && !string.IsNullOrEmpty(op) && op != "unknown";
                        if (b.Total.HasTokens)
                        {
                            b.Total.Uncached = Num(un, 0);
                            b.Total.CacheRead = Num(cr, 0);
                            b.Total.CacheWrite = Num(cw, 0);
                            b.Total.Output = Num(op, 0);
                            long sum = b.Total.Uncached + b.Total.CacheRead + b.Total.CacheWrite + b.Total.Output;
                            string tk = Get(kv, "tokens");
                            b.Total.Tokens = (tk == null || tk == "unknown") ? sum : Num(tk, sum);
                        }
                        string c = Get(kv, "cost");
                        b.Total.CostText = (c == null || c == "none" || c == "unknown") ? "" : c;
                        b.Total.HasCost = b.Total.CostText.Length > 0;
                        continue;
                    }
                    if (line.StartsWith("COST_SESSION ", StringComparison.Ordinal))
                    {
                        s.HasCost = true;
                        string rest = Tail(line, "COST_SESSION").Trim();
                        Dictionary<string, string> head = Pairs(rest);
                        string sc = "";
                        string v;
                        if (head.TryGetValue("scope", out v)) sc = v;
                        // 形如 `scope=window <id> model=… …`：**前导的 key=value 记号全部剥掉**，剩下的开头就是 id ✓
                        // （✗ 不假定 scope 一定是第一个记号 ✓ 但仍要求它在 id 之前 ✓）
                        string afterScope = rest;
                        while (true)
                        {
                            int sp0 = afterScope.IndexOf(' ');
                            string first = sp0 < 0 ? afterScope : afterScope.Substring(0, sp0);
                            if (first.Length > 0 && first.IndexOf('=') > 0)
                            {
                                afterScope = sp0 < 0 ? "" : afterScope.Substring(sp0 + 1).Trim();
                                continue;
                            }
                            break;
                        }
                        CostBlock b = Block(s, sc.Length > 0 ? sc : "window");
                        int sp2 = afterScope.IndexOf(' ');
                        CostSessionRow r = new CostSessionRow();
                        if (sp2 < 0) { r.Id = afterScope; b.Sessions.Add(r); continue; }
                        r.Id = afterScope.Substring(0, sp2);
                        Dictionary<string, string> kv = Pairs(afterScope.Substring(sp2 + 1));
                        string md = Get(kv, "model");
                        r.HasModel = !string.IsNullOrEmpty(md) && md != "unknown";
                        if (r.HasModel) r.Model = md;
                        string un = Get(kv, "uncached");
                        string cr = Get(kv, "cacheRead");
                        string cw = Get(kv, "cacheWrite");
                        string op = Get(kv, "output");
                        r.HasTokens = !string.IsNullOrEmpty(un) && un != "unknown"
                            && !string.IsNullOrEmpty(cr) && cr != "unknown"
                            && !string.IsNullOrEmpty(cw) && cw != "unknown"
                            && !string.IsNullOrEmpty(op) && op != "unknown";
                        if (r.HasTokens)
                        {
                            r.Uncached = Num(un, 0);
                            r.CacheRead = Num(cr, 0);
                            r.CacheWrite = Num(cw, 0);
                            r.Output = Num(op, 0);
                        }
                        string c = Get(kv, "cost");
                        r.CostText = (c == null || c == "none" || c == "unknown") ? "" : c;
                        r.HasCost = r.CostText.Length > 0;
                        b.Sessions.Add(r);
                        continue;
                    }
                    if (line.StartsWith("COST_BUDGET ", StringComparison.Ordinal))
                    {
                        s.HasCost = true;
                        Dictionary<string, string> kv = Pairs(Tail(line, "COST_BUDGET"));
                        string lim = Get(kv, "limit");
                        s.Budget.HasLimit = !string.IsNullOrEmpty(lim) && lim != "none" && lim != "unknown";
                        if (s.Budget.HasLimit) s.Budget.LimitText = lim;
                        string sp3 = Get(kv, "spent");
                        s.Budget.HasSpent = !string.IsNullOrEmpty(sp3) && sp3 != "none" && sp3 != "unknown";
                        if (s.Budget.HasSpent) s.Budget.SpentText = sp3;
                        s.Budget.Over = Get(kv, "over") == "1";
                        // 预算归 today 块（§3.1-③ ✓）——同一对象挂过去 ✓（today 块稍后出现也能读到 ✓）
                        CostBlock tb = Block(s, "today");
                        tb.Budget = s.Budget;
                        continue;
                    }
                    if (line.StartsWith("COST_NOTE", StringComparison.Ordinal))
                    {
                        s.HasCost = true;
                        string n = Tail(line, "COST_NOTE").Trim();
                        if (n.Length > 0 && !s.Notes.Contains(n)) s.Notes.Add(n);
                        continue;
                    }
                }
                catch
                {
                    /* 单行解析失败不影响其它行 —— 界面绝不能因为一行脏数据而崩 */
                }
            }
            return s;
        }

        /// <summary>取（或建）某 scope 的块 ✓ —— **按 scope 分组** ✗ 与出现顺序无关 ✓。</summary>
        private static CostBlock Block(CostSnapshot s, string scope)
        {
            for (int i = 0; i < s.Blocks.Count; i++)
                if (s.Blocks[i] != null && s.Blocks[i].Scope == scope) return s.Blocks[i];
            CostBlock b = new CostBlock();
            b.Scope = scope;
            b.Budget = s.Budget;
            s.Blocks.Add(b);
            return b;
        }

        private static string Scope(Dictionary<string, string> kv)
        {
            string v = Get(kv, "scope");
            if (v == "today" || v == "window") return v;
            return "";   // 认不出 ✗ 不硬猜成 window ✓
        }

        private static string Tail(string line, string marker)
        {
            return line.Length > marker.Length ? line.Substring(marker.Length) : "";
        }

        /// <summary>`unknown` / 缺键 → -1（未知 ✗ 不假装 0 ✓）；其它非法值也 -1 ✓。</summary>
        private static long Sent(Dictionary<string, string> kv, string key)
        {
            string v = Get(kv, key);
            if (string.IsNullOrEmpty(v) || v == "unknown") return -1;
            long n;
            return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : -1;
        }

        private static string Decode(string text) { return SessionsMarkers.Decode(text); }

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
    }
}
