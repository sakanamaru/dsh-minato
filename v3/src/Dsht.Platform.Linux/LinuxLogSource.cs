using Dsht.Domain.Abstractions;
using Dsht.Platform.Shared;

namespace Dsht.Platform.Linux
{
    /// <summary>日志来源（Linux）：实现见 Dsht.Platform.Shared/LogSourceCommon.cs。
    /// B-05 平台层去重：状态目录/logs/launcher.log，与 Windows 同一份代码（行为逐字不变）。</summary>
    public sealed class LinuxLogSource : LogSourceCommon
    {
        public LinuxLogSource(IPaths paths) : base(paths) { }
    }
}
