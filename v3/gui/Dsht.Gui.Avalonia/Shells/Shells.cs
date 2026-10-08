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
    public static partial class Shells
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

        /// <summary>统一下拉样式（用户反馈"下拉框突兀"）：纤细、贴卡片语言 —— 细边框 + 内陷底 + 小圆角。</summary>
        private static ComboBox SlimCombo(System.Collections.IEnumerable items, int selectedIndex, double minWidth)
        {
            return new ComboBox
            {
                ItemsSource = items,
                SelectedIndex = selectedIndex,
                MinWidth = minWidth,
                Height = 30,   // 固定高 = 工具行按钮同高（用户两轮截图：MinHeight 会被内容/边距撑出 1-3px 差 ✗ 固定才严丝合缝）
                FontSize = 11.5,
                Padding = new Thickness(8, 2),
                Background = Palette.InsetBg,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>统一空状态（美学B5）：图标 + 主句 + 下一步 —— 各页不再手写样式漂移 ✓。</summary>
        private static Control EmptyState(string main, string next)
        {
            StackPanel es = new StackPanel { Spacing = 6 };
            SymbolIcon ic = Ic(Symbol.Info, 16, Palette.TextFaint);
            es.Children.Add(ic);
            es.Children.Add(T(main, 13, Palette.TextDim, FontWeight.SemiBold));
            if (!string.IsNullOrEmpty(next)) es.Children.Add(T(next, 11.5, Palette.TextFaint));
            return Card(es, new Thickness(0), new Thickness(18, 18));
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

        /// <summary>概览/看板的「自动刷新」控制（提案A：收进页眉行右侧的细条，不占内容流一整行 ✓）。</summary>

        private static Control PageHeader(MainWindow host) { return PageHeader(host, true); }

        private static Control PageHeader(MainWindow host, bool withMargin)
        {
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            StackPanel s = new StackPanel { Spacing = 4 };
            if (withMargin) s.Margin = new Thickness(24, 18, 24, 12);
            s.Children.Add(T(host.PageTitle, 20, Palette.Text, FontWeight.SemiBold));
            s.Children.Add(new TextBlock { Text = StripMd(host.SubtitleText), Foreground = Palette.TextDim, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            g.Children.Add(s);
            // 提案A：概览/看板页的「自动刷新」收进页眉行右侧 ✓（内容流不再被它占一整行 ✓）
            if (host.IsOverviewLike && host.SubTab == 0)
            {
                Control ar = AutoRefreshBar(host);
                if (ar != null) { Grid.SetColumn(ar, 1); g.Children.Add(ar); }   // ★ 必须设列 ✗ 否则两块同叠列 0（截图抓到标题被遮）
            }
            return g;
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
            /// <summary>看板第一批（2026-10-08 ✓✓）：窗口档生效时 = 7/14/30（KPI 卡标签「（近 N 天）」✓）；0 = 未按窗口过滤 ✓。</summary>
            public int WinDays;
        }


        /// <summary>KPI 卡的视图后缀 ✓（整体视图 = 空串 ✓）。</summary>
        private static string KpiTag(MainWindow host)
        {
            KpiAgg a = Agg(host);
            if (a.Filtered) return host.SubTab == 2 ? "（子代理）" : "（父会话）";
            if (a.WinDays > 0) return "（近 " + a.WinDays + " 天）";   // 看板第一批 ✓ 窗口档标签（truth=0 口径见筛选条 C.4 脚注 ✓）
            return "";
        }



        /// <summary>**字段级更新**版的 KpiCard ✓（同 StatCardLive 的理由 ✓ 2026-10-04 ✓）。</summary>

        /// <summary>图例小点（美学A4）：色点 + 文案，会话页工具行用。</summary>


        /// <summary>按当前风格选会话列表形态：C=紧凑行（密度优先），D=大条形卡片，其余=标准卡片。</summary>


        /// <summary>标准会话卡片：状态点 + 标题/元信息 + 三个指标块（标签+数值+比例条）。</summary>


        /// <summary>C · 深色紧凑：单行会话（发卡线分隔，信息密度优先，无条形）。</summary>

        /// <summary>D · 浅色仪表盘：把可视化放大 —— 标题行 + 三行大比例条。</summary>



        // ================================================================ 主从式明细





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
            sum.Children.Add(T("新输入 " + SessionRow.Human(d.TotalIn - d.TotalCacheRead < 0 ? 0 : d.TotalIn - d.TotalCacheRead) + " · 输出 " + SessionRow.Human(d.TotalOut) + " · 缓存命中 " + SessionRow.Human(d.TotalCacheRead) + " · 会话 " + d.Count + " 个（非空 " + d.NonBlank + "）", 11.5, Palette.TextFaint));
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
        /// <summary>备份页：清单（名称/类型/大小/时间）+ 立即备份 / 导出 / 恢复预览 / 应用恢复 / 删除（两次确认）。</summary>

        /// <summary>设置页：逐键编辑（config-get / config-set），开关型给两个按钮，只读键禁编辑。</summary>

        /// <summary>从 CLI 的 `CONFIG update_channel <v>` 读当前更新通道 ✓（读不到就说"读不到" ✓ 不猜 ✗）。</summary>
        /// <summary>更新中心页 ✓✓（用户要求："放到左侧菜单更新内，其中检查 webui/desktop/dsh minato/已经安装的插件的更新列表和版本，如果能获取更新日志那最好了" ✓）

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


        /// <summary>更新中心的一条 ✓（从 CLI 的 UPDATECENTER_* 行解析 ✓）。</summary>

        /// <summary>解析 `UPDATECENTER_*` 行 ✓（纯字符串 ✓ 不引 JSON ✓ 保持零依赖 ✓）。</summary>

        // —— 设置页的分组渲染辅助（2026-10-02 用户要求"分类 + 选择框" ✓✓）——

        /// <summary>按 keys 的顺序从 items 里挑出该组的配置项（挑过的记进 placed ✓ 不重复 ✓）。</summary>

        /// <summary>没被任何组挑走的 → 原样显示在「其它」（如只读 dsh_versions ✓）。</summary>


        /// <summary>枚举型配置的**合法取值**（做成选择框 ✗ 不再手敲 ✗）。返回 null = 不是枚举（走文本框/开关）✓。

        /// <summary>2026-10-06 审查 U1：这几个键在 V3（跨平台 GUI + CLI）里**没有消费方** ✗

        /// <summary>单个配置项的卡片（key + 人话说明 + CONFIGNOTE 备注 + 编辑控件 ✓）。</summary>

        /// <summary>看板图表：近 N 天新增会话（柱状，N = 7/14/30 可切）+ 缓存命中率分布（柱状）。


        /// <summary>柱状图：等宽柱子 + 底部标签（纯 Grid/Border，零依赖）。</summary>

        /// <summary>看板：指标（KPI + 操作日志）与图表（近 N 天新增会话、命中率分布）。</summary>


        /// <summary>**字段级更新**版的 StatCard ✓（2026-10-04 用户要求："自动刷新…可以只字段刷新吗" ✓✓）。

        // —— 概览页字段的取值辅助 ✓（字段级刷新：每拍重算 ✓ 见 StatCardLive ✓）——
        /// <summary>「端口没监听但官方桌面端在跑」✓（此时 PID/启动时间/已运行 要取桌面端的那一组 ✓）。</summary>




        // ================================================================ 形态与插件



        /// <summary>按过滤 + 搜索条件重画 profile 卡片（只换列表，不动整页）。</summary>

        /// <summary>过滤 bundle 列表：1=只看第三方 2=只看官方；搜索词只保留 id 命中的条目。</summary>


        // ================================================================ 说明区


        /// <summary>日志行着色（与虚拟化前的启发式逐字一致 ✓）：ERROR 红 / WARN 橙 / INFO 淡 / 其它正文色。</summary>



    }
}
