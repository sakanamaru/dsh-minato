using Dsht.Domain.Abstractions;
using Dsht.Platform.Shared;

namespace Dsht.Platform.Linux
{
    /// <summary>会话投影来源（Linux）：实现见 Dsht.Platform.Shared/SessionStatsSourceCommon.cs。
    /// B-05 平台层去重：两平台原本逐行相同 ⇒ 编译同一份共享源，行为逐字不变。</summary>
    public sealed class LinuxSessionStatsSource : SessionStatsSourceCommon
    {
        public LinuxSessionStatsSource(IPaths paths) : base(paths) { }
    }
}
