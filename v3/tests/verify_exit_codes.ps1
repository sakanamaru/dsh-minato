# ============================================================================
#  verify_exit_codes.ps1 —— 退出码契约的端到端验证（P0-1）
#  ---------------------------------------------------------------------------
#  为什么需要它：
#    标记行（*_FAIL）与进程退出码曾经各说各话 —— 打了 FAIL 仍 return 0。
#    只断言内部函数不够：本脚本对每一条改过的分支**真跑一个失败场景**，
#    同时断言 ①退出码 ②标记行 ③（拒绝类）目标没有被改动。
#
#  契约（README 同步写明）：
#    0 = 成功（含计划/只读预览）
#    1 = 执行失败或拒绝（前置条件不满足）
#    2 = 用法错误
#    3 = 启动自完整性拒绝（既有，未改）
#
#  覆盖（每条 <=> 源码里一个 return 分支）：
#    config-set      未知键 -> 2 · 只读配置写不进去 -> 1
#    backup-dir      相对路径 -> 2 · 建不出目录 -> 1 · 存不下设置 -> 1 · 删不掉设置 -> 1
#    restore --dry-run  无有效备份 -> 1 · 无效包目录 -> 1 · 根外路径 -> 1 · 未完成包（自动/显式）-> 1
#    wipe            缺 --yes -> 1 · 带 --yes -> 0（两种都不删除）
#    RunQuiet        狂写 stderr 的子进程不卡死 · 不退出子进程在超时后被结束
#
#  用法：
#    powershell -NoProfile -ExecutionPolicy Bypass -File v3\tests\verify_exit_codes.ps1 -Repo .
#    powershell ... -Cli <dsh-minato.exe 路径>
#  退出码：0 = 全绿；1 = 有失败；2 = 找不到/构建不出 CLI
# ============================================================================
[CmdletBinding()]
param(
    [string]$Repo = ".",
    [string]$Cli = "",
    [switch]$Keep
)

$ErrorActionPreference = "Stop"
function Invoke-External([scriptblock]$sb) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $sb } finally { $ErrorActionPreference = $old }
}

$script:pass = 0
$script:fail = 0
$script:skip = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { $script:pass++; Write-Host ("  [PASS] " + $name) }
    else { $script:fail++; Write-Host ("  [FAIL] " + $name) }
}
function Skip([string]$name, [string]$why) {
    $script:skip++
    Write-Host ("  [SKIP] " + $name + " —— " + $why)
}
function Section([string]$t) { Write-Host ""; Write-Host ("== " + $t) }
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
    $raw = (Invoke-External { & $exe @cmdArgs 2>&1 }) | Out-String
    return @{ rc = $LASTEXITCODE; raw = $raw; out = (Clean $raw) }
}
function HasMarker([string]$clean, [string]$regex) {
    return (@(($clean -split "`n") | Where-Object { $_ -match $regex }).Count -gt 0)
}

# ---- 定位 CLI：优先 -Cli；否则 Release 构建产物（缺了就构建一次）----
$repoFull = (Resolve-Path -LiteralPath $Repo).Path
if ([string]::IsNullOrEmpty($Cli)) {
    $Cli = Join-Path $repoFull 'v3\src\Dsht.Cli\bin\Release\net8.0\dsh-minato.exe'
    if (-not (Test-Path -LiteralPath $Cli)) {
        $dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
        if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
        Write-Host "未找到 Release 构建，先构建 CLI …"
        & $dotnet build (Join-Path $repoFull 'v3\src\Dsht.Cli\Dsht.Cli.csproj') -c Release --nologo 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Cli)) {
            Write-Host ("找不到 CLI：" + $Cli); exit 2
        }
    }
}
if (-not (Test-Path -LiteralPath $Cli)) { Write-Host ("找不到 CLI：" + $Cli); exit 2 }
$Cli = (Resolve-Path -LiteralPath $Cli).Path

$iso = Join-Path $env:TEMP ("dsht_exitcodes_" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$cliDir = Join-Path $iso 'cli'
$homeDir = Join-Path $iso 'home'
$bkDir = Join-Path $iso 'bk'
$bkEmpty = Join-Path $iso 'bk-empty'
New-Item -ItemType Directory -Path $cliDir, $homeDir, $bkDir, $bkEmpty -Force | Out-Null
Copy-Item -Path (Join-Path (Split-Path -Parent $Cli) '*') -Destination $cliDir -Recurse -Force
$exe = Join-Path $cliDir (Split-Path -Leaf $Cli)
Remove-Item -LiteralPath (Join-Path $cliDir '.backup-dir'), (Join-Path $cliDir 'launcher.config') -Force -ErrorAction SilentlyContinue
$cfg = Join-Path $cliDir 'launcher.config'

$oldHome = $env:DSH_HOME
$oldBk = $env:DSH_MINATO_BACKUP_DIR
$env:DSH_HOME = $homeDir
$env:DSH_MINATO_BACKUP_DIR = $bkDir

try {
    Section "0. 隔离事实"
    Write-Host ("  CLI      : " + $exe)
    Write-Host ("  副本目录 : " + $cliDir)
    Write-Host ("  数据根   : " + $homeDir)
    Write-Host ("  备份根   : " + $bkDir)
    Check "状态目录里没有旧的 .backup-dir / launcher.config（用例可重复）" `
        ((-not (Test-Path -LiteralPath (Join-Path $cliDir '.backup-dir'))) -and (-not (Test-Path -LiteralPath $cfg)))

    # ------------------------------------------------------------------
    # 1) config-set
    # ------------------------------------------------------------------
    Section "1. config-set（用法错误 2 · 写入未生效 1）"

    $r = Run-Cli $exe @("config-set", "no_such_key", "x")
    Check "未知键 -> CONFIGSET_FAIL unknown-key" (HasMarker $r.out '^CONFIGSET_FAIL unknown-key')
    Check "未知键 -> 退出码 2（用法错误）" ($r.rc -eq 2)

    $wsA = Join-Path $iso 'ws-a'
    $r = Run-Cli $exe @("config-set", "ws", $wsA)
    Check "前置：合法键写入成功（CONFIGSET_OK ws）" ((HasMarker $r.out '^CONFIGSET_OK ws') -and ($r.rc -eq 0))
    Check "前置：配置文件真的出现" (Test-Path -LiteralPath $cfg)

    # 只读配置：平台的 WriteConfig 会吞掉异常，必须靠回读发现“没写进去”
    if (Test-Path -LiteralPath $cfg) {
        Set-ItemProperty -LiteralPath $cfg -Name IsReadOnly -Value $true
        try {
            $wsB = Join-Path $iso 'ws-b'
            $r = Run-Cli $exe @("config-set", "ws", $wsB)
            Check "只读配置 -> CONFIGSET_FAIL 写入未生效" (HasMarker $r.out '^CONFIGSET_FAIL ')
            Check "只读配置 -> 退出码 1（执行失败）" ($r.rc -eq 1)
            $g = Run-Cli $exe @("config-get")
            Check "只读配置 -> 回读仍是旧值（没有假成功）" (HasMarker $g.out ('^CONFIG ws ' + [regex]::Escape($wsA) + '$'))
        } finally {
            Set-ItemProperty -LiteralPath $cfg -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
        }
    } else {
        Check "只读配置 -> CONFIGSET_FAIL（前置配置文件缺失）" $false
    }

    # ------------------------------------------------------------------
    # 2) backup-dir
    # ------------------------------------------------------------------
    Section "2. backup-dir（相对路径 2 · 建不出/存不下/删不掉 1）"

    $r = Run-Cli $exe @("backup-dir", "--set", "relative\dir")
    Check "相对路径 -> BACKUP_DIR_FAIL" (HasMarker $r.out '^BACKUP_DIR_FAIL ')
    Check "相对路径 -> 退出码 2（用法错误）" ($r.rc -eq 2)

    # 建不出目录：父路径是一个文件
    $blocker = Join-Path $iso 'not-a-dir'
    [System.IO.File]::WriteAllText($blocker, "x")
    $r = Run-Cli $exe @("backup-dir", "--set", (Join-Path $blocker 'sub'))
    Check "父路径是文件 -> BACKUP_DIR_FAIL 无法创建目录" (HasMarker $r.out '^BACKUP_DIR_FAIL .*无法创建目录')
    Check "父路径是文件 -> 退出码 1（执行失败）" ($r.rc -eq 1)

    # 存不下设置：.backup-dir 被一个目录占住（WriteAllText 必失败）
    $sel = Join-Path $cliDir '.backup-dir'
    Remove-Item -LiteralPath $sel -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $sel -Force | Out-Null
    $r = Run-Cli $exe @("backup-dir", "--set", (Join-Path $iso 'bk-new'))
    Check "设置文件是目录 -> BACKUP_DIR_FAIL 无法保存设置" (HasMarker $r.out '^BACKUP_DIR_FAIL .*无法保存设置')
    Check "设置文件是目录 -> 退出码 1" ($r.rc -eq 1)
    Remove-Item -LiteralPath $sel -Force -Recurse -ErrorAction SilentlyContinue

    # 删不掉设置：只读的 .backup-dir
    $r = Run-Cli $exe @("backup-dir", "--set", (Join-Path $iso 'bk-new2'))
    Check "前置：设置成功（BACKUP_DIR_OK）" ((HasMarker $r.out '^BACKUP_DIR_OK ') -and ($r.rc -eq 0))
    if (Test-Path -LiteralPath $sel) {
        Set-ItemProperty -LiteralPath $sel -Name IsReadOnly -Value $true
        try {
            $r = Run-Cli $exe @("backup-dir", "--reset")
            Check "只读设置文件 -> BACKUP_DIR_FAIL 无法恢复默认" (HasMarker $r.out '^BACKUP_DIR_FAIL .*无法恢复默认')
            Check "只读设置文件 -> 退出码 1" ($r.rc -eq 1)
        } finally {
            Set-ItemProperty -LiteralPath $sel -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
        }
    } else {
        Check "只读设置文件 -> BACKUP_DIR_FAIL（前置设置文件缺失）" $false
    }

    # 成功面不回退
    $r = Run-Cli $exe @("backup-dir")
    Check "查看 -> 退出码 0" (($r.rc -eq 0) -and (HasMarker $r.out '^BACKUP_DIR '))
    $r = Run-Cli $exe @("backup-dir", "--reset")
    Check "恢复默认 -> 退出码 0" (($r.rc -eq 0) -and (HasMarker $r.out '^BACKUP_DIR_RESET '))

    # 不可复现的防御分支：如实标注，不假装覆盖
    Skip "设置文件删掉后仍存在（BACKUP_DIR_FAIL 第二种）" `
        "需要 Delete 与 File.Exists 之间的竞态，无法确定性复现"
    Skip "目录创建后仍不存在（BACKUP_DIR_FAIL 第三种）" `
        "本机 CreateDirectory 对不存在的盘符直接抛错（走“无法创建目录”），该分支需要特定平台行为"
    Skip "GetFullPath 抛异常的“路径无效”分支" " .NET 8 在 Windows 上不再对常见非法字符抛异常（实测 8 组）"
    Skip "取不到状态目录（BACKUP_DIR_FAIL 第四种）" `
        "StateDir 由平台层保证非空（exe 目录或 %APPDATA% 回退），该分支是防御性的"

    # ------------------------------------------------------------------
    # 3) restore --dry-run
    # ------------------------------------------------------------------
    Section "3. restore --dry-run（拒绝类全部 1）"

    $env:DSH_MINATO_BACKUP_DIR = $bkEmpty
    $r = Run-Cli $exe @("restore", "--dry-run")
    Check "无有效备份 -> DRYRUN_FAIL 无有效备份" (HasMarker $r.out '^DRYRUN_FAIL .*无有效备份')
    Check "无有效备份 -> 退出码 1" ($r.rc -eq 1)
    Check "无有效备份 -> 没有 DRYRUN_OK（不假成功）" (-not (HasMarker $r.out '^DRYRUN_OK'))
    $env:DSH_MINATO_BACKUP_DIR = $bkDir

    # 无效包目录（在备份根内，有 dsh-data- 名字但没有数据特征）
    $badPkg = Join-Path $bkDir 'dsh-data-20990102-000000-auto'
    New-Item -ItemType Directory -Path $badPkg -Force | Out-Null
    $r = Run-Cli $exe @("restore", "--dry-run", "--path", $badPkg)
    Check "无效包目录 -> DRYRUN_FAIL 无效备份目录" (HasMarker $r.out '^DRYRUN_FAIL .*无效备份目录')
    Check "无效包目录 -> 退出码 1" ($r.rc -eq 1)

    # 根外路径
    $r = Run-Cli $exe @("restore", "--dry-run", "--path", $env:WINDIR)
    Check "备份根外路径 -> DRYRUN_FAIL 不是有效备份包" (HasMarker $r.out '^DRYRUN_FAIL .*不是有效备份包')
    Check "备份根外路径 -> 退出码 1" ($r.rc -eq 1)

    # 未完成包（有数据特征、没有完成标记）——自动分支与显式分支各一条
    $trunc = Join-Path $bkDir 'dsh-data-20990103-000000-auto'
    New-Item -ItemType Directory -Path $trunc -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $trunc 'credentials.yaml'), "x`n")
    $r = Run-Cli $exe @("restore", "--dry-run")
    Check "自动选择到未完成包 -> DRYRUN_FAIL（缺完成标记）" (HasMarker $r.out '^DRYRUN_FAIL .*未完成')
    Check "自动选择到未完成包 -> 退出码 1" ($r.rc -eq 1)
    $r = Run-Cli $exe @("restore", "--dry-run", "--path", $trunc)
    Check "显式指定未完成包 -> DRYRUN_FAIL（缺完成标记）" (HasMarker $r.out '^DRYRUN_FAIL .*未完成')
    Check "显式指定未完成包 -> 退出码 1" ($r.rc -eq 1)

    # 成功面：真做一份完整备份，再预览它
    $dataHome = Join-Path $iso 'data'
    New-Item -ItemType Directory -Path (Join-Path $dataHome 'storages') -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $dataHome 'storages\a.txt'), "ALPHA`n")
    $env:DSH_HOME = $dataHome
    $env:DSH_MINATO_BACKUP_DIR = $bkDir
    $b = Run-Cli $exe @("backup", "--to", $bkDir)
    $pkg = $null
    if (HasMarker $b.out '^BACKUP_OK ') {
        $line = @(($b.out -split "`n") | Where-Object { $_ -match '^BACKUP_OK ' })[0]
        $pkg = $line.Substring('BACKUP_OK '.Length).Trim()
    }
    Check "前置：backup 成功（BACKUP_OK + rc=0）" (($b.rc -eq 0) -and ($pkg -ne $null))
    $r = Run-Cli $exe @("restore", "--dry-run", "--path", $pkg)
    Check "完整包预览 -> DRYRUN_OK" (HasMarker $r.out '^DRYRUN_OK')
    Check "完整包预览 -> 退出码 0（成功面不回退）" ($r.rc -eq 0)

    # ------------------------------------------------------------------
    # 4) wipe
    # ------------------------------------------------------------------
    Section "4. wipe（未确认 1 · 已确认 0 · 两种都不删除）"
    $markerFile = Join-Path $dataHome 'storages\a.txt'
    Check "前置：数据文件在" (Test-Path -LiteralPath $markerFile)

    $r = Run-Cli $exe @("wipe")
    Check "缺 --yes -> WIPE_PLAN / WIPE_MANUAL 仍打印" ((HasMarker $r.out '^WIPE_PLAN ') -and (HasMarker $r.out '^WIPE_MANUAL '))
    Check "缺 --yes -> 退出码 1（拒绝执行清除）" ($r.rc -eq 1)
    Check "缺 --yes -> 数据文件原样还在" (Test-Path -LiteralPath $markerFile)

    $r = Run-Cli $exe @("wipe", "--yes")
    Check "带 --yes -> 退出码 0（只给报告）" (($r.rc -eq 0) -and (HasMarker $r.out '^WIPE_MANUAL '))
    Check "带 --yes -> 仍然一个文件都没删" (Test-Path -LiteralPath $markerFile)
    Skip "WIPE_FAIL 分支（GetFullPath 抛异常）" "DSH_HOME 的异常取值在 .NET 8 全部被平台层兜住，防御分支在本机不可达"

    # ------------------------------------------------------------------
    # 5) RunQuiet：双路并发读 + 超时 kill
    # ------------------------------------------------------------------
    Section "5. RunQuiet（狂写 stderr 不卡死 · 不退出会在超时后结束）"

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Run-Cli $exe @("selftest", "--runquiet", "cmd.exe", "/c", "for /L %i in (1,1,30000) do @echo xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx 1>&2")
    $sw.Stop()
    $secs = [int]$sw.Elapsed.TotalSeconds
    Write-Host ("  -- 狂写 stderr：耗时 " + $secs + "s，rc=" + $r.rc)
    Check "狂写 stderr -> 子进程正常退出（RUNQUIET_RESULT exited）" (HasMarker $r.out '^RUNQUIET_RESULT exited')
    Check "狂写 stderr -> 退出码 0" ($r.rc -eq 0)
    Check "狂写 stderr -> 没有卡死（< 20 秒）" ($secs -lt 20)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Run-Cli $exe @("selftest", "--runquiet", "cmd.exe", "/c", "ping -n 30 127.0.0.1 >nul")
    $sw.Stop()
    $secs = [int]$sw.Elapsed.TotalSeconds
    Write-Host ("  -- 长驻子进程：耗时 " + $secs + "s，rc=" + $r.rc)
    Check "长驻子进程 -> 明确超时（RUNQUIET_TIMEOUT）" (HasMarker $r.out '^RUNQUIET_TIMEOUT ')
    Check "长驻子进程 -> RUNQUIET_RESULT timeout" (HasMarker $r.out '^RUNQUIET_RESULT timeout')
    Check "长驻子进程 -> 探针退出码 1（超时不是成功）" ($r.rc -eq 1)
    Check "长驻子进程 -> 8 秒超时后很快返回（< 20 秒）" ($secs -lt 20)
} finally {
    $env:DSH_HOME = $oldHome
    $env:DSH_MINATO_BACKUP_DIR = $oldBk
    if (Test-Path -LiteralPath $cfg) { Set-ItemProperty -LiteralPath $cfg -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath (Join-Path $cliDir '.backup-dir')) {
        Set-ItemProperty -LiteralPath (Join-Path $cliDir '.backup-dir') -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
    }
    if (-not $Keep) { Remove-Item -LiteralPath $iso -Recurse -Force -ErrorAction SilentlyContinue }
    else { Write-Host ("隔离目录保留: " + $iso) }
}

Write-Host ""
$total = $script:pass + $script:fail
Write-Host ("== 退出码契约：" + $script:pass + "/" + $total + " passed, " + $script:fail + " failed（SKIP " + $script:skip + "）==")
# 项数下限：删掉用例会静默变绿 -> 这里必须红（SKIP 不计入，避免用 SKIP 冲抵）
$EXPECTED_MIN = 28
if (($script:pass + $script:fail) -lt $EXPECTED_MIN) {
    Write-Host ("  [FAIL] 项数不足：实跑 " + ($script:pass + $script:fail) + " < 声明 " + $EXPECTED_MIN)
    $script:fail++
}
if ($script:fail -ne 0) { exit 1 }
exit 0
