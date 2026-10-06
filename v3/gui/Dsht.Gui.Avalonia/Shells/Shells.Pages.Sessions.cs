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
    /// <summary>从 Shells.cs 拆出的页面构建代码（2026-10-06 重构：只移动、不改逻辑）。</summary>
    public static partial class Shells
    {
        private static Control LegendDot(IBrush c, string text)

        {

            StackPanel p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 0, 0, 0) };

            p.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = c, VerticalAlignment = VerticalAlignment.Center });

            TextBlock t = T(text, 10.5, Palette.TextFaint); t.VerticalAlignment = VerticalAlignment.Center;

            p.Children.Add(t);

            return p;

        }

        private static Control Toolbar(MainWindow host)

        {

            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            Control filters = Segmented(new string[] { "全部", "非空", "dsh 内" }, host.Filter, delegate(int i) { host.SetFilter(i); });

            Grid.SetColumn(filters, 0);



            StackPanel sorts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(14, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };

            // 美学A4：状态图例 ✓ 四种状态不再只靠色点+悬停猜 ✓

            sorts.Children.Add(LegendDot(Palette.Good, "运行中"));

            sorts.Children.Add(LegendDot(Palette.Idle, "挂着"));

            sorts.Children.Add(LegendDot(Palette.Muted, "空/未知"));

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

        private static Control SessionCard(SessionRowVm vm)

        {

            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto") };



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

        private static Control Line(string text)

        {

            return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Palette.TextDim, FontSize = 12 };

        }

    }
}