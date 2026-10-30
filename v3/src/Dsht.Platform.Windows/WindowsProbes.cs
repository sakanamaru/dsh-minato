using Dsht.Platform.Shared;

namespace Dsht.Platform.Windows
{
    /// <summary>端口探测（Windows）：实现见 Dsht.Platform.Shared/ProbesCommon.cs。
    /// B-05 平台层去重：两平台原本逐行相同 ⇒ 编译同一份共享源，行为逐字不变。</summary>
    public sealed class WindowsPortProbe : PortProbeCommon
    {
    }

    /// <summary>HTTP 探测（Windows）：实现见 Dsht.Platform.Shared/ProbesCommon.cs。
    /// B-05 平台层去重；RespondsCore 是 WindowsProcessQuery 直接用的历史方法（语义与 Responds 逐字相同，保留以不改调用点）。</summary>
    public sealed class WindowsHttpProbe : HttpProbeCommon
    {
        public bool RespondsCore(string url, int timeoutMs) { return Responds(url, timeoutMs); }
    }
}
