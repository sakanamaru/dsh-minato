using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using Dsht.Gui.Avalonia.Markers;
using Dsht.Gui.Avalonia.ViewModels;

namespace Dsht.Gui.Avalonia
{
    /// <summary>主窗口：取数据 + 管导航状态（主菜单 / 子菜单 / 布局），界面由 Shells 构建。
    /// 纪律：**GUI 只是呈现适配器** —— 不引用核心程序集，只运行 CLI 并解析标记行（V3.0 方案 §7.3）。
    /// 设计方向参考了 March7thAssistant（GPL-3.0）的做法，**未复制其任何代码、图标、字体或图片资源**。</summary>
    public partial class MainWindow : Window
    {
        /// <summary>主菜单（侧栏一级）。</summary>
        public static readonly string[] NavItems = new string[] { "概览", "看板", "会话与 Token", "形态与插件", "备份", "体检", "设置", "说明", "更新", "日志" };
        /// <summary>主菜单图标（FluentIcons，编译期检查）。</summary>
        public static readonly FluentIcons.Common.Symbol[] NavIcons = new FluentIcons.Common.Symbol[]
        {
            FluentIcons.Common.Symbol.Home, FluentIcons.Common.Symbol.DataBarVertical, FluentIcons.Common.Symbol.ChatMultiple,
            FluentIcons.Common.Symbol.PuzzlePiece, FluentIcons.Common.Symbol.Archive, FluentIcons.Common.Symbol.Shield,
            FluentIcons.Common.Symbol.Settings, FluentIcons.Common.Symbol.Question, FluentIcons.Common.Symbol.ArrowSync, FluentIcons.Common.Symbol.DocumentText
        };
        private static readonly string[][] NavCli = new string[][]
        {
            new string[] { "status", "--detail" },
            new string[] { "status", "--detail" },
            new string[] { "sessions" },
            new string[] { "profiles" },
            new string[] { "backup-list", "--detail" },
            new string[] { "doctor" },
            new string[] { "config-get" },
            new string[] { "describe" },
            new string[] { "update-center" },
            new string[] { "log", "--lines", "500" }
        };
        private static readonly string[][] NavSubs = new string[][]
        {
            new string[] { "概览", "原始输出" },
            new string[] { "指标", "图表" },   // ★ 看板重排（2026-10-09 ✓✓ 规格 §2-7 ✓）：指标 / 图表 双子标签 ✓
            new string[] { "整体", "父会话", "子代理", "统计" },   // 用户要求的三视图 + 原有统计 ✓
            new string[] { "原始输出" },
            new string[] { "原始输出" },
            new string[] { "原始输出" },
            new string[] { "原始输出" },
            new string[] { "原始输出" },
            new string[] { "检查" },
            new string[] { "日志" }
        };
        private static readonly string[][] NavDesc = new string[][]
        {
            new string[] { "这台机器的运行状态：dsh 是否在跑（含官方桌面端）+ 运行事实 + DeepSeek 余额；指标与图表的唯一主场在「看板」。", "status --detail 的标记行原文。" },
            // ★ 看板重排（2026-10-09 ✓✓ 规格 §2-1 ✓）：两行说明 → 一行 ≤40 字 ✓ 指标/图表 各一条 ✓
            new string[] { "KPI（会话/token/命中率/解码速度）+ 筛选 + 总计。", "七张趋势图：新增/命中率/token 消耗/热力/耗时/Token 分类/体检。" },
            new string[] { "逐条会话：标题、token、缓存命中率、解码速度、上下文压力（排序用工具栏的下拉）。", "汇总统计：总量、命中率、速度，以及最耗 token 的会话排行。" },
            new string[] { "每个 profile 启用了哪个形态（web/headless/acp）以及装了哪些插件（含第三方）。" },
            new string[] { "备份清单：每个备份的时间、范围与大小。" },
            new string[] { "体检：配置、日志、网络与安装完整性检查。" },
            new string[] { "当前配置项（脱敏后）。" },
            new string[] { "工具箱对当前安装的判断与依据。" },
            new string[] { "webui / 官方桌面端 / 本工具 / 已装插件 的版本与更新状态（**检查是只读的** ✓）。" },
            new string[] { "工具箱的操作日志（级别筛选 / 关键词 / 行数 / 导出）。筛选由 CLI 完成 ✓ 这里只显示。" }
        };

        private SessionsSnapshot _data;
        private ProfilesSnapshot _profiles;
        private StatusSnapshot _status;
        private BackupSummary _backups;
        private List<BackupItem> _backupItems = new List<BackupItem>();
        private List<ConfigItem> _config = new List<ConfigItem>();
        public List<BackupItem> BackupItems { get { return _backupItems; } }
        public List<ConfigItem> Config { get { return _config; } }
        /// <summary>待二次确认的破坏性操作（删除备份）；空=没有待确认项。</summary>
        public string PendingDelete = "";
        private List<SessionRowVm> _rows = new List<SessionRowVm>();
        private int _shell = Shells.Shells.Hybrid;     // 默认：混合式（主菜单 + 子菜单）
        private int _filter = SessionsView.FilterAll;
        private string _rawOutput = "";
        private int _mainSection = 1;                  // 默认停在「会话与 Token」
        private int _subTab;

        public StackPanel DetailHost;

        public MainWindow()
        {
            InitializeComponent();
            SetWindowIcon();
            BuildWindowChrome();
            InitChrome();
            for (int i = 0; i < 5; i++) BindShell(i);
            Closing += delegate(object s2, global::Avalonia.Controls.WindowClosingEventArgs ce)
            {
                // close_action：tray=最小化到托盘（可取消）/ ask=弹确认（可取消）/ 空|exit=直接退出 ✓
                string act = (_closeAction ?? "").Trim();
                if (act == "tray" && !_realExit) { ce.Cancel = true; HideToTray(); return; }
                if (act == "ask" && !_realExit) { ce.Cancel = true; ConfirmExitAsync(); return; }
            };
            for (int i = 0; i < 4; i++) BindStyle(i);
            // ★★★ 用户反馈「延迟还在」：根因不在切页慢，而在**点击后零反馈** ✗✗
            //   实测（2026-10-02）：体检页要跑 `doctor`（6.5s，含 npm registry 探测）、
            //   更新页要跑 `update-center`（7.9s，查 GitHub）—— 期间 BuildShell 一次都不执行，
            //   屏幕纹丝不动 ✓ 看起来就是"卡死" ✓✓
            //   ✓ 现在：首屏**先**画骨架 + 加载浮层（不等数据 ✓），数据到了再画一次 ✓
            // ★★★ 启动页可自选（2026-10-02 用户要求："启动默认打开页面设置里可自选" ✓✓）
            //   读 config 的 gui_start_page ✓（0..9 = NavItems 索引 ✓）—— 必须在**首帧之前**读好 ✗
            //   否则第一拍建在默认页上 ✗ → 同步读一次 config-get（~90ms ✓ 只此一次 ✓）
            //   读不到 / 越界 → 停在默认页 ✓ **绝不因偏好而拒绝启动** ✓✓
            try
            {
                string cfgText = Run(CliPath(), "config-get");
                string[] cfgLines = cfgText.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < cfgLines.Length; i++)
                {
                    string t3 = cfgLines[i].Trim();
                    if (t3.StartsWith("CONFIG gui_start_page ", StringComparison.Ordinal))
                    {
                        int np;
                        if (int.TryParse(t3.Substring("CONFIG gui_start_page ".Length).Trim(), out np) && np >= 0 && np < NavItems.Length) _mainSection = np;
                    }
                    // ★ 概览自动刷新（2026-10-02 ✓）：同一份 config-get 里顺手读 ✓ 不多起进程 ✓
                    if (t3.StartsWith("CONFIG gui_shell ", StringComparison.Ordinal))
                    {
                        // 布局偏好（2026-10-06 U4：顶栏切换器的选择被记住 ✓ 越界保持默认 ✗ 不猜 ✗）
                        int ns; if (int.TryParse(t3.Substring("CONFIG gui_shell ".Length).Trim(), out ns) && ns >= 0 && ns <= 4) _shell = ns;
                    }
                    if (t3.StartsWith("CONFIG gui_style ", StringComparison.Ordinal))
                    {
                        int nt; if (int.TryParse(t3.Substring("CONFIG gui_style ".Length).Trim(), out nt) && nt >= 0 && nt <= 3) Palette.Apply(nt);
                    }
                    if (t3.StartsWith("CONFIG gui_auto_refresh ", StringComparison.Ordinal))
                        ApplyAutoRefresh(t3.Substring("CONFIG gui_auto_refresh ".Length).Trim());
                    if (t3.StartsWith("CONFIG close_action ", StringComparison.Ordinal)) _closeAction = t3.Substring("CONFIG close_action ".Length).Trim();
                }
            }
            catch { }
            _loading = true;
            BuildShell();
            Refresh();
        }

        private readonly List<Button> _windowButtons = new List<Button>();

        /// <summary>自绘窗口标题栏（无边框窗口）：整条顶栏可拖动（落在按钮上的按下不算）、双击最大化/还原、
        /// 右侧三个窗口按钮。颜色由 ApplyChrome 按 Palette 刷，风格切换时自动跟随。</summary>
        private void BuildWindowChrome()
        {
            Border bar = this.FindControl<Border>("AppBar");
            StackPanel right = this.FindControl<StackPanel>("AppBarRight");
            if (bar != null)
            {
                bar.PointerPressed += delegate(object s, PointerPressedEventArgs e)
                {
                    if (IsFromButton(e.Source as Control)) return;
                    try { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); }
                    catch { /* 某些平台/状态下不允许拖动：忽略，不要因此崩 */ }
                };
                bar.DoubleTapped += delegate(object s, TappedEventArgs e) { if (IsFromButton(e.Source as Control)) return; ToggleMaximize(); };   // F18 FIX
            }
            if (right != null)
            {
                right.Children.Add(MakeWindowButton("—", delegate { WindowState = WindowState.Minimized; }, false, "最小化"));
                right.Children.Add(MakeWindowButton("□", delegate { ToggleMaximize(); }, false, "最大化 / 还原"));
                right.Children.Add(MakeWindowButton("✕", delegate { Close(); }, true, "关闭"));
            }
            RecolorWindowButtons();
        }

        /// <summary>按下是否来自某个按钮（含其后代）——是的话不要开始拖动窗口。</summary>
        private static bool IsFromButton(Control c)
        {
            Control cur = c;
            while (cur != null)
            {
                if (cur is Button) return true;
                cur = cur.Parent as Control;
            }
            return false;
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private Button MakeWindowButton(string glyph, Action onClick, bool danger, string tip)
        {
            Button b = new Button
            {
                Content = glyph,
                Width = 34,
                Height = 26,
                Padding = new Thickness(0),
                FontSize = 12,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            if (danger)
            {
                // 美学A6：关闭键 hover 警示色 ✓（只挂一次 ✓ Recolor 不重复挂 ✓）
                b.PointerEntered += delegate { b.Foreground = Palette.Bad; };
                b.PointerExited += delegate { b.Foreground = Palette.TextDim; };
            }
            ToolTip.SetTip(b, tip);
            b.Click += delegate { onClick(); };
            _windowButtons.Add(b);
            return b;
        }

        /// <summary>窗口按钮随主题重上色（关闭键用警示色，其余用次要文字色）。</summary>
        private void RecolorWindowButtons()
        {
            // §9 遗留修复（美学A6）：原来三元两分支相同 = 三个按钮恒 TextDim ✗
            // ✓ 现在：刷色只设基础色；关闭键的 hover 警示色在 MakeWindowButton 构造时挂一次 ✓
            //   （ApplyChrome 每次 BuildShell 都会调本方法 ✗ hover 绝不能在这里挂 → 会叠加 ✓）
            for (int i = 0; i < _windowButtons.Count; i++) _windowButtons[i].Foreground = Palette.TextDim;
        }
        // ---------------- 备份 / 设置 的操作（都走 CLI，异步，不阻塞界面） ----------------

        /// <summary>立即备份 ✓✓。**用户要求（2026-09-30）**：「第一次备份弹窗选择」✓✓
        ///   流程：**先跑一次 `backup`** ✓
        ///     · 成功 → 完事 ✓（已有备份时 CLI 会**沿用**上次的目录 ✓ 不追问 ✓）
        ///     · 返回「第一次备份必须指定目录」→ **弹文件夹选择器** ✓ → 再跑 `backup --to <选中的目录>` ✓✓
        ///   为什么这样：**判据放在 CLI 里** ✓ 界面不用自己猜"是不是第一次" ✓✓（单一事实来源 ✓）</summary>
        public async void CreateBackup()
        {
            // ★ 写动作也过闸门（2026-10-04 ✓）：这里原来是**独立的 async void** ✗ 绕开了 RunCliAction 的闸门 ✗
            //   → 连点三次 = **三个 `backup` 同时跑** ✗（三份包 + 各自触发保留份数清理 ✓ 同名文件互相踩 ✓）
            if (_actionBusy)
            {
                int bs = (int)(System.DateTime.UtcNow - _actionStartedAt).TotalSeconds;
                _actionLog = "「" + _actionBusyLabel + "」还在进行中（已 " + bs + " 秒）—— 等它结束再备份 ✓";
                ShowToast("「" + _actionBusyLabel + "」还在跑（已 " + bs + " 秒）· 请等它结束 ✗");
                BuildShell();
                return;
            }
            _actionBusy = true; _actionBusyLabel = "立即备份"; _actionStartedAt = System.DateTime.UtcNow;
            BuildShell();
            string outp;
            try { outp = await System.Threading.Tasks.Task.Run(delegate { return Run(CliPath(), "backup"); }); }
            finally
            {
                // ★ 第一步结束就**放开**闸门 ✓ —— 因为下面"第一次备份"的第二次调用走 `RunCliAction` ✓
                //   会由它**重新占用**闸门 ✓（这里不放的话，第二步会被自己的闸门拒掉 ✗✗ 那个 bug 更难查 ✓）
                _actionBusy = false; _actionBusyLabel = "";
            }
            // ★★★ **F13 修复（GUI 审计 MAJOR —— 靠中文判断成功）** ✓✓
            //   ✗ 原来用 `outp.IndexOf("第一次备份必须指定目录") < 0` 判断"成功了" ✗✗
            //     → 而那句话在 CLI 里是 `T(zh, en)` ✓ → **切到英文后判断永远失败** ✗
            //     → 第一次备份**永远做不成** ✓（把失败当结果弹出来 ✓ 弹窗选择器永不出现 ✗）
            //   ✓ 现在：**看机器标记** ✓✓（`BACKUP_OK` = 成功 ✓ `BACKUP_FAIL` = 失败 ✓）
            bool backupOk = outp != null && outp.IndexOf("BACKUP_OK", StringComparison.Ordinal) >= 0;
            // ★★★ **N9 修复（复审 MAJOR —— 判定太宽）** ✓✓
            //   ✗ 原来只认 `BACKUP_FAIL` ✗ → 而 CLI 对**每一种失败**都打它 ✓
            //     → 配置目录不存在 / `--to` 没生效 / 数据目录不存在 / 备份真的失败
            //       全都会**静默弹出文件夹选择器** ✗✗（真正的错误被吞掉 ✓）
            //   ✓ 现在：**只认 CLI 专用的 `BACKUP_NEEDS_DIR`** ✓✓ 其余错误照常显示 ✓
            bool needsFolder = !backupOk && outp != null && outp.IndexOf("BACKUP_NEEDS_DIR", StringComparison.Ordinal) >= 0;
            if (backupOk || !needsFolder)
            {
                // ✓✓ **用户反馈（2026-10-01）**：「现在备份还是受阻」✗
                //   真因：备份**成功了** ✓ 但这里**没刷新列表** ✗
                //     → 备份页的「共 N 份」还是旧数字 ✓ → **看起来像没成功** ✓✓
                //   ✓ 现在：**显示结果 + 立刻 Refresh()** ✓✓（列表马上更新 ✓）
                _actionLog = "备份结果：" + Environment.NewLine + (outp == null ? "" : outp.Trim());   // 审查 M2 修复：原来弹的是上一个动作的旧文案 ✗ 真实错误用户看不到 ✗✗
                InvalidateCliCache();   // MAJOR FIX: the backup just wrote; without this the list can serve a pre-backup cache entry
                BuildShell(); ShowToast(_actionLog);
                Refresh();   // ✓ 关键：刷新备份列表 ✓✓（原来漏了 ✓）
                return;
            }
            // 第一次 → 让用户选目录 ✓✓
            string dir = await PickFolder("选择备份目录（建议放在安装目录之外，例如 D:\\dsh-backups）");
            if (string.IsNullOrEmpty(dir))
            {
                _actionLog = "已取消备份 ✓（第一次备份需要先选一个目录 ✓）";
                BuildShell(); ShowToast(_actionLog);
                return;
            }
            RunCliAction("backup --to \"" + dir + "\"", "立即备份（第一次，写到 " + dir + "）");
        }

        /// <summary>弹系统文件夹选择器 ✓✓（Avalonia 的 StorageProvider ✓ 取消返回空串 ✓ 不猜 ✓）。</summary>
        // ★★ **注意（今晚第四次踩）**：本项目的命名空间是 `Dsht.Gui.Avalonia` ✗
        //   所以写 `Avalonia.Platform.Storage.X` 会被**相对解析**成 `Dsht.Gui.Avalonia.Platform.Storage.X` ✗✗
        //   → **凡是 `Avalonia.*` 都要写成 `global::Avalonia.*`** ✓✓（`Avalonia.Threading` / `Avalonia.Controls.Primitives` 都栽过 ✓）
        private async System.Threading.Tasks.Task<string> PickFolder(string title)
        {
            try
            {
                TopLevel top = TopLevel.GetTopLevel(this);
                if (top == null) return "";
                System.Collections.Generic.IReadOnlyList<global::Avalonia.Platform.Storage.IStorageFolder> res =
                    await top.StorageProvider.OpenFolderPickerAsync(new global::Avalonia.Platform.Storage.FolderPickerOpenOptions
                    {
                        Title = title,
                        AllowMultiple = false
                    });
                if (res == null || res.Count == 0) return "";
                return res[0].Path.LocalPath;
            }
            catch { return ""; }
        }

        public void ExportBackup(string name) { RunCliAction("backup-export --path \"" + name + "\" --yes --to " + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export"), "导出备份"); }

        /// <summary>删除一份备份 ✓。**用户反馈（2026-09-30）**：「gui 删除备份好像也有问题」✓✓
        ///   ✗ 原来 `backup-delete --path <名>` **没有 --yes** ✗
        ///     → 而 CLI 要求 `--yes` ✓（`BKDEL_PLAN … 确认请加 --yes` ✓ 它只打印计划**什么都不删** ✗）
        ///     → 界面上**两次点击确认都点完了** ✓ 结果**备份还在** ✓✓ **完全解释通了** ✓
        ///   ✓ 现在：**补上 --yes** ✓（界面的两次点击本身就是确认 ✓ 与 house style 一致 ✓）</summary>
        /// <summary>安装**可选的桥接插件** ✓✓（用户要求：「安装桥接插件有按钮吗」✓）。
        /// 它只读 ✓ 不联网 ✓ 不发模型请求 ✓ 不改 dsh 状态 ✓
        /// 装了 → 工具箱能显示「运行中」（运行态是进程内事实 ✓ 磁盘投影给不了 ✗）
        /// 不装 → 那一格显示 unknown ✓ 其余功能不缺 ✓✓
        /// 写操作 → 走**两次点击确认**（house style ✓ 与删除备份/清除数据一致 ✓）✓</summary>
        public void InstallBridge() { RunCliAction("bridge-install --yes", "安装桥接插件"); }
        /// <summary>删除备份 ✓✓。**用户要求（2026-09-30）**：「删除弹窗输入当前时间才执行」✓✓
        ///   理由：删除**不可逆** ✓ 光"再点一次"太容易手滑 ✓ → **必须看着时间手打一遍** ✓
        ///   CLI 侧同样有闸门 ✓（`--confirm-time` 与真实时间相差 &gt;120 秒 → 拒绝 ✓✓）</summary>
        // ================================================================ 备份位置

        /// <summary>当前备份位置 ✓✓（从 `backup-list` 的 `BACKUP_DIR <路径>` 行读 ✓ 读不到返回空 ✓ 不猜 ✓）。</summary>
        public string BackupDirText
        {
            get
            {
                try
                {
                    string raw = RawOutput ?? "";
                    string[] ls = raw.Replace("\r\n", "\n").Split('\n');
                    for (int i = 0; i < ls.Length; i++)
                    {
                        string l = ls[i] == null ? "" : ls[i].Trim();
                        if (l.StartsWith("BACKUP_DIR ", StringComparison.Ordinal)) return l.Substring("BACKUP_DIR ".Length).Trim();
                    }
                }
                catch { }
                return "";
            }
        }

        /// <summary>改备份位置 ✓✓（用户要求：「备份路径在备份页面里设置并且显示吧」✓）。
        /// 弹系统文件夹选择器 ✓ → `backup-dir --set <目录>` ✓ → CLI 会校验并持久化 ✓✓。</summary>
        public async void ChangeBackupDir()
        {
            string dir = await PickFolder("选择备份位置（建议放在安装目录之外，例如 D:\\dsh-backups）");
            if (string.IsNullOrEmpty(dir)) { _actionLog = "已取消更改备份位置 ✓"; BuildShell(); ShowToast(_actionLog); return; }
            RunCliAction("backup-dir --set \"" + dir + "\"", "更改备份位置");
        }

        /// <summary>恢复默认备份位置 ✓✓（删掉持久化文件 ✓ 回到 `StateDir/backup` ✓）。</summary>
        public void ResetBackupDir() { RunCliAction("backup-dir --reset", "恢复默认备份位置"); }

        public async void DeleteBackup(string name)
        {
            string now = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string typed = await PromptDialog.Ask(this, "删除备份",
                "删除「" + name + "」**不可恢复** ✓ 请输入**当前时间**以确认：",
                "打开时已全选 ✓ 可直接 Ctrl+C 复制上面提示的时间再粘贴 ✓ 格式 yyyy-MM-dd HH:mm:ss ✓",
                now);
            if (typed == null) { _actionLog = "已取消删除 ✓"; BuildShell(); ShowToast(_actionLog); return; }
            RunCliAction("backup-delete --path \"" + name + "\" --yes --confirm-time \"" + typed.Trim() + "\"", "删除备份");
        }

        public void DryRunRestore(string name) { RunCliAction("restore --dry-run --path \"" + name + "\"", "恢复预览"); }

        /// <summary>应用恢复。**只在隔离数据根里允许**（CLI 自己的准入闸门会拒绝其它情况，界面把它的话原样显示）。</summary>
        /// <summary>应用恢复。**两次点击确认**（美学A2：高危操作里只有它原来一次点击 ✗
        /// 删除备份输时间 / 隔离插件 / 更新 web 都是两次 ✓ 统一 ✓）。CLI 的隔离闸门仍在 ✓✓</summary>
        public void ApplyRestore(string name)
        {
            string key = "restore|" + name;
            if (PendingDelete != key) { PendingDelete = key; Rebuild(); return; }
            PendingDelete = "";
            RunCliAction("restore --path \"" + name + "\" --apply", "应用恢复");
        }

        public void SetConfig(string key, string value) { InvalidateCfgCache(); RunCliAction("config-set " + key + " \"" + (value == null ? "" : value.Replace("\"", "")) + "\"", "保存设置 " + key); }

        // —— 动作串行闸门（2026-10-04 用户在工作电脑上实测："更新可以连续点好几次" ✗✗）——
        private bool _actionBusy;
        private string _actionBusyLabel = "";
        private System.DateTime _actionStartedAt = System.DateTime.MinValue;
        /// <summary>有 CLI 动作在跑 ✓（更新 / 启动 / 备份 / 恢复…）。界面据此禁用按钮 ✗ 不排队 ✗。</summary>
        public bool ActionBusy { get { return _actionBusy; } }
        /// <summary>正在跑的动作名 ✓（界面显示"进行中：更新 dsh"）。</summary>
        public string ActionBusyLabel { get { return _actionBusyLabel; } }

        private void RunCliAction(string args, string label)
        {
            // ★★ 动作串行闸门（2026-10-04 用户在工作电脑上实测："更新可以连续点好几次" ✗✗）
            //   ✗ 原来每次点击都 fire-and-forget 起一个 CLI 进程 ✗
            //     → `update --yes` 连点三次 = **三个更新同时跑** ✗✗
            //       （npm 安装互相踩 + 三次备份 + 回滚点互相覆盖 ✓ 真机上就是这样 ✓）
            //   ✓ 现在：**同时只允许一个动作** ✓ 正在跑时新的点击**如实拒绝并说明原因** ✗ 不排队 ✗
            //     （排队会让用户以为点的是别的东西、还会在长任务后面堆一串 ✗ 这里选择"说清楚" ✓✓）
            if (_actionBusy)
            {
                int secs = (int)(System.DateTime.UtcNow - _actionStartedAt).TotalSeconds;
                _actionLog = "「" + _actionBusyLabel + "」还在进行中（已 " + secs + " 秒）—— 等它结束再操作 ✓ 没有排队的第二个动作 ✓";
                ShowToast("「" + _actionBusyLabel + "」还在跑（已 " + secs + " 秒）· 请等它结束 ✗");
                BuildShell();
                return;
            }
            _actionBusy = true;
            _actionBusyLabel = label;
            _actionStartedAt = System.DateTime.UtcNow;
            _actionLog = "已发起" + label + "…";
            BuildShell();
            _ = RunCliActionAsync(args, label);
        }

        /// <summary>审查 M1 修复：日志/弹窗里的秘密值打码 ✓ —— config-set balance_key "sk-xxx" → config-set balance_key "***"。
        /// 找不到引号对就原样返回 ✓（宁可不打码也不截断 ✗ —— 不猜 ✗）。</summary>
        private static string MaskSecrets(string args)
        {
            if (string.IsNullOrEmpty(args)) return args;
            int i = args.IndexOf("balance_key", StringComparison.Ordinal);
            if (i < 0) return args;
            int q1 = args.IndexOf('"', i);
            if (q1 < 0) return args;
            int q2 = args.IndexOf('"', q1 + 1);
            if (q2 < 0) return args;
            return args.Substring(0, q1 + 1) + "***" + args.Substring(q2);
        }

        private async System.Threading.Tasks.Task RunCliActionAsync(string args, string label)
        {
          try
          {
            string cli = CliPath();
            // ✗ 原来等待期间**什么都不显示** → 备份 818MB 要几秒，用户感觉"卡住" ✓
            // 现在**先显示"进行中…"** ✓（与体检页同一办法 ✓）→ 用户知道它在干活 ✓✓
            _actionLog = label + "进行中…（" + MaskSecrets(args) + "）";   // 审查 M1 修复：secret 键的值绝不能进可见文案 ✗
            // ★★★ **F4 修复（GUI 审计 MAJOR —— 我上一轮清早了）** ✓✓
            //   ✗ 原来在**写之前**就 `InvalidateCliCache()` ✗ → 紧接着的 `Refresh()`（318 行 ✓）
            //     **把写前状态缓存进去了** ✗✗ → 写完成后最后一次 `Refresh()`（321 行 ✓）
            //     **命中了那份写前缓存** ✗ → **界面最多 4 秒还显示旧状态** ✗✗
            //     ← 正是我在提交信息里说"不会发生"的那件事 ✓ **打脸** ✓
            //   ✓ 现在：**写完成后、最终 Refresh 之前**再清** ✓✓（见 320 行之后 ✓）
            Refresh();
            string outp = cli == null ? "未找到工具箱 CLI。" : await System.Threading.Tasks.Task.Run(delegate { return Run(cli, args); });
            // U9：动作结果的级别 → toast 边框色（error=红 / warn=橙 / info=中性 ✓ 成功不再顶橙边 ✓）
            _lastActionLevel = "info";
            if (outp != null)
            {
                if (outp.IndexOf("_FAIL", StringComparison.Ordinal) >= 0) _lastActionLevel = "error";
                else if (outp.IndexOf("WARN", StringComparison.Ordinal) >= 0 || outp.IndexOf("未生效", StringComparison.Ordinal) >= 0 || outp.IndexOf("不完整", StringComparison.Ordinal) >= 0 || outp.IndexOf("拒绝", StringComparison.Ordinal) >= 0) _lastActionLevel = "warn";
            }
            _actionLog = label + "结果：" + Environment.NewLine + outp.Trim();
            InvalidateCliCache();   // F4 FIX: 写**完成**之后清 ✓ 此时缓存里必然是写前状态 ✓ 紧接着的 Refresh 会重新跑命令 ✓✓
            Refresh();
            // 启动成功后**自动打开浏览器** ✓✓（用户点"启动"就是想用它 ✓）
            // 失败时不打开 ✗；"已在运行"也算成功 ✓（那时打开正好能用 ✓）
            if (args != null && args.StartsWith("start", StringComparison.Ordinal) && outp.IndexOf("START_FAIL", StringComparison.Ordinal) < 0)
                // ✗ 原来打开的是**硬编码裸端口** → dsh 会要求 token → 认证失败 ✗
                // （2026-09-30 真机反馈："dsh web authentication required; reopen the URL printed by dsh web" ✓）
                // 现在只用 CLI 报的 START_URL ✓；取不到就**不打开** ✗（宁可不跳，也不跳到一个必然失败的地址 ✓）
                {
                    string su = "";
                    int ui = outp.IndexOf("START_URL ", StringComparison.Ordinal);
                    if (ui >= 0)
                    {
                        int end = outp.IndexOf('\n', ui);
                        su = (end < 0 ? outp.Substring(ui + 10) : outp.Substring(ui + 10, end - ui - 10)).Trim();
                    }
                    if (su.StartsWith("http", StringComparison.OrdinalIgnoreCase)) OpenUrl(su);
                }
          }
          finally
          {
              // ★ 动作结束 → **必须**放开闸门 ✓（成功 / 异常 / 提前 return 都要放开 ✗
              //   否则按钮会永久卡在"进行中" ✗✗ —— 这比重复点击更难查 ✓）
              _actionBusy = false;
              _actionBusyLabel = "";
              BuildShell();
          }
        }
        /// <summary>看板上的操作日志（一键启动/停止的结果，原样展示给用户）。</summary>
        private string _actionLog = "";
        private string _lastActionLevel = "info";   // U9：最近一条动作结果的级别（toast 边框着色用 ✓）
        public string ActionLog { get { return _actionLog; } }

        /// <summary>一键启动 dsh（调用工具箱核心的 `start`：非交互，GUI 用）。</summary>
        /// <summary>一键启动的方式：0 = webui（dsh web，走 CLI ✓）；1 = desktop（官方桌面端应用 ✓）。
        /// （用户要求："一键启动按钮底下可选默认启动 desktop 还是 webui" ✓✓）</summary>
        public int StartMode = 0;
        /// <summary>看板图表的日期范围（天 ✓ 7/14/30 可切 ✓）。</summary>
        public int ChartDays = 14;
        // —— 排障开关的值（从 CLI 的 config-get 读 ✓ 用户要求的那三个 ✓）——
        public string BrowserMode = "auto";
        public bool UiParallel = true;
        public void SetChartDays(int d) { ChartDays = d; BuildShell(); }
        public void SetStartMode(int m) { StartMode = m; BuildShell(); }

        /// <summary>一键部署：按当前方式行动 ✓（用户要求："web/desktop 也要加一键部署，desktop 直接官网下安装包就行" ✓✓）
        /// · webui   → 调 CLI 的 install（装/升级 dsh 本体 ✓ 官方 npm 包 ✓）
        /// · desktop → **打开官方下载页** ✓（本工具不重打包、不改官方安装包 ✓）</summary>
        public void DeployForMode()
        {
            if (StartMode == 1)
            {
                bool win = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                bool mac = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
                if (!win && !mac)
                {
                    _actionLog = "官方桌面端**暂未发行 Linux 版**（官方目前只提供 Windows 与 macOS）。\nLinux 请用「webui」方式一键部署：装 dsh 本体 + 启动 dsh web。";
                    Refresh();
                    return;
                }
                _actionLog = "官方桌面端请在**官方安装页**下载（那是官方自己的安装包，本工具不重打包、也不改它）：\nhttps://www.deepseek.com/harness/\n装好后回到这里，把方式切到 desktop 点「一键启动」即可。";
                OpenUrl("https://www.deepseek.com/harness/");
                Refresh();
                return;
            }
            RunCliAction("install --yes", "一键部署 dsh（webui）");
        }

        public void StartDsh()
        {
            if (StartMode == 1) { StartDesktopApp(); return; }
            RunCliAction("start --yes", "启动");
        }

        /// <summary>启动**官方桌面端**（Electron 应用 ✓ 独立安装 ✓ 不走 3080 ✓）。
        /// 找不到就**如实说明** ✓ —— Linux 上官方**暂未发行** ✓（用户要求："如果 Linux 没有，就提示暂未发行" ✓✓）</summary>
        public void StartDesktopApp()
        {
            try
            {
                bool win = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                bool mac = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
                if (!win && !mac)
                {
                    _actionLog = "官方桌面端**暂未发行 Linux 版**（官方目前只提供 Windows 与 macOS）。\nLinux 上请用「webui」方式：启动 dsh web 后在浏览器里打开。";
                    Refresh();
                    return;
                }
                if (win)
                {
                    string p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DeepSeek Harness", "DeepSeek Harness.exe");
                    if (System.IO.File.Exists(p)) { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); _actionLog = "已启动官方桌面端：" + p; Refresh(); return; }
                    _actionLog = "没找到官方桌面端。默认安装位置：\n" + p + "\n装好后这个按钮就能直接启动它。";
                    Refresh();
                    return;
                }
                Process.Start(new ProcessStartInfo("open", "-a \"DeepSeek Harness\"") { UseShellExecute = false });
                _actionLog = "已尝试启动官方桌面端（macOS）。";
                Refresh();
            }
            catch (Exception ex) { _actionLog = "启动官方桌面端失败：" + ex.Message; Refresh(); }
        }

        /// <summary>停止 dsh（核心的 `stop`）。</summary>
        public void StopDsh() { RunCliAction("stop --yes", "停止"); }

        /// <summary>只停 **web**（3080 ✓ 不动桌面端 ✓）。两个都开着时用 ✓✓</summary>
        public void StopWebOnly() { RunCliAction("stop --yes", "停止 web"); }

        /// <summary>检查更新 ✓（**只读** ✓ 调 CLI 的 `update-info` ✓ 结果进右下角 toast ✓✓）。</summary>
        public void CheckUpdate() { RunCliAction("update-info", "检查更新"); }

        /// <summary>更新 dsh web ✓ —— **两次点击确认** ✓（沿用删除备份那套 pending 模式 ✓ 一致 ✓）。
        /// CLI 的 `update` 会**先自动备份** ✓ 并保留回滚点 ✓✓。</summary>
        public void ConfirmUpdateWeb()
        {
            string key = "upd:webui";
            if (PendingDelete != key) { PendingDelete = key; Rebuild(); return; }
            PendingDelete = "";
            RunCliAction("update --yes", "更新 dsh web");
        }

        /// <summary>打开官方桌面端安装页 ✓（用户指定：desktop 走 https://www.deepseek.com/en/harness/ ✓）。</summary>
        public void OpenDesktopPage() { OpenUrl("https://www.deepseek.com/en/harness/"); }

        // —— 日志中心的筛选状态 ✓✓（roadmap Phase 2："CLI 已有 log，缺 GUI 表面" ✓）
        //   筛选**走 CLI 的参数** ✓（--level / --grep / --lines ✓）→ 与"GUI 只调 CLI"架构一致 ✓
        public string LogFilter = "";
        public int LogLines = 500;
        public string LogGrep = "";

        /// <summary>拼出 log 命令（带当前筛选 ✓）。空筛选不加对应参数 ✓。</summary>
        public string LogArgs()
        {
            StringBuilder sb = new StringBuilder("log --lines " + LogLines);
            if (!string.IsNullOrEmpty(LogFilter)) sb.Append(" --level ").Append(LogFilter);
            if (!string.IsNullOrEmpty(LogGrep)) sb.Append(" --grep \"").Append(LogGrep.Replace("\"", "")).Append("\"");
            return sb.ToString();
        }

        public void SetLogFilter(string v) { LogFilter = v; Refresh(); }
        public void SetLogLines(int n) { LogLines = n; Refresh(); }
        public void SetLogGrep(string v) { LogGrep = v; Refresh(); }

        /// <summary>导出日志 ✓（走 CLI 的 `--export` ✓ **CLI 自己带 --yes 闸门** ✓ 这里显式加 --yes ✓
        /// 导出到临时目录 ✓ 结果路径进右下角 toast ✓）。</summary>
        // —— 清除数据 ✓✓（roadmap Phase 2："CLI 已有 wipe，缺 GUI 表面" ✓）
        //   **先看计划**（只读 ✓ 不加 --yes 时 CLI 只输出 WIPE_PLAN ✓ 一个字节都不删 ✓）
        public void WipePlan() { RunCliAction("wipe", "清除计划（只读，未删除任何东西）"); }

        // **真清除** ✓（`--yes` ✓ → CLI 会**先做安全备份且必须成功** ✓ 再删 ✓
        //   `WIPE_PRE_BACKUP <路径>` 会显示在页面上 ✓ → 用户能看到安全备份在哪 ✓✓）
        /// <summary>**已停用** ✓✓（用户要求删除清除数据操作 ✓ 只保留"显示手动删除路径" ✓）。
        /// 留着方法体是为了不动调用点 ✓ 但它**不再做任何删除** ✓（CLI 的 wipe 本身也永不删除了 ✓✓）。</summary>
        public void DoWipe() { RunCliAction("wipe", "查看手动删除路径（已停用清除 ✓ 只读 ✓）"); }

        public void ExportLog()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-minato-log-export.txt");
            RunCliAction(LogArgs() + " --export \"" + path + "\" --yes", "导出日志到 " + path);
        }

        /// <summary>执行更新 ✓（CLI 的 `update` ✓ **先备份再更新** ✓ 有回滚点 ✓；结果进 toast ✓）。</summary>
        public void RunUpdate() { RunCliAction("update --yes", "更新 dsh"); }

        /// <summary>只停**官方桌面端**（Electron 多进程 → CLI 用 StopTree 杀整棵 ✓ 不动 web ✓）。</summary>
        public void StopDesktopOnly() { RunCliAction("stop --target desktop --yes", "停止桌面端"); }

        private void RunCoreAction(string verb, string label)
        {
            string core = ToolkitCore();
            if (core == null)
            {
                _actionLog = "无法" + label + "：" + CoreMissingText();
                BuildShell();
                return;
            }
            _actionLog = "已发起" + label + "…（" + Path.GetFileName(core) + " " + verb + "）";
            BuildShell();
            string outp = RunQuick(core, verb, 8);
            _actionLog = label + "结果：" + Environment.NewLine + outp.Trim();
            Refresh();
        }

        /// <summary>短超时运行（启动/停止这类命令可能一直挂着，不能把界面卡住 30 秒）。</summary>
        private static string RunQuick(string cli, string args, int seconds)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(cli, args);
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = new UTF8Encoding(false);
                psi.StandardErrorEncoding = new UTF8Encoding(false);
                using (Process p = Process.Start(psi))
                {
                    StringBuilder sb = new StringBuilder();
                    string err = "";
                    System.Threading.Tasks.Task<string> soT = System.Threading.Tasks.Task.Run(delegate { return p.StandardOutput.ReadToEnd(); });
                    System.Threading.Tasks.Task<string> seT = System.Threading.Tasks.Task.Run(delegate { return p.StandardError.ReadToEnd(); });
                    if (!p.WaitForExit(seconds * 1000)) { try { p.Kill(); } catch { } return "（超过 " + seconds + " 秒未结束，已结束该进程）"; }
                    sb.Append(soT.Result);
                    err = seT.Result;
                    if (!string.IsNullOrEmpty(err)) sb.Append(Environment.NewLine).Append("[stderr] ").Append(err);
                    return sb.Length == 0 ? "（无输出）" : sb.ToString();
                }
            }
            catch (Exception ex) { return "执行失败：" + ex.Message; }
        }
        /// <summary>窗口图标（任务栏/标题栏）。资源 URI 用**程序集名** dsht-gui —— 用命名空间会静默失败。</summary>
        private void SetWindowIcon()
        {
            try
            {
                using (System.IO.Stream s = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://dsht-gui/Assets/logo-icon.png")))
                {
                    Icon = new WindowIcon(s);
                }
            }
            catch { /* 图标缺失不影响功能 */ }
        }
        /// <summary>核心程序不可用时的说明（区分"平台不支持"与"文件缺失"）。</summary>
        private static string CoreMissingText()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                return "此功能需要 Windows 专有的 v2.x 核心（DeepSeek Harness Toolkit.exe）—— 当前平台不支持。"
                     + Environment.NewLine + "跨平台可用的替代：状态/概览/会话/token/形态与插件/备份清单/设置（都走 V3 CLI）。";
            return "未找到工具箱核心程序（DeepSeek Harness Toolkit.exe）：请把它与 GUI 放在同一目录，或设置环境变量 DSHT_CORE。";
        }
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // ---------------- 窗口壳配色（跟随 Palette，风格切换时重刷） ----------------

        /// <summary>顶栏切换器的内容与提示（只装一次；颜色在 ApplyChrome 里刷）。</summary>
        private void InitChrome()
        {
            Symbol[] shellIcons = new Symbol[] { Symbol.PanelLeft, Symbol.Tab, Symbol.Grid, Symbol.PanelRight, Symbol.Board };
            for (int i = 0; i < 5; i++)
            {
                Button b = this.FindControl<Button>("Shell" + i);
                if (b == null) continue;
                b.Content = new SymbolIcon { Symbol = shellIcons[i], IconVariant = IconVariant.Regular, FontSize = 15 };
                ToolTip.SetTip(b, "布局：" + Shells.Shells.Name(i));
            }
            for (int i = 0; i < 4; i++)
            {
                Button b = this.FindControl<Button>("Style" + i);
                if (b == null) continue;
                b.Content = new TextBlock { Text = ((char)('A' + i)).ToString(), FontSize = 12, FontWeight = FontWeight.SemiBold };
                ToolTip.SetTip(b, "风格：" + Palette.StyleName(i));
            }
        }

        /// <summary>把 Palette 应用到窗口壳：主题变体（让 Fluent 控件跟随明暗）、顶栏、两个 segmented 切换器的激活态。</summary>
        private void ApplyChrome()
        {
            Background = Palette.PageBg;
            if (Application.Current != null)
                Application.Current.RequestedThemeVariant = Palette.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

            Border appBar = this.FindControl<Border>("AppBar");
            if (appBar != null) { appBar.Background = Palette.SidebarBg; appBar.BorderBrush = Palette.Border; }

            Border mark = this.FindControl<Border>("BrandMark");
            if (mark != null)
                mark.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(((SolidColorBrush)Palette.Accent).Color, 0),
                        new GradientStop(((SolidColorBrush)Palette.AccentHover).Color, 1)
                    }
                };
            SetFg("BrandTitle", Palette.Text);
            SetFg("BrandSub", Palette.TextFaint);

            Border pill = this.FindControl<Border>("DisclaimerPill");
            if (pill != null) pill.Background = Palette.WarnSoft;
            SetFg("DisclaimerIcon", Palette.Warn);
            SetFg("DisclaimerText", Palette.TextDim);

            RecolorWindowButtons();
            PaintSwitch("ShellSwitch");
            PaintSwitch("StyleSwitch");
            // ★★ **F8 修复（GUI 审计 MINOR）** ✓✓：XAML 里是 Shell0/1/2/**Shell4**（没有 Shell3 ✓）
            //   ✗ 循环只到 4 ✗ → **默认布局 Shell4 从不被 paint** ✗
            //     → ① **默认布局没有高亮** ✗ ② 它是**原生按钮形状** ✗ → 与旁边三个图标按钮**宽度不一致** ✗✗
            //       （这正是用户报过的"按钮宽度不一样" ✓）
            //   ✓ 现在：**循环到 5** ✓（Shell3 不存在 ✓ PaintSwitchButton 自己有 null 检查 ✓✓）
            for (int i = 0; i < 5; i++) PaintSwitchButton("Shell" + i, i == _shell);
            // ✓✓ **用户要求（2026-09-30）**：「窗口不要始终置顶，要始终置顶至少加个按钮」✓
            //   ✗ 之前窗口被**外部**（我的截图脚本）设成 topmost ✗ → 一直压在最上面 ✓ 很烦 ✓
            //   ✓ 现在：**由用户自己控制** ✓✓ 默认**不置顶** ✓ 点一下才置顶 ✓ 再点取消 ✓
            //   ✓ 选择只对**本次运行**有效 ✓✓（不写配置 ✓ —— `always_on_top` 从来不是 CLI 认识的键 ✓
            //     写进去只会让配置文件多一个没人读的项 ✓ 之前的注释说"下次启动恢复"是**错的** ✗）
            PaintPin();
            for (int i = 0; i < 4; i++) PaintSwitchButton("Style" + i, i == Palette.StyleKind);
        }

        private void SetFg(string name, IBrush fg)
        {
            TextBlock t = this.FindControl<TextBlock>(name);
            if (t != null) t.Foreground = fg;
        }

        private void PaintSwitch(string name)
        {
            Border b = this.FindControl<Border>(name);
            if (b != null) { b.Background = Palette.InsetBg; b.BorderBrush = Palette.Border; b.BorderThickness = new Thickness(1); }
        }

        // ================================================================ 置顶开关

        /// <summary>窗口是否置顶 ✓✓。**用户要求**：「窗口不要始终置顶，要始终置顶至少加个按钮」✓
        /// 默认 **false**（不置顶 ✓ 这是正常窗口的行为 ✓）；点按钮切换 ✓ 并存进配置 ✓。</summary>
        private bool _alwaysOnTop;
        private bool _pinPainted;

        private void PaintPin()
        {
            Button b = this.FindControl<Button>("PinBtn");
            if (b == null) return;
            // ✓✓ **用户要求（2026-09-30）**：「置顶放在 abcd 右边，换成图标」✓✓
            //   · 位置：Axaml 里已移到 `StyleSwitch`（A B C D）**右边** ✓✓
            //   · 图标：FluentIcons 的 `Symbol` 里**没有裸 `Pin`** ✗（试过报错 ✓ 扫了 dll 的字符串表确认 ✓）
            //     → 用 **`Symbol.NotePin`** ✓✓（就是个图钉 ✓ 存在 ✓ 置顶时变 accent 色 ✓）
                        // ✓ `Ic()` 是 `Shells` 的 **private static** ✗ → `MainWindow` 里用不了 ✗（试过报错 ✓）
            //   → 直接 new 一个 `SymbolIcon` ✓✓（`FluentIcons.Avalonia` 已在 using 里 ✓）
            b.Content = new SymbolIcon
            {
                Symbol = Symbol.NotePin,
                FontSize = 14,
                Foreground = _alwaysOnTop ? Palette.Accent : Palette.TextDim,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.MinWidth = 30;   // ✓ 图标按钮用图标尺寸 ✓（用户之前报过"宽体普京" ✗ 教训 ✓）
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.Padding = new Thickness(8, 5);
            b.CornerRadius = new CornerRadius(7);
            b.BorderThickness = new Thickness(0);
            b.Background = _alwaysOnTop ? Palette.AccentSoft : Brushes.Transparent;
            ToolTip.SetTip(b, _alwaysOnTop ? "窗口已置顶 ✓ 点一下取消 ✓" : "点一下让窗口始终置顶 ✓");
            if (!_pinPainted)
            {
                _pinPainted = true;
                b.Click += delegate
                {
                    _alwaysOnTop = !_alwaysOnTop;
                    Topmost = _alwaysOnTop;
                    PaintPin();
                    // ✓ 存进配置 ✓ 下次启动恢复 ✓（写操作 → 走 CLI ✓ 失败也不影响本次切换 ✓）
                    // ★★★ **F7 修复（GUI 审计 MAJOR —— 实测 `CONFIGSET_FAIL unknown-key`）** ✓✓
                    //   ✗ 原来写 `config-set always_on_top on` ✗ —— CLI 的 `ConfigValidator` **没有这个键** ✗✗
                    //     → 每次点击都弹一条 unknown-key 的**失败 toast** ✗
                    //     → 而注释说"下次启动恢复" ✗ **是假的** ✓（`_alwaysOnTop` 永远从 false 开始 ✓ 从不读回 ✓）
                    //   ✓ 现在：**改成"仅本次会话"** ✓✓ 并且**如实说明** ✓
                    //     （要真正持久化得先在 CLI 加键 ✓ 那是另一件事 ✓ **不能假装已经持久化** ✗✓）
                    _actionLog = _alwaysOnTop ? "窗口已置顶 ✓（仅本次运行 ✓ 重启后不保留 ✓）" : "已取消置顶 ✓";
                    ShowToast(_actionLog);
                };
            }
        }

        private void PaintSwitchButton(string name, bool active)
        {
            Button b = this.FindControl<Button>(name);
            // ★★ **用户反馈（2026-09-30）**：「概览底下的五个按钮宽度也不一样」✓✓
            //   ✗ 它们在 `StackPanel Orientation=Horizontal` 里 ✗ → **宽度 = 各自文字宽度** ✗
            //     （「侧栏式」3 字 ✓「顶部标签式」5 字 ✓「卡片网格」4 字 ✓ → 三个宽度 ✓）
            //   ✓ 现在：**统一 MinWidth + 文字居中** ✓✓
            //     · 短文字被撑到同一宽度 ✓ 长文字仍可自然变宽 ✓（不会被截断 ✓）
            //     · 这一处同时覆盖 `ShellSwitch`（5 个壳 ✓）与 `StyleSwitch`（4 个样式 ✓）✓✓
            // ★★★ **修正（2026-09-30 用户反馈"宽体普京"）** ✓✓
            //   ✗ 我上一轮加 `MinWidth = 86` 时**没看内容类型** ✗✗：
            //     · `ShellSwitch` 的 5 个按钮装的是 **SymbolIcon**（图标 ✓）不是文字 ✗
            //     · `StyleSwitch` 的 4 个是**文字**（A B C D ✓）
            //     → 图标被撑成 **86px 宽条** ✓ 用户形容"宽体普京" ✓✓
            //   ✓ 现在：**按内容类型分别给** ✓
            //     · 图标按钮 → **34px 方形** ✓（图标居中 ✓ 与图标本体大小相称 ✓）
            //     · 文字按钮 → **86px 等宽** ✓（这是用户原本的诉求 ✓ 短文字不再长短不一 ✓）
            if (b != null)
            {
                // ★★★ **用户反馈（2026-09-30 两次）** ✓✓：
                //   第一次"五个按钮宽度不一样" → 我加了 MinWidth=86 ✗
                //   第二次"宽体普京/普及" → 我改成"图标 34 / 文字 86" ✗✗ **还是错** ✓
                //     · `ShellSwitch` = 图标 ✓ 已修好 ✓
                //     · `StyleSwitch` = **单个字母 A B C D** ✗ —— **86px 对单字符当然还是太宽** ✗✗
                //   ✓ 现在：**按内容长度判** ✓✓（这才是对的判据 ✓）
                //     · 图标 → **34 方形** ✓
                //     · **短文字（≤2 字符 ✓ 如 A B C D）→ 34** ✓✓
                //     · 长文字（如"侧栏式"这种将来若改成文字 ✓）→ 86 等宽 ✓
                bool compact = b.Content is SymbolIcon;
                if (!compact)
                {
                    TextBlock tb0 = b.Content as TextBlock;
                    string txt = tb0 != null ? tb0.Text : (b.Content as string);
                    if (txt != null && txt.Length <= 2) compact = true;
                }
                // ★★★ **用户第三次反馈（2026-09-30）**：「五个按钮宽度不一样」✓✓
                //   ✗ 我用的是 `MinWidth` ✗ —— **它只是下限** ✓
                //     → 内容比下限宽时**照样撑开** ✗ → 两组（图标组 / A B C D 组）**视觉上不齐** ✗✗
                //   ✓ 现在：**紧凑的用固定 Width** ✓✓（绝对等宽 ✓）
                //     · 图标 ✓ 与 ≤2 字符短文字（A B C D ✓）→ **Width = 34 固定** ✓✓
                //     · 更长的文字 → MinWidth = 86 ✓（等宽 ✓ 且不会被截断 ✓）
                if (compact) { b.Width = 34; b.MinWidth = 0; }
                else { b.MinWidth = 86; }
                b.HorizontalContentAlignment = HorizontalAlignment.Center;
            }
            if (b == null) return;
            b.Padding = new Thickness(10, 5);
            b.CornerRadius = new CornerRadius(7);
            b.BorderThickness = new Thickness(0);
            b.Background = active ? (IBrush)Palette.CardBg : Brushes.Transparent;
            IBrush fg = active ? Palette.Accent : Palette.TextDim;
            SymbolIcon si = b.Content as SymbolIcon;
            if (si != null) si.Foreground = fg;
            TextBlock tb = b.Content as TextBlock;
            if (tb != null) tb.Foreground = fg;
        }

        // ---------------- 给 Shells 用的状态 ----------------

        public SessionsSnapshot Data { get { return _data; } }
        public ProfilesSnapshot Profiles { get { return _profiles; } }
        public StatusSnapshot Status { get { return _status; } }
        public BackupSummary Backups { get { return _backups; } }
        public DoctorSummary Doctor { get { return _doctor; } }
        private DoctorSummary _doctor;
        public int ProfilesFilter { get; set; }
        public string ProfileSearch = "";
        /// <summary>待二次确认的隔离操作（"profile|entryId"）；空=没有待确认项。写操作必须点两次。</summary>
        public string PendingPatch = "";
        public void Rebuild() { BuildShell(); }

        public void SetProfilesFilter(int mode) { ProfilesFilter = mode; BuildShell(); }

        /// <summary>健康检查（profilecheck）的原始输出（懒加载一次）。</summary>
        public string Health { get { return _health; } }
        private string _health = "";
        private bool _healthBusy = false;   // F12 FIX: explicit in-progress flag
        public void LoadHealth() { _ = LoadHealthAsync(); }

        /// <summary>体检：**异步**跑 profilecheck + doctor ✓✓
        /// 原来这两次调用是同步的、跑在 UI 线程上 ✗ —— doctor 要查网络，最坏各 30 秒超时
        /// → 点"运行检查"会让整个窗口冻结近一分钟 ✗（2026-09-30 真机反馈"体检页面卡住" ✓）
        /// 现在：先立刻显示"检查中…"（页面有反馈 ✓），两次调用都在后台线程 ✓，完成后刷新 ✓</summary>
        private async System.Threading.Tasks.Task LoadHealthAsync()
        {
            if (_healthBusy) { BuildShell(); return; }
            string cli = CliPath();
            if (cli == null) { _health = "未找到工具箱 CLI。"; BuildShell(); return; }
            _healthBusy = true;
            // F12 residual (GUI final review): the comment above promised an immediate "checking…" line but no
            // such text existed anywhere, so the page stayed blank for up to ~30 s. Set it before the rebuild.
            _health = "检查中…（profilecheck + doctor，doctor 要查网络，最坏约 30 秒）";
            BuildShell();
            string h = await System.Threading.Tasks.Task.Run(delegate { return Run(cli, "profilecheck"); });
            string d = await System.Threading.Tasks.Task.Run(delegate { return Run(cli, "doctor"); });
            _healthBusy = false;
            _health = h;   // ★★★ **F12 修复（GUI 审计 MAJOR —— 体检结果从来没显示过）** ✓✓
            //   ✗ 我上一轮把这一行**误删**了 ✗ → `_health` 在正常路径上**从不赋值** ✗✗
            //     → `Shells.cs` 的 `if (!string.IsNullOrEmpty(host.Health))` **永远为假** ✗
            //     → **"运行检查"按钮既不显示"检查中…"也不显示结果** ✗✗ ← 比修之前更糟 ✓
            //   ✓ 现在：**结果真的写回 `_health`** ✓✓（`Run()` 自己吞异常 ✓ 不会卡住 `_healthBusy` ✓）
            _doctor = SummaryMarkers.ParseDoctor(d);
            BuildShell();
        }

        /// <summary>隔离/恢复一个插件条目：调用工具箱核心的 profilepatch（写操作：它会先备份、再改、失败逐字节回滚）。</summary>
        public void PatchEntry(string profile, string entryId, bool disable)
        {
            string cli = CliPath();
            if (cli == null) { _actionLog = "无法执行隔离：未找到工具箱 CLI。"; BuildShell(); return; }
            string yaml = Path.Combine(Path.Combine(_profilesRoot, profile), "cordis.patch.yml");
            // ★★★ **F2 修复（GUI 审计 MAJOR —— 这个按钮从来没工作过）** ✓✓
            //   ✗ 原来传 `--file <yaml> --disable` / `--set disabled=false` ✗
            //     而 CLI 的 `profilepatch` **只认 `--profile` / `--id` / `--enable` / `--yes`** ✗✗
            //     → 每次都返回 `PROFILEPATCH_FAIL usage: …` ✗ → **隔离/恢复永远无效** ✓
            //   ✓ 现在：**用 CLI 真正接受的旗标** ✓✓
            //     · 禁用 = **不带 `--enable`** ✓（CLI 默认就是禁用 ✓）
            //     · 恢复 = **带 `--enable`** ✓✓
            //   ✓ 顺带：**改走 `RunCliAction`** ✓✓（原来 `Run` 是**同步**的 ✓
            //     → 整个 profilepatch 进程期间**窗口卡死** ✗ → 现在后台跑 ✓ 且写操作会清缓存 ✓✓）
            string args = "profilepatch --profile " + profile + " --id " + entryId + (disable ? "" : " --enable") + " --yes";
            RunCliAction(args, disable ? "隔离插件" : "恢复插件");
            // The old synchronous implementation used to sit here and is gone; it was unreachable and the
            // compiler said so (CS0162). Removed rather than left behind.
        }

        /// <summary>在文件管理器里打开插件目录（Windows 资源管理器 / Linux 文件管理器）。</summary>
        /// <summary>用系统默认程序打开一个 URL（Windows: ShellExecute ✓；Linux: xdg-open ✓）。</summary>
        public void OpenUrl(string url)
        {
            try
            {
                bool win = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                if (win)
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return;
                }
                // Linux：**不加引号** ✓（没有 shell 参与 ✓）· 并**检测退出码** ✗ —— 系统没设默认浏览器时 xdg-open 会失败 ✓
                // （2026-09-30 真机：xdg-settings 返回空 → xdg-open 静默失败 ✗ 而 Process.Start 成功 ✓ → 用户看不到任何反应 ✓）
                // ✗✗ 真机实测（2026-09-30）：GNOME 的 xdg-open(gio) **自己会用 HTTP 客户端请求这个 URL** ✓
                //    → dsh 对它的请求返回 401 ✗ → gio 报 "Unauthorized" 就**放弃** ✗ → 浏览器永远打不开 ✓✓
                //    （URL 本身是对的 ✓ 带 token ✓ —— 问题在 xdg-open 的实现 ✗）
                // → 所以 Linux 上**先直接试浏览器** ✓✓（装了哪个用哪个 ✓）；xdg-open 只给 2 秒做兜底 ✓
                // ✗✗ 真机实测（2026-09-30）第二层原因：这台机器上 **firefox 是 snap 包** ✓
                //    → snap 应用有 **cgroup 限制** ✗：非会话启动器直接跑 `firefox` 会报
                //      "…is not a snap cgroup for tag snap.firefox.firefox" ✓✓
                //    → **`snap run firefox` 能自建正确的 cgroup** ✓✓ 实测只差 DISPLAY ✓（GUI 在会话内有 ✓✓）
                // → 所以 Linux 上顺序：snap run firefox → firefox → chromium 系 → xdg-open ✓
                // ✗✗ 第三层（真机实测 2026-09-30）：snap firefox 还**必须有 `WAYLAND_DISPLAY`** ✓✓
                //    报错原文：Missing Wayland display, WAYLAND_DISPLAY is empty ✓
                //    → 如果 GUI 自己的环境里没有它（例如被非常规方式启动 ✗）→ **替它探测出来** ✓✓
                // 排障开关 ✓：browser_mode 决定**走哪条路**（用户可切换 ✓ 真的接线 ✓）
                bool trySnap = BrowserMode == "auto" || BrowserMode == "snap";
                bool tryDirect = BrowserMode == "auto" || BrowserMode == "direct";
                bool tryXdg = BrowserMode == "auto" || BrowserMode == "xdg";
                string wd = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
                if (string.IsNullOrEmpty(wd))
                {
                    try
                    {
                        string rd = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                        if (string.IsNullOrEmpty(rd)) rd = "/run/user/" + Environment.GetEnvironmentVariable("UID");
                        if (!string.IsNullOrEmpty(rd) && System.IO.Directory.Exists(rd))
                        {
                            string[] socks = System.IO.Directory.GetFiles(rd, "wayland-*");
                            for (int si = 0; si < socks.Length; si++)
                            {
                                string nm = System.IO.Path.GetFileName(socks[si]);
                                if (nm.EndsWith(".lock", StringComparison.Ordinal)) continue;
                                wd = nm; break;
                            }
                        }
                    }
                    catch { }
                }
                if (trySnap)
                {
                    try
                    {
                        ProcessStartInfo sp = new ProcessStartInfo("snap", "run firefox \"" + url + "\"") { UseShellExecute = false };
                        if (!string.IsNullOrEmpty(wd)) sp.Environment["WAYLAND_DISPLAY"] = wd;   // 补上它 ✓✓
                        Process p0 = Process.Start(sp);
                        if (p0 != null) return;   // snap 不在的话会抛异常 ✓ 落到下面的候选 ✓
                    }
                    catch { }
                }
                string[] browsers0 = new string[] { "firefox", "chromium", "chromium-browser", "google-chrome", "epiphany" };
                if (tryDirect)
                {
                    for (int bi0 = 0; bi0 < browsers0.Length; bi0++)
                    {
                        try { Process.Start(new ProcessStartInfo(browsers0[bi0], url) { UseShellExecute = false }); return; } catch { }
                    }
                }
                if (!tryXdg) return;   // browser_mode 指定了别的路 → 不走 xdg ✓
                bool opened = false;
                try
                {
                    ProcessStartInfo xp = new ProcessStartInfo("xdg-open", url) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                    using (Process xp2 = Process.Start(xp))
                    {
                        if (xp2 != null) { xp2.WaitForExit(2000); opened = xp2.HasExited && xp2.ExitCode == 0; }   // 只等 2 秒 ✓（它在 GNOME 上必然失败 ✗ 不值得等 6 秒 ✓）
                    }
                }
                catch { opened = false; }
                if (opened) return;
                // 回退：直接试常见浏览器 ✓（装了哪个用哪个 ✓ 不依赖系统默认关联 ✓）
                string[] browsers = new string[] { "firefox", "chromium", "chromium-browser", "google-chrome", "epiphany" };
                for (int bi = 0; bi < browsers.Length; bi++)
                {
                    try { Process.Start(new ProcessStartInfo(browsers[bi], url) { UseShellExecute = false }); return; } catch { }
                }
                _actionLog = "打不开浏览器：系统没有设置默认浏览器，也没找到常见浏览器。请手动打开这个地址：" + Environment.NewLine + url;
            }
            catch (Exception ex)
            {
                _actionLog = "打开浏览器失败：" + ex.Message + Environment.NewLine + url;
            }
        }
        public void OpenFolder(string path)
        {
            try
            {
                bool win = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                ProcessStartInfo psi = win
                    ? new ProcessStartInfo(path) { UseShellExecute = true }
                    : new ProcessStartInfo("xdg-open", "\"" + path + "\"") { UseShellExecute = false };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _actionLog = "打开目录失败：" + ex.Message + Environment.NewLine + path;
                BuildShell();
            }
        }

        /// <summary>插件安装目录（构造即可，不需要 CLI：<profiles>/<name>/node_modules/<id>）。</summary>
        public string BundleFolder(string profile, string bundleId)
        {
            string root = _profilesRoot;
            if (string.IsNullOrEmpty(root)) return "";
            return Path.Combine(Path.Combine(Path.Combine(root, profile), "node_modules"), bundleId.Replace('/', Path.DirectorySeparatorChar));
        }

        private string _profilesRoot = "";
        public string ProfilesRoot { get { return _profilesRoot; } }

        /// <summary>找工具箱核心程序（有 profilepatch 的那个 v2.x exe）。</summary>
        private static string ToolkitCore()
        {
            string env = Environment.GetEnvironmentVariable("DSHT_CORE");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                return null;   // v2.x 核心是 Windows/.NET Framework 专有：非 Windows 上不是"文件缺失"，而是平台不支持
            string[] names = new string[] { "DeepSeek Harness Toolkit.exe", "dsht.exe" };
            for (int i = 0; i < names.Length; i++)
            {
                string p = Path.Combine(dir, names[i]);
                if (File.Exists(p)) return p;
            }
            return null;
        }
        public void SetProfileSearch(string text) { ProfileSearch = text == null ? "" : text; BuildShell(); }
        public List<SessionRowVm> Rows { get { return _rows; } }
        /// <summary>当前视图要显示的列表（过滤后 ✓；默认 = 全部 ✓）。</summary>
        public List<SessionRowVm> ListSource { get { return _listSource == null ? _rows : _listSource; } }
        private List<SessionRowVm> _listSource;
        public void SetListSource(List<SessionRowVm> src) { _listSource = src; }
        public int SortMode { get; set; }
        public int Filter { get { return _filter; } }
        public int StyleKind { get { return Palette.StyleKind; } }

        /// <summary>切换视觉 demo（A/B/C/D）：换配色与密度后重画外壳。</summary>
        public void SetStyle(int kind)
        {
            Palette.Apply(kind);
            PersistUiPref("gui_style", kind);   // U4：记住风格选择 ✓ 重启保留 ✓（走闸门 ✓ 复核 F2）
            BuildShell();
        }
        public int MainSection { get { return _mainSection; } }
        public int SubTab { get { return _subTab; } }
        public bool IsSessionsSection { get { return _mainSection == 2; } }
        /// <summary>概览与看板都需要 status/profiles/sessions 这批数据。</summary>
        public bool IsOverviewLike { get { return _mainSection <= 1; } }
        /// <summary>看板窗口档（看板第一批 · 2026-10-08 ✓✓）：0=总计（CLI 省略参数 ✓ days=unknown ✓）/ 7 / 14 / 30。
        /// 筛选条 handler：host.BoardDays = n; host.Refresh(); ✓ RunCached 4 秒缓存按命令串分键 ✓ 档位互不踩 ✓。
        /// 设窗口档 ⇒ 清掉自定义区间（D4 ✓ 规格 §11.7-E-4 ✓ 两组互斥 ✓）。✗ 不进 config（会话内状态 ✓ 规格 §4.1 ✓）。</summary>
        private int _boardDays;
        public int BoardDays
        {
            get { return _boardDays; }
            set { _boardDays = (value == 7 || value == 14 || value == 30) ? value : 0; _boardFrom = null; _boardTo = null; }
        }
        // —— 看板第三批（2026-10-09 ✓✓ 规格 §11.7-E ✓✓）——
        /// <summary>口径档：null = 交给 CLI/配置决定（sessions_default_level ✓ §11.7-D ✓）；否则 global / parents / parents_sub。
        /// chips 点击 ⇒ BoardLevel = 档; host.Refresh(); ✓ 高亮以 SESSWIN_META 实发 level 为准（说法与代码一致 ✓✓）。</summary>
        private string _boardLevel;
        public string BoardLevel
        {
            get { return _boardLevel; }
            set
            {
                _boardLevel = (value == "parents" || value == "parents_sub" || value == "global") ? value : null;
            }
        }
        /// <summary>自定义日期区间（D4 ✓）：两段都给了才生效（✗ 不默认补端点 ✗ 不猜 ✓ 与 CLI 同口径 ✓）。</summary>
        private string _boardFrom;
        private string _boardTo;
        public string BoardFrom { get { return _boardFrom; } }
        public string BoardTo { get { return _boardTo; } }
        /// <summary>设自定义区间（本地已校验 ✓）⇒ 清掉窗口档（互斥 ✓）。</summary>
        public void SetBoardCustomRange(string from, string to)
        {
            _boardFrom = from; _boardTo = to; _boardDays = 0;
        }
        /// <summary>看板侧的小提示（自定义区间本地校验失败等 ✓ 复用操作回执位 ✓ GUI 不另编 CLI 文案 ✓ 这只是本地校验原因 ✓）。</summary>
        public void NoteBoard(string text)
        {
            _actionLog = text ?? "";
        }
        /// <summary>sessions/overview 命令串的筛选后缀：
        /// 自定义区间优先生效（--from/--to ✓ D4 ✓）；否则窗口档（>0 → " --days N"；0 → 空 ✓ CLI 省略 = 总计 ✓）；
        /// 口径档非 null → 追加 " --level X"（null = 交给 CLI/配置 ✓）。</summary>
        private string BoardArgsSuffix()
        {
            string suf = _boardDays > 0 ? " --days " + _boardDays.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            if (_boardFrom != null && _boardTo != null) suf = " --from " + _boardFrom + " --to " + _boardTo;
            if (_boardLevel != null) suf += " --level " + _boardLevel;
            return suf;
        }
        public string RawOutput { get { return _rawOutput; } }
        /// <summary>导航表访问一律带范围保护 —— 菜单项数与表长度不一致时不允许越界（审计发现过 UI 线程越界崩溃）。</summary>
        public string[] SubTabs { get { return _mainSection >= 0 && _mainSection < NavSubs.Length && NavSubs[_mainSection] != null ? NavSubs[_mainSection] : new string[0]; } }

        public string PageTitle
        {
            get { return NavItems[_mainSection].Replace("　", " ").Trim(); }
        }

        public string SubtitleText
        {
            get
            {
                string[] d = _mainSection >= 0 && _mainSection < NavDesc.Length && NavDesc[_mainSection] != null ? NavDesc[_mainSection] : new string[0];
                if (d.Length == 0) return "";
            int i = _subTab < d.Length ? _subTab : 0;
                return d[i];
            }
        }

        /// <summary>子菜单在会话页切的是"排序视角"，这里把当前视角说清楚。</summary>
        public string FocusText
        {
            get
            {
                switch (_subTab)
                {
                    case 1: return "当前视角：缓存命中率（低→高）—— 命中率低的会话排在最前，最值得先看。";
                    case 2: return "当前视角：解码速度（快→慢）—— 反映生成 token 的速率。";
                    case 3: return "当前视角：上下文压力（高→低）—— 越靠前越接近触发压缩。";
                    default: return "当前视角：总览（按最后活动排序）—— 想看别的角度，点上面的子菜单。";
                }
            }
        }

        public string SourceText
        {
            get
            {
                if (_data == null) return "数据来源：—";
                return _data.SourceText + "　投影目录：" + _data.Root + "　（GUI 不引用核心程序集，只解析 CLI 标记行）";
            }
        }

        // ---------------- 导航与渲染 ----------------

        public void SetMainSection(int idx)
        {
            if (idx < 0 || idx >= NavItems.Length) return;
            // ★★★ **N8 修复（GUI 复审 MAJOR —— 子集泄漏到别的页面）** ✓✓
            //   ✗ 会话页会把"子代理"这个子集写进 `_listSource` ✗ 而换主页面时**从不清它** ✗✗
            //     → 切到看板/概览时 `KpiStrip` 读到的是**上一个页面的子集** ✓
            //     → 显示"只有子代理"的数字却标着「父会话」✗✗
            //   ✓ 现在：**换主页面就清掉** ✓✓（会话页重建时会自己再设 ✓）
            SetListSource(null);
            _mainSection = idx;
            _subTab = 0;
            SortMode = 0;
            // ★★★ 用户反馈「延迟还在」：点完页面 → 屏幕什么都不变 → 等 CLI 跑完才换页 ✗✗
            //   ✓ 现在：**点击立刻换页**（有旧数据先显示旧数据 ✓ stale-while-revalidate），
            //     同时亮加载浮层 ✓ 数据在后台刷新，到了再画一次 ✓✓ —— 换页从此是 0ms 级 ✓
            _loading = true;
            BuildShell();
            Refresh();
        }

        public void SetSubTab(int idx)
        {
            _subTab = idx;
            if (IsSessionsSection)
            {
                // 子菜单即"排序视角"：总览=最后活动，命中率=低→高，解码=快→慢，压力=高→低
                // 子菜单只决定"看什么"（列表 / 统计）；排序一律交给工具栏的下拉 —— 原先两者都管排序，互相冲突。
                Rerender();
            }
            else
            {
                BuildShell();
            }
        }

        public void SetFilter(int mode)
        {
            _filter = mode;
            Rerender();
        }

        public void Rerender()
        {
            // F6 FIX (GUI audit MAJOR): building the session list used to set the list source as a
            // side effect and nothing ever cleared it. After visiting the sub-agent view the
            // overview skipped every sub-agent row and rendered an EMPTY list, while the cards above
            // still showed the previous view's numbers. Clearing it here makes every rebuild start
            // from the full row set; a view that wants a subset sets it again as it builds.
            SetListSource(null);
            if (_data == null || !_data.Ok) return;
            List<SessionRow> rows = SessionsView.Filter(_data.Rows, _filter);
            rows = SessionsView.Sort(rows, SortMode);
            SessionsView.AttachBars(rows);
            List<SessionRowVm> vms = new List<SessionRowVm>();
            for (int i = 0; i < rows.Count; i++) vms.Add(NewRowVm(rows[i]));
            _rows = vms;
            BuildShell();
        }

        /// <summary>建一行 VM + 查 sub 行填「含子代理」小字（第三批 · 2026-10-09 ✓✓ 规格 §11.7-E-3 ✓✓）：
        /// SubById 无此 id ⇒ SubLine 留空（不显示 ✓）；文案唯一出处 = SessionsMarkers.SubLineText ✓。</summary>
        private SessionRowVm NewRowVm(SessionRow row)
        {
            SessionRowVm vm = new SessionRowVm(row);
            if (row != null && row.Id != null && _data != null)
            {
                SessAggRow sub;
                if (_data.SubById.TryGetValue(row.Id, out sub)) vm.SubLine = SessionsMarkers.SubLineText(sub);
            }
            return vm;
        }

        public void ShowDetail(SessionRowVm vm)
        {
            Shells.Shells.FillDetail(DetailHost, vm);
        }

        private void BindStyle(int id)
        {
            Button b = this.FindControl<Button>("Style" + id);
            if (b == null) return;
            b.Click += delegate(object s, RoutedEventArgs e) { SetStyle(id); };
        }

        private void BindShell(int id)
        {
            Button b = this.FindControl<Button>("Shell" + id);
            if (b == null) return;
            b.Click += delegate(object s, RoutedEventArgs e)
            {
                _shell = id;
                PersistUiPref("gui_shell", id);   // U4：记住布局选择 ✓（走闸门 ✓ 复核 F2）
                BuildShell();
            };
        }

        private void BuildShell()
        {
            ContentControl body = this.FindControl<ContentControl>("Body");
            if (body == null) return;
            _liveFields.Clear();   // ★ 马上要建新树 ✓ 旧字段的引用作废 ✓（页面重建时会重新注册 ✓✓）
            ApplyChrome();
            DetailHost = null;
            // 页面外面包一层 Grid ✓ 把 toast 作为**浮层**加在最后 ✓✓
            // （用户要求：那种提示改成"窗口内右下角弹窗" ✓ 原来是页面流里的一张卡片 ✗）
            Grid wrap = new Grid();
            wrap.Children.Add(Shells.Shells.Build(_shell, this));
            // ★ 加载浮层（不挡交互 ✓）：有旧数据时旧数据照常显示 + 右上角小转标；没有时居中加载卡 ✓
            if (_loading) wrap.Children.Add(LoadingLayer());
            wrap.Children.Add(ToastLayer());
            body.Content = wrap;
            // ★ 内容淡入（用户要求的「动画过渡」✓）：**只在"数据刚到"的那一次**淡入 160ms ✓
            //   点击瞬切的那次（_loading=true）**不淡** ✓ —— 反馈要立刻，过渡要柔和 ✓✓
            //   实现：先把 Opacity 置 0，一拍（40ms）后置 1 → DoubleTransition 自动补间 ✓
            //   整段包 try/catch ✗ 任何一环失败就"直接可见" ✓✓ —— 动画绝不许有把界面变黑的模式 ✗
            bool fade = _fadeNextBuild; _fadeNextBuild = false;   // 美学B1：淡入只在数据到达那一次 ✓
            if (fade && !_loading)
            {
                try
                {
                    wrap.Opacity = 0;
                    wrap.Transitions = new global::Avalonia.Animation.Transitions
                    {
                        new global::Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }
                    };
                    global::Avalonia.Threading.DispatcherTimer fadeTimer = new global::Avalonia.Threading.DispatcherTimer();
                    fadeTimer.Interval = TimeSpan.FromMilliseconds(40);
                    fadeTimer.Tick += delegate { fadeTimer.Stop(); wrap.Opacity = 1; };
                    fadeTimer.Start();
                }
                catch { wrap.Opacity = 1; }   // ★ 保险 ✓✓
            }
            // 有新的操作日志 → 弹一次 ✓（去重：同一条不重复弹 ✓）
            if (!string.IsNullOrEmpty(_actionLog) && _actionLog != _lastToasted)
            {
                _lastToasted = _actionLog;
                ShowToast(_actionLog, _lastActionLevel);
            }
        }

        // —— 右下角弹窗（toast）✓✓ 用户要求："这个绿色框的提示改为在窗口内右下角弹窗提示吧" ——
        private Border _toast;
        private TextBlock _toastText;
        private string _lastToasted = "";
        private global::Avalonia.Threading.DispatcherTimer _toastTimer;

        /// <summary>浮层容器：一个**右下角对齐**的 Border ✓ 初始隐藏 ✓ 不挡操作 ✓（只有它自己那块可点 ✓）。</summary>
        private Control ToastLayer()
        {
            _toastText = new TextBlock { Text = "", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Text };
            StackPanel sp = new StackPanel { Spacing = 6 };
            sp.Children.Add(_toastText);
            sp.Children.Add(new TextBlock { Text = "点一下关闭", FontSize = 10.5, Foreground = Palette.TextFaint });
            _toast = new Border
            {
                Child = sp,
                MaxWidth = 460,
                Background = Palette.CardBg,
                BorderBrush = Palette.Warn,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 11),
                Margin = new Thickness(0, 0, 20, 20),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsVisible = false,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, OffsetY = 4, Color = Color.FromArgb(60, 0, 0, 0) })
            };
            _toast.PointerPressed += delegate { HideToast(); };
            return _toast;
        }

        /// <summary>弹一条 ✓（8 秒后自动消失 ✓ 也可以点掉 ✓）。</summary>
        public void ShowToast(string text) { ShowToast(text, "info"); }

        /// <summary>弹一条 ✓（8 秒自动消失 ✓ 可点掉 ✓）。**边框按级别着色**（U9：成功不再顶橙边 ✓）。</summary>
        public void ShowToast(string text, string level)
        {
            if (_toast == null || _toastText == null || string.IsNullOrEmpty(text)) return;
            _toastText.Text = text;
            _toast.BorderBrush = level == "error" ? Palette.Bad : (level == "warn" ? Palette.Warn : Palette.Border);
            _toast.IsVisible = true;
            if (_toastTimer == null)
            {
                _toastTimer = new global::Avalonia.Threading.DispatcherTimer();
                _toastTimer.Interval = TimeSpan.FromSeconds(8);
                _toastTimer.Tick += delegate { HideToast(); };
            }
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void HideToast()
        {
            if (_toastTimer != null) _toastTimer.Stop();
            if (_toast != null) _toast.IsVisible = false;
        }

        // —— 加载反馈（用户反馈「延迟还在」的根治：**点击必须立刻有反应** ✓）——
        private bool _loading;
        private string _closeAction = "";   // close_action 接线（2026-10-06）：exit=直退 / ask=确认 / tray=托盘
        private bool _realExit;
        private global::Avalonia.Controls.TrayIcon _tray;
        private bool _fadeNextBuild;   // 美学B1：下一次 BuildShell 是否淡入（只有"数据到达型"重建才置真 ✓）
        private bool _fadeSuppressed;  // 手动刷新按钮：不要淡入（连点不闪 ✓）
        /// <summary>一次刷新正在后台跑 ✓。BuildShell 据此决定画不画加载浮层 ✓。</summary>
        public bool IsLoading { get { return _loading; } }
        /// <summary>体检页的**逐行实时列表** ✓（`doctor --stream` 来一条记一条 ✓）。
        ///   跨刷新保留 ✓ → 重进体检页时**旧列表先显示**，新结果再逐行覆盖 ✓✓（杀软式 ✓ 用户建议 ✓）。</summary>
        private List<string> _doctorLive = new List<string>();
        public List<string> DoctorLive { get { return _doctorLive; } }
        /// <summary>当前页面有没有旧数据可显示 ✓ —— 决定浮层做"右上角小转标"还是"居中加载卡" ✓。</summary>
        public bool HasRenderableData
        {
            get { return _rawOutput.Length > 0 || _doctor != null || _data != null || _backups != null || _profiles != null || _doctorLive.Count > 0; }
        }

        // —— 概览自动刷新（2026-10-02 用户要求："快1秒 中3秒 慢5秒 实时0.5秒 暂停和自定义" ✓✓）——
        private global::Avalonia.Threading.DispatcherTimer _autoTimer;
        private double _autoRefreshSeconds;   // 0 = 暂停 ✓
        /// <summary>当前自动刷新间隔（秒；0 = 暂停 ✓）。只在概览/看板页生效 ✓。</summary>
        public double AutoRefreshSeconds { get { return _autoRefreshSeconds; } }
        /// <summary>应用间隔 ✓（**不落盘** ✓ 落盘交给 SetAutoRefresh ✓）：0 = 停表 ✓。
        /// 越界/不认 → 暂停 ✓（Validate 已拦 ✓ 这里是双保险 ✓）。</summary>
        private void ApplyAutoRefresh(string val)
        {
            double sec;
            string t = val == null ? "" : val.Trim();
            if (t.Length == 0 || t == "off") sec = 0;
            else if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sec) || sec < 0.5 || sec > 3600) sec = 0;
            _autoRefreshSeconds = sec;
            if (_autoTimer != null) { _autoTimer.Stop(); _autoTimer = null; }
            if (sec > 0)
            {
                _autoTimer = new global::Avalonia.Threading.DispatcherTimer();
                _autoTimer.Interval = TimeSpan.FromSeconds(sec);
                _autoTimer.Tick += delegate
                {
                    // 只在概览/看板页刷 ✓ 且**不打断**正在跑的刷新 ✓
                    // （跳过本拍 ✗ 不排队 ✓ —— 0.5 秒间隔时若排队会连环补拍 ✗ 变成永不停 ✓✓）
                    // ★ 用**字段级刷新** ✗ 不整页重建 ✓✓（用户要求："可以只字段刷新吗" ✓
                    //   整页重建会丢滚动/焦点/下拉展开 + 触发整页淡入 ✗ 观感就是"整页闪一下" ✗）
                    if (IsOverviewLike && !_busy) RefreshFieldsOnly();
                };
                _autoTimer.Start();
            }
        }
        /// <summary>设置自动刷新（UI 入口 ✓）：立即生效 + 落盘（config-set gui_auto_refresh ✓）。</summary>
        public void SetAutoRefresh(string val)
        {
            ApplyAutoRefresh(val);
            SetConfig("gui_auto_refresh", val == null || val.Trim().Length == 0 ? "off" : val.Trim());
        }

        // —— 字段级刷新（2026-10-04 用户要求："自动刷新回整页刷新，可以只字段刷新吗" ✓✓）——
        //   ✗ 原来每一拍都 `BuildShell()` **重建整棵视觉树** ✗ →
        //     滚动位置回到顶部、焦点丢失、展开的下拉收起、**整页还淡入 160ms** ✗✗ = 观感"整页闪一下" ✓
        //   ✓ 现在：页面把"会变的字段"注册进来 ✓ 自动刷新那一拍**只重算并改 Text** ✗ 不重建 ✓✓
        private sealed class LiveField { public Action Apply; }
        private readonly List<LiveField> _liveFields = new List<LiveField>();
        private bool _silentRefresh;
        /// <summary>注册一个"会变的字段"：apply 里重算并写回界面 ✓（BuildShell 会清空注册表 ✓ 因为那是新树 ✓）。</summary>
        public void Live(Action apply) { if (apply != null) _liveFields.Add(new LiveField { Apply = apply }); }
        /// <summary>最常见的字段：一段文字 ✓ —— value 是**取值函数** ✗ 不是建树那一刻的快照 ✗
        /// （传快照的话字段永远显示旧值 ✗✗ 这正是"看起来没刷新"的经典坑 ✓）。</summary>
        public global::Avalonia.Controls.TextBlock LiveText(string initial, Func<string> value)
        {
            global::Avalonia.Controls.TextBlock tb = new global::Avalonia.Controls.TextBlock { Text = initial };
            Live(delegate { string v = value(); if (v != null && tb.Text != v) tb.Text = v; });
            return tb;
        }
        /// <summary>只更新已注册字段 ✓（不碰视觉树 → 不丢滚动/焦点/下拉 ✓ 也不触发整页淡入 ✓✓）。</summary>
        private void RefreshLiveFields()
        {
            for (int i = 0; i < _liveFields.Count; i++)
            {
                // 单个字段失败**不该拖垮整拍** ✓（那一个不动 ✓ 也不整页报错 ✗ 与全项目"部分失败如实说"一致 ✓）
                try { _liveFields[i].Apply(); } catch { }
            }
        }
        /// <summary>静默刷新 ✓：照常后台取数据 ✗ 但不重建视觉树 ✓ → 只更新已注册字段 ✓。
        /// 若当前页**一个字段都没注册** → **退化为整页重建** ✓（免得"刷新了却什么都没动" ✗✗ 更难查 ✓）。</summary>
        public void RefreshFieldsOnly()
        {
            // ★★ 修复（2026-10-05 用户实测："概览刷新设置后停下来了，字段刷新不生效"）：
            //   概览/看板的四个数据源全走 RunCached（4 秒 TTL）→ 3 秒档**每一拍都命中缓存** →
            //   取回的还是上一拍的旧数据 → 字段写回去还是同一个值 → 看起来"停了"。
            //   ✓ 自动刷新这一拍**先清缓存再跑** → 每拍都是新数据。
            //   （普通刷新/切页继续吃缓存；额外成本只在用户自己开了自动刷新时发生 ✓ 与旁注"每拍都起 CLI 进程"的说法终于一致）
            InvalidateCliCache();
            _silentRefresh = true;
            RefreshCore();
        }

        // —— DeepSeek 余额检测（2026-10-02 用户要求 ✓✓）——
        private Dsht.Gui.Avalonia.Markers.BalanceSummary _balance;
        private System.DateTime _balanceAt = System.DateTime.MinValue;
        /// <summary>余额（`balance` 命令 ✓ 只在绑了 key 时有内容 ✓ 未绑定 → 概览页**整卡隐藏** ✓）。
        /// 取值节奏：60 秒最多一次 ✓ ✗ 不跟 0.5 秒的自动刷新一起打接口 ✗✓（那是打别人的 API ✗✗）。</summary>
        public Dsht.Gui.Avalonia.Markers.BalanceSummary Balance { get { return _balance; } }
        /// <summary>加载浮层的文案要**诚实** ✗ 不写"请稍候"这种空话 ✓ —— 哪页要等、为什么等，如实写 ✓。
        ///   （实测 2026-10-02：体检 `doctor` 6.5s —— 其中一项要探测 npm registry；更新 `update-center` 7.9s —— 要查 GitHub ✓）</summary>
        public string LoadingHint
        {
            get
            {
                switch (_mainSection)
                {
                    case 5: return "正在体检：其中一项要探测 npm registry，离线也得等它超时（约几秒）";
                    case 8: return "正在查询 GitHub / npm registry（耗时取决于网络，约几秒）";
                    case 0:
                    case 1: return "正在读取 dsh 状态…";
                    case 7: return "正在读取安装信息…";
                    default: return "正在读取…";
                }
            }
        }

        /// <summary>加载浮层 ✓✓ 全程 `IsHitTestVisible=false` ✓ 不挡点击 ✓。
        ///   ① 有旧数据 → 右上角小转标（旧数据照常显示 ✓ 不断档 ✓ stale-while-revalidate）
        ///   ② 无旧数据 → 居中卡片（转圈 + **诚实**的等待原因 ✓）</summary>
        private Control LoadingLayer()
        {
            if (HasRenderableData)
            {
                StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
                sp.Children.Add(Spinner(13, 2));
                sp.Children.Add(new TextBlock { Text = "正在刷新…", FontSize = 11, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center });
                return new Border
                {
                    Child = sp,
                    Background = Palette.CardBg,
                    BorderBrush = Palette.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(10, 5),
                    Margin = new Thickness(0, 14, 18, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible = false,
                    BoxShadow = new BoxShadows(new BoxShadow { Blur = 14, OffsetY = 3, Color = Color.FromArgb(50, 0, 0, 0) })
                };
            }
            // 首次进入该页（还没有任何数据）：居中卡片，别让用户对着空白页猜
            StackPanel card = new StackPanel { Spacing = 10 };
            Control ringBig = Spinner(28, 3);
            ringBig.HorizontalAlignment = HorizontalAlignment.Center;
            card.Children.Add(ringBig);
            card.Children.Add(new TextBlock { Text = "首次进入本页，正在读取", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, HorizontalAlignment = HorizontalAlignment.Center });
            card.Children.Add(new TextBlock { Text = LoadingHint, FontSize = 11.5, Foreground = Palette.TextDim, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center });
            return new Border
            {
                Child = card,
                Background = Palette.CardBg,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(26, 22),
                MaxWidth = 420,
                Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 24, OffsetY = 6, Color = Color.FromArgb(60, 0, 0, 0) })
            };
        }

        /// <summary>转圈指示 ✓。用 DispatcherTimer 驱动（不赌动画 API ✓）；元素**脱离视觉树就停表** ✓✓
        ///   —— BuildShell 每次整体重建 → 旧浮层必然脱离 → 表必然停 ✓ 不泄漏 ✓。
        ///   起表失败就退化为静止圆环 ✓：宁可没有动画，不许它影响功能 ✓。</summary>
        private static global::Avalonia.Controls.Shapes.Ellipse Spinner(double size, double stroke)
        {
            global::Avalonia.Controls.Shapes.Ellipse ring = new global::Avalonia.Controls.Shapes.Ellipse
            {
                Width = size,
                Height = size,
                Stroke = Palette.Accent,
                StrokeThickness = stroke,
                StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double>(new double[] { System.Math.PI * size * 0.3, System.Math.PI * size * 0.7 })
            };
            RotateTransform rt = new RotateTransform(0);
            ring.RenderTransform = rt;
            ring.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            try
            {
                global::Avalonia.Threading.DispatcherTimer tm = new global::Avalonia.Threading.DispatcherTimer();
                tm.Interval = TimeSpan.FromMilliseconds(40);
                tm.Tick += delegate { rt.Angle = (rt.Angle + 30) % 360; };
                tm.Start();
                ring.DetachedFromVisualTree += delegate { tm.Stop(); };
            }
            catch { }
            return ring;
        }

        private bool _busy;
        private bool _refreshQueued;
        /// <summary>刷新 ✓。**用户反馈（2026-09-30）**：「按钮交互还是有延迟，并且不低」✓
        ///   ✗ 原来 `if (_busy) return;` ✗ → **刷新期间的点击被直接丢掉** ✗
        ///     → 表现："点了没反应，过一会儿界面才变" ✓✓ **这就是延迟感的来源** ✓
        ///   ✓ 现在：**排队再刷一次** ✓ 不丢点击 ✓✓
        ///   注：真正耗时的是 CLI 子进程（`config-get` / `status` 各启动一个 66 MB 的 exe ✓）✓
        ///       已并行化 ✓ 见 RefreshGuardedAsync ✓</summary>
        public void Refresh()
        {
            _silentRefresh = false;   // ★ 普通刷新 = 要重建整页 ✓（静默刷新走 RefreshFieldsOnly ✓）
            _fadeSuppressed = true;   // 美学B1：用户主动点的刷新 → 不淡入 ✓
            RefreshCore();
        }

        private void RefreshCore()
        {
            if (_busy) { _refreshQueued = true; return; }   // ✓ 不丢 ✓ 排队再刷 ✓
            _busy = true;
            // ★ 静默刷新**不亮加载浮层** ✗（每一拍闪一下那个标记，本身就是"整页在刷"的观感 ✓✓）
            _loading = !_silentRefresh;
            _ = RefreshGuardedAsync();
        }

        /// <summary>GUI 启动时按 `auto_start` **自动起一次** dsh ✓（用户要求："GUI/CLI 启动时自动起" ✓✓）
        /// 约束（重要 ✓）：① **每次 GUI 会话只试一次** ✗（不能每次刷新都起 ✓）
        ///               ② **只在服务没在跑时** ✓（STATUS_DOWN 才起 ✓ 不重复启动 ✓）
        ///               ③ `auto_start=off` 时**什么都不做** ✓✓</summary>
        private bool _autoStartTried = false;
        /// <summary>解析排障开关（`CONFIG browser_mode …` / `CONFIG ui_parallel …` ✓）。
        /// 解析失败就保持默认 ✓ 不猜 ✓。</summary>
        private void ParseTroubleshootSwitches(string cfg)
        {
            if (string.IsNullOrEmpty(cfg)) return;
            string[] ls = cfg.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < ls.Length; i++)
            {
                string t2 = ls[i] == null ? "" : ls[i].Trim();
                if (t2.StartsWith("CONFIG browser_mode ", StringComparison.Ordinal)) BrowserMode = t2.Substring("CONFIG browser_mode ".Length).Trim();
                else if (t2.StartsWith("CONFIG ui_parallel ", StringComparison.Ordinal)) UiParallel = t2.Substring("CONFIG ui_parallel ".Length).Trim() != "off";
            }
        }

        private async System.Threading.Tasks.Task AutoStartOnceAsync(string cli)
        {
            if (_autoStartTried) return;
            _autoStartTried = true;
            try
            {
                string cfg = await System.Threading.Tasks.Task.Run(delegate { return Run(cli, "config-get"); });
                bool wantAuto = cfg != null && cfg.IndexOf("auto_start on", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!wantAuto) return;
                string st = await System.Threading.Tasks.Task.Run(delegate { return Run(cli, "status"); });
                bool down = st != null && st.IndexOf("STATUS_DOWN", StringComparison.Ordinal) >= 0;
                // ✗✗ 关键修正：**只有桌面端在跑时，status 也报 STATUS_DOWN**（它只探 3080 ✓）
                // → 于是 auto-start 会**再起一个 webui** ✗ → 两个同时跑 ✓（用户实测反馈 ✓）
                // → 检测到桌面端就**不再起 web** ✓✓（用户在用桌面端 ✓ 不需要 web ✓）
                bool desktopUp = st != null && st.IndexOf("STATUS_DESKTOP", StringComparison.Ordinal) >= 0;
                if (desktopUp)
                {
                    _actionLog = "检测到官方桌面端正在运行 → **不启动 webui**（避免两个同时跑；要用 web 请先在侧栏点「停止」或关掉桌面端）";
                    BuildShell();
                    return;
                }
                if (!down) return;   // 已经在跑 → 不动它 ✓
                _actionLog = "auto_start=on → 正在自动启动 dsh…";
                BuildShell();
                string outp = await System.Threading.Tasks.Task.Run(delegate { return Run(cli, "start --yes"); });
                _actionLog = "auto_start 自动启动结果：" + Environment.NewLine + (outp == null ? "" : outp.Trim());
                Refresh();
            }
            catch { }
        }   // 异步：CLI 调用不占 UI 线程

        /// <summary>保证 _busy 一定复位：刷新中途抛异常也不许把界面锁死成一次性。</summary>
        private async System.Threading.Tasks.Task RefreshGuardedAsync()
        {
            try { await RefreshAsync(); }
            // ✓ 用户反馈（2026-09-30）：刷新期间的点击原来被**直接丢掉** ✗ → 表现"点了没反应" ✓
            //   现在：收尾时若**有排队的刷新** → 立刻再刷一次 ✓✓
            finally
            {
                // ★ 统一在这里画**最后一次** ✓：数据到手 + 加载浮层熄灭，一笔到位 ✓
                //   （旧代码在 RefreshAsync 的 5 条路径里各画一次 → 分散且必然在数据前多画一次空页 ✗）
                _busy = false;
                // ★ 后面**还有排队的刷新** → 指示灯继续亮着 ✓（别让中间那笔画完就灭 ✗
                //   否则"体检→更新"连点时，更新页会有一段时间**没有指示却还在等数据** ✗✗）
                _loading = _refreshQueued;
                // ★★ 字段级刷新（2026-10-04 ✓）：静默那一拍**不重建** ✓ 只把注册过的字段重算一遍 ✓✓
                //   · 有排队 → 还是得重建（排队的是一次完整刷新 ✓ 队列语义优先 ✓）
                //   · 本页**没注册任何字段** → 退化为整页重建 ✓（否则"刷新了却没动" ✗✗）
                bool silent = _silentRefresh;
                _silentRefresh = false;
                if (silent && !_refreshQueued && _liveFields.Count > 0) RefreshLiveFields();
                else
                {
                    // 美学B1：只有"数据到达型"重建才淡入 ✓（手动刷新被 _fadeSuppressed 压制 ✓）
                    _fadeNextBuild = !_fadeSuppressed;
                    BuildShell();
                }
                _fadeSuppressed = false;
                if (_refreshQueued) { _refreshQueued = false; Refresh(); }   // ✓ 不丢 ✓ 排队再刷 ✓
            }
        }

        /// <summary>手动运行体检（2026-10-02 用户要求："体检不自动运行，用按钮触发" ✓✓）。
        /// 与原来进页自动跑的是**同一条流式路径** ✓（杀软式逐行出结果 ✓）；正在忙时如实说 ✗ 不排队 ✗（体检 6.5 秒，排队会等很久 ✗✗）。</summary>
        public void RunDoctorNow()
        {
            if (_busy) { ShowToast("正在刷新中 ✓ 稍等一下再运行体检"); return; }
            string cli = CliPath();
            if (cli == null) { ShowToast("未找到工具箱 CLI ✗ 把 dsh-minato.exe 放到 gui\\ 旁边试试"); return; }
            _busy = true;
            _loading = true;
            BuildShell();   // 先亮指示 ✓（按钮反馈立刻可见 ✓）
            _ = System.Threading.Tasks.Task.Run(delegate
            {
                _doctorLive = new List<string>();   // 换新列表 ✓（引用替换 ✓ 正在显示的旧树不受影响 ✓）
                _rawOutput = "";
                string full = RunStreaming(cli, "doctor --stream", delegate(string ln) { StreamLine(ln, true); });
                _rawOutput = full;
                _doctor = SummaryMarkers.ParseDoctor(full);   // 汇总行在末尾 ✓ 解析器按前缀认 ✓
                global::Avalonia.Threading.Dispatcher.UIThread.Post(delegate
                {
                    // 审查 H3 修复：体检期间排队的刷新不能丢 ✗（与 RefreshGuardedAsync.finally 同一套 ✓）
                    _busy = false;
                    _loading = _refreshQueued;
                    _fadeNextBuild = true;   // 体检流式完成 = 数据到达 ✓
                    BuildShell();
                    if (_refreshQueued) { _refreshQueued = false; Refresh(); }
                });
            });
        }

        // ★ RefreshAsync **只取数据，不画界面** ✓（画界面统一在 RefreshGuardedAsync 的 finally ✓ 一笔到位 ✓）
        //   —— 旧版在 5 条路径里各画一次：既分散，也让"取数据中"永远得不到一次即时换页 ✗
        private async System.Threading.Tasks.Task RefreshAsync()
        {
            string cli = CliPath();
            if (cli != null && !_autoStartTried) _ = AutoStartOnceAsync(cli);   // 启动时自动起一次 ✓（内部有"只一次 + 只在没跑时"约束 ✓）
            if (cli == null)
            {
                _data = null;
                _rawOutput = "未找到工具箱 CLI。请把 dsh-minato.exe（或 dsht.exe / dsht_v3.exe）放到本程序同目录，或设置环境变量 DSHT_CLI 指向它。";
                return;
            }

            if (IsOverviewLike)
            {
                _rawOutput = await System.Threading.Tasks.Task.Run(delegate { return RunCached(cli, "status --detail"); });
                // ★ 性能优化（2026-10-06 #3）：overview 一次调用给出概览页全部四组标记行 ✓✓
                //   0.5 秒实时档的成本大头是**进程启动**（4 个 66MB exe → 1 个）✓
                //   ui_parallel=off（排障开关）保留老的四命令路径 ✓ 开关仍然真的接线 ✓
                if (UiParallel)
                {
                    _rawOutput = await System.Threading.Tasks.Task.Run(delegate { return RunCached(cli, "overview" + BoardArgsSuffix()); });
                }
                else
                {
                    // 老行为（串行四命令）：聚合大输出在个别环境被管道/杀软卡住时的排障退路 ✓
                    _rawOutput = await System.Threading.Tasks.Task.Run(delegate { return RunCached(cli, "status --detail"); });
                    string part = await System.Threading.Tasks.Task.Run(delegate { return RunCached(cli, "sessions" + BoardArgsSuffix()); });
                    _rawOutput += "\n" + part;
                    part = await System.Threading.Tasks.Task.Run(delegate { return RunCached(cli, "backup-list"); });
                    _rawOutput += "\n" + part;
                    part = await System.Threading.Tasks.Task.Run(delegate { return RunCached(cli, "profiles"); });
                    _rawOutput += "\n" + part;
                }
                _status = StatusMarkers.Parse(_rawOutput);
                _profiles = ProfilesMarkers.Parse(_rawOutput);
                _data = SessionsMarkers.Parse(_rawOutput);
                _backups = SummaryMarkers.ParseBackups(_rawOutput);
                string cfgText2 = await System.Threading.Tasks.Task.Run(delegate { return CfgCached(cli); });   // ✓ 缓存 ✓ 省一次进程启动 ✓✓
                ParseTroubleshootSwitches(cfgText2);
                if (_doctor == null) _doctor = new DoctorSummary();

                // ★ DeepSeek 余额检测（2026-10-02 用户要求 ✓✓）：与状态刷新**分开** ✗
                //   60 秒最多取一次 ✓ ✗ 不跟 0.5 秒自动刷新一起打接口 ✗✓（未绑 key 时 CLI 秒回 unbound ✓ 不联网 ✓）
                if ((System.DateTime.UtcNow - _balanceAt).TotalSeconds > 60)
                {
                    _balanceAt = System.DateTime.UtcNow;   // 先占位 ✓ 防止并行两拍重复打 ✓
                    _ = System.Threading.Tasks.Task.Run(delegate
                    {
                        string b = Run(cli, "balance");
                        global::Avalonia.Threading.Dispatcher.UIThread.Post(delegate
                        {
                            _balance = Dsht.Gui.Avalonia.Markers.BalanceMarkers.Parse(b);
                            // ★ 修复（审查点名）：余额到账**只更新字段** 不整页重建 ——
                            //   原来开着自动刷新时每 60 秒闪一下 + 滚动回顶，恰好破坏字段级刷新要保护的体验。
                            //   本页没注册字段（如刚切走）→ 退化为整页重建（数据不丢）。
                            if (_liveFields.Count > 0) RefreshLiveFields();
                            else BuildShell();
                        });
                    });
                }
                return;
            }
            if (_mainSection == 3)
            {
                _rawOutput = await System.Threading.Tasks.Task.Run(delegate { return RunCached(cli, "profiles"); });
                _profiles = ProfilesMarkers.Parse(_rawOutput);
                for (int i = 0; i < _rawOutput.Length && _profilesRoot.Length == 0; i++) { }
                _profilesRoot = ProfilesRootFrom(cli);
                return;
            }
            if (!IsSessionsSection)
            {
                // ★ 体检 / 更新两页走**流式**（`--stream` ✓）：列表先出来、逐行出结果 —— 杀软式（用户建议 2026-10-02 ✓）。
                //   CLI 默认输出逐字不变（已用 HEAD 基线字节级比对 ✓）→ 契约/测试全不动 ✓；
                //   顺带体检页还多了"全部条目"列表（原来只列 错误/提醒 ✓）。
                if (_mainSection == 5)
                {
                    // ★★ 体检**不自动运行**（2026-10-02 用户要求："体检不自动运行，用按钮触发" ✓✓）
                    //   进页只显示**上次**的结果 ✓（有旧数据先显示旧数据 ✓ 没有就显示引导卡 ✓）
                    //   —— doctor 最长 6.5 秒（含 npm registry 探测 ✓）每次进页白跑一遍 ✗ 纯浪费 ✗
                    //   手动跑：体检页的「运行体检」按钮 → RunDoctorNow()（同一条流式路径 ✓ 逐行出结果 ✓）
                    return;
                }
                if (_mainSection == 8)
                {
                    _rawOutput = "";   // 更新页渲染器每次**重解析** RawOutput ✓（部分文本也认 ✓）→ 逐行追加即可 ✓
                    string full = await System.Threading.Tasks.Task.Run(delegate
                    {
                        return RunStreaming(cli, "update-center --stream", delegate(string ln) { StreamLine(ln, false); });
                    });
                    _rawOutput = full;
                    return;
                }
                // 日志页要带**当前筛选** ✓ 用动态命令 ✓（其余页用 NavCli 的固定命令 ✓）
                // ★★★ **重大修正（2026-10-01）** ✗✗ —— 这是我上一轮引入的 bug ✓
                //   ✗ 原来写的是：`sectionCmd = await Task.Run(() => Run(cli, string.Join(" ", NavCli[…])));` ✗✗
                //     → **它先把命令跑了一遍拿到「输出」** ✗ → `sectionCmd` 成了**多行输出文本** ✗
                //     → 下一行再 `RunCached(cli, sectionCmd)` → **把多行文本当命令行** ✗ → **必然失败** ✗✗
                //     → `_rawOutput` 变成错误信息 ✓ → **凡是解析它的页面都拿不到数据** ✗
                //   ✓ **症状**（用户报的"备份还是 0 份"✓✓）：
                //     · 备份页 `BackupItems.Parse(_rawOutput)` → **找不到 `BACKUP_ITEM` 行 → 0 份** ✗✗
                //     · 其它页（体检/设置/说明/更新/日志）显示的是**原始输出** ✓ → 错误文本看起来也"有内容" ✗
                //       → **所以只有备份页暴露了这个 bug** ✓✓
                //   ✓ 现在：`sectionCmd` **只拼命令字符串** ✓ **不运行它** ✓✓
                string sectionCmd = _mainSection == 9
                    ? LogArgs()
                    : string.Join(" ", (_mainSection >= 0 && _mainSection < NavCli.Length && NavCli[_mainSection] != null ? NavCli[_mainSection] : new string[0]));
                _rawOutput = await System.Threading.Tasks.Task.Run(delegate { return RunCached(cli, sectionCmd); });
                if (_mainSection == 5) _doctor = SummaryMarkers.ParseDoctor(_rawOutput);   // 体检页吃解析结果，不是只吃原文
                return;
            }

            string text = await System.Threading.Tasks.Task.Run(delegate { return RunCached(cli, "sessions" + BoardArgsSuffix()); });   // ★ 窗口档透传（看板第一批 ✓ 列表页自身仍显示全部 SESSION 行 ✓ 不受影响 ✓）
            _data = SessionsMarkers.Parse(text);
            _rawOutput = text;
            if (!_data.Ok)
            {
                _rows = new List<SessionRowVm>();
            }
            else
            {
                List<SessionRow> rows = SessionsView.Sort(SessionsView.Filter(_data.Rows, _filter), SortMode);
                SessionsView.AttachBars(rows);
                List<SessionRowVm> vms = new List<SessionRowVm>();
                for (int i = 0; i < rows.Count; i++) vms.Add(NewRowVm(rows[i]));   // 第三批 ✓ sub 行查表填「含子代理」✓
                _rows = vms;
            }
        }

        /// <summary>从 CLI 的 SESSIONS_ROOT 风格路径推出 profiles 根（profiles 命令不直接给，这里用数据根 + profiles）。</summary>
        private static string ProfilesRootFrom(string cli)
        {
            try
            {
                string outp = RunCached(cli, "sessions");
                string[] lines = outp.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].StartsWith("SESSIONS_ROOT ", StringComparison.Ordinal)) continue;
                    string p = lines[i].Substring("SESSIONS_ROOT ".Length).Trim();
                    DirectoryInfo d = Directory.GetParent(p);
                    if (d != null) return Path.Combine(d.FullName, "profiles");
                }
            }
            catch { }
            return "";
        }

        private static string CliPath()
        {
            string env = Environment.GetEnvironmentVariable("DSHT_CLI");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            bool win = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            string[] names = win
                ? new string[] { "dsh-minato.exe", "dsht.exe", "dsht_v3.exe", "DeepSeek Harness Toolkit.exe" }
                : new string[] { "dsh-minato", "dsht", "dsht_v3", "DeepSeek Harness Toolkit" };   // Unix 的 apphost 没有扩展名
            for (int i = 0; i < names.Length; i++)
            {
                string p = Path.Combine(dir, names[i]);
                if (File.Exists(p)) return p;
            }
            // 只在**同级**找是不够的 ✗：发布包里 GUI 在 <包>\gui\，而 CLI 在 <包>\ 与 <包>\cli-small\
            // → 真机反馈"未找到 CLI" ✓ 就是这个原因。改为向上、向已知子目录、以及 PATH 都找 ✓✓。
            string[] extraDirs = new string[]
            {
                Path.Combine(dir, ".."),                    // 包根（GUI 在 gui\ 时 ✓）
                Path.Combine(dir, "..", ".."),              // 再上一层（GUI 嵌得更深时 ✓）
                Path.Combine(dir, "cli-small"),             // 包内的小体积版 ✓
                Path.Combine(dir, "..", "cli-small"),
                Path.Combine(dir, "..", "..", "cli-small")
            };
            for (int d = 0; d < extraDirs.Length; d++)
            {
                string ed;
                try { ed = Path.GetFullPath(extraDirs[d]); } catch { continue; }
                for (int i = 0; i < names.Length; i++)
                {
                    try { string p = Path.Combine(ed, names[i]); if (File.Exists(p)) return p; } catch { }
                }
            }
            // 兜底：PATH 里找（用户可能已经把 CLI 装到 PATH ✓）
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                string[] parts = pathEnv.Split(Path.PathSeparator);
                for (int d = 0; d < parts.Length; d++)
                {
                    if (string.IsNullOrEmpty(parts[d])) continue;
                    for (int i = 0; i < names.Length; i++)
                    {
                        try { string p = Path.Combine(parts[d], names[i]); if (File.Exists(p)) return p; } catch { }
                    }
                }
            }
            return null;
        }

        // ★★★ **用户反馈（2026-09-30）**：「按钮交互还是有延迟，并且不低」✓✓
        //   根因：**每次刷新都要启动 CLI 子进程** ✗ 而 CLI 是 **66 MB 自包含 exe** ✓
        //     → 一次 `config-get` 就是几百毫秒的进程启动 ✓
        //   而 `config-get` 的结果**极少变化** ✗ → 每次刷新都重跑纯属浪费 ✓
        //   ✓ 现在：**缓存 60 秒** ✓✓ + **任何 config-set 之后立刻失效** ✓✓
        //     （设置页改了配置 → `SetConfig` 清缓存 → 下一次刷新读到新值 ✓ 不会看不到变化 ✗）
        private static string _cfgCache;
        private static System.DateTime _cfgCacheAt = System.DateTime.MinValue;
        private static string CfgCached(string cli)
        {
            if (_cfgCache != null && (System.DateTime.UtcNow - _cfgCacheAt).TotalSeconds < 5.0) return _cfgCache;   // ✓ 60 秒太长 → 用户报"设置读不到配置项" ✗ 改 5 秒 ✓✓
            _cfgCache = Run(cli, "config-get");
            _cfgCacheAt = System.DateTime.UtcNow;
            return _cfgCache;
        }
        /// <summary>配置写入后**立刻让缓存失效** ✓✓（否则设置页改了看不到变化 ✗）。</summary>
        private static void InvalidateCfgCache() { _cfgCache = null; _cfgCacheAt = System.DateTime.MinValue; }

        // —— close_action 接线（2026-10-06 用户要求"无效选项接上"）：ask=确认 / tray=系统托盘 / exit=直退 ——
        private async void ConfirmExitAsync()
        {
            bool yes = await ConfirmDialog.Ask(this, "退出 dsh-minato", "确定要退出吗？");
            if (yes) { _realExit = true; Close(); }
        }

        /// <summary>tray 模式：隐藏主窗口 + 建系统托盘图标（一次性）。托盘不可用（个别 Linux）→ 退化为直退 ✓ 不猜 ✗。</summary>
        private void HideToTray()
        {
            try
            {
                if (_tray == null)
                {
                    _tray = new global::Avalonia.Controls.TrayIcon();
                    _tray.Icon = Icon;
                    _tray.ToolTipText = "dsh-minato";
                    global::Avalonia.Controls.NativeMenu menu = new global::Avalonia.Controls.NativeMenu();
                    global::Avalonia.Controls.NativeMenuItem show = new global::Avalonia.Controls.NativeMenuItem("显示 dsh-minato");
                    show.Click += delegate { Show(); Activate(); };
                    global::Avalonia.Controls.NativeMenuItem quit = new global::Avalonia.Controls.NativeMenuItem("退出");
                    quit.Click += delegate { _realExit = true; try { _tray.Dispose(); } catch { } Close(); };
                    menu.Items.Add(show); menu.Items.Add(quit);
                    _tray.Menu = menu;
                    _tray.IsVisible = true;
                }
                Hide();
                ShowToast("已最小化到系统托盘 ✓ 托盘图标右键可显示或退出 ✓");
            }
            catch { _realExit = true; Close(); }
        }

        // —— GUI 偏好落盘（U4）· **走动作串行闸门**（复核 F2：fire-and-forget 与其它 config-set
        //   并发写同一配置文件有损坏风险 ✗ 3.0.3 起的"一次一个 CLI 动作"纪律不能被偏好写入绕过 ✓）——
        private string _pendingPrefKey = null;
        private int _pendingPrefVal;

        /// <summary>把 GUI 偏好（布局/风格）落盘 ✓。静默变体：不占 _actionLog、不弹 toast ——
        /// 闸门被占时**只记最新值**、释放后补写（快速连点不丢最终状态 ✓）。</summary>
        private void PersistUiPref(string key, int val)
        {
            if (_actionBusy) { _pendingPrefKey = key; _pendingPrefVal = val; return; }
            RunCliActionQuiet(key, val);
        }

        private void RunCliActionQuiet(string key, int val)
        {
            string cli = CliPath();
            if (cli == null) { FlushPendingPref(); return; }
            _actionBusy = true; _actionBusyLabel = "保存界面偏好"; _actionStartedAt = System.DateTime.UtcNow;
            _ = System.Threading.Tasks.Task.Run(delegate
            {
                try
                {
                    Run(cli, "config-set " + key + " " + val.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    InvalidateCfgCache();   // 写完后让下一次 CfgCached 读到新值 ✓
                }
                finally
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(delegate
                    {
                        _actionBusy = false; _actionBusyLabel = "";
                        FlushPendingPref();
                    });
                }
            });
        }

        private void FlushPendingPref()
        {
            if (_pendingPrefKey == null) return;
            string k = _pendingPrefKey; int v = _pendingPrefVal;
            _pendingPrefKey = null;
            PersistUiPref(k, v);
        }
        // ================================================================ CLI 结果短缓存（性能）

        /// <summary>只读 CLI 命令的**短缓存** ✓✓
        ///
        /// **用户反馈（2026-09-30）**：「按钮交互还是有延迟，并且不低」✓✓
        ///   根因：**每次刷新都要启动 CLI 子进程** ✗ 而 CLI 是 **66 MB 自包含 exe** ✓
        ///     → 一次调用就是几百毫秒的进程启动 ✓ 概览页一次刷新要 4 个 ✓ → **1~2 秒** ✗✗
        ///
        /// **纪律（重要 ✓）**：
        ///   · **只缓存只读命令** ✓（`status --detail` / `profiles` / `sessions` / `backup-list` / `config-get` ✓）
        ///   · **写操作绝不缓存** ✗（`backup` / `start` / `stop` / `config-set` / `wipe` … 一律直接跑 ✓✓）
        ///   · TTL 很短 ✓（默认 4 秒 ✓）→ 用户看到的仍是"刚刚"的状态 ✓ 不会显示过期数据 ✓
        ///   · **写操作会清空整个缓存** ✓✓（点完"停止"再刷新，看到的必须是新状态 ✓）
        ///
        /// 为什么这样是**诚实**的：这些命令读的都是**磁盘上的持久状态** ✓
        ///   4 秒内的两次读取结果**本来就一样** ✓ → 复用**不是**在猜 ✓ 是省掉一次必然相同的进程启动 ✓✓
        ///   （`status` 里的"运行中"也一样：它查的是进程是否存在 ✓ 4 秒内不会变 ✓）</summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> _cliCache =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly System.Collections.Generic.Dictionary<string, DateTime> _cliCacheAt =
            new System.Collections.Generic.Dictionary<string, DateTime>(StringComparer.Ordinal);
        // ★★★ **F5 修复（GUI 审计 MAJOR —— 数据竞争）** ✓✓
        //   ✗ 普通 `Dictionary` ✗ 而概览页的 `Task.WhenAll(tPf, tSe, tBk)` 会**三个线程同时写** ✗✗
        //     → 丢条目 ✓ 桶链错乱（**可能取到另一条命令的输出** ✗✗）✓ 甚至抛异常 ✓
        //     → 而异常被 fire-and-forget 吞掉 ✓ → **页面空白或显示旧数据** ✗
        //   ✓ 现在：**一把锁** ✓✓（读写都进锁 ✓ 微秒级 ✓ 命令本身在锁外跑 ✓ 不串行化 ✓✓）
        private static readonly object _cliCacheLock = new object();
        private const double CliCacheTtlSec = 4.0;

        /// <summary>跑一条**只读**命令，带短缓存 ✓✓。写操作请直接用 `Run` ✗ 不要走这里 ✓。</summary>
        private static string RunCached(string cli, string args)
        {
            if (string.IsNullOrEmpty(cli) || string.IsNullOrEmpty(args)) return Run(cli, args);
            lock (_cliCacheLock)
            {
                DateTime at;
                string hit;
                if (_cliCache.TryGetValue(args, out hit) && _cliCacheAt.TryGetValue(args, out at)
                    && (System.DateTime.UtcNow - at).TotalSeconds < CliCacheTtlSec)
                {
                    return hit;
                }
            }
            string outp = Run(cli, args);   // F5 FIX: run OUTSIDE the lock so the three parallel tasks do not serialise
            lock (_cliCacheLock)
            {
                _cliCache[args] = outp;
                _cliCacheAt[args] = System.DateTime.UtcNow;
            }
            return outp;
        }

        /// <summary>清空只读缓存 ✓✓。**任何写操作之后都要调它** ✓
        ///   （否则点完"停止"再刷新，4 秒内还会看到"运行中" ✗✗ —— 那就是**撒谎** ✓）</summary>
        private static void InvalidateCliCache()
        {
            try { lock (_cliCacheLock) { _cliCache.Clear(); _cliCacheAt.Clear(); } } catch { }   // F5 FIX: clear under the lock too
            InvalidateCfgCache();   // ✓ config 缓存一起清 ✓（写操作可能改了配置 ✓）
        }


        /// <summary>按命令给超时上限（2026-10-06 优化 #4）：原来 30 秒一刀切 ✗ 会把跑了一半的
        /// npm install / 大备份 / 大恢复直接杀掉（留半截状态 ✗ 交接 §9 挂账的高危项）。
        /// 长任务 10 分钟封顶 ✓ 其余维持 30 秒 —— 挂住的只读命令快速反馈更重要 ✓。
        /// RunStreaming 保持 30 秒：它跑的 doctor/update-center 是会自行收尾的只读流 ✓。</summary>
        private static int TimeoutForMs(string args)
        {
            if (string.IsNullOrEmpty(args)) return 30000;
            string a = args.TrimStart();
            string[] longCmds = new string[] { "update ", "install ", "uninstall ", "backup ", "backup-export", "backup-delete", "restore ", "import ", "bridge-install" };
            for (int i = 0; i < longCmds.Length; i++) if (a.StartsWith(longCmds[i], StringComparison.Ordinal)) return 600000;
            if (a == "update" || a == "install" || a == "uninstall" || a == "backup" || a == "restore" || a == "import") return 600000;
            return 30000;
        }

        private static string Run(string cli, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(cli, args);
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = new UTF8Encoding(false);
                psi.StandardErrorEncoding = new UTF8Encoding(false);
                using (Process p = Process.Start(psi))
                {
                    StringBuilder sb = new StringBuilder();
                    string err = "";
                    System.Threading.Tasks.Task<string> soT = System.Threading.Tasks.Task.Run(delegate { return p.StandardOutput.ReadToEnd(); });
                    System.Threading.Tasks.Task<string> seT = System.Threading.Tasks.Task.Run(delegate { return p.StandardError.ReadToEnd(); });
                    int limitMs = TimeoutForMs(args);
                    if (!p.WaitForExit(limitMs)) { try { p.Kill(); } catch { } return "（超时 " + (limitMs / 1000) + " 秒，已结束该进程）"; }
                    sb.Append(soT.Result);
                    err = seT.Result;
                    if (!string.IsNullOrEmpty(err)) sb.Append(Environment.NewLine).Append("[stderr] ").Append(err);
                    return sb.Length == 0 ? "（无输出）" : sb.ToString();
                }
            }
            catch (Exception ex)
            {
                return "运行 CLI 失败: " + ex.Message;
            }
        }

        /// <summary>流式跑一条 CLI 命令 ✓（`--stream` 专用 ✓）：stdout **每读到一行就回调**。
        ///   ★ 回调发生在**后台线程** → GUI 侧必须自己包 Dispatcher.UIThread.Post ✓（见 StreamLine ✓）。
        ///   stderr 用后台任务**先排空** ✓✓ —— 与 Run 同一套防死锁姿势 ✓
        ///   （子进程往 stderr 狂写时管道写满会把自己卡死 ✗）。
        ///   返回完整全文 ✓（供 `_rawOutput` 与解析器收尾用 ✓）。</summary>
        private static string RunStreaming(string cli, string args, System.Action<string> onLine)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(cli, args);
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = new UTF8Encoding(false);
                psi.StandardErrorEncoding = new UTF8Encoding(false);
                using (Process p = Process.Start(psi))
                {
                    System.Threading.Tasks.Task<string> seT = System.Threading.Tasks.Task.Run(delegate { return p.StandardError.ReadToEnd(); });
                    StringBuilder sb = new StringBuilder();
                    string ln;
                    while ((ln = p.StandardOutput.ReadLine()) != null)
                    {
                        sb.Append(ln).Append("\r\n");
                        if (onLine != null) { try { onLine(ln); } catch { } }
                    }
                    if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return "（超时 30 秒，已结束该进程）"; }   // 只读流保持 30 秒（见 TimeoutForMs 注释 ✓）
                    string err = seT.Result;
                    if (!string.IsNullOrEmpty(err)) sb.Append("[stderr] ").Append(err);
                    return sb.Length == 0 ? "（无输出）" : sb.ToString();
                }
            }
            catch (Exception ex)
            {
                return "运行 CLI 失败: " + ex.Message;
            }
        }

        /// <summary>把流式行**投递回 UI 线程** ✓：追加进原文 + 体检页记入逐行列表 + 立即重画 ✓✓。
        ///   每行画一次 ✓（体检 ~14 行 / 更新 ~16 行 → 每次 BuildShell 只几毫秒 ✓ 可承受 ✓）。</summary>
        private void StreamLine(string line, bool doctor)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(delegate
            {
                try
                {
                    _rawOutput += line + "\r\n";
                    if (doctor && line.StartsWith("[", StringComparison.Ordinal)) _doctorLive.Add(line);   // 只收条目行（DOCTOR_BEGIN/汇总 行不进列表 ✓）
                    BuildShell();
                }
                catch { }   // 一行画挂了不许断流 ✓
            });
        }
    }
}
