using Dsht.Platform.Shared;

namespace Dsht.Platform.Linux
{
    /// <summary>端口探测（Linux）：实现见 Dsht.Platform.Shared/ProbesCommon.cs。
    /// B-05 平台层去重：两平台原本逐行相同 ⇒ 编译同一份共享源，行为逐字不变。</summary>
    public sealed class LinuxPortProbe : PortProbeCommon
    {
    }

    /// <summary>HTTP 探测（Linux）：实现见 Dsht.Platform.Shared/ProbesCommon.cs。
    /// B-05 平台层去重：两平台原本逐行相同 ⇒ 编译同一份共享源，行为逐字不变。</summary>
    public sealed class LinuxHttpProbe : HttpProbeCommon
    {
    }
}
