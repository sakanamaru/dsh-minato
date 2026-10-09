using Avalonia;

namespace Dsht.Gui.Avalonia.ViewModels
{
    /// <summary>形状 / 间距 / 字阶设计 token（样式重构 2026-10-10 · KIMI 美学）：
    ///   颜色在 <see cref="Palette"/>，**形状在这里** —— 与颜色同级的唯一出处 ✓。
    ///   此前圆角 6/7/8/9/10/12 混用、字阶 9.5~24 共 14 档、间距 2~14 共 12 档 ✗
    ///   收敛为：圆角 3 档 + pill、字阶 6 档、间距 4 的倍数节奏（4/8/12/16/24）。
    ///   只收敛表现层 ✗ 不动布局结构、信息层级、文案与四风格机制 ✓✓</summary>
    public static class ShapeTokens
    {
        // —— 圆角三档 + pill ——
        public const double RCard = 12;    // 卡片 / 对话框 / 浮层
        public const double RCtrl = 8;     // 控件：按钮、输入、下拉、细条、小磁贴
        public const double RInset = 9;    // 凹陷容器（segmented 底）——比控件大半档包住 RCtrl
        public const double RPill = 999;   // 胶囊：chip、徽章、状态灯

        // —— 间距节奏（4 的倍数）——
        public const double S4 = 4;        // 极紧：图标↔短标签
        public const double S8 = 8;        // 紧凑：行内元素
        public const double S12 = 12;      // 卡片内分组
        public const double S16 = 16;      // 卡片之间 / 页面分区
        public const double S24 = 24;      // 页面级留白

        // —— 字阶六档（由 9.5/10/10.5/11/11.5/12/12.5/13/13.5/14/15/16/18/20/24 收敛）——
        public const double FsFoot = 10.5; // 注脚 / 徽章 / 表格次级
        public const double FsBody = 11.5; // 正文 / 按钮 / 下拉
        public const double FsEm = 13;     // 强调 / 小节题 / 主按钮
        public const double FsTitle = 15;  // 卡题 / 对话框题
        public const double FsPage = 20;   // 页题
        public const double FsStat = 24;   // 大数字（KPI / 体检头条）

        // —— 图标三档 ——
        public const double IcSm = 12;     // 行内小图标
        public const double IcMd = 14;     // 正文图标
        public const double IcNav = 15;    // 导航 / 工具行图标

        // —— 控件尺度 ——
        public const double CtrlH = 30;    // 工具行控件统一高（下拉/按钮严丝合缝的经验值 ✓）
        public const double Stroke = 1;    // 发丝描边（分层靠它，不靠粗边）

        /// <summary>统一卡片内边距。</summary>
        public static readonly Thickness CardPad = new Thickness(S16);

        /// <summary>统一页面外边距（左 上 右 下；原 24,18,24,16 → 全 4 倍数）。</summary>
        public static readonly Thickness PagePad = new Thickness(S24, S16, S24, S16);
    }
}
