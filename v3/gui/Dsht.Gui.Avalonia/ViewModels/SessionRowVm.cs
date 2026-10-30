using System;
using Avalonia.Media;
using Dsht.Gui.Avalonia.Markers;
using Dsht.Gui.Avalonia.Design;

namespace Dsht.Gui.Avalonia.ViewModels
{
    /// <summary>配色与视觉 token（一处定义，全界面统一）。四个 demo 通过 Apply 切换：
    /// 0=浅色卡片 1=深色卡片 2=深色紧凑 3=浅色仪表盘。
    /// 所有界面元素的颜色都必须从这里取（包括窗口壳），不许再出现硬编码 White —— 否则深色风格必破。
    /// 做法参考 AuroraZiling/Hollow（MIT，同为 Avalonia）的"颜色集中在资源里"思路，颜色值是我们自己定的。</summary>
    public static class Palette
    {
        public static int StyleKind = 0;
        public static bool Dark;
        public static bool Compact;

        // —— 品牌 ——
        /// <summary>「什么都不画」（窗口底 / 光晕底 ✓ 值来自 token 表 ✓ ✗ 业务文件不许写 `Brushes.Transparent` ✗）。</summary>
        public static IBrush Transparent = B(DesignTokens.TransparentHex);
        public static IBrush Accent = Brushes.Transparent;      // 主强调色（链接、激活态、主按钮）
        public static IBrush AccentHover = Brushes.Transparent; // 主按钮悬停
        public static IBrush AccentSoft = Brushes.Transparent;  // accent 的浅色底（选中 pill、徽章底）
        public static IBrush OnAccent = Brushes.White;          // accent 底上的文字

        // —— 表面层级（低 → 高）——
        public static IBrush PageBg = Brushes.Transparent;      // 页面底
        public static IBrush WindowBgTop = Brushes.Transparent;    // 窗口渐变：上端
        public static IBrush WindowBgBottom = Brushes.Transparent; // 窗口渐变：下端
        public static IBrush SidebarBg = Brushes.Transparent;   // 侧栏/顶栏（略低于卡片）
        public static IBrush CardBg = Brushes.Transparent;      // 卡片
        public static IBrush CardHover = Brushes.Transparent;   // 卡片/列表项悬停
        public static IBrush InsetBg = Brushes.Transparent;     // 凹陷区（切换器底、代码块底）

        // —— 描边 ——
        public static IBrush Border = Brushes.Transparent;      // 发丝线
        public static IBrush BorderStrong = Brushes.Transparent;

        // —— 文字层级（三档：主 / 次 / 弱 ✓ 色阶来自 token 表 ✓ 不是透明度随手调 ✓）——
        public static IBrush Text = Brushes.Transparent;        // 主文字
        public static IBrush TextDim = Brushes.Transparent;     // 次要
        public static IBrush TextFaint = Brushes.Transparent;   // 提示/占位

        // —— 语义色（占位；真值由 Apply 从 token 表灌入 ✗ 这里不许写死色值 ✗）——
        public static IBrush Good = Brushes.Transparent;
        public static IBrush Warn = Brushes.Transparent;
        public static IBrush Bad = Brushes.Transparent;
        public static IBrush Idle = Brushes.Transparent;
        public static IBrush Muted = Brushes.Transparent;
        public static IBrush GoodSoft = Brushes.Transparent;
        public static IBrush WarnSoft = Brushes.Transparent;
        public static IBrush BadSoft = Brushes.Transparent;

        public static IBrush BarTrack = Brushes.Transparent;
        public static IBrush FormAcp = Brushes.Transparent;

        /// <summary>卡片投影（BoxShadows.Parse 格式）；深色风格为空串 = 不投影（靠描边分层）。</summary>
        public static string CardShadow = "";

        static Palette() { Apply(0); }

        /// <summary>按风格把 <see cref="DesignTokens"/> 的取值灌成画刷（全界面唯一的取色入口 ✓）。
        /// ✗ 本方法里不许出现任何色值字面量 ✗ —— 要改颜色 ⇒ 改 token 表 ✓（有源码扫描断言 ✓）。</summary>
        public static void Apply(int kind)
        {
            StyleKind = DesignTokens.Clamp(kind);
            DesignTokens.StyleSet s = DesignTokens.Style(StyleKind);
            Dark = s.Mode == DesignTokens.Mode.Dark;
            Compact = StyleKind == 2;

            Accent = B(s.Accent);
            AccentHover = B(s.AccentHover);
            AccentSoft = B(s.AccentSoft);
            OnAccent = B(s.OnAccent);
            PageBg = B(s.PageBg);
            WindowBgTop = B(s.WindowBgTop);
            WindowBgBottom = B(s.WindowBgBottom);
            SidebarBg = B(s.SidebarBg);
            CardBg = B(s.CardBg);
            CardHover = B(s.CardHover);
            InsetBg = B(s.InsetBg);
            Border = B(s.Border);
            BorderStrong = B(s.BorderStrong);
            Text = B(s.Text);
            TextDim = B(s.TextDim);
            TextFaint = B(s.TextFaint);
            Good = B(s.Normal);
            Warn = B(s.Attention);
            Bad = B(s.Abnormal);
            Idle = B(s.Unknown);
            Muted = B(s.Muted);
            GoodSoft = B(s.NormalSoft);
            WarnSoft = B(s.AttentionSoft);
            BadSoft = B(s.AbnormalSoft);
            BarTrack = B(s.BarTrack);
            FormAcp = B(s.FormAcp);
            CardShadow = s.CardShadow;
        }

        /// <summary>`#RRGGBB` / `#RRGGBBAA` → 画刷（token 表 → 界面 的**唯一**转换点 ✓）。</summary>
        private static IBrush B(string hex) { return new SolidColorBrush(Color.Parse(hex)); }

        public static string StyleName(int kind) { return DesignTokens.Style(kind).Name; }

        public static IBrush HitBrush(int level) { return level >= 3 ? Good : (level == 2 ? Warn : (level == 1 ? Bad : Muted)); }
        public static IBrush CtxBrush(int level) { return level == 1 ? Good : (level == 2 ? Warn : (level >= 3 ? Bad : Muted)); }
        public static IBrush StatusBrush(int kind) { return kind == 0 ? Good : (kind == 2 ? Muted : Idle); }
        // ★ 2026-10-26 状态灯接 token 表：原来的 `LightBrush(LightColor)`（四色灯的**第二份配色出口**，
        //   直接给纯饱和色 ✓）已**删除** ✗ —— 四色灯现在只有一条路：
        //   `OverlayLight.SemanticOf(c)`（语义色 ✓）→ `Design/StatusLight.Create(…)`（**唯一画法**：
        //   柔光晕 + 去饱和底色 + 半透明灯环 ✓）→ 色值全部来自 `DesignTokens` ✓。
        //   ✗ 不许在这里再加回"按灯色直接给一个实心色"的映射 ✗（源码级断言盯着 `LightBrush` 这个名字 ✓）。
        public static IBrush FormBrush(int kind) { return kind == 0 ? Accent : (kind == 1 ? Idle : (kind == 2 ? FormAcp : Warn)); }
        /// <summary>语义色的软底（徽章/光环用）。</summary>
        public static IBrush SoftOf(IBrush b)
        {
            if (b == Good) return GoodSoft;
            if (b == Warn) return WarnSoft;
            if (b == Bad) return BadSoft;
            if (b == Accent) return AccentSoft;
            return InsetBg;
        }
    }

    /// <summary>会话列表的一行（界面视图模型）。纯数据/格式化仍在 Markers 里（可无图形环境单测），
    /// 这里只负责把语义等级翻译成画刷，供界面直接绑定。</summary>
    public sealed class SessionRowVm
    {
        public readonly SessionRow Row;

        public SessionRowVm(SessionRow row) { Row = row; }

        public string ShortId { get { return Row.ShortId; } }
        public string TitleText { get { return Row.TitleText; } }
        public string StatusText { get { return Row.LiveText; } }
        /// <summary>运行态是否已知（未知时状态点用中性色，避免"灰=已结束"的误导）。</summary>
        public bool LiveKnown { get { return Row.LiveKnown; } }
        public IBrush StatusBrush { get { return Palette.StatusBrush(Row.StatusKind); } }
        public string MetaText
        {
            get
            {
                return Row.TurnsText + "　最后活动 " + Row.LastShort
                    + (string.IsNullOrEmpty(Row.CreatedShort) ? "" : "　创建 " + Row.CreatedShort);
            }
        }

        public string InText { get { return Row.InText; } }
        public string OutText { get { return Row.OutText; } }
        public string CacheReadText { get { return Row.CacheReadText; } }

        public string HitText { get { return Row.HitText; } }
        public double HitBar { get { return Row.HitBar; } }
        public IBrush HitBrush { get { return Palette.HitBrush(Row.HitLevel); } }

        public string CtxText { get { return Row.CtxText; } }
        public double CtxBar { get { return Row.CtxBar; } }
        public IBrush CtxBrush { get { return Palette.CtxBrush(Row.CtxLevel); } }

        public string DecodeText { get { return Row.DecodeText; } }

        /// <summary>解码速度与首 token 合成一行（避免 XAML 里用 Run 绑定，减少解析风险）。</summary>
        public string DecodeLine { get { return "解码 " + Row.DecodeText + "　首 token " + Row.TtftText; } }
        public string TtftText { get { return Row.TtftText; } }

        public double TokenBar { get { return Row.TokenBar; } }
        public IBrush TokenBrush { get { return Palette.Accent; } }

        /// <summary>「含子代理」小字行（看板第三批 · 2026-10-09 ✓✓ 规格 §11.7-E-3 ✓✓）：
        /// 空串 = 该会话无 sub 行（不显示 ✓）；文案由 SessionsMarkers.SubLineText 统一产出（唯一出处 ✓ 可单测 ✓）。
        /// 由 VM 构造处从 host.Data.SubById 查表填入（GUI 不算血缘 ✓ 只消费 CLI 标记 ✓✓）。</summary>
        public string SubLine = "";
    }
}
