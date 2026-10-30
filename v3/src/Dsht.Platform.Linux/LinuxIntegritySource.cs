using Dsht.Platform.Shared;

namespace Dsht.Platform.Linux
{
    /// <summary>自身完整性数据源（Linux）：实现见 Dsht.Platform.Shared/IntegritySourceCommon.cs。
    /// B-05 平台层去重：两平台原本逐行相同（纯 BCL）⇒ 编译同一份共享源，行为逐字不变。</summary>
    public sealed class LinuxIntegritySource : IntegritySourceCommon
    {
    }
}
