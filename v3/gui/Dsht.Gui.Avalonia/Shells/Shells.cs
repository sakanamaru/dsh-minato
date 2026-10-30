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
        private static readonly Thickness PageMargin = ShapeTokens.PagePad;   // 样式重构：页面边距走 token（24,16,24,16）✓

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

        /// <summary>统一卡片：RCard 圆角 + 发丝描边 + 浅色风格的极浅投影（深色靠描边分层）。</summary>
        private static Border Card(Control child, Thickness margin, Thickness padding)
        {
            Border b = new Border
            {
                Background = Palette.CardBg,
                CornerRadius = new CornerRadius(ShapeTokens.RCard),
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(ShapeTokens.Stroke),
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
                Height = ShapeTokens.CtrlH,   // 固定高 = 工具行按钮同高（用户两轮截图：MinHeight 会被内容/边距撑出 1-3px 差 ✗ 固定才严丝合缝）
                FontSize = ShapeTokens.FsBody,
                Padding = new Thickness(ShapeTokens.S8, 2),
                Background = Palette.InsetBg,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(ShapeTokens.Stroke),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>统一空状态（美学B5）：图标 + 主句 + 下一步 —— 各页不再手写样式漂移 ✓。</summary>
        private static Control EmptyState(string main, string next)
        {
            StackPanel es = new StackPanel { Spacing = ShapeTokens.S8 };
            SymbolIcon ic = Ic(Symbol.Info, ShapeTokens.IcNav, Palette.TextFaint);
            es.Children.Add(ic);
            es.Children.Add(T(main, ShapeTokens.FsEm, Palette.TextDim, FontWeight.SemiBold));
            if (!string.IsNullOrEmpty(next)) es.Children.Add(T(next, ShapeTokens.FsBody, Palette.TextFaint));
            return Card(es, new Thickness(0), ShapeTokens.CardPad);
        }

        private static Border Chip(string text, IBrush fg, IBrush bg)
        {
            return new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(ShapeTokens.RPill),
                Padding = new Thickness(ShapeTokens.S8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = T(text, ShapeTokens.FsFoot, fg, FontWeight.SemiBold)
            };
        }

        /// <summary>圆角小方块里的图标（KPI / 卡片头部用）。</summary>
        private static Border SoftTile(Symbol icon, IBrush fg, IBrush bg, double size, double iconSize)
        {
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),   // 样式重构：9 → RCtrl 统一控件档
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
                BorderThickness = new Thickness(bordered ? ShapeTokens.Stroke : 0),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(ShapeTokens.S12, 7)   // 竖 7 是有意的：撑满 30px 与下拉/工具行同高 ✗ 勿改 8
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
                Content = T(text, ShapeTokens.FsEm, Palette.OnAccent, FontWeight.SemiBold),
                Background = Palette.Accent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(ShapeTokens.S12, 7)   // 与 GhostButton 同 padding：两种按钮并排严格同高 ✓
            };
            Hover(b, Palette.Accent, Palette.AccentHover);
            b.Click += delegate { act(); };
            return b;
        }

        /// <summary>segmented 切换器：凹陷底 + 选中项浮起为卡片色。</summary>
        private static Control Segmented(string[] items, int active, Action<int> pick) { return Segmented(items, active, pick, null, null); }

        /// <summary>分段 chips。看板体验修复（2026-10-11 ✓✓）：可选 `liveHost/liveActive` ——
        /// 给了就注册字段刷新：高亮跟着**取值函数**走 ✓ 切换不再依赖整页重建 ✓（取值函数 ✗ 不是快照 ✗）。</summary>
        private static Control Segmented(string[] items, int active, Action<int> pick, MainWindow liveHost, Func<int> liveActive)
        {
            StackPanel inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            for (int i = 0; i < items.Length; i++)
            {
                int idx = i;
                bool act = idx == active;
                Button b = new Button
                {
                    Content = T(items[i], ShapeTokens.FsBody, act ? Palette.Text : Palette.TextDim, act ? FontWeight.SemiBold : FontWeight.Normal),
                    Background = act ? Palette.CardBg : Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                    Padding = new Thickness(12, 5)
                };
                b.Click += delegate { pick(idx); };
                if (liveHost != null && liveActive != null)
                {
                    TextBlock tc = (TextBlock)b.Content;
                    liveHost.Live(delegate
                    {
                        bool on2 = liveActive() == idx;
                        IBrush bg = on2 ? Palette.CardBg : Brushes.Transparent;
                        if (!ReferenceEquals(b.Background, bg)) b.Background = bg;
                        IBrush fg = on2 ? Palette.Text : Palette.TextDim;
                        if (!ReferenceEquals(tc.Foreground, fg)) tc.Foreground = fg;
                        FontWeight w = on2 ? FontWeight.SemiBold : FontWeight.Normal;
                        if (tc.FontWeight != w) tc.FontWeight = w;
                    });
                }
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
                BorderThickness = new Thickness(ShapeTokens.Stroke),
                CornerRadius = new CornerRadius(ShapeTokens.RInset),
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
                Content = T(pending ? confirmText : idleText, ShapeTokens.FsBody, pending ? Palette.Bad : Palette.TextDim, pending ? FontWeight.SemiBold : FontWeight.Normal),
                Background = pending ? Palette.BadSoft : Brushes.Transparent,
                BorderBrush = pending ? Palette.Bad : Palette.BorderStrong,
                BorderThickness = new Thickness(ShapeTokens.Stroke),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
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

        /// <summary>二级页签条（2026-10-30）：只有 >=2 个可切换项时才画 ——
        /// 单选项页签点不动任何内容，是纯视觉噪音（体检/设置/说明等页原来都挂着一条）。</summary>
        private static Control SubTabsBar(MainWindow host, Thickness padding)
        {
            if (host.SubTabs.Length <= 1) return null;
            return new Border
            {
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = padding,
                Child = Segmented(host.SubTabs, host.SubTab, delegate(int i) { host.SetSubTab(i); }, host, delegate { return host.SubTab; })
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

            // 视觉层级（2026-10-30）：页题在前、页签在后；只有 >=2 个可切换项时才画页签条。
            Grid body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
            Control title = PageHeader(host);
            Grid.SetRow(title, 0);
            Control subs = SubTabsBar(host, new Thickness(24, 0, 24, 12));
            if (subs != null) { Grid.SetRow(subs, 1); body.Children.Add(subs); }
            Control content = SectionBody(host);
            Grid.SetRow(content, 2);
            Control foot = Footer(host);
            Grid.SetRow(foot, 3);
            body.Children.Add(title); body.Children.Add(content); body.Children.Add(foot);
            Grid.SetColumn(body, 1);
            g.Children.Add(side); g.Children.Add(body);
            return g;
        }

        // ---------------- ② 顶部标签式 ----------------

        private static Control BuildTopTabs(MainWindow host)
        {
            // 视觉层级（2026-10-30）：页题在前、页签在后。
            Grid g = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
            Control nav = MainNavStrip(host);
            Grid.SetRow(nav, 0);
            Control title = PageHeader(host);
            Grid.SetRow(title, 1);
            Control tabs = SubTabsBar(host, new Thickness(24, 0, 24, 0));
            if (tabs != null) { Grid.SetRow(tabs, 2); g.Children.Add(tabs); }
            Control content = SectionBody(host);
            Grid.SetRow(content, 3);
            g.Children.Add(nav); g.Children.Add(title); g.Children.Add(content);
            return g;
        }

        // ---------------- ③ 卡片网格仪表盘 ----------------

        private static Control BuildCardGrid(MainWindow host)
        {
            // 视觉层级（2026-10-30）：页题在前、页签在后（都收进滚动内容里）。
            Grid g = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            Control nav = MainNavStrip(host);
            Grid.SetRow(nav, 0);

            StackPanel body = new StackPanel { Margin = PageMargin, Spacing = ShapeTokens.S16 };
            body.Children.Add(PageHeader(host, false));
            Control tabs = SubTabsBar(host, new Thickness(0, 0, 0, 8));
            if (tabs != null) body.Children.Add(tabs);
            if (host.IsSessionsSection)
            {
                body.Children.Add(KpiStrip(host));
                body.Children.Add(T("会话明细", ShapeTokens.FsTitle, Palette.Text, FontWeight.SemiBold));   // 样式重构：14 → 卡题档 15
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

            StackPanel left = new StackPanel { Margin = new Thickness(14), Spacing = ShapeTokens.S12 };
            // ✓ 用户反馈：④ **会话页时左栏变成会话列表 → 主菜单消失** ✗✗
            //   → 现在：**主菜单永远在最上面** ✓ 会话列表放在它下面 ✓✓（不再互相顶掉 ✓）
            for (int i = 0; i < MainWindow.NavItems.Length; i++) left.Children.Add(NavItem(host, i));
            left.Children.Add(T(host.IsSessionsSection ? "会话列表" : "页面", ShapeTokens.FsBody, Palette.Text, FontWeight.SemiBold));
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
                Padding = new Thickness(24, 8, 24, 8),   // ★ 体验修复：纵向各收 2px（指标页一屏 ✓ 横向不变 ✓）
                Child = Segmented(host.SubTabs, host.SubTab, delegate(int i) { host.SetSubTab(i); }, host, delegate { return host.SubTab; })
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
            SymbolIcon ic = Ic(MainWindow.NavIcons[idx], ShapeTokens.FsBody, active ? Palette.Accent : Palette.TextDim);
            ic.Margin = new Thickness(9, 0, 0, 0);
            ic.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ic, 1);
            TextBlock tb = T(MainWindow.NavItems[idx], ShapeTokens.FsBody, active ? Palette.Accent : Palette.Text);
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
            StackPanel s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ShapeTokens.S8, Margin = new Thickness(12, 0, 12, 6) };
            int cur = host.StartMode;
            string[] names = new string[] { "webui", "desktop" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                bool on = cur == i;
                Button b = new Button
                {
                    Content = T(names[i] == "webui" ? "web" : names[i], ShapeTokens.FsFoot, on ? Palette.OnAccent : Palette.TextDim),
                    Background = on ? Palette.Accent : Palette.CardHover,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
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
                // ★ 看板体验修复（2026-10-11 ✓✓）：切换不再 BuildShell → 高亮走字段刷新 ✓ 点击瞬换 ✓
                TextBlock tc = (TextBlock)b.Content;
                host.Live(delegate
                {
                    bool on2 = host.StartMode == idx;
                    IBrush bg = on2 ? Palette.Accent : Palette.CardHover;
                    if (!ReferenceEquals(b.Background, bg)) b.Background = bg;
                    IBrush fg = on2 ? Palette.OnAccent : Palette.TextDim;
                    if (!ReferenceEquals(tc.Foreground, fg)) tc.Foreground = fg;
                });
                b.Click += delegate { host.SetStartMode(idx); };
                s.Children.Add(b);
            }
            Button dep = new Button
            {
                // ★ 看板体验修复：文案随启动方式原地换 ✓（LiveText ✓ 取值函数 ✗ 不是快照 ✓ 样式与原来逐字对齐 ✓）
                Content = host.LiveText(cur == 1 ? "下载" : "安装", delegate { return host.StartMode == 1 ? "下载" : "安装"; }),
                Background = Palette.CardHover,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(10, 4),
                MinWidth = ModeBtnW,   // N7 FIX: same row, same minimum width
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            TextBlock depT = (TextBlock)dep.Content; depT.FontSize = ShapeTokens.FsFoot; depT.Foreground = Palette.Text;
            dep.Click += delegate { host.DeployForMode(); };
            if (host.ActionBusy) { dep.IsEnabled = false; dep.Opacity = 0.55; }   // ★ 有动作在跑 → 别再引诱人点（2026-10-04 ✓）
            s.Children.Add(dep);
            // **两个都开着时给两个停止按钮** ✓✓（用户要求："如果两个都开着，工具可以选择停止一个" ✓）
            StatusSnapshot st2 = host.Status;
            bool deskOn = st2 != null && st2.Ok && !string.IsNullOrEmpty(st2.DesktopClient);
            bool webOn = st2 != null && st2.Ok && st2.State == 0;
            if (deskOn && webOn)
            {
                StackPanel both = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ShapeTokens.S8, Margin = new Thickness(12, 0, 12, 6) };
                both.Children.Add(T("两个都在跑 →", ShapeTokens.FsFoot, Palette.Warn));
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
            bool deskUp = st != null && st.Ok && st.State == 2 && !string.IsNullOrEmpty(st.DesktopClient);
            bool up = st != null && st.Ok && (st.State == 0 || deskUp);
            bool webUp = st != null && st.State == 0;
            // 状态只说一句（2026-10-30 去重）：句子里已说清的，不再另挂一枚「运行中」徽章。
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Border dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = up ? Palette.Good : Palette.OnAccent,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dot, 0);
            string stText = webUp && deskUp ? "web 与桌面端都在运行" : (deskUp ? "桌面端运行中" : (up ? "停止 dsh" : "一键启动 dsh"));
            TextBlock label = T(stText, ShapeTokens.FsBody, up ? Palette.Accent : Palette.OnAccent, FontWeight.SemiBold);
            label.Margin = new Thickness(10, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            row.Children.Add(dot); row.Children.Add(label);
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
            b.Click += delegate { if (up) host.StopDsh(); else host.StartDsh(); };
            if (host.ActionBusy) { b.IsEnabled = false; b.Opacity = 0.55; }
            return b;
        }

        private static Control MainMenuInner(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = new Thickness(12, 16, 12, 12), Spacing = 2 };
            s.Children.Add(new TextBlock { Text = "导航", Foreground = Palette.TextFaint, FontSize = ShapeTokens.FsFoot, Margin = new Thickness(12, 0, 0, 8) });
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
            s.Children.Add(T(host.PageTitle, ShapeTokens.FsPage, Palette.Text, FontWeight.SemiBold));
            // ★ 看板体验修复（2026-10-11 ✓✓）：副标题（含「图表 = 当前窗口 · 按日分桶」随标签切换的那句）
            //   走 LiveText 原地换 ✓ 不再依赖整页重建 ✓（取值函数 ✗ 不是快照 ✓ 样式与原来逐项对齐 ✓）
            TextBlock sub = host.LiveText(StripMd(host.SubtitleText), delegate { return StripMd(host.SubtitleText); });
            sub.Foreground = Palette.TextDim; sub.FontSize = ShapeTokens.FsBody; sub.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(sub);
            g.Children.Add(s);
            // 提案A：概览/看板页的「自动刷新」收进页眉行右侧 ✓（内容流不再被它占一整行 ✓）
            // ★ 看板重排（2026-10-09 ✓✓）：看板（MainSection==1）两个子标签都显示 ✓（「图表」页也要能刷 ✓）；
            //   概览（MainSection==0）保持原状：仅主标签显示（「原始输出」页不刷 ✓）。
            if (host.IsOverviewLike && (host.MainSection == 1 || host.SubTab == 0))
            {
                Control ar = AutoRefreshBar(host);
                if (ar != null) { Grid.SetColumn(ar, 1); g.Children.Add(ar); }   // ★ 必须设列 ✗ 否则两块同叠列 0（截图抓到标题被遮）
            }
            return g;
        }

        private static Control Footer(MainWindow host)
        {
            // ★ 版本显示（2026-10-11）：页脚这一行现在**长**了（数据来源 + 投影目录 + 工具箱版本 + 桥接插件版本 + 纪律）
            //   → 换行照旧 ✓（TextWrapping.Wrap ✓ 容器宽度就是窗口宽度 ✓ 长文案换行不截断 ✓）
            //   → 并注册成**字段级刷新** ✓：版本是后台补的（启动时 CLI 还不在的情形）✓ 到了原地换字 ✗ 不整页重建 ✓
            TextBlock ft = new TextBlock { Text = host.SourceText, Foreground = Palette.TextFaint, FontSize = ShapeTokens.FsFoot, TextWrapping = TextWrapping.Wrap };
            host.Live(delegate { ft.Text = host.SourceText; });
            return new Border
            {
                Background = Palette.SidebarBg,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24, 7),
                Child = ft
            };
        }

        /// <summary>原始标记行：头部（图标+标题+说明）+ 凹陷等宽文本区（限高内滚）。</summary>
        private static Control RawCard(MainWindow host, string title, string caption)
        {
            StackPanel s = new StackPanel { Spacing = ShapeTokens.S12 };
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            SymbolIcon ic = Ic(Symbol.Code, ShapeTokens.FsBody, Palette.TextDim);
            ic.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ic, 0);
            TextBlock t = T(title, ShapeTokens.FsBody, Palette.Text, FontWeight.SemiBold);
            t.Margin = new Thickness(8, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            TextBlock cap = T(caption, ShapeTokens.FsFoot, Palette.TextFaint);
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
                FontSize = ShapeTokens.FsFoot,
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
                wrap.Spacing = ShapeTokens.S12;
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
                    img.Width = 120; img.Height = 120;
                    img.Stretch = global::Avalonia.Media.Stretch.Uniform;
                    img.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
                    sp.Children.Add(img);
                }
            }
            catch { }
            global::Avalonia.Controls.TextBlock name = new global::Avalonia.Controls.TextBlock();
            name.Text = "dsh-minato";
            name.FontSize = ShapeTokens.FsPage;
            name.FontWeight = global::Avalonia.Media.FontWeight.SemiBold;
            name.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
            sp.Children.Add(name);
            global::Avalonia.Controls.TextBlock tag = new global::Avalonia.Controls.TextBlock();
            tag.Text = "社区版 DeepSeek Harness (dsh) 本机部署运维套件：安装 / 启动 / 监控 / 备份恢复 / 插件诊断与隔离";
            tag.TextWrapping = global::Avalonia.Media.TextWrapping.Wrap;
            tag.Foreground = Palette.TextDim;
            sp.Children.Add(tag);
            global::Avalonia.Controls.TextBlock un = new global::Avalonia.Controls.TextBlock();
            un.Text = "非官方工具，与 DeepSeek 官方无关。图标为社区自制（AI 生成），不适用本项目的 MIT 许可。";
            un.TextWrapping = global::Avalonia.Media.TextWrapping.Wrap;
            un.Foreground = Palette.TextFaint;
            sp.Children.Add(un);
            // ★ 文案精简（2026-10-11）：页脚原来那句架构注记搬到这里 —— 页脚只留版本号与事实。
            global::Avalonia.Controls.TextBlock arch = new global::Avalonia.Controls.TextBlock();
            arch.Text = "本程序只负责显示：它不引用工具箱的核心程序集，而是运行 CLI 并解析它打出的标记行。";
            arch.TextWrapping = global::Avalonia.Media.TextWrapping.Wrap;
            arch.Foreground = Palette.TextFaint;
            sp.Children.Add(arch);
            // ★★★ 2026-10-11（插件卡可读性）：**插件更新的基准与判定**——整段机制说明从更新页的插件卡
            //   搬到这里 ✓（维护者：卡片里那条 NOTE 是一整段机制说明 ✗ 读起来费劲 ✗；长路径也抢注意力 ✗）。
            //   ✗ 内容一个字都没丢 ✗：基准是什么、路径在哪、双比对是什么、为什么没有独立版本渠道、
            //     desktop 为什么装不进、更新前的风险与备份、作者维护不担保 —— **逐条都在下面可读到** ✓。
            //   这里是整段文字天然该待的地方 ✓（架构说明本来就在这一页 ✓）。
            //   ★ 正文本身在 `Markers/UpdateBaselineExplain.cs` ✓（纯函数 ✓ LogicTests 与
            //     `verify_plugin_update_baseline.ps1` 都直接断言它**真的还在** ✓）。
            AddAboutExplain(sp, UpdateBaselineExplain.Title(), UpdateBaselineExplain.Text());
            AddAboutExplain(sp, UpdateBaselineExplain.PathsTitle(), UpdateBaselineExplain.PathsText());
            global::Avalonia.Controls.Border card = new global::Avalonia.Controls.Border();
            card.Padding = new global::Avalonia.Thickness(20, 16);
            card.CornerRadius = new global::Avalonia.CornerRadius(ShapeTokens.RCard);
            card.Background = Palette.CardBg;
            card.BorderBrush = Palette.Border;
            card.BorderThickness = new global::Avalonia.Thickness(ShapeTokens.Stroke);
            card.Margin = PageMargin;
            card.Child = sp;
            return card;
        }

        /// <summary>说明页里的一小节（小标题 + 会换行的正文 ✓ 2026-10-11 插件卡可读性 ✓）。
        /// 用全限定名 —— 这个文件里 `Avalonia.*` 前缀会被解析成 `Dsht.Gui.Avalonia.*` ✗（踩过）。</summary>
        private static void AddAboutExplain(global::Avalonia.Controls.StackPanel sp, string title, string body)
        {
            global::Avalonia.Controls.TextBlock h = new global::Avalonia.Controls.TextBlock();
            h.Text = title;
            h.FontSize = ShapeTokens.FsBody; h.FontWeight = global::Avalonia.Media.FontWeight.SemiBold; h.Foreground = Palette.Text;
            h.Margin = new global::Avalonia.Thickness(0, 12, 0, 0);
            h.TextWrapping = global::Avalonia.Media.TextWrapping.Wrap;
            sp.Children.Add(h);
            global::Avalonia.Controls.TextBlock t = new global::Avalonia.Controls.TextBlock();
            t.Text = body;
            t.TextWrapping = global::Avalonia.Media.TextWrapping.Wrap;
            t.FontSize = ShapeTokens.FsFoot; t.Foreground = Palette.TextDim;
            sp.Children.Add(t);
        }

        /// <summary>「插件基准的完整路径」那一节的正文在 `Markers/UpdateBaselineExplain.cs`（纯函数 ✓ 可测 ✓
        /// 2026-10-11 插件卡可读性：与机制说明一起从更新页卡片搬到说明页 ✓）。</summary>
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
                wrap.Spacing = ShapeTokens.S12;
                wrap.Children.Add(AboutCard());
                wrap.Children.Add(new Border { Margin = PageMargin, Child = RawCard(host, "原始输出", "CLI 标记行原文") });
                return wrap;
            }
        }

        // ================================================================ 会话与 Token

        private static Control SessionsContent(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = ShapeTokens.S16 };
            if (host.Data == null || !host.Data.Ok)
            {
                string msg = host.Data == null
                    ? (string.IsNullOrEmpty(host.RawOutput) ? "读不到会话数据。" : host.RawOutput)
                    : (string.IsNullOrEmpty(host.Data.FailReason) ? "没有可显示的会话。" : host.Data.FailReason);
                StackPanel err = new StackPanel { Spacing = 8 };
                Grid eh = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                SymbolIcon wic = Ic(Symbol.Warning, ShapeTokens.FsBody, Palette.Warn);
                wic.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(wic, 0);
                TextBlock et = T("会话数据不可用", ShapeTokens.FsBody, Palette.Text, FontWeight.SemiBold);
                et.Margin = new Thickness(8, 0, 0, 0);
                et.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(et, 1);
                eh.Children.Add(wic); eh.Children.Add(et);
                err.Children.Add(eh);
                err.Children.Add(new TextBlock { Text = msg, Foreground = Palette.TextDim, FontSize = ShapeTokens.FsBody, TextWrapping = TextWrapping.Wrap });
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
            s.Children.Add(new TextBlock { Text = host.FocusText, Foreground = Palette.TextDim, FontSize = ShapeTokens.FsBody, TextWrapping = TextWrapping.Wrap });
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
                s.Children.Add(Card(T("没有可统计的会话数据。", ShapeTokens.FsFoot, Palette.TextDim), new Thickness(0), new Thickness(16, 14)));
                return s;
            }
            StackPanel sum = new StackPanel { Spacing = 8 };
            sum.Children.Add(T("累计输入 token", ShapeTokens.FsFoot, Palette.TextDim));
            sum.Children.Add(T(SessionRow.Human(d.TotalIn), ShapeTokens.FsPage, Palette.Text, FontWeight.Bold));
            sum.Children.Add(T("新输入 " + SessionRow.Human(d.TotalIn - d.TotalCacheRead < 0 ? 0 : d.TotalIn - d.TotalCacheRead) + " · 输出 " + SessionRow.Human(d.TotalOut) + " · 缓存命中 " + SessionRow.Human(d.TotalCacheRead) + " · 会话 " + d.Count + " 个（非空 " + d.NonBlank + "）", ShapeTokens.FsFoot, Palette.TextFaint));
            sum.Children.Add(Meter(d.TotalHitPercent < 0 ? 0 : d.TotalHitPercent, Palette.HitBrush(d.TotalHitPercent >= 90 ? 3 : (d.TotalHitPercent >= 70 ? 2 : 1)), 8));
            sum.Children.Add(T("缓存命中率 " + PctText(d.TotalHitPercent) + "　解码速度 " + TpsText(d.TotalDecodeTps) + " tok/s", ShapeTokens.FsBody, Palette.TextDim));
            s.Children.Add(Card(sum, new Thickness(0), new Thickness(18, 16)));

            List<SessionRow> top = SessionsView.Sort(SessionsView.Filter(d.Rows, SessionsView.FilterAll), 1);
            StackPanel list = new StackPanel { Spacing = ShapeTokens.S8 };
            list.Children.Add(T("输入 token 最多的会话", ShapeTokens.FsBody, Palette.Text, FontWeight.Bold));
            for (int i = 0; i < top.Count && i < 10; i++)
            {
                SessionRow r = top[i];
                string name = string.IsNullOrEmpty(r.Title) ? r.ShortId : r.Title;
                list.Children.Add(T((i + 1) + ".　" + name + "　　" + r.InText + "　命中 " + r.HitText + "　" + r.DecodeText, ShapeTokens.FsFoot, Palette.TextDim));
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
                Button b = GhostButton(T("进行中：" + host.ActionBusyLabel + " …", ShapeTokens.FsFoot, Palette.TextFaint), delegate { }, true);
                b.IsEnabled = false;
                return b;
            }
            return GhostButton(T(label, ShapeTokens.FsFoot, Palette.Text), act, true);
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
