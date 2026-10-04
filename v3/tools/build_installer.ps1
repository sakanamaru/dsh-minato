# dsh-minato 安装器构建脚本（Windows ✓）
#
# 为什么用 Roslyn csc 而不是 in-box csc（踩过的坑 ✓）：
#   · in-box 的 `Framework64\v4.0.30319\csc.exe` 只有 **C# 5** ✗ —— 连字符串插值都编不过（CS1056）✓
#   · VS2022 自带的 **Roslyn csc 4.14** 能编现代 C# 且**直接定位 net48** ✓（无需 targeting pack ✓）
#   · net48 是 **Windows 系统组件** ✓ → 不算第三方依赖 ✓✓（与 C 运行时同类 ✓）
#
# 为什么 ZipArchive 要显式 /r:（子代理实测 ✓ 我也复现了 ✓）：
#   · 默认引用集里**没有** System.IO.Compression.FileSystem ✗ → 必须显式 /r: 到 GAC ✓
#
# 用法：
#   pwsh -File v3\tools\build_installer.ps1 -PayloadDir <已打包的目录> -Version 3.0.0 -Out <输出exe>
#   或先 `-PayloadZip <已有zip>` 直接复用 ✓
param(
    [string]$PayloadDir = "",
    [string]$PayloadZip = "",
    [string]$Version = "3.0.2",
    [string]$Out = "",
    [string]$Roslyn = "",
    # 测试用：保留包内现有清单，不重算 ✓（用来验证"清单不全必须拒绝" ✓）
    [switch]$SkipManifest = $false,
    [string]$Repo = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
)
$ErrorActionPreference = "Stop"
$u8 = New-Object System.Text.UTF8Encoding($false)

# ✗ 原来写死本机路径 → **CI 上必然找不到** ✗（GitHub 的 windows runner 路径不同 ✓）
# ✓ 现在四级探测：显式参数 → vswhere → 常见位置 glob → 报错说清怎么办 ✓✓
$roslyn = ""
if ($Roslyn -and (Test-Path $Roslyn)) { $roslyn = $Roslyn }
if (-not $roslyn) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        try {
            $vs = (& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null | Select-Object -First 1)
            if ($vs) {
                $cand = Join-Path $vs.Trim() "MSBuild\Current\Bin\Roslyn\csc.exe"
                if (Test-Path $cand) { $roslyn = $cand }
            }
        } catch { }
    }
}
if (-not $roslyn) {
    foreach ($root in @("${env:ProgramFiles}\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio")) {
        if (-not (Test-Path $root)) { continue }
        $hit = @(Get-ChildItem $root -Recurse -Filter 'csc.exe' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\Roslyn\\' } | Select-Object -First 1)
        if ($hit.Count -gt 0) { $roslyn = $hit[0].FullName; break }
    }
}
if (-not $roslyn -or -not (Test-Path $roslyn)) {
    throw "找不到 Roslyn csc。装了 VS2022 就行；否则用 -Roslyn <csc.exe 的完整路径> 指定。`n（in-box 的 Framework64\v4.0.30319\csc.exe 只有 C# 5，编不过本项目 ✓）"
}
Write-Host "Roslyn: $roslyn"
if (-not (Test-Path $roslyn)) { throw "找不到 Roslyn csc：$roslyn（本机靠 VS2022 ✓ 没装就用不了现代 C# ✗）" }
$ico = Join-Path $Repo "v3\gui\Dsht.Gui.Avalonia\Assets\logo-icon.ico"
$src = Join-Path $Repo "v3\tools\installer.cs"
if (-not (Test-Path $src)) { throw "找不到安装器源码：$src" }

$work = Join-Path $env:TEMP ("dsht-setup-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Path $work -Force | Out-Null

# ---- ① 载荷：优先用现成 zip ✓ 否则从目录打一个 ✓ ----
if ($PayloadZip -eq "") {
    if ($PayloadDir -eq "" -or -not (Test-Path $PayloadDir)) { throw "必须给 -PayloadDir 或 -PayloadZip" }
    $PayloadZip = Join-Path $work "payload.zip"
    Write-Host "打包载荷：$PayloadDir"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # 用 ZipFile 但**统一正斜杠** ✓（解压侧也做了 zip-slip 校验 ✓）
    # ★★ 审计发现 #3 修复：**打包前重算整份 hashes.txt** ✓✓
#   ✗ 原来清单只覆盖 3 个文件（dsh-minato.exe / dsh-minato-gui.exe / gui/dsht-gui.exe）✗
#     → 实测：篡改 gui\Avalonia.Base.dll（不在清单里）→ **exit 0 装上了带木马的 DLL** ✗✗
#   ✓ 现在：**载荷里每个文件都算 SHA-256** ✓ → 覆盖 100% ✓ 篡改任何一个都会被拒 ✓✓
#   （哈希 165MB 约 1 秒 ✓ 值得 ✓）
# ---- ③-0 **先编 GUI 启动器壳** ✓✓（审计 C6 CRITICAL）----
#   ✗✗ 原来**没有任何脚本编译 launcher.cs** ✗ → 载荷里**从来没有 `dsh-minato-gui.exe`** ✓
#      → 而安装器要求它（稳定入口 ✓ 快捷方式目标 ✓ ARP 的 DisplayIcon ✓）
#      → **每次安装都会失败** ✗✗（审计实测 exit 4 ✓ 而且 target 留下非空无 marker ✓ → 重试被拒 ✗）
#   ✓ 现在：**构建时就编出来并放进载荷** ✓ → 清单也会覆盖它 ✓✓
$guiExe = Join-Path $PayloadDir "dsh-minato-gui.exe"
$launcherSrc = Join-Path $Repo "v3\tools\launcher.cs"
if (-not (Test-Path $launcherSrc)) { throw "找不到启动器源码：$launcherSrc" }
Write-Host "编译 GUI 启动器壳（dsh-minato-gui.exe）…"
$argsG = @("/nologo", "/target:winexe", "/platform:anycpu", "/win32icon:$ico", "/out:$guiExe",
           "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll", $launcherSrc)
$rg = & $roslyn @argsG 2>&1
$eg = @($rg | Where-Object { $_ -match "error" })
if ($eg.Count -gt 0) {
    $eg | Select-Object -First 6 | ForEach-Object { Write-Host ("  " + $_.ToString().Trim()) }
    throw "GUI 启动器编译失败"
}
Write-Host ("  启动器 " + [Math]::Round((Get-Item $guiExe).Length / 1KB, 1) + " KB ✓（已放进载荷 ✓）")
# ---- ①a **把可选的桥接插件放进载荷** ✓✓（用户要求：「安装桥接插件有按钮吗」✓）
#   ✗ 原来载荷里**没有插件** ✗（实测：安装目录与桌面载荷的相关文件都是 0 个 ✓）
#     → 用户装了工具箱也**拿不到插件** ✗ → 「运行中」这个功能对用户等于不存在 ✓
#   ★★ **m-1 修复（安装器审计 MINOR）** ✓✓：**位置必须在清单与 zip 之前** ✓✓
#     ✗ 原来这一段在 `CreateFromDirectory` **之后** ✗ → 两个后果：
#        ① `-PayloadDir`：插件是在**清单算完**之后才拷进去的 → **不被指纹清单覆盖** ✗
#           而且 zip **已经打好了** → **插件根本不在 payload.zip 里** ✗✗
#        ② `-PayloadZip`：`$PayloadDir` 是空串 → `Join-Path` **抛异常 → 整个脚本死掉** ✗✗
#           （脚本头部文档说 `-PayloadZip` 是支持的 ✓ → **那条路从来没走通过** ✓）
#     ✓ 现在：**移到清单生成之前** ✓✓ 且**只在有载荷目录时执行** ✓（用 zip 时没什么可拷 ✓）
#     ★ 注意：这里**不能**判断 `$PayloadZip` 是否为空 ✗ —— 上面第 65 行已经把它赋成非空了 ✓
#       → 必须判断 `$PayloadDir` ✓✓
if ($PayloadDir -ne '' -and (Test-Path $PayloadDir)) {
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$pluginSrc = Join-Path $repoRoot 'plugin\dsh-minato-bridge'
if (Test-Path $pluginSrc) {
    $pluginDst = Join-Path $PayloadDir 'plugin\dsh-minato-bridge'
    New-Item -ItemType Directory -Path $pluginDst -Force | Out-Null
    Copy-Item (Join-Path $pluginSrc '*') $pluginDst -Recurse -Force
    $pn = @(Get-ChildItem $pluginDst -Recurse -File).Count
    Write-Host ("  桥接插件已放进载荷 ✓（" + $pn + " 个文件 ✓ 供 bridge-install 命令使用 ✓ 已进指纹清单 ✓）")
} else {
    Write-Host ("  ! 没找到插件源码 " + $pluginSrc + " ✓ 载荷里不会有插件 ✓（bridge-install 会明确说不存在 ✓）")
}
}
if (-not $SkipManifest) {
Write-Host "重算整份清单（覆盖载荷内**所有**文件）…"
$manifest = Join-Path $PayloadDir "hashes.txt"
$lines = New-Object System.Collections.Generic.List[string]
$all = Get-ChildItem $PayloadDir -Recurse -File | Where-Object { $_.Name -ne 'hashes.txt' } | Sort-Object FullName
foreach ($f in $all) {
    # ✗✗ C5（审计 CRITICAL）：原来用 `$PayloadDir` 的**字符串长度**去截 `$f.FullName`（绝对路径）✗
    #   → 传**相对路径**时（**CI 正是相对路径** ✓）算出的 rel 是垃圾 ✓
    #   → 例：`dsh-minato-win-x64`（18 字符）+ `D:\a\...\dsh-minato-win-x64\gui\x.exe`
    #         → rel = `h-minato\dsh-minato-win-x64\gui\x.exe` ✗ → 清单全错 ✓
    #   → 安装器找不到这些文件 → 覆盖数 0 → **拒绝安装** ✗✗
    # ✓ 现在：**先把 PayloadDir 解析成绝对路径** ✓ 再截 ✓
    $payloadFull = (Resolve-Path -LiteralPath $PayloadDir).Path.TrimEnd('\')
    $rel = $f.FullName.Substring($payloadFull.Length).TrimStart('\')
    $h = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower()
    $lines.Add($h + "  " + $rel)
}
[System.IO.File]::WriteAllLines($manifest, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("清单已写 ✓ " + $lines.Count + " 个文件（覆盖 100% ✓）")
} else { Write-Host "跳过清单重算 ✓（-SkipManifest ✓ 用于测试 ✓）" }
[System.IO.Compression.ZipFile]::CreateFromDirectory($PayloadDir, $PayloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host ("  载荷 " + [Math]::Round((Get-Item $PayloadZip).Length / 1MB, 1) + " MB ✓")
}
if (-not (Test-Path $PayloadZip)) { throw "载荷 zip 不存在：$PayloadZip" }

# ---- ①a **桥接插件已在上面放进载荷** ✓✓（m-1 修复：必须早于清单与 zip ✓）
#   原来的位置在这里（zip 之后 ✓）→ 两个问题：
#     ✗ `-PayloadDir` 时插件是在**清单算完之后**才拷进去的 → **不被指纹清单覆盖** ✗
#       而且是在 **zip 打好之后** → **根本进不了 payload.zip** ✗✗
#     ✗ `-PayloadZip` 时 `$PayloadDir` 是空的 → `Join-Path` 抛异常 → **脚本直接死** ✗✗
#   ✓ 现在：块被移到 `if (-not $SkipManifest)` 之前 ✓ 并且只在**有载荷目录**时执行 ✓✓
# ---- ①b **版本号归一化** ✓✓（审计 C4 CRITICAL）----
#   ✗ CI 传 `-Version "${{ github.ref_name }}"` ✓ 而 tag 是 `v3.0.0` ✓
#     → `AssemblyVersion("v3.0.0.0")` → **Roslyn CS7034** ✗ → 构建必然失败 ✓
#     （`workflow_dispatch` 在分支上更糟：传 `"main"` ✓）
#   ✓ 现在：**去前导 v、校验成数字点分** ✓ 不合格就报错说清 ✓
#   （注：这个定义曾经加过又被后续替换冲掉 ✗ → 实测 CS7034 复现 ✓ 现在固定在 AssemblyInfo 之前 ✓）
if (-not (Get-Variable -Name verNum -ErrorAction SilentlyContinue)) {
    $verNum = "$Version".Trim().TrimStart('v', 'V')
}
# ★★★ **发版修复（2026-10-01）—— 认「预发布后缀」，但程序集版本必须纯数字** ✓✓
#   ✗ 原来只认 `^\d+(\.\d+){0,3}$` ✗ → `3.0.0-preview.1` 被**直接拒绝** ✗
#     → 而工作流那边是**静默换成 3.0.0** ✗ → 两边一起把预发布版本坑掉 ✓
#   ✗ 而且 `AssemblyVersion("$verNum.0")` 用带后缀的值会变成
#     `"3.0.0-preview.1.0"` ✗ → **非法程序集版本** ✗（CS7034 一类 ✓）
#   ✓ 现在：**显示版本保留后缀** ✓✓（`3.0.0-preview.1` ✓）
#     · **程序集版本取数字部分并补到 4 段** ✓（`3.0.0-preview.1` → `3.0.0.0` ✓）
#     · 仍然拒绝分支名/乱码 ✓（不匹配就报错说清 ✓ 不静默 ✓）
if ($verNum -notmatch '^\d+(\.\d+){0,3}(-[0-9A-Za-z][0-9A-Za-z.\-]*)?$') {
    throw "版本号必须是数字点分形式（可带预发布后缀，如 3.0.0 或 3.0.0-preview.1）✓ 收到的是「$Version」✗（tag 名要先去 v ✓）"
}
$verCore = ($verNum -split '-')[0]
$verParts = @($verCore -split '\.')
while ($verParts.Count -lt 4) { $verParts += '0' }
$asmVer = ($verParts[0..3] -join '.')
Write-Host "版本号: $Version → 显示 $verNum · 程序集 $asmVer ✓"

# ---- ② AssemblyInfo（csc 没有 /version: ✓ 必须自己给 ✓ 否则 ARP 里显示 0.0.0 ✗）----
$ai = @"
using System.Reflection;
[assembly: AssemblyVersion("$asmVer")]
[assembly: AssemblyFileVersion("$asmVer")]
[assembly: AssemblyTitle("dsh-minato setup")]
[assembly: AssemblyProduct("dsh-minato")]
[assembly: AssemblyCompany("dsh-minato (unofficial)")]
[assembly: AssemblyDescription("dsh-minato self-extracting installer")]
"@
[System.IO.File]::WriteAllText((Join-Path $work "AssemblyInfo.cs"), $ai, $u8)

# ---- ③ 编译 ✓ ----
$gac = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL"
$refs = @(
    "/r:$gac\System.IO.Compression\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.dll",
    "/r:$gac\System.IO.Compression.FileSystem\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.FileSystem.dll"
)
if ($Out -eq "") { $Out = Join-Path $Repo ("dsh-minato-" + $Version + "-win-x64-setup.exe") }
# ---- ③a 先编**不带载荷**的小卸载器 ✓✓（评审要求：卸载器不能带 70MB 载荷 ✗ 实测装出来 302MB ✗）----
$unExe = Join-Path $work "uninstall.exe"
Write-Host "编译卸载器（不带载荷 ✓）…"
$argsU = @("/nologo", "/target:winexe", "/platform:anycpu", "/win32icon:$ico", "/out:$unExe") + $refs + @($src, (Join-Path $work "AssemblyInfo.cs"))
$ru = & $roslyn @argsU 2>&1
$eu = @($ru | Where-Object { $_ -match "error" })   # N-16 FIX: also treat warnings as failures below
if ($eu.Count -gt 0) {
    $eu | Select-Object -First 6 | ForEach-Object { Write-Host ("  " + $_.ToString().Trim()) }
    throw "卸载器编译失败"
}
Write-Host ("  卸载器 " + [Math]::Round((Get-Item $unExe).Length / 1KB, 1) + " KB ✓")

# ---- ③b 再编**带载荷 + 内嵌小卸载器**的安装器 ✓✓ ----
$args = @("/nologo", "/target:winexe", "/platform:anycpu", "/win32icon:$ico", "/out:$Out",
          "/resource:$PayloadZip,payload.zip", "/resource:$unExe,uninstall.exe") + $refs + @($src, (Join-Path $work "AssemblyInfo.cs"))
Write-Host "编译安装器…"
$r = & $roslyn @args 2>&1
$errs = @($r | Where-Object { $_ -match "error" })
$warns = @($r | Where-Object { $_ -match "warning CS" })   # N-16 FIX: a warning means dead or unreachable code shipped silently
if ($errs.Count -gt 0) {
    $errs | Select-Object -First 8 | ForEach-Object { Write-Host ("  " + $_.ToString().Trim()) }
    # N-H FIX (installer final review): csc has already written the output file by the time we get here, so a
    # failed build used to leave a broken executable on disk for a later step to pick up.
    Remove-Item $Out -Force -ErrorAction SilentlyContinue
    throw "编译失败（$($errs.Count) 个错）"
}
if ($warns.Count -gt 0) {
    $warns | Select-Object -First 8 | ForEach-Object { Write-Host ("  WARN " + $_.ToString().Trim()) }
    # N-H FIX: same here - the exe exists, but the build is refused, so it must not survive.
    Remove-Item $Out -Force -ErrorAction SilentlyContinue
    throw "安装器编译有警告（$($warns.Count) 个）✗ 警告往往意味着死代码或不可达代码 ✓ 请修掉再发版 ✓"
}
$fi = Get-Item $Out
Write-Host ("✓ 安装器生成：" + $Out)
Write-Host ("  大小 " + [Math]::Round($fi.Length / 1MB, 1) + " MB（= 载荷 + ~40 KB 壳 ✓）")
Write-Host ""
Write-Host "用法（安装器自身支持）："
Write-Host "  dsh-minato-*-setup.exe                     图形界面安装"
Write-Host "  dsh-minato-*-setup.exe --silent --dir=<路径>  静默安装（Scoop/winget/CI 用 ✓）"
Write-Host "  dsh-minato-*-setup.exe --silent --no-path --no-shortcuts"
Write-Host "  <安装目录>\uninstall.exe --uninstall        卸载（**默认不删数据** ✓）"
Write-Host ""
Write-Host "签名（可选 ✓ 你拿到证书后）："
Write-Host "  signtool sign /fd SHA256 /a <内层 exe>… 然后**最后**签外层安装器 ✓"
Write-Host "  （先签内层再签外层 ✓ 反了会让内层签名失效 ✗ 因为追加载荷会改外层 ✓）"
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
