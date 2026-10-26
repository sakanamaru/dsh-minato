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

$overview = Src 'v3\gui\Dsht.Gui.Avalonia\Shells\Shells.Pages.Overview.cs'
$main = Src 'v3\gui\Dsht.Gui.Avalonia\MainWindow.axaml.cs'
$overlay = Src 'v3\gui\Dsht.Gui.Avalonia\OverlayWindow.cs'
$light = Src 'v3\gui\Dsht.Gui.Avalonia\Markers\OverlayLight.cs'
$hier = Src 'v3\gui\Dsht.Gui.Avalonia\Markers\BoardHierarchy.cs'
$tk = Src 'v3\src\Dsht.Domain\Model\ToolkitConfig.cs'
$sess = Src 'v3\gui\Dsht.Gui.Avalonia\Markers\SessionsMarkers.cs'

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
Check '视觉透明仍保留（画出来的透明 ✓ 但它**不提供**点击穿透 ⇒ 穿透归窗口区域 ✓）' ($overlay -ne $null -and $overlay.Contains('Background = Brushes.Transparent') -and $overlay.Contains('WindowTransparencyLevel.Transparent'))
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

Write-Host ("== " + $pass + "/" + ($pass + $fail) + " passed, " + $fail + " failed ==")
if ($fail -gt 0) { exit 1 }
exit 0
