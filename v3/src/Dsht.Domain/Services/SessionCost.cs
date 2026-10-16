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
        // ★ 精度纪律（2026-10-16 修正 ✓ Lead 裁定）：**不做任何四舍五入** ✗
        //   ✗ 原来把金额 `Round(…, 6)` ⇒ 真值 1.18e-7 被写成 `0.000000` ⇒ 界面只能说"金额为 0" ✗ 但那不是 0 ✓
        //   ✓ 现在：保留 `sum / 1_000_000` 的**完整十进制值**，只在**输出**时用往返精度格式化（见 <see cref="Format"/>）：
        //     非零值**永远**不会渲染成 `0` / `0.000000` ✓ 大额仍给足精度（`24755.699751` ✓）✓
        //     小额给科学计数（`1.18E-07` ✓）—— GUI 侧用 `decimal.TryParse(NumberStyles.Float)` 解析 ⇒ 两种形态都认 ✓
        //   ★ 合计仍**恒等于**各会话金额之和（同一份未舍入值 ✓ 逐笔相加得出 ✓）✗ 不留对不上的缝 ✗

        /// <summary>四键是否**至少有一个**非 0（= 已配价 ✓）。全 0 = 未填 ⇒ 不显示任何金额 ✓（规格 §2 ✓）。</summary>
        public static bool IsPriced(ToolkitConfig c)
        {
            if (c == null) return false;
            return c.SessionsPriceInPerMTok > 0m
                || c.SessionsPriceCacheReadPerMTok > 0m
                || c.SessionsPriceCacheWritePerMTok > 0m
                || c.SessionsPriceOutPerMTok > 0m;
        }

        /// <summary>四桶 → 金额（USD，**不舍入** ✓ 完整十进制值）。**未配价 → null**（✗ 不假装 0 ✓）。
        /// 参数顺序与标记行一致：uncached / cacheRead / cacheWrite / output ✓。</summary>
        public static decimal? Amount(ToolkitConfig c, long uncached, long cacheRead, long cacheWrite, long output)
        {
            if (!IsPriced(c)) return null;
            decimal sum = uncached * c.SessionsPriceInPerMTok
                        + cacheRead * c.SessionsPriceCacheReadPerMTok
                        + cacheWrite * c.SessionsPriceCacheWritePerMTok
                        + output * c.SessionsPriceOutPerMTok;
            return sum / 1000000m;   // ✗ 不 Round ✗（舍入会把极小非零值变成 0 ⇒ 界面会说"没有金额" ✗）
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

        /// <summary>金额 → 标记行文本：**往返精度**（`G29` = 29 位有效数字，decimal 的完整精度 ✓
        /// **InvariantCulture** ✓ 机器可读 ✓ 不受区域设置影响 ✓）；null → `none` ✗ 绝不是 `unknown` ✓
        /// （契约只有两个取值：数字 | `none` ✓ —— `none` **只**表示"不可算/未配价" ✗ 绝不用 0 代替 ✓）。
        ///
        /// ★ 硬规则（2026-10-16 ✓ Lead 裁定）：**非零金额永远不许渲染成 `0` / `0.000000`** ✗
        ///   —— 把"很小"写成"没有"就是不诚实 ✓（GUI 侧另有一条同样的显示守卫 ✓ 两侧各守一道 ✓）。
        ///   实测形态（`.NET` decimal）：`0.00075m → "0.00075"` · `0.000000118m → "1.18E-07"` ·
        ///   `0m → "0"`（真零照实说 0 ✓）· `24755.699751m → "24755.699751"`（大额精度不减 ✓）
        ///   · `1m/3000000 → "3.333333333333333333333E-07"`（除不尽的循环小数保留 29 位有效数字 ✓ 仍非零 ✓）。</summary>
        public static string Format(decimal? v)
        {
            return v.HasValue ? v.Value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture) : "none";
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
