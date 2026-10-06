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

                    // 美学A3：行首级别色点 ✓ 扫读时一眼分级 ✓（不再只靠 [OK]/[WARN] 文本前缀 ✓）

                    StackPanel lnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };

                    lnRow.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = c, VerticalAlignment = VerticalAlignment.Center });

                    TextBlock lt = T(ln, 12, c); lt.VerticalAlignment = VerticalAlignment.Center;

                    lnRow.Children.Add(lt);

                    list.Children.Add(lnRow);

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

                s.Children.Add(EmptyState("还没有备份", "点「立即备份」创建第一份（空数据根不会被算作有效备份，这是刻意的规则）。"));

            // 美学A1：一份备份**一行**（原来是每份一张大卡 + 4 个按钮 ✗ 5 份滚 5 屏 ✗）

            for (int i = 0; i < items.Count; i++)

            {

                BackupItem b = items[i];

                Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

                Border kind = Chip(b.KindText, b.Kind == "Manual" ? Palette.Accent : Palette.TextDim, b.Kind == "Manual" ? Palette.AccentSoft : Palette.BarTrack);

                kind.Margin = new Thickness(0, 0, 10, 0); kind.VerticalAlignment = VerticalAlignment.Center;

                Grid.SetColumn(kind, 0);

                StackPanel mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                mid.Children.Add(T(b.Name, 12.5, Palette.Text, FontWeight.SemiBold));

                mid.Children.Add(T(b.SizeText + "　" + b.Time, 11, Palette.TextFaint));

                Grid.SetColumn(mid, 1);

                StackPanel acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

                acts.Children.Add(GhostButton(T("导出", 11, Palette.Text), delegate { host.ExportBackup(b.Name); }, true));

                acts.Children.Add(GhostButton(T("预览", 11, Palette.Text), delegate { host.DryRunRestore(b.Name); }, true));

                acts.Children.Add(GhostButton(T("恢复", 11, Palette.Text), delegate { host.ApplyRestore(b.Name); }, true));

                Button del = GhostButton(T("删除", 11, Palette.Warn), delegate { host.DeleteBackup(b.Name); }, true);

                del.Background = Palette.WarnSoft;   // 删除仍是全行最显眼的一个 ✓（不可逆 ✓）

                acts.Children.Add(del);

                Grid.SetColumn(acts, 2);

                row.Children.Add(kind); row.Children.Add(mid); row.Children.Add(acts);

                s.Children.Add(Card(row, new Thickness(0, 0, 0, 6), new Thickness(14, 10)));

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

            // 美学B3：级别/行数从 7 个按钮合成两个下拉 ✓ 筛选区从两行压缩成一行 ✓

            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            bar.Children.Add(T("级别", 11, Palette.TextFaint));

            string[] lvOpts = new string[] { "全部", "INFO", "WARN", "ERROR" };

            ComboBox lvCb = new ComboBox { MinWidth = 92, FontSize = 12, ItemsSource = lvOpts, SelectedIndex = string.IsNullOrEmpty(host.LogFilter) ? 0 : (System.Array.IndexOf(lvOpts, host.LogFilter.ToUpperInvariant()) < 0 ? 0 : System.Array.IndexOf(lvOpts, host.LogFilter.ToUpperInvariant())) };

            lvCb.SelectionChanged += delegate { host.SetLogFilter(lvCb.SelectedIndex <= 0 ? "" : lvOpts[lvCb.SelectedIndex]); };

            bar.Children.Add(lvCb);

            bar.Children.Add(T("行数", 11, Palette.TextFaint));

            int[] lineOpts = new int[] { 100, 500, 2000 };

            ComboBox lnCb = new ComboBox { MinWidth = 84, FontSize = 12, ItemsSource = lineOpts, SelectedIndex = host.LogLines >= 2000 ? 2 : (host.LogLines <= 100 ? 0 : 1) };

            lnCb.SelectionChanged += delegate { host.SetLogLines(lineOpts[lnCb.SelectedIndex]); };

            bar.Children.Add(lnCb);

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

        private static IBrush LogLineBrush(string l)

        {

            if (l.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0) return Palette.Bad;

            if (l.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0) return Palette.Warn;

            if (l.IndexOf("INFO", StringComparison.OrdinalIgnoreCase) >= 0) return Palette.TextDim;

            return Palette.Text;

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

                s.Children.Add(EmptyState("还没读到更新信息", "点上面的「检查更新」试试（只读，不动任何东西）。"));

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

                s.Children.Add(EmptyState("没有读到配置项", "点「刷新」重试；一直读不到就检查 dsh-minato.exe 是否与本程序同目录。"));

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

        private static void RenderSettingsGroup(StackPanel s, MainWindow host, List<ConfigItem> group, string title)

        {

            if (group == null || group.Count == 0) return;

            s.Children.Add(Card(T(title, 13, Palette.Text, FontWeight.SemiBold), new Thickness(0), new Thickness(16, 12)));

            for (int i = 0; i < group.Count; i++) s.Children.Add(SettingsItemCard(host, group[i]));

        }

        private static List<ConfigItem> PickItems(List<ConfigItem> items, string[] keys, List<ConfigItem> placed)

        {

            List<ConfigItem> g = new List<ConfigItem>();

            for (int k = 0; k < keys.Length; k++)

                for (int i = 0; i < items.Count; i++)

                    if (items[i].Key == keys[k] && !placed.Contains(items[i])) { g.Add(items[i]); placed.Add(items[i]); }

            return g;

        }

        private static List<ConfigItem> RestItems(List<ConfigItem> items, List<ConfigItem> placed)

        {

            List<ConfigItem> g = new List<ConfigItem>();

            for (int i = 0; i < items.Count; i++) if (!placed.Contains(items[i])) g.Add(items[i]);

            return g;

        }

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

        /// 设置页保留它们（配置兼容 ✓），但整行标灰并如实注明 ✓（低成本诚实方案；补实现是另一件事 ✓）。</summary>

        private static string DeadNoteFor(string key)

        {

            if (key == "close_action") return "⚠ V3 不消费此项：本窗口的关闭就是直接退出，没有托盘/询问（经典版 v2.x 核心的设置，保留仅为配置兼容）";

            if (key == "host") return "⚠ V3 不消费此项：服务地址固定为 127.0.0.1:3080（经典版 v2.x 核心的设置，保留仅为配置兼容）";

            if (key == "check_update") return "⚠ V3 不消费此项：本工具自身的更新检查未接线（V3 里真正生效的是 check_dsh_update）";

            if (key == "lang") return "⚠ 此项只影响命令行输出语言；本图形界面暂为中文";

            return null;

        }

        private static Control SettingsItemCard(MainWindow host, ConfigItem c)

        {

            StackPanel row = new StackPanel { Spacing = 8 };

            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            head.Children.Add(T(c.Key, 12.5, Palette.Text, FontWeight.SemiBold));

            head.Children.Add(T(c.Desc, 11.5, Palette.TextDim));

            row.Children.Add(head);

            // 备注 ✓✓（用户要求："备注一下发生什么问题可以尝试启用和禁用" ✓）—— CONFIGNOTE 原样显示 ✓

            string note = NoteFor(host.RawOutput, c.Key);

            if (!string.IsNullOrEmpty(note))

            {

                // 美学A5：CONFIGNOTE 从"每卡一条橙色长文"改为 ⓘ 悬停显示 ✓ 19 张卡不再满眼警示色 ✓

                TextBlock info = T("ⓘ", 12, Palette.Warn);

                info.VerticalAlignment = VerticalAlignment.Center;

                ToolTip.SetTip(info, StripMd(note));

                head.Children.Add(info);

            }



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

    }
}