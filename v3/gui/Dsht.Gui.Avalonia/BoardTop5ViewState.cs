namespace Dsht.Gui.Avalonia
{
    /// <summary>★ 看板「输入 Top 5」的**会话标题开关** —— **纯视图态**（2026-10-26 收尾 (b)）。
    /// <para>与 <see cref="MainWindow.BoardMoreOpen"/> **同性质**：本次会话内有效，**不写配置**、
    /// **不新增配置键**、不落盘。要持久化就得四处同步（ToolkitConfig / ConfigCodec / ConfigValidator /
    /// CONFIG 打印）—— 那不在本次范围内，所以这里是**有意不持久化**。</para>
    /// <para>默认值取自 <see cref="Markers.BoardHierarchy.TitleSwitchDefaultOn"/>（= 关）：会话标题是
    /// **模型从对话内容派生**的，属提示词派生数据，所以默认屏蔽。</para>
    /// <para>单独放一个文件是为了**不动** MainWindow.axaml.cs 的其余部分（同一个 partial 类）。
    /// 切换走字段级刷新，不整页重建。</para>
    /// </summary>
    public partial class MainWindow
    {
        private bool _boardTop5ShowTitle = Markers.BoardHierarchy.TitleSwitchDefaultOn;

        /// <summary>「输入 Top 5」卡是否显示会话标题（**默认关**）。</summary>
        public bool BoardTop5ShowTitle { get { return _boardTop5ShowTitle; } }

        /// <summary>切换标题开关（卡上的入口调它）：纯视图态，走字段级刷新，不整页重建。</summary>
        public void ToggleBoardTop5ShowTitle()
        {
            _boardTop5ShowTitle = !_boardTop5ShowTitle;
            RefreshLiveFields();
        }
    }
}
