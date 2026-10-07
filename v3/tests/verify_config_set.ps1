# verify_config_set.ps1 — 端到端验证 `config-set` 的**落盘 + 回读**（隔离状态目录，真跑真写）
#
# 为什么需要它（T2 / D14，2026-10-07）：
#   真机实测（Ubuntu 26.04 · 全新 HOME + 全新 XDG_DATA_HOME · 目录完全可写）：
#     `config-set ws <路径>` → `CONFIGSET_FAIL 写入未生效（配置文件可能只读或被占用）` ✗
#   根因（读码 + 真机对照）：Linux 的 `StateDir`（`$XDG_DATA_HOME/DeepSeekHarnessLauncher`）
#   **只被算出来、从不被创建** ✗ → 第一次 `File.WriteAllText` 抛 `DirectoryNotFoundException`
#   → 被 `catch {}` 吞掉 → 回读拿到 null → 解析成默认配置 → 与请求值不等 → 报"写入未生效"✗（**归因还是错的**）
#   对照实验：手工 `mkdir -p` 同一个目录后再跑同一条命令 → `CONFIGSET_OK ws` ✓
#
# 这个脚本守的是**那条契约**：设完之后**回读必须等于所设值** ✓（现在恒不等的场景要变成红→绿 ✓）。
#
# 隔离设计（与 verify_restore_apply.ps1 同一手法）：
#   V3 的平台装配按**运行平台**选实现（`Path.DirectorySeparatorChar`），所以本脚本在 Windows 上跑的是
#   Windows 实现 —— 那一侧的 `ResolveStateDir()` 自带写探针、**顺带把目录建出来** ✓ 因此它在 Windows 上
#   从来不会复现这个缺陷 ✓（这正是它一路漏到真机的原因 ✓）。
#   本脚本因此断言的是**跨平台的行为契约**（落盘 + 回读 + 清空 + 非法值），
#   而 Linux 侧的**具体缺陷复现/修复证据**在真机上（见 docs/修复记录-2026-10-07-下一批.md）✓
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File v3\tests\verify_config_set.ps1 -Repo .
#   powershell -ExecutionPolicy Bypass -File v3\tests\verify_config_set.ps1 -Cli <dsh-minato(.exe) 路径>
# 退出码：0 = 全绿；1 = 有用例红；2 = 找不到 CLI
[CmdletBinding()]
param(
    [string]$Repo = ".",
    [string]$Cli = "",
    [switch]$Keep
)

$ErrorActionPreference = "Stop"
# 与 verify_restore_apply / verify_command_matrix 同一手法：只在调外部命令期间放宽为 Continue，
# 否则 V3 exe 往 stderr 打的一行正常诊断（INTEGRITY_SKIPPED）会被 PS 5.1 变成终止错误 ✗
function Invoke-External([scriptblock]$sb) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $sb } finally { $ErrorActionPreference = $old }
}
$script:pass = 0
$script:fail = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { $script:pass++; Write-Host ("  [PASS] " + $name) }
    else { $script:fail++; Write-Host ("  [FAIL] " + $name) }
}
function Section([string]$t) { Write-Host ""; Write-Host ("== " + $t) }
# 只留标记行（把 PS 5.1 的原生噪声与正常诊断剔掉 ✓）
function Clean([string]$s) {
    $out = @()
    if ($null -eq $s) { return "" }
    foreach ($l in ($s -split "`r?`n")) {
        if ($l -match '^\s*$') { continue }
        if ($l -match 'INTEGRITY_SKIPPED') { continue }
        if ($l -match 'CategoryInfo|FullyQualifiedErrorId|NativeCommandError') { continue }
        if ($l -match '^\s*\+') { continue }
        if ($l -match '^At ') { continue }
        if ($l -match '^所在位置') { continue }
        $out += $l.TrimEnd()
    }
    return ($out -join "`n")
}
function Run-Cli([string]$exe, [string[]]$cmdArgs) {
    $raw = (Invoke-External { & $exe @cmdArgs }) | Out-String
    return @{ rc = $LASTEXITCODE; raw = $raw; out = (Clean $raw) }
}
function Marker([string]$clean, [string]$prefix) {
    return (@(($clean -split "`n") | Where-Object { $_ -match ('^' + $prefix) }).Count -gt 0)
}

# ---- 定位 CLI ----
$repoFull = (Resolve-Path -LiteralPath $Repo).Path
if ([string]::IsNullOrWhiteSpace($Cli)) {
    $cand = Join-Path $repoFull "v3\src\Dsht.Cli\bin\Release\net8.0\dsh-minato.exe"
    if (-not (Test-Path -LiteralPath $cand)) {
        $cand2 = Join-Path $repoFull "v3\src\Dsht.Cli\bin\Release\net8.0\dsh-minato"   # Linux
        if (Test-Path -LiteralPath $cand2) { $cand = $cand2 }
    }
    if (-not (Test-Path -LiteralPath $cand)) {
        Write-Host ("找不到 Release 产物：" + $cand)
        Write-Host "先跑：dotnet build v3/src/Dsht.Cli/Dsht.Cli.csproj -c Release   （或 -Cli 指定）"
        exit 2
    }
    $Cli = (Resolve-Path -LiteralPath $cand).Path
}
if (-not (Test-Path -LiteralPath $Cli)) { Write-Host ("找不到 CLI：" + $Cli); exit 2 }

$iso = Join-Path $env:TEMP ("dsht_cfgset_" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$exeDir = Join-Path $iso "exe"
$stateDir = Join-Path $iso "state"
$exe = ""          # 副本路径在 try 里定（把 CLI 旁边的文件一起复制）
$wsDir = Join-Path $iso "ws-proj"

# 读文本（不存在就返回空串 —— 断言要能报"没有这个文件"，而不是脚本自己抛异常 ✗）
function Read-Text([string]$p) {
    if (Test-Path -LiteralPath $p) { return [System.IO.File]::ReadAllText($p) }
    return ""
}

$oldHome = $env:DSH_HOME
$oldXDG = $env:XDG_DATA_HOME
# 真实路径的**只读快照**（名字 + 长度 + mtime ✓ 排序后逐字比对 ✓）—— 用来证明"零越界"
function Get-Snap([string]$root) {
    if (-not (Test-Path -LiteralPath $root)) { return "(absent)" }
    $items = Get-ChildItem -LiteralPath $root -Force -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName | ForEach-Object {
        if ($_.PSIsContainer) { "D:" + $_.FullName + ":" + $_.LastWriteTimeUtc.Ticks } else { "F:" + $_.FullName + ":" + $_.Length + ":" + $_.LastWriteTimeUtc.Ticks }
    }
    return ($items -join "|")
}
$realRoots = @()
if ($env:USERPROFILE) { $realRoots += (Join-Path $env:USERPROFILE ".local\share\DeepSeekHarnessLauncher") }
if ($env:APPDATA) { $realRoots += (Join-Path $env:APPDATA "DeepSeekHarnessLauncher") }
# ⚠ **故意不把 `~/.dsh` 放进对照表** ✗：它由**正在跑的 dsh 自己**持续写入 ✓（实测本机 `shio-bridge` /
#   `llm-deepseek` 的 mtime 在几秒内就变了 ✓ 与本工具无关 ✓）→ 把生命周期不归我们的目录当"零越界"判据
#   只会**假红** ✗。`config-set` 本来也不该碰数据根（它只写状态目录 ✓）——真机侧的零越界见真机证据 ✓
$realSnap = @{}
foreach ($r in ($realRoots | Select-Object -Unique)) { $realSnap[$r] = (Get-Snap $r) }
function Re-SnapshotReal() {
    $bad = @()
    foreach ($k in @($realSnap.Keys)) { if ((Get-Snap $k) -ne $realSnap[$k]) { $bad += $k } }
    return $bad
}

try {
    Remove-Item -LiteralPath $iso -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $exeDir -Force | Out-Null
    # ★ 必须把 CLI **旁边的全部文件**一起复制 ✓：框架依赖构建的 apphost 会去找同目录的 `<name>.dll` ✗
    #   （只复制 exe → `The application to execute does not exist: ...dsh-minato.dll` ✗ 实测踩过 ✓）
    foreach ($f in @(Get-ChildItem -LiteralPath (Split-Path $Cli -Parent) -File -ErrorAction SilentlyContinue)) {
        Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $exeDir $f.Name) -Force
    }
    $exe = Join-Path $exeDir (Split-Path $Cli -Leaf)
    # ★ 构建目录里可能**早就躺着**一份 `launcher.config`（上一次跑用例留下的 ✓ 或手工跑过 ✓）
    #   → 它是 Windows 侧的状态文件（StateDir = exe 目录 ✓）→ 不清掉的话"前置：配置文件还不存在"必然红 ✗
    #   （实测踩到过 ✓：这一条红的根因是测试自己的残留，不是产品 ✓）
    Remove-Item -LiteralPath (Join-Path $exeDir "launcher.config") -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $wsDir -Force | Out-Null

    # 隔离状态目录：Linux 侧 StateDir 认 XDG_DATA_HOME ✓；Windows 侧 StateDir = exe 目录（本身就在 $iso 里 ✓）
    $env:XDG_DATA_HOME = $stateDir
    $env:DSH_HOME = (Join-Path $iso "dsh-home")
    New-Item -ItemType Directory -Path $env:DSH_HOME -Force | Out-Null
    $cfgPath = Join-Path $stateDir "DeepSeekHarnessLauncher/launcher.config"
    if ($exe -match '\.exe$') { $cfgPath = Join-Path $exeDir "launcher.config" }   # Windows 侧 StateDir = exe 目录 ✓

    Section "0. 隔离事实"
    Write-Host ("  CLI      : " + $Cli)
    Write-Host ("  副本     : " + $exe)
    Write-Host ("  状态目录 : " + (Split-Path $cfgPath -Parent))
    Write-Host ("  配置文件 : " + $cfgPath)
    Write-Host ("  工作区   : " + $wsDir)
    # ★ "配置文件此刻还不存在" 才是 D14 的触发条件本身 ✓
    #   ⚠ **不**断言"状态目录不存在"：Windows 侧 `ResolveStateDir()` 自带写探针、**顺带把目录建出来** ✓
    #     → 那一句在 Windows 上必然红 ✗ 而它并不是本用例要守的契约 ✗（Linux 侧的真机复现见修复记录 ✓）
    Check "前置：配置文件此刻还不存在（D14 的触发条件 ✓）" (-not (Test-Path -LiteralPath $cfgPath))
    # 零越界基线：**就在跑命令之前**取（这一秒里没有别的写入者干扰 ✓）
    foreach ($r in @($realSnap.Keys)) { $realSnap[$r] = (Get-Snap $r) }

    Section "1. config-set ws <隔离目录> —— 落盘"
    $wsFull = [System.IO.Path]::GetFullPath($wsDir)
    $r1 = Run-Cli $exe @("config-set", "ws", $wsDir)
    Write-Host ("  " + ($r1.out -replace "`n", "`n  "))
    Check "标记行 CONFIGSET_OK ws（修复前这里是 CONFIGSET_FAIL 写入未生效）" (Marker $r1.out 'CONFIGSET_OK ws')
    Check "退出码 0" ($r1.rc -eq 0)
    Check "配置文件真的被创建了" (Test-Path -LiteralPath $cfgPath)
    $text = Read-Text $cfgPath

    $wsLine = ([regex]::Match($text, '(?m)^ws=(.*)$')).Groups[1].Value.Trim()
    Check "文件里的 ws= 行等于所设值（这就是 T2 的契约 ✓）" ($wsLine -eq $wsFull)
    if ($wsLine -ne $wsFull) { Write-Host ("     期望 [" + $wsFull + "] 实际 [" + $wsLine + "]") }

    Section "2. config-get —— 回读一致"
    $r2 = Run-Cli $exe @("config-get")
    $gotLine = ([regex]::Match($r2.out, '(?m)^CONFIG ws (.*)$')).Groups[1].Value.Trim()
    Check "config-get 报 CONFIG ws <所设值>" ($gotLine -eq $wsFull)
    Check "config-get 退出码 0" ($r2.rc -eq 0)
    if ($gotLine -ne $wsFull) { Write-Host ("     期望 [" + $wsFull + "] 实际 [" + $gotLine + "]") }

    Section "3. 非法值仍然被拒（修复没有放宽校验 ✗）"
    $r3 = Run-Cli $exe @("config-set", "no_such_key", "x")
    Check "未知键 → CONFIGSET_FAIL unknown-key（且退出码仍是 0，与命令契约一致）" (Marker $r3.out 'CONFIGSET_FAIL unknown-key')
    $r3b = Run-Cli $exe @("config-set", "ws", "")
    Check "空值 = 清空 → CONFIGSET_OK ws" (Marker $r3b.out 'CONFIGSET_OK ws')
    $text2 = Read-Text $cfgPath
    $wsLine2 = ([regex]::Match($text2, '(?m)^ws=(.*)$')).Groups[1].Value.Trim()
    Check "清空之后 ws= 行变空（能设上也能清掉 ✓）" ($wsLine2 -eq "")

    Section "4. 零越界（真实默认数据根 / 真实状态目录 快照对照）"
    # ⚠ 基线是在跑命令前**重新取**的 ✓：`~/.dsh` 上还跑着本机的 dsh 会话（它自己每秒都在写 ✗）
    #   → 拿"脚本开头"的快照去比会**假红**（实测：`shio-bridge` / `llm-deepseek` 的 mtime 变了 ✓ 与本工具无关 ✓）
    $cmpBad = Re-SnapshotReal
    if ($cmpBad.Count -eq 0) {
        Check "真实路径一个都没变（命令前后快照逐字对照 ✓）" $true
        foreach ($k in @($realSnap.Keys)) { Write-Host ("     " + $k + " -> 未变") }
    } else {
        Check "真实路径一个都没变（命令前后快照逐字对照 ✓）" $false
        foreach ($k in $cmpBad) { Write-Host ("     变了: " + $k) }
    }
}
finally {
    $env:DSH_HOME = $oldHome
    $env:XDG_DATA_HOME = $oldXDG
    if (-not $Keep) { Remove-Item -LiteralPath $iso -Recurse -Force -ErrorAction SilentlyContinue }
    else { Write-Host ("（-Keep：隔离目录保留在 " + $iso + "）") }
}

Write-Host ""
Write-Host ("== 结果：" + $script:pass + "/" + ($script:pass + $script:fail) + " passed, " + $script:fail + " failed ==")
if ($script:fail -gt 0) { exit 1 }
exit 0
