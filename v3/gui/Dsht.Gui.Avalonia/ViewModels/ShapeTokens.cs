using Avalonia;
using Dsht.Gui.Avalonia.Design;

namespace Dsht.Gui.Avalonia.ViewModels
{
    /// <summary>形状 / 间距 / 字阶设计 token（样式重构 2026-10-10 · 视觉规范化 2026-10-26）：
    ///   **颜色、间距档、字号档、层级、动效时长都在 <see cref="DesignTokens"/>**（唯一出处 ✓）；
    ///   这里只留**形状**（圆角 / 图标 / 控件尺度）+ 把间距与字号**转成**同名常量，方便调用点沿用旧名字 ✓。
    ///   <para>2026-10-26 视觉规范化：字号从 6 档（10.5 / 11.5 / 13 / 15 / 20 / 24）**归并为 3 档**
    ///   （11 / 13 / 22 ✓ 见 <see cref="DesignTokens.FontSteps"/>）✗ 不再有 11.5 / 12.5 这种随手值 ✗；
    ///   间距档与 4 的倍数网格同源（<see cref="DesignTokens.SpacingSteps"/> ✓）。
    ///   只收敛表现层 ✗ 不动布局结构、信息层级、文案与四风格机制 ✓✓</para></summary>
    public static class ShapeTokens
    {
        // —— 圆角三档 + pill ——
        public const double RCard = 12;    // 卡片 / 对话框 / 浮层
        public const double RCtrl = 8;     // 控件：按钮、输入、下拉、细条、小磁贴
        public const double RInset = 9;    // 凹陷容器（segmented 底）——比控件大半档包住 RCtrl
        public const double RPill = 999;   // 胶囊：chip、徽章、状态灯

        // —— 间距节奏（4 的倍数；值来自 DesignTokens，唯一出处 ✓）——
        public const double S4 = DesignTokens.S4;      // 极紧：图标↔短标签
        public const double S8 = DesignTokens.S8;      // 紧凑：行内元素
        public const double S12 = DesignTokens.S12;    // 卡片内分组
        public const double S16 = DesignTokens.S16;    // 卡片之间 / 页面分区
        public const double S24 = DesignTokens.S24;    // 页面级留白

        // —— 字阶三档（由 10.5/11/11.5/12/12.5/13/13.5/14/15/20/22 归并；值来自 DesignTokens ✓）——
        public const double FsFoot = DesignTokens.FontSm;   // 脚注 / 徽章 / 表格次级
        public const double FsBody = DesignTokens.FontMd;   // 正文 / 按钮 / 下拉 / 行标题
        public const double FsEm = DesignTokens.FontMd;     // 强调 / 小节题 / 主按钮
        public const double FsTitle = DesignTokens.FontMd;  // 卡题 / 对话框题
        public const double FsPage = DesignTokens.FontLg;   // 页题
        public const double FsStat = DesignTokens.FontLg;   // 大数字（KPI / 体检头条）

        // —— 图标三档 ——
        public const double IcSm = 12;     // 行内小图标
        public const double IcMd = 14;     // 正文图标
        public const double IcNav = 15;    // 导航 / 工具行图标

        // —— 控件尺度 ——
        public const double CtrlH = 30;    // 工具行控件统一高（下拉/按钮严丝合缝的经验值 ✓）
        public const double Stroke = 1;    // 发丝描边（分层靠它，不靠粗边）

        /// <summary>统一卡片内边距。</summary>
        public static readonly Thickness CardPad = new Thickness(S16);

        /// <summary>统一页面外边距（左 上 右 下；全 4 倍数 ✓）。</summary>
        public static readonly Thickness PagePad = new Thickness(S24, S16, S24, S16);
    }
}
