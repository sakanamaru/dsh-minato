using Dsht.Domain.Abstractions;
using Dsht.Platform.Shared;

namespace Dsht.Platform.Windows
{
    /// <summary>日志来源（Windows）：实现见 Dsht.Platform.Shared/LogSourceCommon.cs。
    /// B-05 平台层去重：状态目录/logs/launcher.log，与 Linux 同一份代码（行为逐字不变）。</summary>
    public sealed class WindowsLogSource : LogSourceCommon
    {
        public WindowsLogSource(IPaths paths) : base(paths) { }
    }
}
