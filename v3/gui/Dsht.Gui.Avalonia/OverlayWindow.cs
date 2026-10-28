using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Dsht.Gui.Avalonia.Design;
using Dsht.Gui.Avalonia.Markers;
using Dsht.Gui.Avalonia.ViewModels;

namespace Dsht.Gui.Avalonia
{
    /// <summary>★ 悬边状态灯悬浮窗（2026-10-26 · 提案 §11 ✓✓ 维护者定案）。
    ///
    /// <para>**两态**：</para>
    /// <para>【悬边态】贴屏幕四边之一 ✓ 只显示**状态灯**（圆点/短条 ✓ 极窄 ✓）；
    ///   ✗ 不抢焦点（`ShowActivated=false` + `Focusable=false` ✓ 出现/刷新不改变前台窗口 ✓
    ///     实测：第三方窗口占前台，悬浮窗出现 + 连续 4 拍刷新，前台采样 60 次零偏离 ✓）；
    ///   ✗ 灯以外的区域**点击穿透**。</para>
    /// <para>★★ **点击穿透的真实机制（2026-10-26 真机实测纠正）** ✗：</para>
    /// <para>· 原写法（窗口背景透明 + 根节点 `IsHitTestVisible=false`）**两头都不成立** ✗：
    ///   ① Avalonia 11 在 Win32 上把「透明窗口」做成 **DirectComposition + `WS_EX_NOREDIRECTIONBITMAP`**
    ///      （实测扩展样式 `0x00200008` = NOREDIRECTIONBITMAP | TOPMOST，**没有** `WS_EX_LAYERED`）⇒
    ///      透明只是**画**出来的，Windows 的命中测试**按整块窗口矩形**走 ⇒ 整窗吃点击 ✗
    ///      （实测：沿窗口中线逐像素 `WindowFromPoint`，32/32 像素全部命中本窗口 ⇒ 灯以外 0 px 穿透 ✗）。
    ///   ② 根节点 `IsHitTestVisible=false` 在 Avalonia 里会**连子树一起剪掉** ⇒ 灯自己也收不到
    ///      悬停/点击 ⇒ 展开不了 ✗（实测：点灯本体 32×84 → 32×84，纹丝不动 ✗）。</para>
    /// <para>· ⇒ 改成 **Win32 窗口区域**（`SetWindowRgn` ✓）：悬边态把窗口**裁剪成灯本身那一块** ✓
    ///   区域之外的像素**在窗口管理器层面就不属于这个窗口** ⇒ 点击天然落到下面的程序 ✓
    ///   （实测：同样逐像素扫描变成 `--OV…OV--` ✓ 灯带之外真的穿透 ✓）；展开态则**清掉区域**
    ///   （整窗可交互 ✓ 用户是主动要看列表的 ✓）。✗ 非 Windows 平台整段空转 ✗（只在 Windows 上调）。</para>
    /// <para>· 根节点不再设 `IsHitTestVisible=false`（只靠 `Background=null` ⇒ 自身不吃命中、子树照收 ✓）。</para>
    /// <para>【展开态】鼠标悬停或点击 ⇒ 展开列表 ✓ 离开 / 再点收回 ✓；
    ///   列表按 **`lastPromptAt` 倒序**（= 先后唤醒 ✓）；每行 = 状态灯 + 名称 + 计划 `完成/总数` + 数据时刻 ✓。</para>
    ///
    /// <para>**硬约束（逐条守）**：</para>
    /// <para>· 只读 ✓ 不写盘（除窗口位置/尺寸配置键 ✓ 走既有 `config-set` ✓）· 不新增进程（数据来自主窗口**同一次取数** ✓）。</para>
    /// <para>· 未知一律「未知」✓ **绝不显示 `0`** ✗（灯为灰 ✓ 计划拿不到 ⇒ 不显示数字 ✓）。</para>
    /// <para>· 四风格（A/B/C/D 含暗色）全部走 <see cref="Palette"/> ✓ 不硬编码任何颜色 ✓。</para>
    /// <para>· ★ 状态灯的**画法与取色只有一条路**（2026-10-26 接进设计 token 表 ✓）：灯色 → 语义色
    ///   <see cref="OverlayLight.SemanticOf"/>（纯判定 ✓）→ <see cref="StatusLight.Create"/>（**唯一画法**：
    ///   柔光晕 + 去饱和底色 + 半透明灯环 ✓ 提案 §12⑥ ✓）→ 色值/透明度/尺寸全部来自 <see cref="DesignTokens"/> ✓。
    ///   ✗ 本文件里没有 hex ✗ 没有 `Color.FromArgb` ✗ 没有命名颜色常量 ✗（源码级断言盯着 ✓ 改坏即红 ✓）。</para>
    /// <para>· 名称默认 = **项目/目录名 + id 短码**（来自 cwd ✓ 非对话内容 ✓）· 标题默认屏蔽 ✓
    ///   开关开启时明示「标题可能由你的对话内容派生」✓ 且**绝不写盘/写日志** ✗。</para>
    /// </summary>
    public sealed class OverlayWindow : Window
    {
        private readonly MainWindow _host;
        private readonly ParkedMemory _memory = new ParkedMemory();

        // —— 布局偏好（来自既有配置键 ✓ 七个键都在 ToolkitConfig 里 ✓）——
        private string _edge = "right";
        private double _offset = 0.5;
        private int _scale = 1;
        private string _screen = "";
        /// <summary>悬边灯样式（配置键 `overlay_light_style` 的原值 ✓ 语义键 dot/line/capsule ✓）。</summary>
        private string _lightStyle = OverlayStyleCatalog.DefaultKey;
        /// <summary>当前实际画哪一款（由 <see cref="_lightStyle"/> 经**唯一映射点**算出来 ✓）。</summary>
        private DesignTokens.LampVariant _variant = DesignTokens.DefaultLampVariant;

        // —— 视觉树 ——
        private Border _lamp;
        /// <summary>悬边灯**当前**的灯色（✗ 只有判定结果，没有色值 ✗）—— 尺寸/风格变了要按它重画一次 ✓。</summary>
        private LightColor _lampColor = LightColor.Unknown;
        private Border _panel;
        private StackPanel _rows;
        private TextBlock _head, _clock, _state, _foot;
        private Button _titleToggle;
        private TextBlock _titleNote;
        private double _panelHeight = 120;

        private bool _expanded;
        private bool _dragging;
        private bool _dragMoved;
        private PixelPoint _dragFrom;
        private PixelPoint _winFrom;
        private DispatcherTimer _collapse;

        /// <summary>最多列几行（✗ 不无限长 ✗ 也不静默丢 —— 余量如实写在页脚 ✓）。</summary>
        private const int MaxRows = 8;

        public OverlayWindow(MainWindow host)
        {
            _host = host;
            // ★ 不抢焦点（提案 §4 / §11 硬要求 ✓）：出现与刷新都不改变前台窗口 ✓
            ShowActivated = false;
            Focusable = false;
            ShowInTaskbar = false;
            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // ★ 透明底 + 无边框：卡片外的区域必须是**透明的**（配合下面的命中测试开关 ⇒ 点击穿透 ✓）
            TransparencyLevelHint = new WindowTransparencyLevel[] { WindowTransparencyLevel.Transparent };
            Background = Palette.Transparent;   // ★ 值来自 token 表 ✓（✗ 不写 Brushes.Transparent ✗ 断言盯着 ✓）
            Title = "dsh-minato 置顶进度窗";
            BuildContent();
            ApplyPlacementFromHost(true);
            UpdateFromHost();
            // ★★ 真机冒烟抓到（2026-10-26 ✗✓）：**平台/Avalonia 会给窗口一个最小宽度** ——
            //   我们请求 21×84（灯 11 + 两侧各 5 透明余量 ✓），实测窗口是 **32×84**（宽度被抬到 32 ✓ 高度一字不差 ✓）。
            //   后果：按"请求尺寸"算右贴边 ⇒ 窗口**有 11 px 探到屏幕外** ✗（违反提案 §4「不许跑到屏幕外」✗）。
            //   ⇒ 修法：定位一律用**实际尺寸**（`Bounds`/`ClientSize` ✓ 平台抬过之后的值 ✓），
            //     并在 `Opened`（平台尺寸已定）与 `SizeChanged` 后各重算一次 ✓ —— 只动定位 ✗ 不动判定 ✗。
            Opened += delegate
            {
                ApplyPosition();
                ApplyRegion();
                // ★ 一次性淡入 + 轻微位移（≤200ms ✓ 只播一次 ✗ 不循环 ✗）；减少动效 ⇒ 瞬变 ✓
                try { DesignTokens.EdgeLampSpec one = EdgeSpec(); StatusLight.FadeIn(_lamp, one.FadeDy); } catch { }
            };
            SizeChanged += delegate { ApplyPosition(); ApplyRegion(); };
            // ★ 关键：`Opened` / `SizeChanged` 都在**布局跑完之前**触发 ⇒ 那一拍 `_lamp.Bounds` 还是 0,0,0,0
            //   （实测：DBG bounds=0, 0, 0, 0 ✗）⇒ 必须**布局跑完再裁一次** ✓
            //   用 `LayoutUpdated` + 上次值缓存 ⇒ 只在灯矩形真的变了才调 `SetWindowRgn` ✗ 不每拍都调 ✗
            LayoutUpdated += delegate { ApplyRegion(); };
        }

        // ================================================================ 点击穿透（★ Win32 窗口区域）

        // ★ 真机踩到的坑（2026-10-26）：`CreateRectRgn` / `DeleteObject` 在 **gdi32.dll**，
        //   `SetWindowRgn` 才在 **user32.dll** ✗ 全写 user32 会 `EntryPointNotFoundException`；
        //   而它被 `catch {}` 吞掉 ⇒ 表现成"什么都没发生"（窗口照旧吃满点击 ✗ 极难查 ✗）。
        //   ⇒ 这里**不写盘、不弹窗**（硬约束：除配置键外零写入 ✗）⇒ 改用**源码级门槛**把入口钉死：
        //     `v3/tests/verify_overlay_wiring.ps1` 断言 CreateRectRgn 必须来自 gdi32.dll ✓（改回去即红 ✓）。
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        // 上次应用的区域（物理像素 ✓ int.MinValue = 还没应用过）
        private int _rgnL = int.MinValue, _rgnT = 0, _rgnR = 0, _rgnB = 0;
        private bool _rgnCleared;

        /// <summary>★ 点击穿透的**唯一执行点**（2026-10-26 真机实测后改的 ✓ 原方案不成立 ✗ 见类注释）：
        /// 悬边态把窗口区域**裁剪成灯本身那一块**（区域之外在窗口管理器层面就不属于本窗口 ⇒ 点击落到下面 ✓）；
        /// 展开态**清掉区域**（整窗可交互 ✓）。
        /// <para>★ 只在 Windows 上调（✗ 非 Windows 整段空转 ✗ 不影响其它平台的可构建性 ✓）。</para>
        /// <para>★ 用**灯的实际布局矩形**（<see cref="Visual.Bounds"/> ✓ 布局说了算 ✗ 不靠"我以为是居中" ✗）
        /// 乘 <see cref="TopLevel.RenderScaling"/> 换成物理像素 ✓（区域坐标是物理像素 ✓）。</para>
        /// <para>★ 幂等：同一个矩形只调一次（`LayoutUpdated` 会高频触发 ✗ 不能每拍都 `SetWindowRgn` ✗）。</para></summary>
        private void ApplyRegion()
        {
            if (!System.OperatingSystem.IsWindows()) return;
            try
            {
                global::Avalonia.Platform.IPlatformHandle ph = TryGetPlatformHandle();
                IntPtr h = ph == null ? IntPtr.Zero : ph.Handle;
                if (h == IntPtr.Zero) return;
                if (_expanded)
                {
                    if (_rgnCleared) return;                           // 幂等 ✓
                    SetWindowRgn(h, IntPtr.Zero, true);                // 展开态：清区域 ⇒ 整窗可交互 ✓
                    _rgnCleared = true; _rgnL = int.MinValue;
                    return;
                }
                if (_lamp == null) return;
                Rect b = _lamp.Bounds;
                if (b.Width <= 1 || b.Height <= 1) return;             // 布局还没跑完 ⇒ 这一拍不动 ✗ 不乱裁 ✗
                double k = RenderScaling <= 0 ? 1.0 : RenderScaling;
                int l = (int)Math.Floor(b.X * k);
                int t = (int)Math.Floor(b.Y * k);
                int r = (int)Math.Ceiling((b.X + b.Width) * k);
                int bo = (int)Math.Ceiling((b.Y + b.Height) * k);
                if (r - l < 1 || bo - t < 1) return;
                if (!_rgnCleared && l == _rgnL && t == _rgnT && r == _rgnR && bo == _rgnB) return;   // 幂等 ✓
                IntPtr rgn = CreateRectRgn(l, t, r, bo);
                if (rgn == IntPtr.Zero) return;
                if (SetWindowRgn(h, rgn, true) == 0) { DeleteObject(rgn); return; }   // 失败 ⇒ 自己回收 ✓
                // ★ 成功时区域**归窗口管理器所有** ✗ 绝不能再 DeleteObject ✗（那是双释放 ✓）
                _rgnL = l; _rgnT = t; _rgnR = r; _rgnB = bo; _rgnCleared = false;
            }
            catch { }
        }

        // ================================================================ 灯的画法（★ 唯一出处）

        /// <summary>★ 悬边灯的**唯一画法**：<see cref="StatusLight.Create"/>（柔光晕 + 去饱和底色 + 半透明灯环 ✓
        /// 提案 §12⑥ ✓）。本文件 ✗ 不自己画灯 ✗ 也不自己取色 ✗ ——
        /// 灯色 → 语义色走 <see cref="OverlayLight.SemanticOf"/>（纯判定 ✓）；色值 / 透明度 / 尺寸 / 光晕
        /// 全部来自 <see cref="DesignTokens"/> ✓（✗ 本文件里没有 hex、没有 `Color.FromArgb`、没有命名颜色 ✗ 断言盯着 ✓）。
        /// <para>灯色变了（每拍刷新）/ 风格变了（A/B/C/D）/ 档位变了（`overlay_scale`）⇒ 重画一次 ✓
        /// （幂等 ✓ 不重算判定 ✓ —— 判定结果就是 <see cref="_lampColor"/> 本身 ✓）。</para>
        /// <para>★ 三款画法（极简细条 / 圆点轻环 / 双端点胶囊）都走 <see cref="StatusLight.Create"/> 的
        /// 同一个重载 ✓ 几何与透明度由 <see cref="DesignTokens.EdgeLampOf"/> 给 ✓（✗ 本文件不自造 ✗）。</para></summary>
        private void ApplyLampLight()
        {
            if (_lamp == null) return;
            _lamp.Child = StatusLight.Create(OverlayLight.SemanticOf(_lampColor), Palette.StyleKind, EdgeSpec());
        }

        /// <summary>当前档位 + 边方向 + 变体下的灯几何（**全部从 token 表算** ✓
        /// 基础厚度/长度/余量/圆点也由既有 token 给 ✓ ✗ 界面文件里没有自造灯几何 ✗）。
        /// <para>★ 变体来自**配置键** `overlay_light_style`（<see cref="_variant"/> ✓ 由
        /// <see cref="OverlayStyleCatalog.VariantOf"/> 映射）；✗ 不是环境变量 ✗ 也不是第二套状态 ✗。</para></summary>
        private DesignTokens.EdgeLampSpec EdgeSpec()
        {
            double t = DesignTokens.LampThicknessOf(_scale);
            double l = DesignTokens.LampLengthOf(_scale);
            double m = DesignTokens.LampMarginOf(_scale);
            double dot = DesignTokens.LampDotOf(t);
            bool vert = _edge == "left" || _edge == "right";
            return DesignTokens.EdgeLampOf(_variant, vert, t, l, m, dot);
        }

        /// <summary>灯体卡片的外观（四风格 + 三变体共用；✗ 不变色 ≠ 不改画法 ✗）。
        /// B 的卡片是正圆（CornerRadius = 半厚），A/C 是胶囊；颜色仍只从 Palette 取 ✓。</summary>
        private void ApplyLampChrome()
        {
            if (_lamp == null) return;
            DesignTokens.EdgeLampSpec spec = EdgeSpec();
            _lamp.Background = Palette.CardBg;
            _lamp.BorderBrush = Palette.BorderStrong;
            _lamp.BorderThickness = new Thickness(ShapeTokens.Stroke);
            _lamp.CornerRadius = new CornerRadius(spec.CornerRadius);
            _lamp.BoxShadow = string.IsNullOrEmpty(Palette.CardShadow) ? default(BoxShadows) : BoxShadows.Parse(Palette.CardShadow);
        }

        // ================================================================ 视觉树

        private void BuildContent()
        {
            // ★ 根：**自身不吃命中**（`Background=null` ⇒ 空处不参与命中测试 ✓）但**不剪子树** ✗ ——
            //   实测：`IsHitTestVisible=false` 在 Avalonia 里连子树一起剪掉 ⇒ 灯自己收不到悬停/点击 ✗✗
            //   灯以外的穿透改由 **Win32 窗口区域**负责（见 `ApplyRegion` ✓ 那才是真的穿透 ✓）。
            Grid root = new Grid { Background = null };
            _lamp = new Border
            {
                // ★ 灯本体可命中 ✓（悬停展开 / 点击切换 / 拖动记忆位置 ✓）
                IsHitTestVisible = true,
                Background = Palette.CardBg,
                BorderBrush = Palette.BorderStrong,
                BorderThickness = new Thickness(ShapeTokens.Stroke),
                CornerRadius = new CornerRadius(ShapeTokens.RPill),
                BoxShadow = string.IsNullOrEmpty(Palette.CardShadow) ? default(BoxShadows) : BoxShadows.Parse(Palette.CardShadow)
            };
            ApplyLampLight();   // ★ 灯 = 唯一画法 StatusLight（柔光晕 + 去饱和 ✓）✗ 不在这里自己画 ✗
            _lamp.PointerEntered += delegate { Expand(true); };
            _lamp.PointerPressed += OnLampPressed;
            _lamp.PointerMoved += OnLampMoved;
            _lamp.PointerReleased += OnLampReleased;
            // ★ 右键菜单：灯本体与展开面板都能右键 ✓（灯以外的透明区在窗口区域外 ⇒ 本来就收不到右键 ✓）
            _lamp.ContextMenu = BuildLampMenu();
            root.Children.Add(_lamp);

            _panel = new Border
            {
                IsVisible = false,
                IsHitTestVisible = true,          // 展开态是**用户主动要看的** ⇒ 面板本身可交互 ✓
                Background = Palette.CardBg,
                BorderBrush = Palette.BorderStrong,
                BorderThickness = new Thickness(ShapeTokens.Stroke),
                CornerRadius = new CornerRadius(ShapeTokens.RCard),
                Padding = new Thickness(ShapeTokens.S12),
                BoxShadow = string.IsNullOrEmpty(Palette.CardShadow) ? default(BoxShadows) : BoxShadows.Parse(Palette.CardShadow)
            };
            StackPanel col = new StackPanel { Spacing = ShapeTokens.S8 };

            Grid headRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            _head = new TextBlock { FontSize = ShapeTokens.FsEm, FontWeight = FontWeight.Bold, Foreground = Palette.Text, Text = "进度" };
            Grid.SetColumn(_head, 0);
            headRow.Children.Add(_head);
            _clock = new TextBlock { FontSize = ShapeTokens.FsFoot, Foreground = Palette.TextFaint, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_clock, 1);
            headRow.Children.Add(_clock);
            col.Children.Add(headRow);

            _state = new TextBlock { FontSize = ShapeTokens.FsBody, Foreground = Palette.TextDim, TextWrapping = TextWrapping.Wrap };
            col.Children.Add(_state);

            _rows = new StackPanel { Spacing = 3 };
            col.Children.Add(_rows);

            _titleNote = new TextBlock { FontSize = ShapeTokens.FsFoot, Foreground = Palette.TextFaint, TextWrapping = TextWrapping.Wrap, IsVisible = false };
            col.Children.Add(_titleNote);

            _titleToggle = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Palette.CardHover,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl),
                Padding = new Thickness(10, 4),
                Content = new TextBlock { FontSize = ShapeTokens.FsFoot, Foreground = Palette.TextDim }
            };
            _titleToggle.Click += delegate { _host.ToggleOverlayShowTitle(); };
            col.Children.Add(_titleToggle);

            _foot = new TextBlock { FontSize = ShapeTokens.FsFoot, Foreground = Palette.TextFaint, TextWrapping = TextWrapping.Wrap };
            col.Children.Add(_foot);

            _panel.Child = col;
            _panel.ContextMenu = BuildLampMenu();
            root.Children.Add(_panel);
            Content = root;

            // 离开窗口 ⇒ 收回（延迟一拍，避免从灯移到面板时误收 ✓）
            PointerExited += delegate { ScheduleCollapse(); };
            PointerEntered += delegate { CancelCollapse(); };
            _collapse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _collapse.Tick += delegate { _collapse.Stop(); if (!_dragging) Expand(false); };
        }

        // ================================================================ 右键菜单（★ 两档"关"必须分清）

        /// <summary>★ 右键菜单（2026-10-28）：三项 —— 本次隐藏 / 永久关闭 / 打开主窗口。
        /// <para>· **本次隐藏**：只隐藏窗口，**不写配置**（`overlay_enabled` 不变 ✓）；看板「置顶进度窗」
        ///   或托盘菜单可再显示 ✓。</para>
        /// <para>· **永久关闭**：写 `overlay_enabled=false` 落盘 ✓，并明示之后可在设置里重新开启 ✓。</para>
        /// <para>★ 两档文案来自 <see cref="OverlayMenu"/> 的唯一出处（✗ 不在界面里各写一份 ✗），
        /// 且靠副标题把"只是藏起来"与"关掉功能"说到一眼能分 ✓。</para></summary>
        private ContextMenu BuildLampMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(MenuItemOf(OverlayMenuAction.SessionHide, delegate { _host.HideOverlayForSession(); }));
            menu.Items.Add(MenuItemOf(OverlayMenuAction.PermanentClose, delegate { _host.CloseOverlayPermanently(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemOf(OverlayMenuAction.OpenMain, delegate { _host.ShowMainFromOverlay(); }));
            return menu;
        }

        /// <summary>一项菜单 = 主标题 + 一句副标题（副标题是两档不混的关键 ✓）。</summary>
        private static MenuItem MenuItemOf(OverlayMenuAction action, Action onClick)
        {
            StackPanel head = new StackPanel { Spacing = 2 };
            head.Children.Add(new TextBlock
            {
                Text = OverlayMenu.TitleOf(action),
                FontSize = ShapeTokens.FsBody,
                FontWeight = FontWeight.SemiBold,
                Foreground = Palette.Text
            });
            string detail = OverlayMenu.DetailOf(action);
            if (!string.IsNullOrEmpty(detail))
            {
                head.Children.Add(new TextBlock
                {
                    Text = detail,
                    FontSize = ShapeTokens.FsFoot,
                    Foreground = Palette.TextFaint,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            MenuItem mi = new MenuItem { Header = head };
            mi.Click += delegate { onClick(); };
            global::Avalonia.Automation.AutomationProperties.SetName(mi, OverlayMenu.TitleOf(action));
            return mi;
        }

        // ================================================================ 展开 / 收回

        /// <summary>展开 / 收回（`hover=true` 表示由悬停触发 ✓ 收回时**保留**悬停语义 ✓）。</summary>
        public void Expand(bool on)
        {
            if (_expanded == on) return;
            _expanded = on;
            _panel.IsVisible = on;
            if (on) _lamp.CornerRadius = new CornerRadius(ShapeTokens.RCtrl);
            else ApplyLampChrome();   // 收回 ⇒ 变体自己的圆角（B 正圆 / A·C 胶囊）✓
            ApplySize();
            // ★★ 真机冒烟抓到的第二个坑（2026-10-26 ✗✓）：展开那一刻窗口高度还是构造时的初值
            //   （`_panelHeight = 120`）⇒ 列表被窗口**裁掉**，只剩两行可见 ✗✗
            //   —— 原来只在**下一次取数**（`UpdateFromHost`）里才量高度 ⇒ `gui_auto_refresh=off`
            //   或刷新间隔很长时，用户点开看到的就是一个被切的列表 ✓（实测截图抓到 ✓）
            //   ⇒ 展开时**立刻按内容量一次**再定尺寸 ✓（✗ 不等下一拍 ✗）。
            if (on) { FitPanelHeight(); ApplySize(); }
            ApplyPosition();
            ApplyRegion();   // ★ 区域随两态切换（悬边态=只有灯 ✓ 展开态=整窗 ✓）
        }

        /// <summary>按**内容实际需要**量一次展开面板的高度（写回 <see cref="_panelHeight"/> ✓ 不抛 ✓）。
        /// 宽度取当前档位该有的面板宽（✗ 不拿 NaN 去量 ✗ 那会量成 0 ✓）。</summary>
        private void FitPanelHeight()
        {
            try
            {
                double w = _panel.Width > 0 ? _panel.Width : (_scale == 0 ? 320 : (_scale == 2 ? 430 : 380));
                _panel.Measure(new Size(w, double.PositiveInfinity));
                double want = _panel.DesiredSize.Height + 2 * DesignTokens.LampMarginOf(_scale);
                if (want > 40) _panelHeight = want;
            }
            catch { }
        }

        public bool IsExpanded { get { return _expanded; } }

        private void ScheduleCollapse()
        {
            if (!_expanded) return;
            CancelCollapse();
            _collapse.Start();
        }

        private void CancelCollapse() { if (_collapse != null) _collapse.Stop(); }

        // ================================================================ 拖动（位置记忆 ✓ 提案 §4）

        private void OnLampPressed(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true; _dragMoved = false;
            _dragFrom = ScreenPixelOf(e);
            _winFrom = Position;
            try { e.Pointer.Capture(_lamp); } catch { }
        }

        private void OnLampMoved(object sender, PointerEventArgs e)
        {
            if (!_dragging) return;
            PixelPoint p = ScreenPixelOf(e);
            int dx = p.X - _dragFrom.X, dy = p.Y - _dragFrom.Y;
            if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 4) return;
            _dragMoved = true;
            Position = new PixelPoint(_winFrom.X + dx, _winFrom.Y + dy);
        }

        private void OnLampReleased(object sender, PointerReleasedEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            try { e.Pointer.Capture(null); } catch { }
            if (_dragMoved) SnapAndPersist();       // 拖动 ⇒ 吸附到最近边 + 记住位置 ✓
            else Expand(!_expanded);                // 没拖动 ⇒ 当作点击 ⇒ 展开 / 收回 ✓
        }

        /// <summary>把 Avalonia 的窗口内坐标（DIP）换算成**屏幕像素**（拖动位移要用同一坐标系 ✓）。</summary>
        private PixelPoint ScreenPixelOf(PointerEventArgs e)
        {
            Point p = e.GetPosition(this);
            double sc = ScalingOf(ScreenOfWindow());
            return new PixelPoint(Position.X + (int)Math.Round(p.X * sc), Position.Y + (int)Math.Round(p.Y * sc));
        }

        /// <summary>吸附到最近的一条边 + 算出沿边的相对位置 ⇒ 写进配置（三个键 ✓ 走既有 config-set ✓）。</summary>
        private void SnapAndPersist()
        {
            Screen sc = ScreenOfWindow() ?? PickScreen();
            if (sc == null) return;
            PixelRect wa = sc.WorkingArea;
            double k = ScalingOf(sc);
            double dw = Bounds.Width > 1 ? Bounds.Width : Width;
            double dh = Bounds.Height > 1 ? Bounds.Height : Height;
            double pw = Math.Max(1, dw * k), ph = Math.Max(1, dh * k);
            double cx = Position.X + pw / 2, cy = Position.Y + ph / 2;
            double dl = Math.Abs(cx - wa.X), dr = Math.Abs(wa.X + wa.Width - cx);
            double dt = Math.Abs(cy - wa.Y), db = Math.Abs(wa.Y + wa.Height - cy);
            string edge = "right"; double best = dr;
            if (dl < best) { edge = "left"; best = dl; }
            if (dt < best) { edge = "top"; best = dt; }
            if (db < best) { edge = "bottom"; best = db; }
            double off;
            if (edge == "left" || edge == "right")
            {
                double span = wa.Height - ph;
                off = span > 1 ? (cy - wa.Y - ph / 2) / span : 0.5;
            }
            else
            {
                double span = wa.Width - pw;
                off = span > 1 ? (cx - wa.X - pw / 2) / span : 0.5;
            }
            _edge = edge;
            _offset = Clamp01(off);
            _screen = ScreenName(sc);
            Expand(false);
            ApplySize();      // ★ 收起后尺寸变了 ⇒ 必须显式重算（Expand 在"状态没变"时会早退 ✓ 不能只靠它 ✓）
            ApplyPosition();
            _host.PersistOverlayPlacement(_edge, _offset, _screen);
        }

        // ================================================================ 位置 / 尺寸（✗ 不许跑到屏幕外 ✗）

        /// <summary>从主窗口已解析的配置里取七个键 ✓（**不新起进程** ✓ 主窗口那份 config-get 是共用的 ✓）。
        /// <para>★ 灯样式也在这条路上（与位置/尺寸同一份配置 ✓ 设置页改了它 ⇒ 这里立刻跟着换 ✓）。</para></summary>
        public void ApplyPlacementFromHost(bool force)
        {
            string e = _host.OverlayEdgeValue;
            double o = _host.OverlayOffsetValue;
            int s = _host.OverlayScaleValue;
            string scr = _host.OverlayScreenValue;
            string style = OverlayStyleCatalog.NormalizeKey(_host.OverlayLightStyleValue);
            bool changed = force || e != _edge || Math.Abs(o - _offset) > 0.0001 || s != _scale
                || !string.Equals(scr, _screen, StringComparison.Ordinal)
                || !string.Equals(style, _lightStyle, StringComparison.Ordinal);
            _edge = e; _offset = o; _scale = s; _screen = scr; _lightStyle = style;
            _variant = OverlayStyleCatalog.VariantOf(_lightStyle);
            if (changed) { ApplySize(); ApplyPosition(); }
        }

        // ★ 灯的几何（厚度 / 长度 / 透明余量 / 圆点）**全部来自 token 表** ✗ 本文件不再自造这些数值 ✗
        //   （断言：这里若再出现 `_scale == 0 ? 8 : …` 这类字面量 ⇒ 红 ✓）

        private void ApplySize()
        {
            if (_expanded)
            {
                double k = ScalingOf(ScreenOfWindow());
                Width = _scale == 0 ? 320 : (_scale == 2 ? 430 : 380);
                Height = Math.Max(96, _panelHeight);
                // 面板宽度随档位变 ⇒ 让内容自己量一次高度（下一拍 UpdateFromHost 会写回 _panelHeight ✓）
                _panel.Width = Width - 2 * DesignTokens.LampMarginOf(_scale);
                _panel.Height = double.NaN;
            }
            else
            {
                _panel.Width = double.NaN;
                _panel.Height = double.NaN;
                // ★ 几何全部由 token 算（含三款变体）✗ 本文件不出现自造灯尺寸 ✗
                DesignTokens.EdgeLampSpec spec = EdgeSpec();
                _lamp.Width = spec.Width;
                _lamp.Height = spec.Height;
                ApplyLampChrome();   // 变体/边/档变了 ⇒ 圆角与卡片外观跟着重算 ✓
                ApplyLampLight();    // 档位/边/变体变了 ⇒ 按 token 重画一次（唯一画法 ✓）
                Width = spec.Width + 2 * spec.Margin;
                Height = spec.Height + 2 * spec.Margin;
            }
            ApplyRegion();   // ★ 尺寸/档位/边变了 ⇒ 区域跟着重算（布局跑完那一拍 `SizeChanged` 会再算一次 ✓）
        }

        /// <summary>定位：贴边（inset=0 ✓）+ 沿边相对位置 ✓ + **钳进该屏工作区** ✓
        /// ⇒ 显示器拔掉回落主屏 ✓ 任何情况下都**不会跑到屏幕外** ✗（提案 §4 硬要求 ✓）。
        /// ★ 尺寸取**实际值**（`Bounds` ✓ 平台可能把窗口抬到它的最小尺寸 ✗ 见构造函数的说明 ✓）。</summary>
        private void ApplyPosition()
        {
            Screen sc = PickScreen();
            if (sc == null) return;
            PixelRect wa = sc.WorkingArea;
            double k = ScalingOf(sc);
            double dw = Bounds.Width > 1 ? Bounds.Width : Width;
            double dh = Bounds.Height > 1 ? Bounds.Height : Height;
            double pw = Math.Max(1, dw * k), ph = Math.Max(1, dh * k);
            double x, y;
            if (_edge == "left") { x = wa.X; y = wa.Y + (wa.Height - ph) * _offset; }
            else if (_edge == "top") { x = wa.X + (wa.Width - pw) * _offset; y = wa.Y; }
            else if (_edge == "bottom") { x = wa.X + (wa.Width - pw) * _offset; y = wa.Y + wa.Height - ph; }
            else { x = wa.X + wa.Width - pw; y = wa.Y + (wa.Height - ph) * _offset; }
            // ★ 钳：无论 offset 多离谱 / 屏幕多小，窗口都留在工作区内 ✓
            x = Clamp(x, wa.X, wa.X + Math.Max(0, wa.Width - pw));
            y = Clamp(y, wa.Y, wa.Y + Math.Max(0, wa.Height - ph));
            try { Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y)); } catch { }
        }

        private Screen ScreenOfWindow()
        {
            try { return Screens.ScreenFromWindow(this); } catch { return null; }
        }

        /// <summary>挑屏：记住的那块（按 `DisplayName`，拿不到就按 `#序号`）→ 找不到 ⇒ **主屏** ✓ 不猜 ✗。</summary>
        private Screen PickScreen()
        {
            try
            {
                IReadOnlyList<Screen> all = Screens.All;
                if (all == null || all.Count == 0) return null;
                if (!string.IsNullOrEmpty(_screen))
                {
                    for (int i = 0; i < all.Count; i++) if (string.Equals(ScreenName(all, i), _screen, StringComparison.Ordinal)) return all[i];
                    // 认不出（显示器拔掉了 / 换了接口）⇒ 回落主屏 ✓（并在面板上如实说一句 ✓）
                }
                for (int i = 0; i < all.Count; i++) if (all[i].IsPrimary) return all[i];
                return all[0];
            }
            catch { return null; }
        }

        /// <summary>这块屏记住的名字（`DisplayName` 优先 ✓ 拿不到用 `#序号` ✓ —— 序号取自**同一份列表** ✓ 不猜 ✓）。</summary>
        public static string ScreenName(IReadOnlyList<Screen> all, int idx)
        {
            if (all == null || idx < 0 || idx >= all.Count) return "";
            Screen s = all[idx];
            if (s == null) return "";
            string n = s.DisplayName;
            if (!string.IsNullOrEmpty(n)) return n;
            return "#" + idx;
        }

        /// <summary>当前窗口所在屏的记住名（拿不到 ⇒ 空串 ✓ 不编 ✗）。</summary>
        private string ScreenName(Screen s)
        {
            if (s == null) return "";
            try
            {
                IReadOnlyList<Screen> all = Screens.All;
                if (all != null) for (int i = 0; i < all.Count; i++) if (ReferenceEquals(all[i], s)) return ScreenName(all, i);
            }
            catch { }
            return s.DisplayName == null ? "" : s.DisplayName;
        }

        private static double ScalingOf(Screen s) { try { return s == null || s.Scaling <= 0 ? 1.0 : s.Scaling; } catch { return 1.0; } }
        private static double Clamp(double v, double lo, double hi) { if (hi < lo) return lo; return v < lo ? lo : (v > hi ? hi : v); }
        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        // ================================================================ 数据（★ 复用主窗口那一次取数 ✓ 不新起进程 ✗）

        /// <summary>刷新灯色与列表（由主窗口在**同一次取数**之后调用 ✓）。
        /// 判定顺序固定：**先判定（读记忆）→ 再记忆这一拍** ✓（否则本拍的 active=1 会被当成"更早"⇒ 假推断 ✗）。</summary>
        public void UpdateFromHost()
        {
            ApplyPlacementFromHost(false);
            Recolor();   // ★ 四风格（A/B/C/D 含暗色）：每次刷新重取 Palette ✓ 切换风格后立刻跟随 ✓ 不硬编码颜色 ✓
            SessionsSnapshot d = _host.Data;
            List<SessionRow> rows = OverlayLight.SortedByLastPrompt(d != null && d.Ok ? d.Rows : null);
            OverlayContext ctx = _host.OverlayContextOf(d);
            Light edge = OverlayLight.EdgeLight(ctx, rows, _memory);

            _lampColor = edge.Color;   // ★ 判定结果（✗ 不是色值 ✗）⇒ 下面按它重画一次灯
            ApplyLampChrome();
            ApplyLampLight();
            _state.Text = OverlayLight.EdgeSummary(edge);
            _state.Foreground = edge.IsLit ? Palette.Text : Palette.TextFaint;
            _clock.Text = "数据时刻 " + OverlayLight.ClockText(_host.DataAt);

            _rows.Children.Clear();
            if (rows.Count == 0)
            {
                _rows.Children.Add(new TextBlock
                {
                    FontSize = ShapeTokens.FsBody,
                    Foreground = Palette.TextFaint,
                    TextWrapping = TextWrapping.Wrap,
                    Text = (d == null || !d.Ok) ? "会话列表：未知（这次没读到会话投影）" : "当前窗口里没有会话"
                });
            }
            else
            {
                int n = Math.Min(rows.Count, MaxRows);
                for (int i = 0; i < n; i++) _rows.Children.Add(RowControl(rows[i], ctx));
            }

            // 页脚：把"为什么某些档是未知"如实说清 ✓（✗ 不猜 ✗ 不假装 ✗）
            string foot = "";
            if (!ctx.BridgePresent) foot = OverlayLight.NoBridgeText;
            if (rows.Count > MaxRows) foot += (foot.Length > 0 ? " " : "") + "另有 " + (rows.Count - MaxRows) + " 个会话未列出。";
            if (!string.IsNullOrEmpty(_screen) && !string.Equals(_screen, ScreenName(ScreenOfWindow()), StringComparison.Ordinal))
                foot += (foot.Length > 0 ? " " : "") + "记住的那块显示器没找到，已回到主屏。";
            _foot.Text = foot;
            _foot.IsVisible = foot.Length > 0;

            bool showTitle = _host.OverlayShowTitleValue;
            _titleNote.IsVisible = showTitle;
            _titleNote.Text = OverlayLight.TitleNotice;
            ((TextBlock)_titleToggle.Content).Text = showTitle ? "显示会话标题：开（点一下关掉）" : "显示会话标题：关（默认）";
            _titleToggle.IsVisible = true;

            // 展开态的高度随行数走 ⇒ 量一次再定位（✗ 不挤压不溢出 ✓ 四风格通用 ✓）
            if (_expanded)
            {
                double before = _panelHeight;
                FitPanelHeight();
                if (Math.Abs(_panelHeight - before) > 0.5) { ApplySize(); ApplyPosition(); ApplyRegion(); }
            }

            _memory.Remember(rows);   // ★ 判定之后再记 ✓
        }

        /// <summary>把 <see cref="Palette"/> 的当前值重新贴到视觉树上 ✓（风格 A/B/C/D 切换后必须调 ✓
        /// 否则深色风格下会留着浅色底 ✗ —— 这正是"四风格都要不挤压不溢出"之外的另一条硬要求 ✓）。</summary>
        public void Recolor()
        {
            try
            {
                ApplyLampChrome();   // 变体不同 ⇒ 圆角不同（B 是正圆）✓ 三款都从 Palette 取色 ✓
                _panel.Background = Palette.CardBg;
                _panel.BorderBrush = Palette.BorderStrong;
                _head.Foreground = Palette.Text;
                _clock.Foreground = Palette.TextFaint;
                _foot.Foreground = Palette.TextFaint;
                _titleNote.Foreground = Palette.TextFaint;
                _titleToggle.Background = Palette.CardHover;
                _titleToggle.BorderBrush = Palette.Border;
                if (_titleToggle.Content is TextBlock tt) tt.Foreground = Palette.TextDim;
                ApplyLampLight();   // 风格变了 ⇒ 悬边灯按新风格重画一次（唯一画法 ✓ 不重算判定 ✓）
                for (int i = 0; i < _rows.Children.Count; i++)
                {
                    Grid g = _rows.Children[i] as Grid;
                    if (g == null || g.Children.Count < 4) continue;
                    // ★ 每行的灯与悬边灯**同一处画法**（StatusLight ✓）✗ 不在这里内联画 ✗
                    LightColor lc = g.Tag is LightColor ? (LightColor)g.Tag : LightColor.Unknown;
                    g.Children[0] = StatusLight.Create(OverlayLight.SemanticOf(lc), Palette.StyleKind, DesignTokens.LampRowDot);
                    TextBlock nm = g.Children[1] as TextBlock; if (nm != null) nm.Foreground = Palette.Text;
                    TextBlock pl = g.Children[2] as TextBlock; if (pl != null) pl.Foreground = Palette.TextDim;
                    TextBlock ck = g.Children[3] as TextBlock; if (ck != null) ck.Foreground = Palette.TextFaint;
                }
            }
            catch { }
        }

        /// <summary>一行：状态灯 + 名称 + 计划 `完成/总数` + 数据时刻 ✓（✗ 无标题（默认）✗ 无正文 ✗ 无工具参数 ✗）。</summary>
        private Control RowControl(SessionRow r, OverlayContext ctx)
        {
            Light one = OverlayLight.RowLight(r, ctx, _memory.PriorActive(r.Id));
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("12,*,Auto,Auto"), Height = _scale == 0 ? 18 : 21 };
            g.Tag = one.Color;   // 重上色用（风格切换 ✓ 不重算判定 ✓）
            // ★ 每盏灯都走**同一处画法**（`Design/StatusLight.cs` ✓ 柔光晕 + 去饱和底色 ✓）
            //   ✗ 不在这里 `new Ellipse` 另画一套 ✗；直径取 token 表（`DesignTokens.LampRowDot` ✓）
            Control dot = StatusLight.Create(OverlayLight.SemanticOf(one.Color), Palette.StyleKind, DesignTokens.LampRowDot);
            Grid.SetColumn(dot, 0);
            g.Children.Add(dot);
            TextBlock name = new TextBlock
            {
                Text = OverlayLight.RowName(r, _host.OverlayShowTitleValue),
                FontSize = ShapeTokens.FsBody,
                Foreground = Palette.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(ShapeTokens.S8, 0, ShapeTokens.S8, 0)
            };
            Grid.SetColumn(name, 1);
            g.Children.Add(name);
            TextBlock plan = new TextBlock
            {
                Text = OverlayLight.PlanText(r),   // ★ 拿不到 ⇒「计划未知」✗ 一个数字都不显示 ✓
                FontSize = ShapeTokens.FsFoot,
                Foreground = Palette.TextDim,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, ShapeTokens.S8, 0)
            };
            Grid.SetColumn(plan, 2);
            g.Children.Add(plan);
            TextBlock clock = new TextBlock
            {
                Text = OverlayLight.RowClockText(r),   // ★ 每行都带数据时刻 ✓
                FontSize = ShapeTokens.FsFoot,
                Foreground = Palette.TextFaint,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(clock, 3);
            g.Children.Add(clock);
            // 每盏灯都能指到它的信号来源 ✓（悬停即可核对 ✓ 灰灯如实说没有信号 ✓）
            ToolTip.SetTip(g, OverlayLight.Describe(one));
            return g;
        }
    }
}
