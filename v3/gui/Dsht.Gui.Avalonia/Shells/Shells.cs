using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using Dsht.Gui.Avalonia.Markers;
using Dsht.Gui.Avalonia.ViewModels;

namespace Dsht.Gui.Avalonia.Shells
{
    /// <summary>五种**布局框架**（外壳），同一个程序里实时切换，方便对比挑选。
    /// ⑤ 混合式 = ① 侧栏（主菜单）+ ② 顶部标签（子菜单）；子菜单不是摆设：会话页里它直接切换排序视角。
    /// 内容构建器（KPI / 会话卡片 / 工具栏 / 说明区）被所有外壳共享 —— 换外壳不动内容，换内容不动外壳。
    /// 所有颜色一律取自 <see cref="Palette"/>（含卡片底），四个风格才不会破。
    /// 设计方向参考了 March7thAssistant（GPL-3.0）的做法，**未复制其任何代码、图标、字体或图片资源**。</summary>
    public static class Shells
    {
        public const int Sidebar = 0;
        public const int TopTabs = 1;
        public const int CardGrid = 2;
        /// <summary>侧栏底部那组按钮（`web` / `desktop` / `安装` / `停 web` / `停桌面端`）的**统一宽度** ✓✓
        /// 用户反馈（2026-09-30）：「概览里五个按钮宽度不一样」✓ —— 它们原来都是**文字宽度** ✗。
        /// 76 能放下最长的 `desktop` ✓ 短文字（`web` ✓ `安装` ✓）被撑到同宽 ✓✓。</summary>
        private const double ModeBtnW = 62;   // MAJOR FIX: 76 overflowed the 232px sidebar (web+desktop+install+padding+3 gaps); 62 keeps the row inside it
        public const int MasterDetail = 3;
        public const int Hybrid = 4;

        private static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono,Consolas,Noto Sans Mono CJK SC,Noto Sans CJK SC,WenQuanYi Zen Hei,Source Han Sans SC,DejaVu Sans Mono,monospace");   // 等宽字体普遍无 CJK 字形 → 补回退列表 ✓（缺字形时才回退，ASCII 仍是等宽 ✓）
        private static readonly Thickness PageMargin = new Thickness(24, 18, 24, 16);

        public static string Name(int id)
        {
            switch (id)
            {
                case Sidebar: return "① 侧栏式";
                case TopTabs: return "② 顶部标签式";
                case CardGrid: return "③ 卡片网格仪表盘";
                case MasterDetail: return "④ 主从式";
                case Hybrid: return "⑤ 混合式（主菜单+子菜单）";
                default: return "未知";
            }
        }

        public static Control Build(int id, MainWindow host)
        {
            switch (id)
            {
                case TopTabs: return BuildTopTabs(host);
                case CardGrid: return BuildCardGrid(host);
                case MasterDetail: return BuildHybrid(host);   // ✓ 用户要求删除主从式 ✓ 暂以混合式代替（下面会把按钮也去掉 ✓）
                case Hybrid: return BuildHybrid(host);
                default: return BuildSidebar(host);
            }
        }

        // ================================================================ 基础构件

        /// <summary>把 UI 文案里的 **Markdown 粗体标记**剥掉 ✓✓
        ///   ✗ 我在文案里到处写 `**重点**` ✗ 而 **Avalonia 不解析 Markdown** ✗ → 屏幕上原样显示 `**` ✗✗
        ///     （用户截图确认：「**本工具不再提供清除数据**」的星号显示出来了 ✓）
        ///   ✓ 在 `T()` 里统一剥掉 ✓✓ —— **一处修全部** ✓（所有 UI 文案都走 T() ✓）</summary>
        internal static string StripMd(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("**", StringComparison.Ordinal) < 0) return s;
            return s.Replace("**", "");
        }

        private static TextBlock T(string text, double size, IBrush fg)
        {
            return new TextBlock { Text = StripMd(text), FontSize = size, Foreground = fg };
        }

        private static TextBlock T(string text, double size, IBrush fg, FontWeight w)
        {
            return new TextBlock { Text = StripMd(text), FontSize = size, Foreground = fg, FontWeight = w };
        }

        private static TextBlock Mono(string text, double size, IBrush fg)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = fg, FontFamily = MonoFont };
        }

        private static SymbolIcon Ic(Symbol s, double size, IBrush fg)
        {
            return new SymbolIcon { Symbol = s, IconVariant = IconVariant.Regular, FontSize = size, Foreground = fg };
        }

        /// <summary>统一卡片：12px 圆角 + 发丝描边 + 浅色风格的极浅投影（深色靠描边分层）。</summary>
        private static Border Card(Control child, Thickness margin, Thickness padding)
        {
            Border b = new Border
            {
                Background = Palette.CardBg,
                CornerRadius = new CornerRadius(12),
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                Margin = margin,
                Padding = padding,
                Child = child
            };
            if (Palette.CardShadow.Length > 0) b.BoxShadow = BoxShadows.Parse(Palette.CardShadow);
            return b;
        }

        private static Border Chip(string text, IBrush fg, IBrush bg)
        {
            return new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = T(text, 10.5, fg, FontWeight.SemiBold)
            };
        }

        /// <summary>圆角小方块里的图标（KPI / 卡片头部用）。</summary>
        private static Border SoftTile(Symbol icon, IBrush fg, IBrush bg, double size, double iconSize)
        {
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(9),
                Background = bg,
                Child = new SymbolIcon
                {
                    Symbol = icon,
                    IconVariant = IconVariant.Regular,
                    FontSize = iconSize,
                    Foreground = fg,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        /// <summary>比例条：自适应容器宽度（星号列），两端圆角。pct &lt; 0 视为未知 → 空条。</summary>
        private static Control Meter(double pct, IBrush brush, double h)
        {
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            int p = (int)Math.Round(pct);
            Grid g = new Grid { Height = h, ColumnDefinitions = new ColumnDefinitions(p.ToString() + "*," + (100 - p) + "*") };
            Border track = new Border { Background = Palette.BarTrack, CornerRadius = new CornerRadius(h / 2) };
            Grid.SetColumnSpan(track, 2);
            g.Children.Add(track);
            if (p > 0)
            {
                Border fill = new Border { Background = brush, CornerRadius = new CornerRadius(h / 2) };
                Grid.SetColumn(fill, 0);
                g.Children.Add(fill);
            }
            return g;
        }

        private static void Hover(Border b, IBrush normal, IBrush hover)
        {
            b.PointerEntered += delegate { b.Background = hover; };
            b.PointerExited += delegate { b.Background = normal; };
        }

        private static void Hover(Button b, IBrush normal, IBrush hover)
        {
            b.PointerEntered += delegate { b.Background = hover; };
            b.PointerExited += delegate { b.Background = normal; };
        }

        private static Button GhostButton(Control content, Action act, bool bordered)
        {
            Button b = new Button
            {
                Content = content,
                Background = bordered ? Palette.CardBg : Brushes.Transparent,
                BorderBrush = bordered ? Palette.Border : Brushes.Transparent,
                BorderThickness = new Thickness(bordered ? 1 : 0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 7)
            };
            IBrush normal = bordered ? Palette.CardBg : Brushes.Transparent;
            Hover(b, normal, Palette.CardHover);
            b.Click += delegate { act(); };
            return b;
        }

        private static Button PrimaryButton(string text, Action act)
        {
            Button b = new Button
            {
                Content = T(text, 12.5, Palette.OnAccent, FontWeight.SemiBold),
                Background = Palette.Accent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 7)
            };
            Hover(b, Palette.Accent, Palette.AccentHover);
            b.Click += delegate { act(); };
            return b;
        }

        /// <summary>segmented 切换器：凹陷底 + 选中项浮起为卡片色。</summary>
        private static Control Segmented(string[] items, int active, Action<int> pick)
        {
            StackPanel inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            for (int i = 0; i < items.Length; i++)
            {
                int idx = i;
                bool act = idx == active;
                Button b = new Button
                {
                    Content = T(items[i], 12, act ? Palette.Text : Palette.TextDim, act ? FontWeight.SemiBold : FontWeight.Normal),
                    Background = act ? Palette.CardBg : Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(12, 5)
                };
                b.Click += delegate { pick(idx); };
                inner.Children.Add(b);
            }
            // ★★ **用户反馈（2026-09-30）**：「子菜单在不同布局可能不显示」✓✓
            //   ✗ 原来直接返回 Border + HorizontalAlignment=Left ✗ → **一行排不下就被裁掉** ✗
            //     （会话页的「整体/父会话/子代理/统计」✓ 窄窗口 / 高 DPI 放大 / 标题变长时右边会缺 ✓）
            //   ✓ 现在：**外面包一层横向 ScrollViewer** ✓✓
            //     · 排得下 → 外观**完全不变** ✓（Border 与 Left 对齐都保留 ✓）
            //     · 排不下 → **出现横向滚动条** ✓ 所有子项**都点得到** ✓✓
            Border box = new Border
            {
                Background = Palette.InsetBg,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = inner
            };
            return new ScrollViewer
            {
                Content = box,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        /// <summary>写操作的两次确认按钮（隔离/恢复）：第一次点击只是变成确认文案，第二次才真执行。</summary>
        private static Button ConfirmButton(string key, string idleText, string confirmText, MainWindow host, Action confirmed)
        {
            bool pending = host.PendingPatch == key;
            Button b = new Button
            {
                Content = T(pending ? confirmText : idleText, 11.5, pending ? Palette.Bad : Palette.TextDim, pending ? FontWeight.SemiBold : FontWeight.Normal),
                Background = pending ? Palette.BadSoft : Brushes.Transparent,
                BorderBrush = pending ? Palette.Bad : Palette.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 3)
            };
            b.Click += delegate
            {
                if (host.PendingPatch == key)
                {
                    host.PendingPatch = "";
                    confirmed();
                }
                else
                {
                    host.PendingPatch = key;
                    host.Rebuild();
                }
            };
            return b;
        }

        // ================================================================ 外壳

        // ★★★ **用户反馈（2026-09-30）**：「多个布局要么没主菜单要么没子菜单」✓✓
        //   逐条核对 5 个壳（读源码确认 ✓）：
        //     ① 侧栏式    → 有主菜单 ✓ **无子菜单** ✗
        //     ② 顶部标签式 → **无主菜单** ✗✗ 只有子菜单 ✓
        //     ③ 卡片网格   → **无主菜单** ✗✗ 只有子菜单 ✓
        //     ④ 主从式    → 会话页时左栏变成会话列表 → **主菜单消失** ✗✗
        //     ⑤ 混合式    → 主菜单 ✓ 子菜单 ✓ **唯一完整的** ✓✓
        //   → 根因：`MainMenu` 只在 ①⑤ 被调用 ✓ 而 `Segmented(SubTabs)` 只在 ②③⑤ ✓
        //   ✓ 现在：**每个壳都必须同时给出主菜单与子菜单** ✓✓
        //     · 新增 `MainNavStrip`（**横向**主菜单 ✓ 供 ②③ 使用 ✓）
        //     · ① 补子菜单 ✓ ④ 左栏永远保留主菜单 ✓

        /// <summary>主菜单的**横向**版本 ✓✓（②顶部标签式 / ③卡片网格 原来没有主导航 ✗）。
        /// 排不下时**横向滚动** ✓（与 `Segmented` 同一策略 ✓ 用户报过"不同布局可能不显示" ✓）。</summary>
        private static Control MainNavStrip(MainWindow host)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            for (int i = 0; i < MainWindow.NavItems.Length; i++) sp.Children.Add(NavItem(host, i));
            return new Border
            {
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 10, 20, 10),
                Child = new ScrollViewer
                {
                    Content = sp,
                    HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                }
            };
        }

        // ---------------- ① 侧栏式 ----------------

        private static Control BuildSidebar(MainWindow host)
        {
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("232,*") };
            Border side = new Border
            {
                Background = Palette.SidebarBg,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new ScrollViewer { Content = MainMenu(host) }
            };
            Grid.SetColumn(side, 0);

            // ✓ 用户反馈：① 原来**只有主菜单没有子菜单** ✗ → 补上子菜单 ✓✓
            Grid body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
            Border subs = new Border
            {
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 10, 24, 10),
                Child = Segmented(host.SubTabs, host.SubTab, delegate(int i) { host.SetSubTab(i); })
            };
            Grid.SetRow(subs, 0);
            Control title = PageHeader(host);
            Grid.SetRow(title, 1);
            Control content = SectionBody(host);
            Grid.SetRow(content, 2);
            Control foot = Footer(host);
            Grid.SetRow(foot, 3);
            body.Children.Add(subs); body.Children.Add(title); body.Children.Add(content); body.Children.Add(foot);
            Grid.SetColumn(body, 1);
            g.Children.Add(side); g.Children.Add(body);
            return g;
        }

        // ---------------- ② 顶部标签式 ----------------

        private static Control BuildTopTabs(MainWindow host)
        {
            // ✓ 用户反馈：② 原来**只有子菜单没有主菜单** ✗ → 补上横向主菜单 ✓✓
            Grid g = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
            Control nav = MainNavStrip(host);
            Grid.SetRow(nav, 0);
            Border tabs = new Border { Margin = new Thickness(24, 14, 24, 0), Child = Segmented(host.SubTabs, host.SubTab, delegate(int i) { host.SetSubTab(i); }) };
            Grid.SetRow(tabs, 1);
            Control title = PageHeader(host);
            Grid.SetRow(title, 2);
            Control content = SectionBody(host);
            Grid.SetRow(content, 3);
            g.Children.Add(nav); g.Children.Add(tabs); g.Children.Add(title); g.Children.Add(content);
            return g;
        }

        // ---------------- ③ 卡片网格仪表盘 ----------------

        private static Control BuildCardGrid(MainWindow host)
        {
            // ✓ 用户反馈：③ 原来**只有子菜单没有主菜单** ✗ → 补上横向主菜单 ✓✓
            Grid g = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
            Control nav = MainNavStrip(host);
            Grid.SetRow(nav, 0);
            Border tabs = new Border { Margin = new Thickness(24, 14, 24, 0), Child = Segmented(host.SubTabs, host.SubTab, delegate(int i) { host.SetSubTab(i); }) };
            Grid.SetRow(tabs, 1);

            StackPanel body = new StackPanel { Margin = PageMargin, Spacing = 14 };
            body.Children.Add(PageHeader(host, false));
            if (host.IsSessionsSection)
            {
                body.Children.Add(KpiStrip(host));
                body.Children.Add(T("会话明细", 14, Palette.Text, FontWeight.SemiBold));
                body.Children.Add(CardGridBody(host));
            }
            else
            {
                body.Children.Add(SectionInner(host));
            }
            body.Children.Add(Explain());
            Control scroll = new ScrollViewer { Content = body };
            Grid.SetRow(scroll, 2);
            g.Children.Add(nav); g.Children.Add(tabs); g.Children.Add(scroll);
            return g;
        }

        // ---------------- ④ 主从式 ----------------

        // N15 NOTE: unreachable on purpose. `MasterDetail` maps to `BuildHybrid` (line 55) because the
        // user asked for the master-detail layout to be removed, and no button selects it any more.
        // Kept as a reference for whoever wants that layout back; it is not dead by accident.
        private static Control BuildMasterDetail(MainWindow host)
        {
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*") };

            StackPanel left = new StackPanel { Margin = new Thickness(14), Spacing = 10 };
            // ✓ 用户反馈：④ **会话页时左栏变成会话列表 → 主菜单消失** ✗✗
            //   → 现在：**主菜单永远在最上面** ✓ 会话列表放在它下面 ✓✓（不再互相顶掉 ✓）
            for (int i = 0; i < MainWindow.NavItems.Length; i++) left.Children.Add(NavItem(host, i));
            left.Children.Add(T(host.IsSessionsSection ? "会话列表" : "页面", 13, Palette.Text, FontWeight.SemiBold));
            if (host.IsSessionsSection)
            {
                left.Children.Add(Segmented(new string[] { "全部", "非空", "dsh 内" }, host.Filter, delegate(int i) { host.SetFilter(i); }));
                left.Children.Add(MasterList(host));
            }
            Border leftCard = new Border
            {
                Background = Palette.SidebarBg,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new ScrollViewer { Content = left }
            };
            Grid.SetColumn(leftCard, 0);

            StackPanel right = new StackPanel { Margin = PageMargin, Spacing = 12 };
            right.Children.Add(PageHeader(host, false));
            right.Children.Add(host.IsSessionsSection ? DetailCard(host) : SectionInner(host));
            if (host.IsSessionsSection) right.Children.Add(Explain());
            Control rightScroll = new ScrollViewer { Content = right };
            Grid.SetColumn(rightScroll, 1);
            g.Children.Add(leftCard); g.Children.Add(rightScroll);
            // DetailHost 此时才就位：补一次首行选中，让右侧详情不用等用户点
            if (host.IsSessionsSection && host.Rows.Count > 0) host.ShowDetail(host.Rows[0]);
            return g;
        }

        // ---------------- ⑤ 混合式：侧栏主菜单 + 顶部子菜单 ----------------

        private static Control BuildHybrid(MainWindow host)
        {
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("232,*") };
            Border side = new Border
            {
                Background = Palette.SidebarBg,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new ScrollViewer { Content = MainMenu(host) }
            };
            Grid.SetColumn(side, 0);

            Grid right = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
            Border subs = new Border
            {
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 10, 24, 10),
                Child = Segmented(host.SubTabs, host.SubTab, delegate(int i) { host.SetSubTab(i); })
            };
            Grid.SetRow(subs, 0);
            Control head = PageHeader(host);
            Grid.SetRow(head, 1);
            Control body = SectionBody(host);
            Grid.SetRow(body, 2);
            Control foot = Footer(host);
            Grid.SetRow(foot, 3);
            right.Children.Add(subs); right.Children.Add(head); right.Children.Add(body); right.Children.Add(foot);
            Grid.SetColumn(right, 1);

            g.Children.Add(side); g.Children.Add(right);
            return g;
        }

        // ================================================================ 共享片段

        private static Control NavItem(MainWindow host, int idx)
        {
            bool active = host.MainSection == idx;
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
            Border bar = new Border
            {
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                Background = active ? Palette.Accent : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(bar, 0);
            SymbolIcon ic = Ic(MainWindow.NavIcons[idx], 15, active ? Palette.Accent : Palette.TextDim);
            ic.Margin = new Thickness(9, 0, 0, 0);
            ic.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ic, 1);
            TextBlock tb = T(MainWindow.NavItems[idx], 13, active ? Palette.Accent : Palette.Text);
            tb.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
            tb.Margin = new Thickness(10, 0, 0, 0);
            tb.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tb, 2);
            row.Children.Add(bar); row.Children.Add(ic); row.Children.Add(tb);

            Button b = new Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = active ? Palette.AccentSoft : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 9)
            };
            if (!active) Hover(b, Brushes.Transparent, Palette.CardHover);
            b.Click += delegate { host.SetMainSection(idx); };
            return b;
        }

        private static Control MainMenu(MainWindow host)
        {
            Grid g = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto") };
            Control inner = MainMenuInner(host);
            Grid.SetRow(inner, 0);
            g.Children.Add(inner);
            Control mode = StartModeRow(host);          // 启动方式：webui / desktop ✓（用户要求 ✓）
            Grid.SetRow(mode, 1);
            g.Children.Add(mode);
            Control start = StartStopButton(host);
            Grid.SetRow(start, 2);
            g.Children.Add(start);
            return g;
        }

        /// <summary>侧栏最底下的一键启动/停止（用户要求放这里，不放看板）。未运行=实心 accent 主按钮；运行中=柔色底+绿点，状态就在按钮里。</summary>
        /// <summary>一键启动按钮**上面**的启动方式切换（webui / desktop ✓）。
        /// 用户要求："一键启动按钮底下可选默认启动 desktop 还是 webui" ✓✓</summary>
        private static Control StartModeRow(MainWindow host)
        {
            StackPanel s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 0, 12, 6) };
            int cur = host.StartMode;
            string[] names = new string[] { "webui", "desktop" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                bool on = cur == i;
                Button b = new Button
                {
                    Content = T(names[i] == "webui" ? "web" : names[i], 11, on ? Palette.OnAccent : Palette.TextDim),
                    Background = on ? Palette.Accent : Palette.CardHover,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 4),
                    // ★★★ **N7 修复（复审 MAJOR —— 注释说了但代码没做）** ✓✓
                    //   ✗ 这里原来只有注释、**没有 MinWidth** ✗ → 每个按钮仍是**文字宽度** ✗
                    //     → `web`(3) / `desktop`(7) 各不相同 ✓ 用户报过两次的"宽度不一样"仍在 ✗✗
                    //     （我上一轮把 `ModeBtnW` 加到了**条件才出现的**"停 web/停桌面端"上 ✗
                    //      而那一条是**默认可见**的 ✓ —— 加错了行 ✓）
                    //   ✓ 现在：**真正的这一行**也统一最小宽度 ✓ 并居中 ✓✓
                    MinWidth = ModeBtnW,
                    HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
                };
                b.Click += delegate { host.SetStartMode(idx); };
                s.Children.Add(b);
            }
            Button dep = new Button
            {
                Content = T(cur == 1 ? "下载" : "安装", 11, Palette.Text),
                Background = Palette.CardHover,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4),
                MinWidth = ModeBtnW,   // N7 FIX: same row, same minimum width
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            dep.Click += delegate { host.DeployForMode(); };
            if (host.ActionBusy) { dep.IsEnabled = false; dep.Opacity = 0.55; }   // ★ 有动作在跑 → 别再引诱人点（2026-10-04 ✓）
            s.Children.Add(dep);
            // **两个都开着时给两个停止按钮** ✓✓（用户要求："如果两个都开着，工具可以选择停止一个" ✓）
            StatusSnapshot st2 = host.Status;
            bool deskOn = st2 != null && st2.Ok && !string.IsNullOrEmpty(st2.DesktopClient);
            bool webOn = st2 != null && st2.Ok && st2.State == 0;
            if (deskOn && webOn)
            {
                StackPanel both = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 0, 12, 6) };
                both.Children.Add(T("两个都在跑 →", 10.5, Palette.Warn));
                Button sw = ActionGhost(host, "停 web", delegate { host.StopWebOnly(); });
        // F9 FIX (GUI audit MAJOR): ModeBtnW was declared and never used, so these buttons sized
        // themselves to their labels and the row had visibly unequal widths - the defect the user
        // reported twice. A shared minimum width and centred content fixes it.
        sw.MinWidth = ModeBtnW; sw.HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
                both.Children.Add(sw);
                Button sd = ActionGhost(host, "停桌面端", delegate { host.StopDesktopOnly(); });
        sd.MinWidth = ModeBtnW; sd.HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;   // F9 FIX
                both.Children.Add(sd);
                s.Children.Add(both);
            }
            return s;
        }
        private static Control StartStopButton(MainWindow host)
        {
            StatusSnapshot st = host.Status;
            // ✗ 原来只看 web 服务（State==0）→ 桌面端在跑时左下角仍显示"未运行" ✓（用户反馈 ✓）
            // 现在把**桌面端也算"dsh 在跑"** ✓（它确实在跑 ✓ 只是不走 3080 ✓）
            bool deskUp = st != null && st.Ok && st.State == 2 && !string.IsNullOrEmpty(st.DesktopClient);
            bool up = st != null && st.Ok && (st.State == 0 || deskUp);
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Border dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = up ? Palette.Good : Palette.OnAccent,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dot, 0);
            // 桌面端在跑 → 说清"桌面端运行中"✓（本工具**停不了它** ✓ 不能假装能 ✓）
            // 三种状态都要说清 ✓：只有 web / 只有桌面端 / **两个都在** ✓✓（用户："两个可能同时开着" ✓）
            // ★★★ **F1 修复（GUI 审计 CRITICAL —— 空引用）** ✓✓
            //   ✗ `StatusSnapshot` 是**类** ✓ 而 `MainWindow._status` **只在概览分支里被赋值** ✗
            //     → CLI 找不到时 `_status` 仍是 null ✓ → `st.State` **抛 NRE** ✗✗
            //     → 异常被 fire-and-forget 的 Refresh 吞掉 ✓ → **窗口主体永远是空的** ✗
            //       而且友好提示「未找到工具箱 CLI」**永远显示不出来** ✓✓
            //   ✓ 现在：**先算一个 webUp 布尔** ✓ 再也不用 `st` 空着 ✓✓
            bool webUp = st != null && st.State == 0;
            string stText = webUp && deskUp ? "web 与桌面端都在运行" : (deskUp ? "桌面端运行中" : (up ? "停止 dsh" : "一键启动 dsh"));
            TextBlock label = T(stText, 13, up ? Palette.Accent : Palette.OnAccent, FontWeight.SemiBold);
            label.Margin = new Thickness(10, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            TextBlock state = T(webUp && deskUp ? "web+桌面端" : (up ? "运行中" : "未运行"), 10.5, up ? Palette.Good : Palette.OnAccent);
            state.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(state, 2);
            row.Children.Add(dot); row.Children.Add(label); row.Children.Add(state);

            Button b = new Button
            {
                Content = row,
                Margin = new Thickness(12, 8, 12, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = up ? Palette.AccentSoft : Palette.Accent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10)
            };
            Hover(b, up ? Palette.AccentSoft : Palette.Accent, up ? Palette.CardHover : Palette.AccentHover);
            b.Click += delegate { if (up) host.StopDsh(); else host.StartDsh(); };   // 桌面端在跑时 StopDsh 会如实说"没在监听 3080"✓ 不谎报 ✓
            if (host.ActionBusy) { b.IsEnabled = false; b.Opacity = 0.55; }   // ★ 同上（2026-10-04 ✓）
            return b;
        }

        private static Control MainMenuInner(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = new Thickness(12, 16, 12, 12), Spacing = 2 };
            s.Children.Add(new TextBlock { Text = "导航", Foreground = Palette.TextFaint, FontSize = 11, Margin = new Thickness(12, 0, 0, 8) });
            for (int i = 0; i < MainWindow.NavItems.Length; i++) s.Children.Add(NavItem(host, i));
            return s;
        }

        private static Control PageHeader(MainWindow host) { return PageHeader(host, true); }

        private static Control PageHeader(MainWindow host, bool withMargin)
        {
            StackPanel s = new StackPanel { Spacing = 4 };
            if (withMargin) s.Margin = new Thickness(24, 18, 24, 12);
            s.Children.Add(T(host.PageTitle, 20, Palette.Text, FontWeight.SemiBold));
            s.Children.Add(new TextBlock { Text = StripMd(host.SubtitleText), Foreground = Palette.TextDim, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            return s;
        }

        private static Control Footer(MainWindow host)
        {
            return new Border
            {
                Background = Palette.SidebarBg,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24, 7),
                Child = new TextBlock { Text = host.SourceText, Foreground = Palette.TextFaint, FontSize = 11, TextWrapping = TextWrapping.Wrap }
            };
        }

        /// <summary>原始标记行：头部（图标+标题+说明）+ 凹陷等宽文本区（限高内滚）。</summary>
        private static Control RawCard(MainWindow host, string title, string caption)
        {
            StackPanel s = new StackPanel { Spacing = 10 };
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            SymbolIcon ic = Ic(Symbol.Code, 15, Palette.TextDim);
            ic.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ic, 0);
            TextBlock t = T(title, 13, Palette.Text, FontWeight.SemiBold);
            t.Margin = new Thickness(8, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            TextBlock cap = T(caption, 11, Palette.TextFaint);
            cap.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cap, 2);
            head.Children.Add(ic); head.Children.Add(t); head.Children.Add(cap);
            s.Children.Add(head);

            TextBox box = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                Text = host.RawOutput,
                FontFamily = MonoFont,
                FontSize = 11.5,
                Foreground = Palette.TextDim,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            s.Children.Add(new Border
            {
                Background = Palette.InsetBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10),
                MaxHeight = 460,
                Child = new ScrollViewer { Content = box }
            });
            return Card(s, new Thickness(0), new Thickness(16, 14));
        }

        private static Control TextPane(MainWindow host)
        {
            {
                // 说明页 = 关于信息（logo 已从标题栏移到这里 ✓）+ CLI 原始输出
                global::Avalonia.Controls.StackPanel wrap = new global::Avalonia.Controls.StackPanel();
                wrap.Spacing = 4;
                wrap.Children.Add(AboutCard());
                wrap.Children.Add(new Border { Margin = PageMargin, Child = RawCard(host, "原始输出", "CLI 标记行原文") });
                return wrap;
            }
        }

        /// <summary>按当前主菜单项选内容（带外层滚动）：会话页=面板，形态页=profile 卡片，状态页=图形化概览，其余=标记行原文。</summary>
        private static Control SectionBody(MainWindow host)
        {
            return new ScrollViewer { Content = SectionInner(host) };
        }

        /// <summary>同 <see cref="SectionBody"/> 但不自带滚动（供已经有滚动容器的外壳用）。</summary>
        /// <summary>关于卡片：整图 logo + 品牌 + 非官方声明（logo 从标题栏移到关于页 ✓）。
        /// 全部用全限定名 —— 这个文件里 Avalonia.* 前缀会被解析成 Dsht.Gui.Avalonia.* ✗（踩过）。</summary>
        private static global::Avalonia.Controls.Control AboutCard()
        {
            global::Avalonia.Controls.StackPanel sp = new global::Avalonia.Controls.StackPanel();
            sp.Spacing = 8;
            try
            {
                using (System.IO.Stream s = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://dsht-gui/Assets/logo-full.png")))
                {
                    global::Avalonia.Controls.Image img = new global::Avalonia.Controls.Image();
                    img.Source = new global::Avalonia.Media.Imaging.Bitmap(s);
                    img.Width = 200; img.Height = 200;
                    img.Stretch = global::Avalonia.Media.Stretch.Uniform;
                    img.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
                    sp.Children.Add(img);
                }
            }
            catch { }
            global::Avalonia.Controls.TextBlock name = new global::Avalonia.Controls.TextBlock();
            name.Text = "dsh-minato";
            name.FontSize = 20;
            name.FontWeight = global::Avalonia.Media.FontWeight.SemiBold;
            name.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
            sp.Children.Add(name);
            global::Avalonia.Controls.TextBlock tag = new global::Avalonia.Controls.TextBlock();
            tag.Text = "社区版 DeepSeek Harness (dsh) 本机部署运维套件：安装 / 启动 / 监控 / 备份恢复 / 插件诊断与隔离";
            tag.TextWrapping = global::Avalonia.Media.TextWrapping.Wrap;
            tag.Opacity = 0.85;
            sp.Children.Add(tag);
            global::Avalonia.Controls.TextBlock un = new global::Avalonia.Controls.TextBlock();
            un.Text = "非官方工具，与 DeepSeek 官方无关。图标为社区自制（AI 生成），不适用本项目的 MIT 许可。";
            un.TextWrapping = global::Avalonia.Media.TextWrapping.Wrap;
            un.Opacity = 0.7;
            sp.Children.Add(un);
            global::Avalonia.Controls.Border card = new global::Avalonia.Controls.Border();
            card.Padding = new global::Avalonia.Thickness(16);
            card.CornerRadius = new global::Avalonia.CornerRadius(10);
            card.Margin = PageMargin;
            card.Child = sp;
            return card;
        }
        private static Control SectionInner(MainWindow host)
        {
            if (host.IsSessionsSection) return SessionsContent(host);
            if (host.MainSection == 3) return ProfilesContent(host);
            if (host.MainSection == 0) return OverviewContent(host);
            if (host.MainSection == 1) return BoardContent(host);
            if (host.MainSection == 4) return BackupContent(host);
            if (host.MainSection == 5) return HealthContent(host);
            if (host.MainSection == 6) return SettingsContent(host);
            if (host.MainSection == 8) return UpdateCenterContent(host);   // 更新中心 ✓（**追加在最后** ✓ 不动前面 8 处硬编码索引 ✓）
            if (host.MainSection == 9) return LogCenterContent(host);   // 日志中心 ✓（同样**追加在最后** ✓）
            {
                // 说明页 = 关于信息（logo 已从标题栏移到这里 ✓）+ CLI 原始输出
                global::Avalonia.Controls.StackPanel wrap = new global::Avalonia.Controls.StackPanel();
                wrap.Spacing = 4;
                wrap.Children.Add(AboutCard());
                wrap.Children.Add(new Border { Margin = PageMargin, Child = RawCard(host, "原始输出", "CLI 标记行原文") });
                return wrap;
            }
        }

        // ================================================================ 会话与 Token

        private static Control SessionsContent(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = 14 };
            if (host.Data == null || !host.Data.Ok)
            {
                string msg = host.Data == null
                    ? (string.IsNullOrEmpty(host.RawOutput) ? "读不到会话数据。" : host.RawOutput)
                    : (string.IsNullOrEmpty(host.Data.FailReason) ? "没有可显示的会话。" : host.Data.FailReason);
                StackPanel err = new StackPanel { Spacing = 8 };
                Grid eh = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                SymbolIcon wic = Ic(Symbol.Warning, 16, Palette.Warn);
                wic.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(wic, 0);
                TextBlock et = T("会话数据不可用", 13.5, Palette.Text, FontWeight.SemiBold);
                et.Margin = new Thickness(8, 0, 0, 0);
                et.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(et, 1);
                eh.Children.Add(wic); eh.Children.Add(et);
                err.Children.Add(eh);
                err.Children.Add(new TextBlock { Text = msg, Foreground = Palette.TextDim, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                s.Children.Add(Card(err, new Thickness(0), new Thickness(18, 16)));
                return s;
            }
            // GUI final review: KpiStrip reads host.ListSource, which the list builder sets - so the builder has
            // to run FIRST even though the cards are displayed above the list. Building them in display order made
            // the strip read the previous page's subset (or none), so its tag and figures disagreed with the rows.
            Control listCtl;
            if (host.SubTab == 3) listCtl = StatsBody(host);
            else if (host.SubTab == 0) listCtl = SessionListGrouped(host);   // 整体：子代理**折叠进父会话**（下拉框 ✓✓ 用户要求 ✓）
            else listCtl = SessionList(host, host.SubTab);
            s.Children.Add(KpiStrip(host));
            s.Children.Add(Toolbar(host));
            s.Children.Add(new TextBlock { Text = host.FocusText, Foreground = Palette.TextDim, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            // 三视图 ✓：0=整体（全部，子代理随后归类进父会话）1=父会话 2=子代理 3=统计 ✓
            s.Children.Add(listCtl);
            s.Children.Add(Explain());
            // 操作日志改为**右下角 toast** ✓✓（不再在页面流里占一张卡片 ✓）
            return s;
        }

/// <summary>KPI 口径的**一次计算** ✓（字段级刷新里每拍重算 ✓ 见 KpiAgg ✓）。
        /// · **整体**视图 → 仍用 CLI 给的精确合计 ✓（口径与 CLI 完全一致 ✓ 不自己算 ✗）
        /// · **父会话 / 子代理** → 按**当前显示的那些行**重算 ✓（求和是确定的 ✓ 不是猜 ✓）</summary>
        private sealed class KpiAgg
        {
            public bool Filtered; public int Count, NonBlank, Live, Subs;
            public long In, Out, Cache;
            public double HitPct = -1, Tps = -1;
        }

        private static KpiAgg Agg(MainWindow host)
        {
            KpiAgg a = new KpiAgg();
            SessionsSnapshot d = host.Data;
            List<SessionRowVm> src = host.ListSource;
            // ★★★ **真机 GUI 冒烟抓到的 bug（截图可见）** ✓✓
            //   ✗ 原来判据是 `src != null && src.Count != d.Rows.Count` ✗
            //     → 列表源是**空列表**（页面刚切过来 / 该视图下没有行）时 `0 != 2` → **filtered = true** ✗
            //       → **对空列表求和 → 四张卡全 0** ✗✗（CLI 说 SESSIONS_OK 2、图表也画了 2 个 ✓）
            //   ✓ 现在：**空源一律按"没有过滤"处理** ✓✓ → 回落到 CLI 的精确合计 ✓
            //     · 只有**非空、而且行数确实与全量不同**时，才认为用户在看子集 ✓
            // 审查 H4 修复：只在会话页才信 ListSource ✗ 其它页它是上次留下的旧快照 → 看板会显示子集之和+错误标签 ✗✗
            a.Filtered = host.IsSessionsSection && src != null && src.Count > 0 && d != null && src.Count != d.Rows.Count;
            if (d == null) return a;
            if (!a.Filtered)
            {
                a.Count = d.Count; a.NonBlank = d.NonBlank; a.Live = d.Live; a.Subs = d.SubAgentCount;
                a.In = d.TotalIn; a.Out = d.TotalOut; a.Cache = d.TotalCacheRead;
                a.HitPct = d.TotalHitPercent; a.Tps = d.TotalDecodeTps;
                return a;
            }
            double hitNum = 0, hitDen = 0, tpsNum = 0, tpsDen = 0;
            for (int fi = 0; fi < src.Count; fi++)
            {
                SessionRow r = src[fi].Row;
                if (r == null) continue;
                a.Count++;
                if (!r.Blank) a.NonBlank++;
                if (r.Live) a.Live++;
                if (r.IsSubAgent) a.Subs++;
                a.In += r.In; a.Out += r.Out; a.Cache += r.CacheRead;
                // 加权：命中率按**输入量**加权 ✓ 解码速度按**输出量**加权 ✓（与 CLI 合计口径同源 ✓）
                if (r.HitPercent >= 0 && r.In > 0) { hitNum += r.HitPercent * r.In; hitDen += r.In; }
                if (r.DecodeTps >= 0 && r.Out > 0) { tpsNum += r.DecodeTps * r.Out; tpsDen += r.Out; }
            }
            a.HitPct = hitDen > 0 ? hitNum / hitDen : -1;
            a.Tps = tpsDen > 0 ? tpsNum / tpsDen : -1;
            return a;
        }

        /// <summary>KPI 卡的视图后缀 ✓（整体视图 = 空串 ✓）。</summary>
        private static string KpiTag(MainWindow host)
        {
            return Agg(host).Filtered ? (host.SubTab == 2 ? "（子代理）" : "（父会话）") : "";
        }

        private static Control KpiStrip(MainWindow host)
        {
            // ★★ 字段级刷新（2026-10-04 用户要求："自动刷新…可以只字段刷新吗" ✓✓）：
            //   四张卡改成 KpiCardLive ✓ 每拍用 `Agg(host)` **重算** ✓（行数百来条 ✓ 纯计算 ✓ 便宜 ✓）
            //   ✗ 不重建视觉树 ✓ → 不丢滚动/焦点 ✓ 也不触发整页淡入 ✓✓
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            g.Children.Add(KpiCardLive(host, Symbol.ChatMultiple,
                delegate { return "会话总数" + KpiTag(host); },
                delegate { SessionsSnapshot d = host.Data; return d == null ? "—" : Agg(host).Count.ToString(); },
                delegate
                {
                    SessionsSnapshot d = host.Data; KpiAgg a = Agg(host);
                    return "非空 " + (d == null ? "—" : a.NonBlank.ToString()) + " · dsh 活跃 " + (d == null ? "—" : a.Live.ToString())
                        + "（在动看每行状态 ✓「挂着」= 还在 dsh 里但没动 ✓） · 子代理 " + (d == null ? "—" : a.Subs.ToString())
                        + " / 根 " + (d == null ? "—" : (a.Count - a.Subs).ToString());
                }, Palette.Text, 0, null));
            g.Children.Add(KpiCardLive(host, Symbol.Database,
                delegate { return "缓存命中率" + KpiTag(host); },
                delegate { return PctText(Agg(host).HitPct); },
                delegate { SessionsSnapshot d = host.Data; return "缓存读 " + (d == null ? "—" : SessionRow.Human(Agg(host).Cache)); },
                Palette.Good, 1, delegate { return Agg(host).HitPct; }));
            g.Children.Add(KpiCardLive(host, Symbol.Gauge,
                delegate { return "解码速度" + KpiTag(host); },
                delegate { return TpsText(Agg(host).Tps); },
                delegate { return "tok/s"; }, Palette.Accent, 2, null));
            g.Children.Add(KpiCardLive(host, Symbol.DataUsage,
                delegate { return "累计 token" + KpiTag(host); },
                delegate { SessionsSnapshot d = host.Data; return d == null ? "—" : SessionRow.Human(Agg(host).In); },
                delegate { SessionsSnapshot d = host.Data; return "输出 " + (d == null ? "—" : SessionRow.Human(Agg(host).Out)); },
                Palette.Text, 3, null));
            return g;
        }

        private static Control KpiCard(Symbol icon, string title, string value, string sub, IBrush valueBrush, int col, double barPercent)
        {
            StackPanel s = new StackPanel();
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Border tile = SoftTile(icon, Palette.Accent, Palette.AccentSoft, 30, 15);
            Grid.SetColumn(tile, 0);
            TextBlock label = new TextBlock { Text = title, Foreground = Palette.TextDim, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(label, 1);
            head.Children.Add(tile); head.Children.Add(label);
            s.Children.Add(head);
            s.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeight.SemiBold, Foreground = valueBrush, Margin = new Thickness(0, 8, 0, 0) });
            s.Children.Add(new TextBlock { Text = sub, Foreground = Palette.TextFaint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (barPercent >= 0)
            {
                int level = barPercent >= 90 ? 3 : (barPercent >= 70 ? 2 : 1);
                Border slot = new Border { Margin = new Thickness(0, 8, 0, 0), Child = Meter(barPercent, Palette.HitBrush(level), 4) };
                s.Children.Add(slot);
            }
            Border card = Card(s, new Thickness(0, 0, col == 3 ? 0 : 12, 0), new Thickness(16, 14));
            Grid.SetColumn(card, col);
            return card;
        }

        /// <summary>**字段级更新**版的 KpiCard ✓（同 StatCardLive 的理由 ✓ 2026-10-04 ✓）。</summary>
        private static Control KpiCardLive(MainWindow host, Symbol icon, Func<string> title, Func<string> value, Func<string> sub, IBrush valueBrush, int col, Func<double> barPercent)
        {
            StackPanel s = new StackPanel();
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Border tile = SoftTile(icon, Palette.Accent, Palette.AccentSoft, 30, 15);
            Grid.SetColumn(tile, 0);
            TextBlock label = host.LiveText(title(), title);
            label.Foreground = Palette.TextDim; label.FontSize = 11.5; label.TextWrapping = TextWrapping.Wrap;
            label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(label, 1);
            head.Children.Add(tile); head.Children.Add(label);
            s.Children.Add(head);
            TextBlock v = host.LiveText(value(), value);
            v.FontSize = 24; v.FontWeight = FontWeight.SemiBold; v.Foreground = valueBrush; v.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(v);
            TextBlock sb = host.LiveText(sub(), sub);
            sb.Foreground = Palette.TextFaint; sb.FontSize = 11; sb.TextWrapping = TextWrapping.Wrap; sb.Margin = new Thickness(0, 2, 0, 0);
            s.Children.Add(sb);
            if (barPercent != null && barPercent() >= 0)
            {
                Border slot = new Border { Margin = new Thickness(0, 8, 0, 0) };
                s.Children.Add(slot);
                host.Live(delegate
                {
                    double p = barPercent();
                    if (p < 0) { slot.Child = null; return; }
                    int level = p >= 90 ? 3 : (p >= 70 ? 2 : 1);
                    slot.Child = Meter(p, Palette.HitBrush(level), 4);
                });
            }
            Border card = Card(s, new Thickness(0, 0, col == 3 ? 0 : 12, 0), new Thickness(16, 14));
            Grid.SetColumn(card, col);
            return card;
        }

        private static Control Toolbar(MainWindow host)
        {
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Control filters = Segmented(new string[] { "全部", "非空", "dsh 内" }, host.Filter, delegate(int i) { host.SetFilter(i); });
            Grid.SetColumn(filters, 0);

            StackPanel sorts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(14, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            TextBlock sl = T("排序", 12, Palette.TextDim);
            sl.VerticalAlignment = VerticalAlignment.Center;
            sorts.Children.Add(sl);
            ComboBox box = new ComboBox { MinWidth = 180, SelectedIndex = host.SortMode, FontSize = 12.5 };
            box.Items.Add("最后活动（新→旧）");
            box.Items.Add("输入 token（多→少）");
            box.Items.Add("缓存命中率（低→高）");
            box.Items.Add("上下文压力（高→低）");
            box.Items.Add("解码速度（快→慢）");
            box.SelectionChanged += delegate { host.SortMode = box.SelectedIndex; host.Rerender(); };
            sorts.Children.Add(box);
            Grid.SetColumn(sorts, 1);

            StackPanel rc = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            SymbolIcon ric = Ic(Symbol.ArrowClockwise, 14, Palette.TextDim);
            ric.VerticalAlignment = VerticalAlignment.Center;
            rc.Children.Add(ric);
            TextBlock rt = T("刷新", 12.5, Palette.Text);
            rt.VerticalAlignment = VerticalAlignment.Center;
            rc.Children.Add(rt);
            Button refresh = GhostButton(rc, delegate { host.Refresh(); }, true);
            Grid.SetColumn(refresh, 2);

            g.Children.Add(filters); g.Children.Add(sorts); g.Children.Add(refresh);
            return g;
        }

        /// <summary>按当前风格选会话列表形态：C=紧凑行（密度优先），D=大条形卡片，其余=标准卡片。</summary>
        /// <summary>会话列表 ✓ mode：0=整体 1=父会话（有子会话的）2=子代理（有父会话的）✓✓</summary>
        /// <summary>**整体视图**：父会话照常显示 ✓，它的子代理**折叠在下拉框里** ✓✓
        /// （用户要求："整体列出子代理时归类到父会话内（做下拉框）" ✓✓）
        /// 98/98 的子 id 都有 SESSION 行 ✓ → 不会出现"找不到父"的孤儿 ✓</summary>
        /// <summary>性能优化（2026-10-06 #1）：给列表一个有界的滚动视口。
        /// 外层页面 ScrollViewer 给内容**无限高** ✗ → VirtualizingStackPanel 从不虚拟化 ✗（SessionListGrouped 注释已承认）。
        /// 包一层限高 ScrollViewer 后，超出部分由列表自己滚动 + **开始虚拟化**：构建/布局量从 O(全部) 降到 O(可见) ✓✓
        /// （items 是预建控件 → 省的是布局/测量，不是实例化 ✓ 如实说明 ✓）。</summary>
        private static Control BoundedList(Control list)
        {
            return new ScrollViewer
            {
                Content = list,
                MaxHeight = 520,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };
        }

        private static Control SessionListGrouped(MainWindow host)
        {
            // ★★ 架构审计（性能）：这里原来是**裸 StackPanel + Children.Add** ✗
            //   → **完全不虚拟化** ✗ → 每个会话卡片都被 eager 创建 ✓（会话一多就卡 ✓ 这正是"按钮延迟"的一部分 ✓）
            // ✓ 现在：**收集成控件列表 → 交给 ItemsControl + VirtualizingStackPanel** ✓✓
            //   · 卡片构建代码**一行没动** ✓ 只换容器 ✓（风险最小的改法 ✓）
            //   ⚠ 诚实边界：虚拟化要生效，**宿主必须给出有界高度** ✓ ——
            //     页面外层的 ScrollViewer 给的是无限高 ✗ → 在那种情况下这层只是"无害的容器" ✓
            //     一旦这个列表被放进有界高度的区域（或将来由它自己滚动 ✓）**立刻开始虚拟化** ✓✓
            System.Collections.Generic.List<Control> items = new System.Collections.Generic.List<Control>();
            List<SessionRowVm> src = host.ListSource;
            // ★★ 架构/性能审查抓到：下面每个子代理都要**线性扫一遍全部行** ✗ → O(父 × 子 × 行) ✗
            //   → 会话一多，默认视图和每次筛选都会卡 ✓（这是用户抱怨"按钮延迟"的一部分 ✓）
            // ✓ 现在：**先建一次 id → 行 的字典** ✓✓ 查找 O(1) ✓
            System.Collections.Generic.Dictionary<string, SessionRowVm> byId =
                new System.Collections.Generic.Dictionary<string, SessionRowVm>(StringComparer.Ordinal);
            for (int bi = 0; bi < src.Count; bi++)
            {
                if (src[bi] != null && src[bi].Row != null && src[bi].Row.Id != null && !byId.ContainsKey(src[bi].Row.Id))
                    byId[src[bi].Row.Id] = src[bi];
            }
            for (int i = 0; i < src.Count; i++)
            {
                SessionRowVm vm = src[i];
                if (vm.Row == null) continue;
                if (vm.Row.IsSubAgent) continue;   // 子代理不在顶层重复显示 ✓（下面折叠 ✓）
                items.Add(SessionCard(vm));
                if (vm.Row.ChildIds.Count > 0)
                {
                    StackPanel kids = new StackPanel { Spacing = 6, Margin = new Thickness(18, 4, 0, 0) };
                    for (int k = 0; k < vm.Row.ChildIds.Count; k++)
                    {
                        SessionRowVm kv = null;
                        byId.TryGetValue(vm.Row.ChildIds[k], out kv);   // ★ O(1) ✓✓（原来每个子都线性扫全部行 ✗）
                        if (kv != null) kids.Children.Add(SessionCard(kv));
                    }
                    // ✗ 原来用 Expander 默认外观 → 自带边框/底色，和卡片放一起**很突兀** ✓（用户指出 ✓）
                    // 现在：**透明背景 + 无边框 + 左侧一条细竖线** ✓ → 视觉上"挂"在父会话下面 ✓✓
                    // （还顺手把默认展开箭头去掉了 ✗ → 用 ▸ 前缀 ✓ 更轻 ✓）
                    Border exHead = new Border
                    {
                        Background = Brushes.Transparent,
                        BorderBrush = Palette.Border,
                        BorderThickness = new Thickness(2, 0, 0, 0),
                        Padding = new Thickness(10, 6, 0, 6),
                        Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
                        Child = T("▸ 子代理 " + vm.Row.ChildIds.Count + " 个（点这里展开）", 11.5, Palette.TextDim)
                    };
                    StackPanel exBox = new StackPanel { Spacing = 6, Margin = new Thickness(16, 4, 0, 0), IsVisible = false };
                    exBox.Children.Add(kids);
                    exHead.PointerPressed += delegate { exBox.IsVisible = !exBox.IsVisible; };
                    StackPanel exWrap = new StackPanel { Spacing = 0 };
                    exWrap.Children.Add(exHead);
                    exWrap.Children.Add(exBox);
                    items.Add(exWrap);
                }
            }
            ItemsControl list = new ItemsControl();
            list.ItemsSource = items;   // 已经构建好的卡片控件 ✓（面板负责虚拟化 ✓）
            list.ItemsPanel = new global::Avalonia.Controls.Templates.FuncTemplate<Panel>(
                delegate { return new VirtualizingStackPanel(); });
            return BoundedList(list);
        }
        private static Control SessionList(MainWindow host, int mode)
        {
            // 过滤 ✓（vm.Row 就是 SessionRow ✓ 解析层已带 IsSubAgent ✓）
            List<SessionRowVm> src = host.Rows;
            if (mode == 1 || mode == 2)
            {
                src = new List<SessionRowVm>();
                for (int fi = 0; fi < host.Rows.Count; fi++)
                {
                    bool sub = host.Rows[fi].Row != null && host.Rows[fi].Row.IsSubAgent;
                    if ((mode == 2) == sub) src.Add(host.Rows[fi]);
                }
            }
            host.SetListSource(src);
            if (Palette.Compact)
            {
                ItemsControl list = new ItemsControl();
                list.ItemsSource = host.ListSource;   // 过滤后的 ✓
                list.ItemTemplate = new FuncDataTemplate<SessionRowVm>(delegate(SessionRowVm vm, INameScope ns) { return SessionRowCompact(vm); });
                Border card = Card(BoundedList(list), new Thickness(0), new Thickness(0));
                card.ClipToBounds = true;
                return card;
            }
            ItemsControl cards = new ItemsControl();
            cards.ItemsSource = host.ListSource;   // 过滤后的 ✓
            cards.ItemTemplate = new FuncDataTemplate<SessionRowVm>(delegate(SessionRowVm vm, INameScope ns)
            {
                return Palette.StyleKind == 3 ? SessionCardDash(vm) : SessionCard(vm);
            });
            return BoundedList(cards);
        }

        /// <summary>标准会话卡片：状态点 + 标题/元信息 + 三个指标块（标签+数值+比例条）。</summary>
        private static Control SessionCard(SessionRowVm vm)
        {
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,170,170,170") };

            Ellipse dot = new Ellipse { Width = 9, Height = 9, Fill = vm.StatusBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            ToolTip.SetTip(dot, vm.StatusText);
            Grid.SetColumn(dot, 0);

            StackPanel mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            mid.Children.Add(new TextBlock { Text = vm.TitleText, Foreground = Palette.Text, FontSize = 13.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            mid.Children.Add(new TextBlock { Text = vm.ShortId + " · " + vm.MetaText, Foreground = Palette.TextDim, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            mid.Children.Add(new TextBlock { Text = vm.DecodeLine, Foreground = Palette.TextFaint, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(mid, 1);

            Control m1 = Metric("输入 token", vm.InText, vm.TokenBar, Palette.Text, vm.TokenBrush);
            Control m2 = Metric("缓存命中", vm.HitText, vm.HitBar, vm.HitBrush, vm.HitBrush);
            Control m3 = Metric("上下文压力", vm.CtxText, vm.CtxBar, vm.CtxBrush, vm.CtxBrush);
            Grid.SetColumn(m1, 2); Grid.SetColumn(m2, 3); Grid.SetColumn(m3, 4);
            g.Children.Add(dot); g.Children.Add(mid); g.Children.Add(m1); g.Children.Add(m2); g.Children.Add(m3);

            Border card = Card(g, new Thickness(0, 0, 0, 8), new Thickness(16, 13));
            Hover(card, Palette.CardBg, Palette.CardHover);
            return card;
        }

        private static Control Metric(string label, string value, double pct, IBrush valueBrush, IBrush meterBrush)
        {
            StackPanel s = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            Grid top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            TextBlock l = T(label, 10.5, Palette.TextFaint);
            Grid.SetColumn(l, 0);
            TextBlock v = T(value, 12, valueBrush, FontWeight.SemiBold);
            Grid.SetColumn(v, 1);
            top.Children.Add(l); top.Children.Add(v);
            s.Children.Add(top);
            s.Children.Add(new Border { Margin = new Thickness(0, 5, 0, 0), Child = Meter(pct, meterBrush, 4) });
            return s;
        }

        /// <summary>C · 深色紧凑：单行会话（发卡线分隔，信息密度优先，无条形）。</summary>
        private static Control SessionRowCompact(SessionRowVm vm)
        {
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("86,*,110,110,120,120"), VerticalAlignment = VerticalAlignment.Center };
            TextBlock id = Mono(vm.ShortId, 11, Palette.TextFaint);
            id.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(id, 0);
            TextBlock title = new TextBlock { Text = vm.TitleText, FontSize = 12, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1);
            TextBlock turns = new TextBlock { Text = vm.MetaText, FontSize = 11, Foreground = Palette.TextDim, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(turns, 2);
            TextBlock tin = new TextBlock { Text = vm.InText, FontSize = 12, Foreground = Palette.Text, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tin, 3);
            TextBlock hit = new TextBlock { Text = vm.HitText, FontSize = 12, Foreground = vm.HitBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hit, 4);
            TextBlock dec = new TextBlock { Text = vm.DecodeText, FontSize = 12, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(dec, 5);
            g.Children.Add(id); g.Children.Add(title); g.Children.Add(turns); g.Children.Add(tin); g.Children.Add(hit); g.Children.Add(dec);
            Border row = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 7),
                Child = g
            };
            Hover(row, Brushes.Transparent, Palette.CardHover);
            return row;
        }

        /// <summary>D · 浅色仪表盘：把可视化放大 —— 标题行 + 三行大比例条。</summary>
        private static Control SessionCardDash(SessionRowVm vm)
        {
            StackPanel s = new StackPanel { Spacing = 8 };
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Ellipse dot = new Ellipse { Width = 9, Height = 9, Fill = vm.StatusBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(dot, 0);
            TextBlock title = new TextBlock { Text = vm.TitleText, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1);
            TextBlock st = T(vm.StatusText, 11, Palette.TextDim);
            st.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(st, 2);
            head.Children.Add(dot); head.Children.Add(title); head.Children.Add(st);
            s.Children.Add(head);
            s.Children.Add(new TextBlock { Text = vm.ShortId + " · " + vm.MetaText + " · " + vm.DecodeLine, FontSize = 11, Foreground = Palette.TextFaint, TextTrimming = TextTrimming.CharacterEllipsis });
            s.Children.Add(DashMeter("输入 token", vm.InText, vm.TokenBar, vm.TokenBrush));
            s.Children.Add(DashMeter("缓存命中", vm.HitText, vm.HitBar, vm.HitBrush));
            s.Children.Add(DashMeter("上下文压力", vm.CtxText, vm.CtxBar, vm.CtxBrush));
            Border card = Card(s, new Thickness(0, 0, 0, 10), new Thickness(18, 14));
            Hover(card, Palette.CardBg, Palette.CardHover);
            return card;
        }

        private static Control DashMeter(string label, string value, double pct, IBrush brush)
        {
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*,76") };
            TextBlock l = T(label, 11, Palette.TextDim);
            l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(l, 0);
            Border m = new Border { VerticalAlignment = VerticalAlignment.Center, Child = Meter(pct, brush, 7) };
            Grid.SetColumn(m, 1);
            TextBlock v = T(value, 11.5, brush, FontWeight.SemiBold);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(v, 2);
            g.Children.Add(l); g.Children.Add(m); g.Children.Add(v);
            return g;
        }

        private static Control CardGridBody(MainWindow host)
        {
            WrapPanel wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < host.Rows.Count; i++)
            {
                Border card = (Border)SessionCardDash(host.Rows[i]);
                card.Margin = new Thickness(0, 0, 12, 12);
                card.Width = 340;
                wrap.Children.Add(card);
            }
            return wrap;
        }

        // ================================================================ 主从式明细

        private static Control MasterList(MainWindow host)
        {
            ListBox box = new ListBox { ItemsSource = host.Rows, SelectedIndex = host.Rows.Count > 0 ? 0 : -1, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            box.ItemTemplate = new FuncDataTemplate<SessionRowVm>(delegate(SessionRowVm vm, INameScope ns)
            {
                StackPanel s = new StackPanel { Spacing = 3 };
                Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                Ellipse dot = new Ellipse { Width = 8, Height = 8, Fill = vm.StatusBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                Grid.SetColumn(dot, 0);
                TextBlock title = new TextBlock { Text = vm.TitleText, FontSize = 12.5, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(title, 1);
                row.Children.Add(dot); row.Children.Add(title);
                s.Children.Add(row);
                s.Children.Add(new TextBlock { Text = vm.ShortId + " · " + vm.HitText + " 命中", FontSize = 11, Foreground = Palette.TextDim, Margin = new Thickness(16, 0, 0, 0) });
                return s;
            });
            box.SelectionChanged += delegate { host.ShowDetail(box.SelectedItem as SessionRowVm); };
            host.ShowDetail(host.Rows.Count > 0 ? host.Rows[0] : null);
            return box;
        }

        private static Control DetailCard(MainWindow host)
        {
            StackPanel s = new StackPanel { Spacing = 12 };
            host.DetailHost = s;
            s.Children.Add(T("（左侧选一个会话）", 12, Palette.TextFaint));
            return Card(s, new Thickness(0), new Thickness(18, 16));
        }

        public static void FillDetail(StackPanel host, SessionRowVm vm)
        {
            if (host == null) return;
            host.Children.Clear();
            if (vm == null)
            {
                host.Children.Add(T("（左侧选一个会话）", 12, Palette.TextFaint));
                return;
            }
            host.Children.Add(Mono(vm.ShortId, 20, Palette.Text));
            StackPanel meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            meta.Children.Add(Chip(vm.StatusText, vm.StatusBrush, Palette.SoftOf(vm.StatusBrush)));
            TextBlock mt = T(vm.MetaText, 12, Palette.TextDim);
            mt.VerticalAlignment = VerticalAlignment.Center;
            meta.Children.Add(mt);
            host.Children.Add(meta);
            host.Children.Add(new Border { Height = 1, Background = Palette.Border, Margin = new Thickness(0, 4, 0, 4) });
            host.Children.Add(KpiRow("输入 token", vm.InText, vm.TokenBar, vm.TokenBrush));
            host.Children.Add(KpiRow("缓存命中率", vm.HitText, vm.HitBar, vm.HitBrush));
            host.Children.Add(KpiRow("上下文压力", vm.CtxText, vm.CtxBar, vm.CtxBrush));
            host.Children.Add(new Border { Height = 1, Background = Palette.Border, Margin = new Thickness(0, 4, 0, 4) });
            host.Children.Add(T("解码速度 " + vm.DecodeText + "　首 token " + vm.TtftText, 12, Palette.TextDim));
            host.Children.Add(T("缓存读 " + vm.CacheReadText, 11, Palette.TextFaint));
        }

        private static Control KpiRow(string label, string value, double percent, IBrush brush)
        {
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("96,110,*") };
            TextBlock l = T(label, 12, Palette.TextDim);
            l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(l, 0);
            TextBlock v = T(value, 14, brush, FontWeight.SemiBold);
            v.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(v, 1);
            Border m = new Border { VerticalAlignment = VerticalAlignment.Center, Child = Meter(percent, brush, 6) };
            Grid.SetColumn(m, 2);
            g.Children.Add(l); g.Children.Add(v); g.Children.Add(m);
            return g;
        }

        // ================================================================ 状态（概览）

        /// <summary>统计视图（会话页「统计」子菜单）：总量、命中率、速度 + 最耗 token 的会话排行。</summary>
        private static Control StatsBody(MainWindow host)
        {
            SessionsSnapshot d = host.Data;
            StackPanel s = new StackPanel { Spacing = 12 };
            if (d == null || !d.Ok)
            {
                s.Children.Add(Card(T("没有可统计的会话数据。", 12, Palette.TextDim), new Thickness(0), new Thickness(16, 14)));
                return s;
            }
            StackPanel sum = new StackPanel { Spacing = 8 };
            sum.Children.Add(T("累计输入 token", 12, Palette.TextDim));
            sum.Children.Add(T(SessionRow.Human(d.TotalIn), 34, Palette.Text, FontWeight.Bold));
            sum.Children.Add(T("输出 " + SessionRow.Human(d.TotalOut) + " · 缓存读 " + SessionRow.Human(d.TotalCacheRead) + " · 会话 " + d.Count + " 个（非空 " + d.NonBlank + "）", 11.5, Palette.TextFaint));
            sum.Children.Add(Meter(d.TotalHitPercent < 0 ? 0 : d.TotalHitPercent, Palette.HitBrush(d.TotalHitPercent >= 90 ? 3 : (d.TotalHitPercent >= 70 ? 2 : 1)), 8));
            sum.Children.Add(T("缓存命中率 " + PctText(d.TotalHitPercent) + "　解码速度 " + TpsText(d.TotalDecodeTps) + " tok/s", 12.5, Palette.TextDim));
            s.Children.Add(Card(sum, new Thickness(0), new Thickness(18, 16)));

            List<SessionRow> top = SessionsView.Sort(SessionsView.Filter(d.Rows, SessionsView.FilterAll), 1);
            StackPanel list = new StackPanel { Spacing = 6 };
            list.Children.Add(T("输入 token 最多的会话", 13, Palette.Text, FontWeight.Bold));
            for (int i = 0; i < top.Count && i < 10; i++)
            {
                SessionRow r = top[i];
                string name = string.IsNullOrEmpty(r.Title) ? r.ShortId : r.Title;
                list.Children.Add(T((i + 1) + ".　" + name + "　　" + r.InText + "　命中 " + r.HitText + "　" + r.DecodeText, 12, Palette.TextDim));
            }
            s.Children.Add(Card(list, new Thickness(0), new Thickness(16, 14)));
            return s;
        }
        /// <summary>体检页：结论徽章 + 分级条目（错误在前），原始输出另有一页。</summary>
        private static Control HealthContent(MainWindow host)
        {
            DoctorSummary d = host.Doctor;
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = 14 };
            if (host.SubTab == 1) { s.Children.Add(new Border { Child = RawCard(host, "原始输出", "doctor 的标记行与分级条目原文") }); return s; }
            // ★★ 体检改为**按钮触发**（2026-10-02 用户要求："体检不自动运行，用按钮触发" ✓✓）
            //   doctor 最长 6.5 秒（含 npm registry 探测 ✓）—— 每次进页自动跑是纯浪费 ✗ 上次的结果保留着 ✓
            StackPanel runBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            runBar.Children.Add(PrimaryButton("运行体检", delegate { host.RunDoctorNow(); }));
            runBar.Children.Add(T("进这一页**不再自动跑** doctor（它最长要 6.5 秒 ✓）；点按钮才跑、逐行出结果，上次的结果保留着 ✓", 11, Palette.TextDim));
            s.Children.Add(Card(runBar, new Thickness(0), new Thickness(16, 14)));
            // ★ 汇总卡只在**结果全到且不在刷新**时显示 ✓
            //   （流式时汇总行在**最后**才来 ✗ 刷新中显示的是**上一次**的结论 ✗ 顶着新行误导人 ✗✓）
            if (d != null && d.Ok && !host.IsLoading)
            {
                IBrush b = d.Error > 0 ? Palette.Bad : (d.Warn > 0 ? Palette.Warn : Palette.Good);
                StackPanel head = new StackPanel { Spacing = 6 };
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                row.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = b, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(T(d.Headline, 24, b, FontWeight.Bold));
                head.Children.Add(row);
                head.Children.Add(T("错误 " + d.Error + " · 提醒 " + d.Warn + " · 通过 " + d.Pass + "（只依据工具箱自己的检查结果，不替它下别的结论）", 12, Palette.TextDim));
                s.Children.Add(Card(head, new Thickness(0), new Thickness(18, 16)));
            }

            // ★★ 杀软式逐行列表（用户建议 2026-10-02 ✓）：`doctor --stream` **来一条画一条** ✓✓
            //   点击瞬间先显示**上次的列表**，新结果逐行覆盖 ✓；这里列的是**全部**条目 ——
            //   原来只列 错误/提醒 ✗ 通过项看不见 ✗ 现在一眼看清"查了什么、各是什么结果" ✓
            if (host.DoctorLive.Count > 0)
            {
                StackPanel list = new StackPanel { Spacing = 5 };
                for (int i = 0; i < host.DoctorLive.Count; i++)
                {
                    string ln = host.DoctorLive[i];
                    IBrush c = (ln.StartsWith("[ERROR", StringComparison.OrdinalIgnoreCase) || ln.StartsWith("[错误]", StringComparison.Ordinal)) ? Palette.Bad
                        : (ln.StartsWith("[WARN", StringComparison.OrdinalIgnoreCase) || ln.StartsWith("[提醒]", StringComparison.Ordinal)) ? Palette.Warn
                        : Palette.TextDim;
                    list.Children.Add(T(ln, 12, c));
                }
                if (host.IsLoading)
                    list.Children.Add(T("…其余项检测中（npm registry 可达性那项最慢，最长 4 秒）", 11, Palette.TextFaint));
                s.Children.Add(Card(list, new Thickness(0), new Thickness(16, 14)));
            }
            else if (d != null && d.Ok && (d.ErrorLines.Count > 0 || d.WarnLines.Count > 0))
            {
                // 兜底：非流式数据源（防御 ✓）—— 保留旧渲染 ✓
                StackPanel list = new StackPanel { Spacing = 6 };
                for (int i = 0; i < d.ErrorLines.Count; i++) list.Children.Add(T(d.ErrorLines[i], 12, Palette.Bad));
                for (int i = 0; i < d.WarnLines.Count; i++) list.Children.Add(T(d.WarnLines[i], 12, Palette.Warn));
                s.Children.Add(Card(list, new Thickness(0), new Thickness(16, 14)));
            }

            if ((d == null || !d.Ok) && host.DoctorLive.Count == 0 && !host.IsLoading)
            {
                s.Children.Add(Card(T("还没有体检结果。点上面的「运行体检」跑一次（会检查网络，最长约 6.5 秒）。", 12, Palette.TextDim), new Thickness(0), new Thickness(16, 14)));
            }
            // 操作日志改为**右下角 toast** ✓✓（不再在页面流里占一张卡片 ✓）
            return s;
        }
        /// <summary>备份页：清单（名称/类型/大小/时间）+ 立即备份 / 导出 / 恢复预览 / 应用恢复 / 删除（两次确认）。</summary>
        private static Control BackupContent(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = 14 };
            // 直接从 CLI 原文解析（不依赖外层装配，少一处可能失联的接线）
            List<BackupItem> items = BackupItems.Parse(host.RawOutput);

            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            Button mk = ActionPrimary(host, "＋ 立即备份", delegate { host.CreateBackup(); });   // 走闸门 ✓ 有动作在跑时禁用 ✓
            bar.Children.Add(mk);
            // Honesty fix (GUI final review): the parser keeps invalid entries visible so the two pages agree on the
// count, but this label says "valid packages" - so it must count only the valid ones, or it contradicts
// the summary card. Invalid rows stay listed and are marked individually.
            int validCount = 0;
            for (int vi = 0; vi < items.Count; vi++) if (!items[vi].Invalid) validCount++;
            bar.Children.Add(T("共 " + validCount + " 份（" + (host.Backups != null && host.Backups.Ok ? "backup-list 有效包" : "未读到清单") + "）", 12, Palette.TextDim));
            s.Children.Add(Card(bar, new Thickness(0), new Thickness(16, 14)));

            // ✓✓ **用户要求（2026-10-01）**：「备份路径在备份页面里设置并且显示吧」✓✓
            //   · **显示**：从 `backup-list` 的 `BACKUP_DIR <路径>` 行读 ✓（`host.BackupDirText` ✓）
            //   · **设置**：「更改位置…」→ 系统文件夹选择器 → `backup-dir --set <目录>` ✓✓
            //   · **恢复默认**：`backup-dir --reset` ✓（回到 `StateDir/backup` ✓）
            //   · **诚实说明**：已有备份**不会被移动** ✓ 仍在原处 ✓ 只有以后的备份写到新位置 ✓
            //     （避免用户以为"改了位置旧备份就过去了" ✗ 那是错的 ✓）
            {
                StackPanel loc = new StackPanel { Spacing = 6 };
                loc.Children.Add(T("备份位置", 12.5, Palette.Text, FontWeight.SemiBold));
                string bd = host.BackupDirText;
                bool known = !string.IsNullOrEmpty(bd);
                loc.Children.Add(Mono(known ? bd : "（读不到路径 —— 点一下「立即备份」或换页刷新后会显示）", 11.5, known ? Palette.Text : Palette.Warn));
                StackPanel lb = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                lb.Children.Add(GhostButton(T("更改位置…", 11.5, Palette.Text), delegate { host.ChangeBackupDir(); }, true));
                lb.Children.Add(GhostButton(T("恢复默认", 11.5, Palette.TextDim), delegate { host.ResetBackupDir(); }, true));
                loc.Children.Add(lb);
                loc.Children.Add(T("已有备份**不会被移动** ✓ 仍在原处 ✓ 只有以后的备份写到新位置 ✓（放在安装目录之外更稳妥 ✓）", 11, Palette.TextFaint));
                s.Children.Add(Card(loc, new Thickness(0), new Thickness(16, 14)));
            }

            if (items.Count == 0)
                s.Children.Add(Card(T("还没有备份。点「立即备份」创建第一份（空数据根不会被算作有效备份，这是刻意的规则）。", 12, Palette.TextDim), new Thickness(0), new Thickness(16, 14)));
            for (int i = 0; i < items.Count; i++)
            {
                BackupItem b = items[i];
                StackPanel row = new StackPanel { Spacing = 8 };
                StackPanel head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                head.Children.Add(Chip(b.KindText, b.Kind == "Manual" ? Palette.Accent : Palette.TextDim, b.Kind == "Manual" ? Palette.AccentSoft : Palette.BarTrack));
                head.Children.Add(T(b.Name, 12.5, Palette.Text, FontWeight.SemiBold));
                head.Children.Add(T(b.SizeText + "　" + b.Time, 11.5, Palette.TextFaint));
                row.Children.Add(head);
                StackPanel acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                Button ex = GhostButton(T("导出", 11.5, Palette.Text), delegate { host.ExportBackup(b.Name); }, true);
                acts.Children.Add(ex);
                Button dr = GhostButton(T("恢复预览", 11.5, Palette.Text), delegate { host.DryRunRestore(b.Name); }, true);
                acts.Children.Add(dr);
                Button ap = GhostButton(T("应用恢复（仅隔离数据根）", 11.5, Palette.Text), delegate { host.ApplyRestore(b.Name); }, true);
                acts.Children.Add(ap);
                // ✓ 用户要求：**删除要弹窗输入当前时间** ✓ → 对话框本身就是确认 ✓ 不再需要两次点击 ✓✓
                // ✓ 用户要求：**删除要弹窗输入当前时间** ✓✓ → **对话框本身就是确认** ✓ 不再需要两次点击 ✓
                //   （`DeleteBackup` 内部会弹输入框 ✓ 输入的时间与真实时间相差 >120 秒 → CLI 拒绝 ✓✓）
                Button del = GhostButton(T("删除（需输入当前时间确认）", 11.5, Palette.Warn), delegate
                {
                    host.DeleteBackup(b.Name);
                }, true);
                del.Background = Palette.WarnSoft;
                acts.Children.Add(del);
                row.Children.Add(acts);
                s.Children.Add(Card(row, new Thickness(0), new Thickness(16, 14)));
            }

            // 操作日志改为**右下角 toast** ✓✓（不再在页面流里占一张卡片 ✓）
            // ════ 清除数据（**危险操作** ✓✓ roadmap Phase 2 的缺口："CLI 已有 wipe，缺 GUI 表面" ✓）════
            // CLI 的 `wipe` 有**五道闸门** ✓✓（读过源码确认 ✓）：
            //   ① 不加 --yes 只出 WIPE_PLAN 计划 ✓  ② --yes 确认 ✓
            //   ③ **先做 -pre-wipe 安全备份，且必须成功** ✓✓（做不出就不清 ✓ 这是最好的设计 ✓）
            //   ④ 数据根是盘根/系统根 → WIPE_REFUSED ✓  ⑤ 备份目录在数据根内 → WIPE_REFUSED ✓
            // GUI 侧只做三件事 ✓：**先看计划** ✓ · **两次点击确认**（house style ✓ 与"删除备份"一致 ✓）· **如实显示安全备份路径** ✓
            StackPanel wipe = new StackPanel { Spacing = 8 };
            wipe.Children.Add(T("清除数据（已改为手动）", 13, Palette.Warn, FontWeight.SemiBold));
            wipe.Children.Add(T("**本工具不再提供清除数据** —— 按用户要求删掉了这个操作 ✓ 避免误点造成不可逆的丢失 ✓。", 11.5, Palette.TextDim));
            // ✓✓ **用户要求（2026-09-30）**：「删除中排除备份文件夹且无法选择，要删自己删」✓✓
            //   · 卸载器侧：**显式跳过 `backup/` 与 `logs/`** ✓✓（installer.cs 已做 ✓）
            //   · 界面上：**没有删除备份文件夹的选项** ✓✓（这里是说明 ✓ 不是按钮 ✓）
            wipe.Children.Add(T("备份文件夹**不在删除范围内** ✓ 本工具**不会**删它 ✓ 想删请**自己**去删（路径见下面的按钮 ✓）。", 11.5, Palette.TextDim));
            wipe.Children.Add(T("界面上**没有**删除备份文件夹的选项 ✓ 这是刻意的 —— 备份是你最后的退路 ✓ 不该被一键清掉 ✓。", 11.5, Palette.TextFaint));
            StackPanel wacts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            wacts.Children.Add(GhostButton(T("显示手动删除路径（只读 ✓ 不删任何东西 ✓）", 11.5, Palette.Text), delegate { host.WipePlan(); }, true));
            // ★★★ **用户要求（2026-09-30）**：「删除 GUI 备份里的清除数据操作按钮，点击只弹出手动删除路径」✓✓
            //   → 原来这里有两个按钮：「先看将删除什么」+「**清除数据…**」（两次点击确认后真删 ✓）
            //   → **现在只留一个只读按钮** ✓✓ 危险按钮**整段删除** ✓
            //   → CLI 侧同样改成了**永不删除** ✓（`wipe` 只输出路径 ✓ 带 `--yes` 也不删 ✓✓）
            //   → 备份页那句"恢复是合并语义…"保留 ✓（它不是危险操作 ✓）
            wipe.Children.Add(wacts);
            s.Children.Add(Card(wipe, new Thickness(0), new Thickness(16, 14)));
            s.Children.Add(Card(T("恢复是合并语义：只覆盖同名文件，不删除目标端独有的文件；「应用恢复」只允许写入隔离数据根（CLI 的准入闸门会拒绝其它情况并把原因显示在上面）。", 11.5, Palette.TextFaint), new Thickness(0), new Thickness(16, 12)));
            return s;
        }

        /// <summary>设置页：逐键编辑（config-get / config-set），开关型给两个按钮，只读键禁编辑。</summary>
        /// <summary>取某个配置键的备注（CLI 的 `CONFIGNOTE <key> <说明>` ✓ 原样显示 ✓ 没有就返回空 ✓）。</summary>
        private static string NoteFor(string raw, string key)
        {
            if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(key)) return "";
            string[] ls = raw.Replace("\r\n", "\n").Split('\n');
            string pre = "CONFIGNOTE " + key + " ";
            for (int i = 0; i < ls.Length; i++)
            {
                string t2 = ls[i] == null ? "" : ls[i].Trim();
                if (t2.StartsWith(pre, StringComparison.Ordinal)) return t2.Substring(pre.Length).Trim();
            }
            return "";
        }

        /// <summary>从 CLI 的 `CONFIG update_channel <v>` 读当前更新通道 ✓（读不到就说"读不到" ✓ 不猜 ✗）。</summary>
        private static string UpdateChannelText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "读不到";
            string[] ls = raw.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < ls.Length; i++)
            {
                string t2 = ls[i] == null ? "" : ls[i].Trim();
                if (t2.StartsWith("CONFIG update_channel ", StringComparison.Ordinal)) return t2.Substring("CONFIG update_channel ".Length).Trim();
            }
            return "读不到";
        }
        /// <summary>更新中心页 ✓✓（用户要求："放到左侧菜单更新内，其中检查 webui/desktop/dsh minato/已经安装的插件的更新列表和版本，如果能获取更新日志那最好了" ✓）
        /// 数据全部来自 CLI 的 `update-center`（**只读** ✓）→ GUI 只解析 ✓ 不自己算 ✗。
        /// 更新动作：**web 走 CLI 的 update（先自动备份 + 回滚点 ✓ 需确认 ✓）**；
        /// **desktop 只给官方安装页** ✓（用户指定 ✓ 本工具不重打包 ✗）；
        /// **插件只给说明与地址** ✓（由各自作者维护 ✓ 本工具不替它更新 ✗ 也不替它担保 ✗）。</summary>
        /// <summary>日志中心页 ✓✓（roadmap Phase 2 的缺口之一："CLI 已有 log，缺 GUI 表面" ✓）
        /// 数据全部来自 CLI 的 `log`（**只读** ✓）→ GUI 只解析 ✓ 不自己读文件 ✗。
        /// 筛选走 **CLI 的参数** ✓（`--level` / `--grep` / `--lines` ✓）—— 与"GUI 只调 CLI"的架构一致 ✓✓
        /// 导出走 CLI 的 `--export` ✓（CLI 自己带 `--yes` 确认闸门 ✓ 这里再问一次 ✓ 双保险 ✓）。</summary>
        private static Control LogCenterContent(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = 14 };

            // —— 顶部：级别筛选 + 搜索 + 行数 + 导出 ✓ ——
            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            string cur = host.LogFilter;
            foreach (string lv in new string[] { "全部", "INFO", "WARN", "ERROR" })
            {
                string val = lv == "全部" ? "" : lv;
                bool on = (cur == val);
                Button b = new Button
                {
                    Content = T(lv, 11.5, on ? Palette.OnAccent : Palette.TextDim),
                    Background = on ? Palette.Accent : Palette.CardHover,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 5)
                };
                b.Click += delegate { host.SetLogFilter(val); };
                bar.Children.Add(b);
            }
            bar.Children.Add(T("行数", 11, Palette.TextFaint));
            foreach (int n in new int[] { 100, 500, 2000 })
            {
                int nn = n;
                bool on = host.LogLines == nn;
                Button b = new Button
                {
                    Content = T(nn.ToString(), 11.5, on ? Palette.OnAccent : Palette.TextDim),
                    Background = on ? Palette.Accent : Palette.CardHover,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 5)
                };
                b.Click += delegate { host.SetLogLines(nn); };
                bar.Children.Add(b);
            }
            bar.Children.Add(PrimaryButton("刷新", delegate { host.Refresh(); }));
            bar.Children.Add(GhostButton(T("导出到文件…", 11.5, Palette.Text), delegate { host.ExportLog(); }, true));
            s.Children.Add(Card(bar, new Thickness(0), new Thickness(16, 14)));

            // —— 搜索框 ✓（按关键词过滤 ✓ 走 CLI 的 --grep ✓）——
            StackPanel find = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            TextBox box = new TextBox { Text = host.LogGrep, Width = 320, Watermark = "按关键词过滤（走 CLI 的 --grep）" };
            find.Children.Add(box);
            find.Children.Add(GhostButton(T("搜索", 11.5, Palette.Text), delegate { host.SetLogGrep(box.Text == null ? "" : box.Text.Trim()); }, true));
            find.Children.Add(GhostButton(T("清除", 11.5, Palette.Text), delegate { host.SetLogGrep(""); }, true));
            find.Children.Add(T("当前：" + (string.IsNullOrEmpty(host.LogGrep) ? "（无过滤）" : host.LogGrep), 11, Palette.TextFaint));
            s.Children.Add(Card(find, new Thickness(0), new Thickness(16, 12)));

            // —— 内容 ✓（等宽字体 ✓ 逐行 ✓）——
            string raw = host.RawOutput;
            if (string.IsNullOrEmpty(raw) || raw.IndexOf("LOG_EMPTY", StringComparison.Ordinal) >= 0)
            {
                StackPanel empty = new StackPanel { Spacing = 6 };
                empty.Children.Add(T("还没有日志", 13, Palette.TextDim, FontWeight.SemiBold));
                empty.Children.Add(T("工具箱的操作日志会写在状态目录的 logs/launcher.log 里。装过一次、启动过一次之后就会有了。", 11.5, Palette.TextFaint));
                s.Children.Add(Card(empty, new Thickness(0), new Thickness(16, 20)));
                return s;
            }

            // 性能优化（2026-10-06 #2）：string + 数据模板 + 虚拟化 —— 2000 行档不再全量建 TextBlock ✓
            List<string> logLines = new List<string>();
            string[] all = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int shown = 0;
            bool inStderr = false;   // N13 FIX: a [stderr] block spans several lines
            for (int i = 0; i < all.Length; i++)
            {
                string l = all[i] == null ? "" : all[i].TrimEnd();
                if (l.Length == 0) continue;
                // CLI 的真实输出是 **`LOG_LINE <内容>`** ✓（VM 实测确认 ✓）→ **剥掉前缀显示内容** ✓
                // ✗ 不能整行跳过 ✗ —— 那样页面会一片空白 ✓（我第一版就是这么写的 ✗ 实测抓到了 ✓）
                if (l.StartsWith("LOG_LINE ", StringComparison.Ordinal)) l = l.Substring("LOG_LINE ".Length);
                else if (l.StartsWith("LOG_OK", StringComparison.Ordinal)) continue;        // 计数行 ✓ 不显示
                else if (l.StartsWith("LOG_EXPORT", StringComparison.Ordinal)) continue;    // 导出回执 ✓ 由 toast 显示
                else if (l.StartsWith("LOG_EMPTY", StringComparison.Ordinal)) continue;     // 空状态 ✓ 上面已处理
                else if (l.StartsWith("INTEGRITY_", StringComparison.Ordinal)) continue;    // 完整性提示 ✓ 不属于日志正文
                // ★★★ **N13 修复（GUI 复审 MINOR —— stderr 多行只跳过第一行）** ✓✓
                //   ✗ `Run()` 把 stderr 作为**一整块**追加 ✓ 只有**第一行**带 `[stderr]` 前缀 ✗
                //     → 后续行**照旧被当成日志记录渲染** ✗ 还让"共 N 行"多算 ✓
                //   ✓ 现在：**遇到 `[stderr]` 就进入跳过模式** ✓✓ 直到块结束 ✓
                else if (l.StartsWith("[stderr]", StringComparison.Ordinal)) { inStderr = true; continue; }
                else if (inStderr) { if (l.StartsWith("LOG_", StringComparison.Ordinal)) inStderr = false; else continue; }
                if (l.Trim().Length == 0) continue;
                logLines.Add(l);
                shown++;
            }
            if (shown == 0)
            {
                s.Children.Add(Card(T("筛选后没有匹配的日志行（换个级别或关键词试试）。", 12, Palette.TextDim), new Thickness(0), new Thickness(16, 16)));
                return s;
            }
            ItemsControl logList = new ItemsControl();
            logList.ItemsSource = logLines;
            logList.ItemTemplate = new FuncDataTemplate<string>(delegate(string s2, INameScope ns2) { return Mono(s2, 11, LogLineBrush(s2)); });
            logList.ItemsPanel = new global::Avalonia.Controls.Templates.FuncTemplate<Panel>(delegate { return new VirtualizingStackPanel(); });
            s.Children.Add(Card(new ScrollViewer { Content = logList, MaxHeight = 460, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }, new Thickness(0), new Thickness(16, 14)));
            s.Children.Add(T("共 " + shown + " 行 · 级别 " + (string.IsNullOrEmpty(host.LogFilter) ? "全部" : host.LogFilter)
                + " · 关键词 " + (string.IsNullOrEmpty(host.LogGrep) ? "（无）" : host.LogGrep)
                + " · 最多 " + host.LogLines + " 行（筛选由 CLI 的 --level/--grep/--lines 完成）", 11, Palette.TextFaint));
            return s;
        }

        /// <summary>动作按钮（幽灵款）✓：已有 CLI 动作在跑时**禁用并显示在跑的是谁** ✗ 不给点 ✗。
        /// 闸门本身在 MainWindow.RunCliAction ✓（这里只管**别引诱人去点** ✓✓
        /// —— 2026-10-04 用户在工作电脑上实测"更新可以连续点好几次" ✗）。</summary>
        private static Button ActionGhost(MainWindow host, string label, Action act)
        {
            if (host.ActionBusy)
            {
                Button b = GhostButton(T("进行中：" + host.ActionBusyLabel + " …", 11.5, Palette.TextFaint), delegate { }, true);
                b.IsEnabled = false;
                return b;
            }
            return GhostButton(T(label, 11.5, Palette.Text), act, true);
        }

        /// <summary>动作按钮（主按钮款）✓ 同上。</summary>
        private static Button ActionPrimary(MainWindow host, string label, Action act)
        {
            if (host.ActionBusy)
            {
                Button b = PrimaryButton("进行中：" + host.ActionBusyLabel + " …", delegate { });
                b.IsEnabled = false;
                return b;
            }
            return PrimaryButton(label, act);
        }

        private static Control UpdateCenterContent(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = 14 };

            // —— 顶部：检查按钮 + 一句说明 ——
            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            bar.Children.Add(PrimaryButton("检查更新", delegate { host.Refresh(); }));
            bar.Children.Add(T("只读检查 ✓ 不动任何东西；更新前会**自动备份**并保留回滚点 ✓", 11.5, Palette.TextDim));
            s.Children.Add(Card(bar, new Thickness(0), new Thickness(16, 14)));

            string raw = host.RawOutput;
            List<UpdItem> items = UpdParse(raw);
            if (items.Count == 0)
            {
                s.Children.Add(Card(T("还没读到更新信息（CLI 未返回 UPDATECENTER_* 标记）。点上面的「检查更新」试试。", 12, Palette.TextDim), new Thickness(0), new Thickness(16, 14)));
                return s;
            }

            for (int i = 0; i < items.Count; i++)
            {
                UpdItem it = items[i];
                StackPanel card = new StackPanel { Spacing = 9 };

                // 标题行：名称 + 状态徽章
                StackPanel head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                head.Children.Add(T(it.Title, 13, Palette.Text, FontWeight.SemiBold));
                head.Children.Add(Chip(it.StateText, it.StateBrush, it.StateBg));
                head.Children.Add(T(it.KindText, 11, Palette.TextFaint));
                card.Children.Add(head);

                // 版本行 ✓（取不到就写 unknown ✓ 不猜 ✗）
                card.Children.Add(T("已装 " + it.Installed + "　→　最新 " + it.Latest, 12, Palette.TextDim));

                // 更新日志 ✓（有就显示 ✓ 没有就说没有 ✓）
                if (!string.IsNullOrEmpty(it.Log))
                    card.Children.Add(T("更新日志：" + it.Log, 11.5, Palette.TextFaint));

                // 说明 / 风险 ✓✓（用户要求："更新前描述风险并且确认" ✓）
                if (!string.IsNullOrEmpty(it.Note))
                    card.Children.Add(T(it.Note, 11.5, Palette.Warn));

                // 地址 ✓（GitHub / 官方页 ✓）
                if (!string.IsNullOrEmpty(it.Url) && it.Url != "unknown")
                {
                    // U8：URL 可点（desktop 有按钮、webui/插件此前只是纯文本 ✗ affordance 断裂 ✓ 补上）
                    StackPanel urlRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    urlRow.Children.Add(T(it.Url, 11, Palette.Accent));
                    urlRow.Children.Add(GhostButton(T("打开", 11, Palette.Text), delegate { host.OpenUrl(it.Url); }, true));
                    card.Children.Add(urlRow);
                }

                // 动作行 ✓
                StackPanel acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (it.Id == "webui")
                {
                    acts.Children.Add(ActionGhost(host, "更新 dsh web（会先备份）", delegate { host.ConfirmUpdateWeb(); }));
                }
                else if (it.Id == "desktop")
                {
                    acts.Children.Add(GhostButton(T("打开官方安装页", 11.5, Palette.Text), delegate { host.OpenDesktopPage(); }, true));
                }
                else if (it.Id == "minato")
                {
                    acts.Children.Add(T("本工具的更新不影响你的数据 ✓（不动 ~/.dsh、备份与配置 ✓）", 11, Palette.TextFaint));
                }
                else
                {
                    acts.Children.Add(T("插件由各自作者维护 ✓ 本工具不替它更新、也不替它担保 ✓ 更新前请先看上面的风险说明 ✓", 11, Palette.TextFaint));
                }
                card.Children.Add(acts);
                s.Children.Add(Card(card, new Thickness(0), new Thickness(16, 14)));
            }

            // ★ 杀软式逐个出结果（用户建议 2026-10-02 ✓）：`update-center --stream` 让快项（桌面端/本工具/插件）
            //   秒出、webui 的网络查询最后出 ✓ 刷新中在尾部明说"还有谁没回来" ✓ 不写"请稍候"这种空话 ✗
            if (host.IsLoading)
                s.Children.Add(Card(T("…其余组件查询中（webui 要查 npm / GitHub，最慢；完成后会自动补上）", 11.5, Palette.TextFaint), new Thickness(0), new Thickness(16, 14)));

            s.Children.Add(Card(T("纪律：**检查是只读的** ✓；**更新前一律自动备份** ✓（本工具自己的更新除外 —— 它不碰数据 ✓）；**desktop 只去官方安装页** ✓；**插件更新由你自行决定** ✓ 本工具只如实展示版本与地址 ✓", 11.5, Palette.TextFaint), new Thickness(0), new Thickness(16, 12)));
            return s;
        }

        /// <summary>更新中心的一条 ✓（从 CLI 的 UPDATECENTER_* 行解析 ✓）。</summary>
        private sealed class UpdItem
        {
            public string Id = "";
            public string Kind = "";
            public string Installed = "unknown";
            public string Latest = "unknown";
            public string State = "unknown";
            public string Url = "";
            public string Note = "";
            public string Log = "";
            public string Profile = "";
            public string Title { get { return Id.StartsWith("plugin:", StringComparison.Ordinal) ? Id.Substring(7) : Id; } }
            public string KindText
            {
                get
                {
                    if (Kind == "webui") return "dsh web（本体）";
                    if (Kind == "desktop") return "官方桌面端";
                    if (Kind == "minato") return "本工具";
                    return Profile.Length > 0 ? ("插件 · profile " + Profile) : "插件";
                }
            }
            public string StateText
            {
                get
                {
                    if (State == "up-to-date") return "已是最新";
                    if (State == "update-available") return "有更新";
                    if (State == "newer-than-latest") return "比最新还新";
                    if (State == "external") return "在官方页更新";
                    return "未知";
                }
            }
            public IBrush StateBrush
            {
                get
                {
                    if (State == "update-available") return Palette.OnAccent;
                    if (State == "up-to-date") return Palette.OnAccent;
                    return Palette.Text;
                }
            }
            public IBrush StateBg
            {
                get
                {
                    if (State == "update-available") return Palette.Accent;
                    if (State == "up-to-date") return Palette.Good;
                    return Palette.CardHover;
                }
            }
        }

        /// <summary>解析 `UPDATECENTER_*` 行 ✓（纯字符串 ✓ 不引 JSON ✓ 保持零依赖 ✓）。</summary>
        private static List<UpdItem> UpdParse(string raw)
        {
            List<UpdItem> list = new List<UpdItem>();
            if (string.IsNullOrEmpty(raw)) return list;
            string[] ls = raw.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < ls.Length; i++)
            {
                string t2 = ls[i] == null ? "" : ls[i].Trim();
                if (!t2.StartsWith("UPDATECENTER_", StringComparison.Ordinal)) continue;
                string[] parts = t2.Split(' ');
                if (parts.Length < 3) continue;
                if (parts[0] == "UPDATECENTER_ITEM")
                {
                    UpdItem it = new UpdItem();
                    it.Id = parts[1];
                    for (int k = 2; k < parts.Length; k++)
                    {
                        int eq = parts[k].IndexOf('=');
                        if (eq <= 0) continue;
                        string kk = parts[k].Substring(0, eq);
                        string vv = parts[k].Substring(eq + 1);
                        if (kk == "kind") it.Kind = vv;
                        else if (kk == "installed") it.Installed = vv;
                        else if (kk == "latest") it.Latest = vv;
                        else if (kk == "state") it.State = vv;
                        else if (kk == "profile") it.Profile = vv;
                    }
                    list.Add(it);
                }
                else if (parts[0] == "UPDATECENTER_URL" || parts[0] == "UPDATECENTER_NOTE" || parts[0] == "UPDATECENTER_LOG")
                {
                    string id = parts[1];
                    string val = t2.Substring(parts[0].Length + 1 + id.Length).Trim();
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (list[k].Id != id) continue;
                        if (parts[0] == "UPDATECENTER_URL") list[k].Url = val;
                        else if (parts[0] == "UPDATECENTER_NOTE") list[k].Note = val;
                        else list[k].Log = val;
                        break;
                    }
                }
            }
            return list;
        }

        private static Control SettingsContent(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = 12 };
            // 直接从 CLI 原文解析（同上）
            List<ConfigItem> items = ConfigMarkers.Parse(host.RawOutput);
            if (items.Count == 0)
            {
                s.Children.Add(Card(T("没有读到配置项（CLI 未返回 CONFIG 行）。点「刷新」重试；一直读不到就检查 dsh-minato.exe 是否与本程序同目录。", 12, Palette.TextDim), new Thickness(0), new Thickness(16, 14)));
                return s;
            }
            // —— 更新中心 ✓✓（用户问："web 更新选项/检查更新按钮在哪里" ✓ 答案是**之前没有** ✗ → 现在有 ✓）——
            // CLI 早就有 `update-info`（**只读** ✓ 报已装/通道/最新/状态/更新前备份/回滚 ✓）
            // 和 `update`（**执行** ✓ 含更新前备份 + 回滚 ✓）—— 只是 GUI 一个按钮都没接 ✗
            StackPanel upd = new StackPanel { Spacing = 8 };
            upd.Children.Add(T("更新", 12.5, Palette.Text, FontWeight.SemiBold));
            upd.Children.Add(T("检查更新是**只读**的 ✓（只查已装版本、更新通道、最新版本，不动任何东西）；更新会**先备份再更新** ✓ 并保留回滚点 ✓", 11, Palette.TextDim));
            StackPanel updRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            updRow.Children.Add(ActionPrimary(host, "检查更新", delegate { host.CheckUpdate(); }));
            updRow.Children.Add(ActionGhost(host, "执行更新（会先备份）", delegate { host.RunUpdate(); }));
            updRow.Children.Add(T("更新通道：" + (UpdateChannelText(host.RawOutput)), 11, Palette.TextFaint));
            upd.Children.Add(updRow);
            // ★ 正在跑别的动作时**说清楚**为什么按钮点不了 ✓（2026-10-04 用户实测"更新能连点好几次" ✗ → 加了闸门 ✓
            //   静默禁用会让人以为界面坏了 ✗ 所以这里写明"在跑什么、要等它" ✓✓）
            if (host.ActionBusy)
                upd.Children.Add(T("已有动作在进行：" + host.ActionBusyLabel + " —— 结束前上面的按钮不可点 ✓（同时只允许一个，避免多个更新互相踩 ✗）", 11, Palette.Warn));
            s.Children.Add(Card(upd, new Thickness(0), new Thickness(16, 14)));
            int roCount = 0; int swCount = 0;
            for (int ci = 0; ci < items.Count; ci++) { if (items[ci].ReadOnly) roCount++; else if (items[ci].IsSwitch) swCount++; }
            StackPanel sum = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            sum.Children.Add(Chip(items.Count + " 项", Palette.Accent, Palette.AccentSoft));
            sum.Children.Add(Chip("可改 " + (items.Count - roCount) + "", Palette.Good, Palette.GoodSoft));
            sum.Children.Add(Chip("开关 " + swCount + "", Palette.TextDim, Palette.CardHover));
            sum.Children.Add(Chip("只读 " + roCount + "", Palette.TextFaint, Palette.CardHover));
            s.Children.Add(Card(sum, new Thickness(0), new Thickness(14, 10)));
            s.Children.Add(Card(T("配置写入会立即生效并落盘（CLI 的 config-set）；键名与 v2.x 完全一致，可用文本编辑器对照。", 11.5, Palette.TextFaint), new Thickness(0), new Thickness(16, 12)));
            // ★★ 设置分类渲染（2026-10-02 用户要求："把设置界面分类，然后做到更简单一点（选择框）" ✓✓）
            //   ① 分两组：界面与启动 / dsh·更新·数据·余额 ✓ 没归组的（只读项等）→ 「其它」原样显示 ✓
            //   ② 枚举型配置一律**下拉选择框** ✗ 不再手敲 ✗（lang/host/通道/关闭行为/自启目标/浏览器方式/启动页/刷新间隔 ✓）
            string[] guiKeys = new string[] { "gui_start_page", "gui_auto_refresh", "lang", "browser_mode", "ui_parallel", "scan_children" };
            string[] dshKeys = new string[] { "auto_start", "auto_start_target", "check_update", "check_dsh_update", "update_channel", "close_action", "host", "keep_backups", "ws" };
            // ★ 余额检测**单独成组**（2026-10-04 用户实测"余额配置入口我没找到" ✗ ——
            //   原来它混在 "dsh · 更新 · 数据 · 余额" 组的最后一行 ✗ 现在单独一张卡、标题直接写清楚 ✓✓）
            string[] balKeys = new string[] { "balance_key" };
            List<ConfigItem> placed = new List<ConfigItem>();
            RenderSettingsGroup(s, host, PickItems(items, guiKeys, placed), "界面与启动（含排障开关）");
            RenderSettingsGroup(s, host, PickItems(items, dshKeys, placed), "dsh · 更新 · 数据");
            RenderSettingsGroup(s, host, PickItems(items, balKeys, placed), "DeepSeek 余额检测（概览页显示充值 / 赠送余额）");
            RenderSettingsGroup(s, host, RestItems(items, placed), "其它");
            // 操作日志改为**右下角 toast** ✓✓（不再在页面流里占一张卡片 ✓）
            return s;
        }
        // —— 设置页的分组渲染辅助（2026-10-02 用户要求"分类 + 选择框" ✓✓）——

        /// <summary>按 keys 的顺序从 items 里挑出该组的配置项（挑过的记进 placed ✓ 不重复 ✓）。</summary>
        private static List<ConfigItem> PickItems(List<ConfigItem> items, string[] keys, List<ConfigItem> placed)
        {
            List<ConfigItem> g = new List<ConfigItem>();
            for (int k = 0; k < keys.Length; k++)
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Key == keys[k] && !placed.Contains(items[i])) { g.Add(items[i]); placed.Add(items[i]); }
            return g;
        }

        /// <summary>没被任何组挑走的 → 原样显示在「其它」（如只读 dsh_versions ✓）。</summary>
        private static List<ConfigItem> RestItems(List<ConfigItem> items, List<ConfigItem> placed)
        {
            List<ConfigItem> g = new List<ConfigItem>();
            for (int i = 0; i < items.Count; i++) if (!placed.Contains(items[i])) g.Add(items[i]);
            return g;
        }

        private static void RenderSettingsGroup(StackPanel s, MainWindow host, List<ConfigItem> group, string title)
        {
            if (group == null || group.Count == 0) return;
            s.Children.Add(Card(T(title, 13, Palette.Text, FontWeight.SemiBold), new Thickness(0), new Thickness(16, 12)));
            for (int i = 0; i < group.Count; i++) s.Children.Add(SettingsItemCard(host, group[i]));
        }

        /// <summary>枚举型配置的**合法取值**（做成选择框 ✗ 不再手敲 ✗）。返回 null = 不是枚举（走文本框/开关）✓。
        /// 必须与 CLI 白名单（ConfigValidator ✓）**逐字一致** ✗ 否则选择框会写进被拒的值 ✗✗。</summary>
        private static string[] OptionsFor(string key)
        {
            switch (key)
            {
                case "lang": return new string[] { "auto", "zh", "en" };
                case "host": return new string[] { "127.0.0.1", "localhost" };
                case "update_channel": return new string[] { "stable", "rc" };
                case "close_action": return new string[] { "ask", "tray", "exit" };
                case "auto_start_target": return new string[] { "auto", "desktop", "web" };
                case "browser_mode": return new string[] { "auto", "snap", "direct", "xdg" };
                case "gui_auto_refresh": return new string[] { "off", "0.5", "1", "3", "5" };
                case "gui_start_page": return MainWindow.NavItems;   // 索引即值 ✓（0..9 ✓）
                case "gui_shell": return new string[] { Name(0), Name(1), Name(2), Name(3), Name(4) };   // 索引即值（0..4 ✓ 2026-10-06 U4）
                case "gui_style": return new string[] { Palette.StyleName(0), Palette.StyleName(1), Palette.StyleName(2), Palette.StyleName(3) };   // 索引即值（0..3 ✓）
                default: return null;
            }
        }

        /// <summary>2026-10-06 审查 U1：这几个键在 V3（跨平台 GUI + CLI）里**没有消费方** ✗
        /// 设置页保留它们（配置兼容 ✓），但整行标灰并如实注明 ✓（低成本诚实方案；补实现是另一件事 ✓）。</summary>
        private static string DeadNoteFor(string key)
        {
            if (key == "close_action") return "⚠ V3 不消费此项：本窗口的关闭就是直接退出，没有托盘/询问（经典版 v2.x 核心的设置，保留仅为配置兼容）";
            if (key == "host") return "⚠ V3 不消费此项：服务地址固定为 127.0.0.1:3080（经典版 v2.x 核心的设置，保留仅为配置兼容）";
            if (key == "check_update") return "⚠ V3 不消费此项：本工具自身的更新检查未接线（V3 里真正生效的是 check_dsh_update）";
            if (key == "lang") return "⚠ 此项只影响命令行输出语言；本图形界面暂为中文";
            return null;
        }

        /// <summary>单个配置项的卡片（key + 人话说明 + CONFIGNOTE 备注 + 编辑控件 ✓）。</summary>
        private static Control SettingsItemCard(MainWindow host, ConfigItem c)
        {
            StackPanel row = new StackPanel { Spacing = 8 };
            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            head.Children.Add(T(c.Key, 12.5, Palette.Text, FontWeight.SemiBold));
            head.Children.Add(T(c.Desc, 11.5, Palette.TextDim));
            row.Children.Add(head);
            // 备注 ✓✓（用户要求："备注一下发生什么问题可以尝试启用和禁用" ✓）—— CONFIGNOTE 原样显示 ✓
            string note = NoteFor(host.RawOutput, c.Key);
            if (!string.IsNullOrEmpty(note)) row.Children.Add(T(note, 11, Palette.Warn));

            StackPanel edit = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (c.ReadOnly)
            {
                edit.Children.Add(T(c.Value, 12, Palette.TextFaint));
            }
            else if (OptionsFor(c.Key) != null)
            {
                // ★ 选择框（用户要求 ✓✓）：枚举型配置一律下拉选 ✓
                string[] opts = OptionsFor(c.Key);
                int idx;
                if (c.Key == "gui_start_page" || c.Key == "gui_shell" || c.Key == "gui_style")   // 索引即值的三个键（2026-10-06 U4）
                {
                    int dflt = (c.Key == "gui_shell" ? 4 : (c.Key == "gui_style" ? 0 : 1));   // 各自默认 ✓
                    int sp; if (!int.TryParse(c.Value, out sp) || sp < 0 || sp >= opts.Length) sp = dflt; idx = sp;
                }
                else idx = Array.IndexOf(opts, c.Value);
                ComboBox cb = new ComboBox { MinWidth = 200, FontSize = 12, ItemsSource = opts, SelectedIndex = idx };
                // ★ 先设 SelectedIndex、后挂事件 ✓ —— 初始化那一拍不写盘 ✓
                cb.SelectionChanged += delegate
                {
                    if (cb.SelectedIndex >= 0 && cb.SelectedIndex < opts.Length)
                    {
                        bool byIndex = (c.Key == "gui_start_page" || c.Key == "gui_shell" || c.Key == "gui_style");
                        host.SetConfig(c.Key, byIndex ? cb.SelectedIndex.ToString() : opts[cb.SelectedIndex]);
                    }
                };
                edit.Children.Add(cb);
            }
            else if (c.IsSwitch)
            {
                string[] opts2 = new string[] { "on", "off" };
                for (int k = 0; k < opts2.Length; k++)
                {
                    string val = opts2[k];
                    bool active = c.Value == val;
                    Button ob = active
                        ? PrimaryButton(val, delegate { host.SetConfig(c.Key, val); })
                        : GhostButton(T(val, 11.5, Palette.TextDim), delegate { host.SetConfig(c.Key, val); }, true);
                    edit.Children.Add(ob);
                }
            }
            else if (c.Key == "balance_key")
            {
                // ★★ 审查 M1 修复：key 不回显明文 ✗（config-get 只报 set/unset ✓）
                //   输入新值→保存替换；留空→保持不变；「清除」→解除绑定 ✓（绝不把掩码写回配置 ✗✗）
                bool hasKey = c.Value != null && c.Value.Trim() == "set";
                TextBox kb = new TextBox { Text = "", Width = 300, FontSize = 12, Watermark = hasKey ? "已设置——输入新值替换；留空=保持不变" : "未绑定——粘贴你的 DeepSeek API key" };
                edit.Children.Add(kb);
                edit.Children.Add(ActionPrimary(host, "保存", delegate
                {
                    string nv = kb.Text == null ? "" : kb.Text.Trim();
                    if (nv.Length > 0) host.SetConfig(c.Key, nv);
                    else host.ShowToast("留空 = 保持当前 key 不变 ✓ 要解除绑定请点「清除」");
                }));
                if (hasKey) edit.Children.Add(ActionGhost(host, "清除（解除绑定）", delegate { host.SetConfig(c.Key, ""); }));
                edit.Children.Add(T("⚠ key 以明文存在本机配置文件里，只在你自己的机器上 ✓ 不上传 ✓；未绑定则概览页不显示余额卡", 11, Palette.Warn));
            }
            else
            {
                TextBox box = new TextBox { Text = c.Value, Width = 300, FontSize = 12 };
                edit.Children.Add(box);
                Button save = PrimaryButton("保存", delegate { host.SetConfig(c.Key, box.Text == null ? "" : box.Text.Trim()); });
                edit.Children.Add(save);
                if (c.Key == "ws") edit.Children.Add(T("留空=自动探测；填了必须存在", 11, Palette.TextFaint));

            }
            row.Children.Add(edit);
            // ★ 2026-10-06 审查 U1：V3 无消费方的键 → 整行标灰 + 如实注明 ✓（控件保留、值仍显示 ✓）
            string deadNote = DeadNoteFor(c.Key);
            if (deadNote != null)
            {
                edit.IsEnabled = false;
                edit.Opacity = 0.6;
                row.Children.Add(T(deadNote, 11, Palette.Warn));
            }
            return Card(row, new Thickness(0), new Thickness(16, 12));
        }

        /// <summary>看板图表：近 N 天新增会话（柱状，N = 7/14/30 可切）+ 缓存命中率分布（柱状）。
        /// **手绘**（Grid + Border 柱），不引入任何图表依赖；日期用 ISO 字符串前缀比对，不做时区/日历运算（不猜）。</summary>
        private static Control ChartsBody(MainWindow host)
        {
            SessionsSnapshot d = host.Data;
            StackPanel s = new StackPanel { Spacing = 14 };
            if (d == null || !d.Ok)
            {
                s.Children.Add(Card(T("没有可绘制的会话数据。", 12, Palette.TextDim), new Thickness(0), new Thickness(16, 14)));
                return s;
            }

            // ① 近 N 天新增会话（N = 7/14/30 可切）
            // 日期范围切换 ✓（用户要求 ✓）
            StackPanel range = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            int[] opts = new int[] { 7, 14, 30 };
            for (int oi = 0; oi < opts.Length; oi++)
            {
                int dd = opts[oi];
                bool on = host.ChartDays == dd;
                Button rb = new Button
                {
                    Content = T("近 " + dd + " 天", 11.5, on ? Palette.OnAccent : Palette.TextDim),
                    Background = on ? Palette.Accent : Palette.CardHover,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 5)
                };
                rb.Click += delegate { host.SetChartDays(dd); };
                range.Children.Add(rb);
            }
            s.Children.Add(Card(range, new Thickness(0), new Thickness(14, 10)));
            int days = host.ChartDays;   // 7/14/30 可切 ✓（用户要求："看板第二页图表内可以切换日期分布查看图表" ✓✓）
            string[] labels = new string[days];
            // ★★★ **日期桶修复（GUI 复审 MAJOR —— 我上一轮的"修好了"是假的）** ✓✓
            //   ✗ `labels[k]` 只有 5 个字符（MM-dd）✗ → 用 `EndsWith(labels[k])` 比**仍然只比后 5 位** ✗✗
            //     → **跨年还是算错** ✓（2025-09-30 会被算进 2026-09-30 那个桶 ✓ 审计实测复现 ✓）
            //   ✓ 现在：**另存一份完整日期 labelsFull** ✓✓ 比较用完整的 ✓ 显示（坐标轴）仍用 MM-dd ✓
            string[] labelsFull = new string[days];
            long[] counts = new long[days];
            System.DateTime today = System.DateTime.UtcNow.Date;
            for (int i = 0; i < days; i++)
            {
                System.DateTime dday = today.AddDays(i - (days - 1));
                labels[i] = dday.ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                labelsFull[i] = dday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
            long max = 0;
            for (int i = 0; i < d.Rows.Count; i++)
            {
                string created = d.Rows[i].Created;
                if (string.IsNullOrEmpty(created) || created.Length < 10) continue;
                string day = created.Substring(0, 10);
                for (int k = 0; k < days; k++)
                {
                    if (labelsFull[k].Length == 10 && day.Length >= 10 && string.Equals(day.Substring(0, 10), labelsFull[k], StringComparison.Ordinal)) { counts[k]++; if (counts[k] > max) max = counts[k]; }   // MAJOR FIX: full date, not MM-dd
                }
            }
            StackPanel c1 = new StackPanel { Spacing = 8 };
            c1.Children.Add(T("近 " + days + " 天新增会话（按 dsh 记录的创建时间，UTC 日期）", 13, Palette.Text, FontWeight.Bold));
            c1.Children.Add(BarChart(labels, counts, max, Palette.Accent, "个"));
            c1.Children.Add(T("最高 " + max + " 个/天　合计 " + Sum(counts) + " 个（创建时间缺失的会话不计入，不猜）", 11.5, Palette.TextFaint));
            s.Children.Add(Card(c1, new Thickness(0), new Thickness(18, 16)));

            // ② 缓存命中率分布
            long low = 0, mid = 0, high = 0, unknown = 0;
            for (int i = 0; i < d.Rows.Count; i++)
            {
                double h = d.Rows[i].HitPercent;
                if (h < 0) unknown++;
                else if (h < 70) low++;
                else if (h < 90) mid++;
                else high++;
            }
            string[] hl = new string[] { "< 70%", "70–90%", "≥ 90%", "unknown" };
            long[] hc = new long[] { low, mid, high, unknown };
            StackPanel c2 = new StackPanel { Spacing = 8 };
            c2.Children.Add(T("缓存命中率分布（会话数）", 13, Palette.Text, FontWeight.Bold));
            c2.Children.Add(BarChart(hl, hc, Math.Max(Math.Max(low, mid), Math.Max(high, unknown)), Palette.Good, "个"));
            c2.Children.Add(T("命中率越高越省钱；unknown 表示该会话没有这个字段（空会话），我们不会把它算成 0%。", 11.5, Palette.TextFaint));
            s.Children.Add(Card(c2, new Thickness(0), new Thickness(18, 16)));

            // ③ 近 N 天 **token 消耗趋势**（N = 7/14/30 可切）（A 类：会话视角，但看的是"花了多少"而不是"开了几个" ✓）
            long[] dayTok = new long[days];
            long maxTok = 0;
            for (int i = 0; i < d.Rows.Count; i++)
            {
                string cr = d.Rows[i].Created;
                if (string.IsNullOrEmpty(cr) || cr.Length < 10) continue;
                string dy = cr.Substring(0, 10);
                for (int k = 0; k < days; k++)
                {
                    if (labelsFull[k].Length == 10 && dy.Length >= 10 && string.Equals(dy.Substring(0, 10), labelsFull[k], StringComparison.Ordinal)) dayTok[k] += d.Rows[i].In;   // MAJOR FIX
                }
            }
            for (int k = 0; k < days; k++) if (dayTok[k] > maxTok) maxTok = dayTok[k];
            long tokDiv = maxTok >= 1000 ? 1000 : 1;   // F11 FIX: unit follows the data, so a sub-1000 day is not rounded to zero
            long[] dayTokK = new long[days];
            for (int k = 0; k < days; k++) dayTokK[k] = dayTok[k] / tokDiv;
            StackPanel c3 = new StackPanel { Spacing = 8 };
            // N11 FIX: the bar unit follows the data, so the title must too (it said "k token" even
            // when the chart was drawing plain tokens)
            c3.Children.Add(T("近 " + days + " 天 token 消耗（输入侧合计，" + (tokDiv >= 1000 ? "k token" : "token") + "；按 dsh 记录的创建时间归日）", 13, Palette.Text, FontWeight.Bold));
            c3.Children.Add(BarChart(labels, dayTokK, maxTok / tokDiv, Palette.Warn, maxTok >= 1000 ? "k tok" : "tok"));   // F11 FIX: unit follows the data
            c3.Children.Add(T("合计 " + SessionRow.Human(Sum(dayTok)) + " token　最高 " + SessionRow.Human(maxTok) + "/天（创建时间缺失的会话不计入，不猜）", 11.5, Palette.TextFaint));
            s.Children.Add(Card(c3, new Thickness(0), new Thickness(18, 16)));

            // ④ **体检结论分布**（B 类：运维视角 —— 这台机器现在健康吗 ✓）
            DoctorSummary dsum = host.Doctor;
            string[] dl = new string[] { "通过", "提醒", "错误" };
            long[] dv = dsum == null || !dsum.Ok ? new long[] { 0, 0, 0 } : new long[] { dsum.Pass, dsum.Warn, dsum.Error };
            StackPanel c4 = new StackPanel { Spacing = 8 };
            c4.Children.Add(T("体检结论分布（条目数）", 13, Palette.Text, FontWeight.Bold));
            c4.Children.Add(BarChart(dl, dv, Math.Max(Math.Max(dv[0], dv[1]), dv[2]), Palette.Accent, "项"));
            c4.Children.Add(T(dsum == null || !dsum.Ok
                ? "还没有体检结果 —— 进「体检」页会自动跑一次 doctor。这里**不显示 0**，因为没跑过和跑过且全过是两件事（不猜）。"
                : "来自 doctor 的分级条目；错误项在「体检」页可以逐条看到原因。", 11.5, Palette.TextFaint));
            s.Children.Add(Card(c4, new Thickness(0), new Thickness(18, 16)));
            return s;
        }

        private static long Sum(long[] a) { long s = 0; for (int i = 0; i < a.Length; i++) s += a[i]; return s; }

        /// <summary>柱状图：等宽柱子 + 底部标签（纯 Grid/Border，零依赖）。</summary>
        private static Control BarChart(string[] labels, long[] values, long max, IBrush brush, string unit)
        {
            bool allZero = true;
            for (int i = 0; i < values.Length; i++) if (values[i] != 0) { allZero = false; break; }

            if (allZero)
            {
                // ① 空状态 ✓✓（用户"美化一下前端" ✓）：原来是一排 2px 小条 ✗ 看着就是空的 ✓
                //    现在居中说明 ✓ —— 而且**不假造数据** ✓ 与项目诚实原则一致 ✓
                StackPanel es = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                es.Children.Add(T("暂无数据", 13.5, Palette.TextDim, FontWeight.SemiBold));
                // GUI final review: every caller passed a `unit` and this method never read it. Using it in the
                // empty state makes that state say what is missing in the same unit the chart would have used.
                es.Children.Add(T("这个区间里没有可统计的记录（单位：" + unit + "；是没有，不是 0）", 11, Palette.TextFaint));
                return new Border { Height = 132, Child = es };
            }

            Grid outer = new Grid { Height = 132 };
            // ② 水平网格线 ✓（4 条淡线 ✓ 让空白区有结构 ✓ 不再是一片白 ✓）
            Grid glines = new Grid { RowDefinitions = new RowDefinitions("*,*,*,*") };
            for (int k = 0; k < 4; k++)
            {
                Border ln = new Border { Height = 1, Background = Palette.Border, Opacity = 0.45, VerticalAlignment = VerticalAlignment.Top };
                Grid.SetRow(ln, k);
                glines.Children.Add(ln);
            }
            outer.Children.Add(glines);

            Grid g = new Grid();
            for (int i = 0; i < labels.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            for (int i = 0; i < labels.Length; i++)
            {
                long v = values[i];
                double h = 4 + (v * 88.0 / max);
                StackPanel col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 3, Margin = new Thickness(2, 0) };
                col.Children.Add(T(v == 0 ? "" : v.ToString(), 10, Palette.TextDim));
                col.Children.Add(new Border { Height = h, CornerRadius = new CornerRadius(4), Background = v == 0 ? Palette.BarTrack : brush });
                // 列多时（如 30 天）标签会挤在一起 ✗ → 只标每 3 个 ✓（条形本身照画 ✓ 数据不省略 ✓）
                bool showLabel = labels.Length <= 16 || (i % 3) == 0 || i == labels.Length - 1;
                col.Children.Add(T(showLabel ? labels[i] : "", 9.5, Palette.TextFaint));
                Grid.SetColumn(col, i);
                g.Children.Add(col);
            }
            outer.Children.Add(g);
            // ③ 基线 ✓（一条实一点的底线 ✓ 让条形有"落地"感 ✓）
            outer.Children.Add(new Border { Height = 1, Background = Palette.Border, VerticalAlignment = VerticalAlignment.Bottom });
            return outer;
        }
        private static Control OverviewContent(MainWindow host)
        {
            if (host.SubTab == 1) return new Border { Margin = PageMargin, Child = RawCard(host, "原始输出", "status --detail 的标记行原文") };
            return StatusDetail(host);
        }

        /// <summary>看板：指标（KPI + 操作日志）与图表（近 N 天新增会话、命中率分布）。</summary>
        private static Control BoardContent(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = 14 };
            if (host.SubTab == 1) { s.Children.Add(ChartsBody(host)); return s; }
            s.Children.Add(KpiStrip(host));
            s.Children.Add(ChartsBody(host));   // ✗ 原来只在 SubTab==1 → 第一页看不到 ✗（2026-09-30 用户反馈 ✓）
            // 操作日志改为**右下角 toast** ✓✓
            // 操作日志改为**右下角 toast** ✓✓（不再在页面流里占一张卡片 ✓）
            return s;
        }

        private static Control StatusDetail(MainWindow host)
        {
            StatusSnapshot st = host.Status;
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = 14 };
            if (st == null || !st.Ok)
            {
                s.Children.Add(Card(new TextBlock { Text = "读不到状态（CLI 未返回 STATUS_* 标记）。", Foreground = Palette.TextDim, FontSize = 12 }, new Thickness(0), new Thickness(18, 16)));
                return s;
            }

            // —— 状态 hero ——
            // 桌面端在跑 → 用 Good 色 ✓（不是红 ✗ —— 它没坏，只是不走 3080 ✓）
            // F1 FIX (cont): same null dereference as the start/stop row - `_status` is null when the CLI
            bool desktopUp = st.State == 2 && !string.IsNullOrEmpty(st.DesktopClient);   // safe: the st == null case returned above
            IBrush stateBrush = st.State == 0 ? Palette.Good : (desktopUp ? Palette.Good : (st.State == 1 ? Palette.Warn : Palette.Bad));
            Grid hero = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid halo = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center };
            halo.Children.Add(new Ellipse { Width = 44, Height = 44, Fill = Palette.SoftOf(stateBrush) });
            halo.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = stateBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(halo, 0);
            StackPanel ht = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
            ht.Children.Add(T(st.StateText, 26, stateBrush, FontWeight.SemiBold));
            ht.Children.Add(new TextBlock
            {
                Text = "依据只来自可观测事实：本地端口是否监听 + 进程是否存在。dsh 换了形态（例如 headless 没有端口）时，这里会如实显示未运行，而不是假装就绪。",
                Foreground = Palette.TextDim,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(ht, 1);
            hero.Children.Add(halo); hero.Children.Add(ht);
            s.Children.Add(Card(hero, new Thickness(0), new Thickness(18, 16)));

            // —— 运行时事实 ——
            Grid facts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
            // ★★ 字段级刷新（2026-10-04 用户要求 ✓）：这一页的数字都可能变 → 全部走 StatCardLive ✓
            //   取值函数**每拍重算** ✓（读 host.X 的最新快照 ✗ 不是建树那一刻的 st ✗✗ —— 传快照就永远显示旧值 ✓）
            //   桌面端在跑时（端口没监听 ✓ 但 dsh 确实在运行 ✓）→ 显示**桌面端的** PID/启动时间/已运行 ✓✓
            facts.Children.Add(StatCardLive(host, Symbol.NumberSymbol,
                delegate { return DeskUp(host.Status) ? "桌面端 PID" : "进程 PID"; },
                delegate { StatusSnapshot s2 = host.Status; string p2 = DeskUp(s2) ? s2.DesktopPid : s2.Pid; return (string.IsNullOrEmpty(p2) || p2 == "0") ? "—" : p2; },
                delegate { return "运行 dsh 的进程号"; }, Palette.Text, null, 0, 3));
            facts.Children.Add(StatCardLive(host, Symbol.Calendar, delegate { return "启动时间"; },
                delegate { StatusSnapshot s2 = host.Status; string v2 = DeskUp(s2) ? s2.DesktopStart : s2.Start; return string.IsNullOrEmpty(v2) ? "—" : v2; },
                delegate { return "dsh 启动的时刻"; }, Palette.Text, null, 1, 3));
            facts.Children.Add(StatCardLive(host, Symbol.Clock, delegate { return "已运行"; },
                delegate { StatusSnapshot s2 = host.Status; string v2 = DeskUp(s2) ? s2.DesktopUptime : s2.Uptime; return string.IsNullOrEmpty(v2) ? "—" : v2; },
                delegate { return "从启动到现在"; }, Palette.Accent, null, 2, 3));
            s.Children.Add(facts);

            // —— 总览指标（把其它页的要点也摆到这里，省得来回点）——
            ProfilesSnapshot pf = host.Profiles;
            SessionsSnapshot se = host.Data;
            BackupSummary bk = host.Backups;
            DoctorSummary dc = host.Doctor;
            Grid row1 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            row1.Children.Add(StatCardLive(host, Symbol.Box, delegate { return "当前形态"; },
                delegate { return FormText(host.Status, host.Profiles); },
                delegate { return "来自 profile 的 dsh.profile.bundles"; }, Palette.Text, null, 0, 4));
            row1.Children.Add(StatCardLive(host, Symbol.PuzzlePiece, delegate { return "profile / 插件"; },
                delegate { ProfilesSnapshot p2 = host.Profiles; return (p2 == null ? "—" : p2.Count.ToString()) + " / " + ThirdCount(p2); },
                delegate { return BundleCount(host.Profiles) + " 个组合包（含官方）"; }, Palette.Text, null, 1, 4));
            row1.Children.Add(StatCardLive(host, Symbol.ChatMultiple, delegate { return "会话"; },
                delegate { SessionsSnapshot s2 = host.Data; return s2 == null ? "—" : s2.Count.ToString(); },
                delegate { SessionsSnapshot s2 = host.Data; return "非空 " + (s2 == null ? "—" : s2.NonBlank.ToString()) + " · dsh 活跃 " + (s2 == null ? "—" : s2.Live.ToString()); },
                Palette.Text, null, 2, 4));
            row1.Children.Add(StatCardLive(host, Symbol.DataUsage, delegate { return "累计输入 token"; },
                delegate { SessionsSnapshot s2 = host.Data; return s2 == null ? "—" : SessionRow.Human(s2.TotalIn); },
                delegate { SessionsSnapshot s2 = host.Data; return "输出 " + (s2 == null ? "—" : SessionRow.Human(s2.TotalOut)); },
                Palette.Text, null, 3, 4));
            s.Children.Add(row1);

            Grid row2 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,12,Auto") };   // 中间那行是**固定 12px 间隔行** ✓ = 田字隔断 ✓（Avalonia 11.2 的 Grid 没有 RowSpacing ✗）
            Control r2c0 = StatCardLive(host, Symbol.Database, delegate { return "缓存命中率"; },
                delegate { SessionsSnapshot s2 = host.Data; return s2 == null ? "—" : PctText(s2.TotalHitPercent); },
                delegate { return "越高越省钱"; }, Palette.Good,
                delegate { SessionsSnapshot s2 = host.Data; return s2 == null ? -1 : s2.TotalHitPercent; }, 0, 2);
            Grid.SetColumn(r2c0, 0); Grid.SetRow(r2c0, 0); row2.Children.Add(r2c0);
            Control r2c1 = StatCardLive(host, Symbol.Gauge, delegate { return "解码速度"; },
                delegate { SessionsSnapshot s2 = host.Data; return s2 == null ? "—" : TpsText(s2.TotalDecodeTps); },
                delegate { return "tok/s"; }, Palette.Accent, null, 1, 2);
            Grid.SetColumn(r2c1, 1); Grid.SetRow(r2c1, 0); row2.Children.Add(r2c1);
            Control r2c2 = StatCardLive(host, Symbol.Archive, delegate { return "备份"; },
                delegate { BackupSummary b2 = host.Backups; return b2 == null || !b2.Ok ? "—" : b2.Count.ToString(); },
                delegate { return "份（backup-list）"; }, Palette.Text, null, 0, 2);
            Grid.SetColumn(r2c2, 0); Grid.SetRow(r2c2, 2); row2.Children.Add(r2c2);
            // 体检卡**保持普通卡** ✓（它只在用户点「运行体检」后才变 ✓ 而那次本来就会整页重建 ✓
            //   所以没必要做成字段 ✗ —— 顺带保住它按错误/提醒变色的信号 ✓✓）
            Control r2c3 = StatCard(Symbol.Stethoscope, "体检", dc == null || !dc.Ok ? "未运行" : dc.Headline, "点下面按钮运行 doctor", dc != null && dc.Error > 0 ? Palette.Bad : (dc != null && dc.Warn > 0 ? Palette.Warn : Palette.Good), -1, 1, 2); Grid.SetColumn(r2c3, 1); Grid.SetRow(r2c3, 2); row2.Children.Add(r2c3);
            s.Children.Add(row2);

            // ★★ DeepSeek 余额检测（2026-10-02 用户要求："DSH余额检测（自己填写key）…未绑定隐藏，绑定显示充值余额/活动赠送余额" ✓✓）
            //   未绑 key → **整卡隐藏** ✓（用户要求 ✓）；绑了 → 充值 / 活动赠送 ✓
            //   取不到 → 如实写原因 ✗ 绝不冒充数字 ✓✓（key 在设置页填：balance_key ✓）
            if (host.Balance != null && host.Balance.Ok && host.Balance.Bound)
            {
                Dsht.Gui.Avalonia.Markers.BalanceSummary ba = host.Balance;
                StackPanel bcard = new StackPanel { Spacing = 8 };
                StackPanel bh = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                bh.Children.Add(T("DeepSeek 余额", 13, Palette.Text, FontWeight.SemiBold));
                if (ba.Currency.Length > 0 && ba.Currency != "unknown") bh.Children.Add(Chip("币种 " + ba.Currency, Palette.TextFaint, Palette.CardHover));
                bcard.Children.Add(bh);
                if (ba.HasNumbers)
                {
                    Grid bgrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
                    Control bc1 = StatCardLive(host, Symbol.DataUsage, delegate { return "充值余额"; },
                        delegate { BalanceSummary b2 = host.Balance; return b2 != null && b2.Topup.Length > 0 ? b2.Topup : "unknown"; },
                        delegate { return "自己充值的余额"; }, Palette.Accent, null, 0, 2);
                    Grid.SetColumn(bc1, 0); bgrid.Children.Add(bc1);
                    Control bc2 = StatCardLive(host, Symbol.Box, delegate { return "活动赠送余额"; },
                        delegate { BalanceSummary b2 = host.Balance; return b2 != null && b2.Granted.Length > 0 ? b2.Granted : "unknown"; },
                        delegate { return "平台活动赠送的余额"; }, Palette.Good, null, 1, 2);
                    Grid.SetColumn(bc2, 1); bgrid.Children.Add(bc2);
                    bcard.Children.Add(bgrid);
                    bcard.Children.Add(host.LiveText(
                        (ba.Total.Length > 0 && ba.Total != "unknown") ? "总计 " + ba.Total : "",
                        delegate { BalanceSummary b2 = host.Balance; return (b2 != null && b2.Total.Length > 0 && b2.Total != "unknown") ? "总计 " + b2.Total : ""; }));
                }
                else
                {
                    bcard.Children.Add(T("暂未取到余额 ✗ 不猜数字 ✓（" + (ba.Unavailable.Length > 0 ? ba.Unavailable : "原因未知") + "）", 11.5, Palette.Warn));
                }
                if (ba.Note.Length > 0) bcard.Children.Add(T(ba.Note, 11, Palette.Warn));
                s.Children.Add(Card(bcard, new Thickness(0), new Thickness(16, 14)));
            }
            else if (host.Balance != null && host.Balance.Ok && !host.Balance.Bound)
            {
                // ★ 入口提示（2026-10-04 用户在工作电脑上实测："余额配置入口我没找到" ✗✗）
                //   余额卡按用户要求**未绑定就隐藏** ✓ —— 但那样连"去哪儿填 key"都无处可寻 ✗
                //   ✓ 现在：卡仍然隐藏 ✓（余额数字一个不显示 ✓）只给**一行入口** + 一键跳到设置页 ✓✓
                StackPanel bh2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                bh2.Children.Add(T("DeepSeek 余额", 12, Palette.Text, FontWeight.SemiBold));
                bh2.Children.Add(T("未绑定 —— 在「设置」页填 balance_key（你自己的 DeepSeek API key）后，这里就会显示充值余额 / 活动赠送余额", 11.5, Palette.TextDim));
                bh2.Children.Add(GhostButton(T("去设置填写", 11.5, Palette.Accent), delegate { host.SetMainSection(6); }, true));
                s.Children.Add(Card(bh2, new Thickness(0), new Thickness(16, 12)));
            }

            // ★★ 概览自动刷新（2026-10-02 用户要求："快1秒 中3秒 慢5秒 实时0.5秒 暂停和自定义" ✓✓）
            //   只在概览/看板页生效 ✓（设置页的 gui_auto_refresh 也能改 ✓）；0.5 秒档 = 后台连续刷新 ✓
            //   每一拍都要起一个 CLI 进程 ✓ CPU/IO 有真实成本 ✓ —— 如实写在旁边 ✓✓
            {
                StackPanel arBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                arBar.Children.Add(T("自动刷新", 12, Palette.Text, FontWeight.SemiBold));
                string[] arLabels = new string[] { "暂停", "实时 0.5 秒", "快 1 秒", "中 3 秒", "慢 5 秒", "自定义…" };
                string[] arValues = new string[] { "off", "0.5", "1", "3", "5", "" };
                double cur = host.AutoRefreshSeconds;
                int sel = 0;
                if (cur > 0)
                {
                    sel = 5;
                    if (Math.Abs(cur - 0.5) < 0.01) sel = 1;
                    else if (Math.Abs(cur - 1) < 0.01) sel = 2;
                    else if (Math.Abs(cur - 3) < 0.01) sel = 3;
                    else if (Math.Abs(cur - 5) < 0.01) sel = 4;
                }
                ComboBox arCb = new ComboBox { MinWidth = 150, FontSize = 12, ItemsSource = arLabels, SelectedIndex = sel };
                // ★ 先设 SelectedIndex、后挂事件 ✓ —— 初始化那一拍不会触发一次多余的写盘 ✓
                arCb.SelectionChanged += async delegate
                {
                    int idx = arCb.SelectedIndex;
                    if (idx < 0) return;
                    if (idx == 5)
                    {
                        string typed = await PromptDialog.Ask(host, "自定义刷新间隔", "请输入秒数", "0.5 – 3600", "3");
                        if (typed == null || typed.Trim().Length == 0) return;
                        host.SetAutoRefresh(typed.Trim());
                        return;
                    }
                    host.SetAutoRefresh(arValues[idx]);
                };
                arBar.Children.Add(arCb);
                arBar.Children.Add(T("只在概览/看板页生效 ✓ 0.5 秒档 = 后台连续刷新（每一拍都起一个 CLI 进程 ✓ 有真实成本 ✓）", 10.5, Palette.TextFaint));
                s.Children.Add(Card(arBar, new Thickness(0), new Thickness(16, 12)));
            }

            // —— 快捷入口 ——
            Grid jumps = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*") };
            int[] targets = new int[] { 1, 2, 3, 4, 5 };
            for (int i = 0; i < targets.Length; i++)
            {
                Control jc = JumpCard(host, targets[i], i == targets.Length - 1);
                Grid.SetColumn(jc, i);
                jumps.Children.Add(jc);
            }
            s.Children.Add(jumps);

            if (st.Extras.Count > 0)
            {
                StackPanel ex = new StackPanel { Spacing = 6 };
                ex.Children.Add(T("CLI 还报告了这些（未识别的标记原样展示）", 12, Palette.TextDim));
                for (int i = 0; i < st.Extras.Count; i++)
                    ex.Children.Add(Mono(st.Extras[i].Key + "  " + st.Extras[i].Value, 11.5, Palette.Text));
                s.Children.Add(Card(ex, new Thickness(0), new Thickness(16, 14)));
            }
            return s;
        }

        private static Control StatCard(Symbol icon, string label, string value, string sub, IBrush valueBrush, double pct, int col, int cols)
        {
            StackPanel s = new StackPanel();
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Border tile = SoftTile(icon, Palette.Accent, Palette.AccentSoft, 30, 15);
            Grid.SetColumn(tile, 0);
            TextBlock l = new TextBlock { Text = label, FontSize = 11.5, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(l, 1);
            head.Children.Add(tile); head.Children.Add(l);
            s.Children.Add(head);
            s.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = valueBrush, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            s.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Palette.TextFaint, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            if (pct >= 0)
            {
                int level = pct >= 90 ? 3 : (pct >= 70 ? 2 : 1);
                s.Children.Add(new Border { Margin = new Thickness(0, 8, 0, 0), Child = Meter(pct, Palette.HitBrush(level), 4) });
            }
            Border card = Card(s, new Thickness(0, 0, col == cols - 1 ? 0 : 12, 0), new Thickness(16, 14));
            Grid.SetColumn(card, col);
            return card;
        }

        /// <summary>**字段级更新**版的 StatCard ✓（2026-10-04 用户要求："自动刷新…可以只字段刷新吗" ✓✓）。
        /// 与 StatCard 视觉一致 ✓；区别是 label/value/sub/百分比都传**取值函数** ✗ 不是快照 ✗
        /// → 自动刷新那一拍**只重算这几个字段并改文字** ✓ **不重建整页** ✓（滚动/焦点/下拉都保住 ✓）。
        /// 百分比条也会跟着重画 ✓（换一个新的 Meter ✓ 便宜 ✓）。</summary>
        private static Control StatCardLive(MainWindow host, Symbol icon, Func<string> label, Func<string> value, Func<string> sub, IBrush valueBrush, Func<double> pct, int col, int cols)
        {
            StackPanel s = new StackPanel();
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Border tile = SoftTile(icon, Palette.Accent, Palette.AccentSoft, 30, 15);
            Grid.SetColumn(tile, 0);
            TextBlock l = host.LiveText(label(), label);
            l.FontSize = 11.5; l.Foreground = Palette.TextDim; l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(10, 0, 0, 0); l.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(l, 1);
            head.Children.Add(tile); head.Children.Add(l);
            s.Children.Add(head);
            TextBlock v = host.LiveText(value(), value);
            v.FontSize = 22; v.FontWeight = FontWeight.SemiBold; v.Foreground = valueBrush;
            v.Margin = new Thickness(0, 8, 0, 0); v.TextTrimming = TextTrimming.CharacterEllipsis;
            s.Children.Add(v);
            TextBlock sb = host.LiveText(sub(), sub);
            sb.FontSize = 11; sb.Foreground = Palette.TextFaint; sb.Margin = new Thickness(0, 2, 0, 0); sb.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(sb);
            if (pct != null && pct() >= 0)
            {
                Border holder = new Border { Margin = new Thickness(0, 8, 0, 0) };
                s.Children.Add(holder);
                host.Live(delegate
                {
                    double p = pct();
                    if (p < 0) { holder.Child = null; return; }
                    int level = p >= 90 ? 3 : (p >= 70 ? 2 : 1);
                    holder.Child = Meter(p, Palette.HitBrush(level), 4);
                });
            }
            Border card = Card(s, new Thickness(0, 0, col == cols - 1 ? 0 : 12, 0), new Thickness(16, 14));
            Grid.SetColumn(card, col);
            return card;
        }

        // —— 概览页字段的取值辅助 ✓（字段级刷新：每拍重算 ✓ 见 StatCardLive ✓）——
        /// <summary>「端口没监听但官方桌面端在跑」✓（此时 PID/启动时间/已运行 要取桌面端的那一组 ✓）。</summary>
        private static bool DeskUp(StatusSnapshot st) { return st != null && st.Ok && st.State == 2 && !string.IsNullOrEmpty(st.DesktopClient); }

        private static string FormText(StatusSnapshot st, ProfilesSnapshot pf)
        {
            // 桌面端在跑 → 直接说 desktop ✓（比 profile 的 web 形态更贴近"现在启动的是哪个" ✓）
            if (DeskUp(st)) return "desktop（官方桌面端）";
            if (pf != null && pf.Ok && pf.Profiles.Count > 0) return pf.Profiles[0].FormText;
            return "—";
        }

        private static int BundleCount(ProfilesSnapshot pf)
        {
            int n = 0;
            if (pf != null && pf.Ok) for (int i = 0; i < pf.Profiles.Count; i++) n += pf.Profiles[i].Bundles;
            return n;
        }

        private static int ThirdCount(ProfilesSnapshot pf)
        {
            int n = 0;
            if (pf != null && pf.Ok) for (int i = 0; i < pf.Profiles.Count; i++) n += pf.Profiles[i].ThirdParty;
            return n;
        }

        private static Control JumpCard(MainWindow host, int section, bool last)
        {
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Border tile = SoftTile(MainWindow.NavIcons[section], Palette.Accent, Palette.AccentSoft, 32, 16);
            Grid.SetColumn(tile, 0);
            TextBlock label = T(MainWindow.NavItems[section], 13, Palette.Text, FontWeight.SemiBold);
            label.Margin = new Thickness(10, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            SymbolIcon chev = Ic(Symbol.ChevronRight, 13, Palette.TextFaint);
            chev.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chev, 2);
            row.Children.Add(tile); row.Children.Add(label); row.Children.Add(chev);

            Button b = new Button
            {
                Content = row,
                Background = Palette.CardBg,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                // ★★★ **用户反馈（2026-09-30）**：「概览里这五个按钮宽度不一样」✓✓
                //   ✗ 外层 Grid 已经是 `*,*,*,*,*`（**列等宽** ✓）✗
                //     但**卡片自身没撑满列** ✗ → 它按内容宽度缩着 ✓
                //     → 「看板」(2 字) 窄 ✓「会话与 Token」(6 字) 宽 ✓✓ **完全解释通了** ✓
                //   ✓ 现在：**卡片和它外层的 Border 都显式 Stretch** ✓✓ → 五张卡**严格等宽** ✓
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Hover(b, Palette.CardBg, Palette.CardHover);
            b.Click += delegate { host.SetMainSection(section); };
            Border wrap = new Border
            {
                Margin = new Thickness(0, 0, last ? 0 : 12, 0),
                CornerRadius = new CornerRadius(12),
                Child = b,
                HorizontalAlignment = HorizontalAlignment.Stretch   // ✓ 撑满所在列 ✓✓
            };
            if (Palette.CardShadow.Length > 0) wrap.BoxShadow = BoxShadows.Parse(Palette.CardShadow);
            return wrap;
        }

        // ================================================================ 形态与插件

        private static Control ProfilesContent(MainWindow host)
        {
            ProfilesSnapshot d = host.Profiles;
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = 12 };
            if (d == null || !d.Ok)
            {
                s.Children.Add(Card(new TextBlock
                {
                    Text = d == null ? "正在读取…" : (string.IsNullOrEmpty(d.FailReason) ? "没有可显示的 profile 信息。下一步：先安装 dsh（侧栏「安装」按钮），装完点「刷新」。" : d.FailReason + " —— 下一步：确认 dsh 已安装，或点「刷新」重试。"),
                    Foreground = Palette.TextDim,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }, new Thickness(0), new Thickness(18, 16)));
                return s;
            }
            int totalBundles = 0; int totalThird = 0;
            for (int i = 0; i < d.Profiles.Count; i++) { totalBundles += d.Profiles[i].Bundles; totalThird += d.Profiles[i].ThirdParty; }
            s.Children.Add(new TextBlock
            {
                Text = d.Count + " 个 profile · " + totalBundles + " 个组合包 · 其中第三方插件 " + totalThird + " 个（数据来自各 profile 的 package.json 里 dsh.profile.bundles）",
                Foreground = Palette.TextDim, FontSize = 12, TextWrapping = TextWrapping.Wrap
            });

            // ★★★ **用户要求（2026-09-30）**：「安装桥接插件有按钮吗」✓✓ → 这里补上 ✓
            //   **它是什么**：把 dsh 的会话/token 状态写成一份**只读快照** ✓
            //     装了 → 工具箱能显示「**运行中**」✓ 这是**磁盘投影给不了的事实** ✓
            //             （运行态是 dsh 进程内的 ✓ 不落盘 ✓）
            //     不装 → 那一格显示 unknown ✓ **其余功能一点都不缺** ✓✓
            //   **为什么值得装**：这是**唯一**需要插件才能拿到的事实 ✓ 也是插件存在的唯一理由 ✓
            //   **诚实边界**：装不装**由用户决定** ✓ 本工具不替他决定 ✓ 也不假装它必需 ✓
            //   **实测过的坑**（写进按钮下方说明 ✓ 用户会踩 ✓）：
            //     · 需要 pnpm ✗ 而 dsh **不会**替你装 ✓
            //     · 装上了但 patch 缺行 → **不会加载且不报错** ✗ → 命令会**如实报** ✓✓
            // ★ 2026-10-02 用户要求："形态与插件页的安装教程可以放到最底部" ✓✓
            //   → 先把这两张卡（按钮 + 教程）装进一个**捕获型委托**，等列表渲染完再调用 ✓
            System.Action addBridgeCards = delegate
            {
                StackPanel bc = new StackPanel { Spacing = 8 };
                bc.Children.Add(T("可选的桥接插件", 13, Palette.Text, FontWeight.SemiBold));
                bc.Children.Add(T("装了它，工具箱才能显示「运行中」—— 运行态是 dsh 进程内的事实，磁盘投影给不了。不装也能用，那一格显示 unknown。插件只读、不联网、不发模型请求、不改 dsh 状态。", 11.5, Palette.TextDim));
                bool armed = host.PendingDelete == "bridge";
                Button ib = GhostButton(
                    T(armed ? "再点一次确认安装" : "安装桥接插件", 12, armed ? Brushes.White : Palette.Accent),
                    delegate
                    {
                        if (host.PendingDelete != "bridge") { host.PendingDelete = "bridge"; host.Rebuild(); return; }
                        host.PendingDelete = "";
                        host.InstallBridge();
                    },
                    true);
                if (armed) ib.Background = Palette.Accent;
                bc.Children.Add(ib);
                bc.Children.Add(T("需要 dsh 与 pnpm（dsh 不会替你装 pnpm，先 npm i -g pnpm）。装完重启 dsh 生效。结果如实显示，包括「包已 link 但 patch 缺行 → 不会加载」这种情形。", 11, Palette.TextFaint));

                // ★★★ 用户要求（2026-10-01）：「在 GUI 内写出安装教程」+「A+B 自选」✓✓
                //   背景（真机实测）：`desktop` profile **由 dsh 桌面端独占管理** ✗ → 命令行装不进去 ✓
                //     用户看到的是 dsh 的英文报错 ✗ 而桌面端有自己的「添加插件」对话框 ✓
                //   实测结论（我逐个跑过 pnpm add ✓）：
                //     ✓ 本地目录路径 → 可以 ✓
                //     ✓ git 仓库 + 子目录（#path:）→ 可以 ✓
                //     ✗ 只填仓库地址 → **不行** ✗（仓库根没有 package.json ✓）
                //     ✗ npm 包名 → 不行 ✗（未发布到 npm ✓ 404 ✓）
                //   诚实边界：桌面端**由它自己管理** ✓ 本工具只能**给路径** ✓ 不能代装 ✓
                //     装完后 **patch 行它不会替你加** ✗ → 用下面「检查」按钮核对 ✓
                string selfPlugin = "";
                try
                {
                    string baseDir = System.AppContext.BaseDirectory;
                    if (string.IsNullOrEmpty(baseDir)) baseDir = System.IO.Directory.GetCurrentDirectory();
                    selfPlugin = System.IO.Path.Combine(System.IO.Path.Combine(baseDir, "plugin"), "dsh-minato-bridge");
                }
                catch { }
                StackPanel tut = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
                tut.Children.Add(T("怎么装进 desktop（由桌面端自己管理，本工具只能给你路径）", 12, Palette.Text, FontWeight.SemiBold));
                tut.Children.Add(T("在桌面端的「添加插件」对话框里，下面两条**任选一条**粘进去（它接受 npm 包名 / GitHub 地址 / 本地目录路径）：", 11.5, Palette.TextDim));
                tut.Children.Add(T("A · 仓库地址（**直接填这个就行** ✓ 仓库根已有 package.json 并声明了 dsh.bundle）", 11, Palette.TextFaint));
                // ★ 更新（2026-10-02）：原来这里写"只填仓库根不行" ✗ —— 那在当时是事实 ✓
                //   但用户要"直接填仓库地址" ✓ → 我在仓库根加了 package.json（声明 dsh.bundle ✓ 指向子目录 ✓）
                //   → **现在仓库地址可以直接装** ✓✓（实测 pnpm add 退出码 0 ✓ 包内 dsh.bundle ✓ patch 文件在 ✓）
                tut.Children.Add(T("❌ 若在旧版本（根目录还没有 package.json 的提交）上填仓库地址，会被拒绝并报「这个包没有声明组合包」—— 更新到最新提交后即可。", 10.5, Palette.TextFaint));
                try
                {
                    SelectableTextBlock ga = new SelectableTextBlock
                    {
                        Text = "https://github.com/sakanamaru/dsh-minato",
                        FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Accent
                    };
                    tut.Children.Add(ga);
                    SelectableTextBlock ga2 = new SelectableTextBlock
                    {
                        Text = "github:sakanamaru/dsh-minato#path:plugin/dsh-minato-bridge",
                        FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Palette.TextDim
                    };
                    tut.Children.Add(T("（等价的显式子目录形式，旧提交上也能用）", 10.5, Palette.TextFaint));
                    tut.Children.Add(ga2);
                }
                catch { tut.Children.Add(T("github:sakanamaru/dsh-minato#path:plugin/dsh-minato-bridge", 11.5, Palette.Accent)); }
                tut.Children.Add(T("B · 本地目录（不依赖网络，更稳）", 11, Palette.TextFaint));
                try
                {
                    SelectableTextBlock gb = new SelectableTextBlock
                    {
                        Text = selfPlugin.Length > 0 ? selfPlugin : "(随包分发的 plugin/dsh-minato-bridge 目录)",
                        FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Accent
                    };
                    tut.Children.Add(gb);
                    // ★ 诚实说明（开发构建 vs 发布包 ✓）：发布包里 plugin/ 就在 exe 旁边 ✓
                    //   而**开发构建**（bin/Release/net8.0）里没有它 ✗ → 路径会指向一个不存在的地方 ✓
                    bool exists = false;
                    try { exists = selfPlugin.Length > 0 && System.IO.Directory.Exists(selfPlugin); } catch { }
                    if (!exists) tut.Children.Add(T("（上面这条路径在**开发构建**里不存在 —— 发布包里 plugin/ 就在 exe 旁边 ✓ 以发布包为准；源码运行时请用仓库里的 plugin/dsh-minato-bridge ✓）", 10.5, Palette.TextFaint));
                }
                catch { tut.Children.Add(T(selfPlugin, 11.5, Palette.Accent)); }
                tut.Children.Add(T("⚠ 装完还要看一步：桌面端**不会**替你往 desktop profile 的 cordis.patch.yml 里加 shio-bridge 行，缺了插件**不会加载**而 dsh **不报错**。装完告诉我（或看工具箱的输出），我帮你核对并补上。", 11, Palette.TextFaint));
                s.Children.Add(Card(tut, new Thickness(0), new Thickness(16, 12)));
                s.Children.Add(Card(bc, new Thickness(0), new Thickness(16, 14)));
            };

            // —— 工具行：过滤 + 搜索（搜索框就地刷新下面的卡片列表，不重建整页，避免输入框丢焦点）——
            Grid tools = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Control chips = Segmented(new string[] { "全部", "只看第三方", "只看官方" }, host.ProfilesFilter, delegate(int i) { host.SetProfilesFilter(i); });
            Grid.SetColumn(chips, 0);
            TextBox search = new TextBox
            {
                Watermark = "搜索 profile 或插件 id…",
                Width = 260,
                Text = host.ProfileSearch,
                Background = Palette.InsetBg,
                BorderBrush = Palette.Border,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6),
                FontSize = 12.5,
                Foreground = Palette.Text
            };
            Grid.SetColumn(search, 2);
            tools.Children.Add(chips); tools.Children.Add(search);
            s.Children.Add(tools);

            s.Children.Add(HealthCard(host));

            StackPanel cardHost = new StackPanel { Spacing = 12 };
            search.TextChanged += delegate
            {
                host.ProfileSearch = search.Text == null ? "" : search.Text;
                RebuildProfileCards(cardHost, host);
            };
            RebuildProfileCards(cardHost, host);
            s.Children.Add(cardHost);

            s.Children.Add(Card(new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    T("这一页怎么读？", 13.5, Palette.Text, FontWeight.SemiBold),
                    Line("• 形态来自每个 profile 的 package.json 里 dsh.profile.bundles：启用 dsh-web-app = Web，dsh-headless = Headless（没有端口），dsh-acp-app = ACP。"),
                    Line("• 这是**配置形态**，不是运行形态 —— 「dsh 在跑」仍然由端口/进程等运行时事实判断（状态页看）。"),
                    Line("• 官方 = @deepseek-ai/* 的组合包；第三方 = 你自己加的插件（例如 example-search-plugin）。")
                }
            }, new Thickness(0), new Thickness(18, 16)));
            // ★ 教程置底（2026-10-02 用户要求 ✓）：列表与说明之后才出现桥接插件卡片 + 安装教程 ✓✓
            addBridgeCards();
            // 操作日志改为**右下角 toast** ✓✓（不再在页面流里占一张卡片 ✓）
            return s;
        }

        private static Control HealthCard(MainWindow host)
        {
            StackPanel s = new StackPanel { Spacing = 10 };
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Border tile = SoftTile(Symbol.Stethoscope, Palette.Accent, Palette.AccentSoft, 34, 17);
            Grid.SetColumn(tile, 0);
            StackPanel tt = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            tt.Children.Add(T("健康检查 · profilecheck", 13.5, Palette.Text, FontWeight.SemiBold));
            tt.Children.Add(T("检查 profile 的语法 / 重复 id / 缺字段，并给出处方", 11.5, Palette.TextFaint));
            Grid.SetColumn(tt, 1);
            Button run = PrimaryButton("运行检查", delegate { host.LoadHealth(); });
            run.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(run, 2);
            head.Children.Add(tile); head.Children.Add(tt); head.Children.Add(run);
            s.Children.Add(head);
            if (!string.IsNullOrEmpty(host.Health))
            {
                s.Children.Add(new Border
                {
                    Background = Palette.InsetBg,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10),
                    MaxHeight = 320,
                    Child = new ScrollViewer
                    {
                        Content = new TextBlock { Text = host.Health, FontFamily = MonoFont, FontSize = 11.5, Foreground = Palette.TextDim, TextWrapping = TextWrapping.Wrap }
                    }
                });
            }
            return Card(s, new Thickness(0), new Thickness(18, 16));
        }

        /// <summary>按过滤 + 搜索条件重画 profile 卡片（只换列表，不动整页）。</summary>
        private static void RebuildProfileCards(StackPanel cardHost, MainWindow host)
        {
            cardHost.Children.Clear();
            ProfilesSnapshot d = host.Profiles;
            if (d == null || !d.Ok) return;
            string q = (host.ProfileSearch ?? "").Trim();
            int shown = 0;
            for (int i = 0; i < d.Profiles.Count; i++)
            {
                ProfileCard p = d.Profiles[i];
                bool nameHit = q.Length > 0 && p.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                List<BundleItem> items = MatchBundles(p, host.ProfilesFilter, nameHit ? "" : q);
                if (host.ProfilesFilter == 1 && p.ThirdParty == 0) continue;
                if (q.Length > 0 && !nameHit && items.Count == 0) continue;
                cardHost.Children.Add(ProfileCard(host, p, items, q));
                shown++;
            }
            if (shown == 0)
            {
                cardHost.Children.Add(Card(T("没有匹配的 profile —— 换个关键词或过滤条件试试。", 12, Palette.TextDim), new Thickness(0), new Thickness(18, 16)));
            }
        }

        /// <summary>过滤 bundle 列表：1=只看第三方 2=只看官方；搜索词只保留 id 命中的条目。</summary>
        private static List<BundleItem> MatchBundles(ProfileCard p, int filter, string q)
        {
            List<BundleItem> r = new List<BundleItem>();
            for (int i = 0; i < p.Items.Count; i++)
            {
                BundleItem it = p.Items[i];
                if (filter == 1 && it.Official) continue;
                if (filter == 2 && !it.Official) continue;
                if (q.Length > 0 && it.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                r.Add(it);
            }
            return r;
        }

        private static Control ProfileCard(MainWindow host, ProfileCard p, List<BundleItem> items, string q)
        {
            StackPanel card = new StackPanel { Spacing = 10 };

            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            head.Children.Add(new TextBlock { Text = p.Name, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, VerticalAlignment = VerticalAlignment.Center });
            IBrush fb = Palette.FormBrush(p.FormKind);
            head.Children.Add(Chip(p.FormText, Palette.OnAccent, fb));
            TextBlock cnt = T(p.CountText, 12, Palette.TextDim);
            cnt.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(cnt);
            card.Children.Add(head);

            if (p.Disabled.Count > 0)
            {
                StackPanel dis = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                SymbolIcon wic = Ic(Symbol.Warning, 13, Palette.Warn);
                wic.VerticalAlignment = VerticalAlignment.Center;
                dis.Children.Add(wic);
                TextBlock dt = T(p.DisabledText, 12, Palette.Warn);
                dt.VerticalAlignment = VerticalAlignment.Center;
                dis.Children.Add(dt);
                card.Children.Add(dis);
            }

            if (items.Count == 0)
            {
                card.Children.Add(T("该条件下没有可显示的条目。", 11.5, Palette.TextFaint));
            }
            for (int b = 0; b < items.Count; b++)
            {
                BundleItem it = items[b];
                bool disabled = p.Disabled.Contains(it.Id);
                Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto") };
                Border kind = Chip(it.KindText, it.Official ? Palette.Accent : Palette.Warn, it.Official ? Palette.AccentSoft : Palette.WarnSoft);
                Grid.SetColumn(kind, 0);
                StackPanel idv = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                idv.Children.Add(Mono(it.Id, 12, disabled ? Palette.TextFaint : Palette.Text));
                if (it.VersionText.Length > 0) idv.Children.Add(T(it.VersionText, 11, Palette.TextFaint));
                if (disabled) idv.Children.Add(Chip("已隔离", Palette.Warn, Palette.WarnSoft));
                Grid.SetColumn(idv, 1);
                row.Children.Add(kind); row.Children.Add(idv);

                string folder = host.BundleFolder(p.Name, it.Id);
                if (folder.Length > 0)
                {
                    Button open = GhostButton(Ic(Symbol.FolderOpen, 13, Palette.TextDim), delegate { host.OpenFolder(folder); }, false);
                    open.Padding = new Thickness(6, 3);
                    ToolTip.SetTip(open, "在文件管理器中打开插件目录");
                    Grid.SetColumn(open, 2);
                    row.Children.Add(open);
                }

                string key = p.Name + "|" + it.Id;
                Button act = disabled
                    ? ConfirmButton(key, "恢复", "再点一次确认恢复", host, delegate { host.PatchEntry(p.Name, it.Id, false); host.Refresh(); })
                    : ConfirmButton(key, "隔离", "再点一次确认隔离", host, delegate { host.PatchEntry(p.Name, it.Id, true); host.Refresh(); });
                act.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(act, 3);
                row.Children.Add(act);
                card.Children.Add(row);
            }

            // DISABLED 里存在、但 bundle 清单里没有的 id（例如条目已被删除但补丁还在）—— 也要能恢复
            for (int k = 0; k < p.Disabled.Count; k++)
            {
                string id = p.Disabled[k];
                bool listed = false;
                for (int b = 0; b < p.Items.Count; b++) if (p.Items[b].Id == id) { listed = true; break; }
                if (listed) continue;
                if (q.Length > 0 && id.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(Chip("已隔离", Palette.Warn, Palette.WarnSoft));
                TextBlock idt = Mono(id, 12, Palette.TextFaint);
                idt.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(idt);
                string rkey = p.Name + "|" + id;
                row.Children.Add(ConfirmButton(rkey, "恢复", "再点一次确认恢复", host, delegate { host.PatchEntry(p.Name, id, false); host.Refresh(); }));
                card.Children.Add(row);
            }

            return Card(card, new Thickness(0), new Thickness(18, 16));
        }

        // ================================================================ 说明区

        private static Control Explain()
        {
            StackPanel s = new StackPanel { Spacing = 8 };
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            SymbolIcon ic = Ic(Symbol.Info, 15, Palette.TextDim);
            ic.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ic, 0);
            TextBlock t = T("这些数字怎么读？", 13.5, Palette.Text, FontWeight.SemiBold);
            t.Margin = new Thickness(8, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            head.Children.Add(ic); head.Children.Add(t);
            s.Children.Add(head);
            s.Children.Add(Line("• 缓存命中率 = 命中缓存的输入 token ÷ 全部输入 token。命中缓存的部分计费更低，所以这个数字越高越省钱；低于 70% 会标成琥珀/红色。"));
            s.Children.Add(Line("• 解码速度 = dsh 投影里的 decodeTokens ÷ decodeMs，即模型生成 token 的速率（tok/s）。它反映生成快慢，不含排队与工具耗时。"));
            s.Children.Add(Line("• 首 token = dsh 投影里的 ttftMs 原值（多步累计），超过 1 秒按秒显示；它是等待第一个字输出的累计时间。"));
            s.Children.Add(Line("• 上下文压力 = 已占用上下文 ÷ 模型窗口。越接近 100% 越可能触发压缩，80% 以上标红提醒。"));
            s.Children.Add(Line("• 输入 token 的条形是相对最长的那条会话画的，用来横向对比，不是绝对刻度。"));
            s.Children.Add(Line("• 显示 unknown 表示 dsh 投影里没有这个字段（例如空会话没有命中率）—— 我们不会用 0 冒充它。"));
            return Card(s, new Thickness(0), new Thickness(18, 16));
        }

        /// <summary>日志行着色（与虚拟化前的启发式逐字一致 ✓）：ERROR 红 / WARN 橙 / INFO 淡 / 其它正文色。</summary>
        private static IBrush LogLineBrush(string l)
        {
            if (l.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0) return Palette.Bad;
            if (l.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0) return Palette.Warn;
            if (l.IndexOf("INFO", StringComparison.OrdinalIgnoreCase) >= 0) return Palette.TextDim;
            return Palette.Text;
        }

        private static Control Line(string text)
        {
            return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Palette.TextDim, FontSize = 12 };
        }

        private static string PctText(double v)
        {
            return v < 0 ? "unknown" : v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        private static string TpsText(double v)
        {
            return v < 0 ? "unknown" : v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
