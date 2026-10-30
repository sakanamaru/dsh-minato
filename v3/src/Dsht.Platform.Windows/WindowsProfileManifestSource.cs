using Dsht.Domain.Abstractions;
using Dsht.Platform.Shared;

namespace Dsht.Platform.Windows
{
    /// <summary>profile 清单来源（Windows）：实现见 Dsht.Platform.Shared/ProfileManifestSourceCommon.cs。
    /// B-05 平台层去重：两平台原本逐行相同 ⇒ 编译同一份共享源，行为逐字不变。</summary>
    public sealed class WindowsProfileManifestSource : ProfileManifestSourceCommon
    {
        public WindowsProfileManifestSource(IPaths paths) : base(paths) { }
    }
}
