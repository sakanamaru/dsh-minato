using System;

namespace Dsht.Domain.Services
{
    /// <summary>B-04（2026-10-30）：多工作区备份包的 `_workspace` 布局完整性判定（纯函数，无 IO —— 枚举由调用方做）。
    /// <para>背景：≥2 个工作区时 CLI 逐个把工作区打包进 `_workspace/&lt;名称&gt;/.dshws`；
    /// 复制失败/中途中断会留下**没有 .dshws 的半截子目录**。恢复侧原来只按"有没有任何一个 .dshws"分流：
    /// 一个都没有 ⇒ 当**旧式单工作区**整棵并入工作区根 ⇒ 半截工作区被静默合并 ✗✗（B-04）。</para>
    /// <para>规则（fail-closed，配合现有 truncated/--force 机制使用）：</para>
    /// <list type="bullet">
    /// <item>没有子目录 ⇒ 放行（空 / 只有文件 = 旧式布局或空包，沿用旧语义）</item>
    /// <item>顶层**有文件** ⇒ 旧式布局（v2.x 的 `_workspace` 直接放内容）⇒ 放行（不误伤老包）</item>
    /// <item>顶层只有子目录且**全部**含 .dshws ⇒ 新格式完整 ⇒ 放行</item>
    /// <item>其余（部分缺 / 一个都缺）⇒ 判不完整（调用方应 RESTORE_FAIL / DRYRUN_FAIL，--force 才继续）</item>
    /// </list>
    /// 诚实边界：旧式包里若**顶层只有目录、没有任何文件**，会与新格式半截包不可区分 ⇒ 按不完整拦下（--force 可用）。
    /// 这是有意的 fail-closed：宁可让极老的包多一句 --force，也不能把半截工作区静默并进工作区根。</summary>
    public static class WorkspaceLayoutJudge
    {
        /// <summary>新格式里部分子目录缺 .dshws（高概率是打包中途失败）。</summary>
        public const string CodeMarkerMissing = "workspace-marker-missing";

        /// <summary>只有子目录、却一个 .dshws 都没有（无法证明是旧式包 ⇒ fail-closed）。</summary>
        public const string CodeNoMarkers = "workspace-no-markers";

        /// <summary>判定 `_workspace` 目录的布局完整性。返回 null = 放行；否则返回原因码（见 CodeMarkerMissing / CodeNoMarkers）。
        /// <para>参数由调用方从文件系统事实得出（本层零 IO）：</para>
        /// <list type="bullet">
        /// <item><paramref name="hasTopLevelFiles"/>：`_workspace` 顶层是否有文件</item>
        /// <item><paramref name="subdirCount"/>：`_workspace` 的直接子目录数</item>
        /// <item><paramref name="markedCount"/>：其中含 .dshws 标记的子目录数</item>
        /// </list></summary>
        public static string Judge(bool hasTopLevelFiles, int subdirCount, int markedCount)
        {
            if (subdirCount <= 0) return null;                 // 空 / 只有文件 ⇒ 旧式或空包，沿用旧语义
            if (hasTopLevelFiles) return null;                 // 顶层有文件 ⇒ 旧式布局（v2.x），兼容放行
            if (markedCount >= subdirCount) return null;       // 新格式：全部带标记 ⇒ 完整
            if (markedCount <= 0) return CodeNoMarkers;        // 只有子目录且一个标记都没有 ⇒ 无法证明是旧包
            return CodeMarkerMissing;                          // 部分有标记、部分没有 ⇒ 明显是半截包
        }
    }
}
