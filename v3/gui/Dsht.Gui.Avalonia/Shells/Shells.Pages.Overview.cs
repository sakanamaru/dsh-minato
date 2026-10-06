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

            v.FontSize = 22;   // 美学B4：与 StatCard 统一为 22 ✓ v.FontWeight = FontWeight.SemiBold; v.Foreground = valueBrush; v.Margin = new Thickness(0, 8, 0, 0);

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

        private static Control AutoRefreshBar(MainWindow host)

        {

            StackPanel arBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            TextBlock arLabel = T("自动刷新", 12, Palette.Text, FontWeight.SemiBold); arLabel.VerticalAlignment = VerticalAlignment.Center; arBar.Children.Add(arLabel);

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
            ToolTip.SetTip(arCb, "只在概览/看板生效 ✓ 0.5 秒档 = 每拍起一个 CLI 进程（有真实成本 ✓）自定义可输 0.5–3600 秒");

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

        /// <summary>看板（提案A 后的唯一分工）：**指标的唯一主场**。

        /// 主标签「指标」= KPI 四卡 + 最近一条操作回执；「图表」子标签 = 4 张图（**全页唯一出现处** ✓

        /// —— 原来主标签也嵌一份 ChartsBody，同一组图渲染两遍 ✗ 已拆 ✓）。</summary>

        /// <summary>看板 = **指标与图表的唯一主场（单页）**。
        /// 用户反馈"主标签太空 + 图表子标签重复"→ 合成一页：KPI 四卡 + 操作回执 + 4 张图 ✓
        /// 同一组图只出现一次（既填满也不重复 ✓）。「图表」子标签已随之取消。</summary>
        private static Control BoardContent(MainWindow host)
        {
            StackPanel s = new StackPanel { Margin = PageMargin, Spacing = 14 };
            s.Children.Add(KpiStrip(host));
            // 操作回执：最近一条动作的结果（细节在右下角 toast）
            if (!string.IsNullOrEmpty(host.ActionLog))
                s.Children.Add(Card(T(host.ActionLog, 12, Palette.TextDim), new Thickness(0), new Thickness(14, 10)));
            else
                s.Children.Add(Card(T("还没有操作 —— 侧栏底部可以一键启动 / 停止 dsh；结果会显示在这里并弹 toast。", 11.5, Palette.TextFaint), new Thickness(0), new Thickness(14, 10)));
            s.Children.Add(ChartsBody(host));
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

            Ellipse haloBg = new Ellipse { Width = 44, Height = 44, Fill = Palette.SoftOf(stateBrush) };

            Ellipse haloDot = new Ellipse { Width = 14, Height = 14, Fill = stateBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            halo.Children.Add(haloBg); halo.Children.Add(haloDot);

            Grid.SetColumn(halo, 0);

            StackPanel ht = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };

            TextBlock heroText = T(st.StateText, 26, stateBrush, FontWeight.SemiBold);

            ht.Children.Add(heroText);

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

            // 用户反馈"概览太空" → 加**最耗 token 三条会话速览**（概览独有内容 ✓ 不与任何页重复 ✓）
            SessionsSnapshot sv = host.Data;
            if (sv != null && sv.Ok && sv.Rows.Count > 0)
            {
                List<SessionRow> top = new List<SessionRow>(sv.Rows);
                top.Sort(delegate(SessionRow a2, SessionRow b2) { return b2.In.CompareTo(a2.In); });
                StackPanel tl = new StackPanel { Spacing = 8 };
                StackPanel th = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                th.Children.Add(T("最耗 token 的三条会话", 13, Palette.Text, FontWeight.SemiBold));
                th.Children.Add(GhostButton(T("查看全部 →", 11, Palette.Accent), delegate { host.SetMainSection(2); }, true));
                tl.Children.Add(th);
                int showN = Math.Min(3, top.Count);
                for (int ti = 0; ti < showN; ti++)
                {
                    SessionRow r = top[ti];
                    Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
                    Ellipse dot = new Ellipse { Width = 8, Height = 8, Fill = Palette.StatusBrush(r.StatusKind), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(dot, 0);
                    TextBlock tt = T(r.TitleText, 12, Palette.Text); tt.Margin = new Thickness(8, 0, 0, 0); tt.VerticalAlignment = VerticalAlignment.Center;
                    tt.TextTrimming = TextTrimming.CharacterEllipsis;
                    Grid.SetColumn(tt, 1);
                    TextBlock tv = T(SessionRow.Human(r.In) + " · 命中 " + r.HitText, 11.5, Palette.TextDim); tv.VerticalAlignment = VerticalAlignment.Center; tv.Margin = new Thickness(12, 0, 0, 0);
                    Grid.SetColumn(tv, 2);
                    TextBlock tc = T(r.Turns + " 轮", 11, Palette.TextFaint); tc.VerticalAlignment = VerticalAlignment.Center; tc.Margin = new Thickness(12, 0, 0, 0);
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

                StackPanel ex = new StackPanel { Spacing = 6 };

                ex.Children.Add(T("CLI 还报告了这些（未识别的标记原样展示）", 12, Palette.TextDim));

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