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

            // —— 看板第一批（2026-10-08 ✓✓ 规格 §4.1 ✓）：窗口档生效 → KPI 按 **SESSION 行 ∩ eligible id 集**重算 ✓
            //   eligible = SESSAGG_SESSION 行 id（CLI 已按会话最后活动时间过滤 ✓ truth=0 ✓）
            //   老 CLI（没有 SESSWIN_META 行 → AggIdSet() 返回 null ✓）或 总计档（WinDays<0 → 全体 ✓）
            //   → 走下面原路径（CLI 精确合计 ✓ 口径不变 ✓✓）
            HashSet<string> elig = d.AggIdSet();

            if (!a.Filtered && elig != null && d.WinDays > 0)

            {

                a.WinDays = d.WinDays;

                double hitNumW = 0, hitDenW = 0, tpsNumW = 0, tpsDenW = 0;

                for (int wi = 0; wi < d.Rows.Count; wi++)

                {

                    SessionRow r = d.Rows[wi];

                    if (r == null) continue;

                    if (r.Id == null || !elig.Contains(r.Id)) continue;

                    a.Count++;

                    if (!r.Blank) a.NonBlank++;

                    if (r.Live) a.Live++;

                    if (r.IsSubAgent) a.Subs++;

                    a.In += r.In; a.Out += r.Out; a.Cache += r.CacheRead;

                    if (r.HitPercent >= 0 && r.In > 0) { hitNumW += r.HitPercent * r.In; hitDenW += r.In; }

                    if (r.DecodeTps >= 0 && r.Out > 0) { tpsNumW += r.DecodeTps * r.Out; tpsDenW += r.Out; }

                }

                a.HitPct = hitDenW > 0 ? hitNumW / hitDenW : -1;

                a.Tps = tpsDenW > 0 ? tpsNumW / tpsDenW : -1;

                return a;

            }

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

        private static Control KpiStrip(MainWindow host)

        {

            // ★★ 字段级刷新（2026-10-04 用户要求："自动刷新…可以只字段刷新吗" ✓✓）：

            //   四张卡改成 KpiCardLive ✓ 每拍用 `Agg(host)` **重算** ✓（行数百来条 ✓ 纯计算 ✓ 便宜 ✓）

            //   ✗ 不重建视觉树 ✓ → 不丢滚动/焦点 ✓ 也不触发整页淡入 ✓✓

            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };

            g.Children.Add(KpiCardLive(host, Symbol.ChatMultiple,

                delegate { return "会话总数" + KpiTag(host); },

                // ★ 看板层级重整（2026-10-26 ✓）：数据不可用（没取到 / SESSIONS_FAIL）⇒ **「未知」**，
                //   ✗ 原来打 `0` —— 那是把"读不到"说成"没有"，正是硬红线要防的 ✓（口径没动 ✓ 只换降级文案 ✓）
                delegate { SessionsSnapshot d = host.Data; return (d == null || !d.Ok) ? "未知" : Agg(host).Count.ToString(); },

                delegate

                {

                    SessionsSnapshot d = host.Data; KpiAgg a = Agg(host);

                    if (d == null || !d.Ok) return "读不到会话投影，这几项都是未知。";

                    return "非空 " + a.NonBlank.ToString() + " · dsh 活跃 " + a.Live.ToString()

                        + "（在动看每行状态；「挂着」= 还在 dsh 里但没动） · 子代理 " + a.Subs.ToString()

                        + " / 无父边 " + (a.Count - a.Subs).ToString();   // ★ 2026-10-26：原写"根"是**过度声称** ✗（无父边 ⇒ 无法证明是根 ✓）

                }, Palette.Text, 0, null));

            g.Children.Add(KpiCardLive(host, Symbol.Database,

                delegate { return "缓存命中率" + KpiTag(host); },

                delegate { SessionsSnapshot d = host.Data; return (d == null || !d.Ok) ? "未知" : PctText(Agg(host).HitPct); },

                delegate
                {
                    SessionsSnapshot d = host.Data;
                    if (d == null || !d.Ok) return "读不到会话投影，这几项都是未知。";
                    return "缓存读 " + SessionRow.Human(Agg(host).Cache);
                },

                Palette.Good, 1, delegate { return Agg(host).HitPct; }));

            g.Children.Add(KpiCardLive(host, Symbol.Gauge,

                delegate { return "解码速度" + KpiTag(host); },

                delegate { SessionsSnapshot d = host.Data; return (d == null || !d.Ok) ? "未知" : TpsText(Agg(host).Tps); },

                delegate { SessionsSnapshot d = host.Data; return (d == null || !d.Ok) ? "读不到会话投影，这几项都是未知。" : "tok/s"; }, Palette.Accent, 2, null));

            g.Children.Add(KpiCardLive(host, Symbol.DataUsage,

                delegate { return "累计 token" + KpiTag(host); },

                delegate { SessionsSnapshot d = host.Data; return (d == null || !d.Ok) ? "未知" : SessionRow.Human(Agg(host).In); },

                delegate
                {
                    SessionsSnapshot d = host.Data; KpiAgg a = Agg(host);
                    if (d == null || !d.Ok) return "读不到会话投影，这几项都是未知。";
                    // ★ 口径拆开（用户反馈：400 亿这个数字太吓人 → 让它自我解释 ✓）
                    long uncached = a.In - a.Cache;
                    if (uncached < 0) uncached = 0;
                    return "新输入 " + SessionRow.Human(uncached) + " · 缓存命中 " + SessionRow.Human(a.Cache)
                        + " · 生成 " + SessionRow.Human(a.Out);
                },

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

            s.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = valueBrush, Margin = new Thickness(0, 8, 0, 0) });

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

            v.FontSize = 22;   // 美学B4：与 StatCard 统一为 22 ✓ v.FontWeight = FontWeight.SemiBold; v.Foreground = valueBrush; v.Margin = new Thickness(0, 5, 0, 0);   // ★ 体验修复：8→5（一屏 ✓）

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

            Border card = Card(s, new Thickness(0, 0, col == 3 ? 0 : 12, 0), new Thickness(16, 8));   // ★ 体验修复：纵向压一档（指标页一屏 ✓ 字号/内容不变 ✓）

            Grid.SetColumn(card, col);

            return card;

        }

        private static Control AutoRefreshBar(MainWindow host)

        {

            StackPanel arBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ShapeTokens.S12 };

            TextBlock arLabel = T("自动刷新", 11.5, Palette.Text, FontWeight.SemiBold); arLabel.VerticalAlignment = VerticalAlignment.Center; arBar.Children.Add(arLabel);

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

            ComboBox arCb = SlimCombo(arLabels, sel, 118); arCb.VerticalAlignment = VerticalAlignment.Center;

            // ★ 先设 SelectedIndex、后挂事件 ✓ —— 初始化那一拍不会触发一次多余的写盘 ✓
            ToolTip.SetTip(arCb, "只在概览/看板生效。0.5 秒档 = 每拍起一个 CLI 进程（有真实成本），自定义可输 0.5–3600 秒");

            arCb.SelectionChanged += async delegate

            {

                int idx2 = arCb.SelectedIndex;

                if (idx2 < 0) return;

                if (idx2 == 5)

                {

                    string typed = await PromptDialog.Ask(host, "自定义刷新间隔", "请输入秒数", "0.5 – 3600", "3");

                    if (typed == null || typed.Trim().Length == 0) return;

                    host.SetAutoRefresh(typed.Trim());

                    return;

                }

                host.SetAutoRefresh(arValues[idx2]);

            };

            arBar.Children.Add(arCb);


            return new Border

            {

                Background = Palette.InsetBg,

                CornerRadius = new CornerRadius(8),

                Padding = new Thickness(12, 7),

                Margin = new Thickness(12, 0, 12, 0),

                VerticalAlignment = VerticalAlignment.Center,

                Child = arBar

            };

        }

        private static Control OverviewContent(MainWindow host)

        {

            if (host.SubTab == 1) return new Border { Margin = PageMargin, Child = RawCard(host, "原始输出", "status --detail 的标记行原文") };

            return StatusDetail(host);

        }

        /// <summary>看板 = **指标与图表的唯一主场（双子标签）**。
        /// ★ 看板重排（2026-10-09 ✓✓ 规格 docs\看板重排规格-给Kimi-2026-10-09.md §2 ✓✓）——
        ///   主标签「指标」= KPI 四卡 → 筛选条 → 总计卡（固定总计块 + 窗口块合一 ✓ §2-5 ✓）→ 操作回执细条 ✓ **没有图** ✗（§2-7 ✓）
        ///     —— 1320×840 首屏顺序 = KPI → 筛选 → 总计（→ 回执细条），与 §2-9 一致 ✓；
        ///   子标签「图表」= 筛选条（同一口径入口 ✓ 两标签共享 ✓ C.4 脚注每屏恰好一次 ✓ §1 ✓）→ 七张图（同排两图等高 ✓ §2-8 ✓）。
        ///   上方两段旧注释曾自相矛盾（指标/图表分标签 → 又合成单页）—— 现在是第二次拆分，以此段为准 ✓。
        /// ★★ 看板层级重整（2026-10-26 ✓✓ 提案《看板优化与置顶悬浮窗》§3 五条 ✓✓）——
        ///   ① 状态胶囊（结论 + 数据时刻）在最上 ✓ ② 今日用量进度（相对**用户自填预算** ✓ ✗ 不预置阈值 ✓）
        ///   ③ 最近活动流 ④ 24 小时分布（整会话累计口径 + 脚注 ✓）⑤ 一屏可读：③④ 与既有次要卡一起收进**折叠区**
        ///   ⇒ 默认首屏只留 5 个数字：胶囊状态 + 数据时刻 + KPI 四卡 + 今日用量 ✓。
        ///   ★ **只改呈现层级** ✗ 数据口径（窗口 / 三档 / 未知 / 估算 / 四桶）一个字没动 ✓✓。</summary>
        private static Control BoardContent(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = new Thickness(24, 8, 24, 8), Spacing = ShapeTokens.S16 };   // ★ 体验修复：看板页纵向边距压半（一屏 ✓ 横向不变 ✓ 别页不动 ✓）
            // ★★ 看板体验修复（2026-10-11 ✓✓ 规格 docs\看板体验修复-派单-Kimi-2026-10-11.md ✓✓）：
            //   ✗ 原来「指标/图表」切换 = 只建一棵子树 + `BuildShell()` 整页重建 → 闪 160ms ✗ 滚动回顶 ✗✗
            //   ✓ 现在两棵子树**都建好**、切换只切 `IsVisible` ✓ 零重建 ✓ 零闪烁 ✓ 不跳滚动 ✓✓
            //   （CLI 数据照旧按原节奏刷 ✗ 不降刷新频率 ✓；两棵树同一时刻只有一棵可见 ✗ 没有双份 CLI 开销 ✓）
            // —— 子标签「指标」顺序（收尾 (a) 之后）= 数据时刻一行 → KPI → 今日用量 → 筛选 → 折叠区 → 回执细条 ——
            StackPanel metrics = new StackPanel { Spacing = ShapeTokens.S12 };   // 12px ✓（规格：卡间距 12~16 ✓ 一屏放下 ✓）
            // ① 数据时刻一行纯文字（2026-10-26 收尾 (a) —— **状态胶囊已按维护者意见拆掉**
            //   「状态胶囊算了」；但它承担的**数据时刻不能丢** ⇒ 保留成顶部**一行纯文字**
            //   不许两套并存：状态现在只有**悬边状态灯**那一处（提案 §11），看板不再重复一遍。
            metrics.Children.Add(BoardDataClockLine(host));
            // KPI 四卡（首屏 4 个数字 ✓ §3⑤）
            metrics.Children.Add(KpiStrip(host));
            // ② 进度化（提案 §3② ✓）：把「今天用了多少」做成进度条 —— 相对**用户自填预算**（sessions_budget_usd_per_day ✓）
            //   ✗ 不许预置阈值 ✗；未填预算 ⇒ **不画进度条** ✓ 只显示数字 ✓
            metrics.Children.Add(BoardTodayGaugeCard(host));
            // 筛选条：窗口档 + 「自定义…」+ 口径 chips + fork 口径差「?」入口（B.5 ✓ §2-4 ✓）+ C.4 固定脚注（本屏唯一一次 ✓ §1 ✓）
            metrics.Children.Add(BoardFilterBar(host));
            // ⑤ 一屏可读（提案 §3⑤ ✓）：③④ 与其余次要指标（总计卡 / 成本明细 / 三档对比 / Top 5）收进**折叠区**
            //   —— 默认**收起** ✓ 首屏只留上面那几个数字 ✓；展开走字段级刷新 ✗ 不整页重建 ✓
            metrics.Children.Add(BoardMoreToggle(host));
            metrics.Children.Add(BoardMoreBody(host));
            // 操作回执细条**常显**（§2-6 ✓ 体验修复 ✓✓）：空也要占住高度 ✗ 不许整卡消失 ✓
            metrics.Children.Add(ReceiptStrip(host));
            // —— 子标签「图表」（§2-7 ✓✓）：筛选条（同一口径入口 ✓ 两标签共享 ✓）+ 范围档卡 + 七张图 ✓
            StackPanel charts = new StackPanel { Spacing = ShapeTokens.S16 };
            charts.Children.Add(BoardFilterBar(host));   // 图随窗口/口径 ✓ C.4 脚注本屏唯一一次（图内不再重复 ✓ §1 ✓）
            charts.Children.Add(ChartRangeCard(host));
            charts.Children.Add(ChartsBody(host));
            bool m0 = host.SubTab != 1;
            metrics.IsVisible = m0; charts.IsVisible = !m0;
            host.Live(delegate   // ★ 切换 = 只切可见性 ✓（SetSubTab → RefreshLiveFields 走到这里 ✓✓）
            {
                bool m = host.SubTab != 1;
                if (metrics.IsVisible != m) metrics.IsVisible = m;
                if (charts.IsVisible == m) charts.IsVisible = !m;
            });
            s.Children.Add(metrics);
            s.Children.Add(charts);
            // ★ 结构变化守卫：切换不再整页重建后，**结构真变**（首次拿到数据 / 失败 / 血缘出现或消失 → 口径 chips 形态变 ✓）
            //   仍重建一次 ✗ 不许"结构变了界面没跟上" ✗✓（RebuildBoard 不淡入 ✓ 不闪 ✓ 只在签名真变时触发 ✓）
            string sig0 = BoardStructSig(host.Data);
            host.Live(delegate
            {
                if (BoardStructSig(host.Data) != sig0) host.RebuildBoard();
            });
            return s;
        }

        // ================================================================ ① 数据时刻一行（收尾 (a) ✓ 原状态胶囊的位置）

        /// <summary>看板顶部**一行纯文字**（2026-10-26 收尾 (a)）：只承担**数据时刻**（HH:mm:ss；
        /// 取不到就说「未知」，绝不显示 00:00:00）。
        /// <para>★ 状态胶囊已拆（维护者：「状态胶囊算了」）：看板上**不再有**状态点 / 状态句 / 判据行 ——
        /// 状态只由**悬边状态灯**一处承担（不许两套并存）。</para>
        /// <para>★ 这一行**不是胶囊**：没有底、没有边框、没有状态点，只有一行文字 + 一句口径说明；
        /// 刷新走字段级 <see cref="MainWindow.LiveText"/>，不整页重建。</para>
        /// </summary>
        private static Control BoardDataClockLine(MainWindow host)
        {
            StackPanel c = new StackPanel { Spacing = 5 };
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            TextBlock clock = host.LiveText(BoardHierarchy.DataClockLine(host.DataAt),
                delegate { return BoardHierarchy.DataClockLine(host.DataAt); });
            clock.FontSize = ShapeTokens.FsBody; clock.Foreground = Palette.TextDim;
            clock.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(clock, 0);
            row.Children.Add(clock);
            // ★ 数据时刻的口径说明（唯一出处）：它是"本机解析出这份 CLI 输出的时刻"，不是 dsh 里数据产生的时刻
            TextBlock note = T("（本机解析出这份输出的时刻；不是数据在 dsh 里产生的时刻）", ShapeTokens.FsFoot, Palette.TextFaint);
            note.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(note, 1);
            row.Children.Add(note);
            c.Children.Add(row);
            return c;
        }

        // ================================================================ ② 今日用量进度（提案 §3②）

        /// <summary>今日用量卡（2026-10-26 · 提案 §3② ✓✓）：把「今天用了多少」做成进度条 ——
        /// **相对用户自填预算**（`sessions_budget_usd_per_day` ✓ 已有键 ✓）✗ 不预置任何阈值 ✗；
        /// 未填预算 ⇒ `GaugeOf(...).DrawBar=false` ⇒ **这一格不出现** ✓ 只显示数字 ✓（✗ 绝不自己造一个上限 ✓）；
        /// 超限提示条也搬到这里（**常显卡** ✓ 折叠区收起时也不会把警示藏起来 ✓）——文案仍走
        /// `CostMarkers.OverBudgetText`（唯一出处 ✓ 只提示 ✗ 不阻止任何操作 ✓）。
        /// 全部字段级刷新 ✓（进度条只在比例真变时重建那一个小控件 ✗ 不整页重建 ✓）。</summary>
        private static Control BoardTodayGaugeCard(MainWindow host)
        {
            StackPanel c = new StackPanel { Spacing = 6 };
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            TextBlock title = T("今天用了多少", 13, Palette.Text, FontWeight.Bold);
            Grid.SetColumn(title, 0);
            head.Children.Add(title);
            TextBlock tag = host.LiveText(GaugeTagText(host), delegate { return GaugeTagText(host); });
            tag.FontSize = 11.5; tag.Foreground = Palette.TextFaint;
            tag.VerticalAlignment = VerticalAlignment.Center; tag.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(tag, 2);
            head.Children.Add(tag);
            c.Children.Add(head);

            // 超限提示条（仅真超限 ✓ 只提示 ✗ 不阻止 ✓ 与成本卡共用同一文案函数 ✓）
            TextBlock over = host.LiveText(CostMarkers.OverBudgetText(host.Cost), delegate { return CostMarkers.OverBudgetText(host.Cost); });
            over.FontSize = 12; over.Foreground = Palette.Bad; over.TextWrapping = TextWrapping.Wrap;
            Border overBar = new Border
            {
                Background = Palette.BadSoft,
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(10, 6),
                Child = over
            };
            overBar.IsVisible = over.Text != null && over.Text.Length > 0;
            host.Live(delegate { overBar.IsVisible = over.Text != null && over.Text.Length > 0; });
            c.Children.Add(overBar);

            // 金额行（有预算 ⇒ 进度句 ✓ 没预算 ⇒ 数字 + 如实说未设预算 ✓）
            TextBlock money = LvLine(host, 12.5, Palette.Text, FontWeight.SemiBold,
                delegate { return BoardHierarchy.GaugeMoneyLine(host.Cost); });
            money.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(money);

            // ★ 进度条：**只有**「用户填了正数预算」且「今日金额可算」时才出现 ✓（✗ 未填预算不画 ✗ 未知不画 ✓）
            BoardHierarchy.Gauge g0 = BoardHierarchy.GaugeOf(host.Cost);
            Border barHost = new Border
            {
                IsVisible = g0.DrawBar,
                Child = g0.DrawBar ? Meter(g0.Percent, GaugeBrush(g0), 6) : null
            };
            double lastPct = g0.DrawBar ? g0.Percent : double.MinValue;
            bool lastOver = g0.Over;
            host.Live(delegate
            {
                BoardHierarchy.Gauge g = BoardHierarchy.GaugeOf(host.Cost);
                if (barHost.IsVisible != g.DrawBar) barHost.IsVisible = g.DrawBar;
                if (!g.DrawBar) { lastPct = double.MinValue; return; }
                if (Math.Abs(g.Percent - lastPct) > 0.05 || g.Over != lastOver)
                {
                    lastPct = g.Percent; lastOver = g.Over;
                    barHost.Child = Meter(g.Percent, GaugeBrush(g), 6);   // ✗ 只换这一格 ✗ 不整页重建 ✓
                }
            });
            c.Children.Add(barHost);

            // 数字（token 行：未配价也照常显示 ✓ ✗ 不用窗口数冒充今日 ✓）
            TextBlock tok = LvLine(host, 11.5, Palette.TextDim, FontWeight.Normal,
                delegate { return CostMarkers.TodayTokensLine(CostToday(host)); });
            tok.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(tok);

            TextBlock fn = T(BoardHierarchy.GaugeFootnote, 10.5, Palette.TextFaint);
            fn.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(fn);
            return Card(c, new Thickness(0), new Thickness(16, 10));
        }

        /// <summary>进度条颜色：超预算 = Bad ✓ 其余 = Accent ✓（✗ 不给"安全阈值"配色 —— 我们没有阈值 ✓）。</summary>
        private static IBrush GaugeBrush(BoardHierarchy.Gauge g)
        {
            return g != null && g.Over ? Palette.Bad : Palette.Accent;
        }

        /// <summary>右上角小字：有预算 ⇒「相对你自填的每日预算」✓；没预算 ⇒「未设置每日预算」✓（✗ 不暗示有上限 ✓）；
        /// ★ 成本数据本身没读到（没有 COST_* 块 / 没有 today 块）⇒ 说「成本数据未提供」✗
        ///   —— ✗ 不许在这种时候断言「未设置每日预算」（那是我们并不知道的事 ✗✓）。</summary>
        private static string GaugeTagText(MainWindow host)
        {
            CostSnapshot cs = host.Cost;
            if (cs == null || !cs.HasCost || cs.Today == null) return "成本数据未提供";
            BoardHierarchy.Gauge g = BoardHierarchy.GaugeOf(cs);
            if (!g.HasBudget) return "未设置每日预算";
            return "相对你自填的每日预算";
        }

        // ================================================================ ⑤ 折叠区（提案 §3⑤）

        /// <summary>折叠区入口（提案 §3⑤ ✓）：默认收起 ⇒ 首屏只留 4~5 个数字 ✓；
        /// 文案/无障碍名随状态走 ✓ 点击只改本地视图态 + 字段刷新一拍 ✗ 不整页重建 ✓。</summary>
        private static Control BoardMoreToggle(MainWindow host)
        {
            StackPanel col = new StackPanel { Spacing = 4 };
            TextBlock label = host.LiveText(MoreLabel(host.BoardMoreOpen), delegate { return MoreLabel(host.BoardMoreOpen); });
            label.FontSize = 11.5; label.Foreground = Palette.Accent;
            Button b = new Button
            {
                Content = label,
                Background = Palette.CardHover,
                BorderBrush = Palette.Accent,          // 描边强调：这是"展开次要指标"的唯一入口 ✓ 一眼能看见 ✓
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(12, 5),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            b.Click += delegate { host.ToggleBoardMore(); };
            global::Avalonia.Automation.AutomationProperties.SetName(b, "更多指标");
            global::Avalonia.Automation.AutomationProperties.SetHelpText(b, "展开或收起次要指标（总计 / 成本明细 / 三档对比 / Top 5 / 24 小时分布 / 最近活动）");
            host.Live(delegate
            {
                string an = host.BoardMoreOpen ? "更多指标（展开中，点击收起）" : "更多指标（点击展开）";
                if (global::Avalonia.Automation.AutomationProperties.GetName(b) != an)
                {
                    global::Avalonia.Automation.AutomationProperties.SetName(b, an);
                }
            });
            col.Children.Add(b);
            TextBlock hint = T("展开后：总计 · 成本明细 · 三档口径对比 · 输入 Top 5 · 24 小时分布 · 最近活动", 10.5, Palette.TextFaint);
            hint.TextWrapping = TextWrapping.Wrap;
            col.Children.Add(hint);
            return col;
        }

        private static string MoreLabel(bool open)
        {
            return open ? "收起更多指标" : "更多指标（展开）";
        }

        /// <summary>折叠区本体（提案 §3⑤ ✓）：既有次要卡 + 新增的 ③④ 一起收在这里。
        /// 默认收起 ✓（`host.BoardMoreOpen=false`）；显隐走字段级刷新 ✓ ✗ 不整页重建 ✓。</summary>
        private static Control BoardMoreBody(MainWindow host)
        {
            StackPanel body = new StackPanel { Spacing = ShapeTokens.S12 };
            body.Children.Add(BoardTotalsCard(host));
            body.Children.Add(BoardCostCard(host));
            body.Children.Add(BoardLevelCompareCard(host));
            body.Children.Add(BoardTop5Card(host));
            body.Children.Add(BoardHourlyCard(host));     // ④ 24 小时分布
            body.Children.Add(BoardActivityCard(host));   // ③ 最近活动流
            body.IsVisible = host.BoardMoreOpen;
            host.Live(delegate
            {
                bool o = host.BoardMoreOpen;
                if (body.IsVisible != o) body.IsVisible = o;
            });
            return body;
        }

        // ================================================================ ④ 24 小时分布（提案 §3④）

        /// <summary>24 小时分布卡（2026-10-26 · 提案 §3④ ✓✓ —— **这条最容易做假，口径严格照 §3④** ✓）：
        /// 能做①每小时的**会话活动数**（按会话最后提问时间归属 ✓ 这是逐时真值 ✓）；
        /// 能做②token 与缓存命中率按**最后活动小时整会话累计归属** ✓ —— ★ 因此**必须带脚注**
        /// 「整会话累计，非该小时增量」✓✓（`BoardHierarchy.HourlyFootnote` 唯一出处 ✓ 逐字断言 ✓）；
        /// ✗ **不许**声称"每小时真实 token 增量"✗（我们的投影只有会话级累计 ✓ 逐时真值须解原始 zstd ✓ 拿不到 ✓）。
        /// 数据不可用 ⇒ 整卡只显示「未知」✗ 绝不画一张全 0 的表 ✓。</summary>
        private static Control BoardHourlyCard(MainWindow host)
        {
            StackPanel c = new StackPanel { Spacing = 4 };
            c.Children.Add(T(BoardHierarchy.HourlyTitle, 13, Palette.Text, FontWeight.Bold));
            TextBlock sum = LvLine(host, 11.5, Palette.TextDim, FontWeight.Normal, delegate
            {
                SessionsSnapshot d = host.Data;
                if (d == null || !d.Ok) return BoardHierarchy.HourlyUnknownText;
                return BoardHierarchy.HourlySummaryText(BoardHierarchy.HourlyDistribution(d));
            });
            sum.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(sum);
            TextBlock peak = LvLine(host, 11.5, Palette.TextDim, FontWeight.Normal, delegate
            {
                SessionsSnapshot d = host.Data;
                if (d == null || !d.Ok) return "";
                return BoardHierarchy.HourlyPeakText(BoardHierarchy.HourlyDistribution(d));
            });
            peak.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(peak);
            // 24 行固定表格（列 = 小时 / 会话活动数 / 整会话累计 token 条形 / 输入 token / 缓存命中率）
            //   ★ 列头**必须写明** token 与命中率是整会话累计口径 ✓（✗ 不写成"每小时用量"✗）
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("44,54,*,120,92") };
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddLvCell(g, T("小时", 11, Palette.TextFaint), 0, 0);
            AddLvCell(g, T("会话", 11, Palette.TextFaint), 0, 1);
            AddLvCell(g, T("整会话累计 token", 11, Palette.TextFaint), 0, 2);
            AddLvCell(g, T("输入 token", 11, Palette.TextFaint), 0, 3);
            AddLvCell(g, T("缓存命中率", 11, Palette.TextFaint), 0, 4);
            for (int h = 0; h < 24; h++)
            {
                int hh = h;
                g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                int row = hh + 1;
                AddLvCell(g, LvLine(host, 11, Palette.TextDim, FontWeight.Normal,
                    delegate { return BoardHierarchy.Two(hh) + " 时"; }), row, 0);
                AddLvCell(g, LvLine(host, 11, Palette.Text, FontWeight.Normal,
                    delegate { return HourCellOf(host, hh).Sessions.ToString(); }), row, 1);
                Border bar = new Border { Height = 8, VerticalAlignment = VerticalAlignment.Center };
                bar.Child = new Border { Background = Palette.BarTrack, CornerRadius = new CornerRadius(4) };
                double lastP = double.MinValue;
                host.Live(delegate
                {
                    BoardHierarchy.Hourly hy = host.Data != null && host.Data.Ok ? BoardHierarchy.HourlyDistribution(host.Data) : null;
                    double p = hy == null ? -1 : HourBarPercent(hy, hh);
                    if (Math.Abs(p - lastP) < 0.05) return;
                    lastP = p;
                    bar.Child = p < 0
                        ? (Control)new Border { Background = Palette.BarTrack, CornerRadius = new CornerRadius(4) }
                        : Meter(p, Palette.Accent, 8);   // ✗ 只换这一格 ✗ 不整页重建 ✓
                });
                AddLvCell(g, bar, row, 2);
                AddLvCell(g, LvLine(host, 11, Palette.TextDim, FontWeight.Normal,
                    delegate { return BoardHierarchy.HourTokenText(HourCellOf(host, hh)); }), row, 3);
                AddLvCell(g, LvLine(host, 11, Palette.TextDim, FontWeight.Normal,
                    delegate { return BoardHierarchy.HourHitText(HourCellOf(host, hh)); }), row, 4);
            }
            // ★★ 数据不可用 ⇒ **整张表不出现**（只留上面的「未知」句 ✓）——
            //   ✗ 否则会摆出 24 行 0，那正是「未知一律显示未知、绝不显示 0」这条硬红线要防的 ✗✓
            //   ★ 注意：表格只挂到 gHost 上（✗ 不能先挂进 c 再挂进 gHost —— 一个控件只能有一个父 ✗✓）
            Border gHost = new Border { Child = g };
            SessionsSnapshot d0h = host.Data;
            gHost.IsVisible = d0h != null && d0h.Ok;
            host.Live(delegate
            {
                SessionsSnapshot dh = host.Data;
                bool v = dh != null && dh.Ok;
                if (gHost.IsVisible != v) gHost.IsVisible = v;
            });
            c.Children.Add(gHost);
            // ★ 口径脚注（唯一出处 ✓ 逐字含「整会话累计，非该小时增量」✓✓）
            TextBlock fn = T(BoardHierarchy.HourlyFootnote, 10.5, Palette.TextFaint);
            fn.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(fn);
            // ★ 集合口径脚注（2026-10-26 收尾 (c) ✓✓）：与既有热力图**不是同一个集合** ⇒ 必须点明 ✗ 不许含糊 ✓
            TextBlock fn2 = T(BoardHierarchy.HourlyEligibleNote, 10.5, Palette.TextFaint);
            fn2.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(fn2);
            return Card(c, new Thickness(0), new Thickness(18, 8));
        }

        /// <summary>该小时格（数据不可用 ⇒ 全 0 的空格 —— 但**卡片在那种情况下只显示「未知」** ✓
        /// 由 `HourlyUnknownText` 与空表格的显隐把守 ✗ 不会把 0 当成事实摆出来 ✓）。</summary>
        private static BoardHierarchy.HourCell HourCellOf(MainWindow host, int h)
        {
            SessionsSnapshot d = host.Data;
            if (d == null || !d.Ok) return new BoardHierarchy.HourCell { Hour = h };
            return BoardHierarchy.HourlyDistribution(d).Hours[h];
        }

        /// <summary>条形比例（相对本窗口内最大的那一小时 ✓ 纯呈现 ✓ &lt;0 = 该小时没有活动 ⇒ 不画条 ✓）。</summary>
        private static double HourBarPercent(BoardHierarchy.Hourly hy, int h)
        {
            if (hy == null) return -1;
            long max = 0;
            for (int i = 0; i < 24; i++) if (hy.Hours[i].Sessions > max) max = hy.Hours[i].Sessions;
            if (max <= 0) return -1;
            if (hy.Hours[h].Sessions <= 0) return -1;
            return hy.Hours[h].Sessions * 100.0 / max;
        }

        /// <summary>该小时输入 token 文案（**整会话累计** ✓ 全未知 ⇒「未知」✗ 绝不显 0 ✓）。
        /// 取值住在 <see cref="BoardHierarchy.HourTokenText"/>（纯函数 ✓ LogicTests 钉死 ✓ 界面只调它 ✓）。</summary>

        // ================================================================ ③ 最近活动流（提案 §3③）

        /// <summary>最近活动流（2026-10-26 · 提案 §3③ ✓✓）：按时间列最近 N 条会话活动 ✓
        /// **只给 id 短码 + 时间 + token** ✗ —— ✗ **绝不含会话标题 / 对话正文 / 工具参数** ✗（数据最小化 ✓）。
        /// 数据源与总计卡 / 三档表 / Top 5 **同一集合**（eligible ∩ SESSION 行 ✓ 同一次 CLI 取数 ⇒ 同源 ✓ 差 0 ✓）；
        /// 用量未知 ⇒「用量未知」✗ 绝不显 0 ✓；固定 N 行 ⇒ 字段级原地换字 ✗ 不整页重建 ✓。</summary>
        private static Control BoardActivityCard(MainWindow host)
        {
            StackPanel c = new StackPanel { Spacing = 4 };
            c.Children.Add(T("最近活动（当前窗口）", 13, Palette.Text, FontWeight.Bold));
            TextBlock state = LvLine(host, 11.5, Palette.Warn, FontWeight.Normal, delegate
            {
                SessionsSnapshot d = host.Data;
                return (d == null || !d.Ok) ? "会话活动：未知（读不到会话投影）" : "";
            });
            state.IsVisible = state.Text != null && state.Text.Length > 0;
            host.Live(delegate { state.IsVisible = state.Text != null && state.Text.Length > 0; });
            c.Children.Add(state);
            StackPanel rows = new StackPanel { Spacing = 2 };
            for (int i = 0; i < ActivityCardRows; i++)
            {
                int idx = i;
                TextBlock row = LvLine(host, 11.5, Palette.TextDim, FontWeight.Normal,
                    delegate { return ActivityLineAt(host, idx); });
                rows.Children.Add(row);
            }
            c.Children.Add(rows);
            TextBlock fn = T(BoardHierarchy.ActivityFootnote, 10.5, Palette.TextFaint);
            fn.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(fn);
            return Card(c, new Thickness(0), new Thickness(18, 8));
        }

        /// <summary>活动流固定列几行（固定行数 ⇒ 字段级刷新能原地换字 ✓ ✗ 不整页重建 ✓）。</summary>
        private const int ActivityCardRows = 8;

        /// <summary>活动流第 idx 行（建树即算 ✓ 之后每拍重算 ✓ 单一出处 ✓）。</summary>
        private static string ActivityLineAt(MainWindow host, int idx)
        {
            List<BoardHierarchy.ActivityRow> r = BoardHierarchy.RecentActivity(host.Data, ActivityCardRows);
            return idx >= 0 && idx < r.Count ? BoardHierarchy.ActivityLine(r[idx]) : "";
        }


        /// <summary>看板结构签名：数据 ok 态 + 血缘态（决定口径 chips 可点/静态两形态 ✓）。没变 = 纯字段刷新就够 ✓。</summary>
        private static string BoardStructSig(SessionsSnapshot d)
        {
            return (d == null ? "null" : d.Ok ? "ok" : "fail") + "|" + (d != null && d.LineageOk ? "lin" : "nolin");
        }

        /// <summary>回执细条（看板重排 §2-6 ✓✓）：半高细条承载操作回执/运行提示 ✗ 不再占整卡 ✓。
        /// ★ 看板体验修复（2026-10-11 ✓✓）：**常显** ✓ 空状态「暂无操作回执」占住高度 ✗ 不许整卡消失 ✓；
        ///   文字走 LiveText ✓ NoteBoard 写完原地换 ✗ 不再等整页重建 ✓。</summary>
        private static Control ReceiptStrip(MainWindow host)
        {
            TextBlock tb = host.LiveText(ReceiptText(host.ActionLog), delegate { return ReceiptText(host.ActionLog); });
            tb.FontSize = 11.5; tb.Foreground = Palette.TextDim;
            return new Border
            {
                Background = Palette.CardHover,
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(12, 4),   // ★ 体验修复：纵向压一档（一屏 ✓ 常显 ✓）
                Child = tb
            };
        }

        private static string ReceiptText(string log)
        {
            return string.IsNullOrEmpty(log)
                ? "暂无操作回执 —— 侧栏底部可以一键启动 / 停止 dsh；结果会显示在这里并弹 toast。"   // ★ 空状态常显 ✗ 不消失 ✓
                : log;
        }

        /// <summary>看板筛选条（看板第一批 · 规格 §4.1 ✓✓；第三批 2026-10-09 ✓✓ 规格 §11.7-E ✓✓；重排 2026-10-09 ✓✓ §2-3/§2-4 ✓✓）：
        /// 窗口档按钮 + 「自定义…」（D4 ✓）+ 口径 chips（lineage=ok ⇒ 三档全解锁 ✓ 否则血缘两档静态形态 + 「需桥插件血缘」✓）
        /// + fork 口径差「？」入口（B.5 ✓ 文案逐字 ✓）+ C.4 脚注（**每屏恰好一次，就印在这里** ✓ 重排 §1 ✓✓ 图内/卡内不再重复 ✓）。
        /// 两个子标签都挂这条 ✓ 口径入口一处可见 ✓。
        /// ★ 弃用 Flyout · 改**内联展开块**（2026-10-11 ✓✓）：说明块就在筛选行正下方（同一列 ✓ 宽度 = 页面宽度 ✓
        /// ✗ 无 MaxWidth/Width 定值 ✓）✓ 默认收起（不占高度 ✓）✓ 点击切换走字段刷新（`host.BoardHelpOpen` ✓）✓。</summary>
        private static Control BoardFilterBar(MainWindow host)
        {
            StackPanel col = new StackPanel { Spacing = 8 };
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ShapeTokens.S8 };
            int[] opts = new int[] { 0, 7, 14, 30 };
            string[] names = new string[] { "总计", "近 7 天", "近 14 天", "近 30 天" };
            for (int oi = 0; oi < opts.Length; oi++)
            {
                int dd = opts[oi];
                bool on = host.BoardDays == dd && host.BoardFrom == null;   // 自定义区间生效 ⇒ 预设档都不亮 ✓
                Button rb = new Button
                {
                    Content = T(names[oi], 11.5, on ? Palette.OnAccent : Palette.TextDim),
                    Background = on ? Palette.Accent : Palette.CardHover,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                    Padding = new Thickness(12, 5)
                };
                rb.Click += delegate { host.BoardDays = dd; host.RefreshBoard(); };   // ★ 体验修复：设档 ⇒ 清自定义区间 ✓ 本地高亮**先换** ✓ 再静默拉 ✗ 不整页重建 ✓
                // ★ 看板体验修复（2026-10-11 ✓✓）：高亮走字段刷新 —— 点击瞬换 ✓（自定义区间生效 ⇒ 预设档都不亮 ✓ 同一规则 ✓）
                TextBlock rbt = (TextBlock)rb.Content;
                host.Live(delegate
                {
                    bool on2 = host.BoardDays == dd && host.BoardFrom == null;
                    IBrush bg = on2 ? Palette.Accent : Palette.CardHover;
                    if (!ReferenceEquals(rb.Background, bg)) rb.Background = bg;
                    IBrush fg = on2 ? Palette.OnAccent : Palette.TextDim;
                    if (!ReferenceEquals(rbt.Foreground, fg)) rbt.Foreground = fg;
                });
                // UIA/无障碍名（截图脚本按名字找按钮 ✓ 读屏也能念出来 ✓ 与可见文案一致 ✓）
                global::Avalonia.Automation.AutomationProperties.SetName(rb, names[oi]);
                row.Children.Add(rb);
            }
            // 「自定义…」（D4 ✓ 规格 §11.7-E-4 ✓✓）：弹窗一次输两个日期 ⇒ 本地校验（✗ 不猜 ✗ 不补端点 ✓）⇒ --from/--to ✓
            bool customOn = host.BoardFrom != null && host.BoardTo != null;
            Button cb = new Button
            {
                Content = T(customOn ? "自定义：" + host.BoardFrom + "~" + host.BoardTo : "自定义…", 11.5, customOn ? Palette.OnAccent : Palette.TextDim),
                Background = customOn ? Palette.Accent : Palette.CardHover,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(12, 5)
            };
            cb.Click += async delegate
            {
                string typed = await PromptDialog.Ask(host, "自定义日期区间", "请输入起止两个日期（空格或逗号分隔）", "yyyy-MM-dd yyyy-MM-dd", "");
                if (typed == null || typed.Trim().Length == 0) return;   // 取消 ⇒ 不动 ✓
                string f2; string t2; string err2;
                if (!SessionsMarkers.TryParseCustomRange(typed, out f2, out t2, out err2))
                {
                    host.NoteBoard("自定义日期区间未生效：" + err2 + "（不会自动补端点；重新点「自定义…」再输）");
                    host.RefreshBoard();   // ★ 体验修复：回执条/toast 即时 ✓ 不再整页重建 ✓
                    return;
                }
                host.SetBoardCustomRange(f2, t2);
                host.RefreshBoard();   // ★ 体验修复：chip 文案先换 ✓ 再静默拉 ✓
            };
            global::Avalonia.Automation.AutomationProperties.SetName(cb, "自定义…");
            // ★ 看板体验修复（2026-10-11 ✓✓）：自定义 chip 文案/高亮走字段刷新 —— 设了区间立即亮「自定义：from~to」✓
            TextBlock cbt = (TextBlock)cb.Content;
            host.Live(delegate
            {
                bool c2 = host.BoardFrom != null && host.BoardTo != null;
                string t2 = c2 ? "自定义：" + host.BoardFrom + "~" + host.BoardTo : "自定义…";
                if (cbt.Text != t2) cbt.Text = t2;
                IBrush bg = c2 ? Palette.Accent : Palette.CardHover;
                if (!ReferenceEquals(cb.Background, bg)) cb.Background = bg;
                IBrush fg = c2 ? Palette.OnAccent : Palette.TextDim;
                if (!ReferenceEquals(cbt.Foreground, fg)) cbt.Foreground = fg;
            });
            row.Children.Add(cb);
            // 口径选择器（第三批 ✓✓ 规格 §11.7-E-1 ✓✓：lineage=ok ⇒ 三档全可点；lineage=none ⇒ 血缘两档保持不可点静态形态
            //   + 「需桥插件血缘」✗ 不做灰按钮假象 ✓ 沿用第一批形态纪律 ✓；高亮以 SESSWIN_META 实发 level 为准 ✓✓）
            row.Children.Add(new Border { Width = 1, Height = 18, Background = Palette.TextFaint, Opacity = 0.4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) });
            row.Children.Add(T("口径：", 11.5, Palette.TextDim));
            SessionsSnapshot bd0 = host.Data;
            string effLevel = (bd0 != null && bd0.WinLevel != null && bd0.WinLevel.Length > 0 && bd0.WinLevel != "unknown") ? bd0.WinLevel : "global";
            bool lineageOn = bd0 != null && bd0.LineageOk;
            if (lineageOn)
            {
                row.Children.Add(BoardScopeButton(host, "全局", "global", effLevel == "global"));
                row.Children.Add(BoardScopeButton(host, "仅父会话", "parents", effLevel == "parents"));
                row.Children.Add(BoardScopeButton(host, "父会话+子代理", "parents_sub", effLevel == "parents_sub"));
            }
            else
            {
                row.Children.Add(BoardScopeChip("全局", effLevel == "global"));
                row.Children.Add(BoardScopeChip("仅父会话（需桥插件血缘）", false));
                row.Children.Add(BoardScopeChip("父会话+子代理（需桥插件血缘）", false));
            }
            // ★ 看板重排（2026-10-09 ✓✓ 规格 §2-4 ✓✓）：B.5 fork 口径差说明从裸文本行收进「口径」旁的「？」入口 ✓✓
            //   入口显眼（描边强调 ✓ 悬浮提示 ✓ 无障碍名 ✓）；文案 = SessionsMarkers.ForkDiffText 逐字 ✗ 一字未改 ✓
            //   （「相差 N 个会话」N 实时取 SESSWIN_META fork_sessions ✓ 取不到 ⇒ unknown ✓ 0 照显 ✓ D-7 ✓）；
            //   可见条件与旧裸行完全相同（lineage=ok 且 level ∈ {global, parents_sub} ✓）—— 只换呈现 ✗ 不改口径 ✓
            // ★ 看板体验修复（2026-10-11 ✓✓）：「？口径差」入口**常驻容器**，显隐走字段刷新 ✓（不再靠整页重建出现/消失 ✓）
            //   可见条件与旧版逐字相同（lineage=ok 且 level ∈ {global, parents_sub} ✓）—— 只换呈现 ✗ 不改口径 ✓
            // ★★ 弃用 Flyout · 改内联展开块（2026-10-11 ✓✓ 维护者两次反馈后换方案 ✓✓）：
            //   ✗ 弹层（Flyout/ToolTip）不可用：呈现器**自带一层宽度限制** ✗ → 内容上定宽（ft.Width/fcard.Width）也没用 ✓
            //     实测：MaxWidth ⇒ 换行处**吞字**（丢「话，但」✗）；改定宽 ⇒ 出现**滚动条**且字仍被裁 ✗✗ 两次都没修对 ✓
            //   ✓ 现在：说明块**内联**在筛选条正下方（同一列 ✓ 宽度 = 页面宽度 ✓）→ TextWrapping.Wrap 有确定宽度可用 ✓✓
            //     ✗ 全块无任何 Width/MaxWidth 定值 hack ✓ 页面宽度天然确定 ⇒ 不可能裁字 ✓ 也绝不会出滚动条 ✓✓
            //   默认**收起**（IsVisible=false ⇒ **不占高度** ✗ 不留空白占位 ✓）；展开 ⇒ 允许把下方内容往下推 ✓（规格第 6 条 ✓）
            TextBlock qt = T("？口径差", 11.5, Palette.Accent);
            Button qb = new Button
            {
                Content = qt,
                Background = Palette.CardHover,
                BorderBrush = Palette.Accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(10, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel fc = new StackPanel { Spacing = ShapeTokens.S8 };
            fc.Children.Add(T("为什么「全局」和「父会话+子代理」数字不一样？", 13, Palette.Text, FontWeight.SemiBold));
            // 「相差 N 个会话」N 实时取 SESSWIN_META fork_sessions ✓ 取不到 ⇒ unknown ✓ 0 照显 ✓ D-7 ✓（LiveText ✓ 文案 = ForkDiffText 逐字 ✓）
            TextBlock ft = host.LiveText(
                SessionsMarkers.ForkDiffText(bd0 != null ? bd0.ForkSessions : -1),
                delegate { SessionsSnapshot dd = host.Data; return SessionsMarkers.ForkDiffText(dd != null ? dd.ForkSessions : -1); });
            ft.FontSize = 11.5; ft.Foreground = Palette.TextDim;
            ft.TextWrapping = TextWrapping.Wrap;   // ✗ 不再设 Width/MaxWidth ✓（宽度由「筛选条这一列的页面宽度」决定 ✓✓）
            fc.Children.Add(ft);
            Border help = Card(fc, new Thickness(0), new Thickness(14, 10));
            qb.Click += delegate { host.ToggleBoardHelp(); };   // ★ 点击 = 切换下方内联说明块显隐 ✓ 走字段刷新 ✗ 不整页重建 ✓
            global::Avalonia.Automation.AutomationProperties.SetName(qb, "口径：fork 说明");
            global::Avalonia.Automation.AutomationProperties.SetHelpText(qb, "展开/收起 fork 口径差说明（内联说明块）");
            Border qbHost = new Border { Child = qb };
            // ★ 唯一刷新点：显隐 + 按钮文案都在这一个字段里重算 ✓（`Live` = 字段级刷新注册表 ✓
            //   ToggleBoardHelp / 自动刷新那一拍都会跑它 ⇒ 原地换 ✗ 不碰视觉树 ✓ 不 BuildShell ✓ 不 RebuildBoard ✓）
            host.Live(delegate
            {
                SessionsSnapshot d0 = host.Data;
                bool v = d0 != null && d0.Ok && d0.HasWinMeta && d0.LineageOk
                    && (d0.WinLevel == "global" || d0.WinLevel == "parents_sub");
                if (qbHost.IsVisible != v) qbHost.IsVisible = v;
                bool open = host.BoardHelpOpen;
                string label = open ? "？口径差（收起）" : "？口径差";   // 文案反映状态 ✓（收起态 = 会执行收起动作 ✓）
                if (qt.Text != label) qt.Text = label;
                if (help.IsVisible != open) help.IsVisible = open;   // 收起 ⇒ false ⇒ 不占高度 ✗ 不留空白 ✓
                // 按钮是可切换入口 ⇒ 无障碍名也跟着状态变 ✓（✗ 不写死"说明"两个字 ✓ 读屏能念出当前动作 ✓）
                string an = open ? "口径：fork 说明（展开中，点击收起）" : "口径：fork 说明（点击展开）";
                if (global::Avalonia.Automation.AutomationProperties.GetName(qb) != an)
                {
                    global::Avalonia.Automation.AutomationProperties.SetName(qb, an);
                }
            });
            qbHost.IsVisible = bd0 != null && bd0.Ok && bd0.HasWinMeta && bd0.LineageOk
                && (bd0.WinLevel == "global" || bd0.WinLevel == "parents_sub");
            help.IsVisible = host.BoardHelpOpen;   // 默认收起 ✓（重建后也与当前展开态一致 ✓）
            row.Children.Add(qbHost);
            col.Children.Add(row);
            // 内联说明块**紧跟筛选行、同一列**（宽度 = 页面宽度 ✓）；✗ 不是弹层 ✗ 不是独立一行固定高度 ✓
            col.Children.Add(help);
            // C.4 固定脚注（**每屏恰好一次，就印在这里** ✓ 重排 §1 ✓✓）：显隐走字段刷新 ✓ 文案逐字未动 ✓
            TextBlock c4 = host.LiveText(
                (bd0 != null && bd0.HasWinMeta && bd0.WinTruth0) ? SessionsMarkers.TruthFootnote : "",
                delegate { SessionsSnapshot dd = host.Data; return (dd != null && dd.HasWinMeta && dd.WinTruth0) ? SessionsMarkers.TruthFootnote : ""; });
            c4.FontSize = 11.5; c4.Foreground = Palette.TextFaint;
            col.Children.Add(c4);
            return Card(col, new Thickness(0), new Thickness(14, 6));   // ★ 体验修复：纵向压一档（一屏 ✓ 内容不变 ✓）
        }

        /// <summary>口径 chip（静态标签 ✗ 不是按钮 ✓ 血缘缺失时血缘两档永远点不了 —— 用不可点的形态，不做「灰按钮」假象 ✓）。</summary>
        private static Control BoardScopeChip(string text, bool active)
        {
            return new Border
            {
                Background = active ? Palette.Accent : Palette.CardHover,
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(12, 5),
                VerticalAlignment = VerticalAlignment.Center,
                Child = T(text, 11.5, active ? Palette.OnAccent : Palette.TextFaint)
            };
        }

        /// <summary>口径 chip 的可点形态（第三批 · 2026-10-09 ✓✓ 规格 §11.7-E-1 ✓✓）：
        /// lineage=ok ⇒ 三档都是真按钮 ✓ 点击 ⇒ host.BoardLevel = 档 → 重跑 CLI ✓（与窗口档按钮同形制 ✓）。</summary>
        private static Control BoardScopeButton(MainWindow host, string text, string level, bool active)
        {
            Button b = new Button
            {
                Content = T(text, 11.5, active ? Palette.OnAccent : Palette.TextDim),
                Background = active ? Palette.Accent : Palette.CardHover,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(12, 5)
            };
            // ★ 看板体验修复（2026-10-11 ✓✓）：高亮仍**以 CLI 实发 WinLevel 为准**（诚实 ✓ 不预测 ✓）——
            //   点击后静默拉数，数据一到字段刷新换高亮 ✓ 不再整页重建 ✓
            TextBlock tc = (TextBlock)b.Content;
            host.Live(delegate
            {
                SessionsSnapshot d0 = host.Data;
                string eff = (d0 != null && !string.IsNullOrEmpty(d0.WinLevel) && d0.WinLevel != "unknown") ? d0.WinLevel : "global";
                bool on2 = eff == level;
                IBrush bg = on2 ? Palette.Accent : Palette.CardHover;
                if (!ReferenceEquals(b.Background, bg)) b.Background = bg;
                IBrush fg = on2 ? Palette.OnAccent : Palette.TextDim;
                if (!ReferenceEquals(tc.Foreground, fg)) tc.Foreground = fg;
            });
            b.Click += delegate { host.BoardLevel = level; host.RefreshBoard(); };   // ★ 体验修复：静默拉 ✗ 不整页重建 ✓
            global::Avalonia.Automation.AutomationProperties.SetName(b, "口径：" + text);
            return b;
        }

        /// <summary>总计卡（★ 看板重排 2026-10-09 ✓✓ 规格 §2-5 ✓✓）：**固定总计块 + 窗口总计块合一** ——
        /// 旧版两张卡并排 ✗ 各印一遍 C.4 脚注 ✗ 首屏拥挤；现在一卡两节（细分隔线）✓ 首屏少一层 ✓。
        /// 文案**逐字保留**：「不随上方筛选变化」「右端不含 · 时区」「不假装 0」「不进窗口（不猜日期 ✓ 已计入上方总计卡 ✓）」一字未丢 ✓✓
        /// （合并后总计块仍在窗口块上方 ✓ 那句"已计入上方总计卡"依然成立 ✓）；
        /// C.4 脚注**不在卡内重复** —— 筛选条已印、每屏恰好一次（规格 §1 原话：全页只出现一次 ✓✓ 文案未动 ✓ 只挪位置 ✓）。
        /// 数据面不变：固定块 = SESSAGG_TOTAL scope=store（无视窗口过滤 ✓ 规格 §4.2 ✓✓）；
        /// 窗口块 = SESSAGG_SESSION 行（= eligible 集合 ✓）GUI 侧求和（规格 §4.1 ✓✓ 自定义区间 days=unknown 也显 ✓ D4 ✓）。
        /// 任一块可用即渲染 ✓ 都不可用 → 不出卡 ✓。
        /// ★ 看板体验修复（2026-10-11 ✓✓）：文案逐字保留 ✓ 只是改成**字段级取值**（LiveText ✓ 取值函数 ✗ 不是快照 ✓）
        ///   + 节/卡显隐走 IsVisible ✓ —— 切档/切口径不再整页重建 ✓ 数字原地换 ✓。</summary>
        private static Control BoardTotalsCard(MainWindow host)
        {
            SessionsSnapshot d0 = host.Data;
            bool hasTotal0 = d0 != null && d0.HasAggTotal;
            bool hasWin0 = d0 != null && d0.HasWinMeta && (d0.WinDays > 0 || d0.WinFrom != "-");   // 与旧两卡的出现条件分别逐字相同 ✓
            StackPanel c = new StackPanel { Spacing = 4 };   // ★ 体验修复：卡内节奏压一档（一屏放下 ✓ 文案不变 ✓）

            // —— 固定总计块：无视窗口过滤 ✓ scope=store ✓（与旧 BoardTotalCard 逐字一致 ✓）——
            StackPanel secT = new StackPanel { Spacing = 4 };
            secT.Children.Add(LvLine(host, 13, Palette.Text, FontWeight.Bold, delegate
            {
                SessionsSnapshot d = host.Data;
                return "总计（全部 " + (d != null ? d.AggSessions : 0) + " 个会话 · 不随上方筛选变化）";
            }));
            secT.Children.Add(LvLine(host, 12, Palette.TextDim, FontWeight.Normal, delegate
            {
                SessionsSnapshot d = host.Data;
                long un = d != null ? d.AggUncached : 0, cr = d != null ? d.AggCacheRead : 0;
                long cw = d != null ? d.AggCacheWrite : 0, ou = d != null ? d.AggOutput : 0;
                return "输入 " + SessionRow.Human(un + cr)
                    + " token（其中缓存读 " + SessionRow.Human(cr) + " · 缓存写 " + SessionRow.Human(cw) + "）"
                    + "　输出 " + SessionRow.Human(ou) + " token";
            }));
            secT.Children.Add(LvLine(host, 11.5, Palette.TextFaint, FontWeight.Normal, delegate
            {
                SessionsSnapshot d = host.Data;
                string span = (d != null && d.AggFirstDay.Length > 0 && d.AggFirstDay != "unknown" && d.AggLastDay.Length > 0 && d.AggLastDay != "unknown")
                    ? d.AggFirstDay + " ~ " + d.AggLastDay : "未知";
                return "非空会话 " + (d != null ? d.AggNonBlank : 0) + " 个 · 活动跨度（本地日期）" + span
                    + " · token 未知的会话未计入求和";
            }));
            Border secTHost = new Border { Child = secT, IsVisible = hasTotal0 };
            host.Live(delegate
            {
                SessionsSnapshot d = host.Data;
                bool v = d != null && d.HasAggTotal;
                if (secTHost.IsVisible != v) secTHost.IsVisible = v;
            });
            c.Children.Add(secTHost);

            // 两节分隔线 ✓
            Border div = new Border { Height = 1, Background = Palette.Border, Opacity = 0.5, Margin = new Thickness(0, 2), IsVisible = hasTotal0 && hasWin0 };
            host.Live(delegate
            {
                SessionsSnapshot d = host.Data;
                bool v = d != null && d.HasAggTotal && d.HasWinMeta && (d.WinDays > 0 || d.WinFrom != "-");
                if (div.IsVisible != v) div.IsVisible = v;
            });
            c.Children.Add(div);

            // —— 窗口总计块：eligible 集 GUI 侧求和 ✓（与旧 BoardWindowCard 逐字一致 ✓✓ 求和走 BoardMetrics.SumOwnRows ✓ 与三档表同一纯函数 ✓）——
            StackPanel secW = new StackPanel { Spacing = 4 };
            secW.Children.Add(LvLine(host, 13, Palette.Text, FontWeight.Bold, delegate
            {
                SessionsSnapshot d = host.Data;
                if (d == null) return "";
                // 自定义区间 ⇒ 标题改显「自定义区间 from~to」（D4 ✓ 规格 §11.7-E-4 ✓✓）；窗口档 ⇒ 原「近 N 天」不动 ✓
                return d.WinDays > 0
                    ? "近 " + d.WinDays + " 天窗口总计（" + d.WinEligible + " 个会话有活动）"
                    : "自定义区间 " + d.WinFrom + "~" + d.WinTo + " 窗口总计（" + d.WinEligible + " 个会话有活动）";
            }));
            secW.Children.Add(LvLine(host, 12, Palette.TextDim, FontWeight.Normal, delegate
            {
                BoardMetrics.LevelSum s = BoardMetrics.SumOwnRows(host.Data);
                return "输入 " + SessionRow.Human(s.In)
                    + " token（其中缓存读 " + SessionRow.Human(s.CacheRead) + " · 缓存写 " + SessionRow.Human(s.CacheWrite) + "）"
                    + "　输出 " + SessionRow.Human(s.Output) + " token　轮次 " + s.Turns;
            }));
            secW.Children.Add(LvLine(host, 11.5, Palette.TextFaint, FontWeight.Normal, delegate
            {
                SessionsSnapshot d = host.Data;
                if (d == null) return "";
                string range = (d.WinFrom != "-" && d.WinTo != "-") ? d.WinFrom + " ~ " + d.WinTo + "（右端不含 · 时区 " + (d.WinTz.Length > 0 ? d.WinTz : "未知") + "）" : "窗口范围未知";
                string miss = d.WinUnknownLast > 0
                    ? " · 另有 " + d.WinUnknownLast + " 个会话没有最后活动时间 → 不进窗口（已计入上方总计卡）"
                    : "";
                BoardMetrics.LevelSum s = BoardMetrics.SumOwnRows(d);
                string unk = s.TokUnknown > 0
                    ? " · " + s.TokUnknown + " 个会话 token 未知（未计入求和）"
                    : "";
                return "窗口：" + range + miss + unk;
            }));
            Border secWHost = new Border { Child = secW, IsVisible = hasWin0 };
            host.Live(delegate
            {
                SessionsSnapshot d = host.Data;
                bool v = d != null && d.HasWinMeta && (d.WinDays > 0 || d.WinFrom != "-");
                if (secWHost.IsVisible != v) secWHost.IsVisible = v;
            });
            c.Children.Add(secWHost);

            Control card = Card(c, new Thickness(0), new Thickness(18, 8));   // ★ 体验修复：纵向压一档（一屏 ✓ 文案不变 ✓）
            card.IsVisible = hasTotal0 || hasWin0;   // 任一块可用即渲染 ✓ 都不可用 → 不显示 ✓（条件逐字同旧 ✓）
            host.Live(delegate
            {
                SessionsSnapshot d = host.Data;
                bool v = d != null && (d.HasAggTotal || (d.HasWinMeta && (d.WinDays > 0 || d.WinFrom != "-")));
                if (card.IsVisible != v) card.IsVisible = v;
            });
            return card;
        }

        /// <summary>成本卡（2026-10-16 v3.2 ✓ 规格 §3 + §3.1 ✓）：**看板「指标」页顶部** ✓
        /// 「今日 · 当前窗口 · 会话明细」三块 ✓ 数据 = **同一次** `overview` 输出里的 COST_* 两块
        /// （`scope=today` / `scope=window` ✓）—— ✗ 不为成本再起一个 CLI 进程 ✓（规格 §4 ✓）。
        /// 纪律（逐条 ✓）：未配价 → 不显示任何金额、只报 token + 逐字提示句 ✓；金额一律标「估算」✓ 只显示 USD ✓；
        /// 超限只提示 ✗ 不阻止操作 ✓；今日/窗口**各取各的块** ✗ 缺块 → 如实说该块不可用（✗ 绝不串档 ✓）；
        /// N（无锚点排除数 / 模型未知数）只取 COST_META ✓ GUI 不自己数 ✓；
        /// 刷新全部走**字段级** Live / LiveText ✓ ✗ 不整页重建 ✓。</summary>
        private static Control BoardCostCard(MainWindow host)
        {
            StackPanel c = new StackPanel { Spacing = 6 };

            // —— 卡头：标题 + 「估算」标签 ——
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            TextBlock title = T("成本估算", 13, Palette.Text, FontWeight.Bold);
            Grid.SetColumn(title, 0);
            head.Children.Add(title);
            Control tag = Chip("估算 · 非官方账单", Palette.Warn, Palette.WarnSoft);
            Grid.SetColumn(tag, 2);
            head.Children.Add(tag);
            c.Children.Add(head);
            TextBlock sub = T("单价来自你的设置（设置页「成本估算」）。界面只显示美元，不做汇率换算；金额一律是估算，不是 DeepSeek 的官方账单。", 11.5, Palette.TextFaint);
            sub.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(sub);

            // —— 空态 / 失败态（没有成本标记行时只留这一行 ✓ ✗ 不装作 0 ✗ 不画假卡 ✓）——
            // ★★ 真机缺陷修复（2026-10-16 增量 ✓）：初始值必须**建树时就算** ✗ 不能给 "" 等下一拍 ✗
            //   —— 首绘之后若没有 RefreshLiveFields（版本早已取到 / 三档拉取没回来）那些行会一直空着 ✓
            //      （极小单价态截图抓到：预算行 + 会话明细整块缺失 ✓）；取值函数仍是唯一出处 ✓
            TextBlock state = host.LiveText(CostStateText(host), delegate { return CostStateText(host); });
            state.FontSize = 11.5; state.Foreground = Palette.Warn; state.TextWrapping = TextWrapping.Wrap;
            state.IsVisible = state.Text != null && state.Text.Length > 0;
            host.Live(delegate { state.IsVisible = state.Text != null && state.Text.Length > 0; });
            c.Children.Add(state);

            // —— 正文（有成本标记行才出现 ✓）——
            StackPanel body = new StackPanel { Spacing = 4 };
            CostSnapshot cs0 = host.Cost;
            body.IsVisible = cs0 != null && cs0.HasCost;
            host.Live(delegate { CostSnapshot cs = host.Cost; body.IsVisible = cs != null && cs.HasCost; });

            // ★ 看板层级重整（2026-10-26 ✓）：超限提示条**已搬到常显的「今天用了多少」卡** ✓
            //   ✗ 原来它只在本卡里，而本卡现在收在折叠区（默认收起）⇒ 会把警示藏起来 ✗✓
            //   文案仍走 `CostMarkers.OverBudgetText`（唯一出处 ✓ 只提示 ✗ 不阻止任何操作 ✓）

            // ② 今日（scope=today ✓ 含预算 ✓）
            body.Children.Add(T("今日", 12, Palette.Text, FontWeight.SemiBold));
            TextBlock tTok = LvLine(host, 11.5, Palette.TextDim, FontWeight.Normal, delegate { return CostMarkers.TodayTokensLine(CostToday(host)); });
            tTok.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(tTok);
            TextBlock tMoney = LvLine(host, 11.5, Palette.Text, FontWeight.SemiBold, delegate { return CostMarkers.TodayMoneyLine(CostToday(host)); });
            tMoney.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(tMoney);
            // 今日卡脚注（三要素 ✓ N = COST_META unknown_last ✓ 缺块 → 不显示脚注 ✓）
            TextBlock foot = LvLine(host, 10.5, Palette.TextFaint, FontWeight.Normal, delegate
            {
                CostBlock tb = CostToday(host);
                return tb == null ? "" : CostMarkers.TodayFootnote(tb.UnknownLast);
            });
            foot.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(foot);
            // 预算行（未设上限 → 空 ✓ 折叠 ✓）+ 非 global 档必标的「预算按全局计」✓
            TextBlock bud = LvLine(host, 11.5, Palette.TextDim, FontWeight.Normal, delegate
            {
                string line = CostMarkers.BudgetLine(host.Cost);
                if (line.Length == 0) return "";
                string scope = CostMarkers.BudgetScopeLine(host.Cost, CostWindow(host));
                return scope.Length == 0 ? line : line + "　· " + scope;
            });
            bud.TextWrapping = TextWrapping.Wrap;
            bud.IsVisible = bud.Text != null && bud.Text.Length > 0;
            host.Live(delegate { bud.IsVisible = bud.Text != null && bud.Text.Length > 0; });
            body.Children.Add(bud);

            // ③ 当前窗口（scope=window ✓ 含会话明细 ✓）
            body.Children.Add(T("当前窗口", 12, Palette.Text, FontWeight.SemiBold));
            TextBlock wTok = LvLine(host, 11.5, Palette.TextDim, FontWeight.Normal, delegate { return CostMarkers.WindowTokensLine(CostWindow(host)); });
            wTok.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(wTok);
            TextBlock wMoney = LvLine(host, 11.5, Palette.Text, FontWeight.SemiBold, delegate { return CostMarkers.WindowMoneyLine(CostWindow(host)); });
            wMoney.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(wMoney);
            // cost=none ≠ 0（配价但有 N 笔不可算 ✓ 计数取 COST_NOTE ✓）
            TextBlock un = LvLine(host, 11, Palette.Warn, FontWeight.Normal, delegate { return CostMarkers.SessionUnavailableLine(host.Cost, CostWindow(host)); });
            un.TextWrapping = TextWrapping.Wrap;
            un.IsVisible = un.Text != null && un.Text.Length > 0;
            host.Live(delegate { un.IsVisible = un.Text != null && un.Text.Length > 0; });
            body.Children.Add(un);

            // ④ 会话明细（token + 金额（若配价）+ 最后使用模型 ✓ 固定 8 行 → 字段级换字/显隐 ✓）
            TextBlock listHead = LvLine(host, 11, Palette.TextFaint, FontWeight.Normal, delegate
            {
                CostBlock w = CostWindow(host);
                if (w == null || w.Sessions.Count == 0) return "";
                int more;
                int take = Math.Min(CostCardRows, w.Sessions.Count);
                CostMarkers.TopSessions(w, CostCardRows, out more);
                string h = "会话明细（当前窗口 · 按" + (w.Priced ? "估算金额" : "用量") + "降序取前 " + take + " 个";
                if (more > 0) h += "，另有 " + more + " 个未列出";
                return h + "）";
            });
            listHead.TextWrapping = TextWrapping.Wrap;
            listHead.IsVisible = listHead.Text != null && listHead.Text.Length > 0;
            host.Live(delegate { listHead.IsVisible = listHead.Text != null && listHead.Text.Length > 0; });
            body.Children.Add(listHead);
            for (int ri = 0; ri < CostCardRows; ri++)
            {
                int idx = ri;
                TextBlock row = host.LiveText(CostRowText(host, idx), delegate { return CostRowText(host, idx); });
                row.FontSize = 11.5; row.Foreground = Palette.TextDim; row.TextWrapping = TextWrapping.Wrap;
                row.IsVisible = row.Text != null && row.Text.Length > 0;
                host.Live(delegate { row.IsVisible = row.Text != null && row.Text.Length > 0; });
                body.Children.Add(row);
            }
            // 模型近似说明（近似 ✓ + unknown 计数 ✓ 取 COST_META model_unknown ✓）
            TextBlock modelNote = LvLine(host, 10.5, Palette.TextFaint, FontWeight.Normal, delegate { return CostMarkers.ModelNoteLine(CostWindow(host)); });
            modelNote.TextWrapping = TextWrapping.Wrap;
            modelNote.IsVisible = modelNote.Text != null && modelNote.Text.Length > 0;
            host.Live(delegate { modelNote.IsVisible = modelNote.Text != null && modelNote.Text.Length > 0; });
            body.Children.Add(modelNote);
            // COST_NOTE 汇总（多值 ✓ 出现即累加 ✓；预算口径 / 不可算笔数有专属行 ✗ 不重复 ✓）
            TextBlock notes = LvLine(host, 10.5, Palette.TextFaint, FontWeight.Normal, delegate
            {
                CostSnapshot cs = host.Cost;
                return cs == null ? "" : CostMarkers.NotesLine(cs.Notes);
            });
            notes.TextWrapping = TextWrapping.Wrap;
            notes.IsVisible = notes.Text != null && notes.Text.Length > 0;
            host.Live(delegate { notes.IsVisible = notes.Text != null && notes.Text.Length > 0; });
            body.Children.Add(notes);

            c.Children.Add(body);
            return Card(c, new Thickness(0), new Thickness(18, 10));
        }

        /// <summary>会话明细最多列几行（固定行数 ⇒ 字段级刷新能原地换字 ✓ ✗ 不整页重建 ✓）。</summary>
        private const int CostCardRows = 8;

        /// <summary>今日块（`scope=today` ✓ 没有 → null ✗ 绝不退回窗口块 ✓）。</summary>
        private static CostBlock CostToday(MainWindow host)
        {
            CostSnapshot cs = host.Cost;
            return cs == null ? null : cs.Today;
        }

        /// <summary>当前窗口块（`scope=window` ✓ 同上 ✗ 不串档 ✓）。</summary>
        private static CostBlock CostWindow(MainWindow host)
        {
            CostSnapshot cs = host.Cost;
            return cs == null ? null : cs.Window;
        }

        /// <summary>成本卡空态/失败态文案（**建树时算一次当初始值** ✓ 之后每拍由 Live 重算 ✓ 单一出处 ✓）。</summary>
        private static string CostStateText(MainWindow host)
        {
            CostSnapshot cs = host.Cost;
            if (cs == null) return CostMarkers.NotProvided;
            if (!string.IsNullOrEmpty(cs.FailReason)) return CostMarkers.FailPrefix + cs.FailReason;
            if (!cs.HasCost) return CostMarkers.NotProvided;
            return "";
        }

        /// <summary>成本卡会话明细第 idx 行（同上：**建树即算** ✗ 不给空初始值 ✗ —— 否则没有下一拍就一直空着 ✓）。</summary>
        private static string CostRowText(MainWindow host, int idx)
        {
            CostBlock w = CostWindow(host);
            int more;
            List<CostSessionRow> top = CostMarkers.TopSessions(w, CostCardRows, out more);
            return idx >= 0 && idx < top.Count ? CostMarkers.SessionLine(top[idx], w) : "";
        }

        /// <summary>样式化 LiveText（看板体验修复 2026-10-11 ✓✓）：取值函数 ✗ 不是快照 ✓；样式逐项对齐原静态版本 ✓。</summary>
        private static TextBlock LvLine(MainWindow host, double size, IBrush fg, FontWeight w, Func<string> f)
        {
            TextBlock t = host.LiveText(f(), f);
            t.FontSize = size; t.Foreground = fg; t.FontWeight = w;
            return t;
        }

        /// <summary>★ 三档对比表（2026-10-11 看板体验修复 ✓✓ 规格 docs\看板体验修复-派单-Kimi-2026-10-11.md ✓✓）：
        /// 行 = 全局 / 仅父会话 / 父会话+子代理 ✓ 列 = 口径 / 会话 / 输入（合计（未命中 A + 缓存 B）单行 ✓）/ 输出 / 备注 ✓。
        /// 数据源：当前档复用本页同一响应 ✓ 其余档后台各跑一次 `sessions<同窗口> --level X`（MainWindow.EnsureBoardLevelAggs ✓ 4s 节奏 ✓ 窗键校验 ✓）；
        /// 数字 = `SESSAGG_SESSION bucket=own` 求和（BoardMetrics.SumOwnRows ✓ 与总计卡同一纯函数 ✓ HasTokens=false 不计入 ✗ 不假装 0 ✓）。
        /// 降级如实：未回来 → 「读取中…」✗ 不编 ✓；血缘不可用 → 「需桥插件血缘」✓；老 CLI → 「当前数据源未提供」✓；拉取失败 → 「未知（不猜 ✓）」✓。
        /// fork 排除说明在「父会话+子代理」行备注 ✓（fork 口径差全文在筛选条「？口径差」✗ 一字未删 ✓）。</summary>
        private static Control BoardLevelCompareCard(MainWindow host)
        {
            StackPanel c = new StackPanel { Spacing = 4 };   // ★ 体验修复：卡内节奏压一档（一屏 ✓）
            c.Children.Add(T("三档口径对比（同一窗口 · 并排）", 13, Palette.Text, FontWeight.Bold));
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("110,60,*,110,170"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
            AddLvCell(g, T("口径", 11, Palette.TextFaint), 0, 0);
            AddLvCell(g, T("会话", 11, Palette.TextFaint), 0, 1);
            AddLvCell(g, T("输入 token", 11, Palette.TextFaint), 0, 2);
            AddLvCell(g, T("输出 token", 11, Palette.TextFaint), 0, 3);
            AddLvCell(g, T("备注", 11, Palette.TextFaint), 0, 4);
            string[] levels = new string[] { "global", "parents", "parents_sub" };
            string[] names = new string[] { "全局", "仅父会话", "父会话+子代理" };
            for (int i = 0; i < levels.Length; i++)
            {
                string lv = levels[i];
                int row = i + 1;
                AddLvCell(g, T(names[i], 11.5, Palette.Text, FontWeight.SemiBold), row, 0);
                AddLvCell(g, LvLine(host, 11.5, Palette.Text, FontWeight.Normal, delegate { return LvRow(host, lv)[0]; }), row, 1);
                // 输入列单行：合计（未命中 A + 缓存 B）✓（规格 ✓✓ 一屏 ✓；降级时明细为空 → 只显主值 ✓）
                AddLvCell(g, LvLine(host, 11.5, Palette.Text, FontWeight.Normal, delegate
                {
                    string[] r = LvRow(host, lv);
                    return r[2].Length == 0 ? r[1] : r[1] + "（" + r[2] + "）";
                }), row, 2);
                AddLvCell(g, LvLine(host, 11.5, Palette.Text, FontWeight.Normal, delegate { return LvRow(host, lv)[3]; }), row, 3);
                TextBlock note = LvLine(host, 10.5, Palette.TextFaint, FontWeight.Normal, delegate { return LvRow(host, lv)[4]; });
                note.TextWrapping = TextWrapping.Wrap;
                AddLvCell(g, note, row, 4);
            }
            c.Children.Add(g);
            TextBlock fn = T("数字 = SESSAGG_SESSION bucket=own 求和 · token 未知的会话未计入 · 范围 = 当前窗口 eligible 集；「读取中…」= 该档正在后台拉取", 10.5, Palette.TextFaint);
            fn.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(fn);
            return Card(c, new Thickness(0), new Thickness(18, 8));
        }

        private static void AddLvCell(Grid g, Control cell, int row, int col)
        {
            Grid.SetRow(cell, row); Grid.SetColumn(cell, col);
            cell.Margin = new Thickness(0, 0, 8, 2);   // ★ 体验修复：行距压一档（一屏 ✓）
            cell.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(cell);
        }

        /// <summary>三档对比表一行五列的取值（看板体验修复 ✓✓）：每个格子 LiveText 调它 ✓ 降级文案集中在这里 ✗ 不编数 ✓。</summary>
        private static string[] LvRow(MainWindow host, string lv)
        {
            SessionsSnapshot cur = host.Data;
            if (cur == null) return LvRowDeg("读取中…", "");
            if (!cur.Ok) return LvRowDeg("—", "看板数据不可用");
            if (!cur.HasWinMeta) return LvRowDeg("—", "当前数据源未提供");
            if (lv != "global" && !cur.LineageOk) return LvRowDeg("—", "需桥插件血缘");
            SessionsSnapshot d = host.LevelSnap(lv);
            if (d == null) return LvRowDeg("读取中…", "");
            if (!d.Ok || !d.HasWinMeta) return LvRowDeg("—", "未知");
            BoardMetrics.LevelSum s = BoardMetrics.SumOwnRows(d);
            string note;
            if (lv == "global") note = "—";
            else if (lv == "parents") note = "不含子代理";
            else note = d.ForkSessions >= 0 ? "fork " + d.ForkSessions + " 个已排除" : "fork 数未知";
            if (s.TokUnknown > 0) note = (note == "—" ? "" : note + "；") + s.TokUnknown + " 个 token 未知未计入";
            return new string[] {
                s.Sessions + "",
                SessionRow.Human(s.In),
                "未命中 " + SessionRow.Human(s.Uncached) + " + 缓存 " + SessionRow.Human(s.CacheRead),
                SessionRow.Human(s.Output),
                note };
        }

        private static string[] LvRowDeg(string num, string note) { return new string[] { num, num, "", num, note }; }

        /// <summary>输入 Top 5（当前窗口）★ 2026-10-26 收尾 (b)：
        /// 榜单**默认不再显示会话标题** —— 改显示「项目/目录名 + id 短码」（来自 `cwd`，路径派生，非对话内容）；
        /// 要显示标题 ⇒ 走本卡右上角的**开关**（**纯视图态 · 默认关**，提案 §11 红线 A）。
        /// 名称口径的唯一出处是 <see cref="BoardMetrics.DisplayName"/>（纯函数，LogicTests 钉死）。</summary>
        private static Control BoardTop5Card(MainWindow host)
        {
            StackPanel c = new StackPanel { Spacing = 4 };   // ★ 体验修复：卡内节奏压一档（一屏 ✓）
            Grid head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            TextBlock cap = T("输入 Top 5（当前窗口）", 13, Palette.Text, FontWeight.Bold);
            cap.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cap, 0);
            head.Children.Add(cap);
            Button sw = new Button
            {
                Background = Palette.CardHover,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock { FontSize = ShapeTokens.FsFoot, Foreground = Palette.TextDim }
            };
            Grid.SetColumn(sw, 1);
            head.Children.Add(sw);
            c.Children.Add(head);
            // ★ 开启时的**明示**（唯一出处）：关着时不显示；开启时必须显示（提案 §11 红线 A）
            TextBlock notice = T(BoardHierarchy.TitleNotice, ShapeTokens.FsFoot, Palette.TextFaint);
            notice.TextWrapping = TextWrapping.Wrap;
            notice.IsVisible = host.BoardTop5ShowTitle;
            c.Children.Add(notice);
            StackPanel rows = new StackPanel { Spacing = 2 };   // ★ 行距压一档（一屏 ✓）
            BuildTop5Rows(host, rows);
            string sig0 = BoardMetrics.ChartSignature(host.Data, host.ChartDays, host.Doctor, System.DateTime.Now.Date);
            bool t0 = host.BoardTop5ShowTitle;
            host.Live(delegate
            {
                TextBlock tb = sw.Content as TextBlock;
                if (tb != null)
                {
                    string want = host.BoardTop5ShowTitle ? "会话名：标题（点一下改回项目名 · 短码）" : "会话名：项目名 · 短码（点一下显示标题）";
                    if (tb.Text != want) tb.Text = want;
                }
                if (notice.IsVisible != host.BoardTop5ShowTitle) notice.IsVisible = host.BoardTop5ShowTitle;
                string sg = BoardMetrics.ChartSignature(host.Data, host.ChartDays, host.Doctor, System.DateTime.Now.Date);
                bool t = host.BoardTop5ShowTitle;
                if (sg != sig0 || t != t0) { sig0 = sg; t0 = t; rows.Children.Clear(); BuildTop5Rows(host, rows); }   // ★ 只在榜变/开关变才重建 ✓✓
            });
            sw.Click += delegate { host.ToggleBoardTop5ShowTitle(); };
            global::Avalonia.Automation.AutomationProperties.SetName(sw, "会话名显示开关");
            global::Avalonia.Automation.AutomationProperties.SetHelpText(sw, BoardHierarchy.NamePolicyText + BoardHierarchy.TitleNotice);
            c.Children.Add(rows);
            TextBlock fn = T("按输入降序 · 仅列数值完整的会话（token 或轮次未知的不进榜）· 范围 = 当前窗口 eligible 集 · "
                + BoardHierarchy.NamePolicyText + "要显示会话标题请点本卡右上角的开关（本次会话内有效，不写入配置）", 10.5, Palette.TextFaint);
            fn.TextWrapping = TextWrapping.Wrap;
            c.Children.Add(fn);
            return Card(c, new Thickness(0), new Thickness(18, 8));
        }

        private static void BuildTop5Rows(MainWindow host, StackPanel rows)
        {
            List<BoardMetrics.TopRow> top = BoardMetrics.Top5ByInput(host.Data, host.BoardTop5ShowTitle);
            if (top == null || top.Count == 0)
            {
                rows.Children.Add(T("暂无数值完整的会话（token 或轮次未知的不进榜）", 11.5, Palette.TextDim));
                return;
            }
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,90,70") };
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddLvCell(g, T("会话", 11, Palette.TextFaint), 0, 0);
            AddLvCell(g, T("输入", 11, Palette.TextFaint), 0, 1);
            AddLvCell(g, T("输出", 11, Palette.TextFaint), 0, 2);
            AddLvCell(g, T("轮次", 11, Palette.TextFaint), 0, 3);
            for (int i = 0; i < top.Count; i++)
            {
                BoardMetrics.TopRow r = top[i];
                int row = i + 1;
                g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                TextBlock title = T(r.Title, 11.5, Palette.Text);
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                AddLvCell(g, title, row, 0);
                AddLvCell(g, T(SessionRow.Human(r.In), 11.5, Palette.Text), row, 1);
                AddLvCell(g, T(SessionRow.Human(r.Output), 11.5, Palette.TextDim), row, 2);
                AddLvCell(g, T(r.Turns + "", 11.5, Palette.TextDim), row, 3);
            }
            rows.Children.Add(g);
        }

        private static Control StatusDetail(MainWindow host)

        {

            StatusSnapshot st = host.Status;

            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = ShapeTokens.S16 };

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

            Ellipse haloBg = new Ellipse { Width = 44, Height = 44, Fill = Palette.SoftOf(stateBrush) };

            Ellipse haloDot = new Ellipse { Width = 14, Height = 14, Fill = stateBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            halo.Children.Add(haloBg); halo.Children.Add(haloDot);

            Grid.SetColumn(halo, 0);

            StackPanel ht = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };

            TextBlock heroText = T(st.StateText, 26, stateBrush, FontWeight.SemiBold);

            ht.Children.Add(heroText);

            ht.Children.Add(new TextBlock

            {

                Text = "依据只有可观测事实：本地端口是否监听 + 进程是否存在。dsh 换了形态（例如 headless 没有端口）时，这里显示未运行。",

                Foreground = Palette.TextDim,

                FontSize = 12,

                TextWrapping = TextWrapping.Wrap

            });

            Grid.SetColumn(ht, 1);

            hero.Children.Add(halo); hero.Children.Add(ht);

            s.Children.Add(Card(hero, new Thickness(0), new Thickness(18, 16)));

            // 提案A + 交接 §9 遗留修复：hero 走**字段级刷新** ✓ 自动刷新那一拍大徽章与旁边活数字同步 ✓

            host.Live(delegate

            {

                StatusSnapshot s2 = host.Status;

                if (s2 == null || !s2.Ok) return;

                bool dUp2 = s2.State == 2 && !string.IsNullOrEmpty(s2.DesktopClient);

                IBrush br2 = s2.State == 0 ? Palette.Good : (dUp2 ? Palette.Good : (s2.State == 1 ? Palette.Warn : Palette.Bad));

                if (heroText.Text != s2.StateText) heroText.Text = s2.StateText;

                heroText.Foreground = br2;

                haloBg.Fill = Palette.SoftOf(br2);

                haloDot.Fill = br2;

            });



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

            Grid row1 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };   // 提案A：会话数/累计token 的唯一主场在看板 ✓ 这里只留"身份"两卡 ✓

            row1.Children.Add(StatCardLive(host, Symbol.Box, delegate { return "当前形态"; },

                delegate { return FormText(host.Status, host.Profiles); },

                delegate { return "来自 profile 的 dsh.profile.bundles"; }, Palette.Text, null, 0, 2));

            row1.Children.Add(StatCardLive(host, Symbol.PuzzlePiece, delegate { return "profile / 插件"; },

                delegate { ProfilesSnapshot p2 = host.Profiles; return (p2 == null ? "—" : p2.Count.ToString()) + " / " + ThirdCount(p2); },

                delegate { ProfilesSnapshot q = host.Profiles; return BundleCount(q) + " 个组合包（含第三方 " + (q == null ? "—" : q.Profiles.Count.ToString()) + " 个 profile）"; }, Palette.Text, null, 1, 2));
            s.Children.Add(row1);   // ★ P1.1b 拼接时丢失的一行（row1 建了没挂上 → 身份卡整行隐身）




            // 提案A：缓存命中率/解码速度的唯一主场在「看板」✓ 备份/体检由侧栏与跳转目标负责 ✓ 田字 4 卡已删 ✓



            // ★★ DeepSeek 余额检测（2026-10-02 用户要求："DSH余额检测（自己填写key）…未绑定隐藏，绑定显示充值余额/活动赠送余额" ✓✓）

            //   未绑 key → **整卡隐藏** ✓（用户要求 ✓）；绑了 → 充值 / 活动赠送 ✓

            //   取不到 → 如实写原因 ✗ 绝不冒充数字 ✓✓（key 在设置页填：balance_key ✓）

            if (host.Balance != null && host.Balance.Ok && host.Balance.Bound)

            {

                Dsht.Gui.Avalonia.Markers.BalanceSummary ba = host.Balance;

                StackPanel bcard = new StackPanel { Spacing = 8 };

                StackPanel bh = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ShapeTokens.S12 };

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

                    bcard.Children.Add(T("暂未取到余额（" + (ba.Unavailable.Length > 0 ? ba.Unavailable : "原因未知") + "）", 11.5, Palette.Warn));

                }

                if (ba.Note.Length > 0) bcard.Children.Add(T(ba.Note, 11.5, Palette.Warn));

                s.Children.Add(Card(bcard, new Thickness(0), new Thickness(16, 14)));

            }

            else if (host.Balance != null && host.Balance.Ok && !host.Balance.Bound)

            {

                // ★ 入口提示（2026-10-04 用户在工作电脑上实测："余额配置入口我没找到" ✗✗）

                //   余额卡按用户要求**未绑定就隐藏** ✓ —— 但那样连"去哪儿填 key"都无处可寻 ✗

                //   ✓ 现在：卡仍然隐藏 ✓（余额数字一个不显示 ✓）只给**一行入口** + 一键跳到设置页 ✓✓

                StackPanel bh2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ShapeTokens.S12 };

                bh2.Children.Add(T("DeepSeek 余额", 11.5, Palette.Text, FontWeight.SemiBold));

                bh2.Children.Add(T("未绑定 —— 在「设置」页填 balance_key（你自己的 DeepSeek API key）后，这里就会显示充值余额 / 活动赠送余额", 11.5, Palette.TextDim));

                bh2.Children.Add(GhostButton(T("去设置填写", 11.5, Palette.Accent), delegate { host.SetMainSection(6); }, true));

                s.Children.Add(Card(bh2, new Thickness(0), new Thickness(16, 12)));

            }

            // 用户反馈"概览太空" → 加**最耗 token 三条会话速览**（概览独有内容 ✓ 不与任何页重复 ✓）
            SessionsSnapshot sv = host.Data;
            if (sv != null && sv.Ok && sv.Rows.Count > 0)
            {
                List<SessionRow> top = new List<SessionRow>(sv.Rows);
                top.Sort(delegate(SessionRow a2, SessionRow b2) { return b2.In.CompareTo(a2.In); });
                StackPanel tl = new StackPanel { Spacing = 8 };
                StackPanel th = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ShapeTokens.S12 };
                th.Children.Add(T("最耗 token 的三条会话", 13, Palette.Text, FontWeight.SemiBold));
                th.Children.Add(GhostButton(T("查看全部 →", 11.5, Palette.Accent), delegate { host.SetMainSection(2); }, true));
                tl.Children.Add(th);
                int showN = Math.Min(3, top.Count);
                for (int ti = 0; ti < showN; ti++)
                {
                    SessionRow r = top[ti];
                    Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
                    Ellipse dot = new Ellipse { Width = 8, Height = 8, Fill = Palette.StatusBrush(r.StatusKind), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(dot, 0);
                    TextBlock tt = T(r.TitleText, 11.5, Palette.Text); tt.Margin = new Thickness(8, 0, 0, 0); tt.VerticalAlignment = VerticalAlignment.Center;
                    tt.TextTrimming = TextTrimming.CharacterEllipsis;
                    Grid.SetColumn(tt, 1);
                    TextBlock tv = T(SessionRow.Human(r.In) + " · 命中 " + r.HitText, 11.5, Palette.TextDim); tv.VerticalAlignment = VerticalAlignment.Center; tv.Margin = new Thickness(12, 0, 0, 0);
                    Grid.SetColumn(tv, 2);
                    TextBlock tc = T(r.Turns + " 轮", 11.5, Palette.TextFaint); tc.VerticalAlignment = VerticalAlignment.Center; tc.Margin = new Thickness(12, 0, 0, 0);
                    Grid.SetColumn(tc, 3);
                    row.Children.Add(dot); row.Children.Add(tt); row.Children.Add(tv); row.Children.Add(tc);
                    tl.Children.Add(row);
                }
                s.Children.Add(Card(tl, new Thickness(0), new Thickness(16, 14)));
            }



            // 提案A：「自动刷新」控制已收进**页眉行右侧**（见 PageHeader / AutoRefreshBar）✓



            // 提案A：跳转卡与侧栏导航重复 ✓ 已删（侧栏就是导航 ✓）



            if (st.Extras.Count > 0)

            {

                StackPanel ex = new StackPanel { Spacing = ShapeTokens.S8 };

                ex.Children.Add(T("CLI 还报告了这些（未识别的标记原样展示）", 11.5, Palette.TextDim));

                for (int i = 0; i < st.Extras.Count; i++)

                    ex.Children.Add(Mono(st.Extras[i].Key + "  " + st.Extras[i].Value, 11.5, Palette.Text));

                s.Children.Add(Card(ex, new Thickness(0), new Thickness(16, 14)));

            }

            return s;

        }

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

        /// <summary>图组宿主（★ 看板体验修复 2026-10-11 ✓✓）：内容签名 = `BoardMetrics.ChartSignature`（纯函数 ✓ LogicTests 锁 ✓）——
        /// **签名不变 → 一行都不重画** ✓✓；签名变 → **只重建图区**（chartHost.Child）✗ 不整页重建 ✓ 不闪 ✓ 不跳滚动 ✓。
        /// 注：自动刷新的静默路径照旧跑 ✗ 刷新频率一丝没降 ✓ 只是没变就不重画 ✓。</summary>
        private static Control ChartsBody(MainWindow host)
        {
            Border chartHost = new Border { Child = ChartsBodyInner(host) };
            string sig0 = BoardMetrics.ChartSignature(host.Data, host.ChartDays, host.Doctor, System.DateTime.Now.Date);
            host.Live(delegate
            {
                string sg = BoardMetrics.ChartSignature(host.Data, host.ChartDays, host.Doctor, System.DateTime.Now.Date);
                if (sg != sig0) { sig0 = sg; chartHost.Child = ChartsBodyInner(host); }   // ★ 只在输入真变才重画 ✓✓
            });
            return chartHost;
        }

        /// <summary>「近 N 天」范围档卡（★ 看板体验修复 2026-10-11 ✓✓）：从图组拆出 —— 切档只重画分桶图 ✗ 这张卡不动 ✓
        /// 高亮走字段刷新 ✓ 点击瞬换 ✓（SetChartDays 现在是纯本地重分桶 ✓ 不跑 CLI ✓）。</summary>
        private static Control ChartRangeCard(MainWindow host)
        {
            StackPanel range = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ShapeTokens.S8 };
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
                    CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                    Padding = new Thickness(12, 5)
                };
                TextBlock rt = (TextBlock)rb.Content;
                host.Live(delegate
                {
                    bool on2 = host.ChartDays == dd;
                    IBrush bg = on2 ? Palette.Accent : Palette.CardHover;
                    if (!ReferenceEquals(rb.Background, bg)) rb.Background = bg;
                    IBrush fg = on2 ? Palette.OnAccent : Palette.TextDim;
                    if (!ReferenceEquals(rt.Foreground, fg)) rt.Foreground = fg;
                });
                rb.Click += delegate { host.SetChartDays(dd); };
                range.Children.Add(rb);
            }
            return Card(range, new Thickness(0), new Thickness(14, 10));
        }

        /// **手绘**（Grid + Border 柱），不引入任何图表依赖；日期用 ISO 字符串前缀比对，不做时区/日历运算（不猜）。
        /// ★ 看板体验修复（2026-10-11 ✓✓）：原 ChartsBody 本体 —— 范围档卡已拆成 ChartRangeCard ✓ 本函数只在签名变时被重建 ✓。</summary>

        private static Control ChartsBodyInner(MainWindow host)

        {

            SessionsSnapshot d = host.Data;

            StackPanel s = new StackPanel { Spacing = ShapeTokens.S16 };

            if (d == null || !d.Ok)

            {

                s.Children.Add(Card(T("没有可绘制的会话数据。", 11.5, Palette.TextDim), new Thickness(0), new Thickness(16, 14)));

                return s;

            }



            // —— 看板第一批（2026-10-08 ✓✓ 规格 §4.1 ✓）：窗口档生效 → 图 1/2/3 只看 eligible 集（SESSAGG_SESSION id ✓）——
            //   老 CLI（无 SESSWIN_META → AggIdSet()=null ✓）或 总计档（WinDays<0 ✓）→ 不过滤 ✓ 行为与旧版一致 ✓✓
            HashSet<string> elig = d.AggIdSet();

            bool winFilter = elig != null && d.WinDays > 0;



            // ① 近 N 天新增会话（N = 7/14/30 可切）
            // ★ 看板体验修复（2026-10-11 ✓✓）：范围档 chips 已拆成 ChartRangeCard（在 BoardContent 里加 ✓ 本函数只在签名变时重建 ✓）

            int days = host.ChartDays;   // 7/14/30 可切 ✓（用户要求："看板第二页图表内可以切换日期分布查看图表" ✓✓）

            string[] labels = new string[days];

            // ★★★ **日期桶修复（GUI 复审 MAJOR —— 我上一轮的"修好了"是假的）** ✓✓

            //   ✗ `labels[k]` 只有 5 个字符（MM-dd）✗ → 用 `EndsWith(labels[k])` 比**仍然只比后 5 位** ✗✗

            //     → **跨年还是算错** ✓（2025-09-30 会被算进 2026-09-30 那个桶 ✓ 审计实测复现 ✓）

            //   ✓ 现在：**另存一份完整日期 labelsFull** ✓✓ 比较用完整的 ✓ 显示（坐标轴）仍用 MM-dd ✓

            string[] labelsFull = new string[days];

            long[] counts = new long[days];

            System.DateTime today = System.DateTime.Now.Date;   // ★ 看板第二批（规格 §5.5 2.6 / §11.6-C3 ✓）：图 1 分桶键 UTC 日 → **本地日** ✓（与图 3 / 热力图同一归日口径 ✓）

            for (int i = 0; i < days; i++)

            {

                System.DateTime dday = today.AddDays(i - (days - 1));

                labels[i] = dday.ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture);

                labelsFull[i] = dday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            }

            long max = 0;

            for (int i = 0; i < d.Rows.Count; i++)

            {

                if (winFilter && (d.Rows[i] == null || d.Rows[i].Id == null || !elig.Contains(d.Rows[i].Id))) continue;   // ★ 窗口档过滤（看板第一批 ✓）

                string day = SessionsView.LocalDayOfIso(d.Rows[i].Created);   // ★ 第二批：ISO → 本地日（✗ 旧版直接截 UTC 前缀 → 本地凌晨的会话归错日 ✓）

                if (day == null) continue;   // 创建时间缺失/畸形 → 不计入，不猜 ✓

                for (int k = 0; k < days; k++)

                {

                    if (labelsFull[k].Length == 10 && string.Equals(day, labelsFull[k], StringComparison.Ordinal)) { counts[k]++; if (counts[k] > max) max = counts[k]; }   // MAJOR FIX: full date, not MM-dd

                }

            }

            StackPanel c1 = new StackPanel { Spacing = 8 };

            c1.Children.Add(T("近 " + days + " 天新增会话（按 dsh 记录的创建时间，本地日期）", 13, Palette.Text, FontWeight.Bold));   // ★ 第二批：UTC → 本地 ✓ 规格 §11.6-C3 ✓

            c1.Children.Add(BarChart(labels, counts, max, Palette.Accent, "个", 200));   // ★ 重排 §2-8 ✓ 图高归一 180–220 → 200 ✓

            c1.Children.Add(T("最高 " + max + " 个/天　合计 " + Sum(counts) + " 个（创建时间缺失的会话不计入）", 11.5, Palette.TextFaint));

            c1.Children.Add(T("按会话创建日（本地）分桶" + (winFilter ? "；只统计上方窗口内有活动的会话" : ""), 11.5, Palette.TextFaint));   // 规格 §5.5/D-5 + §11.6-C3 ✓ 图 1 保留但明说分桶键 ✓ 第二批改本地日 ✓

            Control cardC1 = Card(c1, new Thickness(0), new Thickness(18, 16));   // ★ 重排 §2-8 ✓ 卡片存局部变量，末尾按「同排两图」装排 ✓



            // ② 缓存命中率分布

            long low = 0, mid = 0, high = 0, unknown = 0;

            for (int i = 0; i < d.Rows.Count; i++)

            {

                if (winFilter && (d.Rows[i] == null || d.Rows[i].Id == null || !elig.Contains(d.Rows[i].Id))) continue;   // ★ 窗口档过滤（看板第一批 ✓）

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

            c2.Children.Add(BarChart(hl, hc, Math.Max(Math.Max(low, mid), Math.Max(high, unknown)), Palette.Good, "个", 200));   // ★ 重排 §2-8 ✓

            c2.Children.Add(T("命中率越高越省钱；unknown 表示该会话没有这个字段（空会话），不会把它算成 0%。", 11.5, Palette.TextFaint));

            Control cardC2 = Card(c2, new Thickness(0), new Thickness(18, 16));   // ★ 重排 §2-8 ✓



            // ③ 近 N 天**有活动的会话 · 整会话累计**（看板第一批 · 2026-10-08 ✓✓ 规格 §4.3/§5.5 D-5 ✓✓）
            //   ✗ 旧版按 **Created 的 UTC 日**把整会话累计塞进创建日 → 与"近 N 天用量"观感打架 ✗
            //   ✓ 现在：分桶键 = 各会话 **lastPromptAt 的本地日** ✓（GUI 侧分桶先例 ✓ 规格 :812 ✓）
            //     · 缺 lastPromptAt 的会话**不进图**（不猜日期 ✓）并在脚注如实计数 ✓
            //     · 数字是**整会话累计**（truth=0 ✗ 非窗口内增量 ✓ C.4 脚注：重排后每屏只在筛选条印一次 ✓ §1 ✓✓）
            string[] labelsLoc = new string[days];

            string[] labelsLocFull = new string[days];

            System.DateTime todayLoc = System.DateTime.Now.Date;

            for (int i = 0; i < days; i++)

            {

                System.DateTime dday = todayLoc.AddDays(i - (days - 1));

                labelsLoc[i] = dday.ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture);

                labelsLocFull[i] = dday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            }

            long[] dayTok = new long[days];

            long maxTok = 0;

            long missingLast3 = 0;

            for (int i = 0; i < d.Rows.Count; i++)

            {

                SessionRow r3 = d.Rows[i];

                if (winFilter && (r3 == null || r3.Id == null || !elig.Contains(r3.Id))) continue;   // ★ 窗口档过滤 ✓

                string lp = r3 == null ? null : r3.Last;

                if (string.IsNullOrEmpty(lp)) { missingLast3++; continue; }

                System.DateTime lpdt;

                if (!System.DateTime.TryParse(lp, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out lpdt)) { missingLast3++; continue; }

                string lday = lpdt.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

                for (int k = 0; k < days; k++)

                {

                    if (labelsLocFull[k].Length == 10 && string.Equals(lday, labelsLocFull[k], StringComparison.Ordinal)) dayTok[k] += r3.In;   // 整会话累计 ✓ 不是窗口增量 ✓

                }

            }

            for (int k = 0; k < days; k++) if (dayTok[k] > maxTok) maxTok = dayTok[k];

            long tokDiv = maxTok >= 1000 ? 1000 : 1;   // F11 FIX: unit follows the data, so a sub-1000 day is not rounded to zero

            long[] dayTokK = new long[days];

            for (int k = 0; k < days; k++) dayTokK[k] = dayTok[k] / tokDiv;

            StackPanel c3 = new StackPanel { Spacing = 8 };

            c3.Children.Add(T("近 " + days + " 天有活动的会话 · 整会话累计（输入侧合计，" + (tokDiv >= 1000 ? "k token" : "token") + "）", 13, Palette.Text, FontWeight.Bold));   // 口径名一字不改 ✓ 规格 §4.3 :119 ✓

            c3.Children.Add(BarChart(labelsLoc, dayTokK, maxTok / tokDiv, Palette.Warn, maxTok >= 1000 ? "k tok" : "tok", 200));   // F11 FIX: unit follows the data  // ★ 重排 §2-8 ✓

            c3.Children.Add(T("合计 " + SessionRow.Human(Sum(dayTok)) + " token　最高 " + SessionRow.Human(maxTok) + "/天（按最后活动时间归本地日；缺失 " + missingLast3 + " 个不计入）", 11.5, Palette.TextFaint));

            // ★ 看板重排（§1 ✓✓）：卡内不再印第二遍 C.4 —— 筛选条已印、每屏恰好一次 ✓ 文案未动 ✓ 只去重复 ✓

            Control cardC3 = Card(c3, new Thickness(0), new Thickness(18, 16));   // ★ 重排 §2-8 ✓



            // —— 看板第二批 · 热力图：近 N 天会话**最后活动日** · 会话数（本地日 ✓✓ 规格 §11.6-C3 ✓✓）——
            //   分桶键与图 3 **完全相同**（lastPromptAt 归本地日 ✓），只是数**会话个数**而不是 token ✓
            //   ✗ 一个会话只计入它最后活动的那一天 —— 投影没有「逐日活跃轨迹」这个事实 ✗ 明说 ✗ 不猜 ✓
            long[] dayCnt = new long[days];

            long missingH = 0;

            for (int i = 0; i < d.Rows.Count; i++)

            {

                SessionRow rh = d.Rows[i];

                if (winFilter && (rh == null || rh.Id == null || !elig.Contains(rh.Id))) continue;   // ★ 窗口档过滤 ✓（与图 1/2/3 同一 eligible 集 ✓）

                string ld = SessionsView.LocalDayOfIso(rh == null ? null : rh.Last);

                if (ld == null) { missingH++; continue; }

                for (int k = 0; k < days; k++)

                {

                    if (string.Equals(ld, labelsLocFull[k], StringComparison.Ordinal)) { dayCnt[k]++; break; }

                }

            }

            long maxCnt = 0;

            for (int k = 0; k < days; k++) if (dayCnt[k] > maxCnt) maxCnt = dayCnt[k];

            StackPanel ch = new StackPanel { Spacing = 8 };

            ch.Children.Add(T(string.Format(SessionsMarkers.HeatTitle, days), 13, Palette.Text, FontWeight.Bold));

            // 网格：行 = 星期（一→日 ✓ ISO），列 = 周（最旧在左 ✓）；窗口首日之前的补齐格不画 ✓
            System.DateTime firstDay = todayLoc.AddDays(-(days - 1));

            int lead = ((int)firstDay.DayOfWeek + 6) % 7;   // DayOfWeek: Sunday=0 → 一=0…日=6

            int cols = (lead + days + 6) / 7;

            Grid hg = new Grid();   // 间距用各格 Margin 出（此 Avalonia 版本的 Grid 无 ColumnSpacing/RowSpacing ✓ 编译门槛实测 ✓）

            // ★ 看板重排（§2-8 ✓✓）：格子 14px → 22px —— 网格总高 7×(22+5) = 189px ∈ 图高 180–220 区间 ✓ 与同排条形图视觉等高 ✓
            hg.ColumnDefinitions.Add(new ColumnDefinition(20, GridUnitType.Pixel));   // 星期标签列

            for (int ci = 0; ci < cols; ci++) hg.ColumnDefinitions.Add(new ColumnDefinition(22, GridUnitType.Pixel));

            for (int ri = 0; ri < 7; ri++) hg.RowDefinitions.Add(new RowDefinition(22, GridUnitType.Pixel));

            string[] wd = new string[] { "一", "二", "三", "四", "五", "六", "日" };

            for (int ri = 0; ri < 7; ri++)

            {

                TextBlock wl = T(wd[ri], 10.5, Palette.TextFaint);   // ★ 重排 §2-8 ✓ 9 → 10（格变大，字跟上 ✓）

                wl.VerticalAlignment = VerticalAlignment.Center;

                Grid.SetRow(wl, ri); Grid.SetColumn(wl, 0);

                hg.Children.Add(wl);

            }

            for (int k = 0; k < days; k++)

            {

                int pos = lead + k;

                Border cell = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Background = HeatBrush(dayCnt[k]), Margin = new Thickness(2.5) };   // ★ 重排 §2-8 ✓ 14 → 22 ✓

                ToolTip.SetTip(cell, labelsLocFull[k] + "：" + dayCnt[k] + " 个会话");   // 悬浮出真值 ✓

                Grid.SetRow(cell, pos % 7); Grid.SetColumn(cell, pos / 7 + 1);

                hg.Children.Add(cell);

            }

            ch.Children.Add(hg);

            ch.Children.Add(T("最高 " + maxCnt + " 个/天（与图 3 同一归日口径：各会话最后活动时间归本地日）", 11.5, Palette.TextFaint));

            ch.Children.Add(T(string.Format(SessionsMarkers.HeatFootnote, missingH), 11.5, Palette.TextFaint));

            Control cardHeat = Card(ch, new Thickness(0), new Thickness(18, 16));   // ★ 重排 §2-8 ✓



            // —— 看板第二批 · 耗时分解堆叠（真值 ✓ 整会话累计归窗口 ✓ 规格 §11.6-C1 ✓✓）——
            //   四桶：等首 token(ttftMs) / 流式解码(decodeMs) / llm 其他(=llmMs−ttft−decode ≥0 ✓ 勘察 §5 恒等式) / 工具执行(toolMs)
            StackPanel ctm = new StackPanel { Spacing = 8 };

            ctm.Children.Add(T(SessionsMarkers.TimeTitle, 13, Palette.Text, FontWeight.Bold));

            if (d.TimeRows.Count == 0)

            {

                // 旧 CLI 没有 SESSTIME_SESSION 行 → 如实说明 ✗ 不画空图 ✗ 不猜 ✓
                ctm.Children.Add(T("当前数据源未提供逐会话计时（旧版 CLI 没有 SESSTIME_SESSION 行），因此这里不出图。", 11.5, Palette.TextFaint));

            }

            else

            {

                int knownT;

                long[] tt = SessionsView.TimeSplit(d.TimeRows, out knownT);

                long totT = tt[0] + tt[1] + tt[2] + tt[3];

                string[] tn = new string[] { "等首 token", "流式解码", "llm 其他（无首 token 的 step 等）", "工具执行" };

                IBrush[] tb = new IBrush[] { Palette.Accent, Palette.FormAcp, Palette.Idle, Palette.Warn };

                ctm.Children.Add(StackedBar(tt, totT, tb));

                for (int si = 0; si < 4; si++)

                    ctm.Children.Add(LegendRow(tb[si], tn[si] + "　" + FmtMs(tt[si]) + "（" + PctOf(tt[si], totT) + "）"));

                ctm.Children.Add(T("窗口内 " + d.TimeRows.Count + " 个会话，" + knownT + " 个计入（缺 sessionStats 的 " + (d.TimeRows.Count - knownT) + " 个不计入）", 11.5, Palette.TextFaint));

                ctm.Children.Add(T(SessionsMarkers.TimeFootnote, 11.5, Palette.TextFaint));

                // ★ 看板重排（§1 ✓✓）：卡内不再印第二遍 C.4 —— 筛选条已印、每屏恰好一次 ✓ 文案未动 ✓ 只去重复 ✓

            }

            Control cardTime = Card(ctm, new Thickness(0), new Thickness(18, 16));   // ★ 重排 §2-8 ✓



            // —— 看板第二批 · 上下文构成 · Token 分类（dsh 启发式折算口径必标 ✓ messageTokens 一桶不拆 D3 ✓ 规格 §11.6-C2 ✓✓）——
            StackPanel ccx = new StackPanel { Spacing = 8 };

            ccx.Children.Add(T(SessionsMarkers.CtxTitle, 13, Palette.Text, FontWeight.Bold));

            if (d.CtxRows.Count == 0)

            {

                ccx.Children.Add(T("当前数据源未提供上下文构成（旧版 CLI 没有 SESSCTX_SESSION 行），因此这里不出图。", 11.5, Palette.TextFaint));

            }

            else

            {

                int knownC;

                long[] cx = SessionsView.CtxTotals(d.CtxRows, out knownC);

                long totC = cx[0] + cx[1] + cx[2];

                string[] cn = new string[] { "system", "tools", "message（一桶不拆）" };

                IBrush[] cbr = new IBrush[] { Palette.Accent, Palette.Good, Palette.Warn };

                ccx.Children.Add(StackedBar(cx, totC, cbr));

                for (int si = 0; si < 3; si++)

                    ccx.Children.Add(LegendRow(cbr[si], cn[si] + "　" + SessionRow.Human(cx[si]) + " tok（" + PctOf(cx[si], totC) + "）"));

                ccx.Children.Add(T("窗口内 " + d.CtxRows.Count + " 个会话，" + knownC + " 个计入（缺 contextBreakdown 的 " + (d.CtxRows.Count - knownC) + " 个不计入）", 11.5, Palette.TextFaint));

                ccx.Children.Add(T(SessionsMarkers.CtxFootnote, 11.5, Palette.TextFaint));

            }

            Control cardCtx = Card(ccx, new Thickness(0), new Thickness(18, 16));   // ★ 重排 §2-8 ✓



            // ④ **体检结论分布**（B 类：运维视角 —— 这台机器现在健康吗 ✓）

            DoctorSummary dsum = host.Doctor;

            string[] dl = new string[] { "通过", "提醒", "错误" };

            long[] dv = dsum == null || !dsum.Ok ? new long[] { 0, 0, 0 } : new long[] { dsum.Pass, dsum.Warn, dsum.Error };

            StackPanel c4 = new StackPanel { Spacing = 8 };

            c4.Children.Add(T("体检结论分布（条目数）", 13, Palette.Text, FontWeight.Bold));

            c4.Children.Add(BarChart(dl, dv, Math.Max(Math.Max(dv[0], dv[1]), dv[2]), Palette.Accent, "项", 200));   // ★ 重排 §2-8 ✓

            c4.Children.Add(T(dsum == null || !dsum.Ok

                ? "还没有体检结果 —— 进「体检」页会自动跑一次 doctor。这里不显示 0：没跑过和跑过且全过是两件事。"

                : "来自 doctor 的分级条目；错误项在「体检」页可以逐条看到原因。", 11.5, Palette.TextFaint));

            Control cardC4 = Card(c4, new Thickness(0), new Thickness(18, 16));   // ★ 重排 §2-8 ✓

            // —— ★ 看板重排（2026-10-09 ✓✓ 规格 §2-7/§2-8 ✓✓）：同排两图等高装排 ——
            //   三列 Grid（图 | 14px 间隔列 | 图；此 Avalonia Grid 无 ColumnSpacing ✓ 用间隔列 ✓），Grid 单元格默认拉伸 ⇒ 同排两卡等高 ✓
            //   排布：①新增会话 | ②命中率 ／ ③token 消耗 | 热力图 ／ 耗时 | Token 分类 ／ 体检分布独占整行
            //   （体检分布来自 doctor ✗ 不随窗口过滤 ✓ 不与窗口图混排，单独成行更诚实 ✓）
            s.Children.Add(PairRow(cardC1, cardC2));
            s.Children.Add(PairRow(cardC3, cardHeat));
            s.Children.Add(PairRow(cardTime, cardCtx));
            s.Children.Add(cardC4);

            return s;

        }

        /// <summary>同排两图（★ 看板重排 §2-8 ✓✓）：三列 Grid，中列 16px 间隔（样式重构：14 → S16 节奏 ✓）两图等宽 ✓ 默认拉伸 ⇒ 同排等高 ✓。</summary>
        private static Control PairRow(Control left, Control right)
        {
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 2);
            g.Children.Add(left);
            g.Children.Add(right);
            return g;
        }

        /// <summary>条形图。★ 看板重排（2026-10-09 ✓✓ 规格 §2-8 ✓✓）：图高归一 180–220 —— 新增可选参数 height（看板传 200 ✓）；
        /// 默认 132 ⇒ 条幅公式 4 + v×(132−44)/max = 4 + v×88/max，与旧版**逐点相同** ✓（调用处没改的零行为变化 ✓）。</summary>
        private static Control BarChart(string[] labels, long[] values, long max, IBrush brush, string unit, double height = 132)

        {

            bool allZero = true;

            for (int i = 0; i < values.Length; i++) if (values[i] != 0) { allZero = false; break; }



            if (allZero)

            {

                // ① 空状态 ✓✓（用户"美化一下前端" ✓）：原来是一排 2px 小条 ✗ 看着就是空的 ✓

                //    现在居中说明 ✓ —— 而且**不假造数据** ✓ 与项目诚实原则一致 ✓

                StackPanel es = new StackPanel { Spacing = ShapeTokens.S4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

                es.Children.Add(T("暂无数据", 13, Palette.TextDim, FontWeight.SemiBold));

                // GUI final review: every caller passed a `unit` and this method never read it. Using it in the

                // empty state makes that state say what is missing in the same unit the chart would have used.

                es.Children.Add(T("这个区间里没有可统计的记录（单位：" + unit + "；是没有，不是 0）", 11.5, Palette.TextFaint));

                return new Border { Height = height, Child = es };   // ★ 重排 §2-8 ✓ 空状态也等高 ✓

            }



            Grid outer = new Grid { Height = height };

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

                double h = 4 + (v * (height - 44.0) / max);   // ★ 重排 §2-8 ✓ 默认 132 ⇒ v×88.0 与旧版一致 ✓

                StackPanel col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = ShapeTokens.S4, Margin = new Thickness(2, 0) };

                col.Children.Add(T(v == 0 ? "" : v.ToString(), 10.5, Palette.TextDim));

                col.Children.Add(new Border { Height = h, CornerRadius = new CornerRadius(4), Background = v == 0 ? Palette.BarTrack : brush });

                // 列多时（如 30 天）标签会挤在一起 ✗ → 只标每 3 个 ✓（条形本身照画 ✓ 数据不省略 ✓）

                bool showLabel = labels.Length <= 16 || (i % 3) == 0 || i == labels.Length - 1;

                col.Children.Add(T(showLabel ? labels[i] : "", 10.5, Palette.TextFaint));

                Grid.SetColumn(col, i);

                g.Children.Add(col);

            }

            outer.Children.Add(g);

            // ③ 基线 ✓（一条实一点的底线 ✓ 让条形有"落地"感 ✓）

            outer.Children.Add(new Border { Height = 1, Background = Palette.Border, VerticalAlignment = VerticalAlignment.Bottom });

            return outer;

        }

        // —— 看板第二批绘图助手（2026-10-09 ✓✓ 规格 §11.6 ✓）——

        /// <summary>水平堆叠条：各段宽度按值占比（Star ✓）；值为 0 的段不占位 ✓；全 0 → 空轨道 ✗ 不假造色块 ✓。</summary>
        private static Control StackedBar(long[] vals, long total, IBrush[] brushes)

        {

            Grid g = new Grid { Height = 18 };

            if (total <= 0)

            {

                g.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = Palette.BarTrack });

                return g;

            }

            int nseg = 0;

            for (int i = 0; i < vals.Length; i++) if (vals[i] > 0) nseg++;

            int col = 0;

            for (int i = 0; i < vals.Length; i++)

            {

                if (vals[i] <= 0) continue;

                g.ColumnDefinitions.Add(new ColumnDefinition(vals[i], GridUnitType.Star));

                Border seg = new Border { Background = brushes[i] };

                if (nseg == 1) seg.CornerRadius = new CornerRadius(9);

                else if (col == 0) seg.CornerRadius = new CornerRadius(9, 0, 0, 9);

                else if (col == nseg - 1) seg.CornerRadius = new CornerRadius(0, 9, 9, 0);

                Grid.SetColumn(seg, col++);

                g.Children.Add(seg);

            }

            return g;

        }

        /// <summary>图例行：色块 + 说明文字。</summary>
        private static Control LegendRow(IBrush brush, string text)

        {

            StackPanel r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            r.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = brush, VerticalAlignment = VerticalAlignment.Center });

            r.Children.Add(T(text, 11.5, Palette.TextDim));

            return r;

        }

        /// <summary>毫秒 → 人读时长（0 → "0 s" ✓ 真值直出 ✓）。</summary>
        private static string FmtMs(long ms)

        {

            if (ms <= 0) return "0 s";

            double sec = ms / 1000.0;

            if (sec < 60) return sec.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";

            double min = sec / 60.0;

            if (min < 60) return min.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " min";

            return (min / 60.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " h";

        }

        /// <summary>占比 → 百分比文字（total ≤ 0 → "0%" ✓ 不除零 ✓）。</summary>
        private static string PctOf(long v, long total)

        {

            if (total <= 0) return "0%";

            return (v * 100.0 / total).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";

        }

        /// <summary>热力格色：0 → 空槽色（✗ 不装有色 ✓）；≥1 → 主题 Accent 的四档不透明度（1 / 2-3 / 4-7 / ≥8）。</summary>
        private static IBrush HeatBrush(long v)

        {

            if (v <= 0) return Palette.BarTrack;

            SolidColorBrush scb = Palette.Accent as SolidColorBrush;

            Color basec = scb == null ? Colors.SteelBlue : scb.Color;

            double op = v == 1 ? 0.30 : (v <= 3 ? 0.50 : (v <= 7 ? 0.75 : 1.0));

            return new SolidColorBrush(basec, op);

        }

        private static long Sum(long[] a) { long s = 0; for (int i = 0; i < a.Length; i++) s += a[i]; return s; }

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
