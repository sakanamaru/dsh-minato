using System;
using System.Collections.Generic;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;
using Dsht.Domain.Services;

namespace Dsht.Cli
{
    /// <summary>成本面板 v3.2 的 CLI 侧（2026-10-16 ✓ 规格 §2/§3 + Lead 裁定 §3.1 ✓）。
    ///
    /// 契约（**冻结** ✗ 不许改名 ✗ 不许加键进 COST_META ✗）：
    /// <code>
    /// COST_META    scope=today|window days=… from=… to=… level=… priced=0|1 currency=USD tz=… truth=0 unknown_last=N model_unknown=N
    /// COST_TOTAL   scope=today|window uncached=… cacheRead=… cacheWrite=… output=… tokens=… cost=…|none
    /// COST_SESSION scope=window &lt;id&gt; model=…|unknown uncached=… cacheRead=… cacheWrite=… output=… cost=…|none
    /// COST_BUDGET  limit=…|none spent=…|none over=0|1
    /// COST_NOTE    price-missing | official-billing-not-compared | model-approximate | budget-scope-global | session-cost-unavailable n=N
    /// COST_FAIL    &lt;原因&gt;（形状照 SESSIONS_FAIL ✓ 另有 stderr COST_ARG / COST_LINEAGE ✓）
    /// </code>
    ///
    /// 纪律：
    /// · **一个进程吐两块**（scope=today 今日 / scope=window 当前窗口 ✓）—— ✗ 不为成本再起进程 ✗（规格 §4 ✓）；
    /// · 落地在 **`cost` 命令** 与 **`overview` 聚合**（GUI 看板只跑 overview ✓ 同一拍同一份输出 ✓）；
    /// · **绝不动 `sessions` 输出** ✗（规格 §3.1-① ✓）—— 两者共用 <see cref="ScanSessions"/> 的一次扫描 ✓ 只读一遍盘 ✓；
    /// · 金额**只用 `output=`** ✗ 不用 `decodeTokens`（会低估 ✓ 规格 §2 ✓）· 未配价**不显示任何金额** ✗（cost=none ✓）；
    /// · `COST_BUDGET` 恒按「今日 · level=global」✗ 不随 `--level` 变（否则父子重复计数 ⇒ 假超限 ✓ 规格 §3.1-③ ✓）；
    /// · **零写入**：只读会话投影 + 配置，全程不写任何文件 ✓（验收 5 ✓）。</summary>
    public static partial class Program
    {
        /// <summary>cost [--days 7|14|30 | --from --to] [--level global|parents|parents_sub]
        /// —— 只读 ✓ 参数与 `sessions` **逐字同一套**（含成对/互斥/格式校验 ✓ 规格 §2 ✓）。</summary>
        private static int Cost(ServiceRegistry reg, string[] args)
        {
            return EmitCost(ScanSessions(reg, args));
        }

        /// <summary>成本发射端（`cost` 与 `overview` 共用 ✓）。
        /// ★ 失败面：**只打 COST_FAIL**（✗ 不打半截数字 ✗ 规格 §3.1 ④ ✓）；stderr 说明照 sessions 的形制 ✓。</summary>
        private static int EmitCost(SessionScan sc)
        {
            if (!sc.Ok)
            {
                Console.Error.WriteLine("COST_ARG " + sc.FailZh);
                Console.WriteLine("COST_FAIL " + T(sc.FailZh, sc.FailEn));
                // EXIT-02 政策 A：参数非法（FailKind=arg）= 用法错误 2；其余（投影读不到等）= 执行失败 1
                return sc.FailKind == "arg" ? 2 : 1;
            }
            if (sc.Level != "global" && !sc.LineageOk)
            {
                EmitLineageDegrade("COST_LINEAGE", sc.Level);
                Console.WriteLine("COST_FAIL " + T("血缘不可用：口径 " + sc.Level + " 需要桥插件血缘段（第三批 · dsh-minato-bridge 直读原始日志首帧 header，正文帧一帧不解）；未检测到 ⇒ 请安装/升级桥插件后重试",
                    "lineage unavailable: scope " + sc.Level + " needs the bridge plugin lineage section (first-frame headers only); install/upgrade dsh-minato-bridge and retry"));
                return 1;   // 执行失败：缺桥插件血缘前置（EXIT-02 政策 A 的裁决：sessions/cost 血缘不可用 = 1，不是用法错误）
            }
            ToolkitConfig cfg = _cfg == null ? new ToolkitConfig() : _cfg;
            bool priced = SessionCost.IsPriced(cfg);
            string tz = MarkerText.Encode(TimeZoneInfo.Local.Id);

            // —— ① 当前窗口集：与 SESSAGG_SESSION bucket=own **逐 id 同一判定** ✓（规格 §5-3 验收 ✓ 两处过滤照抄 Program.Sessions）——
            List<SessionStat> win = new List<SessionStat>();
            for (int i = 0; i < sc.List.Count; i++)
            {
                SessionStat s = sc.List[i];
                if (sc.Windowed && !SessionWindow.Accept(SessionWindow.EffectiveLastPromptMs(s), sc.FromMs, sc.ToMsEx)) continue;
                LineageEdge le = null;
                LineageInfo li = null;
                if (sc.Graph != null && s.Id != null && sc.Lin != null)
                {
                    sc.Lin.Edges.TryGetValue(s.Id, out le);
                    sc.Graph.Nodes.TryGetValue(s.Id, out li);
                }
                if (!SessionLineage.LevelAccept(sc.Level, le, li)) continue;
                win.Add(s);
            }
            // —— ② 今日集：**恒 level=global** ✗ 不随 --level 变（预算语义 = 今天真实花了多少 ✓ 规格 §3.1-③ ✓）——
            DateTime todayDate = DateTime.Now.Date;
            long todayFrom = LocalMidnightUtcMs(todayDate);
            long todayTo = LocalMidnightUtcMs(todayDate.AddDays(1));
            string todayText = todayDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            List<SessionStat> today = new List<SessionStat>();
            for (int i = 0; i < sc.List.Count; i++)
            {
                SessionStat s = sc.List[i];
                if (!SessionWindow.Accept(SessionWindow.EffectiveLastPromptMs(s), todayFrom, todayTo)) continue;
                today.Add(s);
            }

            // —— window 块（规格 §3.1-② ✓）——
            EmitCostMeta("window", sc.Days, sc.FromText, sc.ToText, sc.Level, priced, tz, sc.UnknownLast, ModelUnknown(win));
            EmitCostTotal("window", cfg, win);
            int unavailable = 0;
            for (int i = 0; i < win.Count; i++)
            {
                SessionStat s = win[i];
                decimal? amt = SessionCost.AmountFor(cfg, s);
                if (priced && !amt.HasValue) unavailable++;   // 配了价但这一笔**算不了**（token 未知 ✗ 不假装 0 ✓）
                System.Globalization.CultureInfo i0c = System.Globalization.CultureInfo.InvariantCulture;
                Console.WriteLine("COST_SESSION scope=window " + MarkerText.Encode(s.Id)
                    + " model=" + (string.IsNullOrEmpty(s.LastUsedModel) ? "unknown" : MarkerText.Encode(s.LastUsedModel))
                    + " uncached=" + (s.HasTokens ? s.UncachedInputTokens.ToString(i0c) : "unknown")
                    + " cacheRead=" + (s.HasTokens ? s.CacheReadTokens.ToString(i0c) : "unknown")
                    + " cacheWrite=" + (s.HasTokens ? s.CacheWriteTokens.ToString(i0c) : "unknown")
                    + " output=" + (s.HasTokens ? s.OutputTokens.ToString(i0c) : "unknown")
                    + " cost=" + SessionCost.Format(amt));
            }

            // —— today 块（今日 ✓：META + TOTAL；✗ 无逐会话行 ✓ 省输出 ✓ 规格 §3.1-② ✓）——
            EmitCostMeta("today", 0, todayText, todayText, "global", priced, tz, sc.UnknownLast, ModelUnknown(today));
            decimal? todayCost = EmitCostTotal("today", cfg, today);

            // —— 预算行（恒「今日 · level=global」✓）——
            bool limitSet = cfg.SessionsBudgetUsdPerDay > 0m;
            Console.WriteLine("COST_BUDGET limit=" + (limitSet ? SessionCost.Format(cfg.SessionsBudgetUsdPerDay) : "none")
                + " spent=" + SessionCost.Format(todayCost)
                + " over=" + (SessionCost.IsOverBudget(cfg, todayCost) ? "1" : "0"));

            // —— 说明行（可多条 ✓ 顺序不保证 ⇒ GUI 按"出现即累加"解析 ✓ 规格 §3.1-④ ✓）——
            if (!priced) Console.WriteLine("COST_NOTE price-missing");
            Console.WriteLine("COST_NOTE official-billing-not-compared");
            Console.WriteLine("COST_NOTE model-approximate");
            if (priced && unavailable > 0) Console.WriteLine("COST_NOTE session-cost-unavailable n=" + unavailable.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (sc.Level != "global") Console.WriteLine("COST_NOTE budget-scope-global");
            return 0;
        }

        /// <summary>COST_META（两个 scope 同一键序 ✓ 冻结 ✗ 不加键 ✗）。
        /// `days` / `from` / `to` 与 `SESSWIN_META` **同一写法**（0 → unknown ✓ 自定义区间打真实区间 ✓ 今日块 from=to=今天 ✓）。</summary>
        private static void EmitCostMeta(string scope, int days, string fromText, string toText, string level,
            bool priced, string tz, int unknownLast, int modelUnknown)
        {
            System.Globalization.CultureInfo i0c = System.Globalization.CultureInfo.InvariantCulture;
            Console.WriteLine("COST_META scope=" + scope
                + " days=" + (days > 0 ? days.ToString(i0c) : "unknown")
                + " from=" + fromText
                + " to=" + toText
                + " level=" + level
                + " priced=" + (priced ? "1" : "0")
                + " currency=USD"
                + " tz=" + tz
                + " truth=0"
                + " unknown_last=" + unknownLast.ToString(i0c)
                + " model_unknown=" + modelUnknown.ToString(i0c));
        }

        /// <summary>COST_TOTAL：**已知 token 的会话求和**（与 `SESSAGG_TOTAL` 同一欠账纪律 ✓ —— 未知的既不假装 0 也不静默漏：
        /// `COST_NOTE session-cost-unavailable n=N` 把"算不了"的笔数说出来 ✓）。
        /// 返回该 scope 的金额（未配价 → null ✓ **恒等于**各 `COST_SESSION` 金额之和 ✓ —— 同一份**未舍入**的十进制值逐笔相加 ✓
        /// ⇒ 数值完全相等 ✗ 不因显示写法（`0.00075` / `1.18E-07`）而对不上 ✓）。</summary>
        private static decimal? EmitCostTotal(string scope, ToolkitConfig cfg, List<SessionStat> set)
        {
            System.Globalization.CultureInfo i0c = System.Globalization.CultureInfo.InvariantCulture;
            long un = 0, cr = 0, cw = 0, op = 0;
            decimal? sum = SessionCost.IsPriced(cfg) ? (decimal?)0m : null;
            for (int i = 0; i < set.Count; i++)
            {
                SessionStat s = set[i];
                if (!s.HasTokens) continue;   // 未知 ⇒ 不进求和（欠账由 NOTE 明说 ✓）
                un += s.UncachedInputTokens; cr += s.CacheReadTokens; cw += s.CacheWriteTokens; op += s.OutputTokens;
                decimal? a = SessionCost.Amount(cfg, s.UncachedInputTokens, s.CacheReadTokens, s.CacheWriteTokens, s.OutputTokens);
                if (a.HasValue) sum = sum.Value + a.Value;
            }
            Console.WriteLine("COST_TOTAL scope=" + scope
                + " uncached=" + un.ToString(i0c)
                + " cacheRead=" + cr.ToString(i0c)
                + " cacheWrite=" + cw.ToString(i0c)
                + " output=" + op.ToString(i0c)
                + " tokens=" + (un + cr + cw + op).ToString(i0c)
                + " cost=" + SessionCost.Format(sum));
            return sum;
        }

        /// <summary>该集合里**最后使用模型未知**的会话数（`COST_META model_unknown=` ✓ GUI 脚注/近似说明的唯一来源 ✗ 不许自己数 ✓）。</summary>
        private static int ModelUnknown(List<SessionStat> set)
        {
            int n = 0;
            for (int i = 0; i < set.Count; i++) if (string.IsNullOrEmpty(set[i].LastUsedModel)) n++;
            return n;
        }
    }
}
