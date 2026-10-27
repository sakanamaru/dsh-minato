# verify_overlay_wiring.ps1 —— 悬边状态灯悬浮窗的**接线**源码级断言（2026-10-26 · 提案 §11）
#
# 为什么单开一个脚本：GUI 逻辑测试（Dsht.Gui.LogicTests）**只编译 Markers/*.cs**
#   （不引用 Avalonia、不编译 Shells/ 与 OverlayWindow.cs）⇒ 它证不了两件事：
#     ① 看板里**真的没有**第二份标题开关（接线统一）
#     ② 悬浮窗**真的**设了 `ShowActivated=false` / 灯以外 `IsHitTestVisible=false`
#   这两件是**界面接线**，只能对源码文本断言 —— 就是本脚本。
#
# 纪律：只读源码，不构建、不写盘（可反复跑）；每条断言**可证伪**（把对应那一行改坏即红）。
# 注意：本文件含非 ASCII ⇒ **必须带 UTF-8 BOM**（verify_switchover 的 `invariant ps1 utf8 bom` 会查）；
#       本文件按 Windows PowerShell 5.1 写（✗ 不用行首 `-and` 续行 ✗ 那是 PS 7 才认的写法）。
param([string]$Repo = 'D:\dsh-minato\repo')

$ErrorActionPreference = 'Stop'
$pass = 0
$fail = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { $script:pass++; Write-Host ("  [PASS] " + $name) }
    else { $script:fail++; Write-Host ("  [FAIL] " + $name) }
}
function Src([string]$rel) {
    $p = Join-Path $Repo $rel
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    return (Get-Content -LiteralPath $p -Raw -Encoding UTF8)
}
# ★ 状态灯那几条断言扫的是**代码**（去掉 `//` 行注释 ✓）：注释里点名"不许写某种颜色 / 不许内联画灯"是好事 ✓
#   ✗ 不该被自己的断言判红 ✗（边界：只处理行注释；被扫的文件里没有块注释、也没有含 `//` 的字符串字面量 ✓）
function CodeOf([string]$txt) {
    if ($null -eq $txt) { return '' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($l in ($txt -split "`r?`n")) {
        $i = $l.IndexOf('//')
        if ($i -ge 0) { $l = $l.Substring(0, $i) }
        [void]$sb.Append($l).Append("`n")
    }
    return $sb.ToString()
}
# 自造色值的判据：`#RRGGBB(AA)` · `Color.FromArgb/FromRgb/…` · 命名颜色常量（`Brushes.` / `Colors.`）
function HasRawColor([string]$code) {
    if ([string]::IsNullOrEmpty($code)) { return $true }   # 拿不到源码 ⇒ 判红 ✗ 不静默放过 ✗
    if ([regex]::IsMatch($code, '#[0-9A-Fa-f]{6}')) { return $true }
    # 只认"手工色值"：Color.FromArgb(60, 0, 0, 0) 这种**数值字面量**通道 ✗
    # （从 token 解析出来的通道，如 Color.FromArgb((byte)c.A, …) 不算 ✗ 那是把 token 变成画刷 ✓）
    if ([regex]::IsMatch($code, 'Color\.From[A-Za-z0-9]*\s*\(\s*[0-9]')) { return $true }
    if ([regex]::IsMatch($code, 'Brushes\.[A-Za-z0-9]+')) { return $true }
    if ([regex]::IsMatch($code, 'Colors\.[A-Za-z0-9]+')) { return $true }
    return $false
}
function CountOf([string]$code, [string]$needle) {
    if ([string]::IsNullOrEmpty($code) -or [string]::IsNullOrEmpty($needle)) { return 0 }
    return ([regex]::Matches($code, [regex]::Escape($needle))).Count
}

$overview = Src 'v3\gui\Dsht.Gui.Avalonia\Shells\Shells.Pages.Overview.cs'
$main = Src 'v3\gui\Dsht.Gui.Avalonia\MainWindow.axaml.cs'
$overlay = Src 'v3\gui\Dsht.Gui.Avalonia\OverlayWindow.cs'
$light = Src 'v3\gui\Dsht.Gui.Avalonia\Markers\OverlayLight.cs'
$hier = Src 'v3\gui\Dsht.Gui.Avalonia\Markers\BoardHierarchy.cs'
$tk = Src 'v3\src\Dsht.Domain\Model\ToolkitConfig.cs'
$sess = Src 'v3\gui\Dsht.Gui.Avalonia\Markers\SessionsMarkers.cs'
$statusLight = Src 'v3\gui\Dsht.Gui.Avalonia\Design\StatusLight.cs'
$paletteSrc = Src 'v3\gui\Dsht.Gui.Avalonia\ViewModels\SessionRowVm.cs'
$menu = Src 'v3\gui\Dsht.Gui.Avalonia\Markers\OverlayMenu.cs'
$prog = Src 'v3\gui\Dsht.Gui.Avalonia\Program.cs'
$ovCode = CodeOf $overlay
$lightCode = CodeOf $light
$statusLightCode = CodeOf $statusLight

Write-Host "== 悬边状态灯接线（源码级，可证伪） =="

# ---- ① 接线统一：看板顶部「置顶进度窗」按钮接回，且只走已持久化的配置键 ----
Check '看板顶部有「置顶进度窗」按钮，且读的是 OverlayEnabledValue（已持久化键的唯一真值）' ($overview -ne $null -and $overview.Contains('host.OverlayEnabledValue') -and $overview.Contains('host.ToggleOverlay()'))
Check '看板 Top 5 的标题开关走的是 OverlayShowTitleValue（与悬浮窗同一个键）' ($overview -ne $null -and $overview.Contains('host.OverlayShowTitleValue') -and $overview.Contains('host.ToggleOverlayShowTitle()'))
Check '★ 看板里**没有**第二份标题开关（纯视图态那一路已不存在：无 BoardTop5ShowTitle / ToggleBoardTop5ShowTitle）' ($overview -ne $null -and -not $overview.Contains('BoardTop5ShowTitle') -and -not $overview.Contains('ToggleBoardTop5ShowTitle'))
Check '★ 纯视图态那个源文件已删除（BoardTop5ViewState.cs 不存在 ⇒ 谁加回来即红）' (-not (Test-Path -LiteralPath (Join-Path $Repo 'v3\gui\Dsht.Gui.Avalonia\BoardTop5ViewState.cs')))
Check '看板 Top 5 与悬浮窗**共用同一句**标题明示（都取唯一出处，不各写一份）' ($overview -ne $null -and $overview.Contains('BoardHierarchy.TitleNotice') -and $overlay -ne $null -and $overlay.Contains('OverlayLight.TitleNotice'))

# ---- ② 标题明示文案：全项目**只有一处字面量**（BoardHierarchy 直接引用 OverlayLight） ----
Check '标题明示文案只有一处字面量（BoardHierarchy.TitleNotice = OverlayLight.TitleNotice，✗ 不重复写整句）' ($hier -ne $null -and $light -ne $null -and $hier.Contains('public const string TitleNotice = OverlayLight.TitleNotice;') -and $hier.IndexOf('"标题可能由你的对话内容派生') -lt 0 -and $light.Contains('"标题可能由你的对话内容派生'))

# ---- ③ 两个开关都默认关（置顶进度窗不默认开 · 标题默认屏蔽） ----
Check '标题开关默认关（TitleSwitchDefaultOn = false ⇒ 改成 true 即红）' ($hier -ne $null -and $hier.Contains('public const bool TitleSwitchDefaultOn = false;'))
Check '置顶进度窗默认关（ToolkitConfig.OverlayEnabled = false）' ($tk -ne $null -and $tk.Contains('public bool OverlayEnabled = false;'))
Check '标题开关默认关（ToolkitConfig.OverlayShowTitle = false）' ($tk -ne $null -and $tk.Contains('public bool OverlayShowTitle = false;'))

# ---- ④ 不抢焦点 / 点击穿透 ----
Check '★ 不抢焦点：ShowActivated = false 与 Focusable = false 都在' ($overlay -ne $null -and $overlay.Contains('ShowActivated = false') -and $overlay.Contains('Focusable = false'))
Check '★ 点击穿透：靠 **Win32 窗口区域**（SetWindowRgn + CreateRectRgn + ApplyRegion ✓ 悬边态裁成灯本身那一块）' ($overlay -ne $null -and $overlay.Contains('SetWindowRgn(') -and $overlay.Contains('CreateRectRgn(') -and $overlay.Contains('ApplyRegion()'))
Check '★ 点击穿透：CreateRectRgn 必须来自 **gdi32.dll**（写成 user32 ⇒ EntryPointNotFoundException ⇒ 区域静默失效，实测踩过 ✗）' ($overlay -ne $null -and $overlay.Contains('DllImport("gdi32.dll")') -and $overlay.Contains('CreateRectRgn'))
Check '★ 点击穿透：**不再**靠根节点 IsHitTestVisible=false（Avalonia 会连子树一起剪掉 ⇒ 灯收不到悬停/点击，实测踩过 ✗）' ($overlay -ne $null -and -not $overlay.Contains('IsHitTestVisible = false,') -and $overlay.Contains('Grid root = new Grid { Background = null };'))
Check '★ 点击穿透：区域只在 Windows 上应用（✗ 非 Windows 整段空转 ✗ 不破坏跨平台可构建 ✓）' ($overlay -ne $null -and $overlay.Contains('OperatingSystem.IsWindows()'))
Check '视觉透明仍保留（画出来的透明 ✓ 但它**不提供**点击穿透 ⇒ 穿透归窗口区域 ✓；透明值本身也来自 token 表 ✓）' ($overlay -ne $null -and $overlay.Contains('Background = Palette.Transparent') -and $overlay.Contains('WindowTransparencyLevel.Transparent'))
Check '置顶小窗的其它硬属性齐全（Topmost / 不在任务栏 / 无边框 / 不可缩放）' ($overlay -ne $null -and $overlay.Contains('Topmost = true') -and $overlay.Contains('ShowInTaskbar = false') -and $overlay.Contains('SystemDecorations = SystemDecorations.None') -and $overlay.Contains('CanResize = false'))

# ---- ⑤ 一色一信号：造灯的唯一入口拒绝"没有来源的灯" ----
Check '★ 一色一信号：Lit() 里"来源为空 ⇒ 强制灰"这一行在（指不到信号来源的灯在代码层亮不起来）' ($light -ne $null -and $light.Contains('if (color == LightColor.Unknown || string.IsNullOrEmpty(source)) return l;'))
Check '★ 一色一信号：五色各有**独立的来源常量**（红①/红②/红③ · 黄 · 蓝 · 绿①/绿②）' ($light -ne $null -and $light.Contains('SrcDshDown') -and $light.Contains('SrcSnapshotStale') -and $light.Contains('SrcPluginMismatch') -and $light.Contains('SrcQuestions') -and $light.Contains('SrcBridgeLive') -and $light.Contains('SrcPlanDone') -and $light.Contains('SrcInferredParked'))
Check '★ 无信号就是灰：没有一处把**空来源**直接喂给 Lit（造灯全部传 Src* 常量）' ($light -ne $null -and -not $light.Contains('Lit(LightColor.Bad, "")') -and -not $light.Contains('Lit(LightColor.Asking, "")') -and -not $light.Contains('Lit(LightColor.Running, "")') -and -not $light.Contains('Lit(LightColor.Done, "")'))

# ---- ⑥ 未知一律「未知」· 绝不 0：计划拿不到 ⇒ 不显示数字 ----
Check '★ 计划数字：拿不到 / 总数为 0 ⇒ 返回「计划未知」（✗ 绝不 0/0）' ($light -ne $null -and $light.Contains('if (r == null || !r.PlanKnown || r.PlanTotal <= 0) return "计划未知";'))
Check '★ 排序 = lastPromptAt 倒序（提案 §11 逐字：会话按先后唤醒倒序）' ($light -ne $null -and $light.Contains('if (a != b) return b.CompareTo(a);'))

# ---- ⑦ 四色灯的信号来源真的接在既有取数上（不新起进程、不加新标记行） ----
Check '红灯②的信号 = CLI 已有的 SESSIONS_SNAPSHOT_STALE 诊断行（不新增标记行）' ($sess -ne $null -and $sess.Contains('SESSIONS_SNAPSHOT_STALE'))
Check '红灯③的信号 = update-center 的 state=content-mismatch（复用既有判定，不重算哈希）' ($main -ne $null -and $main.Contains('state=content-mismatch'))
Check '六个 overlay_* 键在主窗口被读到（与设置页 / CONFIG 打印同一批键）' ($main -ne $null -and $main.Contains('CONFIG overlay_enabled ') -and $main.Contains('CONFIG overlay_edge ') -and $main.Contains('CONFIG overlay_offset ') -and $main.Contains('CONFIG overlay_scale ') -and $main.Contains('CONFIG overlay_screen ') -and $main.Contains('CONFIG overlay_show_title '))

# ---- ⑧ ★ 状态灯接进设计 token 表（2026-10-26）：取色点 / 画法唯一性 / 明暗取值来源 ----
#   为什么这条要在这里钉：GUI 逻辑测试能断言 token 表本身（⑮-3/⑮-4），
#   但"界面文件里真的没有第二处取色 / 第二套画法"只能对源码文本断言 —— 就是这一段 ✓
Check '★ 状态灯画法**只有一处**：悬边灯与展开列表每行灯都调 StatusLight.Create（≥2 处 ✓ ✗ 界面文件里不自己 new Ellipse 画 ✗）' `
    ($overlay -ne $null -and (CountOf $ovCode 'StatusLight.Create(') -ge 2 -and $ovCode.IndexOf('new Ellipse') -lt 0)
Check '★ 悬边灯的两个文件里**零自造色值**（✗ hex ✗ Color.FromArgb/FromRgb ✗ Brushes./Colors. 命名颜色常量 ✗）' `
    ($overlay -ne $null -and $light -ne $null -and -not (HasRawColor $ovCode) -and -not (HasRawColor $lightCode))
Check '★ 第二份配色出口已删除：`Palette.LightBrush`（按灯色直接给一个纯饱和实心色）在取色出口里已不存在（✗ 加回来即红 ✗）' `
    ($paletteSrc -ne $null -and (CodeOf $paletteSrc).IndexOf('LightBrush') -lt 0)
Check '★ 画法文件 `Design/StatusLight.cs` 里也零自造色值（色值只从 token 表取 ✓ 柔光晕 + 去饱和 ✓）' `
    ($statusLight -ne $null -and -not (HasRawColor $statusLightCode) -and $statusLightCode.Contains('DesignTokens.StatusBase') -and $statusLightCode.Contains('DesignTokens.StatusHalo') -and $statusLightCode.Contains('DesignTokens.StatusRing'))
Check '★ 灯的尺寸 / 光晕 / 描边 / 透明也来自 token 表（StatusLight 里 0 个自造数值 ✗ 改回字面量即红 ✓）' `
    ($statusLight -ne $null -and $statusLightCode.Contains('SizeSm = DesignTokens.LightSizeSm;') -and $statusLightCode.Contains('GlowBlur = DesignTokens.LightGlowBlur;') -and $statusLightCode.Contains('Spread = DesignTokens.LightGlowSpread') -and $statusLightCode.Contains('StrokeThickness = DesignTokens.LightRingStroke') -and $statusLightCode.Contains('DesignTokens.TransparentHex'))
Check '★ 灯的几何（厚度 / 长度 / 透明余量 / 圆点）也来自 token 表（✗ 界面文件里不再有 `_scale == 0 ? 8/56/4` 这类自造数值 ✗）' `
    ($overlay -ne $null -and $overlay.Contains('DesignTokens.LampThicknessOf(_scale)') -and $overlay.Contains('DesignTokens.LampLengthOf(_scale)') -and $overlay.Contains('DesignTokens.LampMarginOf(_scale)') -and $overlay.Contains('DesignTokens.LampDotOf(') -and $overlay.Contains('DesignTokens.LampRowDot') -and $ovCode.IndexOf('_scale == 0 ? 8 :') -lt 0 -and $ovCode.IndexOf('_scale == 0 ? 56 :') -lt 0 -and $ovCode.IndexOf('_scale == 0 ? 4 :') -lt 0)
Check '★ 四色灯 → 语义色的映射在**纯判定文件**里（`OverlayLight.SemanticOf` ✓ 可单测 ✓ 界面只消费 ✓）' `
    ($light -ne $null -and $light.Contains('public static DesignTokens.Semantic SemanticOf(LightColor c)') -and $overlay -ne $null -and $overlay.Contains('OverlayLight.SemanticOf('))
Check '★ 一色一语义（口径不变）：红=异常 · 黄=注意 · 蓝=强调 · 绿=正常 · 灰=未知（改坏即红）' `
    ($light -ne $null -and $light.Contains('if (c == LightColor.Bad) return DesignTokens.Semantic.Abnormal;') -and $light.Contains('if (c == LightColor.Asking) return DesignTokens.Semantic.Attention;') -and $light.Contains('if (c == LightColor.Running) return DesignTokens.Semantic.Accent;') -and $light.Contains('if (c == LightColor.Done) return DesignTokens.Semantic.Normal;') -and $light.Contains('return DesignTokens.Semantic.Unknown;'))

Check '★ 色值口径一致：token 的 8 位是 #RRGGBBAA，不许把 token 直接交给 Avalonia 的 Color.Parse（8 位会被当 #AARRGGBB ⇒ 实测灯环变紫 ✗）' `
    ($statusLight -ne $null -and $statusLightCode.IndexOf('Color.Parse(') -lt 0 -and $statusLightCode.Contains('Color.FromArgb(') -and $statusLightCode.Contains('DesignTokens.Parse('))

# ---- ⑨ 右键菜单两档 / 设置入口闭环 / 三款灯样式（2026-10-28）----
Check '★ 右键菜单真的挂在灯与展开面板上（ContextMenu + BuildLampMenu；灯以外仍靠窗口区域穿透）' `
    ($overlay -ne $null -and $overlay.Contains('_lamp.ContextMenu = BuildLampMenu()') -and $overlay.Contains('_panel.ContextMenu = BuildLampMenu()'))
Check '★ 右键三项都接上：本次隐藏 / 永久关闭 / 打开主窗口（动作枚举来自 OverlayMenu 唯一出处）' `
    ($overlay -ne $null -and $overlay.Contains('OverlayMenuAction.SessionHide') -and $overlay.Contains('OverlayMenuAction.PermanentClose') -and $overlay.Contains('OverlayMenuAction.OpenMain') -and $overlay.Contains('_host.HideOverlayForSession()') -and $overlay.Contains('_host.CloseOverlayPermanently()') -and $overlay.Contains('_host.ShowMainFromOverlay()'))
Check '★ 两档文案只在 OverlayMenu 一处定义（界面文件里不重复写「本次隐藏」「永久关闭」「打开主窗口」）' `
    ($menu -ne $null -and $menu.Contains('public const string SessionHideTitle = "本次隐藏";') -and $menu.Contains('public const string PermanentCloseTitle = "永久关闭";') -and $overlay -ne $null -and $overlay.IndexOf('"本次隐藏"') -lt 0 -and $overlay.IndexOf('"永久关闭"') -lt 0 -and $overlay.IndexOf('"打开主窗口"') -lt 0)
$hideBody = ''
if ($main -ne $null -and $main -match 'public void HideOverlayForSession\(\)([\s\S]*?)public void ShowOverlayForSession\(\)') { $hideBody = $Matches[1] }
Check '★ 本次隐藏**不写配置**（方法正文 0 处 config-set）；永久关闭写 `overlay_enabled off` 落盘' `
    ($main -ne $null -and $hideBody.Length -gt 0 -and $hideBody.IndexOf('config-set') -lt 0 -and $main.Contains('public void CloseOverlayPermanently()') -and $main.Contains('config-set overlay_enabled off'))
Check '★ 没有第二套状态键：`overlay_hidden` 不存在；临时隐藏只在内存 `_ovHiddenThisSession`（持久化仍只有 overlay_enabled）' `
    ($main -ne $null -and $main.IndexOf('overlay_hidden') -lt 0 -and $main.Contains('_ovHiddenThisSession'))
Check '★ 设置页 / 看板 / 右键永久关闭写的是**同一个** key：设置页 SetConfig 特判 overlay_enabled + 看板按钮读 OverlayButtonText' `
    ($main -ne $null -and $main.Contains('string.Equals(key, "overlay_enabled"') -and $overview -ne $null -and $overview.Contains('host.OverlayButtonText') -and $overview.Contains('host.OverlayEnabledValue') -and $overview.Contains('host.ToggleOverlay()'))
Check '★ 「本次隐藏」的再显示入口齐全：看板按钮的隐藏态分支 + 托盘菜单「显示悬浮窗」' `
    ($overview -ne $null -and $overview.Contains('host.OverlayHiddenThisSession') -and $overview.Contains('host.ShowOverlayForSession()') -and $main -ne $null -and $main.Contains('OverlayMenu.TrayShowTitle') -and $main.Contains('ShowOverlayFromTray()'))
Check '★ 三款变体只影响画法：启动读一次 DSHT_OVERLAY_LAMP / 减少动效；绘制走 EdgeLampOf；三款画法都在 StatusLight 一处' `
    ($prog -ne $null -and $prog.Contains('ReadLampVariantFromEnvironment') -and $prog.Contains('ReadReducedMotionFromEnvironment') -and $overlay -ne $null -and $overlay.Contains('DesignTokens.EdgeLampOf(') -and $statusLight -ne $null -and $statusLight.Contains('CreateMinimalBar') -and $statusLight.Contains('CreateDotRing') -and $statusLight.Contains('CreateCapsule'))
Check '★ 三款变体的几何/透明度全部来自 token 表（OverlayWindow 里不再有自造灯尺寸；StatusLight 里 0 个 hex）' `
    ($statusLight -ne $null -and -not (HasRawColor $statusLightCode) -and $overlay -ne $null -and $overlay.Contains('DesignTokens.LampThicknessOf(_scale)') -and $overlay.Contains('DesignTokens.LampLengthOf(_scale)') -and $overlay.Contains('DesignTokens.LampMarginOf(_scale)') -and $overlay.Contains('DesignTokens.LampDotOf('))

Write-Host ("== " + $pass + "/" + ($pass + $fail) + " passed, " + $fail + " failed ==")
if ($fail -gt 0) { exit 1 }
exit 0