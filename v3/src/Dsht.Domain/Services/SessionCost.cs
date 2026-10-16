using System;
using Dsht.Domain.Model;

namespace Dsht.Domain.Services
{
    /// <summary>会话成本的**纯函数**算法（成本面板 v3.2 · 2026-10-16 · 施工规格 §2 冻结口径 ✓）。
    ///
    /// 口径（逐字实现规格 §2 ✓，✗ 不许含糊 ✗）：
    ///   · 金额 = (uncached×输入价 + cacheRead×缓存读价 + cacheWrite×缓存写价 + output×输出价) ÷ 1,000,000，单位 **USD**
    ///   · 四桶 1:1 直取 `tokenUsage.val.totals.*`（同 `SESSAGG_SESSION` 的 uncached/cacheRead/cacheWrite/output ✓ 无换算 ✓）
    ///   · **算钱只用 `output`** ✗ 绝不用 `decodeTokens` —— 后者来自另一个投影单元（`sessionStats`）且
    ///     只在**测到 firstToken** 时才累加 ⇒ 会低估 ✗（规格 §2 / 勘察 §4.2 ✓）
    ///   · 四键**全 0 = 未填** ⇒ 不显示金额（`Amount` 返回 null ⇒ 标记行打 `none` ✓）
    ///     · 未填的单个键按 **0** 参与（估算会偏低 ⇒ `docs/COST.md` 如实写明 ✗ 不假装完整 ✓）
    ///   · **绝不硬编码任何价格** ✗ · **绝不新造价格键** ✗ —— 只读用户自填的四个 `sessions_price_*_per_mtok` ✓
    ///
    /// 领域层纯度（架构红线 ✓）：纯算术 ✓ 零 IO ✓ **零时钟** ✓（本地日换算在 CLI 侧 ✓ 与 SessionWindow 同一纪律）。</summary>
    public static class SessionCost
    {
        /// <summary>金额显示精度（小数位）。**逐会话金额按此四舍五入** ⇒ 合计 = **各会话显示值之和** ✓
        /// （GUI 把 `COST_SESSION` 的数字相加一定等于 `COST_TOTAL cost=` ✓ ✗ 不留最后一位对不上的缝 ✗）。</summary>
        public const int Decimals = 6;

        /// <summary>四键是否**至少有一个**非 0（= 已配价 ✓）。全 0 = 未填 ⇒ 不显示任何金额 ✓（规格 §2 ✓）。</summary>
        public static bool IsPriced(ToolkitConfig c)
        {
            if (c == null) return false;
            return c.SessionsPriceInPerMTok > 0m
                || c.SessionsPriceCacheReadPerMTok > 0m
                || c.SessionsPriceCacheWritePerMTok > 0m
                || c.SessionsPriceOutPerMTok > 0m;
        }

        /// <summary>四桶 → 金额（USD，四舍五入到 <see cref="Decimals"/> 位）。**未配价 → null**（✗ 不假装 0 ✓）。
        /// 参数顺序与标记行一致：uncached / cacheRead / cacheWrite / output ✓。</summary>
        public static decimal? Amount(ToolkitConfig c, long uncached, long cacheRead, long cacheWrite, long output)
        {
            if (!IsPriced(c)) return null;
            decimal sum = uncached * c.SessionsPriceInPerMTok
                        + cacheRead * c.SessionsPriceCacheReadPerMTok
                        + cacheWrite * c.SessionsPriceCacheWritePerMTok
                        + output * c.SessionsPriceOutPerMTok;
            return decimal.Round(sum / 1000000m, Decimals, MidpointRounding.AwayFromZero);
        }

        /// <summary>某会话的金额：
        /// · 未配价 → null（`priced=0` 已表达 ⇒ 打 `none` ✓）
        /// · **已配价但该会话 token 未知**（`HasTokens=false`，如行门丢弃/占比缺失）→ null ⇒ `none`
        ///   ✗ 不假装 0 ✓（0 是"真的没花"，未知是"算不了" ✗ 两件事 ✓）—— CLI 侧另计条数写进 `COST_NOTE` ✓。</summary>
        public static decimal? AmountFor(ToolkitConfig c, SessionStat s)
        {
            if (s == null || !s.HasTokens) return null;
            return Amount(c, s.UncachedInputTokens, s.CacheReadTokens, s.CacheWriteTokens, s.OutputTokens);
        }

        /// <summary>金额 → 标记行文本：固定小数点后 6 位、**InvariantCulture**（机器可读 ✓ 不受区域设置影响 ✓）；
        /// null → `none` ✗ 绝不是 `unknown` ✓（契约只有这两个取值之二：数字 | none）。</summary>
        public static string Format(decimal? v)
        {
            return v.HasValue ? v.Value.ToString("0." + new string('0', Decimals), System.Globalization.CultureInfo.InvariantCulture) : "none";
        }

        /// <summary>每日预算是否被**今日估算金额**超过。未设上限（0）或未配价（spent=null）⇒ false（不作判定 ✗ 不猜 ✓）。
        /// ★ 调用方保证 <paramref name="spent"/> 恒为 **level=global** 的今日金额（Lead 裁决 2026-10-16 ✓
        ///   非 global 口径会把父与子都算进来 ⇒ 重复计数 ⇒ 假超限 ✗）。</summary>
        public static bool IsOverBudget(ToolkitConfig c, decimal? spent)
        {
            if (c == null || !spent.HasValue) return false;
            if (c.SessionsBudgetUsdPerDay <= 0m) return false;
            return spent.Value > c.SessionsBudgetUsdPerDay;
        }
    }
}
