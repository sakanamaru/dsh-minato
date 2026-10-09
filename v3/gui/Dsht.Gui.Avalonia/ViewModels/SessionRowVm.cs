using System;
using Avalonia.Media;
using Dsht.Gui.Avalonia.Markers;

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
        public static IBrush Accent = Brushes.Transparent;      // 主强调色（链接、激活态、主按钮）
        public static IBrush AccentHover = Brushes.Transparent; // 主按钮悬停
        public static IBrush AccentSoft = Brushes.Transparent;  // accent 的浅色底（选中 pill、徽章底）
        public static IBrush OnAccent = Brushes.White;          // accent 底上的文字

        // —— 表面层级（低 → 高）——
        public static IBrush PageBg = Brushes.Transparent;      // 页面底
        public static IBrush SidebarBg = Brushes.Transparent;   // 侧栏/顶栏（略低于卡片）
        public static IBrush CardBg = Brushes.Transparent;      // 卡片
        public static IBrush CardHover = Brushes.Transparent;   // 卡片/列表项悬停
        public static IBrush InsetBg = Brushes.Transparent;     // 凹陷区（切换器底、代码块底）

        // —— 描边 ——
        public static IBrush Border = Brushes.Transparent;      // 发丝线
        public static IBrush BorderStrong = Brushes.Transparent;

        // —— 文字层级 ——
        public static IBrush Text = Brushes.Transparent;        // 主文字
        public static IBrush TextDim = Brushes.Transparent;     // 次要
        public static IBrush TextFaint = Brushes.Transparent;   // 提示/占位

        // —— 语义色（四风格通用，只在 Apply 里微调深浅）——
        public static IBrush Good = new SolidColorBrush(Color.Parse("#16A34A"));
        public static IBrush Warn = new SolidColorBrush(Color.Parse("#B45309"));
        public static IBrush Bad = new SolidColorBrush(Color.Parse("#DC2626"));
        public static IBrush Idle = new SolidColorBrush(Color.Parse("#8A919C"));
        public static IBrush Muted = new SolidColorBrush(Color.Parse("#C9CDD4"));
        public static IBrush GoodSoft = new SolidColorBrush(Color.Parse("#E2F4E8"));
        public static IBrush WarnSoft = new SolidColorBrush(Color.Parse("#FBEFD8"));
        public static IBrush BadSoft = new SolidColorBrush(Color.Parse("#FAE3E3"));

        public static IBrush BarTrack = Brushes.Transparent;
        public static IBrush FormAcp = new SolidColorBrush(Color.Parse("#7C5CFC"));

        /// <summary>卡片投影（BoxShadows.Parse 格式）；深色风格为空串 = 不投影（靠描边分层）。</summary>
        public static string CardShadow = "";

        static Palette() { Apply(0); }

        public static void Apply(int kind)
        {
            StyleKind = kind;
            Dark = kind == 1 || kind == 2;
            Compact = kind == 2;

            if (kind == 2)
            {
                // C · 深色紧凑：密度优先的控制台，青色 accent
                Accent = new SolidColorBrush(Color.Parse("#2DD4BF"));
                AccentHover = new SolidColorBrush(Color.Parse("#5FE3D0"));
                AccentSoft = new SolidColorBrush(Color.Parse("#143B37"));
                OnAccent = new SolidColorBrush(Color.Parse("#052622"));
                PageBg = new SolidColorBrush(Color.Parse("#0A0C0E"));
                SidebarBg = new SolidColorBrush(Color.Parse("#0C0F12"));
                CardBg = new SolidColorBrush(Color.Parse("#0F1318"));
                CardHover = new SolidColorBrush(Color.Parse("#151B22"));
                InsetBg = new SolidColorBrush(Color.Parse("#08090B"));
                Border = new SolidColorBrush(Color.Parse("#1F262E"));
                BorderStrong = new SolidColorBrush(Color.Parse("#2C3540"));
                Text = new SolidColorBrush(Color.Parse("#D9DFE8"));
                TextDim = new SolidColorBrush(Color.Parse("#93A0AE"));
                TextFaint = new SolidColorBrush(Color.Parse("#5F6B78"));
                Good = new SolidColorBrush(Color.Parse("#4ADE80"));
                Warn = new SolidColorBrush(Color.Parse("#F2C14E"));
                Bad = new SolidColorBrush(Color.Parse("#F2707C"));
                Idle = new SolidColorBrush(Color.Parse("#5F6B78"));
                Muted = new SolidColorBrush(Color.Parse("#3E4854"));
                GoodSoft = new SolidColorBrush(Color.Parse("#133327"));
                WarnSoft = new SolidColorBrush(Color.Parse("#38300F"));
                BadSoft = new SolidColorBrush(Color.Parse("#3B1A1E"));
                BarTrack = new SolidColorBrush(Color.Parse("#1C232B"));
                FormAcp = new SolidColorBrush(Color.Parse("#A78BFA"));
                CardShadow = "";
            }
            else if (kind == 1)
            {
                // B · 深色卡片：石墨底 + 亮靛蓝 accent
                Accent = new SolidColorBrush(Color.Parse("#7C8DFF"));
                AccentHover = new SolidColorBrush(Color.Parse("#97A4FF"));
                AccentSoft = new SolidColorBrush(Color.Parse("#2A3558"));
                OnAccent = Brushes.White;
                PageBg = new SolidColorBrush(Color.Parse("#0E1013"));
                SidebarBg = new SolidColorBrush(Color.Parse("#12151A"));
                CardBg = new SolidColorBrush(Color.Parse("#161A20"));
                CardHover = new SolidColorBrush(Color.Parse("#1C222B"));
                InsetBg = new SolidColorBrush(Color.Parse("#0B0D10"));
                Border = new SolidColorBrush(Color.Parse("#262C37"));
                BorderStrong = new SolidColorBrush(Color.Parse("#343D4B"));
                Text = new SolidColorBrush(Color.Parse("#E8EAF0"));
                TextDim = new SolidColorBrush(Color.Parse("#A6ADBB"));
                TextFaint = new SolidColorBrush(Color.Parse("#9AA3B2"));
                Good = new SolidColorBrush(Color.Parse("#3FB97F"));
                Warn = new SolidColorBrush(Color.Parse("#F0B35E"));
                Bad = new SolidColorBrush(Color.Parse("#EF6A6A"));
                Idle = new SolidColorBrush(Color.Parse("#9AA3B2"));
                Muted = new SolidColorBrush(Color.Parse("#46505E"));
                GoodSoft = new SolidColorBrush(Color.Parse("#17352A"));
                WarnSoft = new SolidColorBrush(Color.Parse("#3A2E18"));
                BadSoft = new SolidColorBrush(Color.Parse("#3D1F1F"));
                BarTrack = new SolidColorBrush(Color.Parse("#232A35"));
                FormAcp = new SolidColorBrush(Color.Parse("#9D8CFF"));
                CardShadow = "";
            }
            else if (kind == 3)
            {
                // D · 浅色仪表盘：更冷的蓝灰底，把可视化放大
                Accent = new SolidColorBrush(Color.Parse("#2F6BFF"));
                AccentHover = new SolidColorBrush(Color.Parse("#2059DB"));
                AccentSoft = new SolidColorBrush(Color.Parse("#E4ECFF"));
                OnAccent = Brushes.White;
                PageBg = new SolidColorBrush(Color.Parse("#EEF2F9"));
                SidebarBg = new SolidColorBrush(Color.Parse("#F7F9FC"));
                CardBg = Brushes.White;
                CardHover = new SolidColorBrush(Color.Parse("#F4F7FC"));
                InsetBg = new SolidColorBrush(Color.Parse("#E8EEF7"));
                Border = new SolidColorBrush(Color.Parse("#E2E7F0"));
                BorderStrong = new SolidColorBrush(Color.Parse("#D0D8E4"));
                Text = new SolidColorBrush(Color.Parse("#131C2B"));
                TextDim = new SolidColorBrush(Color.Parse("#55617A"));
                TextFaint = new SolidColorBrush(Color.Parse("#6B7280"));
                Good = new SolidColorBrush(Color.Parse("#0EA36B"));
                Warn = new SolidColorBrush(Color.Parse("#B45309"));
                Bad = new SolidColorBrush(Color.Parse("#E5484D"));
                Idle = new SolidColorBrush(Color.Parse("#6B7280"));
                Muted = new SolidColorBrush(Color.Parse("#CBD3DF"));
                GoodSoft = new SolidColorBrush(Color.Parse("#DFF2E9"));
                WarnSoft = new SolidColorBrush(Color.Parse("#FBEFD5"));
                BadSoft = new SolidColorBrush(Color.Parse("#FBE5E5"));
                BarTrack = new SolidColorBrush(Color.Parse("#E6EBF3"));
                FormAcp = new SolidColorBrush(Color.Parse("#8B5CF6"));
                CardShadow = "0 1 2 0 #1B2A4A14";
            }
            else
            {
                // A · 浅色卡片（默认）：暖灰底 + 品牌蓝
                Accent = new SolidColorBrush(Color.Parse("#4D6BFE"));
                AccentHover = new SolidColorBrush(Color.Parse("#3B57E8"));
                AccentSoft = new SolidColorBrush(Color.Parse("#E9EEFF"));
                OnAccent = Brushes.White;
                PageBg = new SolidColorBrush(Color.Parse("#F5F6F8"));
                SidebarBg = new SolidColorBrush(Color.Parse("#FCFCFD"));
                CardBg = Brushes.White;
                CardHover = new SolidColorBrush(Color.Parse("#F6F7FA"));
                InsetBg = new SolidColorBrush(Color.Parse("#EFF1F5"));
                Border = new SolidColorBrush(Color.Parse("#E5E7EB"));
                BorderStrong = new SolidColorBrush(Color.Parse("#D6D9E0"));
                Text = new SolidColorBrush(Color.Parse("#151A23"));
                TextDim = new SolidColorBrush(Color.Parse("#5B6270"));
                TextFaint = new SolidColorBrush(Color.Parse("#6B7280"));
                Good = new SolidColorBrush(Color.Parse("#16A34A"));
                Warn = new SolidColorBrush(Color.Parse("#B45309"));
                Bad = new SolidColorBrush(Color.Parse("#DC2626"));
                Idle = new SolidColorBrush(Color.Parse("#8A919C"));
                Muted = new SolidColorBrush(Color.Parse("#C9CDD4"));
                GoodSoft = new SolidColorBrush(Color.Parse("#E2F4E8"));
                WarnSoft = new SolidColorBrush(Color.Parse("#FBEFD8"));
                BadSoft = new SolidColorBrush(Color.Parse("#FAE3E3"));
                BarTrack = new SolidColorBrush(Color.Parse("#ECEEF2"));
                FormAcp = new SolidColorBrush(Color.Parse("#7C5CFC"));
                CardShadow = "0 1 2 0 #10182812";
            }
        }

        public static string StyleName(int kind)
        {
            if (kind == 1) return "B · 深色卡片";
            if (kind == 2) return "C · 深色紧凑";
            if (kind == 3) return "D · 浅色仪表盘";
            return "A · 浅色卡片";
        }

        public static IBrush HitBrush(int level) { return level >= 3 ? Good : (level == 2 ? Warn : (level == 1 ? Bad : Muted)); }
        public static IBrush CtxBrush(int level) { return level == 1 ? Good : (level == 2 ? Warn : (level >= 3 ? Bad : Muted)); }
        public static IBrush StatusBrush(int kind) { return kind == 0 ? Good : (kind == 2 ? Muted : Idle); }
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
