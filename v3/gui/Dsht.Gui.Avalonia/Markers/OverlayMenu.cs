using System;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>★ 悬浮窗右键菜单的三项动作（2026-10-28）。
    /// <para>**两档"关"必须一眼分清**：</para>
    /// <para>· <see cref="SessionHide"/> = **本次隐藏**：只隐藏窗口，**不写配置** ⇒ `overlay_enabled` 保持原值 ✓；
    ///   看板「置顶进度窗」或托盘菜单可再显示 ✓。</para>
    /// <para>· <see cref="PermanentClose"/> = **永久关闭**：写 `overlay_enabled=false` 落盘 ✓；
    ///   之后可在设置里重新开启 ✓（与看板按钮读写的**同一个键** ✓ 不是两套状态 ✗）。</para>
    /// <para>· <see cref="OpenMain"/> = 打开主窗口（不碰悬浮窗开关 ✓）。</para>
    /// <para>本文件是**纯判定 + 文案**（不依赖 Avalonia ⇒ LogicTests 直接钉死 ✓）：
    /// 文案只有这一处 ✓；"哪一档写盘"也可单测 ✓（改坏即红 ✓）。</para></summary>
    public enum OverlayMenuAction
    {
        SessionHide = 0,
        PermanentClose = 1,
        OpenMain = 2
    }

    public static class OverlayMenu
    {
        // —— 用户可见文案（唯一出处 ✓ 零 ✓/✗ 记号、零施工语气 ✓）——

        /// <summary>本次隐藏 · 主标题。</summary>
        public const string SessionHideTitle = "本次隐藏";
        /// <summary>本次隐藏 · 副标题（明确说"不改设置"✓）。</summary>
        public const string SessionHideDetail = "只藏到本次运行结束，不改设置；看板「置顶进度窗」或托盘菜单可再显示";
        /// <summary>永久关闭 · 主标题。</summary>
        public const string PermanentCloseTitle = "永久关闭";
        /// <summary>永久关闭 · 副标题（明确说"写入设置"与"可再开启"✓）。</summary>
        public const string PermanentCloseDetail = "写入设置并关闭；之后可在设置里重新开启";

        /// <summary>打开主窗口。</summary>
        public const string OpenMainTitle = "打开主窗口";

        /// <summary>本次隐藏的即时回执（✗ 不说成"已关闭" ✗）。</summary>
        public const string SessionHideToast = "已隐藏置顶进度窗（设置未改，可随时再显示）";
        /// <summary>永久关闭的即时回执（明确指出去哪里再开 ✓）。</summary>
        public const string PermanentCloseToast = "已永久关闭置顶进度窗；可在设置里重新开启";

        /// <summary>菜单顺序（确定性 ✓ 断言按它遍历 ✓）。</summary>
        public static readonly OverlayMenuAction[] Actions =
        {
            OverlayMenuAction.SessionHide, OverlayMenuAction.PermanentClose, OverlayMenuAction.OpenMain
        };

        /// <summary>主标题。</summary>
        public static string TitleOf(OverlayMenuAction a)
        {
            if (a == OverlayMenuAction.SessionHide) return SessionHideTitle;
            if (a == OverlayMenuAction.PermanentClose) return PermanentCloseTitle;
            return OpenMainTitle;
        }

        /// <summary>副标题（打开主窗口没有副标题 ✓ 不编 ✗）。</summary>
        public static string DetailOf(OverlayMenuAction a)
        {
            if (a == OverlayMenuAction.SessionHide) return SessionHideDetail;
            if (a == OverlayMenuAction.PermanentClose) return PermanentCloseDetail;
            return "";
        }

        /// <summary>★ 这一项会不会写 `overlay_enabled`（本次隐藏 **false** ✓ 永久关闭 **true** ✓
        /// 打开主窗口 false ✓）—— 这张表就是"两档不混"的机器判据 ✓。</summary>
        public static bool WritesConfig(OverlayMenuAction a)
        {
            return a == OverlayMenuAction.PermanentClose;
        }

        /// <summary>这一项会不会把悬浮窗藏起来（打开主窗口不藏 ✓）。</summary>
        public static bool HidesWindow(OverlayMenuAction a)
        {
            return a == OverlayMenuAction.SessionHide || a == OverlayMenuAction.PermanentClose;
        }

        /// <summary>★ 看板按钮文案（唯一出处 ✓）：
        /// 关 = 可打开；开但本次隐藏 = 可显示；开 = 可关掉。
        /// <para>★ "本次隐藏"不是第三套开关状态 —— 它只是**本次运行内的窗口可见性** ✓；
        /// 真正的持久化状态仍只有 `overlay_enabled` 一个键 ✓。</para></summary>
        public static string ButtonText(bool enabled, bool hiddenThisSession)
        {
            if (!enabled) return "置顶进度窗：关（点一下打开）";
            if (hiddenThisSession) return "置顶进度窗：本次已隐藏（点一下显示）";
            return "置顶进度窗：开（点一下关掉）";
        }

        /// <summary>托盘菜单项文案（仅托盘存在时用 ✓）。</summary>
        public const string TrayShowTitle = "显示悬浮窗";
        /// <summary>托盘里发现功能已被永久关闭时的如实说明（✗ 不悄悄改键 ✗）。</summary>
        public const string TrayDisabledToast = "置顶进度窗已在设置里关闭；请先在设置或看板顶部开启";

        /// <summary>文案是否含 ✓/✗ 记号（断言用 ✓ 用户可见文案必须为 false）。</summary>
        public static bool HasMark(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf('✓') >= 0 || text.IndexOf('✗') >= 0;
        }

        /// <summary>文案是否含施工语气（断言用 ✓ 用户可见文案必须为 false）。</summary>
        public static bool HasBuilderTone(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("本轮", StringComparison.Ordinal) >= 0
                || text.IndexOf("施工", StringComparison.Ordinal) >= 0
                || text.IndexOf("落实", StringComparison.Ordinal) >= 0
                || text.IndexOf("改动", StringComparison.Ordinal) >= 0;
        }
    }
}
