param(
    [string]$Repo = ''
)
# 视觉规范源码扫描（task-6, 2026-10-30）
#   · 颜色只能来自 Design/DesignTokens.cs；字号只能用 ShapeTokens/DesignTokens 的档位；
#   · 顶级窗口 XAML 只许 11/13/22；用户可见文案不许 ✓/✗、emoji、施工词；
#   · 文字层级用色阶不用 Opacity；圆角只用 token 档；Toast 必须剥离 Markdown；
#   · 页题先于页签；单选项页签不渲染。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File v3/tests/verify_ui_visual.ps1 -Repo <仓库根>
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrEmpty($Repo)) { $Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
$gui    = Join-Path $Repo 'v3\gui\Dsht.Gui.Avalonia'
$shells = Join-Path $gui 'Shells'
$tokens = Join-Path $gui 'Design\DesignTokens.cs'
$mwCs   = Join-Path $gui 'MainWindow.axaml.cs'
$mwAx   = Join-Path $gui 'MainWindow.axaml'
$shellCs = @(Get-ChildItem $shells -Recurse -File -Filter *.cs)
$pass = 0; $fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { $script:pass++; Write-Output ('ok   ' + $name) }
    else {
        $script:fail++
        if ([string]::IsNullOrEmpty($detail)) { Write-Output ('FAIL ' + $name) }
        else { Write-Output ('FAIL ' + $name + ' :: ' + $detail) }
    }
}
function Scan([string[]]$files, [string]$pattern) {
    $hits = @()
    foreach ($f in $files) {
        if ([string]::IsNullOrEmpty($f)) { continue }
        if (-not (Test-Path $f)) { continue }
        foreach ($m in (Select-String -Path $f -Pattern $pattern -AllMatches)) {
            $line = $m.Line.Trim()
            if ($line.Length -gt 130) { $line = $line.Substring(0, 130) }
            $hits += (($m.Path -replace [regex]::Escape($gui + '\'), '') + ':' + $m.LineNumber + ': ' + $line)
        }
    }
    return $hits
}
function Hint([string[]]$hits) {
    if ($hits.Count -eq 0) { return '' }
    return ('(' + $hits.Count + ') ' + (($hits | Select-Object -First 3) -join ' | '))
}
function VisibleText([string[]]$files) {
    $sb = ''
    foreach ($f in $files) {
        if ([string]::IsNullOrEmpty($f)) { continue }
        if (-not (Test-Path $f)) { continue }
        $raw = [IO.File]::ReadAllText($f)
        $sb += [regex]::Replace($raw, '(?m)//[^\r\n]*', '') + [Environment]::NewLine
    }
    return $sb
}
Check 'token 表：Design/DesignTokens.cs 存在' (Test-Path $tokens)
$tokText = [IO.File]::ReadAllText($tokens)
$fontVals = @([regex]::Matches($tokText, 'public const double Font(Sm|Md|Lg) = ([0-9.]+);') | ForEach-Object { [double]$_.Groups[2].Value })
Check '字号档：DesignTokens 恰好 3 档且互不相同' (($fontVals.Count -eq 3) -and (($fontVals | Select-Object -Unique).Count -eq 3)) ($fontVals -join ',')
$spacingBad = @([regex]::Matches($tokText, 'public const double S[0-9]+ = ([0-9]+);') | ForEach-Object { [int]$_.Groups[1].Value } | Where-Object { ($_ % 4) -ne 0 })
Check '间距档：全部是 4 的倍数' ($spacingBad.Count -eq 0) ($spacingBad -join ',')
$colorFiles = @(Get-ChildItem $gui -Recurse -File -Include *.cs, *.axaml | Where-Object { $_.Name -ne 'DesignTokens.cs' } | ForEach-Object { $_.FullName })
$colorHits = Scan $colorFiles '#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\b'
Check '颜色：hex 只允许出现在 Design/DesignTokens.cs' ($colorHits.Count -eq 0) (Hint $colorHits)
$fontFiles = @($shellCs | ForEach-Object { $_.FullName })
$fontFiles += $mwCs
$fontHits = Scan $fontFiles 'FontSize\s*=\s*[0-9]'
Check '字号：Shells/MainWindow 无数字 FontSize（只用 token）' ($fontHits.Count -eq 0) (Hint $fontHits)
$magicT = Scan $fontFiles '\b(T|Mono|LvLine|AddWrapped)\([^,\r\n]*,\s*[0-9]+(\.[0-9]+)?\s*,'
Check '字号：T/Mono/LvLine/AddWrapped 的 size 参数只许 token' ($magicT.Count -eq 0) (Hint $magicT)
$axText = [IO.File]::ReadAllText($mwAx)
$axSizes = @([regex]::Matches($axText, 'FontSize="([0-9.]+)"') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
$axBad = @($axSizes | Where-Object { @('11', '13', '22') -notcontains $_ })
Check 'XAML：顶栏字号只许 11/13/22' ($axBad.Count -eq 0) ($axBad -join ',')
$uiFiles = @($shellCs | ForEach-Object { $_.FullName })
$uiFiles += $mwCs
$uiFiles += $mwAx
$uiText = VisibleText $uiFiles
$checkHits = @([regex]::Matches($uiText, '"[^"\r\n]*(✓|✗)[^"\r\n]*"') | ForEach-Object { $_.Value })
Check '文案：可见字符串里没有 ✓/✗' ($checkHits.Count -eq 0) (($checkHits | Select-Object -First 3) -join ' | ')
$emojiHits = Scan $uiFiles '[\uD800-\uDBFF]'
Check '文案：源码里没有 emoji / 代理对字符' ($emojiHits.Count -eq 0) (Hint $emojiHits)
$jargonHits = @([regex]::Matches($uiText, '"[^"\r\n]*(施工|断言|契约|红线|陷阱|可证伪)[^"\r\n]*"') | ForEach-Object { $_.Value })
Check '文案：可见字符串里没有施工/审查词' ($jargonHits.Count -eq 0) (($jargonHits | Select-Object -First 3) -join ' | ')
$opacityHits = Scan @($shellCs | ForEach-Object { $_.FullName }) '\.Opacity\s*=\s*0\.(7|8|85)\b'
Check '层级：文字不用 Opacity 0.7/0.8/0.85 代替色阶' ($opacityHits.Count -eq 0) (Hint $opacityHits)
$radiusHits = @()
foreach ($f in @($shellCs | ForEach-Object { $_.FullName })) {
    foreach ($m in (Select-String -Path $f -Pattern 'CornerRadius = new CornerRadius\(([0-9]+)' -AllMatches)) {
        $v = [int]$m.Matches[0].Groups[1].Value
        if (@(0, 2, 3, 4, 8, 9, 12, 999) -notcontains $v) { $radiusHits += (($m.Path -replace [regex]::Escape($gui + '\'), '') + ':' + $m.LineNumber + ': ' + $m.Line.Trim()) }
    }
}
Check '圆角：Shells 只用 token 圆角档' ($radiusHits.Count -eq 0) (Hint $radiusHits)
$mwText = [IO.File]::ReadAllText($mwCs)
$shText = [IO.File]::ReadAllText((Join-Path $shells 'Shells.cs'))
Check 'Toast：ShowToast 走 StripMd（不再显示 ** 星号）' ($mwText.Contains('Shells.StripMd(text)'))
Check '页签：单选项不渲染（SubTabs.Length <= 1）' ($shText.Contains('SubTabs.Length <= 1'))
$iTitle = $shText.IndexOf('Control title = PageHeader(host);')
$iSubs  = $shText.IndexOf('SubTabsBar(host, new Thickness(24, 0, 24, 12))')
Check '层级：侧栏式外壳里页题在页签之前' (($iTitle -ge 0) -and ($iSubs -gt $iTitle))
Check '说明页：关于卡有卡片底与 120px 立绘（不再裸文字浮在页面底上）' (($shText.Contains('card.Background = Palette.CardBg;')) -and ($shText.Contains('img.Width = 120')))
# 9 第二轮（task-7）回归断言：页脚/网格/胶囊/设置行/空态/长文案/Toast。
$opText = [IO.File]::ReadAllText((Join-Path $shells 'Shells.Pages.Operations.cs'))
$ovText = [IO.File]::ReadAllText((Join-Path $shells 'Shells.Pages.Overview.cs'))
Check '页脚：不再拼接「未知（CLI 报告的是…）」中间态' (-not $mwText.Contains('未知（CLI 报告的是'))
Check '页脚：不再重复「数据来源：」前缀' (-not $mwText.Contains('"数据来源：" + _data.SourceText'))
Check '状态胶囊：只留一句状态，不再挂重复徽章' (-not $shText.Contains('TextBlock state = T('))
Check 'KPI：概览网格统一为 2 列（数据缺失也保持形状）' ($ovText.Contains('统一 2 列网格') -and $ovText.Contains('row1.Children.Add(new Border { IsHitTestVisible = false })'))
Check '设置：分组卡 + 行分隔 + 控件右对齐（不再 32 张同构卡）' ($opText.Contains('private static Control SettingsItemRow') -and $opText.Contains('SettingsDivider()') -and -not $opText.Contains('SettingsItemCard('))
Check '设置：开关选中态用 accent 柔底（不再整屏实心主按钮）' ($opText.Contains('Background = Palette.AccentSoft,') -and $opText.Contains('BorderBrush = Palette.Accent,'))
Check '空态：体检/日志统一走 EmptyState' ($opText.Contains('EmptyState("还没有体检结果"') -and $opText.Contains('EmptyState("还没有日志"'))
Check '看板：缺块重复句只留一次' ($ovText.Contains('tok.Text != money.Text'))
Check '看板：数据时刻口径缩成一句（≤30 字符）' (-not $ovText.Contains('本机解析出这份输出的时刻；不是数据在 dsh 里产生的时刻'))
Check 'Toast：桌面端提示压缩为可行动短句' ($mwText.Contains('桌面端在运行，未启动 web') -and -not $mwText.Contains('避免两个同时跑；要用 web'))
Write-Output ''
Write-Output ('verify_ui_visual: ' + $pass + ' passed, ' + $fail + ' failed  (repo=' + $Repo + ')')
if ($fail -gt 0) { exit 1 }
exit 0