using System;
using Dsht.Domain.Model;

namespace Dsht.Domain.Services
{
    /// <summary>看板第一批 · 日期窗口过滤（纯函数 ✓✓）。
    /// ★ 时区纪律：本地日边界的换算在 **CLI 侧**完成（TimeZoneInfo 取偏移，夏令时逐日正确 ✓）；
    ///   本类只做 epoch 毫秒区间比较 —— 领域层不碰时钟、不碰时区（架构红线 ✓）。
    /// ★ 口径（施工规格 §4.2 / C.4）：按**会话最后活动时间**（lastPromptAt）筛选 ✓；
    ///   用量为**整会话累计** ✗ 不是窗口内增量 —— C.4 脚注随 truth=0 常显 ✓。</summary>
    public static class SessionWindow
    {
        /// <summary>会话是否落在窗口内：[fromMsInclusive, toMsExclusive) —— 左闭右开（本地日 00:00 → 次日 00:00）。
        /// lastPromptMs ≤ 0（缺时间锚，如实按"未知"处理 ✓）→ false：不知道就是不进窗口 ✗ 绝不猜 ✓。</summary>
        public static bool Accept(long lastPromptMs, long fromMsInclusive, long toMsExclusive)
        {
            if (lastPromptMs <= 0) return false;
            return lastPromptMs >= fromMsInclusive && lastPromptMs < toMsExclusive;
        }

        /// <summary>会话的"有效最后活动"毫秒：投影给了 epoch 毫秒就直接用；
        /// 插件快照只给 ISO 字符串（ParseSnapshot 不填 epoch ✓ 实测形状）→ 在这里解析回毫秒。
        /// 两者都没有 → 0（未知 ✓）。纯函数：固定纪元 ✓ 不读时钟 ✓ 不带本机时区 ✓。</summary>
        public static long EffectiveLastPromptMs(SessionStat s)
        {
            if (s == null) return 0;
            if (s.LastPromptEpochMs > 0) return s.LastPromptEpochMs;
            if (s.LastPromptAt == null || s.LastPromptAt.Length == 0) return 0;
            DateTime t;
            if (!DateTime.TryParse(s.LastPromptAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out t))
                return 0;
            return (long)(t - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }
    }
}
